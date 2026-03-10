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

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `TestInfrastructure/` | MongoDB Testcontainers fixture | Understand container lifecycle or modify the test MongoDB setup |
