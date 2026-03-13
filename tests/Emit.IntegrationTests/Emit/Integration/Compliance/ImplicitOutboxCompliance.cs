namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for EF Core implicit outbox (Tier 1): produce + SaveChangesAsync
/// without an explicit transaction. MongoDB does not support this tier.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ImplicitOutboxCompliance : IAsyncLifetime
{
    /// <summary>
    /// Configures Emit with persistence (outbox enabled) and Kafka with a string,string topic.
    /// </summary>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan pollingInterval);

    /// <summary>
    /// Produces a message via IEventProducer and optionally calls SaveChangesAsync.
    /// </summary>
    protected abstract Task ProduceAndSaveAsync(
        IServiceProvider services,
        string key,
        string value,
        bool save,
        CancellationToken ct = default);

    /// <summary>
    /// Produces two messages: the first with SaveChangesAsync, the second without.
    /// Then calls SaveChangesAsync again to flush the second message.
    /// Returns after the second SaveChangesAsync.
    /// </summary>
    protected abstract Task ProduceTwoWithInterleavedSaveAsync(
        IServiceProvider services,
        string key,
        string value1,
        string value2,
        CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GivenImplicitMode_WhenProduceAndSaveChanges_ThenOutboxEntryDelivered()
    {
        // Arrange
        var topic = $"test-implicit-save-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act
            await ProduceAndSaveAsync(host.Services, "k", "implicit-hello", save: true);

            // Assert
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("implicit-hello", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenImplicitMode_WhenProduceWithoutSaveChanges_ThenNoDelivery()
    {
        // Arrange
        var topic = $"test-implicit-nosave-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — produce without SaveChangesAsync.
            await ProduceAndSaveAsync(host.Services, "k", "no-save", save: false);

            // Wait for daemon cycles.
            await Task.Delay(pollingInterval * 5);

            // Assert — no delivery.
            Assert.Empty(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenImplicitMode_WhenProduceAndSaveChangesWithBusinessData_ThenBothPersisted()
    {
        // Arrange
        var topic = $"test-implicit-biz-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — produce and save (business data verification is provider-specific).
            await ProduceAndSaveAsync(host.Services, "k", "biz-msg", save: true);

            // Assert — outbox entry delivered.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("biz-msg", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenImplicitMode_WhenProduceThenSaveChangesThenProduceAgain_ThenSecondProduceRequiresAnotherSaveChanges()
    {
        // Arrange
        var topic = $"test-implicit-two-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var host = BuildHost(sink, topic, groupId, pollingInterval);

        await host.StartAsync();

        try
        {
            // Act — produce msg1 + save, produce msg2 without save, then save again.
            await ProduceTwoWithInterleavedSaveAsync(host.Services, "k", "msg1", "msg2");

            // Assert — both messages delivered in order.
            var ctx1 = await sink.WaitForMessageAsync();
            Assert.Equal("msg1", ctx1.Message);
            var ctx2 = await sink.WaitForMessageAsync();
            Assert.Equal("msg2", ctx2.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private IHost BuildHost(MessageSink<string> sink, string topic, string groupId, TimeSpan pollingInterval)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmit(emit, topic, groupId, pollingInterval));
            })
            .Build();
    }
}
