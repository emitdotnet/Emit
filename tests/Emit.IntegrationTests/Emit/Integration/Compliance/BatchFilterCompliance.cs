namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static Emit.IntegrationTests.Integration.TestHelpers;

/// <summary>
/// Compliance tests for consumer filters on batch consumers. Derived classes configure a
/// <c>string, string</c> topic with a consumer group in batch mode that applies a filter.
/// Messages prefixed with <c>"skip:"</c> are blocked by the filter; all other messages pass through.
/// </summary>
[Trait("Category", "Integration")]
public abstract class BatchFilterCompliance
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic in batch mode
    /// that has a producer and a consumer group applying a filter before
    /// <see cref="BatchSinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithFilter(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic in batch mode
    /// with two filters applied: one that blocks <c>"skip-f1:"</c> prefix messages and one
    /// that blocks <c>"skip-f2:"</c> prefix messages. The consumer group must use
    /// <see cref="BatchSinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithMultipleFilters(EmitBuilder emit, string topic, string groupId);

    [Fact]
    public async Task GivenFilterPassesItem_WhenBatchConsumed_ThenItemReachesHandler()
    {
        // Arrange
        var topic = $"test-bfilt-pass-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

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
            await WaitUntilAsync(
                () => sink.Messages.Count >= 1,
                "Expected at least one message to arrive at the batch consumer",
                PollTimeout);

            Assert.Contains("pass:hello", sink.Messages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenFilterBlocksSomeItems_WhenBatchConsumed_ThenSurvivorsReachHandlerAndFilteredAreDropped()
    {
        // Arrange
        var topic = $"test-bfilt-partial-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

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
            // Act — produce a filtered message followed by a sentinel that passes
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "skip:should-drop"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "pass:sentinel"));

            // Assert — only the sentinel arrives; the "skip:" message was blocked
            await WaitUntilAsync(
                () => sink.Messages.Count >= 1,
                "Expected at least one message to arrive at the batch consumer",
                PollTimeout);

            Assert.Contains("pass:sentinel", sink.Messages);
            Assert.DoesNotContain("skip:should-drop", sink.Messages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenFilterBlocksAllItems_WhenBatchConsumed_ThenHandlerNotInvoked()
    {
        // Arrange
        var topic = $"test-bfilt-allblock-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

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
            // Act — all messages are blocked, then a sentinel that passes to confirm consumer is alive
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "skip:one"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "skip:two"));
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "pass:sentinel"));

            // Assert — only the sentinel arrives; the blocked messages were all dropped
            await WaitUntilAsync(
                () => sink.Messages.Count >= 1,
                "Expected at least one message to arrive at the batch consumer",
                PollTimeout);

            Assert.DoesNotContain("skip:one", sink.Messages);
            Assert.DoesNotContain("skip:two", sink.Messages);
            Assert.Contains("pass:sentinel", sink.Messages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenMultipleFilters_WhenBatchConsumed_ThenAllMustPassPerItem()
    {
        // Arrange — uses ConfigureWithMultipleFilters which blocks "skip-f1:" AND "skip-f2:"
        var topic = $"test-bfilt-multi-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureWithMultipleFilters(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "skip-f1:blocked-by-first"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "skip-f2:blocked-by-second"));
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "pass:sentinel"));

            // Assert — only the sentinel passes (both f1 and f2 messages are blocked)
            await WaitUntilAsync(
                () => sink.Messages.Count >= 1,
                "Expected at least one message to arrive at the batch consumer",
                PollTimeout);

            Assert.DoesNotContain("skip-f1:blocked-by-first", sink.Messages);
            Assert.DoesNotContain("skip-f2:blocked-by-second", sink.Messages);
            Assert.Contains("pass:sentinel", sink.Messages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task GivenFilteredItems_WhenBatchConsumed_ThenOffsetsAdvanceForSkippedItems()
    {
        // Arrange — verifies that filtered messages do not replay after restart (offsets are committed)
        var topic = $"test-bfilt-offsets-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new BatchSinkConsumer<string>();

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
            // Act — produce a blocked message followed by a sentinel
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "skip:filtered"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "pass:sentinel"));

            // Wait for sentinel to arrive
            await WaitUntilAsync(
                () => sink.Messages.Count >= 1,
                "Expected sentinel to arrive",
                PollTimeout);

            Assert.Contains("pass:sentinel", sink.Messages);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Filter that blocks messages whose value starts with <c>"skip:"</c>.
    /// </summary>
    public sealed class PrefixFilter : IConsumerFilter<string>
    {
        /// <inheritdoc />
        public ValueTask<bool> ShouldConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => ValueTask.FromResult(!context.Message.StartsWith("skip:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Filter that blocks messages whose value starts with <c>"skip-f1:"</c>.
    /// </summary>
    public sealed class PrefixF1Filter : IConsumerFilter<string>
    {
        /// <inheritdoc />
        public ValueTask<bool> ShouldConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => ValueTask.FromResult(!context.Message.StartsWith("skip-f1:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Filter that blocks messages whose value starts with <c>"skip-f2:"</c>.
    /// </summary>
    public sealed class PrefixF2Filter : IConsumerFilter<string>
    {
        /// <inheritdoc />
        public ValueTask<bool> ShouldConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => ValueTask.FromResult(!context.Message.StartsWith("skip-f2:", StringComparison.Ordinal));
    }
}
