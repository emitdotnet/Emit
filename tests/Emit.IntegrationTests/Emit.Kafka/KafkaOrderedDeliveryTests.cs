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
/// Integration tests for ordered, complete delivery across multiple Kafka partitions and workers.
/// Verifies that ByteKeyHash routing combined with Kafka's per-partition ordering guarantees
/// that messages for each key arrive at the consumer in the exact order they were produced,
/// regardless of how many workers or partitions are in play. Also verifies that offsets are
/// committed after consumption.
/// </summary>
[Trait("Category", "Integration")]
public class KafkaOrderedDeliveryTests(KafkaContainerFixture fixture)
    : IClassFixture<KafkaContainerFixture>
{
    // 4 keys spread across 3 partitions via Kafka's default murmur2 partitioner.
    // ByteKeyHash routes each key to a dedicated worker, preserving per-key ordering.
    private static readonly string[] Keys = ["apple", "banana", "cherry", "date"];
    private const int PartitionCount = 3;
    private const int WorkerCount = 4;
    private const int MessagesPerKey = 25;

    /// <summary>
    /// With the default queue size and a fast handler, and with one queued message per worker and a slow handler:
    /// then nearly every message finds its worker's queue full, so its partition is paused, the poll loop seeks
    /// back to it and polls on, and the partition resumes once the queue has room. No message may be skipped,
    /// handled twice or handled out of order through any of that.
    /// </summary>
    [Theory]
    [InlineData(32, 0)]
    [InlineData(1, 2)]
    public async Task GivenMultipleKeysAndPartitions_WhenAllMessagesProduced_ThenAllArrivedInKeyOrderWithOffsetCommit(
        int bufferSize,
        int handlerDelayMilliseconds)
    {
        // Arrange — unique topic and group to isolate this test from others sharing the broker.
        var topic = $"test-ordered-{Guid.NewGuid():N}";
        var groupId = $"test-ordered-group-{Guid.NewGuid():N}";

        var sink = new MessageSink<string>();
        var commitAwaiter = new CommitAwaiter(topic);

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(new HandlerDelay(TimeSpan.FromMilliseconds(handlerDelayMilliseconds)));
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
                            t.UseUtf8Serialization();

                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.CommitInterval = TimeSpan.FromSeconds(2);
                                group.WorkerCount = WorkerCount;
                                group.WorkerDistribution = WorkerDistribution.ByKeyHash;
                                group.BufferSize = bufferSize;
                                group.AddConsumer<DelayedSinkConsumer>();
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
            // "key:seq" (e.g., "apple:0", "apple:1") for ordering assertions.
            // Each key is produced in strict ascending sequence order (0 .. MessagesPerKey-1),
            // which Kafka preserves at the partition level and ByteKeyHash preserves at the
            // worker level.
            for (var seq = 0; seq < MessagesPerKey; seq++)
            {
                foreach (var key in Keys)
                {
                    await producer.ProduceAsync(new EventMessage<string, string>(key, $"{key}:{seq}"));
                }
            }

            // Drain the sink until all messages arrive. Allow up to 60 s per message to
            // account for partition assignment and initial poll latency.
            var totalMessages = Keys.Length * MessagesPerKey;
            var received = new List<string>(totalMessages);

            for (var i = 0; i < totalMessages; i++)
            {
                var ctx = await sink.WaitForMessageAsync();
                received.Add(ctx.Message!);
            }

            // Assert — all messages arrived.
            Assert.Equal(totalMessages, received.Count);

            // Assert — per-key ordering: for each key, the sink must have received messages
            // with sequences 0, 1, 2, ..., MessagesPerKey-1 in that exact order.
            //
            // Why this holds: Kafka guarantees that messages with the same key land on the
            // same partition and are delivered in offset order. ByteKeyHash maps the same key
            // bytes to the same worker on every call, and the worker's channel processes
            // messages sequentially. Therefore, seq 0 always reaches the worker before seq 1,
            // and seq 1 before seq 2, for every key.
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
            // the last message is processed. CommitAwaiter resolves as soon as any commit
            // event containing our topic arrives; no Task.Delay required.
            var commit = await commitAwaiter.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Contains(commit.Offsets, o => o.Topic == topic);

            // Assert: nothing was handled twice, including after the last message
            Assert.Equal(totalMessages, sink.ReceivedMessages.Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// How long <see cref="DelayedSinkConsumer"/> takes to handle a message.
    /// </summary>
    private sealed record HandlerDelay(TimeSpan Value);

    /// <summary>
    /// Consumer that takes <see cref="HandlerDelay"/> to handle each message, then writes it to the sink.
    /// </summary>
    private sealed class DelayedSinkConsumer(MessageSink<string> sink, HandlerDelay delay) : IConsumer<string>
    {
        public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            if (delay.Value > TimeSpan.Zero)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            await sink.WriteAsync(context, cancellationToken);
        }
    }
}
