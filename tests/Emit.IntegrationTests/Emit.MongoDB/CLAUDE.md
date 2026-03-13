# Emit.MongoDB/

MongoDB integration tests: compliance implementations proving MongoDB persistence correctness.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MongoDbDistributedLockCompliance.cs` | Inherits DistributedLockCompliance; proves MongoDB distributed lock correctness | Debug or extend MongoDB distributed lock tests |
| `MongoDbLeaderElectionCompliance.cs` | Inherits LeaderElectionCompliance; proves MongoDB leader election correctness | Debug or extend MongoDB leader election tests |
| `MongoDbDaemonAssignmentCompliance.cs` | Inherits DaemonAssignmentCompliance; proves MongoDB daemon assignment correctness | Debug or extend MongoDB daemon assignment tests |
| `MongoDbKafkaOutboxCompliance.cs` | Inherits OutboxDeliveryCompliance; proves MongoDB outbox delivery correctness | Debug or extend MongoDB outbox delivery tests |
| `MongoDbMixedProducerTests.cs` | MongoDB-specific: outbox delivery when mixing direct and outbox producers | Debug or extend mixed producer integration tests |
| `MongoDbTransactionalMiddlewareCompliance.cs` | Inherits TransactionalMiddlewareCompliance; proves [Transactional] middleware with MongoDB + Kafka | Debug or extend MongoDB transactional middleware tests |
| `MongoDbUnitOfWorkCompliance.cs` | Inherits UnitOfWorkCompliance; proves IUnitOfWork with MongoDB + Kafka | Debug or extend MongoDB unit of work tests |
| `MongoDbSessionAccessorCompliance.cs` | Inherits MongoSessionAccessorCompliance; proves IMongoSessionAccessor lifecycle with MongoDB | Debug or extend MongoDB session accessor tests |
| `MongoDbProducerRoutingCompliance.cs` | Inherits ProducerRoutingCompliance; proves outbox-by-default and UseDirect() with MongoDB + Kafka | Debug or extend MongoDB producer routing tests |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `TestInfrastructure/` | MongoDB Testcontainers fixture | Understand container lifecycle or modify the test MongoDB setup |
