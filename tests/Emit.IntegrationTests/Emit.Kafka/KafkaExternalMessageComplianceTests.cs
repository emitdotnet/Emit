namespace Emit.Kafka.Tests;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaExternalMessageComplianceTests(KafkaContainerFixture fixture)
    : ExternalMessageCompliance, IClassFixture<KafkaContainerFixture>
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
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.ConsumerGroup($"test-group-{Guid.NewGuid():N}", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    protected override async Task ProduceExternalMessageAsync(string topic, string key, string value)
    {
        var producerConfig = new ConfluentKafka.ProducerConfig
        {
            BootstrapServers = fixture.BootstrapServers,
        };
        using var producer = new ConfluentKafka.ProducerBuilder<string, string>(producerConfig).Build();
        await producer.ProduceAsync(topic, new ConfluentKafka.Message<string, string>
        {
            Key = key,
            Value = value,
        });
    }
}
