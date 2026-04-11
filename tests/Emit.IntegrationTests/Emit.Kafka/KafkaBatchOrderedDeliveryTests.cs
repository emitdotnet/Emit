namespace Emit.Kafka.Tests;

using Emit.Abstractions;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Observability;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Integration tests for ordered, complete delivery across multiple Kafka partitions and workers
/// using the batch consumer pipeline. Verifies that ByteKeyHash routing combined with Kafka's
/// per-partition ordering guarantees that messages for each key arrive at the batch consumer in
/// the exact order they were produced, regardless of how many workers or partitions are in play.
/// Also verifies that offsets are committed after all batches are processed.
/// </summary>
[Trait("Category", "Integration")]
public class KafkaBatchOrderedDeliveryTests(KafkaContainerFixture fixture)
    : IClassFixture<KafkaContainerFixture>
{
    // 8 keys spread across 6 partitions via Kafka's default murmur2 partitioner.
    // ByteKeyHash routes each key to a dedicated worker, preserving per-key ordering.
    private static readonly string[] Keys = ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel"];
    private const int PartitionCount = 6;
    private const int WorkerCount = 3;
    private const int MessagesPerKey = 25;
    private const int BatchMaxSize = 10;
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task GivenMultipleKeysAndPartitions_WhenBatchConsumed_ThenAllArrivedInKeyOrderWithOffsetCommit()
    {
        // Arrange
        var topic = $"test-batch-ordered-{Guid.NewGuid():N}";
        var groupId = $"test-batch-ordered-group-{Guid.NewGuid():N}";

        var batchSink = new BatchSinkConsumer<string>();
        var commitAwaiter = new CommitAwaiter(topic);

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(batchSink);
                services.AddSingleton<IKafkaConsumerObserver>(commitAwaiter);
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
                            t.Provisioning(opts => opts.NumPartitions = PartitionCount);
                            t.SetUtf8KeySerializer();
                            t.SetUtf8ValueSerializer();
                            t.SetUtf8KeyDeserializer();
                            t.SetUtf8ValueDeserializer();

                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.CommitInterval = TimeSpan.FromSeconds(2);
                                group.WorkerCount = WorkerCount;
                                group.WorkerDistribution = WorkerDistribution.ByKeyHash;
                                group.AddBatchConsumer<BatchSinkConsumer<string>>(bc =>
                                {
                                    bc.MaxSize = BatchMaxSize;
                                    bc.Timeout = BatchTimeout;
                                });
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

            // Act — produce messages interleaved across all keys so workers process messages
            // from different keys concurrently. Message values encode the key and sequence as
            // "key:seq" (e.g., "alpha:0", "alpha:1") for ordering assertions.
            var totalMessages = Keys.Length * MessagesPerKey;

            for (var seq = 0; seq < MessagesPerKey; seq++)
            {
                foreach (var key in Keys)
                {
                    await producer.ProduceAsync(new EventMessage<string, string>(key, $"{key}:{seq}"));
                }
            }

            // Drain — poll the batch sink until all messages arrive. The batch consumer
            // collects messages into batches of up to BatchMaxSize; we only care about the
            // flattened message list for completeness and ordering assertions.
            var deadline = DateTime.UtcNow.AddSeconds(60);

            while (batchSink.Messages.Count < totalMessages && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }

            var received = batchSink.Messages;

            // Assert — all messages arrived.
            Assert.Equal(totalMessages, received.Count);

            // Assert — per-key ordering: for each key, the sink must have received messages
            // with sequences 0, 1, 2, ..., MessagesPerKey-1 in that exact relative order.
            //
            // Why this holds: Kafka guarantees that messages with the same key land on the
            // same partition and are delivered in offset order. ByteKeyHash maps the same key
            // bytes to the same worker on every call, and the worker's channel processes
            // messages sequentially. The batch accumulator preserves insertion order within
            // each batch. Therefore, seq 0 always reaches the consumer before seq 1, and
            // seq 1 before seq 2, for every key — even across batch boundaries.
            foreach (var key in Keys)
            {
                var keyMessages = received
                    .Where(m => m.StartsWith(key + ":", StringComparison.Ordinal))
                    .ToList();

                Assert.Equal(MessagesPerKey, keyMessages.Count);

                for (var i = 0; i < keyMessages.Count; i++)
                {
                    var seq = int.Parse(keyMessages[i].Split(':')[1]);
                    Assert.Equal(i, seq);
                }
            }

            // Assert — offsets for the topic are committed within the commit interval after
            // the last batch is processed.
            var commit = await commitAwaiter.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Contains(commit.Offsets, o => o.Topic == topic);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Resolves on the first successful offset commit event that contains an offset
    /// for the monitored topic.
    /// </summary>
    private sealed class CommitAwaiter(string topic) : IKafkaConsumerObserver
    {
        private readonly TaskCompletionSource<OffsetsCommittedEvent> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public Task OnOffsetsCommittedAsync(OffsetsCommittedEvent e)
        {
            if (e.Offsets.Any(o => o.Topic == topic))
            {
                tcs.TrySetResult(e);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Waits until a commit event for the monitored topic arrives.
        /// </summary>
        public async Task<OffsetsCommittedEvent> WaitAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"No offset commit for topic '{topic}' received within {timeout}.");
            }
        }
    }
}
