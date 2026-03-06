namespace Emit.Kafka.Tests;

using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaPerConsumerMiddlewareComplianceTests(KafkaContainerFixture fixture)
    : PerConsumerMiddlewareCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureWithPerConsumerMiddleware(EmitBuilder emit, string topic, string groupId)
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
                    group.AddConsumer<ConsumerWithMiddleware>(handler =>
                    {
                        handler.Use<PerConsumerCounterMiddleware>(MiddlewareLifetime.Singleton);
                    });
                    group.AddConsumer<ConsumerWithoutMiddleware>();
                });
            });
        });
    }
}
