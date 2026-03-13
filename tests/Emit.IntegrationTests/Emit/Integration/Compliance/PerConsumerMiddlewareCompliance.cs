namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for per-consumer middleware. Derived classes configure a
/// <c>string, string</c> topic with two consumers in the same group:
/// <see cref="ConsumerWithMiddleware"/> has a counter middleware registered per-consumer,
/// while <see cref="ConsumerWithoutMiddleware"/> has none. Both consumers write to
/// a shared sink, enabling fan-out verification. The counter must be incremented exactly
/// once (only for <see cref="ConsumerWithMiddleware"/>).
/// </summary>
[Trait("Category", "Integration")]
public abstract class PerConsumerMiddlewareCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group with two consumers. <see cref="ConsumerWithMiddleware"/> must have
    /// <see cref="PerConsumerCounterMiddleware"/> registered via per-consumer configuration.
    /// <see cref="ConsumerWithoutMiddleware"/> has no per-consumer middleware. Both consumers
    /// write to the shared <see cref="MessageSink{T}"/> of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureWithPerConsumerMiddleware(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Verifies that per-consumer middleware fires only for the consumer it is registered on,
    /// not for other consumers in the same group.
    /// </summary>
    [Fact]
    public async Task GivenPerConsumerMiddleware_WhenMessageProduced_ThenMiddlewareFiresOnlyForThatConsumer()
    {
        // Arrange
        var topic = $"test-per-consumer-mw-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var counter = new GlobalPipelineCompliance.InvocationCounter();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(counter);
                services.AddEmit(emit => ConfigureWithPerConsumerMiddleware(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce one message; fan-out delivers it to both consumers.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "hello"));

            // Assert — both consumers receive the message (fan-out = 2 sink entries).
            await sink.WaitForMessageAsync();
            await sink.WaitForMessageAsync();
            Assert.Equal(2, sink.ReceivedMessages.Count);

            // Assert — per-consumer middleware fired exactly once (only for ConsumerWithMiddleware).
            Assert.Equal(1, counter.Count);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Consumer that has per-consumer middleware registered. Writes messages to the shared sink.
    /// </summary>
    public sealed class ConsumerWithMiddleware(MessageSink<string> sink) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => sink.WriteAsync(context, cancellationToken);
    }

    /// <summary>
    /// Consumer with no per-consumer middleware. Writes messages to the shared sink.
    /// </summary>
    public sealed class ConsumerWithoutMiddleware(MessageSink<string> sink) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => sink.WriteAsync(context, cancellationToken);
    }

    /// <summary>
    /// Per-consumer middleware that increments the shared <see cref="GlobalPipelineCompliance.InvocationCounter"/>.
    /// </summary>
    public sealed class PerConsumerCounterMiddleware(GlobalPipelineCompliance.InvocationCounter counter)
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
