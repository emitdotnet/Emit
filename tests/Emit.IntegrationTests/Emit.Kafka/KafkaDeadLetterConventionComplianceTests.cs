namespace Emit.Kafka.Tests;

using Confluent.Kafka.Admin;
using Emit.Abstractions.ErrorHandling;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaDeadLetterConventionComplianceTests(KafkaContainerFixture fixture)
    : DeadLetterConventionCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureWithDlqConvention(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId)
    {
        // DLQ topic must exist before host starts (DlqTopicVerifier checks at startup).
        CreateTopic(dlqTopic);

        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });

            // Register the naming convention: source + ".dlt"
            kafka.DeadLetter(opt => opt.TopicNamingConvention = src => src + ".dlt");

            // Source topic — producer + always-failing consumer, DLQ via convention (no explicit topic).
            kafka.Topic<string, string>(sourceTopic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.OnError(e => e.Default(d => d.DeadLetter()));
                    group.AddConsumer<AlwaysFailingConsumer>();
                });
            });

            // DLQ topic — consumer only.
            kafka.Topic<string, string>(dlqTopic, t =>
            {
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

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
