# Emit.EntityFrameworkCore/

EF Core persistence provider: outbox repository with PostgreSQL support and auto-generated sequences via IDENTITY columns.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | EF Core provider documentation: schema, migrations, configuration, troubleshooting | Understand EF Core provider usage or configure the entity model |
| `EfCoreOutboxRepository.cs` | IOutboxRepository implementation with Npgsql raw SQL and transaction integration | Implement EF Core queries or troubleshoot persistence |
| `EfCoreDistributedLockProvider.cs` | IDistributedLockProvider implementation using PostgreSQL INSERT ON CONFLICT via EF Core DbConnection | Implement or debug distributed lock behavior |
| `EfCoreDaemonAssignmentPersistence.cs` | IDaemonAssignmentPersistence implementation for PostgreSQL | Understand or debug daemon assignment persistence |
| `EfCoreLeaderElectionPersistence.cs` | ILeaderElectionPersistence implementation for PostgreSQL | Understand or debug leader election persistence |
| `IEfCoreTransactionContext.cs` | EF Core-specific ITransactionContext adding access to the underlying DbTransaction | Enlist an EF Core transaction or access the raw DbTransaction |
| `EfCoreTransactionContext.cs` | IEfCoreTransactionContext implementation wrapping IDbContextTransaction with commit/rollback state tracking | Debug EF Core transaction lifecycle or understand double-commit guard |
| `EfCoreTransactionExtensions.cs` | BeginTransactionAsync extension on IEmitContext starting an EF Core transaction and associating it with the context | Open a transaction in an EF Core handler before producing messages |
| `TableNames.cs` | Table name constants (outbox, locks) | Reference or modify table naming |
| `Emit.EntityFrameworkCore.csproj` | Project file with EF Core and Npgsql dependencies | Configure EF Core provider dependencies |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Configuration/` | LockCleanupOptions for expired lock cleanup interval | Configure lock cleanup behavior |
| `DependencyInjection/` | UseEntityFrameworkCore extension methods, builders, and ModelBuilder configuration | Register EF Core persistence or extend registration API |
| `Models/` | EF Core entity models for locks, daemon assignments, leader election, and nodes | Understand EF Core entity schemas |
| `Worker/` | LockCleanupWorker background service for expired lock cleanup | Understand lock cleanup behavior |
