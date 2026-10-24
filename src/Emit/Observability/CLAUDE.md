# Observability/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ConsumeObserverMiddleware.cs` | Middleware invoking registered consume observers for each consumed message | Understand consume observer invocation or customize consumer observability |
| `OutboxObserverInvoker.cs` | Invoker calling registered outbox observers for outbox lifecycle events | Understand outbox observer invocation flow or customize outbox observability |
| `ProduceObserverMiddleware.cs` | Middleware invoking registered produce observers for each produced message | Understand produce observer invocation or customize producer observability |
| `DaemonObserverInvoker.cs` | Notifies registered daemon observers of daemon lifecycle events, in order and away from the heartbeat | Understand daemon observer invocation or customize daemon observability |
| `LeaderElectionObserverInvoker.cs` | Notifies registered leader election observers of election events, in order and away from the heartbeat | Understand leader election observer invocation or customize election observability |
| `ObserverNotificationQueue.cs` | Runs observer notifications one after another on a task scheduler, with a bounded backlog, so a slow observer never holds up the heartbeat; a bounded flush lets shutdown wait for pending notifications | Change how leader election and daemon observers are notified |
| `ObserverInvoker.cs` | Extension method invoking every observer with isolated error handling, so a failing observer affects no other | Invoke observers or change how observer failures are handled |
