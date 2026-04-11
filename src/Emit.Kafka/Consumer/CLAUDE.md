# Consumer/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ConsumerGroupWorker.cs` | BackgroundService managing Kafka consumer group lifecycle and partition assignment | Understand consumer group orchestration |
| `ConsumerWorker.cs` | Per-partition/per-group worker processing messages via channel and IConsumer | Understand message dispatch and processing flow |
| `ConsumerGroupRegistration.cs` | Immutable descriptor capturing build-time consumer group configuration | Understand consumer group registration model |
| `KafkaConsumerFlowControl.cs` | IConsumerFlowControl implementation for pausing and resuming Kafka consumer partitions | Implement flow control or backpressure handling |
| `KafkaTransportContext.cs` | Kafka-specific TransportContext subclass carrying topic, partition, and offset metadata | Understand Kafka transport context or implement Kafka-specific middleware |
| `KafkaDeadLetterHeaders.cs` | Kafka-specific dead-letter header constants for source topic, partition, and offset | Implement DLQ producers or read Kafka-specific DLQ diagnostic headers |
| `DlqProducer.cs` | Dead letter queue producer for routing failed messages to DLQ topics | Understand DLQ message routing or debug DLQ behavior |
| `StartupDiagnosticsLogger.cs` | Logs consumer group startup diagnostics and configuration | Debug consumer startup issues or understand consumer configuration |
| `OffsetCommitter.cs` | Accumulates committable watermark offsets and periodically flushes to Kafka | Understand or debug offset commit behavior |
| `OffsetManager.cs` | Routes enqueue/completion calls to per-partition PartitionOffsets trackers | Understand offset tracking across partitions |
| `PartitionOffsets.cs` | Per-partition contiguous watermark algorithm for safe offset commits | Understand or debug offset watermark tracking |
| `IDistributionStrategy.cs` | Contract for selecting which worker processes a given message | Implement new distribution strategies |
| `ByKeyHashStrategy.cs` | Key hash-based message distribution preserving key-level ordering | Understand or modify key-based distribution |
| `RoundRobinStrategy.cs` | Even round-robin message distribution across workers | Understand or modify round-robin distribution |
| `TopicPartitionKey.cs` | Composite key record for identifying Kafka topic-partition pairs | Reference topic-partition identification |
| `BatchAccumulator.cs` | Two-phase batch accumulation algorithm: block for first message, drain available, wait up to timeout | Understand or debug batch message accumulation |
| `BatchConfig.cs` | Batch accumulation configuration: MaxSize (default 100) and Timeout (default 5s) per worker | Configure batch consumer sizing |
| `WorkerPoolSupervisor.cs` | Manages a pool of consumer workers: creation, health monitoring, fault detection, and graceful shutdown | Understand worker pool lifecycle or debug consumer worker management |
