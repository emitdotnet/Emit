# Modules/

Pipeline feature modules holding deferred configuration for circuit breaker, error handling, rate limiting, retry, and validation concerns.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `CircuitBreakerModule.cs` | Holds circuit breaker configuration; delegates to CircuitBreakerBuilder on build | Understand how circuit breaker config flows from builder to pipeline |
| `ErrorModule.cs` | Holds error policy configuration; builds ErrorPolicy for ConsumeErrorMiddleware | Understand how error policies are deferred from builder to pipeline construction |
| `RateLimitModule.cs` | Holds rate limit configuration; delegates to RateLimitBuilder to produce a RateLimiter | Understand how rate limiting config flows from builder to pipeline |
| `RetryModule.cs` | Holds retry configuration (max attempts, backoff) and RetryConfig record | Understand how retry config flows from builder to pipeline or add retry parameters |
| `ValidationModule.cs` | Holds validation configuration; resolves class-based or delegate validators from DI | Understand how validators are registered and resolved during pipeline construction |
