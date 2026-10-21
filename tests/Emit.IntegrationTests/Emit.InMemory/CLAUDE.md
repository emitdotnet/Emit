# Emit.InMemory/

Runs the leader election and daemon assignment compliance suites against the in-memory store used by the unit test simulations (linked from `tests/Emit.UnitTests/Emit/Coordination/InMemoryCoordinationStore.cs`). No container is needed.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `InMemoryLeaderElectionCompliance.cs` | Inherits LeaderElectionCompliance; proves the in-memory store matches the providers' leader election semantics | Debug a divergence between the simulation store and the providers |
| `InMemoryDaemonAssignmentCompliance.cs` | Inherits DaemonAssignmentCompliance; proves the in-memory store matches the providers' daemon assignment semantics | Debug a divergence between the simulation store and the providers |
