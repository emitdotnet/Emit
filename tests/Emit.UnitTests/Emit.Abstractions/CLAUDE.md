# Emit.Abstractions/

Unit tests for the Emit.Abstractions library: backoff strategies, dead-letter headers, message validation results, error handling, metrics, and pipeline contracts.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `BackoffTests.cs` | Unit tests for backoff delay calculation | Debugging backoff test failures or understanding backoff behavior |
| `DeadLetterHeadersTests.cs` | Unit tests for dead-letter header construction and values | Debugging dead-letter header test failures |
| `MessageValidationResultTests.cs` | Unit tests for MessageValidationResult construction and state | Debugging validation result test failures |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `ErrorHandling/` | Tests for ErrorAction and ErrorPolicyActionBuilder | Debugging error handling abstraction test failures |
| `Metrics/` | Tests for LockMetrics | Debugging lock metrics test failures |
| `Pipeline/` | Tests for MessagePipeline and MiddlewareDescriptor | Debugging pipeline abstraction test failures |
