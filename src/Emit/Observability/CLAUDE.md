# Observability/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ConsumeObserverMiddleware.cs` | Middleware invoking registered consume observers for each consumed message | Understand consume observer invocation or customize consumer observability |
| `OutboxObserverInvoker.cs` | Invoker calling registered outbox observers for outbox lifecycle events | Understand outbox observer invocation flow or customize outbox observability |
| `ProduceObserverMiddleware.cs` | Middleware invoking registered produce observers for each produced message | Understand produce observer invocation or customize producer observability |
| `DaemonObserverInvoker.cs` | Invoker calling registered daemon observers for daemon lifecycle events | Understand daemon observer invocation or customize daemon observability |
| `LeaderElectionObserverInvoker.cs` | Invoker calling registered leader election observers for election events | Understand leader election observer invocation or customize election observability |
