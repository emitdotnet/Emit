namespace Emit.Kafka.Tests;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaProduceConsumeCompliance(KafkaContainerFixture fixture)
    : ProduceConsumeCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureEmit(EmitBuilder emit, string topic)
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
                t.ConsumerGroup($"test-group-{Guid.NewGuid():N}", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }
}
