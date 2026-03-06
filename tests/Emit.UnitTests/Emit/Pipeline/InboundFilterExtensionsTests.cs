namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

public sealed class InboundFilterExtensionsTests
{
    // ── AddConsumerFilter (class-based helper) ──

    [Fact]
    public void GivenPipeline_WhenAddConsumerFilter_ThenAddsDescriptor()
    {
        // Arrange
        var pipeline = new MessagePipelineBuilder();

        // Act
        pipeline.AddConsumerFilter<string, AcceptAllFilter>();

        // Assert
        Assert.Single(pipeline.Descriptors);
    }

    [Fact]
    public async Task GivenClassFilterReturnsTrue_WhenPipelineExecutes_ThenCallsNext()
    {
        // Arrange
        var builder = new TestInboundBuilder();
        builder.Filter<AcceptAllFilter>();

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    [Fact]
    public async Task GivenClassFilterReturnsFalse_WhenPipelineExecutes_ThenSkipsNext()
    {
        // Arrange
        var builder = new TestInboundBuilder();
        builder.Filter<RejectAllFilter>();

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline(context);

        // Assert
        Assert.False(nextCalled.Value);
    }

    // ── Filter(Func<InboundContext<T>, bool>) — sync predicate ──

    [Fact]
    public void GivenSyncPredicate_WhenFilter_ThenAddsDescriptor()
    {
        // Arrange
        var builder = new TestInboundBuilder();

        // Act
        builder.Filter(ctx => ctx.Message == "pass");

        // Assert
        Assert.Single(builder.InboundPipeline.Descriptors);
    }

    [Fact]
    public async Task GivenSyncPredicateReturnsTrue_WhenPipelineExecutes_ThenCallsNext()
    {
        // Arrange
        var builder = new TestInboundBuilder();
        builder.Filter(_ => true);

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    [Fact]
    public async Task GivenSyncPredicateReturnsFalse_WhenPipelineExecutes_ThenSkipsNext()
    {
        // Arrange
        var builder = new TestInboundBuilder();
        builder.Filter(_ => false);

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline(context);

        // Assert
        Assert.False(nextCalled.Value);
    }

    // ── Filter(Func<InboundContext<T>, CancellationToken, ValueTask<bool>>) — async predicate ──

    [Fact]
    public void GivenAsyncPredicate_WhenFilter_ThenAddsDescriptor()
    {
        // Arrange
        var builder = new TestInboundBuilder();

        // Act
        builder.Filter((_, _) => ValueTask.FromResult(true));

        // Assert
        Assert.Single(builder.InboundPipeline.Descriptors);
    }

    [Fact]
    public async Task GivenAsyncPredicateReturnsTrue_WhenPipelineExecutes_ThenCallsNext()
    {
        // Arrange
        var builder = new TestInboundBuilder();
        builder.Filter((_, _) => ValueTask.FromResult(true));

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    [Fact]
    public async Task GivenAsyncPredicateReturnsFalse_WhenPipelineExecutes_ThenSkipsNext()
    {
        // Arrange
        var builder = new TestInboundBuilder();
        builder.Filter((_, _) => ValueTask.FromResult(false));

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline(context);

        // Assert
        Assert.False(nextCalled.Value);
    }

    // ── Multiple filters (AND semantics) ──

    [Fact]
    public async Task GivenMultipleFilters_WhenAllPass_ThenCallsNext()
    {
        // Arrange
        var builder = new TestInboundBuilder();
        builder.Filter(_ => true);
        builder.Filter((_, _) => ValueTask.FromResult(true));

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    [Fact]
    public async Task GivenMultipleFilters_WhenOneRejects_ThenSkipsNext()
    {
        // Arrange
        var builder = new TestInboundBuilder();
        builder.Filter(_ => true);
        builder.Filter(_ => false);

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline(context);

        // Assert
        Assert.False(nextCalled.Value);
    }

    // ── Helpers ──

    private static (TestInboundContext<string> Context, StrongBox<bool> NextCalled) BuildAndGetContext(
        TestInboundBuilder builder)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestInboundContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services,
            Message = "test-message"
        };
        return (context, new StrongBox<bool>(false));
    }

    private static MessageDelegate<InboundContext<string>> BuildPipeline(
        TestInboundBuilder builder,
        TestInboundContext<string> context,
        StrongBox<bool> nextCalled)
    {
        MessageDelegate<InboundContext<string>> terminal = _ =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        };

        return builder.InboundPipeline.Build<InboundContext<string>, string>(
            context.Services, terminal);
    }

    // ── Test types ──

    private sealed class TestInboundBuilder : IInboundConfigurable<string>
    {
        public IMessagePipelineBuilder InboundPipeline { get; } = new MessagePipelineBuilder();

        public IInboundConfigurable<string> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
            where TMiddleware : class, IMiddleware<InboundContext<string>>
        {
            InboundPipeline.Use(typeof(TMiddleware), lifetime);
            return this;
        }

        public IInboundConfigurable<string> Filter<TFilter>()
            where TFilter : class, IConsumerFilter<string>
        {
            InboundPipeline.AddConsumerFilter<string, TFilter>();
            return this;
        }
    }

    private sealed class TestInboundContext<T> : InboundContext<T>;

    private sealed class AcceptAllFilter : IConsumerFilter<string>
    {
        public ValueTask<bool> ShouldConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private sealed class RejectAllFilter : IConsumerFilter<string>
    {
        public ValueTask<bool> ShouldConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }

    private sealed class StrongBox<T>(T value)
    {
        public T Value { get; set; } = value;
    }
}
