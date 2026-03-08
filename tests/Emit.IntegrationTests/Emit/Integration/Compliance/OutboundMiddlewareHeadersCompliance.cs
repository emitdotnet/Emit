namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests verifying that outbound middleware can add, modify, and remove
/// headers before a message is produced, and that those changes are visible to the consumer.
/// Derived classes configure a <c>string, string</c> topic with a producer and consumer group,
/// and handle both direct and outbox produce paths.
/// </summary>
[Trait("Category", "Integration")]
public abstract class OutboundMiddlewareHeadersCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing <see cref="SinkConsumer{TMessage}"/> of <see cref="string"/>.
    /// Also registers <see cref="HeaderManipulationMiddleware"/> on <c>emit.OutboundPipeline</c>.
    /// When <paramref name="useOutbox"/> is <c>true</c>, configures a persistence provider with
    /// an outbox and sets the producer to use the outbox.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    /// <param name="useOutbox">Whether to configure the producer in outbox mode.</param>
    protected abstract void ConfigureEmit(EmitBuilder emit, string topic, string groupId, bool useOutbox);

    /// <summary>
    /// Produces a message with the given headers. When <paramref name="useOutbox"/> is <c>true</c>,
    /// the derived class wraps the produce in a transaction and commits it.
    /// </summary>
    /// <param name="services">The host-level service provider.</param>
    /// <param name="message">The message to produce.</param>
    /// <param name="useOutbox">Whether the outbox path is active.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    protected abstract Task ProduceAsync(
        IServiceProvider services,
        EventMessage<string, string> message,
        bool useOutbox,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies that outbound middleware can add, modify, and remove headers, and that
    /// the consumer observes the resulting header state after delivery.
    /// Runs for both direct produce and outbox produce paths.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenOutboundMiddlewareThatModifiesHeaders_WhenConsumed_ThenHeadersReflectMiddlewareChanges(
        bool useOutbox)
    {
        // Arrange
        var topic = $"test-mw-headers-{(useOutbox ? "outbox-" : "")}{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmit(emit, topic, groupId, useOutbox));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce with initial headers that the middleware will manipulate.
            var message = new EventMessage<string, string>(
                "k",
                "payload",
                [
                    new("x-original", "before"),
                    new("x-ephemeral", "remove-me"),
                ]);

            await ProduceAsync(host.Services, message, useOutbox);

            // Assert — consumer received the message.
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("payload", ctx.Message);

            var headers = ctx.Features.Get<IHeadersFeature>();
            Assert.NotNull(headers);

            // Header added by middleware is present.
            var injected = headers.Headers.FirstOrDefault(h => h.Key == "x-injected").Value;
            Assert.Equal("injected-value", injected);

            // Header modified by middleware has the new value.
            var modified = headers.Headers.FirstOrDefault(h => h.Key == "x-original").Value;
            Assert.Equal("after", modified);

            // Header removed by middleware is absent.
            var removed = headers.Headers.FirstOrDefault(h => h.Key == "x-ephemeral").Value;
            Assert.Null(removed);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Outbound middleware that adds, modifies, and removes headers to exercise
    /// the full range of header manipulation in the outbound pipeline.
    /// </summary>
    public sealed class HeaderManipulationMiddleware : IMiddleware<OutboundContext<string>>
    {
        /// <inheritdoc />
        public async Task InvokeAsync(OutboundContext<string> context, MessageDelegate<OutboundContext<string>> next)
        {
            var existing = context.Features.Get<IHeadersFeature>()?.Headers
                ?? [];

            var modified = new List<KeyValuePair<string, string>>();

            foreach (var header in existing)
            {
                // Remove: skip x-ephemeral entirely.
                if (header.Key == "x-ephemeral")
                    continue;

                // Modify: change the value of x-original.
                if (header.Key == "x-original")
                {
                    modified.Add(new("x-original", "after"));
                    continue;
                }

                modified.Add(header);
            }

            // Add: inject a new header.
            modified.Add(new("x-injected", "injected-value"));

            context.Features.Set<IHeadersFeature>(new HeadersFeature(modified));

            await next(context);
        }
    }
}
