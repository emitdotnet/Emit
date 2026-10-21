# Models/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `LockEntity.cs` | Distributed lock entity with key, lock ID, and expiry | Understand EF Core lock schema |
| `DaemonAssignmentEntity.cs` | Daemon assignment entity keyed by service name and daemon id | Understand daemon assignment schema |
| `LeaderEntity.cs` | Leader lease entity of a service, keyed by service name | Understand leader election schema |
| `NodeEntity.cs` | Node registration entity with its service name and declared daemon ids | Understand node registration schema |
