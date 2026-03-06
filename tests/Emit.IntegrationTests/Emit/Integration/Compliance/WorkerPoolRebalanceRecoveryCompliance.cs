namespace Emit.IntegrationTests.Integration.Compliance;

using Emit.Abstractions;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

/// <summary>
/// Compliance tests for worker pool restart after a broker-triggered rebalance caused by
/// the consumer poll loop stalling. A hanging consumer fills the worker channel, blocking
/// the poll loop's <c>WriteAsync</c> and preventing <c>Consume()</c> from being called.
/// Once <c>max.poll.interval.ms</c> elapses, the broker removes the consumer from the group.
/// After the hang is released and <c>Consume()</c> resumes, the rebalance callbacks fire,
/// the old pool is torn down, partitions are reassigned, and a fresh pool is started.
/// Derived classes configure a provider-specific producer and consumer group for a
/// <c>string, string</c> topic containing <see cref="HangingConsumer"/>.
/// </summary>
[Trait("Category", "Integration")]
public abstract class WorkerPoolRebalanceRecoveryCompliance
{
    /// <summary>
    /// Configures the messaging provider with a <c>string, string</c> topic that has a producer
    /// and a consumer group containing <see cref="HangingConsumer"/>. The consumer group must use
    /// the given <paramref name="bufferSize"/>, <paramref name="maxPollInterval"/>, and
    /// <paramref name="workerStopTimeout"/> values.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="topic">The topic name to register.</param>
    /// <param name="bufferSize">Bounded channel capacity per worker.</param>
    /// <param name="maxPollInterval">
    /// Maximum time the broker allows between <c>Consume()</c> calls before triggering a rebalance.
    /// </param>
    /// <param name="workerStopTimeout">Maximum time to drain workers on pool shutdown.</param>
    protected abstract void ConfigureEmit(
        EmitBuilder emit,
        string topic,
        int bufferSize,
        TimeSpan maxPollInterval,
        TimeSpan workerStopTimeout);

    [Fact]
    public async Task GivenHangingConsumer_WhenMaxPollIntervalExceeds_ThenWorkersRestartAndRecoverAfterRebalance()
    {
        // Arrange
        var topic = $"test-rebalance-{Guid.NewGuid():N}";
        var sink = new MessageSink<string>();
        var gate = new ConsumerGate();

        // BufferSize=1: m1 occupies the worker (hangs), m2 fills the channel,
        // m3 blocks WriteAsync in the poll loop — preventing Consume() from being called.
        const int bufferSize = 1;
        var maxPollInterval = TimeSpan.FromSeconds(10);
        var workerStopTimeout = TimeSpan.FromSeconds(5);

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(gate);
                services.AddEmit(emit =>
                {
                    ConfigureEmit(emit, topic, bufferSize, maxPollInterval, workerStopTimeout);
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            using var scope = host.Services.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, string>>();

            // Act — Step 1: Produce three messages to stall the poll loop.
            //   m1: worker dequeues and hangs on the gate.
            //   m2: sits in the bounded channel (capacity=1, now full).
            //   m3: poll loop reads from Kafka and calls WriteAsync — blocks because channel is full,
            //       preventing any further Consume() calls.
            await producer.ProduceAsync(new EventMessage<string, string>("k1", "hang-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k2", "hang-2"));
            await producer.ProduceAsync(new EventMessage<string, string>("k3", "hang-3"));

            // Act — Step 2: Wait longer than max.poll.interval.ms.
            // librdkafka detects the poll stall on its internal timer and leaves the consumer group.
            // The rebalance callbacks will fire on the next Consume() call, which happens after
            // the gate is released and the blocked WriteAsync unblocks.
            await Task.Delay(maxPollInterval + TimeSpan.FromSeconds(4));

            // Act — Step 3: Release the gate. This unblocks the hanging consumer (m1 completes),
            // draining the channel so WriteAsync(m3) can finish. The poll loop then calls Consume()
            // again, at which point the pending rebalance fires:
            //   OnPartitionsLost  → supervisor.StopAsync(), offsetManager.Clear()
            //   OnPartitionsAssigned → supervisor.Start() with a fresh worker pool
            // New HangingConsumer instances receive the same ConsumerGate singleton, which is
            // already completed, so they do not hang.
            gate.Release();

            // Act — Step 4: Allow time for rebalance callbacks and pool restart to complete.
            await Task.Delay(TimeSpan.FromSeconds(8));

            // Act — Step 5: Produce recovery messages. The freshly started worker pool should
            // pick these up and deliver them to the sink.
            await producer.ProduceAsync(new EventMessage<string, string>("k4", "recover-1"));
            await producer.ProduceAsync(new EventMessage<string, string>("k5", "recover-2"));

            // Assert — Both recovery messages must arrive. Redeliveries of hang-* messages
            // may appear first (partition was lost before offset commit), so drain until found.
            var found = new HashSet<string>();
            while (found.Count < 2)
            {
                var ctx = await sink.WaitForMessageAsync(TimeSpan.FromSeconds(60));
                if (ctx.Message is "recover-1" or "recover-2")
                {
                    found.Add(ctx.Message);
                }
            }

            Assert.Contains("recover-1", found);
            Assert.Contains("recover-2", found);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Shared gate used to block and then release <see cref="HangingConsumer"/> instances.
    /// Registered as a singleton so all consumer instances (including those created after
    /// a pool restart) share the same gate state.
    /// </summary>
    public sealed class ConsumerGate
    {
        private readonly TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Task that completes when <see cref="Release"/> is called.</summary>
        public Task Task => tcs.Task;

        /// <summary>Unblocks all consumers currently awaiting the gate.</summary>
        public void Release() => tcs.TrySetResult();
    }

    /// <summary>
    /// Consumer that blocks on <see cref="ConsumerGate"/> before writing to the sink.
    /// While the gate is pending the consumer hangs indefinitely, stalling the worker.
    /// Once released — including for consumer instances created after a pool restart —
    /// the gate task is already completed and the consumer proceeds immediately.
    /// </summary>
    public sealed class HangingConsumer(MessageSink<string> sink, ConsumerGate gate)
        : IConsumer<string>
    {
        /// <inheritdoc />
        public async Task ConsumeAsync(InboundContext<string> context, CancellationToken cancellationToken)
        {
            await gate.Task.ConfigureAwait(false);
            await sink.WriteAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
