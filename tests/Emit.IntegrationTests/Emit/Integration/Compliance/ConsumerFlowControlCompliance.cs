namespace Emit.IntegrationTests.Integration.Compliance;

using System.Collections.Concurrent;
using Emit.Abstractions;
using Emit.Consumer;
using Emit.DependencyInjection;
using Emit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static Emit.IntegrationTests.Integration.TestHelpers;

/// <summary>
/// Compliance tests for pausing and resuming a consumer group declaration through the
/// <see cref="IConsumerFlowControl"/> resolved under its name: the pause holds through rebalances and an open
/// circuit, waits for the messages being handled, and resumes from where the group stopped. Derived classes
/// declare each <see cref="FlowControlGroup"/> on a <c>string, string</c> topic with a producer.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ConsumerFlowControlCompliance
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a test waits to show that nothing arrives. It can only make a test pass wrongly when the
    /// machine is slow, never fail it.
    /// </summary>
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(4);

    /// <summary>
    /// The consumer a declaration registers.
    /// </summary>
    public enum GroupConsumer
    {
        /// <summary><see cref="SinkConsumer{T}"/> writing to the host's <see cref="MessageSink{T}"/>.</summary>
        Sink,

        /// <summary><see cref="CountingToggleableConsumer"/>, failing while <see cref="ConsumerToggle.ShouldThrow"/>.</summary>
        Toggleable,

        /// <summary><see cref="GatedConsumer"/>, holding each message until <see cref="Gate"/> opens.</summary>
        Gated,

        /// <summary><see cref="PausingConsumer"/>, pausing its own group on the message <c>pause</c>.</summary>
        Pausing,

        /// <summary><see cref="BatchSinkConsumer{T}"/>, as a batch consumer.</summary>
        BatchSink,
    }

    /// <summary>
    /// A consumer group declaration of a test.
    /// </summary>
    /// <param name="Topic">The topic it consumes, created with <see cref="Partitions"/> partitions.</param>
    /// <param name="GroupId">Its group id.</param>
    /// <param name="Name">The name its flow control is resolved under.</param>
    public sealed record FlowControlGroup(string Topic, string GroupId, string Name)
    {
        /// <summary>Gets the number of partitions of the topic.</summary>
        public int Partitions { get; init; } = 1;

        /// <summary>Gets the consumer the declaration registers.</summary>
        public GroupConsumer Consumer { get; init; } = GroupConsumer.Sink;

        /// <summary>Gets the circuit breaker of the declaration, if any.</summary>
        public Action<CircuitBreakerBuilder>? CircuitBreaker { get; init; }
    }

    /// <summary>
    /// Configures the messaging provider with every declaration. Each topic gets a producer, and each
    /// declaration starts from the earliest offset.
    /// </summary>
    /// <param name="emit">The Emit builder to configure.</param>
    /// <param name="groups">The declarations.</param>
    protected abstract void ConfigureEmit(EmitBuilder emit, IReadOnlyList<FlowControlGroup> groups);

    /// <summary>
    /// Registers what reports the partitions assigned to and taken from the host's consumers to
    /// <paramref name="assignments"/>.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="assignments">The tracker to report to.</param>
    protected abstract void TrackAssignments(IServiceCollection services, AssignmentTracker assignments);

    /// <summary>
    /// Registers what reports the offsets the host's consumers commit to <paramref name="commits"/>, as the
    /// next offset to read.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="commits">The tracker to report to.</param>
    protected abstract void TrackCommits(IServiceCollection services, CommitTracker commits);

    // ── Circuit breaker ──

    [Fact]
    public async Task GivenGroupPausedByTheApplicationWhileItsCircuitIsOpen_WhenTheCircuitGoesHalfOpen_ThenNothingIsConsumedUntilTheApplicationResumes()
    {
        // Arrange: the circuit opens on two failures and goes half-open after its pause duration
        var circuitPause = TimeSpan.FromSeconds(2);
        var group = NewGroup() with
        {
            Consumer = GroupConsumer.Toggleable,
            CircuitBreaker = cb => cb.FailureThreshold(2).SamplingWindow(TimeSpan.FromSeconds(30)).PauseDuration(circuitPause).TripOn<InvalidOperationException>(),
        };
        var sink = new MessageSink<string>();
        var toggle = new ConsumerToggle { ShouldThrow = true };
        var failures = new InvocationCounter();
        using var host = BuildHost([group], sink, services => services.AddSingleton(toggle).AddSingleton(failures));
        await host.StartAsync();
        try
        {
            await ProduceAsync(host, group, "fail-1", "fail-2");
            await WaitUntilAsync(() => failures.Count >= 2, "The circuit's failures were not handled", ReceiveTimeout);
            var flowControl = FlowControlOf(host, group);
            await flowControl.PauseAsync(CancellationToken.None);
            toggle.ShouldThrow = false;
            await ProduceAsync(host, group, "held");

            // Act: the circuit goes half-open while the application still holds the group paused
            await Task.Delay(circuitPause + QuietPeriod);
            var receivedWhilePaused = sink.ReceivedMessages.Any(m => m.Message == "held");
            await flowControl.ResumeAsync(CancellationToken.None);

            // Assert
            Assert.False(receivedWhilePaused);
            await WaitForMessagesAsync(sink, ["held"]);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenGroupWhoseCircuitIsOpen_WhenTheApplicationPausesAndResumesIt_ThenNothingIsConsumedBeforeTheCircuitAllowsIt()
    {
        // Arrange: the circuit stays open for a minute
        var group = NewGroup() with
        {
            Consumer = GroupConsumer.Toggleable,
            CircuitBreaker = cb => cb.FailureThreshold(2).SamplingWindow(TimeSpan.FromSeconds(30)).PauseDuration(TimeSpan.FromMinutes(1)).TripOn<InvalidOperationException>(),
        };
        var sink = new MessageSink<string>();
        var toggle = new ConsumerToggle { ShouldThrow = true };
        var failures = new InvocationCounter();
        using var host = BuildHost([group], sink, services => services.AddSingleton(toggle).AddSingleton(failures));
        await host.StartAsync();
        try
        {
            await ProduceAsync(host, group, "fail-1", "fail-2");
            await WaitUntilAsync(() => failures.Count >= 2, "The circuit's failures were not handled", ReceiveTimeout);
            toggle.ShouldThrow = false;
            var flowControl = FlowControlOf(host, group);

            // Act
            await flowControl.PauseAsync(CancellationToken.None);
            await flowControl.ResumeAsync(CancellationToken.None);
            await ProduceAsync(host, group, "held");
            await Task.Delay(QuietPeriod);

            // Assert
            Assert.DoesNotContain(sink.ReceivedMessages, m => m.Message == "held");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    // ── Rebalances ──

    [Fact]
    public async Task GivenPartitionTakenByAnotherNodeWhileTheGroupWasPaused_WhenItComesBackAfterTheResume_ThenItsMessagesAreConsumed()
    {
        // Arrange
        var group = NewGroup() with { Partitions = 2 };
        var firstSink = new MessageSink<string>();
        var firstAssignments = new AssignmentTracker();
        using var first = BuildHost([group], firstSink, services => TrackAssignments(services, firstAssignments));
        await first.StartAsync();
        try
        {
            await WaitUntilAsync(() => firstAssignments.Count == 2, "The first node was not assigned both partitions", ReceiveTimeout);
            var flowControl = FlowControlOf(first, group);
            await flowControl.PauseAsync(CancellationToken.None);
            await JoinAndLeaveAsync(group, firstAssignments);
            await flowControl.ResumeAsync(CancellationToken.None);

            // Act
            var produced = await ProduceAcrossPartitionsAsync(first, group);

            // Assert
            await WaitForMessagesAsync(firstSink, produced);
        }
        finally
        {
            await first.StopAsync();
        }
    }

    [Fact]
    public async Task GivenPausedGroup_WhenRebalancesHandItsPartitionsBack_ThenNothingIsConsumedUntilTheResume()
    {
        // Arrange
        var group = NewGroup() with { Partitions = 2 };
        var firstSink = new MessageSink<string>();
        var firstAssignments = new AssignmentTracker();
        using var first = BuildHost([group], firstSink, services => TrackAssignments(services, firstAssignments));
        await first.StartAsync();
        try
        {
            await WaitUntilAsync(() => firstAssignments.Count == 2, "The first node was not assigned both partitions", ReceiveTimeout);
            var flowControl = FlowControlOf(first, group);
            await flowControl.PauseAsync(CancellationToken.None);
            await JoinAndLeaveAsync(group, firstAssignments);

            // Act
            var produced = await ProduceAcrossPartitionsAsync(first, group);
            await Task.Delay(QuietPeriod);
            var receivedWhilePaused = firstSink.ReceivedMessages.Count;
            await flowControl.ResumeAsync(CancellationToken.None);

            // Assert
            Assert.Equal(0, receivedWhilePaused);
            await WaitForMessagesAsync(firstSink, produced);
        }
        finally
        {
            await first.StopAsync();
        }
    }

    // ── Pause and resume ──

    [Fact]
    public async Task GivenMessageBeingHandled_WhenTheApplicationPauses_ThenThePauseCompletesOnlyOnceItIsHandled()
    {
        // Arrange
        var group = NewGroup() with { Consumer = GroupConsumer.Gated };
        var sink = new MessageSink<string>();
        var gate = new Gate();
        using var host = BuildHost([group], sink, services => services.AddSingleton(gate));
        await host.StartAsync();
        try
        {
            await ProduceAsync(host, group, "handled");
            await gate.Entered.WaitAsync(ReceiveTimeout);

            // Act
            var pause = FlowControlOf(host, group).PauseAsync(CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(1));
            var completedWhileHandled = pause.IsCompleted;
            gate.Open();

            // Assert
            Assert.False(completedWhileHandled);
            await pause.WaitAsync(ReceiveTimeout);
            Assert.Contains(sink.ReceivedMessages, m => m.Message == "handled");
        }
        finally
        {
            gate.Open();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenStoppedHost_WhenTheGroupIsPausedAndResumed_ThenNothingFails()
    {
        // Arrange
        var group = NewGroup();
        using var host = BuildHost([group], new MessageSink<string>());
        await host.StartAsync();
        var flowControl = FlowControlOf(host, group);
        await host.StopAsync();

        // Act
        var failure = await Record.ExceptionAsync(async () =>
        {
            await flowControl.PauseAsync(CancellationToken.None);
            await flowControl.ResumeAsync(CancellationToken.None);
        });

        // Assert
        Assert.Null(failure);
    }

    [Fact]
    public async Task GivenMessagesProducedWhileTheGroupIsPaused_WhenItResumes_ThenTheyAreConsumedInOrderAndCommitted()
    {
        // Arrange
        var group = NewGroup();
        var sink = new MessageSink<string>();
        var commits = new CommitTracker();
        using var host = BuildHost([group], sink, services => TrackCommits(services, commits));
        await host.StartAsync();
        try
        {
            await ProduceAsync(host, group, "before");
            await WaitForMessagesAsync(sink, ["before"]);
            var flowControl = FlowControlOf(host, group);
            await flowControl.PauseAsync(CancellationToken.None);
            string[] produced = ["m-1", "m-2", "m-3", "m-4"];
            await ProduceAsync(host, group, produced);
            await Task.Delay(QuietPeriod);
            var receivedWhilePaused = sink.ReceivedMessages.Count;

            // Act
            await flowControl.ResumeAsync(CancellationToken.None);

            // Assert: the four messages follow "before" at offsets 1 to 4, so the commit reaches offset 5
            Assert.Equal(1, receivedWhilePaused);
            await WaitForMessagesAsync(sink, produced);
            Assert.Equal(["before", .. produced], sink.ReceivedMessages.Select(m => m.Message));
            await WaitUntilAsync(() => commits.Highest(group.Topic) >= 5, "The resumed messages were not committed", ReceiveTimeout);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenGroupPausedBeforeTheHostStarts_WhenMessagesArrive_ThenNothingIsConsumedUntilTheResumeAndThenAllInOrder()
    {
        // Arrange: the partition is assigned during the pause, so it is paused when its first message arrives
        // and the client fetches again from that message on resume
        var group = NewGroup();
        var sink = new MessageSink<string>();
        using var host = BuildHost([group], sink);
        var flowControl = FlowControlOf(host, group);
        await flowControl.PauseAsync(CancellationToken.None);
        await host.StartAsync();
        try
        {
            string[] produced = ["held-1", "held-2", "held-3", "held-4", "held-5"];
            await ProduceAsync(host, group, produced);
            await Task.Delay(QuietPeriod);
            var receivedWhilePaused = sink.ReceivedMessages.Count;

            // Act
            await flowControl.ResumeAsync(CancellationToken.None);

            // Assert
            Assert.Equal(0, receivedWhilePaused);
            await WaitForMessagesAsync(sink, produced);
            Assert.Equal(produced, sink.ReceivedMessages.Select(m => m.Message));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenOneGroupIdDeclaredOnTwoTopicsUnderTwoNames_WhenOneNameIsPaused_ThenOnlyThatDeclarationStops()
    {
        // Arrange
        var groupId = $"group-{Guid.NewGuid():N}";
        var paused = new FlowControlGroup($"topic-{Guid.NewGuid():N}", groupId, $"paused-{Guid.NewGuid():N}");
        var running = new FlowControlGroup($"topic-{Guid.NewGuid():N}", groupId, $"running-{Guid.NewGuid():N}");
        var sink = new MessageSink<string>();
        using var host = BuildHost([paused, running], sink);
        await host.StartAsync();
        try
        {
            var flowControl = FlowControlOf(host, paused);
            await flowControl.PauseAsync(CancellationToken.None);

            // Act
            await ProduceAsync(host, paused, "held");
            await ProduceAsync(host, running, "flowing");
            await WaitForMessagesAsync(sink, ["flowing"]);
            await Task.Delay(QuietPeriod);
            var heldWhilePaused = sink.ReceivedMessages.Any(m => m.Message == "held");
            await flowControl.ResumeAsync(CancellationToken.None);

            // Assert
            Assert.False(heldWhilePaused);
            await WaitForMessagesAsync(sink, ["held"]);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenBatchConsumerGroupPaused_WhenMessagesArrive_ThenNothingIsConsumedUntilTheResume()
    {
        // Arrange
        var group = NewGroup() with { Consumer = GroupConsumer.BatchSink };
        var batches = new BatchSinkConsumer<string>();
        using var host = BuildHost([group], new MessageSink<string>(), services => services.AddSingleton(batches));
        await host.StartAsync();
        try
        {
            var flowControl = FlowControlOf(host, group);
            await flowControl.PauseAsync(CancellationToken.None);
            await ProduceAsync(host, group, "b-1", "b-2", "b-3");
            await Task.Delay(QuietPeriod);
            var receivedWhilePaused = batches.Messages.Count;

            // Act
            await flowControl.ResumeAsync(CancellationToken.None);

            // Assert
            Assert.Equal(0, receivedWhilePaused);
            await WaitUntilAsync(() => batches.Messages.Count >= 3, "The batch consumer did not receive the resumed messages", ReceiveTimeout);
            Assert.Equal(["b-1", "b-2", "b-3"], batches.Messages);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task GivenHandlerOfTheGroup_WhenItPausesItsOwnGroup_ThenItCompletesAndNothingElseIsConsumedUntilTheResume()
    {
        // Arrange
        var group = NewGroup() with { Consumer = GroupConsumer.Pausing };
        var sink = new MessageSink<string>();
        using var host = BuildHost([group], sink, services => services.AddSingleton(new PausedGroup(group.Name)));
        await host.StartAsync();
        try
        {
            // Act
            await ProduceAsync(host, group, PausingConsumer.PauseMessage);
            await WaitForMessagesAsync(sink, [PausingConsumer.PauseMessage]);
            await ProduceAsync(host, group, "after");
            await Task.Delay(QuietPeriod);
            var afterWhilePaused = sink.ReceivedMessages.Any(m => m.Message == "after");
            await FlowControlOf(host, group).ResumeAsync(CancellationToken.None);

            // Assert
            Assert.False(afterWhilePaused);
            await WaitForMessagesAsync(sink, ["after"]);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    // ── Helpers ──

    private static FlowControlGroup NewGroup() =>
        new($"test-flow-{Guid.NewGuid():N}", $"group-{Guid.NewGuid():N}", $"name-{Guid.NewGuid():N}");

    private static IConsumerFlowControl FlowControlOf(IHost host, FlowControlGroup group) =>
        host.Services.GetRequiredKeyedService<IConsumerFlowControl>(group.Name);

    private IHost BuildHost(
        IReadOnlyList<FlowControlGroup> groups,
        MessageSink<string> sink,
        Action<IServiceCollection>? configureServices = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(new HostTopics([.. groups.Select(g => g.Topic).Distinct()]));
                configureServices?.Invoke(services);
                services.AddEmit(emit => ConfigureEmit(emit, groups));
            })
            .Build();

    /// <summary>
    /// Starts a second node of the group and waits until it took a partition from the first, then stops it and
    /// waits until the first node holds every partition again.
    /// </summary>
    private async Task JoinAndLeaveAsync(FlowControlGroup group, AssignmentTracker firstAssignments)
    {
        var secondAssignments = new AssignmentTracker();
        using var second = BuildHost([group], new MessageSink<string>(), services => TrackAssignments(services, secondAssignments));
        await second.StartAsync();
        try
        {
            await WaitUntilAsync(
                () => secondAssignments.Count > 0 && firstAssignments.Count + secondAssignments.Count == group.Partitions,
                "The second node did not take a partition",
                ReceiveTimeout);
        }
        finally
        {
            await second.StopAsync();
        }

        await WaitUntilAsync(() => firstAssignments.Count == group.Partitions, "The first node did not get every partition back", ReceiveTimeout);
    }

    /// <summary>
    /// Produces to the group's topic. Producers are registered per message type, in the order the topics are
    /// declared, so the topic's producer is found by its position among the host's topics.
    /// </summary>
    private static async Task ProduceAsync(IHost host, FlowControlGroup group, params string[] values)
    {
        using var scope = host.Services.CreateScope();
        var topics = host.Services.GetRequiredService<HostTopics>().Topics;
        var producer = scope.ServiceProvider.GetServices<IEventProducer<string, string>>().ElementAt(topics.IndexOf(group.Topic));
        foreach (var value in values)
        {
            await producer.ProduceAsync(new EventMessage<string, string>(value, value));
        }
    }

    /// <summary>
    /// Produces enough messages under distinct keys for every partition to receive some.
    /// </summary>
    private static async Task<string[]> ProduceAcrossPartitionsAsync(IHost host, FlowControlGroup group)
    {
        string[] values = [.. Enumerable.Range(0, 20).Select(i => $"m-{i}")];
        await ProduceAsync(host, group, values);
        return values;
    }

    private static Task WaitForMessagesAsync(MessageSink<string> sink, IReadOnlyCollection<string> expected) =>
        WaitUntilAsync(
            () => expected.All(value => sink.ReceivedMessages.Any(m => m.Message == value)),
            $"Not every expected message was consumed: {string.Join(", ", expected)}",
            ReceiveTimeout);

    /// <summary>
    /// The partitions currently assigned to one host's consumers.
    /// </summary>
    public sealed class AssignmentTracker
    {
        private readonly ConcurrentDictionary<(string Topic, int Partition), byte> assigned = new();

        /// <summary>Gets the number of partitions currently assigned.</summary>
        public int Count => assigned.Count;

        /// <summary>Records partitions assigned to the host.</summary>
        public void Assigned(string topic, IEnumerable<int> partitions)
        {
            foreach (var partition in partitions)
            {
                assigned.TryAdd((topic, partition), 0);
            }
        }

        /// <summary>Records partitions taken from the host.</summary>
        public void Taken(string topic, IEnumerable<int> partitions)
        {
            foreach (var partition in partitions)
            {
                assigned.TryRemove((topic, partition), out _);
            }
        }
    }

    /// <summary>
    /// The highest offset committed per topic, fed by the provider's commit notifications.
    /// </summary>
    public sealed class CommitTracker
    {
        private readonly ConcurrentDictionary<string, long> highest = new();

        /// <summary>Records a committed offset.</summary>
        public void Committed(string topic, long offset) => highest.AddOrUpdate(topic, offset, (_, current) => Math.Max(current, offset));

        /// <summary>Gets the highest offset committed for a topic, or -1.</summary>
        public long Highest(string topic) => highest.TryGetValue(topic, out var offset) ? offset : -1;
    }

    /// <summary>
    /// The topics of a host, in the order they are declared.
    /// </summary>
    private sealed record HostTopics(List<string> Topics);

    /// <summary>
    /// Consumer that fails while <see cref="ConsumerToggle.ShouldThrow"/> is set, counting its failures, and
    /// otherwise writes the message to the sink.
    /// </summary>
    public sealed class CountingToggleableConsumer(ConsumerToggle toggle, InvocationCounter failures, MessageSink<string> sink)
        : IConsumer<string>
    {
        /// <inheritdoc />
        public Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            if (toggle.ShouldThrow)
            {
                failures.Increment();
                throw new InvalidOperationException("Simulated failure for circuit breaker test.");
            }

            return sink.WriteAsync(context, cancellationToken);
        }
    }

    /// <summary>
    /// Holds <see cref="GatedConsumer"/> on each message until opened.
    /// </summary>
    public sealed class Gate
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets a task that completes once a message reached the consumer.</summary>
        public Task Entered => entered.Task;

        /// <summary>Lets every held message through.</summary>
        public void Open() => opened.TrySetResult();

        internal Task PassAsync()
        {
            entered.TrySetResult();
            return opened.Task;
        }
    }

    /// <summary>
    /// Consumer that waits for its <see cref="Gate"/> before writing each message to the sink.
    /// </summary>
    public sealed class GatedConsumer(Gate gate, MessageSink<string> sink) : IConsumer<string>
    {
        /// <inheritdoc />
        public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            await gate.PassAsync().WaitAsync(cancellationToken);
            await sink.WriteAsync(context, cancellationToken);
        }
    }

    /// <summary>
    /// The name of the declaration <see cref="PausingConsumer"/> belongs to.
    /// </summary>
    public sealed record PausedGroup(string Name);

    /// <summary>
    /// Consumer that pauses its own group when it handles <see cref="PauseMessage"/>, then writes the message
    /// to the sink.
    /// </summary>
    public sealed class PausingConsumer(PausedGroup group, MessageSink<string> sink) : IConsumer<string>
    {
        /// <summary>The message that makes the consumer pause its group.</summary>
        public const string PauseMessage = "pause";

        /// <inheritdoc />
        public async Task ConsumeAsync(ConsumeContext<string> context, CancellationToken cancellationToken)
        {
            if (context.Message == PauseMessage)
            {
                var flowControl = context.Services.GetRequiredKeyedService<IConsumerFlowControl>(group.Name);
                await flowControl.PauseAsync(cancellationToken);
            }

            await sink.WriteAsync(context, cancellationToken);
        }
    }
}
