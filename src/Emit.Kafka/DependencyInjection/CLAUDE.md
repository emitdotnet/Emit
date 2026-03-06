# DependencyInjection/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `KafkaBuilder.cs` | Fluent builder for Kafka cluster config, producer registration, and consumer groups | Extend Kafka registration API or configure cluster settings |
| `KafkaEmitBuilderExtensions.cs` | AddKafka extension methods with keyed service registration and provider setup | Register Kafka clusters or understand keyed service strategy |
| `KafkaClientConfig.cs` | Shared Kafka client settings (connection, security, timeouts, connection pooling) with ApplyTo method | Configure shared Kafka client connection settings |
| `KafkaConsumerConfig.cs` | Consumer-specific configuration overrides (offset reset, timeouts, fetch settings) with ApplyTo method | Configure Kafka consumer behavior |
| `KafkaConsumerGroupBuilder.cs` | Fluent builder for consumer group configuration (topics, concurrency, distribution) | Configure Kafka consumer group registration |
| `KafkaConsumerHandlerBuilder.cs` | Per-consumer handler middleware configuration with compile-time type safety | Configure middleware for individual consumer handlers |
| `KafkaProducerBuilder.cs` | Fluent builder for producer configuration (acks, linger, batch) with per-producer outbound middleware | Configure individual Kafka producers |
| `KafkaProducerConfig.cs` | Producer configuration model holding serializer and topic settings | Understand producer configuration structure |
| `KafkaSchemaRegistryConfig.cs` | Schema Registry connection configuration (URL, timeouts, retries, caching, auth) with ApplyTo method | Configure Confluent Schema Registry connection |
| `KafkaTopicBuilder.cs` | Fluent builder for topic-level consumer configuration | Configure per-topic consumer settings |
| `KafkaTopicBuilderExtensions.cs` | Convenience extension methods providing built-in serializers/deserializers (Utf8, ByteArray, Null, numeric, Ignore) | Use built-in Kafka serializers |
