namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Pipeline;
using global::Emit.Pipeline.Modules;
using global::Emit.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class InboundFilterExtensionsTests
{
    // ── AddConsumerFilter (class-based helper) ──

    [Fact]
    public void GivenPipeline_WhenAddConsumerFilter_ThenRegistersTheFilter()
    {
        // Arrange
        var pipeline = new MessagePipelineBuilder<ConsumeContext<string>>();

        // Act
        pipeline.AddConsumerFilter<string, AcceptAllFilter>();

        // Assert
        Assert.False(pipeline.IsEmpty);
    }

    [Fact]
    public async Task GivenClassFilterReturnsTrue_WhenPipelineExecutes_ThenCallsNext()
    {
        // Arrange
        var builder = new RouteHandlerConfigurator<string>();
        builder.Filter<AcceptAllFilter>();

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    [Fact]
    public async Task GivenClassFilterReturnsFalse_WhenPipelineExecutes_ThenSkipsNext()
    {
        // Arrange
        var builder = new RouteHandlerConfigurator<string>();
        builder.Filter<RejectAllFilter>();

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled.Value);
    }

    // ── Filter(Func<ConsumeContext<T>, bool>) — sync predicate ──

    [Fact]
    public void GivenSyncPredicate_WhenFilter_ThenAddsDescriptor()
    {
        // Arrange
        var builder = new RouteHandlerConfigurator<string>();

        // Act
        builder.Filter(ctx => ctx.Message == "pass");

        // Assert
        Assert.False(builder.Pipeline.IsEmpty);
    }

    [Fact]
    public async Task GivenSyncPredicateReturnsTrue_WhenPipelineExecutes_ThenCallsNext()
    {
        // Arrange
        var builder = new RouteHandlerConfigurator<string>();
        builder.Filter(_ => true);

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    [Fact]
    public async Task GivenSyncPredicateReturnsFalse_WhenPipelineExecutes_ThenSkipsNext()
    {
        // Arrange
        var builder = new RouteHandlerConfigurator<string>();
        builder.Filter(_ => false);

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled.Value);
    }

    // ── Filter(Func<ConsumeContext<T>, CancellationToken, ValueTask<bool>>) — async predicate ──

    [Fact]
    public void GivenAsyncPredicate_WhenFilter_ThenAddsDescriptor()
    {
        // Arrange
        var builder = new RouteHandlerConfigurator<string>();

        // Act
        builder.Filter((_, _) => ValueTask.FromResult(true));

        // Assert
        Assert.False(builder.Pipeline.IsEmpty);
    }

    [Fact]
    public async Task GivenAsyncPredicateReturnsTrue_WhenPipelineExecutes_ThenCallsNext()
    {
        // Arrange
        var builder = new RouteHandlerConfigurator<string>();
        builder.Filter((_, _) => ValueTask.FromResult(true));

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    [Fact]
    public async Task GivenAsyncPredicateReturnsFalse_WhenPipelineExecutes_ThenSkipsNext()
    {
        // Arrange
        var builder = new RouteHandlerConfigurator<string>();
        builder.Filter((_, _) => ValueTask.FromResult(false));

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled.Value);
    }

    // ── Multiple filters (AND semantics) ──

    [Fact]
    public async Task GivenMultipleFilters_WhenAllPass_ThenCallsNext()
    {
        // Arrange
        var builder = new RouteHandlerConfigurator<string>();
        builder.Filter(_ => true);
        builder.Filter((_, _) => ValueTask.FromResult(true));

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    [Fact]
    public async Task GivenMultipleFilters_WhenOneRejects_ThenSkipsNext()
    {
        // Arrange
        var builder = new RouteHandlerConfigurator<string>();
        builder.Filter(_ => true);
        builder.Filter(_ => false);

        var (context, nextCalled) = BuildAndGetContext(builder);

        // Act
        var pipeline = BuildPipeline(builder, context, nextCalled);
        await pipeline.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled.Value);
    }

    // ── Helpers ──

    private static (TestConsumeContext<string> Context, StrongBox<bool> NextCalled) BuildAndGetContext(
        RouteHandlerConfigurator<string> builder)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestConsumeContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = services,
            Message = "test-message",
            TransportContext = TestTransportContext.Create(services),
        };
        return (context, new StrongBox<bool>(false));
    }

    private static IMiddlewarePipeline<ConsumeContext<string>> BuildPipeline(
        RouteHandlerConfigurator<string> builder,
        TestConsumeContext<string> context,
        StrongBox<bool> nextCalled)
    {
        IMiddlewarePipeline<ConsumeContext<string>> terminal = new TestPipeline<ConsumeContext<string>>(_ =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        return MessagePipelineBuilder.Build<ConsumeContext<string>, string>(
            context.Services, terminal, builder.Pipeline);
    }

    // ── Test types ──

    private sealed class TestConsumeContext<T> : ConsumeContext<T>;

    private sealed class AcceptAllFilter : IConsumerFilter<string>
    {
        public ValueTask<bool> ShouldConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private sealed class RejectAllFilter : IConsumerFilter<string>
    {
        public ValueTask<bool> ShouldConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }

    private sealed class StrongBox<T>(T value)
    {
        public T Value { get; set; } = value;
    }

}
