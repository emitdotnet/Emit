# Pipeline/

Unit tests for the core pipeline: consumer filter middleware, feature collection, inbound filter extensions, message pipeline builder, and configurable pipeline extensions.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ConsumerPipelineCircuitBreakerTests.cs` | Composed consumer pipelines reporting each attempt exactly once to the circuit breaker, with and without a retry policy, filters and validators that throw included | Debugging circuit breaker counting failures |
| `BatchPerItemAdapterTests.cs` | Per-item middleware lifted onto a batch: surviving items, dropped items, and the batch narrowed to the items not yet dropped before an exception propagates | Debugging per-item batch filtering or validation |
| `ConsumerFilterMiddlewareTests.cs` | Unit tests for ConsumerFilterMiddleware predicate evaluation and skip behavior | Debugging consumer filter middleware test failures |
| `FeatureCollectionTests.cs` | Unit tests for FeatureCollection get/set and type resolution | Debugging feature collection test failures |
| `InboundFilterExtensionsTests.cs` | Unit tests for inbound filter extension methods | Debugging inbound filter extension test failures |
| `MessagePipelineBuilderTests.cs` | Unit tests for MessagePipelineBuilder middleware registration and build | Debugging pipeline builder test failures |
| `PipelineConfigurableExtensionsTests.cs` | Unit tests for pipeline configurable extension methods | Debugging pipeline configurable extension test failures |
