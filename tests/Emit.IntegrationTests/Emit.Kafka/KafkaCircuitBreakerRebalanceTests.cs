namespace Emit.Kafka.Tests;

using Emit.Abstractions;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka-specific tests verifying that the circuit breaker pause duration controls how long
/// the consumer partitions remain paused after the circuit trips.
/// </summary>
[Trait("Category", "Integration")]
public class KafkaCircuitBreakerRebalanceTests(KafkaContainerFixture fixture)
    : IClassFixture<KafkaContainerFixture>
{
    /// <summary>
    /// Verifies that a shorter pause duration causes the consumer to resume sooner than a longer one.
    /// Produces messages that trip the circuit, then confirms the probe message arrives within
    /// a time bound consistent with the configured short pause duration.
    /// </summary>
    [Fact]
    public async Task GivenShortPauseDuration_WhenCircuitTrips_ThenConsumerResumesWithinPauseBound()
    {
        // Arrange — threshold=2, pauseDuration=3s.
        const int threshold = 2;
        var pauseDuration = TimeSpan.FromSeconds(3);

        var topic = $"test-cb-pause-short-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var toggle = new ConsumerToggle { FailureThreshold = threshold };

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(toggle);
                services.AddEmit(emit =>
                {
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config =>
                        {
                            config.BootstrapServers = fixture.BootstrapServers;
                        });
                        kafka.AutoProvision();

                        kafka.Topic<string, string>(topic, t =>
                        {
                            t.UseUtf8Serialization();

                            t.Producer();
                            t.ConsumerGroup($"group-pause-short-{Guid.NewGuid():N}", group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.CircuitBreaker(cb =>
                                {
                                    cb.FailureThreshold(threshold)
                                      .SamplingWindow(TimeSpan.FromSeconds(30))
                                      .PauseDuration(pauseDuration)
                                      .TripOn<InvalidOperationException>();
                                });
                                group.AddConsumer<ToggleableConsumer>();
                            });
                        });
                    });
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — trip the circuit by producing threshold-many failing messages.
            toggle.ShouldThrow = true;
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "trip-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "trip-2"));

            // Wait until the consumer has actually recorded both failures, then give
            // OpenCircuitAsync (PauseAsync) a moment to complete before flipping the toggle.
            await toggle.WaitForFailureThresholdAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            // Toggle back to success so the probe succeeds and closes the circuit.
            toggle.ShouldThrow = false;

            // Produce the probe. Record when it was sent.
            var sentAt = DateTimeOffset.UtcNow;
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "probe-after-trip"));

            // Assert — probe must arrive within pauseDuration + generous buffer (10s).
            // This confirms the consumer resumed after the configured short pause, not an arbitrarily long one.
            var deadline = TimeSpan.FromSeconds(pauseDuration.TotalSeconds + 10);

            ConsumeContext<string> ctx;
            do
            {
                ctx = await sink.WaitForMessageAsync(deadline);
            }
            while (ctx.Message != "probe-after-trip");

            var elapsed = DateTimeOffset.UtcNow - sentAt;
            Assert.True(
                elapsed.TotalSeconds < pauseDuration.TotalSeconds + 10,
                $"Expected probe to arrive within {pauseDuration.TotalSeconds + 10}s of sending, " +
                $"but it took {elapsed.TotalSeconds:F1}s. Circuit pause may be too long or consumer did not resume.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a longer pause duration keeps the consumer paused longer, so a probe message
    /// produced immediately after the circuit trips does not arrive before the pause expires.
    /// </summary>
    [Fact]
    public async Task GivenLongPauseDuration_WhenCircuitTrips_ThenProbeBlockedUntilPauseExpires()
    {
        // Arrange — threshold=2, pauseDuration=10s.
        const int threshold = 2;
        var pauseDuration = TimeSpan.FromSeconds(10);
        // Observation window: confirm message does not arrive in first observationWindow seconds.
        var observationWindow = TimeSpan.FromSeconds(5);

        var topic = $"test-cb-pause-long-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var toggle = new ConsumerToggle { FailureThreshold = threshold };

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(toggle);
                services.AddEmit(emit =>
                {
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config =>
                        {
                            config.BootstrapServers = fixture.BootstrapServers;
                        });
                        kafka.AutoProvision();

                        kafka.Topic<string, string>(topic, t =>
                        {
                            t.UseUtf8Serialization();

                            t.Producer();
                            t.ConsumerGroup($"group-pause-long-{Guid.NewGuid():N}", group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.CircuitBreaker(cb =>
                                {
                                    cb.FailureThreshold(threshold)
                                      .SamplingWindow(TimeSpan.FromSeconds(30))
                                      .PauseDuration(pauseDuration)
                                      .TripOn<InvalidOperationException>();
                                });
                                group.AddConsumer<ToggleableConsumer>();
                            });
                        });
                    });
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — trip the circuit.
            toggle.ShouldThrow = true;
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "trip-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "trip-2"));

            // Wait until the consumer has actually recorded both failures, then give
            // OpenCircuitAsync (PauseAsync) a moment to complete before flipping the toggle.
            await toggle.WaitForFailureThresholdAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            // Toggle to success and produce the probe immediately after the circuit opens.
            toggle.ShouldThrow = false;
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "blocked-probe"));

            // Assert — the probe must NOT arrive within the observationWindow (circuit is still open).
            await Task.Delay(observationWindow);
            var arrived = sink.ReceivedMessages.Any(m => m.Message == "blocked-probe");
            Assert.False(arrived,
                $"Probe arrived within {observationWindow.TotalSeconds}s, but the circuit pause is {pauseDuration.TotalSeconds}s. " +
                "The consumer should still be paused.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Consumer that either throws or writes to a <see cref="MessageSink{TMessage}"/>
    /// based on <see cref="ConsumerToggle.ShouldThrow"/>.
    /// </summary>
    public sealed class ToggleableConsumer(MessageSink<string> sink, ConsumerToggle toggle)
        : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            if (toggle.ShouldThrow)
            {
                toggle.RecordFailure();
                throw new InvalidOperationException("Simulated failure for circuit breaker pause test.");
            }

            return sink.WriteAsync(context, cancellationToken);
        }
    }

    /// <summary>
    /// Toggle that controls whether <see cref="ToggleableConsumer"/> throws.
    /// Tracks failure count and signals when a threshold is reached so tests can
    /// synchronize on the circuit opening rather than relying on fixed delays.
    /// </summary>
    public sealed class ConsumerToggle
    {
        private int failureCount;
        private readonly TaskCompletionSource failureThresholdReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// When <see langword="true"/>, the consumer throws <see cref="InvalidOperationException"/>.
        /// </summary>
        public volatile bool ShouldThrow;

        /// <summary>
        /// Number of failures that must occur before <see cref="WaitForFailureThresholdAsync"/> returns.
        /// </summary>
        public int FailureThreshold { get; set; } = int.MaxValue;

        /// <summary>
        /// Called by the consumer each time it throws. Signals when <see cref="FailureThreshold"/> is reached.
        /// </summary>
        public void RecordFailure()
        {
            if (Interlocked.Increment(ref failureCount) >= FailureThreshold)
            {
                failureThresholdReached.TrySetResult();
            }
        }

        /// <summary>
        /// Waits until the consumer has recorded at least <see cref="FailureThreshold"/> failures.
        /// </summary>
        public Task WaitForFailureThresholdAsync(TimeSpan timeout) =>
            failureThresholdReached.Task.WaitAsync(timeout);
    }
}
