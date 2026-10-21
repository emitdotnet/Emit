namespace Emit.UnitTests.Daemon;

using System.Collections.Concurrent;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Daemon;
using global::Emit.Abstractions.Metrics;
using global::Emit.Configuration;
using global::Emit.Daemon;
using global::Emit.LeaderElection;
using global::Emit.Metrics;
using global::Emit.Models;
using global::Emit.Observability;
using global::Emit.UnitTests.Coordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

public class OutboxDaemonTests
{
    private const string ServiceName = "test-service";
    private const string ProviderId = "test-provider";

    private static readonly DaemonRunContext Run = new(CancellationToken.None, generation: 7, fencingKey: Guid.NewGuid(), holdsAssignment: () => true);

    private readonly Guid nodeId = Guid.NewGuid();
    private readonly Mock<IOutboxRepository> mockRepository = new();

    private readonly TransactionalProvider provider = new();
    private readonly OutboxOptions options = new()
    {
        PollingInterval = TimeSpan.FromSeconds(1),
        BatchSize = 100
    };

    private readonly PollClock time;

    public OutboxDaemonTests()
    {
        time = new PollClock(options.PollingInterval);
    }

    [Fact]
    public async Task GivenEntryThatCannotBeSent_WhenItsBatchIsDelivered_ThenNoneOfTheBatchIsVisibleUntilTheWholeBatchIsDeliveredInOneGo()
    {
        // Arrange: a batch is all or nothing; a batch that fails is read again on the next poll
        var failing = CreateEntry("entry-1", "group-1", sequence: 1);
        var heldBack = CreateEntry("entry-2", "group-1", sequence: 2);
        var otherGroup = CreateEntry("entry-3", "group-2", sequence: 3);
        var failures = 0;
        provider.SendFailure = entry => entry == failing && Interlocked.Increment(ref failures) == 1
            ? new InvalidOperationException("Delivery failed")
            : null;
        var batchesHandled = SetupBatches([failing, heldBack, otherGroup], [failing, heldBack, otherGroup]);
        List<IReadOnlyCollection<object>> deletes = [];
        mockRepository
            .Setup(r => r.DeleteAsync(It.IsAny<IReadOnlyCollection<object>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<object> ids, CancellationToken _) => deletes.Add(ids))
            .Returns(Task.CompletedTask);
        var daemon = CreateDaemon([provider]);

        // Act
        await RunUntilAsync(daemon, batchesHandled);

        // Assert: the batch is delivered and deleted once, in one database call, each group in order
        Assert.Equal([failing.Id, heldBack.Id], provider.Visible.Where(id => id != otherGroup.Id));
        Assert.Equal([failing.Id, heldBack.Id, otherGroup.Id], provider.Visible.Order());
        Assert.Equal([failing.Id!, heldBack.Id!, otherGroup.Id!], Assert.Single(deletes).Order());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenProviderThatCannotDeliverFromThisNode_WhenTheDaemonDelivers_ThenItsRunFailsSoTheDaemonCanMoveToAnotherNode(
        bool deliveryCannotBegin)
    {
        // Arrange: a batch that fails is retried in place, but a node that cannot reach the message broker would
        // otherwise hold the outbox forever while other nodes could deliver it
        Exception unreachable = deliveryCannotBegin
            ? new InvalidOperationException("No transaction coordinator.")
            : new MessageBrokerUnreachableException("No partition leader.", new TimeoutException());
        if (deliveryCannotBegin)
        {
            provider.OnBegin = () => throw unreachable;
        }
        else
        {
            provider.SendFailure = _ => unreachable;
        }

        _ = SetupBatches([CreateEntry("entry-1", "group-1", sequence: 1)]);
        var daemon = CreateDaemon([provider]);

        // Act
        var run = daemon.RunAsync(Run);
        var failure = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(30)));

