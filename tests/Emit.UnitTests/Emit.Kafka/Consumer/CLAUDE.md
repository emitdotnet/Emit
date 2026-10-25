# Consumer/

Unit tests for Kafka consumer internals: worker pool, offset management, DLQ producer, and distribution strategies.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `ByKeyHashStrategyTests.cs` | Unit tests for ByKeyHash worker distribution strategy | Debugging ByKeyHash strategy test failures |
| `ConsumerGroupRegistrationTests.cs` | Unit tests for consumer group registration and configuration | Debugging consumer group registration test failures |
| `ConsumerGroupWorkerTests.cs` | Unit tests for ConsumerGroupWorker lifecycle and message dispatch | Debugging consumer group worker test failures |
| `ConsumerWorkerTests.cs` | Unit tests for ConsumerWorker message processing loop | Debugging consumer worker test failures |
| `KafkaTransportContextTests.cs` | Unit tests for the tracing attributes of a Kafka message: where it was consumed from, its key and tombstone, none while producing | Debugging Kafka span attribute test failures |
| `DlqProducerTests.cs` | Unit tests for DLQ producer message forwarding and header decoration | Debugging DLQ producer test failures |
| `KafkaConsumerFlowControlTests.cs` | Unit tests for consumer flow control: separate holds, partitions not yet assigned, refused messages, waiting for messages being handled, partitions paused for a full queue, detach | Debugging flow control test failures |
| `OffsetCommitterTests.cs` | Unit tests for offset committer watermark tracking and commit, and discarding the offsets of partitions lost | Debugging offset committer test failures |
| `OffsetManagerTests.cs` | Unit tests for offset manager coordination across partitions and the first unhandled offset of a partition | Debugging offset manager test failures |
| `PartitionOffsetsTests.cs` | Unit tests for per-partition offset tracking | Debugging partition offset test failures |
| `RoundRobinStrategyTests.cs` | Unit tests for RoundRobin worker distribution strategy | Debugging RoundRobin strategy test failures |
| `StartupDiagnosticsLoggerTests.cs` | Unit tests for startup diagnostics log output | Debugging startup diagnostics test failures |
| `PartitionAssignmentHandlerTests.cs` | The worker pool following rebalances, and the worker a message goes to (none while the host stops): kept partitions rewound to their first unhandled message and a new pool started, nothing rewound when everything is revoked, the running pool kept on an incremental assignment, lost partitions cancelled at once without commit, a faulted pool replaced, no pool started while the host stops | Debugging rebalance handling test failures |
| `WorkerPoolTestKit.cs` | Worker pools running real workers, a committer, messages, and a consumer whose handling blocks, for the pool and rebalance tests | Write a test that stops, replaces or starts worker pools |
| `WorkerPoolSupervisorTests.cs` | Worker pool supervisor: fault detection, and a stop that cancels handlers outlasting the stop timeout (at once for lost partitions), waits a bounded time for them, and is not held by a blocking cancellation callback | Debugging worker pool supervisor test failures |
| `BatchAccumulatorTests.cs` | Batch accumulation: wait for the first message, full and partial batches, cancellation returning the messages taken, completed channel | Debugging batch accumulation test failures |
