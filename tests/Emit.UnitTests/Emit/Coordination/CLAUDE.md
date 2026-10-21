# Coordination/

Deterministic multi-node simulations: real HeartbeatWorker and DaemonCoordinator instances over a shared in-memory store, with explicit heartbeats and simulated time.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `InMemoryCoordinationStore.cs` | In-memory leader election and daemon assignment persistence with the providers' semantics; also linked into the integration tests, where it runs the compliance suites | Add a persistence operation, or simulate a database partition or calls that take effect but fail on the node. After changing it, run the `InMemory*Compliance` classes of the integration tests (no Docker needed), which hold it to the providers' semantics |
| `TestNodes.cs` | Builds a node's real DaemonCoordinator and HeartbeatWorker over the given persistence, with observers notified inline | Construct a coordinator or heartbeat worker in a test |
| `InlineTaskScheduler.cs` | Task scheduler that runs daemon starts, cancellations and lease token callbacks synchronously, so heartbeats in tests are deterministic | Construct a coordinator in a test |
| `ControllableDaemonAgent.cs` | Test daemon whose runs the test ends, crashes, fails at start or keeps going after cancellation; records concurrent runs | Simulate a misbehaving daemon |
| `Leases.cs` | Node leases for tests: one that never runs out, and one that ran out during a simulated suspend before its timers ran | Give a coordinator or a daemon run a lease in a test |
| `QueuedTaskScheduler.cs` | Task scheduler that holds what is scheduled on it until the test runs it, as a busy thread pool does | Test what happens before a token's cancellation or a callback gets to run |
| `SteppingClock.cs` | Fake clock whose wall clock steps independently of monotonic time and timers, as clock synchronization or a suspend does | Test clock steps or suspends |
| `ClusterSimulation.cs` | Harness running nodes of several services, with kill, graceful stop, partition, lost responses and a hung heartbeat loop, checking safety invariants after every round, stopped nodes included | Write a multi-node scenario |
| `ClusterSimulationTests.cs` | Scenarios for node failures, misbehaving daemons, rebalancing, and seeded random failure sequences across two services sharing a database | Debugging coordination failures across nodes |
