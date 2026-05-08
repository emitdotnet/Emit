namespace Emit.Kafka.Tests;

using System.Text;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.FluentValidation;
using Emit.IntegrationTests.Integration;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Pipeline;
using Emit.Testing;
using global::FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka-specific tests for <c>.SkipNullPayloads()</c>: verifies that tombstone records
/// (non-null key, null value bytes) are silently dropped before reaching the consumer handler
/// and that no dead-letter entries are produced.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KafkaSkipNullPayloadsTests(KafkaContainerFixture fixture)
    : IClassFixture<KafkaContainerFixture>
{
    /// <summary>
    /// Produces a Kafka tombstone (non-null key, null value bytes) to a topic using a raw
    /// Confluent producer, bypassing Emit serialization.
    /// </summary>
    private async Task ProduceTombstoneAsync(string topic, string key)
    {
        var producerConfig = new ConfluentKafka.ProducerConfig
        {
            BootstrapServers = fixture.BootstrapServers,
        };

        using var rawProducer = new ConfluentKafka.ProducerBuilder<byte[], byte[]>(producerConfig).Build();
        var keyBytes = Encoding.UTF8.GetBytes(key);

        await rawProducer.ProduceAsync(topic, new ConfluentKafka.Message<byte[], byte[]>
        {
            Key = keyBytes,
            Value = null!, // tombstone
        });

        rawProducer.Flush(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a single consumer with <c>.SkipNullPayloads()</c> silently drops tombstone
    /// records and does not produce any dead-letter entries. Offsets advance past the tombstone.
    /// </summary>
    [Fact]
    public async Task GivenSingleConsumerWithSkipNullPayloads_WhenTombstoneConsumed_ThenDroppedNoDlqEntry()
    {
        // Arrange
        var topic = $"test-skip-null-single-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var dlqSink = new MessageSink<byte[]>();
        var dlqTopic = $"test-skip-null-single-dlq-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlq-{Guid.NewGuid():N}";

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
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
                                group.AddConsumer<DlqCaptureConsumer>();
                            });
                        });

                        kafka.Topic<string, string>(topic, t =>
                        {
                            t.UseUtf8Serialization();
                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.SkipNullPayloads();
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
            // Act — produce a tombstone, then a sentinel non-null message
            await ProduceTombstoneAsync(topic, "key-1");

            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("key-2", "sentinel"));

            // Assert — sentinel arrives, tombstone was dropped, no DLQ entry
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("sentinel", ctx.Message);
            Assert.Single(sink.ReceivedMessages);
            Assert.Empty(dlqSink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a batch consumer with <c>.SkipNullPayloads()</c> drops null items from
    /// the batch before the handler sees them. No dead-letter entries are produced.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumerWithSkipNullPayloads_WhenBatchContainsTombstone_ThenDroppedNoDlqEntry()
    {
        // Arrange
        var topic = $"test-skip-null-batch-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();
        var dlqSink = new MessageSink<byte[]>();
        var dlqTopic = $"test-skip-null-batch-dlq-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlq-{Guid.NewGuid():N}";

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
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
                                group.AddConsumer<DlqCaptureConsumer>();
                            });
                        });

                        kafka.Topic<string, string>(topic, t =>
                        {
                            t.UseUtf8Serialization();
                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.SkipNullPayloads();
                                group.AddBatchConsumer<BatchSinkConsumer<string>>(opts =>
                                {
                                    opts.MaxSize = 10;
                                    opts.Timeout = TimeSpan.FromSeconds(2);
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
            // Act — produce a tombstone then a non-null sentinel
            await ProduceTombstoneAsync(topic, "key-1");

            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("key-2", "sentinel"));

            // Assert — sentinel arrives in the batch, tombstone was dropped, no DLQ entry
            await TestHelpers.WaitUntilAsync(
                () => sink.Messages.Count >= 1,
                "Expected sentinel to arrive in batch",
                TimeSpan.FromSeconds(30));

            Assert.Contains("sentinel", sink.Messages);
            Assert.DoesNotContain(null, sink.Messages);
            Assert.Empty(dlqSink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that <c>.SkipNullPayloads()</c> combined with <c>.ValidateWithFluentValidation(...)</c>
    /// silently drops tombstones (filter runs before validation), routes invalid non-null records to
    /// dead-letter, and passes valid non-null records to the handler.
    /// </summary>
    [Fact]
    public async Task GivenSkipNullPayloadsCombinedWithValidation_WhenTombstoneAndInvalidAndValid_ThenCorrectRouting()
    {
        // Arrange
        var topic = $"test-skip-null-combo-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var dlqTopic = $"test-skip-null-combo-dlq-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlq-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(dlqSink);
                services.AddScoped<IValidator<string>, ValidPrefixValidator>();
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
                                group.AddConsumer<DlqCaptureConsumer>();
                            });
                        });

                        kafka.Topic<string, string>(topic, t =>
                        {
                            t.UseUtf8Serialization();
                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.SkipNullPayloads();
                                group.ValidateWithFluentValidation(a => a.DeadLetter());
                                group.OnError(e => e.Default(d => d.DeadLetter()));
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
            // Act — produce tombstone, invalid message, valid message
            await ProduceTombstoneAsync(topic, "key-tombstone");

            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("key-invalid", "bad-no-prefix"));
            await producer.ProduceAsync(new EventMessage<string, string>("key-valid", "valid:hello"));

            // Assert — valid message reaches handler
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("valid:hello", ctx.Message);
            Assert.Single(sink.ReceivedMessages);

            // Assert — invalid message dead-lettered, tombstone is NOT dead-lettered
            await TestHelpers.WaitUntilAsync(
                () => dlqSink.ReceivedMessages.Count >= 1,
                "Expected invalid message to be dead-lettered",
                TimeSpan.FromSeconds(30));

            Assert.Single(dlqSink.ReceivedMessages);
            Assert.Equal("bad-no-prefix", Encoding.UTF8.GetString(dlqSink.ReceivedMessages.First().Message));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Validator that requires message values to start with <c>"valid:"</c>.
    /// </summary>
    private sealed class ValidPrefixValidator : AbstractValidator<string>
    {
        public ValidPrefixValidator()
        {
            RuleFor(x => x).Must(x => x.StartsWith("valid:", StringComparison.Ordinal))
                .WithMessage("Message must start with 'valid:'");
        }
    }
}
