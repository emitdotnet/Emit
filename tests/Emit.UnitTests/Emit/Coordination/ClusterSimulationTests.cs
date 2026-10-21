namespace Emit.UnitTests.Coordination;

using Xunit;

/// <summary>
/// Multi-node scenarios for leader election and daemon assignment across services sharing one
/// database. Every scenario is deterministic: heartbeats are explicit and time is simulated.
/// </summary>
public sealed class ClusterSimulationTests
{
    private const string Api = "api";
    private const string Worker = "worker";

    public enum NodeFailure
    {
        Partition,
        HungHeartbeatLoop,
        LosesResponses,
    }

    // --- Failures ---

    [Theory]
    [InlineData(NodeFailure.Partition)]
    [InlineData(NodeFailure.HungHeartbeatLoop)]
    [InlineData(NodeFailure.LosesResponses)]
    public async Task GivenNodeRunningDaemonFails_WhenItCannotRenewItsLease_ThenItStopsTheDaemonWhichMovesAndNeverRunsTwice(NodeFailure failure)
    {
        // Arrange: a node without daemons starts first and leads, so the node that fails is never the leader
        await using var cluster = new ClusterSimulation();
        cluster.StartNode(Worker);
        await cluster.RoundsAsync(2);
        var first = cluster.StartNode(Worker, "d");
        var second = cluster.StartNode(Worker, "d");
        await cluster.SettleAsync();
        var runner = first.IsRunning("d") ? first : second;
        var survivor = runner == first ? second : first;

        // Act: every round checks that no daemon runs on two nodes
        switch (failure)
        {
            case NodeFailure.Partition:
                runner.Connection.IsPartitioned = true;
                break;
            case NodeFailure.LosesResponses:
                // Its heartbeats keep reaching the database, so the leader sees it alive
                runner.Connection.LosesResponses = true;
                break;
            default:
                runner.HungRounds = int.MaxValue;
                break;
        }

        var leaseRounds = cluster.RoundsIn(cluster.LeaderElectionOptions.LeaseDuration);
        await cluster.RoundsAsync(leaseRounds);

        // Assert: a node that cannot renew its lease stops its daemon once the lease runs out
        Assert.False(runner.IsRunning("d"));

        // Act: a node that stopped heartbeating is removed once its registration expired, and the survivor
        // acknowledges the daemon on the round after. A node whose heartbeats still land stays registered: once its
        // lease ran out it declares no daemon, so the leader revokes it on the next round and moves it when the node
        // has not confirmed within its registration TTL, and the survivor acknowledges it on the round after.
        var registrationRounds = cluster.RoundsIn(cluster.LeaderElectionOptions.NodeRegistrationTtl);
        await cluster.RoundsAsync(failure == NodeFailure.LosesResponses ? registrationRounds + 2 : registrationRounds - leaseRounds + 1);

        // Assert
        Assert.True(survivor.IsRunning("d"));
    }

    [Fact]
    public async Task GivenLeaderWhoseHeartbeatLoopHangs_WhenItsLeaseExpires_ThenAnotherNodeLeadsAndTheHungOneNoLongerReportsLeadership()
    {
        // Arrange
        await using var cluster = new ClusterSimulation();
        var first = cluster.StartNode(Worker);
        var second = cluster.StartNode(Worker);
        await cluster.SettleAsync();
        var leader = first.Worker.IsLeader ? first : second;
        var follower = leader == first ? second : first;
        var leaseRounds = cluster.RoundsIn(cluster.LeaderElectionOptions.LeaseDuration) + 1;

        // Act: every round also checks that the two never lead at once. A leader whose heartbeats fail steps down
        // at once; one whose heartbeat loop hangs is fenced only by its lease running out.
        leader.HungRounds = 2 * leaseRounds;

        await cluster.RoundsAsync(leaseRounds);

        // Assert
        Assert.True(follower.Worker.IsLeader);
        Assert.False(leader.Worker.IsLeader);
    }

    // --- Robustness ---

    [Fact]
    public async Task GivenDaemonThatKeepsFailingOnItsNode_WhenAnotherNodeDeclaresIt_ThenItMovesThere()
    {
        // Arrange: a run fails on every round until the failure limit, after which the node gives the daemon up to
        // the other node that offers it
        await using var cluster = new ClusterSimulation();
        var broken = cluster.StartNode(Worker, "d");
        broken.Agent("d").FailNextRuns = cluster.DaemonOptions.MaxConsecutiveFailures;
        await cluster.RoundsAsync(2);
        var healthy = cluster.StartNode(Worker, "d");

        // Act
        await cluster.RoundsAsync(cluster.DaemonOptions.MaxConsecutiveFailures + 1);
        await cluster.SettleAsync();

        // Assert
        Assert.Equal(cluster.DaemonOptions.MaxConsecutiveFailures, broken.Agent("d").RunCount);
        Assert.False(broken.IsRunning("d"));
        Assert.True(healthy.IsRunning("d"));
    }

