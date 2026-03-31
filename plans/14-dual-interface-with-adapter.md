# Approach 14: `IBatchConsumer<T>` as Adapter to `IConsumer<MessageBatch<T>>`

## Concept

Combine the best of Approach 12 (clean `IBatchConsumer<T>` API) with Approach 13 (zero forked pipeline composition). The trick: `IBatchConsumer<TValue>` is a user-facing convenience interface, but internally it's adapted into `IConsumer<MessageBatch<TValue>>`. The pipeline runs on `ConsumeContext<MessageBatch<TValue>>`, going through the same `Compose` path and all existing middleware via generics.

## User Experience

```csharp
// Clean dedicated interface
public class OrderBatchConsumer : IBatchConsumer<OrderCreated>
{
    public async Task ConsumeAsync(
        BatchConsumeContext<OrderCreated> context,
        CancellationToken ct)
    {
        foreach (var item in context.Items)
        {
            await db.UpsertAsync(item.Message, ct);
        }
    }
}
```

**Registration:**
```csharp
topic.ConsumerGroup("orders-group", group =>
{
    group.Batch(batch =>
    {
        batch.MaxSize = 100;
        batch.Timeout = TimeSpan.FromSeconds(5);
    });
    group.AddBatchConsumer<OrderBatchConsumer>();
    group.OnError(e => e.DeadLetter());
});
```

## Internal Architecture

### The Adapter Pattern

`IBatchConsumer<TValue>` is registered with a DI adapter that wraps it as `IConsumer<MessageBatch<TValue>>`:

```csharp
// User implements this (clean API):
public interface IBatchConsumer<TValue>
{
    Task ConsumeAsync(BatchConsumeContext<TValue> context, CancellationToken ct);
}

// Internal adapter (registered in DI automatically):
internal sealed class BatchConsumerAdapter<TValue, TConcrete> : IConsumer<MessageBatch<TValue>>
    where TConcrete : class, IBatchConsumer<TValue>
{
    public async Task ConsumeAsync(
        ConsumeContext<MessageBatch<TValue>> context,
        CancellationToken ct)
    {
        // Resolve the real batch consumer from DI
        var batchConsumer = context.Services.GetRequiredService<TConcrete>();

        // Adapt ConsumeContext<MessageBatch<T>> → BatchConsumeContext<T>
        var batchContext = new BatchConsumeContext<TValue>
        {
            MessageId = context.MessageId,
            Timestamp = context.Timestamp,
            CancellationToken = context.CancellationToken,
            Services = context.Services,
            Items = context.Message.Items,
            TransportContext = context.TransportContext,
            RetryAttempt = context.RetryAttempt,
        };

        await batchConsumer.ConsumeAsync(batchContext, ct).ConfigureAwait(false);
    }
}
```

### Pipeline Flow

```
Worker accumulates batch
    → Creates ConsumeContext<MessageBatch<TValue>>
    → Pipeline: Error<MBatch<T>> → Tracing<MBatch<T>> → Metrics<MBatch<T>> → ... → HandlerInvoker<MBatch<T>>
        → Resolves IConsumer<MessageBatch<TValue>> = BatchConsumerAdapter<TValue, OrderBatchConsumer>
            → Adapter resolves IBatchConsumer<TValue> = OrderBatchConsumer
            → Adapter creates BatchConsumeContext<TValue>
            → Adapter calls OrderBatchConsumer.ConsumeAsync(batchContext, ct)
```

### DI Registration

In `KafkaBuilder.RegisterConsumerGroup`:
```csharp
foreach (var batchConsumerType in groupBuilder.BatchConsumerTypes)
{
    // Register the user's batch consumer as scoped
    services.TryAddScoped(batchConsumerType);

    // Register the adapter as scoped
    var adapterType = typeof(BatchConsumerAdapter<,>).MakeGenericType(typeof(TValue), batchConsumerType);
    var iConsumerType = typeof(IConsumer<>).MakeGenericType(typeof(MessageBatch<TValue>));
    services.TryAddScoped(adapterType);
}
```

