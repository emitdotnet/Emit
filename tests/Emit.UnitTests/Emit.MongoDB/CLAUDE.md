# Emit.MongoDB/

Unit tests for the Emit.Persistence.MongoDB library: outbox repository, transaction context, transaction extensions, BSON configuration, and DI registration.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MongoDbOutboxRepositoryTests.cs` | Unit tests for MongoDbOutboxRepository enqueue, delete, and sequence behavior | Debugging MongoDB repository test failures |
| `MongoTransactionContextTests.cs` | Unit tests for MongoTransactionContext construction and commit | Debugging MongoDB transaction context test failures |
| `MongoSessionHolderTests.cs` | Mongo session holder: empty until set | Debugging session holder test failures |
| `MongoUnitOfWorkTests.cs` | MongoDB unit of work starting a session and transaction, making it ambient and setting the session holder | Debugging MongoDB unit of work test failures |
| `MongoUnitOfWorkTransactionTests.cs` | MongoDB transaction: commit, rollback, session access, rollback on dispose, ambient transaction cleared | Debugging MongoDB transaction test failures |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Configuration/` | Tests for BSON serialization configuration | Debugging BSON configuration test failures |
| `DependencyInjection/` | Tests for MongoDB DI builder extensions | Debugging MongoDB DI registration test failures |
