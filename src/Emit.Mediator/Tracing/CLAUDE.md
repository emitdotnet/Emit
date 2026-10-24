# Tracing/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MediatorTracingMiddleware.cs` | Outermost mediator middleware starting a span named after the request type, covering the whole dispatch, with enrichers and error status | Understand mediator tracing |
