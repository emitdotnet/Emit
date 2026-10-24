namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.IntegrationTests.Integration;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for consumer filters. Derived classes configure a <c>string, string</c>
/// topic with a consumer group that applies a filter. Messages prefixed with <c>"skip:"</c>
/// are blocked by the filter; all other messages pass through.
/// </summary>
[Trait("Category", "Integration")]
public abstract class FilterCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group applying <see cref="PrefixFilter"/> followed by
    /// <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithFilter(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Verifies that a message not blocked by the filter is delivered to the consumer.
    /// </summary>
    [Fact]
    public async Task GivenFilterPassesMessage_WhenConsumed_ThenConsumerInvoked()
    {
        // Arrange
        var topic = $"test-filter-pass-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureWithFilter(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "pass:hello"));

            // Assert
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("pass:hello", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a message blocked by the filter is never delivered to the consumer.
    /// Uses a sentinel message to confirm the consumer pipeline is alive.
    /// </summary>
    [Fact]
    public async Task GivenFilterBlocksMessage_WhenConsumed_ThenConsumerNotInvoked()
    {
        // Arrange
        var topic = $"test-filter-block-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureWithFilter(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a blocked message then a sentinel that passes.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "skip:hello"));
            await producer.ProduceAsync(new EventMessage<string, string>("k", "pass:sentinel"));

            // Assert — only the sentinel arrives; the "skip:" message was blocked.
            var ctx = await sink.WaitForMessageAsync();
            Assert.Equal("pass:sentinel", ctx.Message);
            Assert.Single(sink.ReceivedMessages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer and a
    /// consumer group filtering with <paramref name="predicate"/>, whose error policy retries every
    /// exception 3 times with no backoff, then discards it. The consumer group must contain
    /// <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    /// <param name="predicate">The filter predicate, given the message value.</param>
    protected abstract void ConfigureWithFilterAndRetry(
        EmitBuilder emit,
        string topic,
        string groupId,
        Func<string, bool> predicate);

    /// <summary>
    /// Verifies that a filter that throws once, as when a dependency it calls is briefly unreachable, is a
    /// failed attempt the error policy retries: the message is filtered again and handled.
    /// </summary>
    [Fact]
    public async Task GivenFilterThatThrowsOnce_WhenRetryConfigured_ThenTheMessageIsFilteredAgainAndHandled()
    {
        // Arrange
        var topic = $"test-filter-retry-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var calls = new InvocationCounter();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureWithFilterAndRetry(
                    emit, topic, groupId,
                    _ => calls.Increment() == 1 ? throw new TimeoutException("dependency unreachable") : true));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "payload"));

            // Assert
            var received = await sink.WaitForMessageAsync();
            Assert.Equal("payload", received.Message);
            Assert.Equal(1, received.RetryAttempt);
            Assert.Equal(2, calls.Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Filter that blocks messages whose value starts with <c>"skip:"</c>.
    /// All other messages pass through to the consumer.
    /// </summary>
    public sealed class PrefixFilter : IConsumerFilter<string>
    {
        /// <inheritdoc />
        public ValueTask<bool> ShouldConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => ValueTask.FromResult(!context.Message.StartsWith("skip:", StringComparison.Ordinal));
    }
}
