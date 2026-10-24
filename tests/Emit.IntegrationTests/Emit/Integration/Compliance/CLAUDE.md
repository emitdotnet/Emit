# Compliance/

Abstract compliance test base classes. Each class defines [Fact] methods that every provider must pass. Provider test projects inherit these and supply infrastructure.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `BatchConsumerCompliance.cs` | Batch consumer: full batch, partial batch timeout, validation (a rejected item validated once when the validator throws on a later one), retry, rate limiting, circuit breaker, distribution | Add batch consumer tests or debug batch consuming behavior |
| `CircuitBreakerCompliance.cs` | Circuit breaker open/close/reset behavior, exact failure counts with and without retries, state reported per consumer group and topic | Add circuit breaker tests or debug circuit breaker issues |
| `ConsumerFlowControlCompliance.cs` | Pausing a named consumer group declaration: through rebalances and an open circuit, waiting for messages being handled, resuming in order, before start and after stop, batch groups, from a handler, polling with a full queue | Add flow control tests or debug pause and resume |
| `ConsumeObserverCompliance.cs` | IConsumeObserver lifecycle event delivery | Add consume observer tests or debug observer invocation |
| `ConsumerObserverCompliance.cs` | IConsumerObserver lifecycle event delivery | Add consumer observer tests or debug observer invocation |
| `DaemonAssignmentCompliance.cs` | Daemon assignment persistence per service: compare-and-swap writes, release, and generations that never repeat | Add daemon tests or debug distributed daemon behavior |
| `DeadLetterCompliance.cs` | Dead-letter queue delivery after exhausted retries | Add DLQ tests or debug dead-letter routing |
| `DistributedLockCompliance.cs` | Mutual exclusion, TTL expiry, and lock renewal | Add distributed lock tests or debug locking behavior |
| `ErrorPolicyCompliance.cs` | Error policy evaluation: retry, discard, and dead-letter branching | Add error policy tests or debug error handling |
| `ExternalMessageCompliance.cs` | Consuming messages produced outside Emit (raw Kafka messages) | Debug external message ingestion or verify raw message support |
| `FanOutCompliance.cs` | Multiple consumer groups independently consuming the same topic | Add fan-out tests or debug independent consumer group behavior |
| `FilterCompliance.cs` | Consumer filter predicate evaluation, a filter that throws retried by the error policy | Add filter tests or debug filter skipping behavior |
| `GlobalPipelineCompliance.cs` | Global middleware execution order and context propagation | Add global pipeline tests or debug cross-cutting middleware |
| `LeaderElectionCompliance.cs` | Leader election persistence contract: lease acquisition, renewal and takeover by recorded heartbeat, heartbeats that do not claim the lease, resign, services sharing a database, conditional registration removal, and deregistration | Add leader election tests or debug leadership transitions |
| `DatabaseLeaderElectionCompliance.cs` | Leader election contract that needs a real database: a heartbeat stalled on a lock leaves no registration when it times out | Add leader election tests that depend on database locking |
| `ServiceCoordinationCompliance.cs` | Real hosts: services sharing a database each elect a leader and run their own daemons (issue #85 scenario), daemon observers registered through DI hear about them, a node that shuts down hands its daemon and leadership to another node of its service once its observers heard the daemon stop, and daemons registered with `AddDaemon` spread when a node joins and fail over when a node loses the database, never acting on two nodes at once | Debug daemon coordination end to end |
| `OffsetCommitCompliance.cs` | Watermark-based offset commit correctness | Add offset commit tests or debug at-least-once delivery |
| `OutboundMiddlewareHeadersCompliance.cs` | Header propagation through outbound middleware pipeline | Verify or change outbound header forwarding behavior |
| `OutboxDeliveryCompliance.cs` | Transactional outbox delivery to Kafka: per-group order, also when the outbox daemon moves to another node of the service, and services sharing a database each delivering only their own entries | Add outbox delivery tests or debug outbox-to-Kafka flow or per-service outbox processing |
| `TombstoneCompliance.cs` | Tombstones (a key and no value) through the outbox and directly: null value delivered, per-key order with tombstones, rollback, headers added by Emit, schema registry value serializer | Add tombstone tests or debug producing tombstones |
| `OutboxHeadersCompliance.cs` | Message header propagation through the outbox | Verify or change outbox header forwarding behavior |
| `OutboxObserverCompliance.cs` | IOutboxObserver lifecycle event delivery | Add outbox observer tests or debug outbox observer invocation |
| `PerConsumerMiddlewareCompliance.cs` | Per-consumer middleware scoping and execution | Add per-consumer middleware tests |
| `ProduceConsumeCompliance.cs` | Basic produce-and-consume round-trip | Add end-to-end produce/consume tests |
| `ProduceObserverCompliance.cs` | IProduceObserver lifecycle event delivery | Add produce observer tests or debug produce observer invocation |
| `ProviderPipelineCompliance.cs` | Provider-level pipeline middleware execution and context propagation | Add provider pipeline tests or debug provider middleware |
| `RateLimiterCompliance.cs` | Consumer rate limiting enforcement | Add rate limiter tests or debug rate limiting behavior |
| `RetryCompliance.cs` | Retry policy: attempt count, backoff, success after failure, and retries outlasting the poll interval on a full queue without a rebalance | Add retry tests or debug retry sequencing |
| `RoutingCompliance.cs` | Content-based router: route key matching and handler dispatch | Add routing tests or debug router dispatch |
| `ValidationCompliance.cs` | Message validation: pass, fail (discard, dead-letter), a failed validation never retried, a validator that throws retried by the error policy | Add validation tests or debug validation outcomes |
| `WorkerDistributionCompliance.cs` | ByKeyHash and RoundRobin distribution strategy correctness | Add distribution tests or debug worker assignment |
| `WorkerPoolRebalanceRecoveryCompliance.cs` | Worker pool recovery after Kafka partition rebalance | Add rebalance recovery tests or debug partition reassignment |
| `TransactionalMiddlewareCompliance.cs` | [Transactional] attribute middleware: commit delivery, throw rollback, non-transactional pass-through, retry isolation | Add transactional middleware tests or debug [Transactional] behavior |
| `UnitOfWorkCompliance.cs` | IUnitOfWork explicit transaction API: begin/commit, begin/rollback, auto-rollback on dispose, ordering | Add unit of work tests or debug IUnitOfWork behavior |
| `ImplicitOutboxCompliance.cs` | EF Core implicit outbox (Tier 1): produce+SaveChanges atomicity, no-save no delivery | Add implicit outbox tests or debug EF Core implicit tier |
| `MongoSessionAccessorCompliance.cs` | IMongoSessionAccessor lifecycle: session populated after BeginAsync, cleared after dispose | Add MongoDB session accessor tests or debug IMongoSessionAccessor |
| `ProducerRoutingCompliance.cs` | Producer routing: outbox-by-default, UseDirect() opt-out, mixed behavior | Add producer routing tests or debug outbox/direct routing |
| `MediatorTransactionalCompliance.cs` | [Transactional] on mediator handlers: transaction present/absent, commit, rollback, response integrity, registration order, both handler shapes | Add mediator [Transactional] tests or debug the attribute on IRequestHandler |
| `ProducerOutboxRoutingCompliance.cs` | Producer routes through the outbox: a rolled-back transaction delivers nothing, regardless of registration order | Add producer outbox-routing tests or debug direct-versus-outbox routing |