### HandlerInvoker for Batch

`HandlerInvoker<MessageBatch<TValue>>` is created with the ADAPTER type, not the user's consumer type:

```csharp
var adapterType = typeof(BatchConsumerAdapter<,>).MakeGenericType(typeof(TValue), consumerType);
var invoker = new HandlerInvoker<MessageBatch<TValue>>(adapterType);
```

The invoker resolves `BatchConsumerAdapter<TValue, OrderBatchConsumer>` from DI, which in turn resolves `OrderBatchConsumer`.

### Dead-Letter: Same `IBatchMessage` Solution as Approach 13

`MessageBatch<T>` implements `IBatchMessage`, and `ConsumeErrorMiddleware` detects it:

```csharp
// In ConsumeErrorMiddleware.ExecuteDeadLetterAsync:
if (context.Message is IBatchMessage batchMessage)
{
    foreach (var itemTransport in batchMessage.GetItemTransportContexts())
    {
        await deadLetterSink.ProduceAsync(
            itemTransport.RawKey, itemTransport.RawValue, headers, ct);
    }
}
else
{
    // Existing single-message dead-letter logic
}
```

## Key Types

```csharp
// Emit.Abstractions — User-facing
public interface IBatchConsumer<TValue>
{
    Task ConsumeAsync(BatchConsumeContext<TValue> context, CancellationToken ct);
}

public class BatchConsumeContext<T> : MessageContext
{
    public required IReadOnlyList<BatchItem<T>> Items { get; init; }
    public required TransportContext TransportContext { get; init; }
    public int RetryAttempt { get; set; }
}

public sealed class BatchItem<T>
{
    public required T Message { get; init; }
    public required TransportContext TransportContext { get; init; }
}

public sealed class MessageBatch<T> : IReadOnlyList<BatchItem<T>>, IBatchMessage
{
    internal IReadOnlyList<BatchItem<T>> Items { get; }
    // IReadOnlyList implementation...
    // IBatchMessage implementation...
}

// Emit (internal) — Adapter
internal sealed class BatchConsumerAdapter<TValue, TConcrete> : IConsumer<MessageBatch<TValue>>
    where TConcrete : class, IBatchConsumer<TValue> { /* ... */ }
```

## Files Changed

### New Files (~8)
1. `src/Emit.Abstractions/IBatchConsumer.cs`
2. `src/Emit.Abstractions/BatchConsumeContext.cs`
3. `src/Emit.Abstractions/BatchItem.cs`
4. `src/Emit.Abstractions/MessageBatch.cs` (+ `IBatchMessage` internal interface)
5. `src/Emit/Pipeline/BatchConsumerAdapter.cs`
6. `src/Emit.Kafka/DependencyInjection/BatchConfigBuilder.cs`
7. `src/Emit.Kafka/Consumer/BatchConfig.cs`

### Modified Files (~5)
1. `src/Emit.Kafka/DependencyInjection/KafkaConsumerGroupBuilder.cs` — add Batch(), AddBatchConsumer()
2. `src/Emit.Kafka/DependencyInjection/KafkaBuilder.cs` — batch registration path in RegisterConsumerGroup
3. `src/Emit.Kafka/Consumer/ConsumerGroupRegistration.cs` — batch config + pipeline factory
4. `src/Emit.Kafka/Consumer/ConsumerWorker.cs` — batch accumulation loop + FanOutBatchAsync
5. `src/Emit/Consumer/ConsumeErrorMiddleware.cs` — add `IBatchMessage` check for per-item DLQ

