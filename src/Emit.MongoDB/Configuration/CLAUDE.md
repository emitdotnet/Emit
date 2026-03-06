# Configuration/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `BsonConfiguration.cs` | Global BSON class maps and conventions: camelCase, UTC DateTimes, ObjectId mapping | Configure MongoDB serialization or troubleshoot BSON mapping |
| `MongoDbContext.cs` | Holds IMongoClient and IMongoDatabase references for the outbox persistence provider | Configure MongoDB persistence context |
