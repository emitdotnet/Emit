namespace Emit.Kafka.Tests;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaFanOutCompliance(KafkaContainerFixture fixture)
    : FanOutCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureFanOut(EmitBuilder emit, string topic, string groupId)
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
                    group.AddConsumer<ConsumerA>();
                    group.AddConsumer<ConsumerB>();
                });
            });
        });
    }
}
