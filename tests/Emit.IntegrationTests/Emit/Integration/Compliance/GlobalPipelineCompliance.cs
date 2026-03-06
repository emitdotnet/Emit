namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for global inbound and outbound pipeline middleware. Derived classes
/// configure a <c>string, string</c> topic and register middleware on the global
/// <see cref="EmitBuilder.InboundPipeline"/> and <see cref="EmitBuilder.OutboundPipeline"/>.
/// </summary>
[Trait("Category", "Integration")]
public abstract class GlobalPipelineCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// Also registers <see cref="InboundCounterMiddleware"/> on <c>emit.InboundPipeline</c>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithInboundMiddleware(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// Also registers <see cref="OutboundCounterMiddleware"/> on <c>emit.OutboundPipeline</c>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithOutboundMiddleware(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Verifies that global inbound middleware is invoked when a message is consumed.
    /// </summary>
    [Fact]
    public async Task GivenGlobalInboundMiddleware_WhenMessageConsumed_ThenMiddlewareInvoked()
    {
        // Arrange
        var topic = $"test-inbound-mw-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var counter = new InvocationCounter();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(counter);
                services.AddEmit(emit => ConfigureWithInboundMiddleware(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "hello"));

            // Assert — consumer received the message and middleware fired.
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("hello", ctx.Message);
            Assert.True(counter.Count > 0, $"Expected inbound middleware to be invoked at least once, but count was {counter.Count}.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Verifies that global outbound middleware is invoked when a message is produced,
    /// and that the message still reaches the consumer.
    /// </summary>
    [Fact]
    public async Task GivenGlobalOutboundMiddleware_WhenMessageProduced_ThenMiddlewareInvoked()
    {
        // Arrange
        var topic = $"test-outbound-mw-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var counter = new InvocationCounter();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(counter);
                services.AddEmit(emit => ConfigureWithOutboundMiddleware(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "hello"));

            // Assert — consumer received the message and outbound middleware fired.
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("hello", ctx.Message);
            Assert.True(counter.Count > 0, $"Expected outbound middleware to be invoked at least once, but count was {counter.Count}.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Thread-safe counter for tracking middleware invocations.
    /// </summary>
    public sealed class InvocationCounter
    {
        private int count;

        /// <summary>Gets the total number of invocations recorded.</summary>
        public int Count => Volatile.Read(ref count);

        /// <summary>Increments the invocation counter by one.</summary>
        public void Increment() => Interlocked.Increment(ref count);
    }

    /// <summary>
    /// Global inbound middleware that increments an <see cref="InvocationCounter"/> and
    /// delegates to the next middleware in the pipeline.
    /// </summary>
    public sealed class InboundCounterMiddleware(InvocationCounter counter)
        : IMiddleware<InboundContext<string>>
    {
        /// <inheritdoc />
        public async Task InvokeAsync(InboundContext<string> context, MessageDelegate<InboundContext<string>> next)
        {
            counter.Increment();
            await next(context);
        }
    }

    /// <summary>
    /// Global outbound middleware that increments an <see cref="InvocationCounter"/> and
    /// delegates to the next middleware in the pipeline.
    /// </summary>
    public sealed class OutboundCounterMiddleware(InvocationCounter counter)
        : IMiddleware<OutboundContext<string>>
    {
        /// <inheritdoc />
        public async Task InvokeAsync(OutboundContext<string> context, MessageDelegate<OutboundContext<string>> next)
        {
            counter.Increment();
            await next(context);
        }
    }
}
