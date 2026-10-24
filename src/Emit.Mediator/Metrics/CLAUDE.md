# Metrics/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MediatorMetrics.cs` | Mediator-specific metrics: request duration, handler invocation counters, error rates | Monitor mediator performance or debug request handling |
| `MediatorMeterNames.cs` | The names of the mediator's meters, registered as package meters by `AddMediator` | Reference or disable the mediator meter |
| `MediatorMetricsMiddleware.cs` | Middleware recording mediator metrics for each request handled | Understand metric recording or customize mediator instrumentation |
