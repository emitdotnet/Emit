# Approach 1: Dedicated `IBatchConsumer<TValue>` Interface (MassTransit-Style)

## Inspiration

MassTransit's `IConsumer<Batch<T>>` pattern. The batch is a first-class message type wrapping multiple items.

## User Experience

```csharp
public class OrderBatchConsumer : IBatchConsumer<OrderCreated>
{
    public async Task ConsumeAsync(BatchConsumeContext<OrderCreated> context, CancellationToken ct)
    {
        // context.Messages is IReadOnlyList<ConsumeContext<OrderCreated>>
        // Each item carries its own TransportContext (topic, partition, offset, key)

        await using var transaction = await db.BeginTransactionAsync(ct);
        foreach (var item in context.Messages)
        {
            await db.InsertAsync(item.Message, ct);
        }
        await transaction.CommitAsync(ct);

        // If this method throws, NO offsets are committed for ANY message in the batch.
        // The entire batch will be redelivered.
    }
}
```

**Registration:**
```csharp
.ConsumerGroup("orders-group", c =>
{
    c.AddBatchConsumer<OrderBatchConsumer>(batch =>
    {
        batch.MaxBatchSize = 100;
        batch.MaxBatchTimeout = TimeSpan.FromSeconds(5);
    });
    c.OnError(e => e.DeadLetter()); // error policy still applies per-batch
});
```

## New Types in `Emit.Abstractions`

```csharp
public interface IBatchConsumer<TValue>
{
    Task ConsumeAsync(BatchConsumeContext<TValue> context, CancellationToken cancellationToken);
}

public class BatchConsumeContext<T> : MessageContext
{
    public required IReadOnlyList<ConsumeContext<T>> Messages { get; init; }
    public required TransportContext TransportContext { get; init; }
    // MessageId = batch ID (new GUID)
    // Timestamp = batch creation time
    // Services = batch-level DI scope
}
```

## Internal Changes

### `ConsumerWorker<TKey, TValue>` — Add Batch Accumulation

The worker's `RunAsync` method gets a parallel code path. When the registration indicates batch consumers exist, messages are accumulated into a buffer instead of immediately dispatched:

```
New flow:
  reader.TryRead(out raw)
    → deserialize
    → add to batch buffer (List<(ConsumeResult, DeserializedMessage)>)
    → if buffer.Count >= MaxBatchSize OR timer elapsed:
        → FlushBatchAsync()
        → mark ALL offsets as processed (or none if exception)
```

### Batch Offset Strategy

The key insight: **all offsets in a batch must be committed atomically or not at all.**

- Before dispatch: `offsetManager.Enqueue(...)` for each message in the batch
- On success: `offsetManager.MarkAsProcessed(...)` for each message
- On failure: do NOT mark any offset → watermark stays, batch will be reprocessed on restart

This works with the existing watermark algorithm because:
- Messages within a single worker are processed sequentially
- If a batch fails, the watermark for those partitions stays where it was
- On restart/rebalance, Kafka redelivers from the last committed offset

### `ConsumerGroupRegistration<TKey, TValue>`

Add batch config fields:
```csharp
public int? MaxBatchSize { get; init; }
public TimeSpan? MaxBatchTimeout { get; init; }
public bool HasBatchConsumers { get; init; }
```

### Pipeline Adaptation

Three options for how batches flow through the middleware pipeline:

**Option A: Batch pipeline is a separate pipeline typed on `BatchConsumeContext<T>`**
- New `BatchHandlerInvoker<TValue>` terminal
- Middleware pipeline is `IMiddlewarePipeline<BatchConsumeContext<TValue>>`
- Requires either duplicating middleware or making middleware generic on context type
- **Problem**: All existing middleware (`ConsumeErrorMiddleware`, `RetryMiddleware`, etc.) is typed on `ConsumeContext<TValue>`, not `BatchConsumeContext<TValue>`. Would need rewrite or adapters.

