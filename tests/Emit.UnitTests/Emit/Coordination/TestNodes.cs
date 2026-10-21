namespace Emit.UnitTests.Coordination;

using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;
using Emit.Abstractions.Observability;
using Emit.Configuration;
using Emit.Daemon;
using Emit.LeaderElection;
using Emit.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Builds the real coordination components of one node, with observers notified inline.
/// </summary>
internal static class TestNodes
{
    public static DaemonCoordinator CreateCoordinator(
        IDaemonAssignmentPersistence assignmentPersistence,
        ILeaderElectionPersistence leaderElectionPersistence,
        IEnumerable<IDaemonAgent> agents,
        DaemonOptions daemonOptions,
        LeaderElectionOptions leaderElectionOptions,
        INodeIdentity identity,
        TimeProvider timeProvider,
        TaskScheduler runScheduler,
        IDaemonObserver[]? daemonObservers = null,
        ILeaderElectionObserver[]? leaderElectionObservers = null)
    {
        AssertValid(leaderElectionOptions, daemonOptions);
        return new(
            assignmentPersistence,
            leaderElectionPersistence,
            agents,
            new DaemonObserverInvoker(daemonObservers ?? [], InlineTaskScheduler.Instance, NullLogger<DaemonObserverInvoker>.Instance),
            new LeaderElectionObserverInvoker(leaderElectionObservers ?? [], InlineTaskScheduler.Instance, NullLogger<LeaderElectionObserverInvoker>.Instance),
            Options.Create(daemonOptions),
            Options.Create(leaderElectionOptions),
            identity,
            timeProvider,
            new NodeObservations(timeProvider),
            runScheduler,
            NullLogger<DaemonCoordinator>.Instance);
    }

    public static HeartbeatWorker CreateWorker(
        ILeaderElectionPersistence persistence,
        DaemonCoordinator coordinator,
        LeaderElectionOptions options,
        INodeIdentity identity,
        TimeProvider timeProvider,
        TaskScheduler callbackScheduler,
        ILeaderElectionObserver[]? observers = null) =>
        new(
            persistence,
            coordinator,
            new LeaderElectionObserverInvoker(observers ?? [], InlineTaskScheduler.Instance, NullLogger<LeaderElectionObserverInvoker>.Instance),
            Options.Create(options),
            identity,
            timeProvider,
            callbackScheduler,
            NullLogger<HeartbeatWorker>.Instance);

    /// <summary>
    /// Fails unless a host would accept the timings: tests of the coordination must run a configuration the
    /// validators allow. The service name comes from the node identity, so it is not checked here.
    /// </summary>
    private static void AssertValid(LeaderElectionOptions leaderElectionOptions, DaemonOptions daemonOptions)
    {
        string[] failures =
        [
            .. (new LeaderElectionOptionsValidator().Validate(null, leaderElectionOptions).Failures ?? [])
                .Where(failure => !failure.Contains(nameof(LeaderElectionOptions.ServiceName), StringComparison.Ordinal)),
            .. new DaemonOptionsValidator(Options.Create(leaderElectionOptions)).Validate(null, daemonOptions).Failures ?? [],
        ];
        Assert.Empty(failures);
    }
}
