# Emit.MongoDB/

MongoDB persistence provider: outbox repository with sharding support and atomic sequence generation.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | MongoDB provider documentation: schema, indexes, sharding, sequence generation, troubleshooting | Understand MongoDB provider usage or configure collections |
| `CollectionNames.cs` | MongoDB collection name constants (outbox, sequences, locks) | Reference or modify collection naming |
| `MongoDbOutboxRepository.cs` | IOutboxRepository implementation with aggregation-based group heads and atomic sequences | Implement MongoDB queries or troubleshoot persistence |
| `MongoDbDistributedLockProvider.cs` | IDistributedLockProvider implementation using MongoDB atomic upserts and TTL index | Implement or debug distributed lock behavior |
| `MongoDbDaemonAssignmentPersistence.cs` | IDaemonAssignmentPersistence implementation for MongoDB | Understand or debug daemon assignment persistence |
| `MongoDbLeaderElectionPersistence.cs` | ILeaderElectionPersistence implementation for MongoDB | Understand or debug leader election persistence |
| `MongoTransactionContext.cs` | IMongoTransactionContext implementation wrapping IClientSessionHandle with commit/rollback state tracking | Debug MongoDB transaction lifecycle or understand double-commit guard |
| `Emit.MongoDB.csproj` | Project file with MongoDB.Driver dependency | Configure MongoDB provider dependencies |
| `IMongoSessionAccessor.cs` | Access to the active MongoDB session of the current scope, to enlist repository operations in the ambient transaction | Write MongoDB repositories that join the transaction |
| `IMongoUnitOfWorkTransaction.cs` | MongoDB unit of work transaction exposing its client session | Use the session of an explicit MongoDB transaction |
| `MongoSessionHolder.cs` | Scoped holder of the active MongoDB session, set when a unit of work begins and cleared when it ends | Understand how the session reaches IMongoSessionAccessor |
| `MongoUnitOfWork.cs` | MongoDB IUnitOfWork: starts a session and transaction and makes it the ambient transaction | Understand explicit transactions with MongoDB |
| `MongoUnitOfWorkTransaction.cs` | MongoDB unit of work transaction: commit, rollback, and rollback on dispose when not committed | Debug MongoDB commit or rollback |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Configuration/` | MongoDB options, BSON class maps, conventions, and validators | Configure MongoDB connection, collections, or serialization |
| `DependencyInjection/` | UseMongoDb extension methods registering IMongoClient, repository, and validators | Register MongoDB persistence provider |
| `Models/` | MongoDB document models for locks, sequences, daemon assignments, leader election, and nodes | Understand MongoDB document schemas |
