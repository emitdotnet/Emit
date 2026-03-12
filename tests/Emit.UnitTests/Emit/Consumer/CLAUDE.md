# Consumer/

Unit tests for consumer middleware: circuit breaker, rate limiter, validation middleware, and consume error handling.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `CircuitBreakerBuilderTests.cs` | Unit tests for CircuitBreakerBuilder configuration | Debugging circuit breaker builder test failures |
| `CircuitBreakerObserverTests.cs` | Unit tests for CircuitBreakerObserver state transitions | Debugging circuit breaker observer test failures |
| `ConsumeErrorMiddlewareTests.cs` | Unit tests for ConsumeErrorMiddleware exception handling and error policy dispatch | Debugging consume error middleware test failures |
| `RateLimitMiddlewareTests.cs` | Unit tests for RateLimitMiddleware throttling behavior | Debugging rate limit middleware test failures |
| `ValidationMiddlewareTests.cs` | Unit tests for ValidationMiddleware message validation and discard/retry outcomes | Debugging validation middleware test failures |
