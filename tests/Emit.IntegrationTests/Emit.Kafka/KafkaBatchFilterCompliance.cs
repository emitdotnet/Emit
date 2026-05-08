namespace Emit.Kafka.Tests;

using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

[Trait("Category", "Integration")]
public class KafkaBatchFilterCompliance(KafkaContainerFixture fixture)
    : BatchFilterCompliance, IClassFixture<KafkaContainerFixture>
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
                    group.AddBatchConsumer<BatchSinkConsumer<string>>(opts =>
                    {
                        opts.MaxSize = 10;
                        opts.Timeout = TimeSpan.FromSeconds(2);
                    });
                });
            });
        });
    }

    protected override void ConfigureWithMultipleFilters(EmitBuilder emit, string topic, string groupId)
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
                    group.Filter<PrefixF1Filter>();
                    group.Filter<PrefixF2Filter>();
                    group.AddBatchConsumer<BatchSinkConsumer<string>>(opts =>
                    {
                        opts.MaxSize = 10;
                        opts.Timeout = TimeSpan.FromSeconds(2);
                    });
                });
            });
        });
    }
}
