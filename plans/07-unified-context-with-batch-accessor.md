# Approach 7: Unified ConsumeContext with Batch Accessor via Features

## Concept

Keep `IConsumer<TValue>` as-is. When batch mode is enabled, the worker accumulates messages and invokes the consumer once per batch with a **representative** `ConsumeContext<TValue>` (using the first message). The batch data is attached via the `Features` collection, and the user accesses it through an extension method.

## User Experience

```csharp
public class OrderConsumer : IConsumer<OrderCreated>
{
    public async Task ConsumeAsync(ConsumeContext<OrderCreated> context, CancellationToken ct)
    {
        // Check if this is a batch context
        if (context.TryGetBatch(out var batch))
        {
            // batch is IReadOnlyList<BatchItem<OrderCreated>>
            foreach (var item in batch)
            {
                await db.UpsertAsync(item.Message, ct);
            }
        }
        else
        {
            // Single message mode
            await db.UpsertAsync(context.Message, ct);
        }
    }
}
```

**Extension method:**
```csharp
public static class BatchConsumeContextExtensions
{
    public static bool TryGetBatch<T>(
        this ConsumeContext<T> context,
        out IReadOnlyList<BatchItem<T>> batch)
    {
        var feature = context.Features.Get<BatchFeature<T>>();
        if (feature is not null)
        {
            batch = feature.Items;
            return true;
        }
        batch = default!;
        return false;
    }
}
```

**Registration:**
```csharp
.ConsumerGroup("orders-group", c =>
{
    c.Batch(batch =>
    {
        batch.MaxSize = 100;
        batch.Timeout = TimeSpan.FromSeconds(5);
    });
    c.AddConsumer<OrderConsumer>();
});
```

## How It Works

1. Worker accumulates deserialized messages in a buffer
2. On flush, creates a `ConsumeContext<TValue>` from the first message
3. Attaches `BatchFeature<TValue>` containing ALL messages to `Features`
4. Runs through the EXISTING pipeline (same types, same middleware)
5. Consumer checks `TryGetBatch` to access batch data

### Pipeline Compatibility

**Perfect**: The pipeline type is `IMiddlewarePipeline<ConsumeContext<TValue>>` — unchanged. The `ConsumeContext<TValue>` is a real context with a real message. The batch is just extra data in the features bag.

### Offset Management

Same as other approaches:
- Enqueue all offsets before pipeline invocation
- Mark all on success, none on failure
- Worker controls this outside the pipeline

## Pros

- **Zero pipeline changes**: Existing middleware works completely unchanged
- **Zero new interfaces**: Uses existing `IConsumer<TValue>`
- **Backward compatible**: Consumers that don't check for batches work fine (process just the first message? NO — see cons)
- **Features collection already exists**: No new infrastructure needed
- Uses existing registration APIs

## Cons

- **context.Message is misleading**: In batch mode, `context.Message` is the first item's message. Users must remember to check `TryGetBatch` or they'll process only one message.
- **No compile-time safety**: Nothing tells the user at compile time that their consumer will receive batches. They must know to check `TryGetBatch`.
- **Dual code path in consumer**: Every consumer needs `if (batch) { ... } else { ... }`. This is error-prone.
- **Silent data loss risk**: If the user writes `await Process(context.Message)` without checking for batch, they process only the first message of every batch. All other messages in the batch are silently lost (offsets committed but not processed).
- **Validation/retry semantics unclear**: Middleware validates `context.Message` (first item only), not the whole batch.
- **context.TransportContext metadata**: Partition, offset, key are for the first message only.

## Verdict

**Elegant internally but dangerous for users.** The silent data loss risk (processing only the first message when batch mode is enabled) is a deal-breaker. The API violates the principle of least surprise — `context.Message` should always be the message you need to process, not sometimes one message and sometimes a representative of many.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes `context.Message` being "the first item" is an acceptable degradation. In reality, middleware like `ValidationMiddleware` calls validators against `context.Message` — with batch mode on, it would validate only the first item, letting invalid messages in the remaining N-1 items through unchecked.
- Assumes users will reliably remember to call `TryGetBatch`. This is the same "pit of failure" that plagues nullable references and `TryGetValue` patterns — developers forget, especially when refactoring or onboarding.

### Risks Not Discussed
- **Testing complexity**: Unit tests that mock `ConsumeContext<T>` work fine in single mode but silently break in batch mode. No compile-time signal tells test authors to also test the batch path.
- **Middleware evolution**: Any future middleware that inspects `context.Message` will silently misbehave for batches. This creates an ongoing maintenance burden — every middleware author must remember to check for batches.
- **Observability confusion**: Tracing and metrics will report per-batch metrics under a single-message identity. Dashboard cardinality will be wrong — one "message processed" event actually represents N messages.

### Verdict Justification
The verdict ("dangerous for users") is **well-justified and possibly understated**. The silent data loss risk is not a theoretical concern — it's the DEFAULT behavior. A user who doesn't know about `TryGetBatch` will process one message per batch and commit offsets for all of them. This is worse than a crash; it's silent, undetectable data loss at scale.

### Blind Spots
- Doesn't discuss the `Features` bag's performance characteristics. Feature lookups are dictionary-based — fine for one lookup, but if multiple middleware layers start checking for batch presence, the per-message overhead adds up.
- Doesn't mention that this pattern makes `IBatchConsumer<T>` impossible to add later without a breaking change, since users are already writing `IConsumer<T>` with conditional batch logic.

### Strongest Argument
The zero-infrastructure-change claim is genuinely strong — nothing new needs to be added to the pipeline, DI, or middleware. But this strength is also the weakness: the reason nothing changes is that the framework doesn't actually *understand* batches. It's the user's problem to handle the duality, and that's a design smell.

**Overall: 3/10** — Correctly identified as a dangerous anti-pattern. The silent data loss scenario alone should disqualify this approach.
