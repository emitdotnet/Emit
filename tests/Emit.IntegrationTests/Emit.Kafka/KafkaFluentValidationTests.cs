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

    private sealed class StringContentValidator : AbstractValidator<string>
    {
        public StringContentValidator()
        {
            RuleFor(x => x).Must(x => x.StartsWith("valid:", StringComparison.Ordinal))
                .WithMessage("Message must start with 'valid:'");
        }
    }

}
