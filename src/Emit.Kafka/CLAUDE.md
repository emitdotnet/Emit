# Emit.Kafka/

Kafka provider: producer/consumer implementations with middleware pipeline, outbox integration, and DI registration.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Kafka provider documentation: configuration, serialization, migration guide | Understand Kafka provider usage or configuration options |
| `KafkaOutboxProvider.cs` | IOutboxProvider implementation: delivers each batch in a Kafka transaction with a transactional producer per run of the outbox daemon, fencing earlier runs; messages are handed to the idempotent producer in order without waiting for each; a send that could not reach the broker is reported as such so the daemon can move | Understand Kafka entry processing flow |
| `KafkaPipelineProducer.cs` | IEventProducer implementation invoking outbound middleware pipeline before serializing and producing | Understand producer pipeline integration |
| `KafkaProducePayload.cs` | Payload the producer sets on the send context: the message key for the terminal, and whether the message is a tombstone | Understand how the key reaches the Kafka terminals |
| `KafkaSerializer.cs` | Shared serialization utilities for Kafka key/value serialization | Understand or modify Kafka message serialization |
| `KafkaTopicVerifier.cs` | Hosted service that verifies all required Kafka topics exist before the application starts; auto-provisions missing topics when enabled, treating a topic another instance created first as provisioned | Troubleshoot topic provisioning or understand startup topic validation |
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
| `Tracing/` | The `send` span shared by direct, outbox and dead letter sends, the enricher adding Kafka attributes to the `create` span, and the attribute names | Understand Kafka tracing |
| `Producer/` | Empty; reserved for future Kafka producer implementations | Not yet populated |
| `Serialization/` | Previously contained KafkaPayload (deleted) — outbox data now stored directly on OutboxEntry | Historical reference only |
