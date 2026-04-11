# Integration/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `TestHelpers.cs` | `WaitUntilAsync` polling helper for integration tests | Write any compliance or integration test that needs to poll for a condition |
| `AlwaysFailingConsumer.cs` | `IConsumer<string>` and `IBatchConsumer<string>` that always throw — for error policy and DLQ tests | Simulate persistent consumer failures in compliance tests |
| `ConsumerToggle.cs` | Toggle controlling whether `ToggleableConsumer`/`ToggleableBatchConsumer` throws | Simulate runtime failure toggling in circuit breaker tests |
| `DlqCaptureConsumer.cs` | `IConsumer<byte[]>` that forwards dead-lettered messages to a `MessageSink<byte[]>` | Register on DLQ topics to capture dead-lettered messages in compliance tests |
| `InvocationCounter.cs` | Thread-safe counter for tracking consumer handler invocations | Count handler invocations in retry, pipeline, or batch tests |
| `ToggleableBatchConsumer.cs` | `IBatchConsumer<string>` that throws or forwards to `BatchSinkConsumer<string>` based on toggle | Test batch circuit breaker open/close behavior |
| `ToggleableConsumer.cs` | `IConsumer<string>` that throws or writes to `MessageSink<string>` based on toggle | Test circuit breaker open/close behavior |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Compliance/` | Abstract compliance test base classes with [Fact] methods; provider test projects inherit these | Add a new compliance scenario, understand existing test contracts, or onboard a new provider |
