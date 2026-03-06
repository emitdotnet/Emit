namespace Emit.Kafka.Tests;

using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaWorkerDistributionComplianceTests(KafkaContainerFixture fixture)
    : WorkerDistributionCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureWithByKeyHash(
        EmitBuilder emit,
        string topic,
        string groupId,
        int workerCount)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = fixture.BootstrapServers;
            });

            kafka.Topic<byte[], string>(topic, t =>
            {
                t.SetKeySerializer(ConfluentKafka.Serializers.ByteArray);
                t.SetValueSerializer(ConfluentKafka.Serializers.Utf8);
                t.SetKeyDeserializer(ConfluentKafka.Deserializers.ByteArray);
                t.SetValueDeserializer(ConfluentKafka.Deserializers.Utf8);

                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.WorkerCount = workerCount;
                    group.WorkerDistribution = WorkerDistribution.ByKeyHash;
                    group.AddConsumer<OrderTrackingConsumer>();
                });
            });
        });
    }
}
