# Emit.EntityFrameworkCore/

PostgreSQL integration tests: EF Core compliance implementations and test DbContext.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `TestDbContext.cs` | EF Core DbContext used exclusively in tests with Emit model builder integration | Understand the test database schema or add test-specific entity configuration |
| `PostgreSqlDistributedLockCompliance.cs` | Inherits DistributedLockCompliance; proves EF Core distributed lock correctness | Debug or extend PostgreSQL distributed lock tests |
| `PostgreSqlLeaderElectionCompliance.cs` | Inherits LeaderElectionCompliance; proves EF Core leader election correctness | Debug or extend PostgreSQL leader election tests |
| `PostgreSqlDaemonAssignmentCompliance.cs` | Inherits DaemonAssignmentCompliance; proves EF Core daemon assignment correctness | Debug or extend PostgreSQL daemon assignment tests |
| `EfCoreKafkaOutboxCompliance.cs` | Inherits OutboxDeliveryCompliance; proves EF Core outbox delivery correctness | Debug or extend EF Core outbox delivery tests |
| `EfCoreOutboxHeadersCompliance.cs` | Inherits OutboxHeadersCompliance; verifies header propagation through the EF Core outbox | Debug or extend header propagation tests |
| `EfCoreOutboxObserverCompliance.cs` | Inherits OutboxObserverCompliance; proves EF Core outbox observer event delivery | Debug or extend EF Core outbox observer tests |
| `EfCoreDaemonObserverCompliance.cs` | Inherits DaemonObserverCompliance; proves EF Core daemon observer event delivery | Debug or extend EF Core daemon observer tests |
| `EfCoreOutboxOrderingTests.cs` | Sequence ordering guarantees for the EF Core outbox repository | Debug or extend outbox ordering tests |
| `EfCoreLockCleanupTests.cs` | LockCleanupWorker behavior for EF Core expired lock rows | Debug or extend lock cleanup behavior |
| `EfCoreTransactionalMiddlewareCompliance.cs` | Inherits TransactionalMiddlewareCompliance; proves [Transactional] middleware with EF Core + Kafka | Debug or extend EF Core transactional middleware tests |
| `EfCoreUnitOfWorkCompliance.cs` | Inherits UnitOfWorkCompliance; proves IUnitOfWork with EF Core + Kafka | Debug or extend EF Core unit of work tests |
| `EfCoreImplicitOutboxCompliance.cs` | Inherits ImplicitOutboxCompliance; proves EF Core implicit outbox (Tier 1) with SaveChangesAsync | Debug or extend EF Core implicit outbox tests |
| `EfCoreProducerRoutingCompliance.cs` | Inherits ProducerRoutingCompliance; proves outbox-by-default and UseDirect() with EF Core + Kafka | Debug or extend EF Core producer routing tests |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `TestInfrastructure/` | PostgreSQL Testcontainers fixture | Understand container lifecycle or modify the test database setup |
