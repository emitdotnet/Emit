# Emit.Kafka/

Kafka provider: producer/consumer implementations with middleware pipeline, outbox integration, and DI registration.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Kafka provider documentation: configuration, serialization, migration guide | Understand Kafka provider usage or configuration options |
| `KafkaOutboxProvider.cs` | IOutboxProvider implementation: deserializes KafkaPayload and produces to real Kafka | Understand Kafka entry processing flow |
| `KafkaPipelineProducer.cs` | IEventProducer implementation invoking outbound middleware pipeline before serializing and producing | Understand producer pipeline integration |
| `OutboundKafkaContext.cs` | Outbound pipeline context carrying Kafka-specific message data for production | Understand Kafka outbound pipeline context |
| `KafkaSerializationHelper.cs` | Shared serialization utilities for Kafka key/value serialization | Understand or modify Kafka message serialization |
| `Provider.cs` | Well-known "kafka" provider identifier constant | Reference Kafka provider ID |
| `Emit.Kafka.csproj` | Project file with Confluent.Kafka dependency | Configure Kafka provider dependencies |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Consumer/` | Consumer worker, offset management, distribution strategies, and pipeline features | Implement or debug Kafka consumer functionality |
| `DependencyInjection/` | AddKafka extension methods, KafkaBuilder, and topic/consumer/producer builders | Register Kafka clusters or extend Kafka DI API |
| `Metrics/` | Kafka provider-specific metrics and broker-level metrics | Monitor Kafka performance or debug consumer lag |
| `Observability/` | Kafka consumer observer interface and observer invoker | Implement Kafka consumer monitoring or custom instrumentation |
| `Serialization/` | MessagePack-serialized KafkaPayload model | Understand outbox payload structure for Kafka messages |
