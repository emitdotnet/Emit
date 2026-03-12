namespace Emit.Kafka.Tests;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaRetryCompliance(KafkaContainerFixture fixture)
    : RetryCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureWithRetryUntilSuccess(
        EmitBuilder emit,
        string topic,
        string groupId,
        int maxRetries)
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
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.OnError(e => e.Default(d => d.Retry(maxRetries, Backoff.None).Discard()));
                    group.AddConsumer<RetrySucceedingConsumer>();
                });
            });
        });
    }

    protected override void ConfigureWithRetryUntilDeadLetter(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId,
        int maxRetries)
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

            // Source topic — producer + consumer that always fails, retries, then dead-letters.
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
                    group.OnError(e => e.Default(d => d.Retry(maxRetries, Backoff.None).DeadLetter()));
                    group.AddConsumer<AlwaysFailingConsumer>();
                });
            });
        });
    }

    protected override void ConfigureWithRetryUntilDiscard(
        EmitBuilder emit,
        string topic,
        string groupId)
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
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.OnError(e => e.Default(d => d.Retry(2, Backoff.None).Discard()));
                    group.AddConsumer<AlwaysFailingConsumer>();
                });
            });
        });
    }
}
