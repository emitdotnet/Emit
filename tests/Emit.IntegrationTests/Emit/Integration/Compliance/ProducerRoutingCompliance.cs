namespace Emit.IntegrationTests.Integration.Compliance;

using System.Text;
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
/// Compliance tests for producer routing: outbox-by-default when outbox infra is configured,
/// and UseDirect() opt-out for immediate delivery.
/// Derived classes configure a persistence provider; Kafka configuration is handled by the base class.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ProducerRoutingCompliance : IAsyncLifetime
{
    private string? currentOutboxTopic;

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

    [Fact]
    public async Task GivenOutboxInfraEnabled_WhenProducerHasNoDirectFlag_ThenMessageGoesToOutbox()
    {
        // Arrange
        var outboxTopic = $"test-route-outbox-{Guid.NewGuid():N}";
        var directTopic = $"test-route-direct-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var outboxSink = new MessageSink<string>();
        var directSink = new MessageSink<string>();

        var host = BuildHost(outboxSink, directSink, outboxTopic, directTopic, groupId, pollingInterval);
        await host.StartAsync();

        try
        {
            // Act — produce to outbox topic via transaction.
            await ProduceViaOutboxAsync(host.Services, "k", "outbox-msg");

            // Assert — message delivered via outbox daemon.
            var ctx = await outboxSink.WaitForMessageAsync();
            Assert.Equal("outbox-msg", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenOutboxInfraEnabled_WhenProducerUsesUseDirect_ThenMessageGoesDirectToBroker()
    {
        // Arrange
        var outboxTopic = $"test-route-outbox2-{Guid.NewGuid():N}";
        var directTopic = $"test-route-direct2-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(10); // Long interval to prove direct delivery.
        var outboxSink = new MessageSink<string>();
        var directSink = new MessageSink<string>();

        var host = BuildHost(outboxSink, directSink, outboxTopic, directTopic, groupId, pollingInterval);
        await host.StartAsync();

        try
        {
            // Act — produce to direct topic (no transaction needed).
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider
                .GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "direct-msg"))
                ;

            // Assert — delivered immediately (well before outbox polling interval).
            var ctx = await directSink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("direct-msg", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenMixedDirectAndOutboxProducers_WhenBothProduce_ThenDirectDeliveredImmediatelyOutboxDeliveredByDaemon()
    {
        // Arrange
        var outboxTopic = $"test-route-mixed-outbox-{Guid.NewGuid():N}";
        var directTopic = $"test-route-mixed-direct-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var outboxSink = new MessageSink<string>();
        var directSink = new MessageSink<string>();

        var host = BuildHost(outboxSink, directSink, outboxTopic, directTopic, groupId, pollingInterval);
        await host.StartAsync();

        try
        {
            // Act — produce to both topics.
            {
                using var scope = host.Services.CreateScope();
                var producer = scope.ServiceProvider
                    .GetRequiredService<IEventProducer<string, string>>();
                await producer.ProduceAsync(new EventMessage<string, string>("k", "direct-first"))
                    ;
            }

            await ProduceViaOutboxAsync(host.Services, "k", "outbox-second");

            // Assert — both delivered.
            var directCtx = await directSink.WaitForMessageAsync();
            Assert.Equal("direct-first", directCtx.Message);

            var outboxCtx = await outboxSink.WaitForMessageAsync();
            Assert.Equal("outbox-second", outboxCtx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private async Task ProduceViaOutboxAsync(
        IServiceProvider services,
        string key,
        string value,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var repository = sp.GetRequiredService<IOutboxRepository>();

        await using var tx = await unitOfWork.BeginAsync(ct);

        var keyBytes = Encoding.UTF8.GetBytes(key);
        await repository.EnqueueAsync(new OutboxEntry
        {
            SystemId = "kafka",
            Destination = $"kafka://localhost/{currentOutboxTopic}",
            GroupKey = $"kafka:{currentOutboxTopic}:{Convert.ToBase64String(keyBytes)}",
            Body = Encoding.UTF8.GetBytes(value),
            EnqueuedAt = DateTime.UtcNow,
            Properties = new Dictionary<string, string> { ["key"] = Convert.ToBase64String(keyBytes) },
        }, ct);

        await FlushBeforeCommitAsync(sp);
        await tx.CommitAsync(ct);
    }

    private IHost BuildHost(
        MessageSink<string> outboxSink,
        MessageSink<string> directSink,
        string outboxTopic,
        string directTopic,
        string groupId,
        TimeSpan pollingInterval)
    {
        currentOutboxTopic = outboxTopic;

        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Register sinks as keyed-like services using named wrappers.
                services.AddSingleton(new OutboxTopicSink(outboxSink));
                services.AddSingleton(new DirectTopicSink(directSink));
                services.AddEmit(emit =>
                {
                    ConfigurePersistence(emit, pollingInterval);
                    ConfigureKafka(emit, outboxTopic, directTopic, groupId);
                });
            })
            .Build();
    }

    private void ConfigureKafka(
        EmitBuilder emit,
        string outboxTopic,
        string directTopic,
        string groupId)
    {
        emit.AddKafka(kafka =>
        {
            kafka.ConfigureClient(config =>
            {
                config.BootstrapServers = BootstrapServers;
            });
            kafka.AutoProvision();

            kafka.Topic<string, string>(outboxTopic, t =>
            {
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.ConsumerGroup($"{groupId}-outbox", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<OutboxTopicSinkConsumer>();
                });
            });

            kafka.Topic<string, string>(directTopic, t =>
            {
                t.SetUtf8KeySerializer();
                t.SetUtf8ValueSerializer();
                t.SetUtf8KeyDeserializer();
                t.SetUtf8ValueDeserializer();

                t.Producer(p => p.UseDirect());
                t.ConsumerGroup($"{groupId}-direct", group =>
                {
                    group.AutoOffsetReset = ConfluentKafka.AutoOffsetReset.Earliest;
                    group.AddConsumer<DirectTopicSinkConsumer>();
                });
            });
        });
    }
}

/// <summary>Wrapper to distinguish the outbox topic sink from the direct topic sink in DI.</summary>
public sealed class OutboxTopicSink(MessageSink<string> sink)
{
    /// <summary>The underlying message sink.</summary>
    public MessageSink<string> Sink => sink;
}

/// <summary>Wrapper to distinguish the direct topic sink from the outbox topic sink in DI.</summary>
public sealed class DirectTopicSink(MessageSink<string> sink)
{
    /// <summary>The underlying message sink.</summary>
    public MessageSink<string> Sink => sink;
}

/// <summary>Sink consumer for outbox topic.</summary>
public sealed class OutboxTopicSinkConsumer(OutboxTopicSink wrapper) : IConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        => wrapper.Sink.WriteAsync(context, cancellationToken);
}

/// <summary>Sink consumer for direct topic.</summary>
public sealed class DirectTopicSinkConsumer(DirectTopicSink wrapper) : IConsumer<string>
{
    /// <inheritdoc />
    public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        => wrapper.Sink.WriteAsync(context, cancellationToken);
}
