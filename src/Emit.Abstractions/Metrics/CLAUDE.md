# Metrics/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `EmitMetricsEnrichment.cs` | Extension methods for enriching metric tags with emit-specific context | Add custom metric enrichment or understand tag propagation |
| `LockMetrics.cs` | Distributed lock metrics: acquisition duration, hold duration, release operations | Monitor lock performance or debug lock contention |
| `MeterNames.cs` | The names of Emit's own meters (core, outbox, lock, daemon) | Reference meter names for observability configuration |
| `HistogramAdvice.cs` | The bucket boundaries Emit's histograms advise: seconds for durations, bytes for sizes | Create a histogram or change its buckets |
| `PackageMeter.cs` | A meter an optional Emit package registers in the service collection, which instrumentation subscribes to | Add the meters of a package |
