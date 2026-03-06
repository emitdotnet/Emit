# Emit.EntityFrameworkCore/

PostgreSQL integration tests: EF Core compliance implementations and test DbContext.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `TestDbContext.cs` | EF Core DbContext used exclusively in tests with Emit model builder integration | Understand the test database schema or add test-specific entity configuration |
| `PostgreSqlDistributedLockCompliance.cs` | Inherits DistributedLockCompliance; proves EF Core distributed lock correctness | Debug or extend PostgreSQL distributed lock tests |
| `PostgreSqlLeaderElectionCompliance.cs` | Inherits LeaderElectionCompliance; proves EF Core leader election correctness | Debug or extend PostgreSQL leader election tests |
| `PostgreSqlDaemonAssignmentCompliance.cs` | Inherits DaemonAssignmentCompliance; proves EF Core daemon assignment correctness | Debug or extend PostgreSQL daemon assignment tests |
| `EfCoreKafkaOutboxComplianceTests.cs` | Inherits OutboxDeliveryCompliance and OutboxHeadersCompliance; proves EF Core outbox delivery correctness | Debug or extend EF Core outbox delivery tests |
| `EfCoreOutboxHeadersComplianceTests.cs` | Inherits OutboxHeadersCompliance; verifies header propagation through the EF Core outbox | Debug or extend header propagation tests |
| `EfCoreOutboxOrderingTests.cs` | Tests sequence ordering guarantees for the EF Core outbox repository | Debug or extend outbox ordering tests |
| `EfCoreLockCleanupTests.cs` | Tests the LockCleanupWorker behavior for EF Core expired lock rows | Debug or extend lock cleanup behavior |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `TestInfrastructure/` | PostgreSQL Testcontainers fixture | Understand container lifecycle or modify the test database setup |
