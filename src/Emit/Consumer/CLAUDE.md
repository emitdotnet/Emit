# Consumer/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `CircuitBreakerBuilder.cs` | Fluent builder for configuring circuit breaker error handling policies | Configure circuit breaker thresholds, break duration, or recovery behavior |
| `CircuitBreakerObserver.cs` | Observer implementing circuit breaker pattern for consumer error handling | Understand circuit breaker logic or debug handler protection |
| `DeadLetterTopicMap.cs` | Registry mapping source topics to dead letter queue topics | Configure or query DLQ topic mappings |
| `DlqTopicVerifier.cs` | Startup validation ensuring all configured DLQ topics exist in the broker | Troubleshoot missing DLQ topics or understand DLQ verification |
| `ErrorHandlingMiddleware.cs` | Middleware executing error policies (consume, retry, dead letter) based on error type | Implement error handling logic or debug error policy execution |
| `RateLimitMiddleware.cs` | Middleware enforcing rate limits on consumer message processing | Configure rate limiting or debug throttling behavior |
| `ValidationMiddleware.cs` | Middleware validating consumed messages and routing validation failures to error policies | Implement message validation or debug validation errors |
| `WorkerDistribution.cs` | Logic distributing consumed messages across worker tasks using distribution strategies | Understand consumer parallelism or debug message distribution |
