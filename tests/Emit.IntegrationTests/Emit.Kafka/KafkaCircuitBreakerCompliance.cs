namespace Emit.Kafka.Tests;

using Emit.Abstractions;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration;
using Emit.IntegrationTests.Integration.Compliance;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Testing;
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

    /// <inheritdoc />
    protected override void ConfigureEmitWithTwoTopics(
        EmitBuilder emit,
        string groupId,
        string failingTopic,
        string healthyTopic,
        Action<CircuitBreakerBuilder> configureCircuitBreaker)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config => config.BootstrapServers = fixture.BootstrapServers);
            kafka.AutoProvision();
            kafka.Topic<string, string>(failingTopic, t =>
            {
                t.UseUtf8Serialization();
                t.Producer();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.CircuitBreaker(configureCircuitBreaker);
                    group.AddConsumer<AlwaysFailingConsumer>();
                });
            });
            kafka.Topic<string, string>(healthyTopic, t =>
            {
                t.UseUtf8Serialization();
                t.ConsumerGroup(groupId, group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.CircuitBreaker(configureCircuitBreaker);
                    group.AddConsumer<SinkConsumer<string>>();
                });
            });
        });
    }

    /// <inheritdoc />
    protected override void ConfigureEmit(EmitBuilder emit, CircuitBreakerGroup group)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config => config.BootstrapServers = fixture.BootstrapServers);
            kafka.AutoProvision();
            kafka.Topic<string, string>(group.Topic, t =>
            {
                t.UseUtf8Serialization();
                t.Producer();
                t.ConsumerGroup($"test-cb-group-{Guid.NewGuid():N}", consumers =>
                {
                    consumers.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    consumers.CircuitBreaker(group.CircuitBreaker);
                    if (group.Retries is { } retries)
                    {
                        consumers.OnError(e => e.Default(d => d.Retry(retries, Backoff.None).Discard()));
                    }

                    consumers.AddConsumer<CountingFailingConsumer>();
                });
            });
        });
    }
}