### NOT Changed (Zero Forked Pipeline Composition)
The same `ConsumerPipelineComposer.Compose` method builds the pipeline for batch consumers. No `ComposeBatch` fork. No batch middleware shells.
- `ConsumeErrorMiddleware` — tiny addition (IBatchMessage check), not a parallel shell
- `RetryMiddleware` — works as-is for `MessageBatch<T>`
- `ConsumeTracingMiddleware` — works as-is
- `ConsumeMetricsMiddleware` — works as-is
- `ConsumeObserverMiddleware` — works as-is
- `ConsumerPipelineComposer` — works as-is (no new `ComposeBatch` needed)
- `HandlerInvoker` — works as-is (resolves adapter type)
- `MiddlewarePipeline` — unchanged
- `MessagePipelineBuilder` — unchanged

## Pros

- **Clean user API**: `IBatchConsumer<T>` is explicit and discoverable
- **ZERO forked pipeline composition**: No `ComposeBatch` method; same `Compose` path for both single and batch consumers
- **ZERO structural duplication**: No batch middleware shells that must track their single-message counterparts
- **ZERO pipeline composer changes**: `ConsumerPipelineComposer.Compose` works as-is
- **ZERO handler invoker changes**: `HandlerInvoker<MessageBatch<T>>` resolves the adapter
- **Adapter pattern is clean**: User sees `IBatchConsumer<T>`, internals see `IConsumer<MessageBatch<T>>`
- **Per-item dead-lettering**: Via `IBatchMessage` marker with minimal change to error middleware
- **Retry works correctly**: `RetryMiddleware<MessageBatch<T>>` retries the entire batch
- **Tracing, metrics, observers**: All work unchanged
- **Registration validation**: Batch consumers and batch config are validated together
- **Fewest new files**: ~8 new, ~5 modified, 0 parallel middleware shells

## Cons

- **Adapter indirection**: One extra DI resolve per batch (adapter → real consumer). Negligible perf impact but adds a layer to debugging.
- **`IBatchMessage` in error middleware**: The single-message error middleware must detect batches. This is a small but non-zero coupling.
- **`MessageBatch<T>` is mostly internal**: Users never see it directly (they interact with `BatchConsumeContext<T>`), but it exists in the type hierarchy
- **Cannot mix single + batch consumers by default**: Design choice enforced at registration, not a hard platform constraint. Mixing could be supported if semantics were defined.
- **BatchConsumeContext.RetryAttempt**: Adapter must forward retry attempt from `ConsumeContext` — slightly awkward plumbing

## Comparison with Approach 12

The user API is identical. The fundamental difference is whether the pipeline composition is forked or reused.

| Aspect | Approach 12 (Dedicated Pipeline) | Approach 14 (Adapter) |
|--------|----------------------------------|----------------------|
| User API | `IBatchConsumer<T>` | `IBatchConsumer<T>` (same) |
| Pipeline composition | `ComposeBatch` fork of `Compose` | Same `Compose` path |
| Batch middleware shells | ~5 (each tracks single-msg counterpart) | 0 |
| Error middleware changes | New `BatchErrorMiddleware` shell | Small `IBatchMessage` check |
| Handler invoker | New `BatchHandlerInvoker` | Reuse `HandlerInvoker` |
| Total new files | ~12 | ~8 |
| Total modified files | ~6 | ~5 |
| Structural duplication | Yes — forked composer + shells | None |
| Indirection | Direct | Adapter layer (one extra DI resolve per batch) |
| Pipeline context type | `BatchConsumeContext<T>` | `ConsumeContext<MessageBatch<T>>` |

## Verdict

