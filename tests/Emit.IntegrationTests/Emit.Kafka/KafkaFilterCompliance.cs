namespace Emit.Kafka.Tests;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaFilterCompliance(KafkaContainerFixture fixture)
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
            kafka.AutoProvision();

            kafka.Topic<string, string>(topic, t =>
            {
                t.UseUtf8Serialization();

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

    protected override void ConfigureWithFilterAndRetry(
        EmitBuilder emit,
        string topic,
        string groupId,
        Func<string, bool> predicate)
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
                    group.Filter((context, _) => ValueTask.FromResult(predicate(context.Message)));
                    group.OnError(e => e.Default(d => d.Retry(3, Backoff.None).Discard()));
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }
}
