# Industry Research Findings — Batch Consumption Across Frameworks

Compiled from deep research into MassTransit, KafkaFlow, Spring Kafka, Wolverine, NServiceBus, Azure Service Bus, and AWS SQS.

## MassTransit — `IConsumer<Batch<T>>`

### Architecture
- **User API**: `IConsumer<Batch<T>>` — the batch is a first-class message type
- **`Batch<T>`**: interface with `Length`, indexed access `[int]`, `IEnumerable<ConsumeContext<T>>`, `BatchCompletionMode` (Time/Size/Forced)
- **Transport subscribes to `T`, not `Batch<T>`** — individual messages are intercepted and accumulated internally

### Accumulation (Source-Level)
- **`BatchConsumerFactory<TMessage>`**: entry point, calls `BatchCollector.Collect()` for each message
- **`BatchCollector<TMessage>`**: manages lifecycle of `BatchConsumer` instances. Uses `TaskExecutor(1)` for serialization
- **`BatchConsumer<TMessage>`**: the accumulator. Holds `Dictionary<Guid, BatchEntry>` (deduplicates by MessageId)
  - Timer starts on construction with `TimeLimit`
  - `TimeLimitStart.FromLast` option resets timer on each message
  - `BatchCompletionMode.Forced` when a retry arrives (retried messages force-complete current batch)
- **Clever blocking trick**: Each individual message's `Consume()` call **awaits a shared `TaskCompletionSource`**. The transport thinks it's processing individual messages, but they all block until the batch is dispatched. On failure, `TrySetException()` faults all waiting messages independently.

### Configuration
```csharp
x.AddConsumer<OrderBatchConsumer>(cfg =>
    cfg.Options<BatchOptions>(options => options
        .SetMessageLimit(100)         // Default: 10
        .SetTimeLimit(s: 1)           // Default: 1 second
        .SetConcurrencyLimit(10)      // Default: 1
        .GroupBy<T, string>(ctx => ctx.Message.CustomerId)));
```
- **Auto-PrefetchCount**: `BatchOptions.DefaultConfigurationCallback` sets `PrefetchCount = ConcurrencyLimit * MessageLimit` automatically

### Error Handling
- On batch consumer throw → `TaskCompletionSource.TrySetException()` → each individual message faulted by transport independently
- Retried messages (`RetryAttempt > 0`) force-complete current batch immediately (batch of 1)
- **Two nested pipes**: outer pipe subscribes to `T` (per message ack), inner pipe subscribes to `Batch<T>` (user middleware)

### Kafka-Specific
- `BatchCheckpointer` is **separate** from consumer-level `Batch<T>` — it reduces offset commit frequency
- `CheckpointMessageCount = 5000`, `CheckpointInterval = 1 min` (default)

---

## KafkaFlow — Batching as Middleware

### Architecture
- **Batching is a middleware** (`BatchConsumeMiddleware`), not a separate consumer type
- Registered with `MiddlewareLifetime.Worker` — one instance per worker, own buffer per worker
- Disables `AutoMessageCompletion` per message, controls completion after batch dispatch

### Configuration
```csharp
.AddMiddlewares(middlewares => middlewares
    .AddDeserializer<JsonCoreDeserializer>()
    .AddBatching(100, TimeSpan.FromSeconds(10))
    .Add<MyBatchMiddleware>())
```

### Accumulation
- `List<IMessageContext>` buffer, `SemaphoreSlim(1,1)` for serialization
- First message starts `Task.Delay(timeout)` timer
- If `buffer.Count >= batchSize` → cancel timer, dispatch immediately
- Creates `BatchConsumeMessageContext` with `Message.Value = IReadOnlyCollection<IMessageContext>`

### Error Handling
- **On exception**: batch cleared, `Complete()` still called → **offsets committed on error** (batch is lost)
- **On OperationCanceledException (worker stop)**: sets `ShouldStoreOffset = false` → offsets NOT committed
- **No retry mechanism** built into batch middleware

