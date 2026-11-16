# Daemon/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ExpiredRowsCleanupDaemon.cs` | Base of the cleanup daemons: a periodic timer on the run's assignment, a primary connection per cleanup, errors logged and retried on the next tick | Change how the cleanup daemons run |
| `LockCleanupDaemon.cs` | `emit:lock-cleanup`: deletes expired distributed lock rows | Understand or troubleshoot expired lock cleanup |
| `InboxCleanupDaemon.cs` | `emit:inbox-cleanup`: deletes expired inbox records in batches, skipping rows another service's daemon is deleting | Understand or troubleshoot expired inbox cleanup |