**Best balance of clean API + zero structural duplication.** This approach gets the clean `IBatchConsumer<T>` user API while reusing 100% of the existing pipeline composition via the adapter pattern. There is no `ComposeBatch` fork, no batch middleware shells to keep in sync. The only change to existing middleware is a small `IBatchMessage` check in the error middleware for per-item dead-lettering. This is the most architecturally sound solution — maximum reuse, zero forked composition, clean separation of concerns.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes one level of adapter indirection has "negligible perf impact." This is almost certainly true for the batch use case (one adapter invocation per batch, not per message), but should be validated with a benchmark if batch processing becomes latency-sensitive.
- Assumes `BatchConsumeContext<T>` as a separate type (not inheriting `ConsumeContext<T>`) is the right choice. This means batch consumers cannot use extension methods or utilities written for `ConsumeContext<T>`. Inheritance IS an option — `BatchConsumeContext<T> : ConsumeContext<T>` could work if all `ConsumeContext<T>` properties map cleanly. It should be avoided only if it introduces ghost/unused properties (e.g., `Message` would refer to a single item when the context is inherently multi-item). If the inheritance is seamless, it removes the "two worlds" problem.
- Assumes `RetryAttempt` forwarding from `ConsumeContext` to `BatchConsumeContext` via the adapter is straightforward. It is, but it means `BatchConsumeContext.RetryAttempt` is set by the adapter, not by the retry middleware directly. If retry middleware ever sets other properties (e.g., `RetryDelay`), the adapter must be updated to forward those too.

### Risks Not Discussed
- **Adapter registration complexity**: `typeof(BatchConsumerAdapter<,>).MakeGenericType(typeof(TValue), batchConsumerType)` is runtime generic construction. If AOT/trimming is ever a goal, this pattern is problematic. It works perfectly under JIT, but is worth noting.
- **`IBatchMessage` scope creep**: Once the error middleware checks for `IBatchMessage`, other middleware authors might add similar checks (metrics per-item, tracing per-item). This gradual "batch awareness leak" could erode the "zero middleware changes" claim over time.
- **`MessageBatch<T>` internal visibility**: If `MessageBatch<T>` is public (it must be, for the generic pipeline to work), advanced users might try to build `IConsumer<MessageBatch<T>>` directly, bypassing the adapter and `IBatchConsumer<T>`. This creates two ways to do the same thing.

### Verdict Justification
The verdict ("best balance of clean API + zero structural duplication") is **well-justified and correctly identifies this as the optimal approach**. The evidence is strong:
- Zero forked pipeline composition (proven by Approach 13's analysis — `MessageBatch<T>` flows through the same `Compose` path)
- Zero batch middleware shells (no parallel structures that must track single-message counterparts)
- Clean user API (proven by contrasting with Approach 13's `IConsumer<MessageBatch<T>>`)
- Minimal file changes (~8 new, ~5 modified)
- Per-item dead-lettering via `IBatchMessage` (small, well-contained change)

The comparison table with Approach 12 makes the architectural distinction clear: the key difference is forked composition vs reused composition, not raw file count. The only risk is the adapter indirection and `IBatchMessage` marker, both of which are well-understood patterns with minimal downside.

### Blind Spots
- Doesn't discuss testing ergonomics. How does a user unit-test their `IBatchConsumer<T>`? They need to construct a `BatchConsumeContext<T>`, which requires `Items`, `TransportContext`, `Services`, etc. A test helper or builder would be valuable.
- Doesn't mention how `group.AddMiddleware<TMiddleware>()` (user-registered group-level middleware) interacts with batch consumers. Since the pipeline is typed on `ConsumeContext<MessageBatch<T>>`, user middleware registered as `IMiddleware<ConsumeContext<T>>` won't apply. This should be documented.
- Doesn't specify what happens if a user calls both `AddConsumer<T>()` and `AddBatchConsumer<T>()` in the same group. The validation should reject this clearly.

### Strongest Argument
The combination of **clean user API** (`IBatchConsumer<T>`) with **zero forked pipeline composition** (via adapter to `IConsumer<MessageBatch<T>>`) is genuinely elegant. No other approach achieves both simultaneously. This is the architectural sweet spot — the user gets the interface they want, and the internals get the type reuse they need through the same `Compose` path. The adapter pattern is the bridge that makes this possible.

**Overall: 9/10** — The strongest approach in the set. Loses one point for the `IBatchMessage` marker pattern (small but real coupling) and testing ergonomics blind spot. The core architecture is sound, the trade-offs are minimal and well-understood, and the implementation path is clear.
