# Approach 9: Batch Accumulation Outside Pipeline, Per-Item Pipeline Execution (Hybrid)

## Concept

Accumulate messages at the worker level, then run each message through the EXISTING middleware pipeline individually, but defer offset commits until the entire batch completes. The batch consumer receives all contexts AFTER they've been through the pipeline.

This is a hybrid: the pipeline runs per-message (preserving all middleware semantics), but offset commit is deferred until the batch-level consumer succeeds.

## User Experience

```csharp
public class OrderBatchConsumer : IBatchConsumer<OrderCreated>
{
    public async Task ConsumeAsync(
        BatchConsumeContext<OrderCreated> context,
        CancellationToken ct)
    {
        // Each item has already passed through validation, tracing, etc.
        foreach (var item in context.Items)
        {
            await db.UpsertAsync(item.Message, ct);
        }
    }
}
```

## How It Works

```
Worker accumulates N messages
    ↓
For each message in batch:
    → Run through middleware pipeline (error, tracing, metrics, validation, retry)
    → Pipeline terminal does NOT call IConsumer<T>
    → Instead, terminal adds the survived context to a collection
    ↓
After all messages processed through pipeline:
    → Build BatchConsumeContext<T> from survived contexts
    → Call IBatchConsumer<T>.ConsumeAsync(batchContext, ct)
    → On success: mark all offsets as processed
    → On failure: mark none
```

### Modified Terminal

Instead of `HandlerInvoker<TValue>` which calls `IConsumer<TValue>`, use a `BatchCollectorTerminal<TValue>`:

```csharp
internal sealed class BatchCollectorTerminal<TValue> : IHandlerInvoker<ConsumeContext<TValue>>
{
    private readonly ConcurrentBag<ConsumeContext<TValue>> collected = new();

    public Task InvokeAsync(ConsumeContext<TValue> context)
    {
        collected.Add(context);
        return Task.CompletedTask;
    }

    public IReadOnlyList<ConsumeContext<TValue>> GetCollected() => [.. collected];
    public void Clear() => collected.Clear();
}
```

### Worker Batch Flow

```csharp
// Accumulate raw messages
var rawBatch = AccumulateBatch(reader, ct);

// Enqueue all offsets
foreach (var raw in rawBatch)
    offsetManager.Enqueue(raw.Topic, raw.Partition.Value, raw.Offset.Value);

// Run each through the pipeline (pipeline terminal collects, doesn't dispatch)
var collector = new BatchCollectorTerminal<TValue>();
// ... pipeline is built with collector as terminal

foreach (var raw in rawBatch)
{
    var deserialized = await DeserializeAsync(raw);
    var context = BuildConsumeContext(deserialized);
    await pipeline.InvokeAsync(context); // collector captures context
}

// Build batch from collected contexts
var batchContext = new BatchConsumeContext<TValue>
{
    Items = collector.GetCollected(),
    // ...
};

// Dispatch to batch consumer
try
{
    var batchConsumer = scope.ServiceProvider.GetRequiredService<IBatchConsumer<TValue>>();
    await batchConsumer.ConsumeAsync(batchContext, ct);

    // Success: mark all offsets
    foreach (var raw in rawBatch)
        offsetManager.MarkAsProcessed(raw.Topic, raw.Partition.Value, raw.Offset.Value);
}
catch
{
    // Failure: don't mark offsets
}
```

## The Semantic Problems

### What Does the Pipeline Actually Do?

When each message runs through the pipeline:
- **Validation**: Individual messages are validated. If one fails → dead-lettered/discarded. Good.
- **Retry**: If pipeline fails for one message, it's retried. But what does "success" mean if the terminal just collects?
- **Error handling**: If validation fails or middleware throws, the error middleware handles it per-message. The message is removed from the batch. Good.
- **Tracing**: A span is created per message. But the real "work" happens later in the batch consumer.
- **Metrics**: Processing time is measured per-message, but the real processing time is the batch consumer call.

### The Filtering Problem

