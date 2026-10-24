# Consumer/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `CircuitBreakerBuilder.cs` | Fluent builder for configuring circuit breaker error handling policies | Configure circuit breaker thresholds, break duration, or recovery behavior |
| `CircuitBreakerObserver.cs` | Circuit breaker counting failed handler attempts; while open it pauses the group and holds every attempt, and half-open it lets one probe through at a time | Understand circuit breaker logic or debug handler protection |
| `ConsumeErrorMiddleware.cs` | Outermost middleware catching handler errors and applying error policy (dead-letter or discard) | Implement error handling logic or debug error policy execution |
| `RateLimitMiddleware.cs` | Middleware enforcing rate limits on consumer message processing | Configure rate limiting or debug throttling behavior |
| `RetryMiddleware.cs` | Consume-pipeline middleware running each handler attempt: holds it while the circuit breaker is open and reports its outcome, and retries a failure when the matching error policy clause retries, with that clause's backoff | Understand retry behavior or debug retry exhaustion |
| `ValidationMiddleware.cs` | Middleware validating consumed messages and routing validation failures to error policies | Implement message validation or debug validation errors |
| `BatchValidationMiddleware.cs` | Middleware validating each item in a batch individually, filtering invalid items before handler | Debug batch validation or understand per-item validation flow |
| `WorkerDistribution.cs` | Logic distributing consumed messages across worker tasks using distribution strategies | Understand consumer parallelism or debug message distribution |