    [Fact]
    public async Task GivenAllDaemonsOnOneNode_WhenMoreNodesJoin_ThenDaemonsSpreadEvenlyAndStayPut()
    {
        // Arrange
        await using var cluster = new ClusterSimulation();
        string[] daemons = ["a", "b", "c", "d"];
        var first = cluster.StartNode(Worker, daemons);
        await cluster.SettleAsync();
        Assert.All(daemons, d => Assert.True(first.IsRunning(d)));

        // Act
        var others = Enumerable.Range(0, 3).Select(_ => cluster.StartNode(Worker, daemons)).ToList();

        // Rebalancing moves one daemon at a time and waits for its drain, so spreading three takes several settles
        var nodes = others.Prepend(first).ToList();
        for (var settles = 0; settles < daemons.Length && !nodes.All(n => daemons.Count(n.IsRunning) == 1); settles++)
        {
            await cluster.SettleAsync();
        }

        var runs = nodes.SelectMany(n => n.Agents).Sum(a => a.RunCount);
        await cluster.SettleAsync();

        // Assert: balanced, and a balanced cluster moves nothing again
        Assert.All(nodes, n => Assert.Equal(1, daemons.Count(n.IsRunning)));
        Assert.Equal(runs, nodes.SelectMany(n => n.Agents).Sum(a => a.RunCount));
        cluster.AssertConverged();
    }

    // --- Randomized failure sequences ---

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 30; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task GivenRandomFailureSequence_WhenEveryRoundRuns_ThenSafetyHoldsAndClusterConverges(int seed)
    {
        // Arrange
        var random = new Random(seed);
        await using var cluster = new ClusterSimulation();
        string[] services = [Api, Worker];
        string[] daemonIds = ["d1", "d2", "d3", "emit:outbox"];

        // Daemons slow to stop end their run within the drain timeout, as every daemon must
        List<(ControllableDaemonAgent Agent, int RoundsLeft)> slowToStop = [];
        var drainRounds = cluster.RoundsIn(cluster.DaemonOptions.DrainTimeout);

        // Act: every round checks the safety invariants
        for (var step = 0; step < 150; step++)
        {
            var alive = cluster.Nodes.Where(n => n.State == SimNodeState.Running).ToList();
            var roll = random.Next(100);

            if (roll < 15 && alive.Count < 8)
            {
                var declared = daemonIds.Where(_ => random.Next(2) == 0).ToArray();
                cluster.StartNode(services[random.Next(services.Length)], declared);
            }
            else if (roll < 22 && alive.Count > 0)
            {
                cluster.Kill(alive[random.Next(alive.Count)]);
            }
            else if (roll < 28 && alive.Count > 0)
            {
                await cluster.StopAsync(alive[random.Next(alive.Count)]);
            }
            else if (roll < 36 && alive.Count > 0)
            {
                var node = alive[random.Next(alive.Count)];
                node.Connection.IsPartitioned = !node.Connection.IsPartitioned;
            }
            else if (roll < 48 && alive.Count > 0)
            {
                alive[random.Next(alive.Count)].HungRounds = random.Next(1, 12);
            }
            else if (roll < 54 && alive.Count > 0)
            {
                var running = alive.SelectMany(n => n.Agents).Where(a => a.IsRunning).ToList();
                if (running.Count > 0)
                {
                    running[random.Next(running.Count)].Crash();
                }
            }
            else if (roll < 60 && alive.Count > 0)
            {
                var node = alive[random.Next(alive.Count)];
                node.Connection.LosesResponses = !node.Connection.LosesResponses;
            }
            else if (roll < 66 && alive.Count > 0)
            {
                var running = alive.SelectMany(n => n.Agents).Where(a => a.IsRunning && !a.IgnoresCancellation).ToList();
                if (running.Count > 0)
                {
                    var agent = running[random.Next(running.Count)];
                    agent.IgnoresCancellation = true;
                    slowToStop.Add((agent, random.Next(1, drainRounds + 1)));
                }
            }

            for (var i = slowToStop.Count - 1; i >= 0; i--)
            {
                var (agent, roundsLeft) = slowToStop[i];
                if (roundsLeft > 1)
                {
                    slowToStop[i] = (agent, roundsLeft - 1);
                    continue;
                }

                agent.IgnoresCancellation = false;
                agent.FinishStop();
                slowToStop.RemoveAt(i);
            }

            await cluster.RoundAsync();
        }

        // Slow daemons still stopping end their runs within the drain timeout, as the others did
        foreach (var (agent, _) in slowToStop)
        {
            agent.IgnoresCancellation = false;
            agent.FinishStop();
        }

        foreach (var node in cluster.Nodes)
        {
            node.Connection.IsPartitioned = false;
            node.Connection.LosesResponses = false;
            node.HungRounds = 0;
        }

        await cluster.SettleAsync();

        // Assert
        cluster.AssertConverged();
    }
}
