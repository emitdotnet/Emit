# Modules/

Pipeline feature modules holding deferred configuration for circuit breaker and rate limiting, plus the filter and validation middleware.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `CircuitBreakerModule.cs` | Holds circuit breaker configuration; delegates to CircuitBreakerBuilder on build | Understand how circuit breaker config flows from builder to pipeline |
| `RateLimitModule.cs` | Holds rate limit configuration; delegates to RateLimitBuilder to produce a RateLimiter | Understand how rate limiting config flows from builder to pipeline |
| `FilterMiddleware.cs` | Runs registered consumer filters and short-circuits messages they reject | Understand consumer filter evaluation |
| `ValidationMiddleware.cs` | Validates messages before the handler and dead-letters or discards invalid ones inline | Understand how validators are resolved and how validation failures are handled |
