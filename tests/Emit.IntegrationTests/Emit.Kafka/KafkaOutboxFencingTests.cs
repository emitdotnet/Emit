namespace Emit.Kafka.Tests;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Kafka.Tests.TestInfrastructure;
using Emit.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;
using ConfluentKafkaAdmin = Confluent.Kafka.Admin;

/// <summary>
/// Integration tests for the fencing of an earlier run of the outbox daemon in Kafka.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KafkaOutboxFencingTests(KafkaContainerFixture kafkaFixture) : IClassFixture<KafkaContainerFixture>, IAsyncLifetime
{
    private static readonly byte[] Key = [1];

    private readonly string topic = $"test-outbox-fencing-{Guid.NewGuid():N}";

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        using var admin = new ConfluentKafka.AdminClientBuilder(
            new ConfluentKafka.AdminClientConfig { BootstrapServers = kafkaFixture.BootstrapServers }).Build();
        await admin.CreateTopicsAsync([new ConfluentKafkaAdmin.TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
    }

    /// <inheritdoc/>
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenRunOfTheOutboxDaemonStillSending_WhenALaterRunBeginsDelivering_ThenNothingTheEarlierRunSendsBecomesVisible()
    {
        // Arrange: a run paused with a send in its transaction, as after a long garbage collection, while the leader
        // gave the outbox to another node whose run of the daemon now delivers
        var fencingKey = Guid.NewGuid();
        using var pausedNode = BuildServices();
        using var currentNode = BuildServices();
        await using var paused = await pausedNode.GetRequiredService<IOutboxProvider>().BeginDeliveryAsync(new OutboxDeliveryContext(fencingKey, 1));
        await paused.SendAsync(Entry("sent by the paused run"));

        // Act
        await using (var current = await currentNode.GetRequiredService<IOutboxProvider>().BeginDeliveryAsync(new OutboxDeliveryContext(fencingKey, 2)))
        {
            await current.SendAsync(Entry("sent by the run that replaced it"));
            await current.CompleteAsync();
        }

        // Assert: the paused run resumes, and nothing it sent, or sends, becomes visible
        await Assert.ThrowsAnyAsync<ConfluentKafka.KafkaException>(async () =>
        {
            await paused.SendAsync(Entry("sent by the paused run after it resumed"));
            await paused.CompleteAsync();
        });
        Assert.Equal(["sent by the run that replaced it"], ReadCommitted());
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmit(emit => emit.AddKafka(kafka => kafka.ConfigureClient(config => config.BootstrapServers = kafkaFixture.BootstrapServers)));
        return services.BuildServiceProvider();
    }

    private OutboxEntry Entry(string value) =>
        KafkaBuilder.CreateOutboxEntry(
            $"kafka://localhost/{topic}", topic, typeof(byte[]), typeof(string), Key, System.Text.Encoding.UTF8.GetBytes(value), headers: null);

    /// <summary>
    /// Reads every message of the topic as a consumer that reads committed messages does, the default of the
    /// Kafka client.
    /// </summary>
    private List<string> ReadCommitted()
    {
        using var consumer = new ConfluentKafka.ConsumerBuilder<byte[], byte[]>(new ConfluentKafka.ConsumerConfig
        {
            BootstrapServers = kafkaFixture.BootstrapServers,
            GroupId = $"group-{Guid.NewGuid():N}",
            AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest,
            EnablePartitionEof = true,
        }).Build();
        consumer.Subscribe(topic);

        List<string> values = [];
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromSeconds(1));
            if (result is null)
            {
                continue;
            }

            if (result.IsPartitionEOF)
            {
                return values;
            }

            values.Add(System.Text.Encoding.UTF8.GetString(result.Message.Value));
        }

        throw new TimeoutException("The topic was not read to its end within timeout.");
    }
}
