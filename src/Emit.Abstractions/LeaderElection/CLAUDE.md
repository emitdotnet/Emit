# LeaderElection/

Leader election abstractions: contracts for determining node role (leader vs follower) via heartbeat-based persistence.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ILeaderElectionService.cs` | Service contract exposing current node role and leader identity | Query leader status in application code or middleware |
| `ILeaderElectionPersistence.cs` | Persistence contract for heartbeat registration and leader acquisition, scoped per service name; decisions use recorded heartbeat ids, never database time | Implement leader election storage for a new persistence backend |
| `HeartbeatRequest.cs` | Record carrying the service name, node identity, declared daemon ids, lease duration, node TTL, the heartbeat id the lease and registration record, and the unrenewed lease heartbeat to take over | Understand the heartbeat data sent to persistence |
| `HeartbeatResult.cs` | Record returned by persistence: whether the node leads its service, the current leader, and the heartbeat that last wrote the lease | Understand how leadership is communicated back from persistence |
| `NodeRegistration.cs` | Record of a registered node of a service: the daemon ids it declared, its last heartbeat id and its registration TTL | Understand how the leader learns which nodes can run which daemons |
