# Pipeline/

Unit tests for the core pipeline: consumer filter middleware, feature collection, inbound filter extensions, and middleware levels.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ConsumerPipelineCircuitBreakerTests.cs` | Composed consumer pipelines reporting each attempt exactly once to the circuit breaker, with and without a retry policy, filters and validators that throw included | Debugging circuit breaker counting failures |
| `BatchPerItemAdapterTests.cs` | Per-item middleware lifted onto a batch: surviving items, dropped items, and the batch narrowed to the items not yet dropped before an exception propagates | Debugging per-item batch filtering or validation |
| `FeatureCollectionTests.cs` | Unit tests for FeatureCollection get/set and type resolution | Debugging feature collection test failures |
| `InboundFilterExtensionsTests.cs` | Unit tests for inbound filter extension methods | Debugging inbound filter extension test failures |
| `MessagePipelineBuilderTests.cs` | Middleware levels: middleware of base contexts running on derived pipelines outermost level first, generic definitions closed with the message type or rejected on registration, scoped resolution, short-circuit, a group level checked against batches | Debugging pipeline builder test failures |
| `BatchConsumerAdapterTests.cs` | Batch consumer adapter resolving the consumer from DI and passing the same context | Debugging batch consumer adapter test failures |
| `ConsumerPipelineComposerTests.cs` | Consumer pipeline composition with and without filters and validation, and a transactional consumer whose own middleware fails after the handler | Debugging consumer pipeline composition |
| `NullPayloadFilterExtensionsTests.cs` | `SkipNullPayloads()` registering a predicate that drops null messages | Debugging null payload filter test failures |
| `OutboxTerminalBuilderTests.cs` | Outbox terminal: the entry carries the node and service identity, and works without a transaction | Debugging outbox terminal test failures |
