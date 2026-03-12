namespace Emit.Kafka.Tests;

using System.Text;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka-specific tests for DLQ misconfiguration scenarios where a dead letter action is
/// configured in the error policy but no <see cref="IDeadLetterSink"/> is registered.
/// </summary>
[Trait("Category", "Integration")]
public class KafkaDlqMisconfigurationTests(KafkaContainerFixture fixture)
    : IClassFixture<KafkaContainerFixture>
{
    /// <summary>
    /// Verifies that when a deserialization error policy routes to a DLQ but no dead letter sink
    /// is registered, the message is silently discarded (not thrown) and subsequent valid messages
    /// continue to be processed normally.
    /// </summary>
    [Fact]
    public async Task GivenDeserializationErrorWithNoDeadLetterSink_WhenConsumed_ThenMessageDiscardedNotThrown()
    {
        // Arrange
        var sourceTopic = $"test-dlq-misconfig-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var dlqTopic = $"test-dlq-misconfig-dlq-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit =>
                {
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config =>
                        {
                            config.BootstrapServers = fixture.BootstrapServers;
                        });
                        kafka.AutoProvision();

                        // Source topic with Int32 key deserializer — invalid keys will fail deserialization.
                        // The DLQ policy names a topic but no dead letter sink is registered (no kafka.DeadLetter(...)).
                        kafka.Topic<int, string>(sourceTopic, t =>
                        {
                            t.SetInt32KeyDeserializer();
                            t.SetUtf8ValueDeserializer();

                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.OnDeserializationError(e => e.DeadLetter());
                                group.AddConsumer<PassThroughConsumer>();
                            });
                        });
                    });
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            var producerConfig = new ConfluentKafka.ProducerConfig
            {
                BootstrapServers = fixture.BootstrapServers
            };

            using var rawProducer = new ConfluentKafka.ProducerBuilder<byte[], byte[]>(producerConfig).Build();

            // Act — produce a message with an invalid key (5 bytes for Int32 which expects 4).
            var invalidKey = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            var firstValue = Encoding.UTF8.GetBytes("bad-key-message");
            await rawProducer.ProduceAsync(sourceTopic,
                new ConfluentKafka.Message<byte[], byte[]> { Key = invalidKey, Value = firstValue });

            // Act — produce a second message with a valid key (exactly 4 bytes for Int32).
            var validKey = BitConverter.GetBytes(42);
            if (BitConverter.IsLittleEndian) Array.Reverse(validKey);
            var secondValue = Encoding.UTF8.GetBytes("valid-after-discard");
            await rawProducer.ProduceAsync(sourceTopic,
                new ConfluentKafka.Message<byte[], byte[]> { Key = validKey, Value = secondValue });

            rawProducer.Flush(TimeSpan.FromSeconds(5));

            // Assert — the pipeline does not crash; the second message (valid key) is consumed normally.
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("valid-after-discard", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Passes all messages directly to the sink without additional processing.
    /// </summary>
    private sealed class PassThroughConsumer(MessageSink<string> sink) : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => sink.WriteAsync(context, cancellationToken);
    }
}
