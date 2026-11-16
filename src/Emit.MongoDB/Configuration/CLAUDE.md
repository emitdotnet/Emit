# Configuration/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `BsonConfiguration.cs` | Global BSON class maps and conventions: camelCase, UTC DateTimes, ObjectId mapping | Configure MongoDB serialization or troubleshoot BSON mapping |
| `MongoDbContext.cs` | Holds IMongoClient and IMongoDatabase references for the outbox persistence provider | Configure MongoDB persistence context |
| `MongoDbInboxOptions.cs` | Inbox options: Retention (7 days) | Configure the MongoDB inbox |
| `MongoDbInboxOptionsValidator.cs` | IValidateOptions validator requiring a retention greater than zero | Modify validation rules for inbox options |
