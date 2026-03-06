# LeaderElection/

Leader election implementation: periodic heartbeat worker that races for leadership via persistence.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `HeartbeatWorker.cs` | BackgroundService that sends periodic heartbeats to ILeaderElectionPersistence and updates the in-memory ILeaderElectionService with the result | Modify heartbeat interval, leader election timing, or understand leader state transitions |
