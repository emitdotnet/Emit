namespace Emit.Kafka.Tests;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaProduceObserverCompliance(KafkaContainerFixture fixture)
    : ProduceObserverCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureWithProduceObserver(EmitBuilder emit, string topic)
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

                t.Producer();
            });
        });
    }
}
