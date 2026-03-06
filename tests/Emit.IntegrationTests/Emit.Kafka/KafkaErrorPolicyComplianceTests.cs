namespace Emit.Kafka.Tests;

using Confluent.Kafka.Admin;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaErrorPolicyComplianceTests(KafkaContainerFixture fixture)
    : ErrorPolicyCompliance, IClassFixture<KafkaContainerFixture>
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

            // Source topic — producer + consumer with typed exception clause.
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
                    group.OnError(e => e
                        .When<ArgumentException>(a => a.DeadLetter(dlqTopic))
                        .Default(a => a.Discard()));
                    group.AddConsumer<TypedExceptionConsumer>();
                });
            });

            // DLQ topic — consumer only; captures dead-lettered messages.
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

    protected override void ConfigureEmitWithPredicateWhen(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId)
    {
        // DLQ topic must exist before the source consumer starts.
        CreateTopic(dlqTopic);

        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });

            // Source topic — producer + consumer that always throws; predicate filters by message content.
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
                    group.OnError(e => e
                        .When<InvalidOperationException>(
                            ex => ex.Message.Contains("MATCH"),
                            a => a.DeadLetter(dlqTopic))
                        .Default(a => a.Discard()));
                    group.AddConsumer<PredicateThrowingConsumer>();
                });
            });

            // DLQ topic — consumer only.
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