If the pipeline validates 100 messages and 3 fail validation (dead-lettered), the batch consumer receives 97 messages. This is actually GOOD — the batch contains only valid messages. But:
- The user expected 100 messages in the batch
- The 3 dead-lettered messages had their offsets committed by the error middleware, but the batch consumer hasn't confirmed the other 97 yet
- What if the batch consumer then throws? The 97 messages aren't committed, but the 3 dead-lettered ones already were → inconsistent offset state

### The DI Scope Problem

Each message gets its own scope in the pipeline. But the batch consumer call needs a scope too. And the batch consumer might need services that were set up in the per-message scopes (e.g., transaction context). This creates scope lifecycle issues.

## Pros

- Per-message validation, filtering, and dead-lettering work correctly
- Messages that fail validation are excluded from the batch (clean data to the consumer)
- The existing pipeline runs unmodified per message
- No pipeline type changes needed

## Cons

- **Two-phase processing is confusing**: Messages go through pipeline individually, then through batch consumer as a group. Two different processing models for the same message.
- **Offset inconsistency**: If individual messages are dead-lettered (offset committed) but batch consumer later throws (remaining offsets not committed), you get partial commit state.
- **Misleading tracing**: Per-message spans show "processing complete" but the real processing hasn't happened yet.
- **Wasted retry effort**: Pipeline retries individual messages, but the batch consumer might retry the whole batch anyway on failure.
- **Double processing overhead**: Each message is processed twice — once through the pipeline, once through the batch consumer.
- **Complex lifecycle**: Scope per message → collection → new scope for batch → different service instances.
- **No error policy for batch failure**: If the batch consumer throws, there's no per-message error handling — the entire batch fails without dead-letter routing.

## Verdict

**Clever but semantically broken.** The two-phase model creates inconsistencies in offset management, tracing, and error handling. The individual pipeline run is misleading — it suggests processing is complete when it isn't. And the partial commit problem (dead-lettered messages committed, batch messages not) violates the all-or-nothing semantics the user expects.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes the pipeline terminal can be replaced without affecting the pipeline's observable behavior. In reality, the terminal (HandlerInvoker) is THE contract between the pipeline and the user's code. Replacing it with a collector fundamentally changes what "pipeline completion" means — it no longer means "message processed."
- Assumes per-message validation before batching is desirable. For many batch use cases (bulk inserts, analytics), individual validation is unnecessary overhead — the batch consumer can validate the entire batch more efficiently.

### Risks Not Discussed
- **Scope leakage**: Each per-message pipeline run creates an `AsyncServiceScope`. But the services in those scopes (DbContext, HttpClient, etc.) may be disposed before the batch consumer accesses them through the collected contexts. This is a use-after-dispose time bomb.
- **Activity/span hierarchy**: Per-message tracing creates N spans, but the batch consumer's work isn't captured under any of them. The observability story is fragmented — you see N "message processed" spans that completed instantly (just collection) and one "batch processed" span that contains the real work, with no parent-child relationship.
- **Memory pressure**: Collecting N full `ConsumeContext<T>` objects means N DI scopes, N deserialized messages, N transport contexts, all held in memory simultaneously. For large batches, this could be significant.

### Verdict Justification
The verdict ("clever but semantically broken") is **precisely correct**. The two-phase model creates a semantic gap: the pipeline's job is to process a message to completion, but in this approach it only collects. This breaks the fundamental contract of the pipeline. The offset inconsistency (dead-lettered items committed, batch items not) is a concrete manifestation of this semantic gap.

### Blind Spots
- Doesn't explore whether the `BatchCollectorTerminal` could propagate exceptions back to the worker to prevent the partial-commit problem. If the collector detected that some messages were dead-lettered, it could signal the worker to adjust offset marking.
- Doesn't discuss ordering. The pipeline runs messages sequentially, but `ConcurrentBag` doesn't preserve insertion order. The batch consumer would receive messages in arbitrary order, which matters for ordered topics.

### Strongest Argument
The per-message validation/filtering idea (pipeline removes invalid messages before batching) is genuinely useful. If Emit ever needs per-item validation within batches, this approach's "filter then batch" concept could be adopted as a pre-processing step inside the worker's batch accumulation loop.

**Overall: 3/10** — Correctly rejected. The semantic inconsistency is fundamental, but the "filter then batch" concept has residual value.
