# Models/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `LockEntity.cs` | Distributed lock entity with key, lock ID, and expiry | Understand EF Core lock schema |
| `DaemonAssignmentEntity.cs` | Daemon assignment entity mapping daemons to nodes | Understand daemon assignment schema |
| `LeaderEntity.cs` | Leader election entity tracking current leader and lease | Understand leader election schema |
| `NodeEntity.cs` | Node registration entity tracking cluster membership | Understand node registration schema |
