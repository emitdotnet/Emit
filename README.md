# Emit

A .NET 10.0+ transactional outbox library that provides reliable, ordered delivery of messages to external systems like Kafka.

## Overview

Emit implements the [transactional outbox pattern](https://microservices.io/patterns/data/transactional-outbox.html). Instead of directly producing messages to Kafka (which can fail independently of your database transaction), Emit persists the operation in an outbox table within your database as part of the same ACID transaction. A background worker then processes the outbox, delivering messages with retry logic and ordering guarantees.

### Core Principles

- **Transactional safety**: Your data and outbox entries commit atomically - both succeed or both roll back
- **Ordering guarantees**: Entries within the same group are processed strictly in sequence order
- **Provider-agnostic**: The outbox engine works with any persistence provider (MongoDB, PostgreSQL) and any message provider (Kafka)
- **Drop-in replacement**: For Kafka, replace Confluent's producer registration with Emit's - your application code requires no changes

## Installation

```bash
# Core library (required)
dotnet add package Emit

# Choose one persistence provider
dotnet add package Emit.Persistence.MongoDB
# OR
dotnet add package Emit.Persistence.PostgreSQL

# Add message providers
dotnet add package Emit.Provider.Kafka
```

## Quick Start

### Basic Setup with MongoDB + Kafka

```csharp
services.AddEmit(builder =>
{
    // Configure MongoDB persistence
    builder.UseMongoDb((sp, options) =>
    {
        options.ConnectionString = "mongodb://localhost:27017";
        options.DatabaseName = "myapp";
        options.CollectionName = "outbox";
    });

    // Configure Kafka provider
    builder.AddKafka((sp, kafka) =>
    {
        kafka.AddProducer<string, OrderCreated>(producer =>
        {
            producer.ProducerConfig = new ProducerConfig
            {
                BootstrapServers = "localhost:9092"
            };
            producer.ValueSerializer = new JsonSerializer<OrderCreated>();
        });
    });
});
```

### Using the Producer

```csharp
public class OrderService
{
    private readonly IProducer<string, OrderCreated> producer;
    private readonly IMongoTransactionManager transactionManager;

    public OrderService(
        IProducer<string, OrderCreated> producer,
        IMongoTransactionManager transactionManager)
    {
        this.producer = producer;
        this.transactionManager = transactionManager;
    }

    public async Task CreateOrderAsync(Order order)
    {
        await using var transaction = await transactionManager.BeginTransactionAsync();

        // Save order to database (within transaction)
        await orderRepository.InsertAsync(order, transaction);

        // Produce message (enqueued to outbox within same transaction)
        await producer.ProduceAsync("orders", new Message<string, OrderCreated>
        {
            Key = order.Id,
            Value = new OrderCreated { OrderId = order.Id, Total = order.Total }
        });

        // Both the order and the outbox entry commit atomically
        await transaction.CommitAsync();
    }
}
```

### PostgreSQL Setup

```csharp
services.AddEmit(builder =>
{
    builder.UsePostgreSql((sp, options) =>
    {
        options.ConnectionString = "Host=localhost;Database=myapp;Username=postgres;Password=secret";
        options.TableName = "outbox";
    });

    builder.AddKafka((sp, kafka) =>
    {
        kafka.AddProducer<string, OrderCreated>(producer =>
        {
            producer.ProducerConfig = new ProducerConfig
            {
                BootstrapServers = "localhost:9092"
            };
        });
    });
});
```

## Configuration

### Resilience Policy

Configure retry and circuit breaker behavior at multiple levels:

```csharp
services.AddEmit(builder =>
{
    // Global resilience policy (applies to all providers)
    builder.ConfigureResilience(policy => policy
        .WithRetry(
            maxRetries: 5,
            BackoffStrategy.Exponential,
            baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromMinutes(2))
        .WithCircuitBreaker(
            failureThreshold: 3,
            cooldown: TimeSpan.FromMinutes(5)));

    builder.UseMongoDb((sp, options) => { /* ... */ });

    builder.AddKafka((sp, kafka) =>
    {
        // Provider-level override
        kafka.ConfigureResilience(policy => policy
            .WithRetry(10, BackoffStrategy.FixedInterval, TimeSpan.FromSeconds(5)));

        kafka.AddProducer<string, OrderCreated>(producer =>
        {
            // Producer-level override (most specific wins)
            producer.ConfigureResilience(policy => policy
                .WithRetry(3, BackoffStrategy.Exponential, TimeSpan.FromSeconds(2)));
        });
    });
});
```

### Multi-Cluster Kafka Support

Connect to multiple Kafka clusters using named registrations:

```csharp
services.AddEmit(builder =>
{
    builder.UseMongoDb((sp, options) => { /* ... */ });

    // Default cluster (standard DI resolution)
    builder.AddKafka((sp, kafka) =>
    {
        kafka.AddProducer<string, OrderCreated>(producer =>
        {
            producer.ProducerConfig = new ProducerConfig
            {
                BootstrapServers = "kafka-primary:9092"
            };
        });
    });

    // Analytics cluster (keyed DI resolution)
    builder.AddKafka("analytics", (sp, kafka) =>
    {
        kafka.AddProducer<string, string>(producer =>
        {
            producer.ProducerConfig = new ProducerConfig
            {
                BootstrapServers = "kafka-analytics:9092"
            };
        });
    });
});
```

Resolve named producers:

```csharp
public class AnalyticsService
{
    public AnalyticsService(
        [FromKeyedServices("analytics")] IProducer<string, string> analyticsProducer)
    {
        // Use analytics cluster producer
    }
}
```

### Transaction-Optional Usage

Transactions are optional. For non-critical events or when no transaction is available:

```csharp
// Without transaction - writes directly to outbox (best-effort)
await producer.ProduceAsync("events", message);
```

## Architecture

### How It Works

1. **Enqueue**: When you call `ProduceAsync`, the message is serialized and stored in the outbox table within your database transaction
2. **Process**: A background worker polls the outbox, processes entries in order, and produces to Kafka
3. **Confirm**: After successful Kafka delivery, the entry is marked as completed
4. **Retry**: Failed entries are retried with configurable backoff and circuit breaker protection
5. **Cleanup**: Completed entries are purged after a configurable retention period

### Worker Model

V1 uses a single active worker model with global lease coordination:
- One worker holds the lease and processes all entries
- Other workers wait and take over if the lease holder fails
- Within a worker, groups are processed in parallel but entries within a group are strictly sequential

### Ordering Guarantees

Entries are grouped by `GroupKey` (for Kafka: `cluster:topic`). Within each group:
- Entries are assigned monotonically increasing sequence numbers at enqueue time
- The worker processes entries strictly in sequence order
- If an entry fails, the entire group is blocked until it succeeds or is resolved

## Provider Documentation

- [Emit.Provider.Kafka](src/Emit.Provider.Kafka/README.md) - Kafka producer configuration, serialization
- [Emit.Persistence.MongoDB](src/Emit.Persistence.MongoDB/README.md) - MongoDB schema, indexes, sharding
- [Emit.Persistence.PostgreSQL](src/Emit.Persistence.PostgreSQL/README.md) - PostgreSQL schema, migrations

## Requirements

- .NET 10.0 or later
- One of: MongoDB 7.0+ or PostgreSQL 16+
- [Transactional](https://github.com/ChuckNovice/transactional) library for transaction management

## License

This project is licensed under the Apache 2.0 License - see the [LICENSE](LICENSE) file for details.
