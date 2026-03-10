# Markers/

Sentinel records registered in DI to signal which persistence and outbox providers have been configured.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `RegistrationMarkers.cs` | PersistenceProviderMarker, OutboxProviderMarker, OutboxRegistrationMarker, and DistributedLockRegistrationMarker sentinel types | Implement provider registration validation or understand how providers signal their presence |
