namespace Emit.IntegrationTests.Integration.Compliance;

using System.Collections.Concurrent;
using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Mediator;
using Emit.Mediator.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for the global middleware levels: <see cref="EmitBuilder.Pipeline"/> runs on every pipeline,
/// <see cref="EmitBuilder.InboundPipeline"/> on every message consumed, and <see cref="EmitBuilder.OutboundPipeline"/>
/// on every message produced. Derived classes configure a <c>string, string</c> topic.
/// </summary>
[Trait("Category", "Integration")]
public abstract class GlobalPipelineCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer and a consumer group
    /// containing <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureProvider(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Verifies that each global level runs on its pipelines and only on them: a message produced and consumed, and
    /// a mediator request.
    /// </summary>
    [Fact]
    public async Task GivenMiddlewareOnEachGlobalLevel_WhenAMessageFlowsAndARequestIsSent_ThenEachRunsOnItsPipelinesOnly()
    {
        // Arrange
        var topic = $"test-global-mw-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var recorder = new PipelineRecorder();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(recorder);
                services.AddEmit(emit =>
                {
                    emit.Pipeline.Use<EveryPipelineMiddleware>();
                    emit.InboundPipeline.Use<InboundMiddleware>();
                    emit.OutboundPipeline.Use<OutboundMiddleware>();
                    emit.AddMediator(mediator => mediator.AddHandler<PingHandler>());
                    ConfigureProvider(emit, topic, groupId);
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>()
                .ProduceAsync(new EventMessage<string, string>("k", "hello"));
            var consumed = await sink.WaitForMessageAsync();
            await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new Ping());

            // Assert
            Assert.Equal("hello", consumed.Message);
            Assert.Equal(["consume", "mediator", "send"], recorder.Of(nameof(EveryPipelineMiddleware)));
            Assert.Equal(["consume"], recorder.Of(nameof(InboundMiddleware)));
            Assert.Equal(["send"], recorder.Of(nameof(OutboundMiddleware)));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Records the kind of pipeline each middleware ran on.
    /// </summary>
    public sealed class PipelineRecorder
    {
        private readonly ConcurrentQueue<(string Middleware, string Pipeline)> entries = new();

        /// <summary>Records that <paramref name="middleware"/> ran on the pipeline of <paramref name="context"/>.</summary>
        public void Record(string middleware, MessageContext context) => entries.Enqueue((middleware, context switch
        {
            ConsumeContext => "consume",
            SendContext => "send",
            MediatorContext => "mediator",
            _ => context.GetType().Name,
        }));

        /// <summary>The kinds of pipeline <paramref name="middleware"/> ran on, in alphabetical order.</summary>
        public IReadOnlyList<string> Of(string middleware) =>
            [.. entries.Where(e => e.Middleware == middleware).Select(e => e.Pipeline).Order()];
    }

    /// <summary>
    /// Middleware of every pipeline.
    /// </summary>
    public sealed class EveryPipelineMiddleware(PipelineRecorder recorder) : IMiddleware<MessageContext>
    {
        /// <inheritdoc />
        public Task InvokeAsync(MessageContext context, Func<Task> next)
        {
            recorder.Record(nameof(EveryPipelineMiddleware), context);
            return next();
        }
    }

    /// <summary>
    /// Middleware of every message consumed.
    /// </summary>
    public sealed class InboundMiddleware(PipelineRecorder recorder) : IMiddleware<ConsumeContext>
    {
        /// <inheritdoc />
        public Task InvokeAsync(ConsumeContext context, Func<Task> next)
        {
            recorder.Record(nameof(InboundMiddleware), context);
            return next();
        }
    }

    /// <summary>
    /// Middleware of every message produced.
    /// </summary>
    public sealed class OutboundMiddleware(PipelineRecorder recorder) : IMiddleware<SendContext>
    {
        /// <inheritdoc />
        public Task InvokeAsync(SendContext context, Func<Task> next)
        {
            recorder.Record(nameof(OutboundMiddleware), context);
            return next();
        }
    }

    /// <summary>
    /// A mediator request without a response.
    /// </summary>
    public sealed record Ping : IRequest;

    /// <summary>
    /// Handles <see cref="Ping"/>.
    /// </summary>
    public sealed class PingHandler : IRequestHandler<Ping>
    {
        /// <inheritdoc />
        public Task HandleAsync(Ping request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
