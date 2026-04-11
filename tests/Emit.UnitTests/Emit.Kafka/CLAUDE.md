# Emit.Kafka/

Unit tests for the Emit.Provider.Kafka library: producer, consumer, serialization, service collection extensions, topic verification, metrics, and observability.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `KafkaOutboxProviderTests.cs` | Unit tests for KafkaOutboxProvider produce behavior | Debugging Kafka outbox provider test failures |
| `KafkaPipelineProducerTests.cs` | Unit tests for KafkaPipelineProducer message production | Debugging Kafka pipeline producer test failures |
| `KafkaSerializerTests.cs` | Unit tests for Kafka serialization helper utilities | Debugging serialization helper test failures |
| `KafkaServiceCollectionExtensionsTests.cs` | Unit tests for Kafka service collection extension registration | Debugging Kafka service registration test failures |
| `KafkaTopicVerifierTests.cs` | Unit tests for KafkaTopicVerifier topic existence checking | Debugging topic verifier test failures |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Consumer/` | Tests for consumer worker pool, offset management, DLQ producer, and distribution strategies | Debugging Kafka consumer test failures |
| `DependencyInjection/` | Tests for KafkaBuilder, topic builder, consumer group builder, and config validation | Debugging Kafka DI registration or config validation test failures |
| `Metrics/` | Tests for Kafka broker and consumer metrics | Debugging Kafka metrics test failures |
| `Observability/` | Tests for Kafka consumer observer invoker | Debugging Kafka consumer observer test failures |
