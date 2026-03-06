# Daemon/

Daemon coordination implementation: leader-driven assignment of IDaemonAgent instances to cluster nodes.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `DaemonCoordinator.cs` | Background service that runs only on the leader node; reads registered IDaemonAgents and assigns them via IDaemonAssignmentPersistence | Modify daemon assignment logic or understand coordinator lifecycle |
| `OutboxDaemon.cs` | IDaemonAgent implementation representing the outbox processing daemon; assignment triggers outbox poll activation on the assigned node | Understand how outbox processing is distributed across nodes |
