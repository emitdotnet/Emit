# Emit.EntityFrameworkCore/

Unit tests for the Emit.Persistence.EntityFrameworkCore library: outbox repository, transaction context, transaction extensions, and DI registration.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `EfCoreOutboxRepositoryTests.cs` | Unit tests for EfCoreOutboxRepository enqueue, delete, and sequence behavior | Debugging EF Core repository test failures |
| `EfCoreTransactionContextTests.cs` | Unit tests for EfCoreTransactionContext construction and commit | Debugging EF Core transaction context test failures |
| `EfCoreTransactionExtensionsTests.cs` | Unit tests for EF Core transaction extension methods | Debugging EF Core transaction extension test failures |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `DependencyInjection/` | Tests for EntityFrameworkCore DI builder extensions | Debugging EF Core DI registration test failures |
