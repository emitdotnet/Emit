# Consumer/

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ConsumerGroupWorker.cs` | BackgroundService managing the Kafka consumer lifecycle, the poll loop, and the rebalance callbacks' logging, metrics, observers and flow control | Understand consumer group orchestration |
| `PartitionAssignmentHandler.cs` | Keeps the worker pool and its offset manager in step with the partitions assigned, and picks the worker each message goes to (none while the host stops): a revoke stops the pool and rewinds the partitions kept to their first unhandled message, lost partitions cancel the handlers at once and drop their pending commits, an incremental assignment keeps the running pool, a faulted pool is replaced | Understand rebalances or change how the pool follows them |
| `ConsumerWorker.cs` | Per-partition/per-group worker processing messages via channel and IConsumer: adds where each message was consumed from to its headers and takes its message id from them; retries a deserialization that failed for a reason other than invalid data; signals when its full queue is half empty again | Understand message dispatch and processing flow |
| `ConsumerGroupRegistration.cs` | Immutable descriptor capturing build-time consumer group configuration | Understand consumer group registration model |
| `KafkaConsumerFlowControl.cs` | A declaration's pause state: separate application and circuit breaker holds, partitions paused until a full worker queue has room, the partitions reported by the rebalance handlers, and the count of messages being handled that an application pause waits for | Change how a consumer group is paused or resumed |
| `KafkaTransportContext.cs` | Kafka-specific TransportContext subclass carrying topic, partition, offset, consumer group, key and tombstone, and the tracing attributes they give | Understand Kafka transport context or implement Kafka-specific middleware |
| `KafkaDeadLetterHeaders.cs` | Kafka-specific header constants for the source partition and offset the consumer adds to each message | Implement DLQ producers or read Kafka-specific DLQ diagnostic headers |
| `DlqProducer.cs` | Dead letter queue producer for routing failed messages to DLQ topics; its send span becomes the dead letter's creation context | Understand DLQ message routing or debug DLQ behavior |
| `StartupDiagnosticsLogger.cs` | Logs consumer group startup diagnostics and configuration | Debug consumer startup issues or understand consumer configuration |
| `OffsetCommitter.cs` | Accumulates committable watermark offsets and periodically flushes to Kafka; drops those of partitions lost | Understand or debug offset commit behavior |
| `OffsetManager.cs` | Routes enqueue/completion calls to per-partition PartitionOffsets trackers, and gives a partition's first unhandled offset, where a kept partition is rewound to when its pool stops; one per worker pool, retired when the pool is replaced | Understand offset tracking across partitions |
| `PartitionOffsets.cs` | Per-partition contiguous watermark algorithm for safe offset commits | Understand or debug offset watermark tracking |
| `IDistributionStrategy.cs` | Contract for selecting which worker processes a given message | Implement new distribution strategies |
| `ByKeyHashStrategy.cs` | Key hash-based message distribution preserving key-level ordering | Understand or modify key-based distribution |
| `RoundRobinStrategy.cs` | Even round-robin message distribution across workers | Understand or modify round-robin distribution |
| `TopicPartitionKey.cs` | Composite key record for identifying Kafka topic-partition pairs | Reference topic-partition identification |
| `BatchAccumulator.cs` | Two-phase batch accumulation algorithm: block for first message, drain available, wait up to timeout | Understand or debug batch message accumulation |
| `BatchOptions.cs` | Batch accumulation configuration: MaxSize (default 100) and Timeout (default 5s) per worker | Configure batch consumer sizing |
| `WorkerPoolSupervisor.cs` | Manages a pool of consumer workers: creation, health monitoring, fault detection, and stop: the workers get the stop timeout to finish, are then cancelled and given the timeout again; a stopped worker takes no further message, and a handler ignoring cancellation is logged and left running | Understand worker pool lifecycle, rebalances, or debug consumer worker management |
