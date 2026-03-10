# Abstractions/

Unit tests for core distributed lock base class and event producer extensions.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `DistributedLockProviderBaseTests.cs` | Unit tests for DistributedLockProviderBase retry loop and backoff behavior | Debugging distributed lock provider base test failures |
| `DistributedLockTests.cs` | Unit tests for DistributedLock acquisition and release | Debugging distributed lock test failures |
| `EventProducerExtensionsTests.cs` | Unit tests for IEventProducer extension methods | Debugging event producer extension test failures |
| `TestRandomProvider.cs` | Test helper: deterministic IRandomProvider for backoff tests | Understanding how distributed lock base tests control randomness |
