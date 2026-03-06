# Compliance/

Abstract compliance test base classes. Each class defines [Fact] methods that every provider must pass. Provider test projects inherit these and supply infrastructure.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `CircuitBreakerCompliance.cs` | Circuit breaker open/close/reset behavior | Add circuit breaker tests or debug circuit breaker issues |
| `ConsumeObserverCompliance.cs` | IConsumeObserver lifecycle event delivery | Add observer tests or debug observer invocation |
| `DaemonAssignmentCompliance.cs` | Daemon assignment and redistribution by leader | Add daemon tests or debug distributed daemon behavior |
| `DeadLetterCompliance.cs` | Dead-letter queue delivery after exhausted retries | Add DLQ tests or debug dead-letter routing |
| `DeadLetterConventionCompliance.cs` | DLQ message format and header conventions | Verify or change the dead-letter message envelope contract |
| `DistributedLockCompliance.cs` | Mutual exclusion, TTL expiry, and lock renewal | Add distributed lock tests or debug locking behavior |
| `ErrorPolicyCompliance.cs` | Error policy evaluation: retry, discard, and dead-letter branching | Add error policy tests or debug error handling |
| `ExternalMessageCompliance.cs` | Consuming messages produced outside Emit (no KafkaPayload wrapper) | Debug external message ingestion or verify raw message support |
| `FanOutCompliance.cs` | Multiple consumer groups independently consuming the same topic | Add fan-out tests or debug independent consumer group behavior |
| `FilterCompliance.cs` | Consumer filter predicate evaluation | Add filter tests or debug filter skipping behavior |
| `GlobalPipelineCompliance.cs` | Global middleware execution order and context propagation | Add global pipeline tests or debug cross-cutting middleware |
| `LeaderElectionCompliance.cs` | Leader election, failover, and follower promotion | Add leader election tests or debug leadership transitions |
| `OffsetCommitCompliance.cs` | Watermark-based offset commit correctness | Add offset commit tests or debug at-least-once delivery |
| `OutboxDeliveryCompliance.cs` | Transactional outbox enqueue and Kafka delivery | Add outbox delivery tests or debug outbox-to-Kafka flow |
| `OutboxHeadersCompliance.cs` | Message header propagation through the outbox | Verify or change header forwarding behavior |
| `PerConsumerMiddlewareCompliance.cs` | Per-consumer middleware scoping and execution | Add per-consumer middleware tests |
| `ProduceConsumeCompliance.cs` | Basic produce-and-consume round-trip | Add end-to-end produce/consume tests |
| `RateLimiterCompliance.cs` | Consumer rate limiting enforcement | Add rate limiter tests or debug rate limiting behavior |
| `RetryCompliance.cs` | Retry policy: attempt count, backoff, and success after failure | Add retry tests or debug retry sequencing |
| `RoutingCompliance.cs` | Content-based router: route key matching and handler dispatch | Add routing tests or debug router dispatch |
| `ValidationCompliance.cs` | Message validation: pass, fail (discard), and transient error (retry) | Add validation tests or debug validation outcomes |
| `WorkerDistributionCompliance.cs` | ByKeyHash and RoundRobin distribution strategy correctness | Add distribution tests or debug worker assignment |
| `WorkerPoolRebalanceRecoveryCompliance.cs` | Worker pool recovery after Kafka partition rebalance | Add rebalance recovery tests or debug partition reassignment |
