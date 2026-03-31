namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Observability;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Models;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests for <see cref="IOutboxObserver"/>. Derived classes configure a persistence
/// provider with the outbox enabled. Kafka configuration is handled by the base class.
/// The test verifies that the enqueued, processing, and processed callbacks all fire when a
/// message is produced through the outbox and delivered to the consumer.
/// </summary>
[Trait("Category", "Integration")]
public abstract class OutboxObserverCompliance : IAsyncLifetime
{
    /// <summary>
    /// Gets the Kafka bootstrap servers address for producing and consuming messages.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider (MongoDB or EF Core) with outbox enabled.
    /// Kafka configuration is handled by the base class.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="pollingInterval">The outbox daemon polling interval.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit, TimeSpan pollingInterval);

    /// <summary>
    /// Hook for EF Core implementations to call SaveChangesAsync before committing.
    /// MongoDB does not need this. Default implementation does nothing.
    /// </summary>
    protected virtual Task FlushBeforeCommitAsync(IServiceProvider scopedServices)
        => Task.CompletedTask;

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
                    ConfigurePersistence(emit, pollingInterval);
                    ConfigureKafka(emit, topic, groupId);
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

    private async Task ProduceTransactionallyAsync(
        IServiceProvider services,
        string key,
        string value,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();

        await using var transaction = await unitOfWork.BeginAsync(ct);

        await producer.ProduceAsync(new EventMessage<string, string>(key, value), ct);

        await FlushBeforeCommitAsync(sp);
        await transaction.CommitAsync(ct);
    }

    private void ConfigureKafka(EmitBuilder emit, string topic, string groupId)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers);
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
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