### Key Issue (for Emit)
KafkaFlow's middleware model has **different offset semantics** than Emit. KafkaFlow middleware can set `ShouldStoreOffset = false` to prevent commits. Emit's pipeline returns and the worker marks offset — middleware can't control offset commit.

---

## Spring Kafka — `@KafkaListener` Batch Mode

### Architecture
- **Poll-based batching**: batch is whatever `KafkaConsumer.poll()` returns (no framework buffering layer)
- Batch size controlled by Kafka consumer properties: `max.poll.records`, `fetch.min.bytes`, `fetch.max.wait.ms`
- `factory.setBatchListener(true)` or `@KafkaListener(batch = "true")`

### Handler Signatures
```java
void listen(List<String> messages)
void listen(List<ConsumerRecord<Integer, String>> records)
void listen(List<Message<?>> list, Acknowledgment ack)
```

### Error Handling — Partial Batch Failure
```java
throw new BatchListenerFailedException("Failed", exception, failedRecord);
```
- Records before the failed index → offsets committed
- Failed record + subsequent records → retried according to `BackOff`
- When retries exhausted → failed record sent to DLT
- **Non-blocking retries (`@RetryableTopic`) NOT supported with batch listeners**

### Key Insight
Spring relies on Kafka's native poll batching rather than adding its own buffer. This is simpler but gives less control (can't set a custom timeout or min batch size independently of Kafka's fetch settings).

---

## Wolverine — `BatchMessagesOf<T>`

### Architecture
- Uses `BatchingChannel<Envelope>` (from JasperFx.Blocks library) — `System.Threading.Channels`-based accumulator
- Individual messages posted to channel; batch flushed on size or time trigger

### Configuration
```csharp
opts.BatchMessagesOf<Item>(batching =>
{
    batching.BatchSize = 500;           // Default: 100
    batching.TriggerTime = 1.Seconds(); // Default: 250ms
    batching.LocalExecutionQueueName = "items";
}).Sequential();
```

### Handler
```csharp
public static void Handle(Item[] items) { /* batch handler */ }
```

### Key Features
- **Grouping**: `DefaultMessageBatcher<T>` groups by `TenantId` before dispatching
- **Custom batching**: `IMessageBatcher` interface for arbitrary grouping
- **Durable inbox**: Individual messages marked `InBatch = true`, not marked handled until batch handler completes
- Failed batch messages go to dead letter queue individually

---

## NServiceBus — No Batch Consumption

- **Deliberately processes one message at a time** for transactional guarantees
- Recommends saga-based pattern for controlled bulk processing
- **Batched dispatch** (outgoing): all send/publish calls during handler are bundled and dispatched atomically after pipeline completes

---

## Azure Service Bus — Batch Receive

### API
```csharp
IReadOnlyList<ServiceBusReceivedMessage> messages =
    await receiver.ReceiveMessagesAsync(maxMessages: 10, maxWaitTime: TimeSpan.FromSeconds(5));
```

- **PeekLock** by default: each message must be individually completed/abandoned
- **No partial-batch-failure API**: application manages per-message settlement
- Azure Functions: `maxMessageBatchSize` up to 1000, with `minMessageBatchSize` option

---

## AWS SQS — Batch Receive

### API
```csharp
var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
{
    QueueUrl = queueUrl,
    MaxNumberOfMessages = 10,  // Hard limit: 10
    WaitTimeSeconds = 20       // Long polling
});
```

### Lambda Partial Failure
```csharp
return new SQSBatchResponse(failures); // Report per-item failures
```
- FIFO queue: must stop after first failure, return all subsequent as failures (ordering guarantee)

---

## Industry Consensus — Design Patterns

