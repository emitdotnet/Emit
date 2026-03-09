namespace Emit.UnitTests.Pipeline;

using System.Linq;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Pipeline;
using Xunit;

public sealed class PipelineConfigurableExtensionsTests
{
    // ── UseInbound(Type) on IInboundPipelineConfigurable ──

    [Fact]
    public void GivenInboundBuilder_WhenUseInbound_ThenMiddlewareAddedToPipeline()
    {
        // Arrange
        var builder = new TestInboundBuilder();

        // Act
        builder.UseInbound(typeof(TestMiddlewareA));

        // Assert
        Assert.Contains(builder.InboundPipeline.Descriptors, d => d.MiddlewareType == typeof(TestMiddlewareA));
    }

    [Fact]
    public void GivenInboundBuilder_WhenUseInbound_ThenReturnsSameInstance()
    {
        // Arrange
        var builder = new TestInboundBuilder();

        // Act
        var result = builder.UseInbound(typeof(TestMiddlewareA));

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    public void GivenInboundBuilder_WhenUseInboundCalledMultipleTimes_ThenAllRegisteredInOrder()
    {
        // Arrange
        var builder = new TestInboundBuilder();

        // Act
        builder
            .UseInbound(typeof(TestMiddlewareA))
            .UseInbound(typeof(TestMiddlewareB));

        // Assert
        Assert.Equal(
            [typeof(TestMiddlewareA), typeof(TestMiddlewareB)],
            builder.InboundPipeline.Descriptors.Select(d => d.MiddlewareType).ToList());
    }

    [Fact]
    public void GivenInboundBuilder_WhenUseInboundWithLifetime_ThenDescriptorHasCorrectLifetime()
    {
        // Arrange
        var builder = new TestInboundBuilder();

        // Act
        builder.UseInbound(typeof(TestMiddlewareA), MiddlewareLifetime.Scoped);

        // Assert
        var descriptor = Assert.Single(builder.InboundPipeline.Descriptors);
        Assert.Equal(typeof(TestMiddlewareA), descriptor.MiddlewareType);
        Assert.Equal(MiddlewareLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void GivenInboundBuilder_WhenUseInboundWithLifetime_ThenReturnsSameInstance()
    {
        // Arrange
        var builder = new TestInboundBuilder();

        // Act
        var result = builder.UseInbound(typeof(TestMiddlewareA), MiddlewareLifetime.Scoped);

        // Assert
        Assert.Same(builder, result);
    }

    // ── UseOutbound(Type) on IOutboundPipelineConfigurable ──

    [Fact]
    public void GivenOutboundBuilder_WhenUseOutbound_ThenMiddlewareAddedToPipeline()
    {
        // Arrange
        var builder = new TestOutboundBuilder();

        // Act
        builder.UseOutbound(typeof(TestMiddlewareA));

        // Assert
        Assert.Contains(builder.OutboundPipeline.Descriptors, d => d.MiddlewareType == typeof(TestMiddlewareA));
    }

    [Fact]
    public void GivenOutboundBuilder_WhenUseOutbound_ThenReturnsSameInstance()
    {
        // Arrange
        var builder = new TestOutboundBuilder();

        // Act
        var result = builder.UseOutbound(typeof(TestMiddlewareA));

        // Assert
        Assert.Same(builder, result);
    }

    // ── Factory Use(factory) on IInboundConfigurable<TMessage> ──

    [Fact]
    public void GivenTypedInboundBuilder_WhenUseWithFactory_ThenFactoryAddedToPipeline()
    {
        // Arrange
        IInboundConfigurable<string> builder = new TestTypedInboundBuilder();
        Func<IServiceProvider, IMiddleware<ConsumeContext<string>>> factory = _ => new TestMiddlewareA();

        // Act
        builder.Use(factory);

        // Assert
        var descriptor = Assert.Single(builder.InboundPipeline.Descriptors);
        Assert.Same(factory, descriptor.Factory);
        Assert.Equal(MiddlewareLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void GivenTypedInboundBuilder_WhenUseWithFactoryAndLifetime_ThenCorrectLifetime()
    {
        // Arrange
        IInboundConfigurable<string> builder = new TestTypedInboundBuilder();

        // Act
        builder.Use(_ => new TestMiddlewareA(), MiddlewareLifetime.Scoped);

        // Assert
        var descriptor = Assert.Single(builder.InboundPipeline.Descriptors);
        Assert.Equal(MiddlewareLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void GivenTypedInboundBuilder_WhenUseWithFactory_ThenReturnsSameInstance()
    {
        // Arrange
        IInboundConfigurable<string> builder = new TestTypedInboundBuilder();

        // Act
        var result = builder.Use(_ => new TestMiddlewareA());

        // Assert
        Assert.Same(builder, result);
    }

    // ── Null builder checks ──

    [Fact]
    public void GivenNullInboundBuilder_WhenUseInbound_ThenThrowsArgumentNullException()
    {
        // Arrange
        IInboundPipelineConfigurable builder = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.UseInbound(typeof(TestMiddlewareA)));
    }

    [Fact]
    public void GivenNullInboundBuilder_WhenUseInboundWithLifetime_ThenThrowsArgumentNullException()
    {
        // Arrange
        IInboundPipelineConfigurable builder = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.UseInbound(typeof(TestMiddlewareA), MiddlewareLifetime.Scoped));
    }

    [Fact]
    public void GivenNullTypedInboundBuilder_WhenUseWithFactory_ThenThrowsArgumentNullException()
    {
        // Arrange
        IInboundConfigurable<string> builder = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => builder.Use(_ => new TestMiddlewareA()));
    }

    private sealed class TestInboundBuilder : IInboundPipelineConfigurable
    {
        public IMessagePipelineBuilder InboundPipeline { get; } = new MessagePipelineBuilder();
    }

    private sealed class TestOutboundBuilder : IOutboundPipelineConfigurable
    {
        public IMessagePipelineBuilder OutboundPipeline { get; } = new MessagePipelineBuilder();
    }

    private sealed class TestTypedInboundBuilder : IInboundConfigurable<string>
    {
        public IMessagePipelineBuilder InboundPipeline { get; } = new MessagePipelineBuilder();

        public IInboundConfigurable<string> Use<TMiddleware>(MiddlewareLifetime lifetime = default)
            where TMiddleware : class, IMiddleware<ConsumeContext<string>>
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

    private sealed class TestMiddlewareA : IMiddleware<ConsumeContext<string>>
    {
        public Task InvokeAsync(ConsumeContext<string> context, IMiddlewarePipeline<ConsumeContext<string>> next) =>
            next.InvokeAsync(context);
    }

    private sealed class TestMiddlewareB : IMiddleware<ConsumeContext<string>>
    {
        public Task InvokeAsync(ConsumeContext<string> context, IMiddlewarePipeline<ConsumeContext<string>> next) =>
            next.InvokeAsync(context);
    }
}
