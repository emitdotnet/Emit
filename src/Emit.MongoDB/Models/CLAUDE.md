# Models/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `LockDocument.cs` | Distributed lock document with key as _id, lock ID, and expiry | Understand MongoDB lock schema |
| `SequenceCounter.cs` | Atomic counter document with group key ID and sequence value | Understand MongoDB sequence generation schema |
| `DaemonAssignmentDocument.cs` | Daemon assignment document, unique per service name and daemon id | Understand daemon assignment schema |
| `LeaderDocument.cs` | Leader lease document of a service, unique per service name | Understand leader election schema |
| `NodeDocument.cs` | Node registration document with its service name and declared daemon ids | Understand node registration schema |
