# Emit.EntityFrameworkCore/

Unit tests for the Emit.Persistence.EntityFrameworkCore library: outbox repository, transaction context, transaction extensions, and DI registration.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `EfCoreOutboxRepositoryTests.cs` | Unit tests for EfCoreOutboxRepository enqueue, delete, and sequence behavior | Debugging EF Core repository test failures |
| `EfCoreTransactionContextTests.cs` | Unit tests for EfCoreTransactionContext construction and commit | Debugging EF Core transaction context test failures |
| `EfCoreUnitOfWorkTests.cs` | EF Core unit of work beginning a transaction and making it ambient | Debugging EF Core unit of work test failures |
| `EfCoreUnitOfWorkTransactionTests.cs` | EF Core transaction: save then commit, commit and rollback once only, rollback on dispose | Debugging EF Core transaction test failures |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `DependencyInjection/` | Tests for EntityFrameworkCore DI builder extensions | Debugging EF Core DI registration test failures |
