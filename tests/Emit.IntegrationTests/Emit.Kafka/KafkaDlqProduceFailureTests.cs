namespace Emit.Kafka.Tests;

using Confluent.Kafka.Admin;
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
/// Kafka-specific tests verifying that a failure to produce to the DLQ does not crash
/// the source consumer pipeline. The source consumer continues processing subsequent messages.
/// </summary>
[Trait("Category", "Integration")]
public class KafkaDlqProduceFailureTests(KafkaContainerFixture fixture)
    : IClassFixture<KafkaContainerFixture>
{
    /// <summary>
    /// Verifies that when the DLQ topic is deleted after startup (so produce fails), the source
    /// consumer pipeline continues processing subsequent messages that succeed.
    /// The failed message is discarded (DLQ produce exception is caught and logged by the worker).
    /// </summary>
    [Fact]
    public async Task GivenDlqTopicDeletedAfterStartup_WhenMessageDeadLettered_ThenPipelineContinues()
    {
        // Arrange
        var sourceTopic = $"test-dlq-fail-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = $"test-dlq-fail-dlt-{Guid.NewGuid():N}";
        var successSink = new MessageSink<string>();
        var failToggle = new FailToggle();

        // Create the DLQ topic so DlqTopicVerifier passes at startup.
        CreateTopic(dlqTopic);

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(successSink);
                services.AddSingleton(failToggle);
                services.AddEmit(emit =>
                {
                    emit.AddKafka(kafka =>
                    {
                        kafka.ConfigureClient(config =>
                        {
                            config.BootstrapServers = fixture.BootstrapServers;
                        });

                        kafka.Topic<string, string>(sourceTopic, t =>
                        {
                            t.SetKeySerializer(ConfluentKafka.Serializers.Utf8);
                            t.SetValueSerializer(ConfluentKafka.Serializers.Utf8);
                            t.SetKeyDeserializer(ConfluentKafka.Deserializers.Utf8);
                            t.SetValueDeserializer(ConfluentKafka.Deserializers.Utf8);

                            t.Producer();
                            t.ConsumerGroup(groupId, group =>
                            {
                                group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                                group.OnError(e => e.Default(d => d.DeadLetter(dlqTopic)));
                                group.AddConsumer<ToggleableSuccessConsumer>();
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

            // Act — produce a message that succeeds first, to confirm the consumer is running.
            failToggle.ShouldFail = false;
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "before-dlq-failure"));
            var first = await successSink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("before-dlq-failure", first.Message);

            // Act — delete the DLQ topic so any DLQ produce attempt will fail.
            DeleteTopic(dlqTopic);

            // Act — produce a message that always fails → triggers dead letter → DLQ produce fails.
            // The worker catches the exception from DLQ produce, logs, and continues.
            failToggle.ShouldFail = true;
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "triggers-dlq-failure"));

            // Allow time for the failing message to be processed (DLQ produce will fail and be discarded).
            await Task.Delay(TimeSpan.FromSeconds(5));

            // Act — produce a final message that succeeds; proves pipeline is still alive.
            failToggle.ShouldFail = false;
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "after-dlq-failure"));

            // Assert — the final message is delivered; the pipeline survived the DLQ produce failure.
            InboundContext<string> ctx;
            do
            {
                ctx = await successSink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            }
            while (ctx.Message != "after-dlq-failure");

            Assert.Equal("after-dlq-failure", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Controls whether <see cref="ToggleableSuccessConsumer"/> throws.
    /// </summary>
    public sealed class FailToggle
    {
        /// <summary>When <see langword="true"/>, the consumer throws.</summary>
        public volatile bool ShouldFail;
    }

    /// <summary>
    /// Consumer that succeeds or throws based on <see cref="FailToggle"/>.
    /// </summary>
    private sealed class ToggleableSuccessConsumer(MessageSink<string> sink, FailToggle failToggle) : IConsumer<string>
    {
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
        {
            if (failToggle.ShouldFail)
            {
                throw new InvalidOperationException("Simulated consumer failure to trigger DLQ.");
            }

            return sink.WriteAsync(context, cancellationToken);
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

    private void DeleteTopic(string topicName)
    {
        using var adminClient = new ConfluentKafka.AdminClientBuilder(
            new ConfluentKafka.AdminClientConfig { BootstrapServers = fixture.BootstrapServers })
            .Build();

        try
        {
            adminClient.DeleteTopicsAsync([topicName]).GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort — if deletion fails the test may behave differently but won't crash.
        }
    }
}
