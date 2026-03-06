namespace Emit.Kafka.Tests;

using Confluent.Kafka.Admin;
using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaValidationComplianceTests(KafkaContainerFixture fixture)
    : ValidationCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureWithValidationDiscard(EmitBuilder emit, string topic, string groupId)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });

            kafka.Topic<string, string>(topic, t =>
            {
                t.SetKeySerializer(ConfluentKafka.Serializers.Utf8);
                t.SetValueSerializer(ConfluentKafka.Serializers.Utf8);
                t.SetKeyDeserializer(ConfluentKafka.Deserializers.Utf8);
                t.SetValueDeserializer(ConfluentKafka.Deserializers.Utf8);

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.Validate(
                        msg => msg.StartsWith("valid:", StringComparison.Ordinal)
                            ? MessageValidationResult.Success
                            : MessageValidationResult.Fail("invalid"),
                        e => e.Discard());
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    protected override void ConfigureWithValidationDeadLetter(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId)
    {
        CreateTopic(dlqTopic);

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
                    group.Validate(
                        msg => msg.StartsWith("valid:", StringComparison.Ordinal)
                            ? MessageValidationResult.Success
                            : MessageValidationResult.Fail("invalid"),
                        e => e.DeadLetter(dlqTopic));
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });

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

    protected override void ConfigureWithThrowingValidatorAndDeadLetter(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId)
    {
        CreateTopic(dlqTopic);

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
                    group.Validate(
                        (_, _) => throw new InvalidOperationException("Simulated validator exception."),
                        e => e.Discard());
                    group.OnError(e => e.Default(d => d.DeadLetter(dlqTopic)));
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });

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
