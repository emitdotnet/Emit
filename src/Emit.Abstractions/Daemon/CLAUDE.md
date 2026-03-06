# Daemon/

Daemon assignment abstractions: contracts for daemons that can be distributed across cluster nodes by the leader.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `IDaemonAgent.cs` | Contract for a named daemon that can be assigned to a node; implement to create a distributable work unit | Implement a new distributable background service |
| `IDaemonAssignmentPersistence.cs` | Persistence contract for reading and writing daemon assignments | Implement daemon assignment storage for a new persistence backend |
| `DaemonAssignment.cs` | Record representing an assignment of a named daemon to a specific node | Understand the assignment data model |
| `DaemonAssignmentState.cs` | Enum of daemon assignment lifecycle states (Assigned, Released, etc.) | Understand or match assignment state transitions |
