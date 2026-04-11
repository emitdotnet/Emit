namespace Emit.Kafka.Tests;

using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaCircuitBreakerCompliance(KafkaContainerFixture fixture)
    : CircuitBreakerCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        Action<CircuitBreakerBuilder> configureCircuitBreaker)
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
                t.ConsumerGroup($"test-cb-group-{Guid.NewGuid():N}", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.CircuitBreaker(configureCircuitBreaker);
                    group.AddConsumer<ToggleableConsumer>();
                });
            });
        });
    }
}
