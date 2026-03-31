# Approach 6: `BatchConsumeContext<T>` Inherits from `ConsumeContext<IReadOnlyList<T>>`

## Concept

Make the batch context fit into the existing pipeline by making `TValue = IReadOnlyList<T>`. The batch context IS a `ConsumeContext<IReadOnlyList<OrderCreated>>`, so the entire middleware pipeline (typed on `ConsumeContext<TValue>`) works with `TValue = IReadOnlyList<OrderCreated>`.

## User Experience

```csharp
public class OrderBatchConsumer : IConsumer<IReadOnlyList<OrderCreated>>
{
    public async Task ConsumeAsync(
        ConsumeContext<IReadOnlyList<OrderCreated>> context,
        CancellationToken ct)
    {
        foreach (var order in context.Message)
        {
            await db.UpsertAsync(order, ct);
        }
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
    c.AddConsumer<OrderBatchConsumer>();
});
```

## How It Works

1. Topic is declared as `Topic<string, OrderCreated>` — individual messages are `OrderCreated`
2. Worker deserializes each message as `OrderCreated`
3. When batch mode enabled, worker accumulates `List<OrderCreated>`
4. Worker creates `ConsumeContext<IReadOnlyList<OrderCreated>>` where `Message = accumulated list`
5. Pipeline processes `ConsumeContext<IReadOnlyList<OrderCreated>>` — all middleware works

### The Type Registration Problem

The topic declares `TValue = OrderCreated`, so deserializers produce `OrderCreated`. But the consumer pipeline needs `TValue = IReadOnlyList<OrderCreated>`. The same dual-type problem from Approach 2.

The registration would need to build two different pipeline types:
- Deserialization pipeline: works with `OrderCreated`
- Consumer pipeline: works with `IReadOnlyList<OrderCreated>`

### What About Per-Item Metadata?

With `ConsumeContext<IReadOnlyList<OrderCreated>>`, the user loses access to per-item Kafka metadata (partition, offset, key, headers). The `TransportContext` on the `ConsumeContext` can only represent ONE transport location.

Options:
- Use the first message's transport context (confusing)
- Create a synthetic transport context with batch metadata
- Provide per-item metadata through a separate mechanism (Features collection)

## Pros

- Uses existing `IConsumer<T>` interface — no new interface
- Pipeline middleware works unchanged (it's just `ConsumeContext<IReadOnlyList<T>>`)
- No new context types needed
- Simple conceptual model: "batch = list of messages"

## Cons

- **Loss of per-item metadata**: Users can't access individual partition/offset/key/headers
- **Misleading API**: `IConsumer<IReadOnlyList<OrderCreated>>` doesn't communicate batch semantics clearly. It looks like the topic produces lists.
- **Type registration gymnastics**: Same dual-type issue as Approach 2
- **Validation**: `IMessageValidator<IReadOnlyList<OrderCreated>>` is awkward
- **Dead letter**: What does the `TransportContext.RawValue` contain for a batch?
- **No type safety at registration**: Nothing prevents `AddConsumer<IConsumer<IReadOnlyList<T>>>` without batch config, which would fail at runtime

## Verdict

**Too lossy.** Dropping per-item metadata makes this unsuitable for any use case where the consumer needs to know which partition/offset/key a message came from. The API is also misleading — it looks like the topic produces lists, not individual messages.

---

## Decision Critic Assessment

### Verdict Stress Test: STRONGLY JUSTIFIED
The loss of per-item metadata is a genuine deal-breaker. Any batch consumer that needs to log, route, or audit individual messages needs partition/offset/key access. Stripping this metadata eliminates critical use cases.

### Hidden Assumption: "Users always need per-item metadata"
The document assumes per-item metadata is essential. For some use cases (bulk database inserts, aggregate calculations), the consumer only needs the message payloads and doesn't care about partition/offset/key. For these cases, `IReadOnlyList<T>` is actually the perfect API. However, designing for the lowest common denominator (no metadata) when the cost of providing metadata is low (use `BatchItem<T>`) is the wrong trade-off.

### Counter-Argument: Simplicity Has Value
`IConsumer<IReadOnlyList<OrderCreated>>` is the simplest possible batch API. No new types, no new interfaces, no wrappers. For teams that value extreme simplicity, this has appeal. But simplicity that loses information is a false economy — the user pays later when they need metadata and can't access it.

### Blind Spot: Validation Actually Works
The document says `IMessageValidator<IReadOnlyList<OrderCreated>>` is awkward, but a batch validator that validates each item in the list is perfectly natural. The awkwardness is in the type name, not the semantics.

### Strongest Argument AGAINST: "Misleading API"
The API says the topic produces lists. It doesn't. The topic produces individual messages that are accumulated into lists by the framework. This semantic mismatch between the topic declaration (`Topic<string, OrderCreated>`) and the consumer (`IConsumer<IReadOnlyList<OrderCreated>>`) creates confusion. Correctly identified as disqualifying.
