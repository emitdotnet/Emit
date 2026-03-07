namespace Emit.Kafka.Tests;

using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaRateLimiterCompliance(KafkaContainerFixture fixture)
    : RateLimiterCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureWithRateLimit(
        EmitBuilder emit,
        string topic,
        string groupId,
        int permitsPerWindow,
        TimeSpan window)
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
                    group.WorkerCount = 1;
                    group.RateLimit(rl => rl.FixedWindow(permitsPerWindow, window));
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }
}
