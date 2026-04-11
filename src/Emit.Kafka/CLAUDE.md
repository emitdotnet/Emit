# Emit.Kafka/

Kafka provider: producer/consumer implementations with middleware pipeline, outbox integration, and DI registration.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Kafka provider documentation: configuration, serialization, migration guide | Understand Kafka provider usage or configuration options |
| `KafkaOutboxProvider.cs` | IOutboxProvider implementation: reads Body, Headers, and Properties from OutboxEntry and produces to Kafka | Understand Kafka entry processing flow |
| `KafkaPipelineProducer.cs` | IEventProducer implementation invoking outbound middleware pipeline before serializing and producing | Understand producer pipeline integration |
| `KafkaSerializationHelper.cs` | Shared serialization utilities for Kafka key/value serialization | Understand or modify Kafka message serialization |
| `KafkaTopicVerifier.cs` | Hosted service that verifies all required Kafka topics exist before the application starts; auto-provisions missing topics when enabled | Troubleshoot topic provisioning or understand startup topic validation |
| `TopicCleanupPolicy.cs` | Enum specifying Kafka topic cleanup policy (Delete or Compact) | Configure topic cleanup behavior when auto-provisioning topics |
| `TopicCompressionType.cs` | Enum specifying Kafka topic compression codec | Configure topic compression when auto-provisioning topics |
| `Provider.cs` | Well-known "kafka" provider identifier constant | Reference Kafka provider ID |
| `Emit.Kafka.csproj` | Project file with Confluent.Kafka dependency | Configure Kafka provider dependencies |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Consumer/` | Consumer worker, offset management, distribution strategies, and transport context | Implement or debug Kafka consumer functionality |
| `DependencyInjection/` | AddKafka extension methods, KafkaBuilder, and topic/consumer/producer builders | Register Kafka clusters or extend Kafka DI API |
| `Metrics/` | Kafka provider-specific metrics and broker-level metrics | Monitor Kafka performance or debug consumer lag |
| `Observability/` | Kafka consumer observer interface and observer invoker | Implement Kafka consumer monitoring or custom instrumentation |
| `Producer/` | Empty; reserved for future Kafka producer implementations | Not yet populated |
| `Serialization/` | Previously contained KafkaPayload (deleted) — outbox data now stored directly on OutboxEntry | Historical reference only |
