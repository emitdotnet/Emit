# LeaderElection/

Unit tests for leader election: heartbeat worker lifecycle and renewal.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `HeartbeatWorkerTests.cs` | Unit tests for HeartbeatWorker renewal, failure handling, local lease expiry, fencing, giving up the lease after a failed heartbeat, and the request it sends | Debugging heartbeat worker test failures |
