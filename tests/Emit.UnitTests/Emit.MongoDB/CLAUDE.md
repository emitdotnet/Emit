# Emit.MongoDB/

Unit tests for the Emit.Persistence.MongoDB library: outbox repository, transaction context, transaction extensions, BSON configuration, and DI registration.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MongoDbOutboxRepositoryTests.cs` | Unit tests for MongoDbOutboxRepository enqueue, delete, and sequence behavior | Debugging MongoDB repository test failures |
| `MongoTransactionContextTests.cs` | Unit tests for MongoTransactionContext construction and commit | Debugging MongoDB transaction context test failures |
| `MongoTransactionExtensionsTests.cs` | Unit tests for MongoDB transaction extension methods | Debugging MongoDB transaction extension test failures |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Configuration/` | Tests for BSON serialization configuration | Debugging BSON configuration test failures |
| `DependencyInjection/` | Tests for MongoDB DI builder extensions | Debugging MongoDB DI registration test failures |
