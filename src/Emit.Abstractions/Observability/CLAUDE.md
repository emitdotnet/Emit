# Observability/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `IConsumeObserver.cs` | Observer interface for consumer lifecycle events (before/after consume, errors) | Implement consumer monitoring, logging, or custom instrumentation |
| `IDaemonObserver.cs` | Observer interface for daemon lifecycle events (assignment, release, errors) | Implement daemon monitoring, logging, or custom instrumentation |
| `ILeaderElectionObserver.cs` | Observer interface for leader election events (became leader, lost leadership) | Implement leader election monitoring, logging, or custom instrumentation |
| `IOutboxObserver.cs` | Observer interface for outbox lifecycle events (before/after processing, errors) | Implement outbox monitoring, logging, or custom instrumentation |
| `IProduceObserver.cs` | Observer interface for producer lifecycle events (before/after produce, errors) | Implement producer monitoring, logging, or custom instrumentation |
