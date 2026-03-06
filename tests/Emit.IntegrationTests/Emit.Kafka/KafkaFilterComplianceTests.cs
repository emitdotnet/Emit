namespace Emit.Kafka.Tests;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaFilterComplianceTests(KafkaContainerFixture fixture)
    : FilterCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureWithFilter(EmitBuilder emit, string topic, string groupId)
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
                    group.Filter<PrefixFilter>();
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }
}
