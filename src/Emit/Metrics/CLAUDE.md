# Metrics/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `EmitMetrics.cs` | Core Emit metrics: message counts, processing duration, error rates | Monitor overall Emit performance or set up dashboards |
| `ConsumeMetricsMiddleware.cs` | Middleware recording consumer metrics for each consumed message | Understand consumer metric recording or customize consumer instrumentation |
| `ProduceMetricsMiddleware.cs` | Middleware recording producer metrics for each produced message | Understand producer metric recording or customize producer instrumentation |
| `OutboxMetrics.cs` | Outbox-specific metrics: queue depth, processing lag, worker throughput | Monitor outbox health or debug processing delays |