**Option B: Run the outer pipeline once per batch, with a synthetic ConsumeContext**
- Create a synthetic `ConsumeContext<TValue>` where `Message` is the first item (or a sentinel)
- Middleware pipeline runs normally (error, tracing, metrics, observers)
- At the terminal, instead of `HandlerInvoker`, a `BatchHandlerInvoker` resolves `IBatchConsumer<TValue>` and passes the full `BatchConsumeContext<TValue>`
- **Problem**: ConsumeContext.Message is misleading (it's not the real message), and middleware that inspects the message (validation, filters) won't work correctly for batches.

**Option C: `ConsumerPipelineComposer<MessageBatch<TValue>>` using the existing `Compose` method**
- `MessageBatch<TValue>` is treated as a first-class `TValue` — just another generic instantiation
- The existing `Compose` method is called with `TValue = MessageBatch<T>`, producing a typed pipeline `IMiddlewarePipeline<ConsumeContext<MessageBatch<T>>>`
- A new `BatchHandlerInvoker<TValue>` terminal resolves `IBatchConsumer<TValue>` (or an `IConsumer<MessageBatch<TValue>>`) at the end of that pipeline
- **Key insight**: This is not pipeline duplication — it is a second generic instantiation of the same composition function, exactly as generics are intended to work
- All cross-cutting middleware (error, tracing, metrics, observers) reuses the same implementations; only the terminal differs
- This dissolves the "pipeline type incompatibility" conclusion: the existing composition infrastructure handles it without modification

## Pros

- Clean, explicit user API: the user knows they're writing a batch consumer
- Separation of concerns: single-message and batch consumers are distinct types
- MassTransit-proven pattern — familiar to .NET ecosystem developers
- Strongly typed batch context with access to individual message metadata
- Error semantics are clear: throw = entire batch fails

## Cons

- **Pipeline adaptation required**: Existing middleware pipeline is typed on `ConsumeContext<TValue>`. Options A and B above have real friction, but Option C (generic instantiation via existing `Compose`) removes this as a blocking concern — the same middleware implementations are reused with a different type argument, requiring only a new terminal invoker.
- **Cannot mix single and batch consumers in the same consumer group** (fan-out semantics become ambiguous — does a batch consumer get batches while a single consumer gets individual messages?)
- **Worker accumulation changes the processing model**: workers currently process-and-ack per message. Batch accumulation adds timer complexity and partial-batch edge cases (shutdown, rebalance)
- New interface means the user's existing `IConsumer<T>` implementations cannot be reused
- Validation middleware doesn't apply naturally to batches

## Verdict

**Clean user API with manageable internal work.** The apparent pipeline type incompatibility is resolved by Option C: calling the existing `Compose` method with `TValue = MessageBatch<T>` produces a fully functional pipeline without duplicating middleware code. The remaining work — a new `BatchHandlerInvoker` terminal and worker-level accumulation — is incremental. The main costs are worker accumulation complexity and the new `IBatchConsumer<T>` interface sitting alongside `IConsumer<T>`.

---

## Decision Critic Assessment

### Verdict Stress Test: PARTIALLY JUSTIFIED
The original verdict overstated the pipeline incompatibility as a "fatal flaw." Option C — calling existing `Compose` with `TValue = MessageBatch<T>` — dissolves it. The real frictions are (1) worker accumulation complexity, (2) the separate `IBatchConsumer<T>` interface, and (3) validation semantics on a batch type. These are real costs but not blockers.

### Hidden Assumption: "All middleware needs the typed message"
The analysis assumes all middleware in the pipeline inspects `context.Message`. In reality, most cross-cutting middleware (error, tracing, metrics, observers) never reads `Message` — they only use `MessageId`, `TransportContext`, `CancellationToken`, and `Services`. Only validation and the handler terminal access `Message`. This means Option C's pipeline, instantiated with `MessageBatch<T>`, reuses virtually all existing middleware unchanged.

### Overlooked Risk: MassTransit Comparison is Misleading
The document says "MassTransit can do this because `Batch<T>` IS the message type." But MassTransit's actual implementation is far more complex — it uses dual pipes, TaskCompletionSource blocking, and a collector pattern. The simplicity implied by "Batch<T> IS the message type" hides massive internal complexity. This approach's rejection based on MassTransit comparison is valid but the reasoning is incomplete.

### Strongest Argument FOR This Approach
The user API is excellent. `IBatchConsumer<T>` is the clearest possible declaration of intent. This advantage is real and persists into Approach 14 which inherits it.

### Strongest Argument AGAINST
The `IBatchConsumer<T>` interface sits alongside `IConsumer<T>`, introducing two handler contracts. This is real API surface that users must understand and that the framework must maintain in parallel. Worker accumulation complexity (timeout, partial-batch on shutdown/rebalance) is non-trivial to get right reliably.
