namespace Emit.Kafka.Tests;

using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaOffsetCommitCompliance(KafkaContainerFixture fixture)
    : OffsetCommitCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureSimpleConsumer(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan commitInterval)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });

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
                    group.CommitInterval = commitInterval;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    protected override void ConfigureHangingConsumer(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan commitInterval,
        TimeSpan workerStopTimeout)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });

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
                    group.CommitInterval = commitInterval;
                    group.WorkerStopTimeout = workerStopTimeout;
                    group.AddConsumer<HangingConsumer>();
                });
            });
        });
    }

    protected override void ConfigureSpeedConsumer(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan commitInterval)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });

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
                    group.CommitInterval = commitInterval;
                    group.WorkerCount = 2;
                    group.WorkerDistribution = WorkerDistribution.RoundRobin;
                    group.AddConsumer<SpeedConsumer>();
                });
            });
        });
    }
}