        // Assert
        Assert.Same(unreachable, failure);
        Assert.Empty(provider.Visible);
    }

    [Fact]
    public async Task GivenEntryTheSystemRefusesAsItIsHandedOver_WhenItsGroupIsDelivered_ThenNoneOfTheBatchBecomesVisible()
    {
        // Arrange: a system can refuse an entry on the spot, as a Kafka client refuses an oversized message, without
        // that failing what it was already given; nothing of the batch may become visible, or later entries of the
        // group would appear before the refused one
        var first = CreateEntry("entry-1", "group-1", sequence: 1);
        var refused = CreateEntry("entry-2", "group-1", sequence: 2);
        var after = CreateEntry("entry-3", "group-1", sequence: 3);
        provider.OnSend = entry => entry == refused ? Task.FromException(new InvalidOperationException("The message is too large.")) : Task.CompletedTask;
        var batchesHandled = SetupBatches([first, refused, after], [first, refused, after]);
        var daemon = CreateDaemon([provider]);

        // Act
        await RunUntilAsync(daemon, batchesHandled);

        // Assert
        Assert.Empty(provider.Visible);
        mockRepository.Verify(r => r.DeleteAsync(It.IsAny<IReadOnlyCollection<object>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GivenRunOfTheDaemon_WhenItDelivers_ThenTheProviderReceivesTheRunsFencingKeyAndGeneration()
    {
        // Arrange: the provider fences earlier runs with them
        var batchHandled = SetupBatches([CreateEntry("entry-1", "group-1", sequence: 1)]);
        var daemon = CreateDaemon([provider]);

        // Act
        await RunUntilAsync(daemon, batchHandled);

        // Assert
        var context = Assert.Single(provider.Contexts);
        Assert.Equal((Run.FencingKey, Run.Generation), (context.FencingKey, context.Generation));
    }

    [Fact]
    public async Task GivenFullBatchDelivered_WhenTheDaemonGoesOn_ThenItReadsTheNextBatchWithoutWaitingForThePollInterval()
    {
        // Arrange: a full batch means the outbox likely holds more, and a backlog drains at full speed
        options.BatchSize = 1;
        var backlogDrained = SetupBatches([CreateEntry("entry-1", "group-1", sequence: 1)], [CreateEntry("entry-2", "group-1", sequence: 2)]);
        var daemon = CreateDaemon([provider]);
        using var cts = new CancellationTokenSource();

        // Act
        var run = daemon.RunAsync(Run with { AssignmentToken = cts.Token });

        // Assert: the clock never moved
        await backlogDrained.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(["entry-1", "entry-2"], provider.Visible);
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GivenMoreGroupsThanTheLimit_WhenABatchIsDispatched_ThenOnlyThatManyGroupsAreDeliveredAtOnce()
    {
        // Arrange
        options.MaxConcurrentGroups = 2;
        var active = 0;
        var peak = 0;
        var started = 0;
        var limitReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.OnSend = async _ =>
        {
            Interlocked.Increment(ref started);
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref peak, now);
            if (now == options.MaxConcurrentGroups)
            {
                limitReached.TrySetResult();
            }

            await release.Task;
            Interlocked.Decrement(ref active);
        };
        var batchHandled = SetupBatches(
        [
            CreateEntry("entry-1", "group-1", sequence: 1),
            CreateEntry("entry-2", "group-2", sequence: 2),
            CreateEntry("entry-3", "group-3", sequence: 3),
        ]);
        var daemon = CreateDaemon([provider]);
        using var cts = new CancellationTokenSource();
        var run = daemon.RunAsync(Run with { AssignmentToken = cts.Token });
        await limitReached.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Act
        release.SetResult();
        await PollUntilAsync(batchHandled);
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(3, started);
        Assert.Equal(options.MaxConcurrentGroups, peak);
    }

    [Fact]
    public async Task GivenOperationCanceledNotCausedByItsToken_WhenRunning_ThenKeepsRunning()
    {
        // Arrange: a driver or client timeout surfaces as an OperationCanceledException
        var secondBatchRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        mockRepository
            .Setup(r => r.GetBatchAsync(ServiceName, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return Task.FromException<IReadOnlyList<OutboxEntry>>(new OperationCanceledException("Client timeout"));
                }

                secondBatchRead.TrySetResult();
                return Task.FromResult<IReadOnlyList<OutboxEntry>>([]);
            });
        var daemon = CreateDaemon();
        using var cts = new CancellationTokenSource();

        // Act
        var run = daemon.RunAsync(Run with { AssignmentToken = cts.Token });
        await PollUntilAsync(secondBatchRead.Task);

        // Assert
        Assert.False(run.IsCompleted);
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenDaemonStoppedWhileAnEntryIsDelivered_WhenTheSendSucceeds_ThenItCompletesTheDeliveryAndDeletesTheEntryOnlyWithinTheDrainTimeoutOfTheStop(
        bool sendOutlivesTheDrainTimeout)
    {
        // Arrange: the daemon is stopped as its node shuts down, still holding its assignment. An entry left behind
        // after delivery is delivered again by the next owner, but the daemon must end within the drain timeout of
        // being stopped, which the options validation keeps inside the time the node stays registered
        var drainTimeout = TimeSpan.FromSeconds(15);
        var entry = CreateEntry("entry-1", "group-1", sequence: 1);
        using var cts = new CancellationTokenSource();
        bool? deleteStartedWithLiveToken = null;
        provider.OnSend = _ =>
        {
            cts.Cancel();
            if (sendOutlivesTheDrainTimeout)
            {
                time.Advance(drainTimeout);
            }

            return Task.CompletedTask;
        };
        mockRepository
            .Setup(r => r.GetBatchAsync(ServiceName, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([entry]);
        mockRepository
            .Setup(r => r.DeleteAsync(It.Is<IReadOnlyCollection<object>>(ids => ids.Contains(entry.Id!)), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyCollection<object> _, CancellationToken token) =>
            {
                deleteStartedWithLiveToken = !token.IsCancellationRequested;
                return Task.CompletedTask;
            });
        var daemon = CreateDaemon([provider], new DaemonOptions { DrainTimeout = drainTimeout });

        // Act
        await daemon.RunAsync(Run with { AssignmentToken = cts.Token }).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(sendOutlivesTheDrainTimeout ? null : true, deleteStartedWithLiveToken);
        Assert.Equal(sendOutlivesTheDrainTimeout ? [] : [entry.Id], provider.Visible);
    }

    [Fact]
    public async Task GivenRegisteredProviders_WhenTheDaemonPolls_ThenItReadsOnlyTheEntriesOfThoseProviders()
    {
        // Arrange: entries of a provider the node does not have stay pending for a node that has it
        var batchRead = SetupBatches();
        var daemon = CreateDaemon([provider]);

        // Act
        await RunUntilAsync(daemon, batchRead);

        // Assert
        mockRepository.Verify(
            r => r.GetBatchAsync(
                ServiceName,
                It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { ProviderId })),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GivenRunNoLongerHoldingItsAssignment_WhenTheDaemonPolls_ThenItReadsNothing()
    {
        // Arrange: a run can stop holding its assignment before its token is cancelled, as after a pause or a
        // suspend, and the leader may already have given the outbox to another node
        var daemon = CreateDaemon();
        using var cts = new CancellationTokenSource();
        var run = daemon.RunAsync(new DaemonRunContext(cts.Token, Run.Generation, Run.FencingKey, holdsAssignment: () => false));

        // Act
        for (var i = 0; i < 3; i++)
        {
            await time.PollWaitStarted.WaitAsync(TimeSpan.FromSeconds(30));
            time.Advance(options.PollingInterval);
        }

        // Assert
        mockRepository.Verify(r => r.GetBatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static OutboxEntry CreateEntry(string id, string groupKey, long sequence) =>
        new()
        {
            Id = id,
            SystemId = ProviderId,
            Destination = "kafka://localhost:9092/test-topic",
            GroupKey = groupKey,
            Sequence = sequence,
            EnqueuedAt = DateTime.UtcNow,
            Body = [1, 2, 3],
            Properties = new Dictionary<string, string>()
        };

    /// <summary>
    /// Returns <paramref name="batches"/> on the first batch reads and nothing afterwards. The returned task
    /// completes when the daemon asks for the batch after the last one, which it only does once that one is
    /// fully handled.
    /// </summary>
    private Task SetupBatches(params OutboxEntry[][] batches)
    {
        var nextBatchRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        mockRepository
            .Setup(r => r.GetBatchAsync(ServiceName, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var read = Interlocked.Increment(ref reads);
                if (read <= batches.Length)
                {
                    return Task.FromResult<IReadOnlyList<OutboxEntry>>(batches[read - 1]);
                }

                nextBatchRequested.TrySetResult();
                return Task.FromResult<IReadOnlyList<OutboxEntry>>([]);
            });
        return nextBatchRequested.Task;
    }

    private async Task RunUntilAsync(OutboxDaemon daemon, Task signal)
    {
        using var cts = new CancellationTokenSource();
        var run = daemon.RunAsync(Run with { AssignmentToken = cts.Token });
        await PollUntilAsync(signal);
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Lets each poll interval elapse as soon as the daemon waits for it, until <paramref name="signal"/> completes.
    /// </summary>
    private async Task PollUntilAsync(Task signal)
    {
        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!signal.IsCompleted)
        {
            var pollWaitStarted = time.PollWaitStarted.WaitAsync(giveUp.Token);
            if (await Task.WhenAny(signal, pollWaitStarted) == pollWaitStarted)
            {
                await pollWaitStarted;
                time.Advance(options.PollingInterval);
            }
        }

        await signal;
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private OutboxDaemon CreateDaemon(
        IEnumerable<IOutboxProvider>? providers = null,
        DaemonOptions? daemonOptions = null)
    {
        // The poll clock tells a wait between polls by its length, so the daemon's other timer must be longer
        daemonOptions ??= new DaemonOptions();
        Assert.True(daemonOptions.DrainTimeout > options.PollingInterval);

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider.GetService(typeof(IOutboxRepository)))
            .Returns(mockRepository.Object);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var metrics = new OutboxMetrics(null, new EmitMetricsEnrichment());
        var observerInvoker = new OutboxObserverInvoker(
            [],
            metrics,
            NullLogger<OutboxObserverInvoker>.Instance);

        return new OutboxDaemon(
            mockScopeFactory.Object,
            providers ?? [],
            observerInvoker,
            metrics,
            Options.Create(options),
            Options.Create(daemonOptions),
            Mock.Of<INodeIdentity>(n => n.NodeId == nodeId && n.ServiceName == ServiceName),
            time,
            NullLogger<OutboxDaemon>.Instance);
    }

    /// <summary>
    /// Signals each wait between polls the daemon starts, so a test lets a poll interval elapse only once the daemon
    /// waits for it. A wait is recognised by being no longer than the polling interval, so the daemon's other timers,
    /// such as query timeouts and the drain window, must be longer than it to go unsignalled.
    /// </summary>
    private sealed class PollClock(TimeSpan pollingInterval) : SteppingClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
        public SemaphoreSlim PollWaitStarted { get; } = new(0);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime > TimeSpan.Zero && dueTime <= pollingInterval)
            {
                PollWaitStarted.Release();
            }

            return timer;
        }
    }

    /// <summary>
    /// A provider of a transactional system: what a delivery sends becomes visible only when it completes, and a
    /// delivery in which a send failed cannot complete.
    /// </summary>
    private sealed class TransactionalProvider : IOutboxProvider
    {
        private readonly ConcurrentQueue<object> visible = new();

        public Func<OutboxEntry, Task> OnSend { get; set; } = _ => Task.CompletedTask;

        public Action OnBegin { get; set; } = () => { };

        /// <summary>
        /// Gets or sets why an entry handed over could not be sent, found out when its delivery completes.
        /// </summary>
        public Func<OutboxEntry, Exception?> SendFailure { get; set; } = _ => null;

        public ConcurrentQueue<OutboxDeliveryContext> Contexts { get; } = new();

        public IReadOnlyCollection<object> Visible => visible;

        public string SystemId => ProviderId;

        public Task<IOutboxDelivery> BeginDeliveryAsync(OutboxDeliveryContext context, CancellationToken cancellationToken = default)
        {
            Contexts.Enqueue(context);
            OnBegin();
            return Task.FromResult<IOutboxDelivery>(new Delivery(this));
        }

        private sealed class Delivery(TransactionalProvider provider) : IOutboxDelivery
        {
            private readonly ConcurrentQueue<object> sent = new();
            private Exception? failure;

            public async Task SendAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
            {
                await provider.OnSend(entry);

                // Kept in the order entries are handed over, as a transactional system keeps them
                sent.Enqueue(entry.Id!);
                if (provider.SendFailure(entry) is { } failed)
                {
                    Interlocked.CompareExchange(ref failure, failed, null);
                }
            }

            public Task CompleteAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (failure is not null)
                {
                    throw failure;
                }

                foreach (var id in sent)
                {
                    provider.visible.Enqueue(id);
                }

                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
