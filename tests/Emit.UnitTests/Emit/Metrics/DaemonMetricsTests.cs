namespace Emit.Tests.Metrics;

using System.Diagnostics.Metrics;
using global::Emit.Abstractions;
using global::Emit.Abstractions.Daemon;
using global::Emit.Abstractions.LeaderElection;
using global::Emit.Abstractions.Metrics;
using global::Emit.Configuration;
using global::Emit.Daemon;
using global::Emit.LeaderElection;
using global::Emit.Metrics;
using global::Emit.UnitTests.Coordination;
using Moq;
using Xunit;

[Collection("MetricsTests")]
public sealed class DaemonMetricsTests : IDisposable
{
    private const string ServiceName = "test-service";

    private static readonly CancellationToken NotLeader = new(canceled: true);

    private readonly Mock<IDaemonAssignmentPersistence> mockAssignmentPersistence = new();
    private readonly SteppingClock clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid nodeId = Guid.NewGuid();
    private readonly List<Recorded> recorded = [];
    private readonly MeterListener listener = new();

    public DaemonMetricsTests()
    {
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MeterNames.EmitDaemon)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.Start();
    }

    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task GivenRegisteredDaemons_WhenTheirRunsStartStopEndAndLoseTheirLease_ThenTheRunsGaugeReportsEachDaemonByStateWithoutWaitingForATick()
    {
        // Arrange: identifiers unique to this test, since meters of other coordinators may still be alive. A daemon
        // never assigned here is still reported, so a daemon running nowhere shows up as zero rather than missing.
        var lifecycle = new ControllableDaemonAgent($"lifecycle-{Guid.NewGuid():N}");
        var endsUnasked = new ControllableDaemonAgent($"ends-unasked-{Guid.NewGuid():N}");
        var idle = new ControllableDaemonAgent($"idle-{Guid.NewGuid():N}");
        var coordinator = CreateCoordinator(lifecycle, endsUnasked, idle);

        // Assert
        Assert.Equal((0, 0), ObserveRuns(lifecycle.DaemonId));

        // Act
        SetupNodeAssignments(Assignment(lifecycle, DaemonAssignmentState.Active, 1), Assignment(endsUnasked, DaemonAssignmentState.Active, 1));
        await TickAsync(coordinator, Leases.ExpiringAfter(clock, TimeSpan.FromMinutes(1)));

        // Assert
        Assert.Equal((1, 0), ObserveRuns(lifecycle.DaemonId));
        Assert.Equal((1, 0), ObserveRuns(endsUnasked.DaemonId));

        // Act: a run that ended on its own is no longer running, even before the next tick notices
        endsUnasked.Return();

        // Assert
        Assert.Equal((0, 0), ObserveRuns(endsUnasked.DaemonId));

        // Act: a revoked run that does not stop yet
        lifecycle.IgnoresCancellation = true;
        SetupNodeAssignments(Assignment(lifecycle, DaemonAssignmentState.Revoking, 2));
        await TickAsync(coordinator, Leases.ExpiringAfter(clock, TimeSpan.FromMinutes(1)));

        // Assert
        Assert.Equal((0, 1), ObserveRuns(lifecycle.DaemonId));

        // Act
        lifecycle.FinishStop();

        // Assert
        Assert.Equal((0, 0), ObserveRuns(lifecycle.DaemonId));

        // Act: a node cut off from the database has no tick; its lease running out, here before its timer fired as
        // after a suspend, fences the run at once
        SetupNodeAssignments(Assignment(lifecycle, DaemonAssignmentState.Active, 3));
        await TickAsync(coordinator, Leases.ExpiringAfter(clock, TimeSpan.FromMinutes(1)));
        Assert.Equal((1, 0), ObserveRuns(lifecycle.DaemonId));
        clock.StepWallClock(TimeSpan.FromMinutes(1));

        // Assert
        Assert.Equal((0, 1), ObserveRuns(lifecycle.DaemonId));
        Assert.Equal((0, 0), ObserveRuns(idle.DaemonId));
    }

    [Fact]
    public async Task GivenADaemonThatStartsFailsAndIsStoppedOnRequest_WhenTheNodeRunsIt_ThenEachStartAndOnlyTheUnaskedEndAreCounted()
    {
        // Arrange
        var daemon = new ControllableDaemonAgent($"counted-{Guid.NewGuid():N}");
        var coordinator = CreateCoordinator(daemon);
        SetupNodeAssignments(Assignment(daemon, DaemonAssignmentState.Active, 1));

        // Act: started, then failed and started again
        await TickAsync(coordinator, Leases.Held());
        daemon.Crash();
        await TickAsync(coordinator, Leases.Held());

        // Assert
        Assert.Equal(2, Count("emit.daemon.starts", daemon.DaemonId));
        Assert.Equal(1, Count("emit.daemon.failures", daemon.DaemonId));

        // Act: stopped on request, which is no failure
        SetupNodeAssignments(Assignment(daemon, DaemonAssignmentState.Revoking, 2));
        await TickAsync(coordinator, Leases.Held());
        await TickAsync(coordinator, Leases.Held());

        // Assert
        Assert.False(daemon.IsRunning);
        Assert.Equal(2, Count("emit.daemon.starts", daemon.DaemonId));
        Assert.Equal(1, Count("emit.daemon.failures", daemon.DaemonId));
    }

    private DaemonCoordinator CreateCoordinator(params IDaemonAgent[] daemons) =>
        TestNodes.CreateCoordinator(
            mockAssignmentPersistence.Object,
            Mock.Of<ILeaderElectionPersistence>(),
            daemons,
            new DaemonOptions(),
            new LeaderElectionOptions(),
            Mock.Of<INodeIdentity>(n => n.NodeId == nodeId && n.ServiceName == ServiceName),
            clock,
            InlineTaskScheduler.Instance,
            metrics: new DaemonMetrics(null, new EmitMetricsEnrichment()));

    private Task TickAsync(DaemonCoordinator coordinator, LeaseTokenSource lease) =>
        coordinator.OnHeartbeatTickAsync(nodeId, NotLeader, () => lease, CancellationToken.None);

    private void SetupNodeAssignments(params DaemonAssignment[] assignments)
    {
        mockAssignmentPersistence
            .Setup(p => p.GetNodeAssignmentsAsync(ServiceName, nodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments);
    }

    private DaemonAssignment Assignment(IDaemonAgent daemon, DaemonAssignmentState state, long generation) =>
        new(daemon.DaemonId, nodeId, generation, state, DateTime.UtcNow);

    /// <summary>
    /// Reads the runs gauge of a daemon now, as running and stopping runs.
    /// </summary>
    private (int Running, int Stopping) ObserveRuns(string daemonId)
    {
        recorded.Clear();
        listener.RecordObservableInstruments();
        var runs = recorded.Where(r => r.Name == "emit.daemon.runs" && r.DaemonId == daemonId).ToList();
        return (Single(runs, "running"), Single(runs, "stopping"));

        static int Single(List<Recorded> runs, string state) => (int)Assert.Single(runs, r => r.State == state).Value;
    }

    private long Count(string instrumentName, string daemonId) =>
        recorded.Where(r => r.Name == instrumentName && r.DaemonId == daemonId).Sum(r => r.Value);

    private void Record(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? daemonId = null;
        string? state = null;
        foreach (var tag in tags)
        {
            switch (tag.Key)
            {
                case "daemon":
                    daemonId = tag.Value as string;
                    break;
                case "state":
                    state = tag.Value as string;
                    break;
            }
        }

        recorded.Add(new Recorded(instrument.Name, daemonId, state, value));
    }

    private sealed record Recorded(string Name, string? DaemonId, string? State, long Value);
}
