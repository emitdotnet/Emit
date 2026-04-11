# Consumer/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `CircuitBreakerBuilder.cs` | Fluent builder for configuring circuit breaker error handling policies | Configure circuit breaker thresholds, break duration, or recovery behavior |
| `CircuitBreakerObserver.cs` | Observer implementing circuit breaker pattern for consumer error handling | Understand circuit breaker logic or debug handler protection |
| `ConsumeErrorMiddleware.cs` | Outermost middleware catching handler errors and applying error policy (dead-letter or discard) | Implement error handling logic or debug error policy execution |
| `RateLimitMiddleware.cs` | Middleware enforcing rate limits on consumer message processing | Configure rate limiting or debug throttling behavior |
| `RetryMiddleware.cs` | Consume-pipeline middleware that retries message processing on failure with configurable backoff | Understand retry behavior or debug retry exhaustion |
| `ValidationMiddleware.cs` | Middleware validating consumed messages and routing validation failures to error policies | Implement message validation or debug validation errors |
| `BatchValidationMiddleware.cs` | Middleware validating each item in a batch individually, filtering invalid items before handler | Debug batch validation or understand per-item validation flow |
| `WorkerDistribution.cs` | Logic distributing consumed messages across worker tasks using distribution strategies | Understand consumer parallelism or debug message distribution |
