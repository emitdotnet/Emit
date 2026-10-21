# Emit/

Core outbox library: configuration, DI registration, pipeline builder, and background workers.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Core library architecture, component interaction, design decisions, and invariants | Understand core architecture or design rationale |
| `EmitContext.cs` | Scoped context exposing the active transaction and registering unit-of-work callbacks | Access the transaction context, or understand how OnSucceeded and OnFailed reach the current unit of work |
| `DefaultRandomProvider.cs` | IRandomProvider implementation wrapping Random.Shared for backoff jitter | Understand or replace random number generation |
| `Emit.csproj` | Core library project file with MessagePack, Transactional, and Options dependencies | Configure core library dependencies or package metadata |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Configuration/` | Options classes and IValidateOptions validators for worker and core settings | Add or modify configuration options |
| `Consumer/` | Circuit breaker, error handling, validation, rate limiting, and distribution middleware | Implement consumer middleware or debug consumer behavior |
| `DependencyInjection/` | EmitBuilder fluent API and AddEmit service registration | Extend the registration API or add new top-level services |
| `ErrorHandling/` | Placeholder directory; error handling types have moved to `src/Emit.Abstractions/ErrorHandling/` | Understand the current location of error handling abstractions |
| `Middleware/` | UnitOfWorkMiddleware bounding each handler's unit of work: transaction and OnSucceeded/OnFailed callbacks | Understand transactions, callbacks, or how a nested handler joins an enclosing transaction |
| `Metrics/` | Core emit metrics and metrics middleware for consume/produce operations | Monitor performance, set up dashboards, or customize instrumentation |
| `Observability/` | Observer middleware invokers for consume, produce, and outbox events | Implement custom observability or understand observer invocation |
| `Pipeline/` | Concrete pipeline builder and configuration extension methods | Understand pipeline construction or modify builder behavior |
| `Daemon/` | DaemonCoordinator and OutboxDaemon background services for distributed outbox processing | Modify outbox daemon scheduling or coordinator behavior |
| `LeaderElection/` | HeartbeatWorker background service managing heartbeat and leader status | Modify leader election timing or heartbeat behavior |
| `RateLimiting/` | Rate limit builder for consumer rate limiting configuration | Configure consumer rate limits |
| `Routing/` | Content-based message routing mapping route keys to typed consumer handlers | Implement message routing or debug route dispatch |
| `Tracing/` | Distributed tracing middleware, activity helpers, and tracing configuration | Implement custom tracing or debug trace propagation |
