namespace Emit.Kafka.Tests;

using System.Text;
using Confluent.Kafka.Admin;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka-specific tests for deserialization error handling. These tests exercise the
/// <c>OnDeserializationError</c> policy, which applies when a message cannot be deserialized
/// before reaching the consumer pipeline.
/// </summary>
[Trait("Category", "Integration")]
public class KafkaDeserializationErrorTests(KafkaContainerFixture fixture)
    : IClassFixture<KafkaContainerFixture>
{
    /// <summary>
    /// Verifies that a message with an invalid key (5 bytes for an Int32 deserializer that
    /// expects exactly 4) is routed to the DLQ with diagnostic headers.
    /// </summary>
    [Fact]
    public async Task GivenInvalidKeyBytes_WhenConsumed_ThenMessageDeadLetteredWithDiagnosticHeaders()
    {
        // Arrange
        var sourceTopic = $"test-deser-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-deser-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<string>();

        // DLQ topic must exist before the source consumer starts (DlqTopicVerifier requires it).
        CreateTopic(dlqTopic);

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit =>
                {
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config =>
                        {
                            config.BootstrapServers = fixture.BootstrapServers;
                        });

                        // Source topic with Int32 key deserializer — 5-byte keys will fail.
                        kafka.Topic<int, string>(sourceTopic, t =>
                        {
                            t.SetInt32KeyDeserializer();
                            t.SetUtf8ValueDeserializer();

                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.OnDeserializationError(e => e.DeadLetter(dlqTopic));
                                group.AddConsumer<NullConsumer>();
                            });
                        });

                        // DLQ topic — consumer only; captures dead-lettered messages.
                        kafka.Topic<string, string>(dlqTopic, t =>
                        {
                            t.SetUtf8KeyDeserializer();
                            t.SetUtf8ValueDeserializer();

                            t.ConsumerGroup(dlqGroupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.AddConsumer<SinkConsumer<string>>();
                            });
                        });
                    });
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a message with a 5-byte key (invalid for Int32 which expects 4 bytes).
            // Use a raw producer to bypass any framework serialization.
            var producerConfig = new ConfluentKafka.ProducerConfig
            {
                BootstrapServers = fixture.BootstrapServers
            };

            using var rawProducer = new ConfluentKafka.ProducerBuilder<byte[], byte[]>(producerConfig).Build();

            var invalidKey = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }; // 5 bytes, Int32 expects 4
            var value = Encoding.UTF8.GetBytes("deserialization-error-payload");

            await rawProducer.ProduceAsync(sourceTopic,
                new ConfluentKafka.Message<byte[], byte[]> { Key = invalidKey, Value = value });

            rawProducer.Flush(TimeSpan.FromSeconds(5));

            // Assert — message arrived in the DLQ.
            var ctx = await dlqSink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("deserialization-error-payload", ctx.Message);

            // Assert — diagnostic headers identify the deserialization error.
            var headers = ctx.Features.Get<IHeadersFeature>();
            Assert.NotNull(headers);

            var exceptionType = headers.Headers
                .FirstOrDefault(h => h.Key == "x-emit-exception-type").Value;
            var sourceTopic_ = headers.Headers
                .FirstOrDefault(h => h.Key == "x-emit-source-topic").Value;

            Assert.NotNull(exceptionType);
            Assert.NotNull(sourceTopic_);
            Assert.Equal(sourceTopic, sourceTopic_);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private void CreateTopic(string topicName)
    {
        using var adminClient = new ConfluentKafka.AdminClientBuilder(
            new ConfluentKafka.AdminClientConfig { BootstrapServers = fixture.BootstrapServers })
            .Build();

        adminClient.CreateTopicsAsync(
            [new TopicSpecification { Name = topicName, ReplicationFactor = 1, NumPartitions = 1 }])
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Consumer that does nothing. Used on the source topic in deserialization error tests
    /// where messages are expected to fail deserialization before reaching any consumer.
    /// </summary>
    private sealed class NullConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
