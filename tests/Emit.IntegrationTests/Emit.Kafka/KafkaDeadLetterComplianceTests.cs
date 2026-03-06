namespace Emit.Kafka.Tests;

using Confluent.Kafka.Admin;
using Emit.Abstractions.ErrorHandling;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaDeadLetterComplianceTests(KafkaContainerFixture fixture)
    : DeadLetterCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureEmit(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId)
    {
        // DLQ topic must exist before the source consumer starts (DlqTopicVerifier requires it).
        CreateTopic(dlqTopic);

        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });

            // Source topic — producer + consumer that always fails, dead-lettering to dlqTopic.
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
                    group.AddConsumer<AlwaysFailingConsumer>();
                });
            });

            // DLQ topic — consumer only; no producer registration needed (DlqProducer uses raw bytes).
            kafka.Topic<string, string>(dlqTopic, t =>
            {
                t.SetKeyDeserializer(ConfluentKafka.Deserializers.Utf8);
                t.SetValueDeserializer(ConfluentKafka.Deserializers.Utf8);

                t.ConsumerGroup(dlqGroupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<DlqCaptureConsumer>();
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

    private void CreateTopic(string topicName)
    {
        using var adminClient = new ConfluentKafka.AdminClientBuilder(
            new ConfluentKafka.AdminClientConfig { BootstrapServers = fixture.BootstrapServers })
            .Build();

        adminClient.CreateTopicsAsync(
            [new TopicSpecification { Name = topicName, ReplicationFactor = 1, NumPartitions = 1 }])
            .GetAwaiter().GetResult();
    }
}
