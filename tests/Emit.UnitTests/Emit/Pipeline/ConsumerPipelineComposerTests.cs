namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Metrics;
using global::Emit.Pipeline;
using global::Emit.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public sealed class ConsumerPipelineComposerTests
{
    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new EmitMetrics(null, new EmitMetricsEnrichment()));
        services.AddSingleton<INodeIdentity>(new Mock<INodeIdentity>().Object);
        services.AddSingleton<IOptions<EmitTracingOptions>>(Options.Create(new EmitTracingOptions()));
        services.AddSingleton(new ActivityEnricherInvoker(NullLogger<ActivityEnricherInvoker>.Instance));
        return services.BuildServiceProvider();
    }

    private static ConsumerPipelineComposer<string> CreateComposer(
        IServiceProvider? services = null,
        IMiddleware<ConsumeContext<string>>? validationMiddleware = null,
        IMiddleware<ConsumeContext<string>>? filterMiddleware = null)
    {
        services ??= BuildServices();
        return new ConsumerPipelineComposer<string>
        {
            Services = services,
            LoggerFactory = NullLoggerFactory.Instance,
            ConsumeObservers = [],
            GroupPipeline = new MessagePipelineBuilder(),
            GlobalInboundPipeline = new MessagePipelineBuilder(),
            ProviderInboundPipeline = new MessagePipelineBuilder(),
            ValidationMiddleware = validationMiddleware,
            FilterMiddleware = filterMiddleware,
        };
    }

    private static IMiddlewarePipeline<ConsumeContext<string>> CreateTerminal() =>
        new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask);

    [Fact]
    public void GivenValidationMiddleware_WhenCompose_ThenComposesSuccessfully()
    {
        // Arrange
        var order = new List<string>();
        var validationMw = new OrderTrackingMiddleware("validation", order);
        var composer = CreateComposer(validationMiddleware: validationMw);

        // Act
        var entry = composer.Compose(
            CreateTerminal(),
            perEntryPipeline: null,
            identifier: "test",
            kind: ConsumerKind.Direct,
            consumerType: typeof(string));

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("test", entry.Identifier);
    }

    [Fact]
    public void GivenFilterMiddleware_WhenCompose_ThenComposesSuccessfully()
    {
        // Arrange
        var order = new List<string>();
        var filterMw = new OrderTrackingMiddleware("filter", order);
        var composer = CreateComposer(filterMiddleware: filterMw);

        // Act
        var entry = composer.Compose(
            CreateTerminal(),
            perEntryPipeline: null,
            identifier: "test-filter",
            kind: ConsumerKind.Direct,
            consumerType: typeof(string));

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("test-filter", entry.Identifier);
    }

    [Fact]
    public void GivenNoMiddleware_WhenCompose_ThenComposesSuccessfully()
    {
        // Arrange
        var composer = CreateComposer();

        // Act
        var entry = composer.Compose(
            CreateTerminal(),
            perEntryPipeline: null,
            identifier: "no-mw",
            kind: ConsumerKind.Direct,
            consumerType: typeof(string));

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("no-mw", entry.Identifier);
    }

    private sealed class OrderTrackingMiddleware(string name, List<string> order) : IMiddleware<ConsumeContext<string>>
    {
        public async Task InvokeAsync(ConsumeContext<string> context, IMiddlewarePipeline<ConsumeContext<string>> next)
        {
            order.Add($"{name}:before");
            await next.InvokeAsync(context).ConfigureAwait(false);
            order.Add($"{name}:after");
        }
    }
}
