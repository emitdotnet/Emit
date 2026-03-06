# Models/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `LockDocument.cs` | Distributed lock document with key as _id, lock ID, and expiry | Understand MongoDB lock schema |
| `SequenceCounter.cs` | Atomic counter document with group key ID and sequence value | Understand MongoDB sequence generation schema |
| `DaemonAssignmentDocument.cs` | Daemon assignment document mapping daemons to nodes | Understand daemon assignment schema |
| `LeaderDocument.cs` | Leader election document tracking current leader and lease | Understand leader election schema |
| `NodeDocument.cs` | Node registration document tracking cluster membership | Understand node registration schema |
