# Emit.Provider.Kafka

Kafka outbox provider for Emit. Provides a drop-in replacement for Confluent's `IProducer<TKey, TValue>` that stores messages in the outbox for reliable, ordered delivery.

## Installation

```bash
dotnet add package Emit.Provider.Kafka
```

## Quick Start

```csharp
services.AddEmit(builder =>
{
    builder.UseMongoDb((sp, options) => { /* ... */ });

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

## Producer Configuration

### Basic Configuration

Each producer requires a `ProducerConfig` (Confluent's configuration object):

```csharp
kafka.AddProducer<string, OrderCreated>(producer =>
{
    producer.ProducerConfig = new ProducerConfig
    {
        BootstrapServers = "broker1:9092,broker2:9092",
        Acks = Acks.All,
        EnableIdempotence = true,
        MessageSendMaxRetries = 3,
        RetryBackoffMs = 100
    };
});
```

### Serializers

Configure key and value serializers using Confluent's `ISerializer<T>` interface:

```csharp
kafka.AddProducer<string, OrderCreated>(producer =>
{
    producer.ProducerConfig = new ProducerConfig { /* ... */ };

    // String keys use built-in serializer by default
    producer.KeySerializer = Serializers.Utf8;

    // Custom value serializer
    producer.ValueSerializer = new JsonSerializer<OrderCreated>();
});
```

### Schema Registry

For Avro, Protobuf, or JSON Schema serialization:

```csharp
builder.AddKafka((sp, kafka) =>
{
    kafka.SchemaRegistryConfig = new SchemaRegistryConfig
    {
        Url = "http://schema-registry:8081"
    };

    kafka.AddProducer<string, OrderCreated>(producer =>
    {
        producer.ProducerConfig = new ProducerConfig { /* ... */ };
        producer.ValueSerializer = new AvroSerializer<OrderCreated>(schemaRegistry);
    });
});
```

## Named vs Unnamed Registration

### Default (Unnamed) Registration

Producers are registered as standard DI services and resolved via normal constructor injection:

```csharp
// Registration
builder.AddKafka((sp, kafka) =>
{
    kafka.AddProducer<string, OrderCreated>(producer => { /* ... */ });
});

// Resolution
public class OrderService
{
    public OrderService(IProducer<string, OrderCreated> producer)
    {
        // Injected via standard DI
    }
}
```

### Named Registration

For multi-cluster scenarios, use named registrations with keyed DI:

```csharp
// Registration
builder.AddKafka("analytics", (sp, kafka) =>
{
    kafka.AddProducer<string, string>(producer =>
    {
        producer.ProducerConfig = new ProducerConfig
        {
            BootstrapServers = "analytics-kafka:9092"
        };
    });
});

// Resolution
public class AnalyticsService
{
    public AnalyticsService(
        [FromKeyedServices("analytics")] IProducer<string, string> producer)
    {
        // Injected via keyed DI
    }
}
```

## Multi-Cluster Support

Connect to multiple Kafka clusters by registering multiple providers:

```csharp
services.AddEmit(builder =>
{
    builder.UseMongoDb((sp, options) => { /* ... */ });

    // Primary cluster (default)
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

    // Analytics cluster (named)
    builder.AddKafka("analytics", (sp, kafka) =>
    {
        kafka.AddProducer<string, AnalyticsEvent>(producer =>
        {
            producer.ProducerConfig = new ProducerConfig
            {
                BootstrapServers = "kafka-analytics:9092"
            };
        });
    });

    // Audit cluster (named)
    builder.AddKafka("audit", (sp, kafka) =>
    {
        kafka.AddProducer<string, AuditEvent>(producer =>
        {
            producer.ProducerConfig = new ProducerConfig
            {
                BootstrapServers = "kafka-audit:9092"
            };
        });
    });
});
```

## Serialization Behavior

### Serialization at Enqueue Time

Messages are serialized immediately when you call `ProduceAsync`:

1. The configured `ISerializer<TKey>` and `ISerializer<TValue>` serialize the key and value to `byte[]`
2. The serialized bytes, along with topic, headers, and metadata, are packaged into the outbox entry
3. The outbox entry is stored in the database within your transaction

This means:
- Schema validation happens at enqueue time, not delivery time
- Serialization errors are caught immediately
- The worker produces raw bytes, requiring no knowledge of the original types

### Payload Format

The Kafka payload is serialized using MessagePack and includes:
- Topic name
- Key bytes (may be null)
- Value bytes (may be null)
- Headers
- Partition (if explicitly specified)
- Timestamp (if explicitly specified)

## Resilience Configuration

Override resilience policy at the provider or producer level:

```csharp
builder.AddKafka((sp, kafka) =>
{
    // Provider-level (applies to all producers in this registration)
    kafka.ConfigureResilience(policy => policy
        .WithRetry(5, BackoffStrategy.Exponential, TimeSpan.FromSeconds(1))
        .WithCircuitBreaker(3, TimeSpan.FromMinutes(2)));

    kafka.AddProducer<string, OrderCreated>(producer =>
    {
        producer.ProducerConfig = new ProducerConfig { /* ... */ };

        // Producer-level override (most specific wins)
        producer.ConfigureResilience(policy => policy
            .WithRetry(10, BackoffStrategy.FixedInterval, TimeSpan.FromSeconds(5)));
    });
});
```

## Drop-in Replacement Guide

Emit's `IProducer<TKey, TValue>` is designed as a drop-in replacement for Confluent's producer:

### Before (Confluent)

```csharp
// Registration
services.AddSingleton<IProducer<string, OrderCreated>>(sp =>
{
    var config = new ProducerConfig { BootstrapServers = "localhost:9092" };
    return new ProducerBuilder<string, OrderCreated>(config)
        .SetValueSerializer(new JsonSerializer<OrderCreated>())
        .Build();
});

// Usage
await producer.ProduceAsync("orders", new Message<string, OrderCreated>
{
    Key = orderId,
    Value = orderCreated
});
```

### After (Emit)

```csharp
// Registration
services.AddEmit(builder =>
{
    builder.UseMongoDb((sp, options) => { /* ... */ });

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

// Usage - UNCHANGED
await producer.ProduceAsync("orders", new Message<string, OrderCreated>
{
    Key = orderId,
    Value = orderCreated
});
```

## GroupKey and Ordering

For Kafka, the `GroupKey` is set to `cluster:topic`. This means:
- All messages to the same topic on the same cluster are ordered
- Messages to different topics can be processed in parallel
- The cluster identifier is extracted from `BootstrapServers` (first server hostname)

## Limitations

The following Confluent producer methods are not applicable to the outbox pattern and are not supported:

- `Flush` - Not needed; messages are durably stored in the outbox
- `Poll` - Internal librdkafka mechanism not applicable
- `InitTransactions`, `BeginTransaction`, `CommitTransaction`, `AbortTransaction` - Kafka transactions are replaced by database transactions
- `SendOffsetsToTransaction` - Consumer-side transaction method
