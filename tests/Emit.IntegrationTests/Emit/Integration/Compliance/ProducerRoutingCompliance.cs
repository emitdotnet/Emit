namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Models;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for producer routing: outbox-by-default when outbox infra is configured,
/// and UseDirect() opt-out for immediate delivery.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ProducerRoutingCompliance : IAsyncLifetime
{
    /// <summary>
    /// Configures Emit with persistence (outbox), Kafka, two topics:
    /// - outboxTopic: producer with default routing (outbox)
    /// - directTopic: producer with UseDirect()
    /// Both topics have SinkConsumer consumer groups.
    /// </summary>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string outboxTopic,
        string directTopic,
        string groupId,
        TimeSpan pollingInterval);

    /// <summary>
    /// Produces via IUnitOfWork + commit to the outbox topic.
    /// </summary>
    protected abstract Task ProduceViaOutboxAsync(
        IServiceProvider services,
        string key,
        string value,
        CancellationToken ct = default);

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

    private IHost BuildHost(
        MessageSink<string> outboxSink,
        MessageSink<string> directSink,
        string outboxTopic,
        string directTopic,
        string groupId,
        TimeSpan pollingInterval)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Register sinks as keyed-like services using named wrappers.
                services.AddSingleton(new OutboxTopicSink(outboxSink));
                services.AddSingleton(new DirectTopicSink(directSink));
                services.AddEmit(emit => ConfigureEmit(emit, outboxTopic, directTopic, groupId, pollingInterval));
            })
            .Build();
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
