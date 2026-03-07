namespace Emit.Kafka.Tests;

using Emit.Abstractions.ErrorHandling;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public class KafkaConsumeObserverCompliance(KafkaContainerFixture fixture)
    : ConsumeObserverCompliance, IClassFixture<KafkaContainerFixture>
{
    protected override void ConfigureWithObserverAndSucceedingConsumer(
        EmitBuilder emit,
        string topic,
        string groupId)
    {
        // Forward the pre-registered singleton instance to IConsumeObserver.
        emit.Services.AddSingleton<Emit.Abstractions.Observability.IConsumeObserver>(
            sp => sp.GetRequiredService<TrackingConsumeObserver>());

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
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    protected override void ConfigureWithObserverAndFailingConsumer(
        EmitBuilder emit,
        string topic,
        string groupId)
    {
        // Forward the pre-registered singleton instance to IConsumeObserver.
        emit.Services.AddSingleton<Emit.Abstractions.Observability.IConsumeObserver>(
            sp => sp.GetRequiredService<TrackingConsumeObserver>());

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
                    group.OnError(e => e.Default(d => d.Discard()));
                    group.AddConsumer<AlwaysFailingConsumer>();
                });
            });
        });
    }
}
