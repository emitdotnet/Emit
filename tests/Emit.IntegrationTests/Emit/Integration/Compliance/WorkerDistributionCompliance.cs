namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Abstractions.Pipeline;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for worker distribution strategies. Derived classes configure a
/// <c>string, string</c> topic with a specific worker count and distribution strategy.
/// </summary>
[Trait("Category", "Integration")]
public abstract class WorkerDistributionCompliance
{
    private const int WorkerCount = 4;
    private const int MessageCount = 5;

    /// <summary>
    /// Configures the messaging provider with a <c>byte[], string</c> topic whose consumer group
    /// uses <paramref name="workerCount"/> workers with the ByKeyHash distribution strategy.
    /// The consumer group must use <see cref="OrderTrackingConsumer"/>.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name.</param>
    /// <param name="groupId">The consumer group ID.</param>
    /// <param name="workerCount">The number of workers to configure.</param>
    protected abstract void ConfigureWithByKeyHash(
        EmitBuilder emit,
        string topic,
        string groupId,
        int workerCount);

    /// <summary>
    /// Verifies that null-keyed messages are all routed to worker 0 when ByKeyHash is configured,
    /// which means they are processed sequentially and arrive in the order they were produced.
    /// </summary>
    [Fact]
    public async Task GivenNullKey_WhenByKeyHashConfigured_ThenAllMessagesProcessedInOrder()
    {
        // Arrange
        var topic = $"test-dist-null-{Guid.NewGuid():N}";
        var groupId = $"group-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddEmit(emit => ConfigureWithByKeyHash(emit, topic, groupId, WorkerCount));
            })
            .Build();

        await host.StartAsync();

        try
        {
            // Act — produce messages with null keys; all should route to worker 0 (sequential).
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<byte[], string>>();

            for (var i = 0; i < MessageCount; i++)
            {
                await producer.ProduceAsync(
                    new EventMessage<byte[], string>(null!, $"seq-{i}"));
            }

            // Assert — all messages arrive; because they all route to worker 0 they are sequential.
            var received = new List<string>(MessageCount);
            for (var i = 0; i < MessageCount; i++)
            {
                var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(30));
                received.Add(ctx.Message!);
            }

            // All messages must arrive in production order since null keys always go to worker 0.
            for (var i = 0; i < MessageCount; i++)
            {
                Assert.Equal($"seq-{i}", received[i]);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Consumer that records received messages and forwards them to the sink.
    /// </summary>
    public sealed class OrderTrackingConsumer(MessageSink<string> sink) : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
            => sink.WriteAsync(context, cancellationToken);
    }
}
