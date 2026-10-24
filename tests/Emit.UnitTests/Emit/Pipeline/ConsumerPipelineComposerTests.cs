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
            GroupPipeline = new MessagePipelineBuilder<ConsumeContext<string>>(),
            GlobalPipeline = new MessagePipelineBuilder<MessageContext>(),
            GlobalInboundPipeline = new MessagePipelineBuilder<ConsumeContext>(),
            ProviderInboundPipeline = new MessagePipelineBuilder<ConsumeContext>(),
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

    [Fact]
    public async Task GivenTransactionalConsumer_WhenItsOwnMiddlewareThrowsAfterTheHandler_ThenTransactionIsNotCommitted()
    {
        // Arrange
        var mockTransaction = new Mock<IUnitOfWorkTransaction>();
        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(mockTransaction.Object);

        var services = new ServiceCollection();
        services.AddSingleton(new EmitMetrics(null, new EmitMetricsEnrichment()));
        services.AddSingleton<INodeIdentity>(new Mock<INodeIdentity>().Object);
        services.AddSingleton<IOptions<EmitTracingOptions>>(Options.Create(new EmitTracingOptions()));
        services.AddSingleton(new ActivityEnricherInvoker(NullLogger<ActivityEnricherInvoker>.Instance));
        services.AddScoped<IEmitContext, EmitContext>();
        services.AddSingleton(mockUow.Object);
        var sp = services.BuildServiceProvider();

        var perConsumer = new MessagePipelineBuilder<ConsumeContext<string>>();
        perConsumer.Use(
            _ => new ThrowAfterNextMiddleware(),
            MiddlewareLifetime.Singleton);

        var entry = CreateComposer(sp).Compose(
            CreateTerminal(),
            perConsumer,
            identifier: "transactional",
            kind: ConsumerKind.Direct,
            consumerType: typeof(TransactionalConsumer));

        var context = new ConsumeContext<string>
        {
            MessageId = "test-id",
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = CancellationToken.None,
            Services = sp,
            Message = "test-message",
            TransportContext = TestTransportContext.Create(sp),
        };

        // Act: the error middleware absorbs the failure (no policy configured, so discard)
        await entry.Pipeline.InvokeAsync(context);

        // Assert: the consumer's own middleware runs inside the unit of work, so its failure rolls back
        mockUow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockTransaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockTransaction.Verify(t => t.DisposeAsync(), Times.Once);
    }

    [Transactional]
    private sealed class TransactionalConsumer : IConsumer<string>
    {
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThrowAfterNextMiddleware : IMiddleware<ConsumeContext<string>>
    {
        public async Task InvokeAsync(ConsumeContext<string> context, Func<Task> next)
        {
            await next().ConfigureAwait(false);
            throw new InvalidOperationException("failed after the handler");
        }
    }

    private sealed class OrderTrackingMiddleware(string name, List<string> order) : IMiddleware<ConsumeContext<string>>
    {
        public async Task InvokeAsync(ConsumeContext<string> context, Func<Task> next)
        {
            order.Add($"{name}:before");
            await next().ConfigureAwait(false);
            order.Add($"{name}:after");
        }
    }
}
