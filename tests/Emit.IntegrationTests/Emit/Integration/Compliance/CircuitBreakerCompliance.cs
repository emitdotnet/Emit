namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for circuit breaker behavior. Derived classes configure a provider-specific
/// producer and consumer group for a <c>string, string</c> topic. The consumer group must include
/// <see cref="ToggleableConsumer"/> and apply the given circuit breaker configuration.
/// </summary>
[Trait("Category", "Integration")]
public abstract class CircuitBreakerCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing <see cref="ToggleableConsumer"/>. The consumer group must
    /// apply the given <paramref name="configureCircuitBreaker"/> callback.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="configureCircuitBreaker">Circuit breaker configuration to apply to the consumer group.</param>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        Action<CircuitBreakerBuilder> configureCircuitBreaker);

    [Fact]
    public async Task GivenCircuitBreaker_WhenFailureTripsBreaker_ThenPausesAndResumesAfterRecovery()
    {
        // Arrange
        var topic = $"test-cb-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var toggle = new ConsumerToggle();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(toggle);
                services.AddEmit(emit =>
                {
                    ConfigureEmit(emit, topic, cb =>
                    {
                        cb.FailureThreshold(2)
                          .SamplingWindow(TimeSpan.FromSeconds(30))
                          .PauseDuration(TimeSpan.FromSeconds(5))
                          .TripOn<InvalidOperationException>();
                    });
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — Step 1: Produce a message that succeeds (circuit is closed)
            await producer.ProduceAsync(new EventMessage<string, string>("key-1", "success-1"));
            var first = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("success-1", first.Message);

            // Act — Step 2: Toggle consumer to throw, produce messages to trip the circuit breaker
            toggle.ShouldThrow = true;
            await producer.ProduceAsync(new EventMessage<string, string>("key-2", "fail-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("key-3", "fail-2"));

            // Wait for the circuit to trip (failures are processed, circuit opens, consumer pauses)
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act — Step 3: Toggle back to success before the circuit recovers
            toggle.ShouldThrow = false;

            // Act — Step 4: Produce the target message while the circuit is open (partitions paused)
            await producer.ProduceAsync(new EventMessage<string, string>("key-4", "target"));

            // Assert — The target message should arrive after the circuit recovers.
            // PauseDuration is 5s, so after ~5s the circuit goes half-open, resumes,
            // and lets a probe message through. If the probe succeeds, the circuit closes.
            // Previously failed messages may be redelivered first (if offsets weren't committed),
            // so drain until we find "target".
            InboundContext<string> context;
            do
            {
                context = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            }
            while (context.Message != "target");

            Assert.Equal("target", context.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that when the probe message in half-open state fails, the circuit re-opens,
    /// and that after another pause duration a successful probe closes the circuit.
    /// </summary>
    [Fact]
    public async Task GivenHalfOpenProbe_WhenProbeFails_ThenCircuitReopens()
    {
        // Arrange — threshold=2, pauseDuration=3s so test completes in reasonable time.
        const int threshold = 2;
        var pauseDuration = TimeSpan.FromSeconds(3);

        var topic = $"test-cb-halfopen-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var toggle = new ConsumerToggle();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(toggle);
                services.AddEmit(emit =>
                {
                    ConfigureEmit(emit, topic, cb =>
                    {
                        cb.FailureThreshold(threshold)
                          .SamplingWindow(TimeSpan.FromSeconds(30))
                          .PauseDuration(pauseDuration)
                          .TripOn<InvalidOperationException>();
                    });
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Step 1: Trip the circuit by producing threshold-many failing messages.
            toggle.ShouldThrow = true;
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "trip-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "trip-2"));

            // Wait for failures to be processed and circuit to open.
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Step 2: Wait past pauseDuration → circuit goes half-open, probe is allowed through.
            // Keep toggle=true so the probe fails and the circuit re-opens.
            await Task.Delay(pauseDuration + TimeSpan.FromSeconds(1));

            // Step 3: Now toggle to success and wait for the second half-open probe to succeed.
            toggle.ShouldThrow = false;

            // Wait for another pauseDuration so the re-opened circuit goes half-open again.
            await Task.Delay(pauseDuration + TimeSpan.FromSeconds(2));

            // Step 4: Produce a recovery message; circuit should be closed now.
            await producer.ProduceAsync(new EventMessage<string, string>("k-final", "recovered"));

            // Assert — after recovery, messages flow through.
            InboundContext<string> ctx;
            do
            {
                ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            }
            while (ctx.Message != "recovered");

            Assert.Equal("recovered", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that failures that have aged out of the sampling window are discarded,
    /// so the circuit does not trip from stale failures alone.
    /// </summary>
    [Fact]
    public async Task GivenFailuresExpireOutOfWindow_WhenMoreFailuresAdded_ThenCircuitDoesNotTrip()
    {
        // Arrange — threshold=2, samplingWindow=3s, pauseDuration=30s (will not recover during test).
        const int threshold = 2;
        var samplingWindow = TimeSpan.FromSeconds(3);
        var pauseDuration = TimeSpan.FromSeconds(30);

        var topic = $"test-cb-window-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var toggle = new ConsumerToggle();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(toggle);
                services.AddEmit(emit =>
                {
                    ConfigureEmit(emit, topic, cb =>
                    {
                        cb.FailureThreshold(threshold)
                          .SamplingWindow(samplingWindow)
                          .PauseDuration(pauseDuration)
                          .TripOn<InvalidOperationException>();
                    });
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — Step 1: Produce 1 failure; count in window = 1 (below threshold=2, circuit stays closed).
            toggle.ShouldThrow = true;
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "fail-old"));

            // Wait for the first failure to be processed and then expire out of the sliding window.
            await Task.Delay(samplingWindow + TimeSpan.FromSeconds(1));

            // Act — Step 2: Produce a second failure. This is the first in the current window.
            // The old failure has aged out, so count=1, still below threshold=2. Circuit stays closed.
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "fail-new-1"));

            // Wait briefly for processing (circuit must remain closed).
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Assert — circuit is still closed: produce a success message that should arrive.
            toggle.ShouldThrow = false;
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "probe-after-one-failure"));

            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            // Drain until we find the probe (earlier fail messages may have been retried successfully too).
            while (ctx.Message != "probe-after-one-failure")
            {
                ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            }

            Assert.Equal("probe-after-one-failure", ctx.Message);

            // Act — Step 3: Produce two more failures in quick succession.
            // Now window has 2 failures → threshold reached → circuit trips.
            toggle.ShouldThrow = true;
            await producer.ProduceAsync(new EventMessage<string, string>("k4", "fail-trip-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k5", "fail-trip-2"));

            // Wait for the circuit to trip and consumer to pause.
            await Task.Delay(TimeSpan.FromSeconds(3));

            // Assert — circuit is now open: produce a message that must NOT arrive within a short window.
            toggle.ShouldThrow = false;
            await producer.ProduceAsync(new EventMessage<string, string>("k6", "blocked-while-open"));

            // The PauseDuration is 30s so blocked-while-open should not arrive quickly.
            // We verify by waiting 5s and asserting the sink didn't receive it yet.
            await Task.Delay(TimeSpan.FromSeconds(5));
            var messagesAfterTrip = sink.ReceivedMessages
                .Skip(1) // skip the probe already asserted above
                .ToList();
            Assert.DoesNotContain(messagesAfterTrip, m => m.Message == "blocked-while-open");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Toggle that controls whether <see cref="ToggleableConsumer"/> throws.
    /// </summary>
    public sealed class ConsumerToggle
    {
        /// <summary>
        /// When <see langword="true"/>, the consumer throws <see cref="InvalidOperationException"/>.
        /// </summary>
        public volatile bool ShouldThrow;
    }

    /// <summary>
    /// Consumer that either throws or writes to a <see cref="MessageSink{TMessage}"/>
    /// based on <see cref="ConsumerToggle.ShouldThrow"/>.
    /// </summary>
    public sealed class ToggleableConsumer(MessageSink<string> sink, ConsumerToggle toggle)
        : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
        {
            if (toggle.ShouldThrow)
            {
                throw new InvalidOperationException("Simulated failure for circuit breaker test");
            }

            return sink.WriteAsync(context, cancellationToken);
        }
    }
}
