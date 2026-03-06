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
| `IMongoTransactionContext.cs` | MongoDB-specific ITransactionContext adding access to the IClientSessionHandle | Enlist a MongoDB session or access the raw session handle |
| `MongoTransactionContext.cs` | IMongoTransactionContext implementation wrapping IClientSessionHandle with commit/rollback state tracking | Debug MongoDB transaction lifecycle or understand double-commit guard |
| `MongoTransactionExtensions.cs` | BeginMongoTransactionAsync extension on IEmitContext starting a MongoDB session transaction and associating it with the context | Open a transaction in a MongoDB handler before producing messages |
| `Emit.MongoDB.csproj` | Project file with MongoDB.Driver dependency | Configure MongoDB provider dependencies |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Configuration/` | MongoDB options, BSON class maps, conventions, and validators | Configure MongoDB connection, collections, or serialization |
| `DependencyInjection/` | UseMongoDb extension methods registering IMongoClient, repository, and validators | Register MongoDB persistence provider |
| `Models/` | MongoDB document models for locks, sequences, daemon assignments, leader election, and nodes | Understand MongoDB document schemas |
