namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for convention-based dead-letter routing. Derived classes configure a
/// <c>string, string</c> source topic whose consumer always fails, with a DLQ naming convention
/// that derives the DLQ topic name from the source topic. The DLQ topic must be pre-created
/// before the host starts to satisfy the DlqTopicVerifier check at startup.
/// </summary>
[Trait("Category", "Integration")]
public abstract class DeadLetterConventionCompliance
{
    /// <summary>
    /// Configures the messaging provider with a global DLQ naming convention, a
    /// <c>string, string</c> source topic whose consumer always fails and dead-letters
    /// using the convention (no explicit topic), and a consumer on the derived DLQ topic
    /// backed by <see cref="DlqCaptureConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="sourceTopic">The source topic name.</param>
    /// <param name="groupId">The consumer group ID for the source topic.</param>
    /// <param name="dlqTopic">
    /// The derived DLQ topic name (e.g., <c>sourceTopic + ".dlt"</c>).
    /// Must be pre-created before the host is started.
    /// </param>
    /// <param name="dlqGroupId">The consumer group ID for the DLQ topic.</param>
    protected abstract void ConfigureWithDlqConvention(
        EmitBuilder emit,
        string sourceTopic,
        string groupId,
        string dlqTopic,
        string dlqGroupId);

    /// <summary>
    /// Verifies that when a consumer always fails and no explicit DLQ topic is specified,
    /// the naming convention is applied and the message is delivered to the derived DLQ topic.
    /// </summary>
    [Fact]
    public async Task GivenDlqNamingConvention_WhenConsumerFails_ThenMessageDeadLetteredToConventionTopic()
    {
        // Arrange
        var sourceTopic = $"test-conv-src-{Guid.NewGuid():N}";
        var groupId = $"group-src-{Guid.NewGuid():N}";
        var dlqTopic = sourceTopic + ".dlt";
        var dlqGroupId = $"group-dlt-{Guid.NewGuid():N}";
        var dlqSink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(dlqSink);
                services.AddEmit(emit =>
                    ConfigureWithDlqConvention(emit, sourceTopic, groupId, dlqTopic, dlqGroupId));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce a message; the consumer always fails → DLQ via convention.
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();
            await producer.ProduceAsync(new EventMessage<string, string>("k", "failing-message"));

            // Assert — message arrived in the convention-derived DLQ topic.
            var ctx = await dlqSink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("failing-message", ctx.Message);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Consumer that always throws <see cref="InvalidOperationException"/>, triggering
    /// the configured dead letter policy on every invocation.
    /// </summary>
    public sealed class AlwaysFailingConsumer : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated consumer failure for convention DLQ test.");
    }

    /// <summary>
    /// Consumer that forwards dead-lettered messages to a <see cref="MessageSink{T}"/>.
    /// </summary>
    public sealed class DlqCaptureConsumer(MessageSink<string> sink) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
            => sink.WriteAsync(context, cancellationToken);
    }
}
