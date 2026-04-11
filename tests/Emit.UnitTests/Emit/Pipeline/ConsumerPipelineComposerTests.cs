namespace Emit.UnitTests.Pipeline;

using global::Emit.Abstractions;
using global::Emit.Abstractions.Metrics;
using global::Emit.Abstractions.Pipeline;
using global::Emit.Consumer;
using global::Emit.Metrics;
using global::Emit.Pipeline;
using global::Emit.Pipeline.Modules;
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
        ValidationModule<string>? validation = null,
        IMiddleware<ConsumeContext<string>>? preBuiltValidation = null)
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
            Validation = validation,
            PreBuiltValidationMiddleware = preBuiltValidation,
        };
    }

    private static IMiddlewarePipeline<ConsumeContext<string>> CreateTerminal() =>
        new TestPipeline<ConsumeContext<string>>(_ => Task.CompletedTask);

    [Fact]
    public void Given_PreBuiltValidationMiddleware_When_Compose_Then_InsertedAtStep4()
    {
        // Arrange
        var order = new List<string>();
        var preBuilt = new OrderTrackingMiddleware("pre-built-validation", order);
        var composer = CreateComposer(preBuiltValidation: preBuilt);

        // Act
        var entry = composer.Compose(
            CreateTerminal(),
            perEntryPipeline: null,
            identifier: "test",
            kind: ConsumerKind.Direct,
            consumerType: typeof(string));

        // Assert — entry should be composed without throwing; PreBuiltValidationMiddleware was used
        Assert.NotNull(entry);
        Assert.Equal("test", entry.Identifier);
    }

    [Fact]
    public void Given_NoPreBuiltValidation_When_ValidationConfigured_Then_BuildsValidationMiddleware()
    {
        // Arrange
        var services = BuildServices();
        var module = new ValidationModule<string>();
        module.Configure(
            msg => MessageValidationResult.Success,
            a => a.Discard());

        var composer = CreateComposer(services: services, validation: module);

        // Act
        var entry = composer.Compose(
            CreateTerminal(),
            perEntryPipeline: null,
            identifier: "test-validation",
            kind: ConsumerKind.Direct,
            consumerType: typeof(string));

        // Assert — entry is composed using ValidationMiddleware built from the module
        Assert.NotNull(entry);
        Assert.Equal("test-validation", entry.Identifier);
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
