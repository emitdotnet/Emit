# Metrics/

Unit tests for core metrics: consume metrics, produce metrics, outbox metrics, and emit-level metrics.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ConsumeMetricsMiddlewareTests.cs` | Unit tests for ConsumeMetricsMiddleware instrument recording | Debugging consume metrics middleware test failures |
| `EmitMetricsTests.cs` | Unit tests for top-level Emit metrics instruments | Debugging emit metrics test failures |
| `MetricsTestCollection.cs` | xUnit collection definition for metrics tests requiring isolated meter state | Understanding why metrics tests use a shared collection |
| `OutboxMetricsTests.cs` | Unit tests for outbox metrics instruments | Debugging outbox metrics test failures |
| `ProduceMetricsMiddlewareTests.cs` | Unit tests for ProduceMetricsMiddleware instrument recording | Debugging produce metrics middleware test failures |
