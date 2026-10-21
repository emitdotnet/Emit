# Daemon/

Daemon coordination implementation: leader-driven assignment of IDaemonAgent instances to cluster nodes.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `NodeObservations.cs` | Tracking, on every node, of how long each registration of its service has gone without a new heartbeat, on the monotonic clock and up to when the read was issued: liveness and responsiveness without database or wall-clock time | Change how nodes are judged alive or dead |
| `DaemonCoordinator.cs` | Called on every heartbeat: every node runs its own assignments and, side by side, observes its service; the leader places and balances the daemons declared by live nodes with compare-and-swap writes, revoking a daemon from a live node before moving it, and giving no new daemon to a node seen giving one up while another node can take it | Modify daemon assignment logic or understand coordinator lifecycle |
| `OutboxDaemon.cs` | IDaemonAgent implementation representing the outbox processing daemon: delivers batches through one delivery per provider, handing each group's entries over in order while its run holds the assignment, and deletes a delivered batch in one call; a batch in which any entry fails is delivered again whole on the next poll, a delivery that cannot begin, or a provider that cannot reach its message broker, fails the run so the daemon can move | Understand how outbox processing is distributed across nodes |
