namespace Emit.Kafka.Tests;

using System.Collections.Concurrent;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Observability;
using Emit.Kafka.Tests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka-specific: incremental (cooperative) rebalances, which revoke only some partitions of a consumer and add
/// partitions to a consumer without revoking any.
/// </summary>
[Trait("Category", "Integration")]
public class KafkaCooperativeRebalanceTests(KafkaContainerFixture fixture)
    : IClassFixture<KafkaContainerFixture>
{
    private const int Keys = 8;
    private const int MessagesPerKey = 5;
    private const int WorkerCount = 2;
    private const int Partitions = 2;

    /// <summary>
    /// Far longer than any handler takes to stop once cancelled, however busy the machine. The handlers wait on the
    /// tracker, so every revoke waits this long before cancelling them, leaving messages in the queues.
    /// </summary>
    private static readonly TimeSpan WorkerStopTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long a wait spanning a stop of the workers may take.</summary>
    private static readonly TimeSpan StopsOutlasted = TimeSpan.FromMinutes(2);

    /// <summary>The messages produced, in order: the keys' sequences interleaved.</summary>
    private static readonly (string Key, string Value)[] Produced =
    [
        .. Enumerable.Range(1, MessagesPerKey).SelectMany(n => Enumerable.Range(1, Keys).Select(key => ($"k{key}", $"k{key}-{n}"))),
    ];

    /// <summary>
    /// Verifies that while consumers join and leave a cooperative group under load, no message is lost, the
    /// messages of a key are handled in order, a key is never handled by two handlers at once, and a consumer never
    /// runs more handlers than it has workers: the partitions a consumer keeps go on from the messages its stopped
    /// pool left, and partitions added to a running pool go to the same workers.
    /// </summary>
    [Fact]
    public async Task GivenCooperativeGroup_WhenConsumersJoinAndLeave_ThenEveryMessageIsHandledInOrderAndNeverTwiceAtOnce()
    {
        // Arrange
        var topic = $"test-coop-{Guid.NewGuid():N}";
        var groupId = $"test-coop-group-{Guid.NewGuid():N}";
        // Handlings wait until the test opens the tracker: every rebalance happens with the workers busy and their
        // queues full
        var tracker = new RebalanceCompliance.HandlingTracker(closed: true);
        var assignments = new Assignments();

        using var producerHost = BuildHost(topic, t => t.Producer());
        using var first = BuildHost(topic, t => AddConsumerGroup(t, groupId), tracker, assignments, "first");
        using var second = BuildHost(topic, t => AddConsumerGroup(t, groupId), tracker, assignments, "second");
        await producerHost.StartAsync();
        await first.StartAsync();

        try
        {
            await ProduceAsync(producerHost);
            await TestHelpers.WaitUntilAsync(
                () => tracker.WaitingOn("first") == WorkerCount, "The first consumer is not handling messages.", StopsOutlasted);

            // Act: a consumer joins, taking a partition from the first, which keeps the other with messages its
            // stopped pool left; then it leaves, adding its partition to the first's running pool
            await second.StartAsync();
            await TestHelpers.WaitUntilAsync(
                () => tracker.WaitingOn("second") > 0, "The second consumer got no partition.", StopsOutlasted);
            await second.StopAsync();
            await TestHelpers.WaitUntilAsync(
                () => assignments.Of("first") == Partitions, "The first consumer did not get the partition back.", StopsOutlasted);
            tracker.Open();

            // Assert
            try
            {
                await TestHelpers.WaitUntilAsync(
                    () => tracker.Completed.Distinct().Count() == Produced.Length, "Messages were lost.", StopsOutlasted);
            }
            catch (TimeoutException)
            {
                var lost = Produced.Select(m => m.Value).Except(tracker.Completed);
                Assert.Fail($"Messages were lost across the rebalances: {string.Join(", ", lost)}");
            }

            Assert.True(tracker.MostAtOnceForAKey == 1, $"{tracker.MostAtOnceForAKey} handlings ran at once for a key.");
            Assert.True(tracker.MostAtOnceOnAHost <= WorkerCount, $"{tracker.MostAtOnceOnAHost} handlings ran at once on a host.");
            var handled = tracker.Completed.Distinct().ToList();
            foreach (var key in Produced.Select(m => m.Key).Distinct())
            {
                Assert.Equal(
                    Produced.Where(m => m.Key == key).Select(m => m.Value),
                    handled.Where(m => m.StartsWith($"{key}-", StringComparison.Ordinal)));
            }
        }
        finally
        {
            await first.StopAsync();
            await producerHost.StopAsync();
        }
    }

    private static async Task ProduceAsync(IHost producerHost)
    {
        using var scope = producerHost.Services.CreateScope();
        var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
        foreach (var (key, value) in Produced)
        {
            await producer.ProduceAsync(new EventMessage<string, string>(key, value));
        }
    }

    private static void AddConsumerGroup(KafkaTopicBuilder<string, string> topic, string groupId) =>
        topic.ConsumerGroup(groupId, group =>
        {
            group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
            group.PartitionAssignmentStrategy = ConfluentKafka.PartitionAssignmentStrategy.CooperativeSticky;
            group.WorkerCount = WorkerCount;
            group.BufferSize = 2;
            group.WorkerStopTimeout = WorkerStopTimeout;
            group.AddConsumer<RebalanceCompliance.TrackingConsumer>();
        });

    private IHost BuildHost(
        string topic,
        Action<KafkaTopicBuilder<string, string>> configureTopic,
        RebalanceCompliance.HandlingTracker? tracker = null,
        Assignments? assignments = null,
        string? hostName = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                if (tracker is not null)
                {
                    services.AddSingleton(tracker);
                    services.AddSingleton(assignments!);
                    services.AddSingleton(new RebalanceCompliance.ConsumerHost(hostName!));
                }

                services.AddEmit(emit => emit.AddKafka(kafka =>
                {
                    kafka.ConfigureClient(config => config.BootstrapServers = fixture.BootstrapServers);
                    kafka.AutoProvision();
                    if (tracker is not null)
                    {
                        kafka.AddConsumerObserver<AssignmentObserver>();
                    }

                    kafka.Topic<string, string>(topic, t =>
                    {
                        t.UseUtf8Serialization();
                        t.Provisioning(options => options.NumPartitions = Partitions);
                        configureTopic(t);
                    });
                }));
            })
            .Build();

    /// <summary>
    /// The partitions each host holds.
    /// </summary>
    private sealed class Assignments
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, byte>> held = new();

        public int Of(string host) => held.TryGetValue(host, out var partitions) ? partitions.Count : 0;

        public void Add(string host, IEnumerable<int> partitions)
        {
            var hostPartitions = held.GetOrAdd(host, _ => new());
            foreach (var partition in partitions)
            {
                hostPartitions[partition] = 0;
            }
        }

        public void Remove(string host, IEnumerable<int> partitions)
        {
            var hostPartitions = held.GetOrAdd(host, _ => new());
            foreach (var partition in partitions)
            {
                hostPartitions.TryRemove(partition, out _);
            }
        }
    }

    /// <summary>
    /// Records the partitions its host is assigned, revoked and loses.
    /// </summary>
    private sealed class AssignmentObserver(Assignments assignments, RebalanceCompliance.ConsumerHost host)
        : IKafkaConsumerObserver
    {
        public Task OnPartitionsAssignedAsync(PartitionsAssignedEvent e)
        {
            assignments.Add(host.Name, e.Partitions);
            return Task.CompletedTask;
        }

        public Task OnPartitionsRevokedAsync(PartitionsRevokedEvent e)
        {
            assignments.Remove(host.Name, e.Partitions);
            return Task.CompletedTask;
        }

        public Task OnPartitionsLostAsync(PartitionsLostEvent e)
        {
            assignments.Remove(host.Name, e.Partitions);
            return Task.CompletedTask;
        }
    }
}
