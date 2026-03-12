namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Observability;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for <see cref="IConsumeObserver"/>. Derived classes configure a
/// <c>string, string</c> topic with a registered observer. Two scenarios are covered:
/// a successful consume (start and complete callbacks fire) and a consumer that throws
/// (error callback fires).
/// </summary>
[Trait("Category", "Integration")]
public abstract class ConsumeObserverCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// The <see cref="TrackingConsumeObserver"/> must be registered as an <see cref="IConsumeObserver"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithObserverAndSucceedingConsumer(
        EmitBuilder emit,
        string topic,
        string groupId);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing <see cref="AlwaysFailingConsumer"/>. The consumer group
    /// must have a Discard error policy so the pipeline does not retry indefinitely.
    /// The <see cref="TrackingConsumeObserver"/> must be registered as an <see cref="IConsumeObserver"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithObserverAndFailingConsumer(
        EmitBuilder emit,
        string topic,
        string groupId);

    /// <summary>
    /// Verifies that when a consumer succeeds, the observer's consuming and consumed callbacks
    /// are both invoked exactly once.
    /// </summary>
    [Fact]
    public async Task GivenConsumeObserver_WhenMessageSucceeds_ThenStartAndCompleteCallbacksFire()
    {
        // Arrange
        var topic = $"test-obs-success-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var observer = new TrackingConsumeObserver();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton<TrackingConsumeObserver>(observer);
                services.AddEmit(emit =>
                    ConfigureWithObserverAndSucceedingConsumer(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "hello"));

            // Assert — wait for the consumed callback (fires after OnConsumedAsync, which is
            // after the consumer returns — slightly later than the sink receiving the message).
            await observer.WaitForConsumedAsync(TimeSpan.FromSeconds(30));

            Assert.True(observer.ConsumingCount > 0,
                $"Expected OnConsumingAsync to be called at least once but count was {observer.ConsumingCount}.");
            Assert.True(observer.ConsumedCount > 0,
                $"Expected OnConsumedAsync to be called at least once but count was {observer.ConsumedCount}.");
            Assert.Equal(0, observer.ErrorCount);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that when a consumer throws, the observer's error callback is invoked.
    /// </summary>
    [Fact]
    public async Task GivenConsumeObserver_WhenConsumerThrows_ThenErrorCallbackFires()
    {
        // Arrange
        var topic = $"test-obs-error-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var observer = new TrackingConsumeObserver();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TrackingConsumeObserver>(observer);
                services.AddEmit(emit =>
                    ConfigureWithObserverAndFailingConsumer(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "fail-me"));

            // Assert — wait for the error callback to fire.
            await observer.WaitForErrorAsync(TimeSpan.FromSeconds(30));
            Assert.True(observer.ErrorCount > 0,
                $"Expected OnConsumeErrorAsync to be called at least once but count was {observer.ErrorCount}.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// <see cref="IConsumeObserver"/> implementation that tracks invocation counts for
    /// each observer callback.
    /// </summary>
    public sealed class TrackingConsumeObserver : IConsumeObserver
    {
        private int consumingCount;
        private int consumedCount;
        private int errorCount;
        private readonly SemaphoreSlim consumedSignal = new(0);
        private readonly SemaphoreSlim errorSignal = new(0);

        /// <summary>Gets the number of times <see cref="IConsumeObserver.OnConsumingAsync{T}"/> was called.</summary>
        public int ConsumingCount => Volatile.Read(ref consumingCount);

        /// <summary>Gets the number of times <see cref="IConsumeObserver.OnConsumedAsync{T}"/> was called.</summary>
        public int ConsumedCount => Volatile.Read(ref consumedCount);

        /// <summary>Gets the number of times <see cref="IConsumeObserver.OnConsumeErrorAsync{T}"/> was called.</summary>
        public int ErrorCount => Volatile.Read(ref errorCount);

        /// <inheritdoc />
        public Task OnConsumingAsync<T>(ConsumeContext<T> context)
        {
            Interlocked.Increment(ref consumingCount);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnConsumedAsync<T>(ConsumeContext<T> context)
        {
            Interlocked.Increment(ref consumedCount);
            consumedSignal.Release();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnConsumeErrorAsync<T>(ConsumeContext<T> context, Exception exception)
        {
            Interlocked.Increment(ref errorCount);
            errorSignal.Release();
            return Task.CompletedTask;
        }

        /// <summary>Waits until at least one consumed callback fires.</summary>
        public async Task WaitForConsumedAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await consumedSignal.WaitAsync(cts.Token);
        }

        /// <summary>Waits until at least one error callback fires.</summary>
        public async Task WaitForErrorAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await errorSignal.WaitAsync(cts.Token);
        }
    }

    /// <summary>
    /// Consumer that always throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public sealed class AlwaysFailingConsumer : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated consumer failure for observer test.");
    }
}
