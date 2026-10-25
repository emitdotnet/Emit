# Consumer/

Unit tests for consumer middleware: circuit breaker, rate limiter, validation middleware, and consume error handling.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `CircuitBreakerBuilderTests.cs` | Unit tests for CircuitBreakerBuilder configuration | Debugging circuit breaker builder test failures |
| `CircuitBreakerObserverTests.cs` | Unit tests for CircuitBreakerObserver state transitions, how it holds attempts while open or half-open, and reporting its state until disposed | Debugging circuit breaker observer test failures |
| `ConsumeErrorMiddlewareTests.cs` | Unit tests for ConsumeErrorMiddleware exception handling and error policy dispatch | Debugging consume error middleware test failures |
| `RecordingCircuitBreaker.cs` | Test circuit breaker recording each attempt and reported outcome in order | Assert what the pipeline reports to the circuit breaker |
| `RateLimitMiddlewareTests.cs` | Unit tests for RateLimitMiddleware throttling behavior | Debugging rate limit middleware test failures |
| `RetryMiddlewareTests.cs` | Retry middleware: a fresh scope per attempt, which clauses retry and how many times | Debugging retry middleware test failures |
