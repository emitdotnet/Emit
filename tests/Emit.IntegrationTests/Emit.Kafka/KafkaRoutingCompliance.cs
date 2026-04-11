namespace Emit.Kafka.Tests;

using Emit.Abstractions;
using Emit.Abstractions.ErrorHandling;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Routing;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaRoutingCompliance(KafkaContainerFixture fixture)
    : RoutingCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureWithRouter(EmitBuilder emit, string topic, string groupId)
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
                    group.OnError(e => e
                        .WhenRouteUnmatched(d => d.Discard())
                        .Default(d => d.Discard()));
                    group.AddRouter<string>(
                        "value-router",
                        ctx => ctx.Message,
                        routes =>
                        {
                            routes.Route<ConsumerA>("route-a");
                            routes.Route<ConsumerB>("route-b");
                        });
                });
            });
        });
    }

    protected override void ConfigureWithTwoRouters(EmitBuilder emit, string topic, string groupId)
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
                    group.OnError(e => e
                        .WhenRouteUnmatched(d => d.Discard())
                        .Default(d => d.Discard()));
                    group.AddRouter<string>(
                        "router-one",
                        ctx => ctx.Message,
                        routes =>
                        {
                            routes.Route<ConsumerA>("router-one-a");
                        });
                    group.AddRouter<string>(
                        "router-two",
                        ctx => ctx.Message,
                        routes =>
                        {
                            routes.Route<ConsumerB>("router-two-b");
                        });
                });
            });
        });
    }
}
