# Daemon/

Daemon assignment abstractions: contracts for daemons that can be distributed across cluster nodes by the leader.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `IDaemonAgent.cs` | Contract for a named daemon that can be assigned to a node; implement to create a distributable work unit | Implement a new distributable background service |
| `IDaemonAssignmentPersistence.cs` | Persistence contract for reading and writing daemon assignments, scoped per service name | Implement daemon assignment storage for a new persistence backend |
| `DaemonAssignment.cs` | Record representing an assignment of a named daemon to a specific node, with its generation and its stable fencing key | Understand the assignment data model |
| `DaemonRunContext.cs` | What a daemon run receives: its assignment token, whether it still holds its assignment at the moment of acting, and the generation and fencing key for fencing its writes in external systems | Implement a daemon whose writes must never overlap |
| `DaemonAssignmentState.cs` | Enum of daemon assignment lifecycle states (Assigned, Released, etc.) | Understand or match assignment state transitions |
