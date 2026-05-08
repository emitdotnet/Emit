namespace Emit.Kafka.Tests;

using System.Text;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.FluentValidation;
using Emit.IntegrationTests.Integration;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using global::FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Kafka-specific tests for FluentValidation-based message validation. These tests exercise
/// <c>ValidateWithFluentValidation</c> with discard and dead-letter error actions.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KafkaFluentValidationTests(KafkaContainerFixture fixture)
    : IClassFixture<KafkaContainerFixture>
{
    /// <summary>
    /// Verifies that a message passing FluentValidation rules is delivered to the consumer.
    /// </summary>
    [Fact]
    public async Task GivenValidMessage_WhenConsumedWithFluentValidation_ThenDelivered()
    {
        // Arrange
        var topic = $"test-fv-valid-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddScoped<IValidator<string>, StringContentValidator>();
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
                            t.UseUtf8Serialization();

                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.ValidateWithFluentValidation(a => a.Discard());
                                group.OnError(e => e.Default(d => d.Discard()));
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
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "valid:hello"));

            // Assert
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("valid:hello", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a message failing FluentValidation rules is discarded and never delivered
    /// to the consumer. A sentinel valid message confirms the consumer is alive and processing.
    /// </summary>
    [Fact]
    public async Task GivenInvalidMessage_WhenConsumedWithFluentValidation_ThenDiscarded()
    {
        // Arrange
        var topic = $"test-fv-discard-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddScoped<IValidator<string>, StringContentValidator>();
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
                            t.UseUtf8Serialization();

                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.ValidateWithFluentValidation(a => a.Discard());
                                group.OnError(e => e.Default(d => d.Discard()));
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
            // Act — produce an invalid message, then a sentinel valid message.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "bad"));
            await producer.ProduceAsync(new EventMessage<string, string>("k", "valid:sentinel"));

            // Assert — only the sentinel arrives; the invalid message was discarded.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("valid:sentinel", ctx.Message);
            Assert.Single(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a message failing FluentValidation rules is routed to the dead-letter topic
    /// when the error action is configured to dead-letter.
    /// </summary>
    [Fact]
    public async Task GivenInvalidMessage_WhenConsumedWithFluentValidationDeadLetter_ThenDeadLettered()
    {
        // Arrange
        var sourceTopic = $"test-fv-dlq-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-fv-dlq-dlt-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddScoped<IValidator<string>, StringContentValidator>();
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

                        kafka.Topic<string, string>(sourceTopic, t =>
                        {
                            t.UseUtf8Serialization();

                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
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
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "bad-message"));

            // Assert — invalid message arrives in the DLQ as raw bytes.
            var ctx = await dlqSink.WaitForMessageAsync();
            Assert.Equal("bad-message", Encoding.UTF8.GetString(ctx.Message));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a Kafka tombstone (non-null key, null value bytes) arriving at a consumer
    /// group with <c>ValidateWithFluentValidation</c> and default <c>allowNullMessages: false</c>
    /// is dead-lettered as a validation failure, while surrounding non-null records reach the handler.
    /// </summary>
    [Fact]
    public async Task GivenSingleConsumerDefaultBehavior_WhenTombstoneConsumed_ThenDeadLettered()
    {
        // Arrange
        var sourceTopic = $"test-fv-tomb-single-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var dlqTopic = $"test-fv-tomb-single-dlq-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlq-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(dlqSink);
                services.AddScoped<IValidator<string>, StringContentValidator>();
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

                        kafka.Topic<string, string>(sourceTopic, t =>
                        {
                            t.UseUtf8Serialization();
                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
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
            // Act — produce tombstone, then a valid non-null sentinel
            await ProduceTombstoneAsync(sourceTopic, "key-tombstone");

            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("key-valid", "valid:sentinel"));

            // Assert — valid message reaches handler
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("valid:sentinel", ctx.Message);
            Assert.Single(sink.ReceivedMessages);

            // Assert — tombstone was dead-lettered (not silently dropped)
            await TestHelpers.WaitUntilAsync(
                () => dlqSink.ReceivedMessages.Count >= 1,
                "Expected tombstone to be dead-lettered",
                TimeSpan.FromSeconds(30));

            Assert.Single(dlqSink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a single consumer with <c>ValidateWithFluentValidation(allowNullMessages: true)</c>
    /// passes null tombstone records through to the handler without dead-lettering them.
    /// </summary>
    [Fact]
    public async Task GivenSingleConsumerAllowNullMessages_WhenTombstoneConsumed_ThenHandlerReceivesNull()
    {
        // Arrange
        var sourceTopic = $"test-fv-allownull-single-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddScoped<IValidator<string>, StringContentValidator>();
                services.AddEmit(emit =>
                {
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config =>
                        {
                            config.BootstrapServers = fixture.BootstrapServers;
                        });
                        kafka.AutoProvision();

                        kafka.Topic<string, string>(sourceTopic, t =>
                        {
                            t.UseUtf8Serialization();
                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.ValidateWithFluentValidation(a => a.Discard(), allowNullMessages: true);
                                group.OnError(e => e.Default(d => d.Discard()));
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
            // Act — produce tombstone only; the handler must receive it as null
            await ProduceTombstoneAsync(sourceTopic, "key-tombstone");

            // Assert — null message arrives at the handler
            var ctx = await sink.WaitForMessageAsync();
            Assert.Null(ctx.Message);
            Assert.Single(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Regression test for the entire-batch DLQ bug: with a batch consumer and default
    /// <c>allowNullMessages: false</c>, a null item in a batch is individually dead-lettered
    /// while sibling non-null items in the same batch reach the handler.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumerDefaultBehavior_WhenBatchContainsTombstone_ThenNullDeadLetteredSiblingsReachHandler()
    {
        // Arrange
        var sourceTopic = $"test-fv-tomb-batch-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var dlqTopic = $"test-fv-tomb-batch-dlq-{Guid.NewGuid():N}";
        var dlqGroupId = $"group-dlq-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();
        var dlqSink = new MessageSink<byte[]>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(dlqSink);
                services.AddScoped<IValidator<string>, StringContentValidator>();
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

                        kafka.Topic<string, string>(sourceTopic, t =>
                        {
                            t.UseUtf8Serialization();
                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.ValidateWithFluentValidation(a => a.DeadLetter());
                                group.OnError(e => e.Default(d => d.DeadLetter()));
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
            // Act — produce tombstone and a non-null sibling in the same window
            await ProduceTombstoneAsync(sourceTopic, "key-tombstone");

            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("key-valid", "valid:sentinel"));

            // Assert — sibling reaches handler
            await TestHelpers.WaitUntilAsync(
                () => sink.Messages.Count >= 1,
                "Expected non-null sibling to reach batch handler",
                TimeSpan.FromSeconds(30));

            Assert.Contains("valid:sentinel", sink.Messages);
            Assert.DoesNotContain(null, sink.Messages);

            // Assert — tombstone is individually dead-lettered, NOT the entire batch
            await TestHelpers.WaitUntilAsync(
                () => dlqSink.ReceivedMessages.Count >= 1,
                "Expected tombstone to be dead-lettered individually",
                TimeSpan.FromSeconds(30));

            Assert.Single(dlqSink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a batch consumer with <c>ValidateWithFluentValidation(allowNullMessages: true)</c>
    /// passes null tombstone items through to the handler alongside non-null items in the same batch.
    /// </summary>
    [Fact]
    public async Task GivenBatchConsumerAllowNullMessages_WhenBatchContainsTombstone_ThenHandlerReceivesNullItems()
    {
        // Arrange
        var sourceTopic = $"test-fv-allownull-batch-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddScoped<IValidator<string>, StringContentValidator>();
                services.AddEmit(emit =>
                {
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config =>
                        {
                            config.BootstrapServers = fixture.BootstrapServers;
                        });
                        kafka.AutoProvision();

                        kafka.Topic<string, string>(sourceTopic, t =>
                        {
                            t.UseUtf8Serialization();
                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.ValidateWithFluentValidation(a => a.Discard(), allowNullMessages: true);
                                group.OnError(e => e.Default(d => d.Discard()));
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
            // Act — produce tombstone then a non-null message in the same window
            await ProduceTombstoneAsync(sourceTopic, "key-tombstone");

            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("key-valid", "valid:hello"));

            // Assert — both null and non-null messages reach the handler
            await TestHelpers.WaitUntilAsync(
                () => sink.Messages.Count >= 2,
                "Expected both tombstone (null) and non-null message to reach batch handler",
                TimeSpan.FromSeconds(30));

            Assert.Contains("valid:hello", sink.Messages);
            Assert.Contains(null, sink.Messages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

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
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);

        await rawProducer.ProduceAsync(topic, new ConfluentKafka.Message<byte[], byte[]>
        {
            Key = keyBytes,
            Value = null!, // tombstone
        });

        rawProducer.Flush(TimeSpan.FromSeconds(5));
    }

    private sealed class StringContentValidator : AbstractValidator<string>
    {
        public StringContentValidator()
        {
            RuleFor(x => x).Must(x => x.StartsWith("valid:", StringComparison.Ordinal))
                .WithMessage("Message must start with 'valid:'");
        }
    }

}
