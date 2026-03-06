# Observability/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `IMediatorObserver.cs` | Observer interface for mediator lifecycle events (before/after send, errors) | Implement mediator monitoring, logging, or custom instrumentation |
| `MediatorObserverMiddleware.cs` | Middleware invoking registered mediator observers for each request | Understand observer invocation or customize mediator observability |
