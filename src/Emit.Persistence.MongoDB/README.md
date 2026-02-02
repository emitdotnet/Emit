# Emit.Persistence.MongoDB

MongoDB persistence provider for Emit. Stores outbox entries in a MongoDB collection with support for sharding and atomic sequence generation.

## Installation

```bash
dotnet add package Emit.Persistence.MongoDB
```

## Quick Start

```csharp
services.AddEmit(builder =>
{
    builder.UseMongoDb((sp, options) =>
    {
        options.ConnectionString = "mongodb://localhost:27017";
        options.DatabaseName = "myapp";
        options.CollectionName = "outbox";
    });

    builder.AddKafka((sp, kafka) => { /* ... */ });
});
```

## Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `ConnectionString` | Required | MongoDB connection string |
| `DatabaseName` | Required | Database name |
| `CollectionName` | `"outbox"` | Outbox collection name |
| `CounterCollectionName` | `"outbox_counters"` | Sequence counter collection name |
| `LeaseCollectionName` | `"outbox_lease"` | Worker lease collection name |

## Collection Schema

### Outbox Collection

```javascript
{
  "_id": ObjectId("..."),
  "providerId": "kafka",
  "registrationKey": "__default__",
  "groupKey": "kafka-cluster:orders",
  "sequence": NumberLong(1),
  "status": "Pending",
  "enqueuedAt": ISODate("2024-01-15T10:30:00Z"),
  "completedAt": null,
  "retryCount": 0,
  "lastAttemptedAt": null,
  "latestError": null,
  "attempts": [],
  "payload": BinData(0, "..."),
  "properties": {
    "topic": "orders",
    "cluster": "kafka-cluster",
    "valueType": "OrderCreated"
  },
  "correlationId": "req-12345"
}
```

### Counter Collection

Used for atomic sequence number generation:

```javascript
{
  "_id": "kafka-cluster:orders",
  "sequence": NumberLong(42)
}
```

### Lease Collection

Used for single-worker coordination:

```javascript
{
  "_id": "global",
  "workerId": "worker-abc123",
  "leaseUntil": ISODate("2024-01-15T10:31:00Z")
}
```

## Indexes

The following indexes are created automatically:

```javascript
// Unique compound index for ordering within groups
{ "groupKey": 1, "sequence": 1 }, { unique: true }

// Composite index for efficient group head queries
{ "status": 1, "groupKey": 1 }

// Index for cleanup queries
{ "completedAt": 1 }
```

## Sharding

The `GroupKey` field is designed to be the shard key. This ensures:

- All entries in the same group reside on the same shard
- Queries filtered by group key are routed to the correct shard
- Sequential processing within a group doesn't cause scatter-gather operations

### Setting Up Sharding

```javascript
// Enable sharding on the database
sh.enableSharding("myapp")

// Shard the outbox collection by groupKey
sh.shardCollection("myapp.outbox", { "groupKey": "hashed" })

// Alternatively, use range-based sharding for specific access patterns
sh.shardCollection("myapp.outbox", { "groupKey": 1 })
```

### Best Practices for GroupKey

- Use consistent, predictable values (e.g., `cluster:topic` for Kafka)
- Ensure good cardinality for even data distribution
- Avoid hot spots by distributing work across many groups

## Sequence Number Generation

MongoDB uses an atomic counter in a dedicated collection for sequence number generation:

1. When enqueueing an entry, `FindOneAndUpdate` with `$inc` atomically increments and returns the next sequence
2. This guarantees globally ordered sequences within each group across all application replicas
3. The counter document is created automatically on first enqueue to a group

This approach is necessary because:
- Timestamps have clock skew across replicas
- `ObjectId` ordering is only approximate across machines
- In-process counters don't work across replicas

## Transaction Integration

The MongoDB provider integrates with the [Transactional](https://github.com/ChuckNovice/transactional) library:

```csharp
public class OrderService
{
    private readonly IMongoTransactionManager transactionManager;
    private readonly IProducer<string, OrderCreated> producer;

    public async Task CreateOrderAsync(Order order)
    {
        await using var transaction = await transactionManager.BeginTransactionAsync();

        // Your business data write
        await ordersCollection.InsertOneAsync(transaction.Session, order);

        // Outbox write (same transaction)
        await producer.ProduceAsync("orders", new Message<string, OrderCreated>
        {
            Key = order.Id,
            Value = new OrderCreated { OrderId = order.Id }
        });

        // Both commit atomically
        await transaction.CommitAsync();
    }
}
```

## Cleanup

Completed entries are automatically purged by the cleanup worker based on configuration:

```csharp
// In appsettings.json or configure programmatically
{
  "Emit": {
    "Cleanup": {
      "RetentionPeriod": "7.00:00:00",  // 7 days
      "CleanupInterval": "01:00:00",     // 1 hour
      "BatchSize": 1000
    }
  }
}
```

## Migration Guide

### From Existing MongoDB Setup

1. Create the outbox collections in your database
2. Add the required indexes (or let Emit create them on startup)
3. If using sharding, configure shard keys before adding data

### Index Creation Script

```javascript
// Run against your database
db.outbox.createIndex(
  { "groupKey": 1, "sequence": 1 },
  { unique: true, name: "ix_outbox_group_key_sequence" }
);

db.outbox.createIndex(
  { "status": 1, "groupKey": 1 },
  { name: "ix_outbox_status_group_key" }
);

db.outbox.createIndex(
  { "completedAt": 1 },
  { name: "ix_outbox_completed_at" }
);
```

## Troubleshooting

### "Duplicate key error" on sequence

This indicates a race condition in sequence generation. Ensure:
- The counter collection is properly configured
- Transactions are being used correctly
- No manual sequence assignment is occurring

### Slow group head queries

Verify the composite index on `(status, groupKey)` exists:

```javascript
db.outbox.getIndexes()
```

### High memory usage during cleanup

Reduce the batch size in cleanup configuration:

```csharp
options.Cleanup.BatchSize = 500; // Lower batch size
```
