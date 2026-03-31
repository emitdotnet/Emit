namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Kafka.DependencyInjection;
using Emit.Models;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

/// <summary>
/// Compliance tests verifying that custom headers set on an outbox-produced message are
/// preserved and available in the consumer pipeline after end-to-end delivery through the outbox.
/// Derived classes configure a persistence provider; Kafka configuration is handled by the base class.
/// </summary>
[Trait("Category", "Integration")]
public abstract class OutboxHeadersCompliance : IAsyncLifetime
{
    private const string CorrelationIdHeaderKey = "x-correlation-id";

    /// <summary>
    /// Gets the Kafka bootstrap servers address for producing and consuming messages.
    /// </summary>
    protected abstract string BootstrapServers { get; }

    /// <summary>
    /// Configures the persistence provider (MongoDB or EF Core) with outbox enabled.
    /// Kafka configuration is handled by the base class.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="pollingInterval">The outbox daemon polling interval.</param>
    protected abstract void ConfigurePersistence(EmitBuilder emit, TimeSpan pollingInterval);

    /// <summary>
    /// Hook for EF Core implementations to call SaveChangesAsync before committing.
    /// MongoDB does not need this. Default implementation does nothing.
    /// </summary>
    protected virtual Task FlushBeforeCommitAsync(IServiceProvider scopedServices)
        => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Verifies that custom headers set at outbox-produce time are available in the consumer
    /// pipeline after the outbox daemon delivers the message.
    /// </summary>
    [Fact]
    public async Task GivenOutboxEntryWithCustomHeaders_WhenDelivered_ThenHeadersPreservedInConsumer()
    {
        // Arrange
        var topic = $"test-outbox-headers-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var correlationId = Guid.NewGuid().ToString("N");

        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — produce via outbox with a custom correlation-id header.
            await ProduceWithHeadersAsync(
                host.Services,
                "k",
                "header-test-payload",
                [new(CorrelationIdHeaderKey, correlationId)]);

            // Assert — message arrives at the consumer.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("header-test-payload", ctx.Message);

            // Assert — the custom header is present in the consumer pipeline context.
            var headers = ctx.Headers;
            Assert.NotNull(headers);

            var receivedCorrelationId = headers
                .FirstOrDefault(h => h.Key == CorrelationIdHeaderKey).Value;

            Assert.NotNull(receivedCorrelationId);
            Assert.Equal(correlationId, receivedCorrelationId);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private async Task ProduceWithHeadersAsync(
        IServiceProvider services,
        string key,
        string value,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var producer = sp.GetRequiredService<IEventProducer<string, string>>();

        await using var transaction = await unitOfWork.BeginAsync(ct);

        await producer.ProduceAsync(new EventMessage<string, string>(key, value, headers), ct);

        await FlushBeforeCommitAsync(sp);
        await transaction.CommitAsync(ct);
    }

    private IHost BuildHost(MessageSink<string> sink, string topic, string groupId, TimeSpan pollingInterval)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit =>
                {
                    ConfigurePersistence(emit, pollingInterval);
                    ConfigureKafka(emit, topic, groupId);
                });
            })
            .Build();
    }

    private void ConfigureKafka(EmitBuilder emit, string topic, string groupId)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config => config.BootstrapServers = BootstrapServers);
            kafka.AutoProvision();

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
}
