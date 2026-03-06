namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests verifying that custom headers set on an outbox-produced message are
/// preserved and available in the consumer pipeline after end-to-end delivery through the outbox.
/// Derived classes configure a persistence provider (with outbox) and a messaging provider.
/// </summary>
[Trait("Category", "Integration")]
public abstract class OutboxHeadersCompliance : IAsyncLifetime
{
    private const string CorrelationIdHeaderKey = "x-correlation-id";

    /// <summary>
    /// Configures the messaging and persistence layers for outbox header round-trip tests.
    /// The derived class must enable the transactional outbox on the persistence provider
    /// and register a <c>string, string</c> topic with both a producer (in outbox mode)
    /// and a consumer group backed by <see cref="SinkConsumer{T}"/> of <see cref="string"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="groupId">The consumer group ID.</param>
    /// <param name="pollingInterval">The outbox daemon polling interval.</param>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        string groupId,
        TimeSpan pollingInterval);

    /// <summary>
    /// Begins a transaction and produces a message with specific headers to the outbox,
    /// then commits the transaction.
    /// </summary>
    /// <param name="services">The host-level service provider.</param>
    /// <param name="key">The message key.</param>
    /// <param name="value">The message value.</param>
    /// <param name="headers">Custom headers to attach to the message.</param>
    /// <param name="ct">A cancellation token.</param>
    protected abstract Task ProduceWithHeadersAsync(
        IServiceProvider services,
        string key,
        string value,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Verifies that custom headers set at outbox-produce time are available in the consumer
    /// pipeline after the outbox daemon delivers the message.
    /// </summary>
    [Fact]
    public async Task GivenOutboxEntryWithCustomHeaders_WhenDelivered_ThenHeadersPreservedInConsumer()
    {
        // Arrange
        var topic = $"test-outbox-headers-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var pollingInterval = TimeSpan.FromSeconds(1);
        var sink = new MessageSink<string>();
        var correlationId = Guid.NewGuid().ToString("N");

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureEmit(emit, topic, groupId, pollingInterval));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce via outbox with a custom correlation-id header.
            await ProduceWithHeadersAsync(
                host.Services,
                "k",
                "header-test-payload",
                [new(CorrelationIdHeaderKey, correlationId)]);

            // Assert — message arrives at the consumer.
            var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("header-test-payload", ctx.Message);

            // Assert — the custom header is present in the consumer pipeline context.
            var headers = ctx.Features.Get<IHeadersFeature>();
            Assert.NotNull(headers);

            var receivedCorrelationId = headers.Headers
                .FirstOrDefault(h => h.Key == CorrelationIdHeaderKey).Value;

            Assert.NotNull(receivedCorrelationId);
            Assert.Equal(correlationId, receivedCorrelationId);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
