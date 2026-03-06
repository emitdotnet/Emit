namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions.Observability;
using Emit.DependencyInjection;
using Emit.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for <see cref="IOutboxObserver"/>. Derived classes configure a persistence
/// provider with the outbox enabled and a messaging provider for a <c>string, string</c> topic.
/// The test verifies that the enqueued, processing, and processed callbacks all fire when a
/// message is produced through the outbox and delivered to the consumer.
/// </summary>
[Trait("Category", "Integration")]
public abstract class OutboxObserverCompliance : IAsyncLifetime
{
    /// <summary>
    /// Configures the persistence layer (with outbox enabled), the messaging provider with a
    /// <c>string, string</c> topic producer, and registers a consumer group that delivers messages.
    /// The <see cref="TrackingOutboxObserver"/> is registered as a singleton before this is called
    /// and will be resolved by the DI container.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    /// <param name="pollingInterval">The outbox daemon polling interval.</param>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan pollingInterval);

    /// <summary>
    /// Begins a transaction, produces a message to the outbox, and commits.
    /// </summary>
    protected abstract Task ProduceTransactionallyAsync(
        IServiceProvider services,
        string key,
        string value,
        CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Verifies that when an outbox entry is enqueued and successfully processed,
    /// <c>OnEnqueuedAsync</c>, <c>OnProcessingAsync</c>, and <c>OnProcessedAsync</c> all fire.
    /// </summary>
    [Fact]
    public async Task GivenOutboxObserver_WhenEntryEnqueuedAndProcessed_ThenAllCallbacksFire()
    {
        // Arrange
        var topic = $"test-outbox-obs-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var observer = new TrackingOutboxObserver();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TrackingOutboxObserver>(observer);
                services.AddEmit(emit =>
                {
                    emit.Services.AddSingleton<IOutboxObserver>(
                        sp => sp.GetRequiredService<TrackingOutboxObserver>());
                    ConfigureEmit(emit, topic, groupId, pollingInterval);
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a message via the transactional outbox.
            await ProduceTransactionallyAsync(host.Services, "k", "outbox-observer-test");

            // Assert — wait for all three callbacks to fire (enqueued, processing, processed).
            await observer.WaitForProcessedAsync(TimeSpan.FromSeconds(30));

            Assert.True(observer.EnqueuedCount > 0,
                $"Expected OnEnqueuedAsync to be called at least once but count was {observer.EnqueuedCount}.");
            Assert.True(observer.ProcessingCount > 0,
                $"Expected OnProcessingAsync to be called at least once but count was {observer.ProcessingCount}.");
            Assert.True(observer.ProcessedCount > 0,
                $"Expected OnProcessedAsync to be called at least once but count was {observer.ProcessedCount}.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// <see cref="IOutboxObserver"/> implementation that tracks invocation counts for
    /// each observer callback.
    /// </summary>
    public sealed class TrackingOutboxObserver : IOutboxObserver
    {
        private int enqueuedCount;
        private int processingCount;
        private int processedCount;
        private int errorCount;
        private readonly SemaphoreSlim processedSignal = new(0);

        /// <summary>Gets the number of times <see cref="IOutboxObserver.OnEnqueuedAsync"/> was called.</summary>
        public int EnqueuedCount => Volatile.Read(ref enqueuedCount);

        /// <summary>Gets the number of times <see cref="IOutboxObserver.OnProcessingAsync"/> was called.</summary>
        public int ProcessingCount => Volatile.Read(ref processingCount);

        /// <summary>Gets the number of times <see cref="IOutboxObserver.OnProcessedAsync"/> was called.</summary>
        public int ProcessedCount => Volatile.Read(ref processedCount);

        /// <summary>Gets the number of times <see cref="IOutboxObserver.OnProcessErrorAsync"/> was called.</summary>
        public int ErrorCount => Volatile.Read(ref errorCount);

        /// <inheritdoc />
        public Task OnEnqueuedAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref enqueuedCount);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnProcessingAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref processingCount);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnProcessedAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref processedCount);
            processedSignal.Release();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnProcessErrorAsync(OutboxEntry entry, Exception exception, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref errorCount);
            return Task.CompletedTask;
        }

        /// <summary>Waits until at least one processed callback fires.</summary>
        public async Task WaitForProcessedAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await processedSignal.WaitAsync(cts.Token);
        }
    }
}
