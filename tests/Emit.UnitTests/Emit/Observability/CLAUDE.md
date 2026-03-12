# Observability/

Unit tests for observer middleware: consume, produce, outbox, and leader election observer invocation.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ConsumeObserverMiddlewareTests.cs` | Unit tests for ConsumeObserverMiddleware observer dispatch and error isolation | Debugging consume observer middleware test failures |
| `LeaderElectionObserverInvokerTests.cs` | Unit tests for LeaderElectionObserverInvoker event dispatch | Debugging leader election observer invoker test failures |
| `OutboxObserverInvokerTests.cs` | Unit tests for OutboxObserverInvoker event dispatch | Debugging outbox observer invoker test failures |
| `ProduceObserverMiddlewareTests.cs` | Unit tests for ProduceObserverMiddleware observer dispatch and error isolation | Debugging produce observer middleware test failures |
