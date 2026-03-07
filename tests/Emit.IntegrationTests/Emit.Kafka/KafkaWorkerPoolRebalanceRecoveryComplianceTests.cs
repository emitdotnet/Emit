namespace Emit.Kafka.Tests;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaWorkerPoolRebalanceRecoveryComplianceTests(KafkaContainerFixture fixture)
    : WorkerPoolRebalanceRecoveryCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        int bufferSize,
        TimeSpan maxPollInterval,
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
                t.ConsumerGroup($"test-rebalance-group-{Guid.NewGuid():N}", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.BufferSize = bufferSize;
                    group.MaxPollInterval = maxPollInterval;

                    // session.timeout.ms must be >= group.min.session.timeout.ms (6 s default in cp-kafka).
                    // Heartbeat interval must be < session.timeout.ms.
                    group.SessionTimeout = TimeSpan.FromSeconds(10);
                    group.HeartbeatInterval = TimeSpan.FromSeconds(3);

                    group.WorkerStopTimeout = workerStopTimeout;
                    group.AddConsumer<HangingConsumer>();
                });
            });
        });
    }
}
