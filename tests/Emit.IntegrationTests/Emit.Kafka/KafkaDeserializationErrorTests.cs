namespace Emit.Kafka.Tests;

using System.Text;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Observability;
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
        var dlqSink = new MessageSink<byte[]>();

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
                        kafka.AutoProvision();

                        kafka.DeadLetter(dlqTopic, t =>
                        {
                            t.ConsumerGroup(dlqGroupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.AddConsumer<SinkConsumer<byte[]>>();
                            });
                        });

                        // Source topic with Int32 key deserializer — 5-byte keys will fail.
                        kafka.Topic<int, string>(sourceTopic, t =>
                        {
                            t.SetInt32KeyDeserializer();
                            t.SetUtf8ValueDeserializer();

                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.OnDeserializationError(e => e.DeadLetter());
                                group.AddConsumer<NullConsumer>();
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
            var ctx = await dlqSink.WaitForMessageAsync();
            Assert.Equal("deserialization-error-payload", Encoding.UTF8.GetString(ctx.Message));

            // Assert — diagnostic headers identify the deserialization error.
            var headers = ctx.Headers;
            Assert.NotNull(headers);

            var exceptionType = headers
                .FirstOrDefault(h => h.Key == "emit-exception-type").Value;
            var sourceTopic_ = headers
                .FirstOrDefault(h => h.Key == "emit-source-topic").Value;

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

    /// <summary>
    /// Verifies that messages whose deserializer cannot reach the schema registry are not dead-lettered or
    /// discarded: they wait, retried, and all arrive in order once the registry can be reached.
    /// </summary>
    [Fact]
    public async Task GivenUnreachableSchemaRegistry_WhenMessagesArrive_ThenTheyWaitAndAllArriveInOrderOnceItIsBack()
    {
        // Arrange: the producer reaches the schema registry directly, the consumer through a proxy that is down
        var topic = $"test-deser-registry-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var failures = new InvocationCounter();
        await using var registry = new ToggleableTcpProxy(new Uri(fixture.SchemaRegistryUrl));

        using var producerHost = BuildAvroHost(fixture.SchemaRegistryUrl, topic, t =>
        {
            t.SetUtf8KeySerializer();
            t.SetAvroValueSerializer();
            t.Producer();
        });
        using var consumerHost = BuildAvroHost(
            registry.Url,
            topic,
            t =>
            {
                t.SetUtf8KeyDeserializer();
                t.SetAvroValueDeserializer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.DeserializationRetryBackoff = Backoff.Fixed(TimeSpan.FromMilliseconds(200));
                    group.OnDeserializationError(e => e.Discard());
                    group.AddConsumer<SinkConsumer<string>>();
                });
            },
            services => services.AddSingleton(sink).AddSingleton(failures));

        await producerHost.StartAsync();
        await consumerHost.StartAsync();

        try
        {
            using (var scope = producerHost.Services.CreateScope())
            {
                var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
                foreach (var value in new[] { "first", "second", "third" })
                {
                    await producer.ProduceAsync(new EventMessage<string, string>("k1", value));
                }
            }

            // Act: the first message is retried while the registry cannot be reached, then the registry is back
            await TestHelpers.WaitUntilAsync(
                () => failures.Count >= 3, "The deserialization of the first message was not retried.");
            Assert.Empty(sink.ReceivedMessages);
            registry.Up();

            // Assert
            await TestHelpers.WaitUntilAsync(
                () => sink.ReceivedMessages.Count == 3, "Not every message arrived once the schema registry was back.");
            Assert.Equal(["first", "second", "third"], sink.ReceivedMessages.Select(m => m.Message));
        }
        finally
        {
            await consumerHost.StopAsync();
            await producerHost.StopAsync();
        }
    }

    /// <summary>
    /// Verifies that bytes the Avro deserializer cannot decode are dead-lettered at once, the schema registry
    /// being reachable: invalid data is never retried.
    /// </summary>
    [Fact]
    public async Task GivenBytesThatAreNotAvro_WhenConsumed_ThenTheyAreDeadLetteredWithoutRetry()
    {
        // Arrange
        var topic = $"test-deser-avro-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var dlqTopic = $"test-deser-avro-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<byte[]>();
        var failures = new InvocationCounter();

        using var host = BuildAvroHost(
            fixture.SchemaRegistryUrl,
            topic,
            t =>
            {
                t.SetUtf8KeyDeserializer();
                t.SetAvroValueDeserializer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.OnDeserializationError(e => e.DeadLetter());
                    group.AddConsumer<NullConsumer>();
                });
            },
            services => services.AddSingleton(dlqSink).AddSingleton(failures),
            kafka => kafka.DeadLetter(dlqTopic, t => t.ConsumerGroup($"group-dlt-{Guid.NewGuid():N}", group =>
            {
                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                group.AddConsumer<SinkConsumer<byte[]>>();
            })));

        await host.StartAsync();

        try
        {
            // Act: a plain JSON payload, which has no schema registry framing
            using var rawProducer = new ConfluentKafka.ProducerBuilder<byte[], byte[]>(
                new ConfluentKafka.ProducerConfig { BootstrapServers = fixture.BootstrapServers }).Build();
            await rawProducer.ProduceAsync(topic, new ConfluentKafka.Message<byte[], byte[]>
            {
                Key = Encoding.UTF8.GetBytes("k1"),
                Value = Encoding.UTF8.GetBytes("""{"not":"avro"}"""),
            });

            // Assert
            var ctx = await dlqSink.WaitForMessageAsync();
            Assert.Equal(
                typeof(InvalidDataException).FullName,
                ctx.Headers.First(h => h.Key == DeadLetterHeaders.ExceptionType).Value);
            Assert.Equal(1, failures.Count);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private IHost BuildAvroHost(
        string schemaRegistryUrl,
        string topic,
        Action<KafkaTopicBuilder<string, string>> configureTopic,
        Action<IServiceCollection>? configureServices = null,
        Action<KafkaBuilder>? configureKafka = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                configureServices?.Invoke(services);
                services.AddEmit(emit => emit.AddKafka(kafka =>
                {
                    kafka.ConfigureClient(config => config.BootstrapServers = fixture.BootstrapServers);
                    kafka.ConfigureSchemaRegistry(registry => registry.Url = schemaRegistryUrl);
                    kafka.AutoProvision();
                    kafka.AddConsumerObserver<DeserializationFailureObserver>();
                    configureKafka?.Invoke(kafka);
                    kafka.Topic(topic, configureTopic);
                }));
            })
            .Build();

    /// <summary>
    /// Counts the failed attempts to deserialize a message, when the test registers an <see cref="InvocationCounter"/>.
    /// </summary>
    private sealed class DeserializationFailureObserver(IServiceProvider services) : IKafkaConsumerObserver
    {
        public Task OnDeserializationErrorAsync(DeserializationErrorEvent e)
        {
            services.GetService<InvocationCounter>()?.Increment();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Consumer that does nothing. Used on the source topic in deserialization error tests
    /// where messages are expected to fail deserialization before reaching any consumer.
    /// </summary>
    private sealed class NullConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
