namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for Kafka-level inbound pipeline middleware. Derived classes configure
/// a <c>string, string</c> topic and register middleware on the Kafka-level
/// <c>kafka.InboundPipeline</c> (not the global <c>emit.InboundPipeline</c>).
/// </summary>
[Trait("Category", "Integration")]
public abstract class ProviderPipelineCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// Also registers <see cref="InboundCounterMiddleware"/> on the Kafka-level inbound pipeline
    /// using <c>kafka.InboundPipeline.Use&lt;InboundCounterMiddleware&gt;</c>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithKafkaInboundMiddleware(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Verifies that Kafka-level inbound middleware is invoked when a message is consumed.
    /// </summary>
    [Fact]
    public async Task GivenKafkaLevelInboundMiddleware_WhenMessageConsumed_ThenMiddlewareInvoked()
    {
        // Arrange
        var topic = $"test-kafka-mw-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var counter = new InvocationCounter();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(counter);
                services.AddEmit(emit => ConfigureWithKafkaInboundMiddleware(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "kafka-pipeline-test"));

            // Assert — consumer received the message and Kafka-level middleware fired.
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("kafka-pipeline-test", ctx.Message);
            Assert.True(counter.Count > 0,
                $"Expected Kafka-level inbound middleware to be invoked at least once, but count was {counter.Count}.");
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
    /// Inbound middleware that increments an <see cref="InvocationCounter"/> when invoked.
    /// </summary>
    public sealed class InboundCounterMiddleware(InvocationCounter counter)
        : IMiddleware<ConsumeContext<string>>
    {
        /// <inheritdoc />
        public async Task InvokeAsync(ConsumeContext<string> context, IMiddlewarePipeline<ConsumeContext<string>> next)
        {
            counter.Increment();
            await next.InvokeAsync(context);
        }
    }
}
