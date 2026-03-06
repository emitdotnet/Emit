# Emit/

Core outbox library: configuration, DI registration, pipeline builder, and background workers.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Core library architecture, component interaction, design decisions, and invariants | Understand core architecture or design rationale |
| `EmitContext.cs` | Scoped context providing access to the active transaction for outbox operations | Set or access the transaction context before producing messages |
| `DefaultRandomProvider.cs` | IRandomProvider implementation wrapping Random.Shared for backoff jitter | Understand or replace random number generation |
| `ActivityFeature.cs` | IActivityFeature implementation capturing W3C traceparent, tracestate, and baggage from the current Activity | Understand how distributed trace context is captured before outbox enqueue |
| `ConsumerIdentityFeature.cs` | IConsumerIdentityFeature implementation holding consumer identifier, kind, type, and route key | Understand how consumer identity is stamped on the pipeline context |
| `HeadersFeature.cs` | IHeadersFeature implementation wrapping an immutable list of string key-value header pairs | Understand how message headers are surfaced in the pipeline |
| `KeyFeature.cs` | IKeyFeature implementation wrapping a typed message key | Understand how message keys are surfaced in the pipeline |
| `RawBytesFeature.cs` | IRawBytesFeature implementation holding raw key and value bytes from the transport | Understand how raw transport bytes are made available for dead-lettering |
| `RetryAttemptFeature.cs` | IRetryAttemptFeature implementation holding the current retry attempt number | Understand how retry attempt count is tracked and exposed to middleware |
| `Emit.csproj` | Core library project file with MessagePack, Transactional, and Options dependencies | Configure core library dependencies or package metadata |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Configuration/` | Options classes and IValidateOptions validators for worker and core settings | Add or modify configuration options |
| `Consumer/` | Circuit breaker, error handling, validation, rate limiting, and distribution middleware | Implement consumer middleware or debug consumer behavior |
| `DependencyInjection/` | EmitBuilder fluent API and AddEmit service registration | Extend the registration API or add new top-level services |
| `Metrics/` | Core emit metrics and metrics middleware for consume/produce operations | Monitor performance, set up dashboards, or customize instrumentation |
| `Observability/` | Observer middleware invokers for consume, produce, and outbox events | Implement custom observability or understand observer invocation |
| `Pipeline/` | Concrete pipeline builder and configuration extension methods | Understand pipeline construction or modify builder behavior |
| `Daemon/` | DaemonCoordinator and OutboxDaemon background services for distributed outbox processing | Modify outbox daemon scheduling or coordinator behavior |
| `LeaderElection/` | HeartbeatWorker background service managing heartbeat and leader status | Modify leader election timing or heartbeat behavior |
| `RateLimiting/` | Rate limit builder for consumer rate limiting configuration | Configure consumer rate limits |
| `Routing/` | Content-based message routing mapping route keys to typed consumer handlers | Implement message routing or debug route dispatch |
| `Tracing/` | Distributed tracing middleware, activity helpers, and tracing configuration | Implement custom tracing or debug trace propagation |
