# Observability/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `IKafkaConsumerObserver.cs` | Observer interface for Kafka consumer-specific events (partition assignment, rebalance, offset commit) | Implement Kafka consumer monitoring or custom instrumentation |
| `KafkaConsumerEvents.cs` | Event data types for Kafka consumer observer callbacks | Understand Kafka consumer event payloads or implement observer handlers |
| `KafkaConsumerObserverInvoker.cs` | Invoker calling registered Kafka consumer observers for consumer lifecycle events | Understand observer invocation flow or customize Kafka observability |
| `TopicPartitionOffset.cs` | Record capturing topic, partition, and offset for Kafka consumer events | Reference Kafka offset tracking data model |
