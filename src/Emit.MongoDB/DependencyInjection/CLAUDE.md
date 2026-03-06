# DependencyInjection/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `MongoDbBuilder.cs` | Fluent builder for MongoDB persistence (Configure, UseOutbox, UseDistributedLock) | Extend MongoDB registration API or configure persistence features |
| `MongoDbEmitBuilderExtensions.cs` | UseMongoDb extension methods registering IMongoClient, repository, and validators | Register MongoDB persistence provider or extend registration |
