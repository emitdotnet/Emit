namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for fan-out delivery: verifies that all consumers registered on the same
/// consumer group each receive a copy of every produced message.
/// Derived classes configure a <c>string, string</c> topic with a single producer and a
/// consumer group containing both <see cref="ConsumerA"/> and <see cref="ConsumerB"/>.
/// Both consumers write to the shared <see cref="MessageSink{T}"/> of <see cref="string"/>.
/// </summary>
[Trait("Category", "Integration")]
public abstract class FanOutCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing both <see cref="ConsumerA"/> and <see cref="ConsumerB"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    protected abstract void ConfigureFanOut(EmitBuilder emit, string topic, string groupId);

    /// <summary>
    /// Verifies that a single produced message is delivered to both registered consumers.
    /// </summary>
    [Fact]
    public async Task GivenTwoConsumersOnSameGroup_WhenMessageProduced_ThenBothConsumersReceiveIt()
    {
        // Arrange
        var topic = $"test-fanout-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureFanOut(emit, topic, groupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "hello"));

            // Assert — both consumers should write to the sink, so we expect 2 messages.
            var first = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(first);

            var second = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(second);

            Assert.Equal(2, sink.ReceivedMessages.Count);
            Assert.All(sink.ReceivedMessages, ctx => Assert.Equal("hello", ctx.Message));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Consumer A for fan-out tests. Writes received messages to the shared <see cref="MessageSink{T}"/>.
    /// </summary>
    public sealed class ConsumerA(MessageSink<string> sink) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
            => sink.WriteAsync(context, cancellationToken);
    }

    /// <summary>
    /// Consumer B for fan-out tests. Writes received messages to the shared <see cref="MessageSink{T}"/>.
    /// </summary>
    public sealed class ConsumerB(MessageSink<string> sink) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
            => sink.WriteAsync(context, cancellationToken);
    }
}
