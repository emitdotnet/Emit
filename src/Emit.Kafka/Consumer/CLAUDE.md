# Consumer/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ConsumerGroupWorker.cs` | BackgroundService managing Kafka consumer group lifecycle and partition assignment | Understand consumer group orchestration |
| `ConsumerWorker.cs` | Per-partition/per-group worker processing messages via channel and IConsumer | Understand message dispatch and processing flow |
| `ConsumerGroupRegistration.cs` | Immutable descriptor capturing build-time consumer group configuration | Understand consumer group registration model |
| `InboundKafkaContext.cs` | Inbound pipeline context carrying deserialized Kafka message data for consumer pipeline | Understand Kafka inbound pipeline context |
| `KafkaConsumerFlowControl.cs` | IConsumerFlowControl implementation for pausing and resuming Kafka consumer partitions | Implement flow control or backpressure handling |
| `DlqProducer.cs` | Dead letter queue producer for routing failed messages to DLQ topics | Understand DLQ message routing or debug DLQ behavior |
| `StartupDiagnosticsLogger.cs` | Logs consumer group startup diagnostics and configuration | Debug consumer startup issues or understand consumer configuration |
| `IKafkaFeature.cs` | Interface providing Kafka-specific metadata (topic, partition, offset) via feature collection | Access Kafka metadata in middleware |
| `KafkaFeature.cs` | IKafkaFeature implementation holding topic, partition, and offset values | Understand Kafka feature storage |
| `OffsetCommitter.cs` | Accumulates committable watermark offsets and periodically flushes to Kafka | Understand or debug offset commit behavior |
| `OffsetManager.cs` | Routes enqueue/completion calls to per-partition PartitionOffsets trackers | Understand offset tracking across partitions |
| `PartitionOffsets.cs` | Per-partition contiguous watermark algorithm for safe offset commits | Understand or debug offset watermark tracking |
| `IDistributionStrategy.cs` | Contract for selecting which worker processes a given message | Implement new distribution strategies |
| `ByKeyHashStrategy.cs` | Key hash-based message distribution preserving key-level ordering | Understand or modify key-based distribution |
| `RoundRobinStrategy.cs` | Even round-robin message distribution across workers | Understand or modify round-robin distribution |
| `TopicPartitionKey.cs` | Composite key record for identifying Kafka topic-partition pairs | Reference topic-partition identification |
| `WorkerPoolSupervisor.cs` | Manages a pool of consumer workers: creation, health monitoring, fault detection, and graceful shutdown | Understand worker pool lifecycle or debug consumer worker management |
