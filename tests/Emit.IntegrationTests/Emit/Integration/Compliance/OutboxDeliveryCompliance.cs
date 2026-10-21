namespace Emit.IntegrationTests.Integration.Compliance;

using System.Collections.Concurrent;
using Emit.Abstractions;
using Emit.Abstractions.Observability;
using Emit.Configuration;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Models;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static Emit.IntegrationTests.Integration.TestHelpers;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests for the transactional outbox: every entry is delivered, in order within each group, also when
/// the outbox daemon moves to another node of the service, and services sharing one database each deliver only the
/// entries they enqueued. Derived classes configure a persistence provider (with outbox enabled); every
/// host built by a test points at the same database. Kafka configuration is handled by the base class.
/// </summary>
[Trait("Category", "Integration")]
public abstract class OutboxDeliveryCompliance : IAsyncLifetime
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// A batch smaller than what the tests produce, so the outbox drains over several batches.
    /// </summary>
    private const int BatchSize = 2;

    /// <summary>
    /// Gets the Kafka bootstrap servers address for producing and consuming messages.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider (MongoDB or EF Core) with outbox enabled. Every call must target
    /// the same database.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="configureOutbox">Configures the outbox the provider enables.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit, Action<OutboxOptions> configureOutbox);

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

    [Fact]
    public async Task GivenNodeShutDownWhileGroupsAreHalfDelivered_WhenAnotherNodeOfTheServiceTakesTheOutboxOver_ThenItDeliversTheRestOfEachGroupInOrder()
    {
        // Arrange: three groups whose entries interleave. The first node is held after delivering part of the second
        // wave and shut down there, so every group has entries left for the second node.
        var serviceName = NewServiceName();
        var sink = new MessageSink<string>();
        string[] keys = ["alpha", "gamma", "beta", "beta", "alpha", "gamma", "beta", "alpha"];
        var holdsTheFirstNode = new HoldingObserver(holdAfter: keys.Length + 1);
        using var first = BuildHost(serviceName, sink, holdsTheFirstNode);
        var secondProcessed = new ProcessedEntryRecorder();
        using var second = BuildHost(serviceName, sink, secondProcessed);
        var produced = new Dictionary<string, int>(StringComparer.Ordinal);
        async Task ProduceInterleavedAsync(IHost host)
        {
            foreach (var key in keys)
            {
                var index = produced.GetValueOrDefault(key);
                produced[key] = index + 1;
                await ProduceAsync(host.Services, key, $"{key}-{index}");
            }
        }

        await ProduceInterleavedAsync(first);
        await first.StartAsync();

        try
        {
            var received = await ReceiveAsync(sink, keys.Length);
            await second.StartAsync();
            await ProduceInterleavedAsync(first);

            // Under load the first node can lose its lease before it reaches the hold, and the daemon moves early
            await Task.WhenAny(holdsTheFirstNode.Held, secondProcessed.FirstProcessed).WaitAsync(TimeSpan.FromSeconds(60));

            // Act: the first node is stopped while held, so its outbox daemon is cancelled before it could take another
            // batch; the run it keeps going until released leaves the rest to the second node
            await first.StopAsync();
            holdsTheFirstNode.Release();
            received.AddRange(await ReceiveAsync(sink, keys.Length, alreadyReceived: received));

            // Assert: the second node took the outbox over, and within each group messages arrived in the order
            // they were produced
            Assert.True(secondProcessed.FirstProcessed.IsCompleted);
            Assert.All(produced, group => Assert.Equal(
                Enumerable.Range(0, group.Value).Select(i => $"{group.Key}-{i}"),
                received.Where(m => m.StartsWith(group.Key + "-", StringComparison.Ordinal))));
        }
        finally
        {
            holdsTheFirstNode.Release();
            await first.StopAsync();
            await second.StopAsync();
        }
    }

    [Fact]
    public async Task GivenEntryOfAProviderTheNodeDoesNotHave_WhenTheOutboxIsDelivered_ThenTheNodeDeliversTheOtherEntriesAndLeavesThatOneWaiting()
    {
        // Arrange: a node of the service with a provider this node does not have, as during a rolling deployment,
        // enqueued an entry among this node's; with batches of two it would share a batch with the first message
        var serviceName = NewServiceName();
        var sink = new MessageSink<string>();
        using var host = BuildHost(serviceName, sink);
        const string otherSystemId = "other-provider";
        await ProduceAsync(host.Services, "key", "m-0");
        await EnqueueAsync(host.Services, new OutboxEntry
        {
            ServiceName = serviceName,
            SystemId = otherSystemId,
            Destination = "other://localhost/destination",
            GroupKey = $"{otherSystemId}:destination",
            Body = [1],
        });
        await ProduceAsync(host.Services, "key", "m-1");
        await ProduceAsync(host.Services, "key", "m-2");

        try
        {
            // Act
            await host.StartAsync();
            var received = await ReceiveAsync(sink, 3);

            // Assert
            Assert.Equal(["m-0", "m-1", "m-2"], received);
            using var scope = host.Services.CreateScope();
            var waiting = await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().GetBatchAsync(serviceName, [otherSystemId], 10);
            Assert.Equal(otherSystemId, Assert.Single(waiting).SystemId);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    // --- Services sharing a database ---

    [Fact]
    public async Task GivenTwoServicesSharingTheDatabase_WhenBothProduceThroughTheOutboxInterleaved_ThenEachDeliversOnlyItsOwnEntriesInOrder()
    {
        // Arrange: both services produce to the same topic under the same key, so their entries would form one group
        // if groups spanned services. Only service A runs at first, so its outbox daemon sees service B's entries
        // waiting next to its own, and anything it delivered of them would reach its consumer among its own.
        const int messagesPerService = 5;
        var topic = $"test-outbox-{NewServiceName()}";
        var serviceA = NewServiceName();
        var serviceB = NewServiceName();
        var sinkA = new MessageSink<string>();
        var sinkB = new MessageSink<string>();
        using var hostA = BuildHost(serviceA, sinkA, topic: topic);
        using var hostB = BuildHost(serviceB, sinkB, topic: topic);
        for (var i = 0; i < messagesPerService; i++)
        {
            await ProduceAsync(hostA.Services, "key", $"a-{i}");
            await ProduceAsync(hostB.Services, "key", $"b-{i}");
        }

        try
        {
            // Act
            await hostA.StartAsync();
            var receivedA = await ReceiveAsync(sinkA, messagesPerService);

            // Assert: service A delivered its own entries in order and left service B's waiting
            Assert.Equal(Enumerable.Range(0, messagesPerService).Select(i => $"a-{i}"), receivedA);

            // Act: service B's consumer reads the whole topic
            await hostB.StartAsync();
            var receivedB = await ReceiveAsync(sinkB, 2 * messagesPerService);

            // Assert
            Assert.Equal(Enumerable.Range(0, messagesPerService).Select(i => $"b-{i}"), receivedB.Where(m => m.StartsWith("b-", StringComparison.Ordinal)));
        }
        finally
        {
            await hostA.StopAsync();
            await hostB.StopAsync();
        }
    }

    // --- Helpers ---

    private Task ProduceAsync(IServiceProvider services, string key, string value) =>
        ProduceTransactionallyAsync(services, key, value, FlushBeforeCommitAsync);

    private async Task EnqueueAsync(IServiceProvider services, OutboxEntry entry)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        await using var transaction = await sp.GetRequiredService<IUnitOfWork>().BeginAsync();
        await sp.GetRequiredService<IOutboxRepository>().EnqueueAsync(entry);
        await FlushBeforeCommitAsync(sp);
        await transaction.CommitAsync();
    }

    private IHost BuildHost(string serviceName, MessageSink<string> sink, IOutboxObserver? recorder = null, string? topic = null)
    {
        topic ??= $"test-outbox-{serviceName}";
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                if (recorder is not null)
                {
                    services.AddSingleton<IOutboxObserver>(recorder);
                }

                services.AddEmit(emit =>
                {
                    ConfigureFastCoordination(emit, serviceName);
                    ConfigurePersistence(emit, outbox =>
                    {
                        outbox.PollingInterval = PollingInterval;
                        outbox.BatchSize = BatchSize;
                    });
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers);
                        kafka.AutoProvision();
                        kafka.Topic<string, string>(topic, t =>
                        {
                            t.UseUtf8Serialization();
                            t.Producer();
                            t.ConsumerGroup($"group-{serviceName}", group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.AddConsumer<SinkConsumer<string>>();
                            });
                        });
                    });
                });
            })
            .Build();
    }

    /// <summary>
    /// Receives messages until <paramref name="count"/> distinct ones arrived that are not in
    /// <paramref name="alreadyReceived"/>, and returns them in the order each first arrived. Delivery is at least
    /// once, so a message delivered again later is not a failure.
    /// </summary>
    private static async Task<List<string>> ReceiveAsync(
        MessageSink<string> sink,
        int count,
        IReadOnlyCollection<string>? alreadyReceived = null)
    {
        List<string> received = [];
        while (received.Count < count)
        {
            var context = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(60));
            if (!received.Contains(context.Message!) && alreadyReceived?.Contains(context.Message!) != true)
            {
                received.Add(context.Message!);
            }
        }

        return received;
    }

    /// <summary>
    /// Holds the outbox daemon once it has processed a number of entries, until released.
    /// </summary>
    private sealed class HoldingObserver(int holdAfter) : IOutboxObserver
    {
        private readonly TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int processed;

        public Task Held => held.Task;

        public void Release() => released.TrySetResult();

        public async Task OnProcessedAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref processed) == holdAfter)
            {
                held.TrySetResult();
                await released.Task;
            }
        }
    }

    private sealed class ProcessedEntryRecorder : IOutboxObserver
    {
        private readonly TaskCompletionSource firstProcessed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Completes once the host has processed an entry.
        /// </summary>
        public Task FirstProcessed => firstProcessed.Task;

        public Task OnProcessedAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            firstProcessed.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