| Concern | Industry Pattern | Our Approach (14) |
|---------|-----------------|-------------------|
| **Trigger** | Size OR timeout (whichever first) | ✅ Same |
| **User interface** | Dedicated batch type or separate handler | ✅ `IBatchConsumer<T>` |
| **Batch accumulation** | Framework-internal (not user concern) | ✅ Worker-level |
| **Per-item metadata** | Available (partition, offset, headers) | ✅ Via `BatchItem<T>.TransportContext` |
| **Error: batch failure** | Entire batch fails (MassTransit, KafkaFlow) | ✅ Same semantics |
| **Error: partial failure** | Per-item (Spring, AWS) or whole batch (MassTransit) | Whole batch (simpler, user decides) |
| **Dead letter** | Per item (Spring) or per batch (varies) | ✅ Per item (via `IBatchMessage`) |
| **Retry** | Retry whole batch (MassTransit), no retry (KafkaFlow) | ✅ Retry whole batch |
| **Offset commit** | None until batch succeeds | ✅ Watermark stays until all marked |
| **Buffer isolation** | Per-worker (KafkaFlow), per-collector (MassTransit) | ✅ Per-worker |
| **Mixing single + batch** | Not supported (separate endpoints) | ✅ Not supported (separate groups) |

### Key Takeaway

**MassTransit's `IConsumer<Batch<T>>` is the closest ecosystem analog to our Approach 14.** The difference: MassTransit's accumulation happens inside the pipeline (messages block awaiting batch completion), while Emit's happens at the worker level (before pipeline entry). Our approach is **simpler and safer** — no `TaskCompletionSource` blocking, no dual-pipe architecture, no race conditions between timer and message delivery.

KafkaFlow's middleware approach confirmed our analysis in Approach 3 — the offset semantics conflict makes pure middleware batching problematic. KafkaFlow works around this with `AutoMessageCompletion` and `ShouldStoreOffset`, features Emit doesn't have. Our worker-level accumulation avoids this entirely.

---

## Decision Critic Assessment

### Quality of Research
The research is comprehensive and technically accurate. The source-level analysis of MassTransit's `BatchCollector`/`BatchConsumer` and KafkaFlow's `BatchConsumeMiddleware` demonstrates deep understanding, not surface-level documentation reading.

### Hidden Assumptions
- Assumes MassTransit is the "closest ecosystem analog." While true architecturally, MassTransit targets a different audience (enterprise service bus) with different performance characteristics. Emit targets high-throughput Kafka consumption, where MassTransit's `TaskCompletionSource` blocking pattern would be unacceptable.
- Assumes the industry consensus table represents settled design. In reality, batch consumption patterns are still evolving — Kafka Streams' `suppress()` operator and Flink's windowing model represent different approaches not covered here.

### Risks Not Discussed
- **KafkaFlow's offset lesson**: The document correctly identifies KafkaFlow's `ShouldStoreOffset` as a feature Emit lacks. But it doesn't explore whether Emit SHOULD add similar offset control. If Emit ever adds middleware-level offset control, Approach 3 (middleware batching) becomes viable.
- **Spring Kafka's simplicity**: The document notes that Spring "relies on Kafka's native poll batching" but doesn't evaluate whether Emit could do the same. If Emit exposed `max.poll.records` configuration and passed the poll result directly to consumers, it would be the simplest possible implementation (though less flexible).

### Verdict Justification
The key takeaway ("MassTransit's `IConsumer<Batch<T>>` is closest, but Emit's worker-level accumulation is simpler and safer") is **correct and well-supported**. The comparison of accumulation strategies (MassTransit's pipeline-level blocking vs Emit's worker-level pre-pipeline) correctly identifies Emit's approach as architecturally cleaner.

### What's Missing
- **Confluent's own batch consumption guidance**: The Confluent .NET client has `ConsumeBatch` (proposed/experimental). Its design rationale could inform Emit's approach.
- **Akka Streams/Kafka**: Akka's `groupedWithin(maxSize, timeout)` operator is the reactive equivalent of the batch accumulation pattern and is worth mentioning for completeness.
- **Performance benchmarks**: None of the researched frameworks publish batch-vs-single throughput benchmarks. This would be valuable data for Emit's implementation.

**Overall: 8/10** — Thorough, accurate, and directly applicable to the design decision. The MassTransit and KafkaFlow analyses are particularly valuable. Would benefit from streaming framework coverage (Kafka Streams, Flink, Akka) for completeness.
