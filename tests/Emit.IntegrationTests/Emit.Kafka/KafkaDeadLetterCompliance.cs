namespace Emit.Kafka.Tests;

using Emit.Abstractions.ErrorHandling;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaDeadLetterCompliance(KafkaContainerFixture fixture)
    : DeadLetterCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureEmit(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId)
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

            // Source topic — producer + consumer that always fails, dead-lettering to dlqTopic.
            kafka.Topic<string, string>(sourceTopic, t =>
            {
                t.UseUtf8Serialization();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.OnError(e => e.Default(d => d.DeadLetter()));
                    group.AddConsumer<AlwaysFailingConsumer>();
                });
            });
        });
    }

    protected override void ConfigureEmitWithKeyCapture(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId)
    {
        // Identical configuration — key bytes are preserved regardless of the test scenario.
        ConfigureEmit(emit, sourceTopic, groupId, dlqTopic, dlqGroupId);
    }
}
