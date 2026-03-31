# Current Architecture Summary

This document captures the relevant internals of Emit's Kafka consumer pipeline as context for the batch consumption design alternatives.

## Consumer Flow (Message-at-a-Time)

```
Kafka Broker
    │
    ▼
ConsumerGroupWorker<TKey, TValue> (BackgroundService, 1 per consumer group)
    │  consumer.Consume(ct) → ConsumeResult<byte[], byte[]>
    │  offsetManager.Enqueue(topic, partition, offset)
    │  strategy.SelectWorker(key, partition) → workerIndex
    │  workers[workerIndex].Writer.WriteAsync(result)
    │
    ▼
ConsumerWorker<TKey, TValue> (N workers per group, Channel<ConsumeResult<byte[],byte[]>>)
    │  reader.TryRead(out raw)
    │  DeserializeAsync(raw) → DeserializedMessage<TKey, TValue>
    │  FanOutAsync(raw, deserialized, ct) → for each consumer pipeline entry
    │      scope = scopeFactory.CreateAsyncScope()
    │      build KafkaTransportContext<TKey>
    │      build ConsumeContext<TValue>
    │      entry.Pipeline.InvokeAsync(context)
    │  offsetManager.MarkAsProcessed(topic, partition, offset)
    │
    ▼
Middleware Pipeline (per consumer entry, built by ConsumerPipelineComposer<TValue>)
    Error → Observer → Tracing → Metrics → Global/Provider/Group MW → Validation → Retry → Per-Entry → Transactional → Handler
    │
    ▼
HandlerInvoker<TValue>
    │  consumer = (IConsumer<TValue>)services.GetRequiredService(consumerType)
    │  consumer.ConsumeAsync(context, ct)
    │
    ▼
IConsumer<TValue>.ConsumeAsync(ConsumeContext<TValue> context, CancellationToken ct)
```

## Key Types

| Type | Role |
|------|------|
| `IConsumer<TValue>` | User-implemented handler for a single message |
| `ConsumeContext<T>` | Post-deserialization context: Message, TransportContext, Headers, RetryAttempt, Transaction |
| `MessageContext<T>` | Base: CancellationToken, Services, Features, MessageId, Timestamp, DestinationAddress |
| `KafkaTransportContext<TKey>` | Kafka transport: Topic, Partition, Offset, GroupId, Key, RawKey, RawValue |
| `ConsumerGroupRegistration<TKey,TValue>` | Immutable config: topic, group, deserializers, pipeline factory, worker settings |
| `ConsumerPipelineEntry<TValue>` | Pairs consumer identity with composed pipeline |
| `ConsumerPipelineComposer<TValue>` | Builds the full middleware chain for each consumer entry |
| `HandlerInvoker<TValue>` | Terminal: resolves `IConsumer<TValue>` from DI and calls `ConsumeAsync` |

## Offset Management

- **Manual commit, periodic flush** — `EnableAutoCommit = false`, `EnableAutoOffsetStore = false`
- `OffsetManager` → per-partition `PartitionOffsets` tracker
- **Watermark algorithm**: maintains a linked list of received offsets and a sorted set of completed-out-of-order offsets. Watermark advances only when the head of the received list completes.
- `OffsetCommitter` accumulates committable watermarks and flushes on timer (default 5s)
- **Critical for batching**: offset is marked processed only AFTER successful `FanOutAsync` + pipeline completion

## Worker Distribution

- **ByKeyHash** (default): same key always goes to same worker → preserves key-level ordering
- **RoundRobin**: even distribution, no ordering guarantees
- Worker pool: `Channel<ConsumeResult<byte[],byte[]>>` per worker, bounded (default 32)

## DI Scoping

- Each message gets a fresh `AsyncServiceScope` per consumer pipeline entry in `FanOutAsync`
- Retry middleware creates additional child scopes per retry attempt
- Consumer handlers are registered as `Scoped`

## Error Handling

- `ConsumeErrorMiddleware` (outermost): catches all, applies policy (dead-letter or discard)
- `RetryMiddleware`: configurable retries with exponential backoff, new scope per retry
- `ValidationMiddleware`: outside retry, validation failures skip retry
- Circuit breaker: monitors failure rate, pauses consumer group

## Key Design Constraints for Batch Feature

1. The middleware pipeline is typed on `ConsumeContext<TValue>` — single message context
2. `HandlerInvoker` calls `IConsumer<TValue>.ConsumeAsync(ConsumeContext<TValue>, CancellationToken)`
3. Offset is marked processed per-message after the entire pipeline completes
4. Each message gets its own DI scope
5. Fan-out iterates consumer pipelines sequentially per message
6. The watermark algorithm supports out-of-order completion, but batch semantics require all-or-nothing

---

## Decision Critic Assessment

### Accuracy: VERIFIED
Cross-referenced against source files. All descriptions factually correct.

### Critical Omission: Error Middleware Swallows Exceptions
`ConsumeErrorMiddleware` catches exceptions and returns normally — the worker sees success and marks the offset. Even "failed" messages (dead-lettered or discarded) have their offsets committed. For batch design, this is crucial: a batch handler throw is caught by error middleware, and offsets proceed regardless. Every batch approach must account for this — it means the error middleware's "success" return is the contract for offset advancement, not the user handler's success.

### Critical Omission: Offset Enqueue vs Mark Location
Offsets are enqueued in the **poll loop** (`ConsumerGroupWorker`), before distribution to workers. Marking happens in the **worker** (`ConsumerWorker`), after pipeline completion. For batching, the worker should own BOTH enqueue and mark (after accumulation, before dispatch). This changes the offset lifecycle and is under-documented.

### Constraint #1 is the Crux
"The middleware pipeline is typed on `ConsumeContext<TValue>`" is THE central constraint. It eliminates approaches 1, 3, 4, 5, 8 and makes 2, 13, 14 viable. Every approach that fails does so because of this constraint. It deserves more prominent placement as the primary architectural tension.
