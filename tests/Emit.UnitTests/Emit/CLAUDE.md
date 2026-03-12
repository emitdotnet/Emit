# Emit/

Unit tests for the core Emit library: pipeline, consumer, daemon, routing, error handling, DI registration, metrics, and observability.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `EmitContextTests.cs` | Unit tests for EmitContext construction and property access | Debugging EmitContext test failures or understanding context behavior |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Abstractions/` | Tests for distributed lock base class and event producer extensions | Debugging distributed lock or event producer test failures |
| `Configuration/` | Tests for options validators: daemon, leader election, outbox | Debugging options validation test failures |
| `Consumer/` | Tests for circuit breaker, rate limit, validation, and consume error middleware | Debugging consumer middleware test failures |
| `Daemon/` | Tests for daemon coordinator and outbox daemon | Debugging daemon test failures |
| `DependencyInjection/` | Tests for EmitBuilder, observer extensions, error action builder, and service collection extensions | Debugging DI registration test failures |
| `ErrorHandling/` | Tests for error policy builder | Debugging error policy test failures |
| `LeaderElection/` | Tests for heartbeat worker | Debugging leader election test failures |
| `Metrics/` | Tests for consume, produce, outbox, and emit metrics | Debugging metrics test failures |
| `Models/` | Tests for OutboxEntry model | Debugging outbox entry model test failures |
| `Observability/` | Tests for consume, produce, outbox, and leader election observer middleware | Debugging observer middleware test failures |
| `Pipeline/` | Tests for consumer filter middleware, feature collection, and pipeline builder | Debugging pipeline test failures |
| `RateLimiting/` | Tests for rate limit builder | Debugging rate limiting test failures |
| `Routing/` | Tests for message router builder and invoker | Debugging routing test failures |
