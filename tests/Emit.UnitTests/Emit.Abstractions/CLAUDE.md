# Emit.Abstractions/

Unit tests for the Emit.Abstractions library: backoff strategies, dead-letter headers, message validation results, error handling, metrics, and pipeline contracts.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `BackoffTests.cs` | Unit tests for backoff delay calculation | Debugging backoff test failures or understanding backoff behavior |
| `HeaderExtensionsTests.cs` | Unit tests for header lookup and upsert: keys match without regard to case, and an upserted key appears once | Debugging header extension test failures |
| `DeadLetterHeadersTests.cs` | Unit tests for dead-letter header construction and values | Debugging dead-letter header test failures |
| `EmitEndpointAddressTests.cs` | Endpoint addresses written as a URI and parsed back with every part kept | Debugging endpoint address test failures |
| `EventMessageTests.cs` | Unit tests for EventMessage: the Tombstone factory | Debugging event message test failures |
| `MessageValidationResultTests.cs` | Unit tests for MessageValidationResult construction and state | Debugging validation result test failures |
| `MessageBatchTests.cs` | MessageBatch: count, indexer, order, item transport contexts | Debugging message batch test failures |
| `MessageContextTests.cs` | Message contexts: replacing services, and replacing the message while keeping features and services | Debugging message context test failures |
| `TransactionalAttributeTests.cs` | [Transactional] attribute usage: classes only, not inherited, not repeated | Debugging transactional attribute test failures |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `ErrorHandling/` | Tests for ErrorAction and ErrorPolicyActionBuilder | Debugging error handling abstraction test failures |
| `Metrics/` | Tests for LockMetrics | Debugging lock metrics test failures |
