# LeaderElection/

Leader election abstractions: contracts for determining node role (leader vs follower) via heartbeat-based persistence.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ILeaderElectionService.cs` | Service contract exposing current node role and leader identity | Query leader status in application code or middleware |
| `ILeaderElectionPersistence.cs` | Persistence contract for heartbeat registration and leader acquisition | Implement leader election storage for a new persistence backend |
| `HeartbeatRequest.cs` | Record carrying node identity and heartbeat timestamp for leader election | Understand the heartbeat data sent to persistence |
| `HeartbeatResult.cs` | Record returned by persistence indicating whether this node is now the leader | Understand how leadership is communicated back from persistence |
