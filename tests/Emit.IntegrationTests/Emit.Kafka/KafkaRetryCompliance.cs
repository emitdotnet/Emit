namespace Emit.Kafka.Tests;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Observability;
using Emit.Kafka.Tests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
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
                t.UseUtf8Serialization();

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
                t.UseUtf8Serialization();

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

    protected override void ConfigureWithLongRetries(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan maxPollInterval)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config => config.BootstrapServers = fixture.BootstrapServers);
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.UseUtf8Serialization();

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.BufferSize = 1;

                    // The broker's smallest session timeout is six seconds, and it must not exceed the poll interval
                    group.MaxPollInterval = maxPollInterval;
                    group.SessionTimeout = TimeSpan.FromSeconds(6);
                    group.HeartbeatInterval = TimeSpan.FromSeconds(2);
                    group.OnError(e => e.Default(d => d.Retry(1000, Backoff.Fixed(TimeSpan.FromMilliseconds(200))).Discard()));
                    group.AddConsumer<RetryUntilRecoveredConsumer>();
                });
            });
        });
    }

    protected override void TrackPartitionsTaken(IServiceCollection services, InvocationCounter rebalances) =>
        services.AddSingleton<IKafkaConsumerObserver>(new PartitionsTakenObserver(rebalances));

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
                t.UseUtf8Serialization();

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

    private sealed class PartitionsTakenObserver(InvocationCounter rebalances) : IKafkaConsumerObserver
    {
        public Task OnPartitionsRevokedAsync(PartitionsRevokedEvent e)
        {
            rebalances.Increment();
            return Task.CompletedTask;
        }

        public Task OnPartitionsLostAsync(PartitionsLostEvent e)
        {
            rebalances.Increment();
            return Task.CompletedTask;
        }
    }
}
