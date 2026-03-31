# Approach 2: `MessageBatch<T>` as the Message Type (MassTransit Literal)

## Inspiration

MassTransit's literal approach: `IConsumer<Batch<OrderCreated>>`. The batch IS the message type `T` in `IConsumer<T>`.

## User Experience

```csharp
// The user implements IConsumer<MessageBatch<T>> — the EXISTING consumer interface
public class OrderBatchConsumer : IConsumer<MessageBatch<OrderCreated>>
{
    public async Task ConsumeAsync(
        ConsumeContext<MessageBatch<OrderCreated>> context,
        CancellationToken ct)
    {
        foreach (var item in context.Message)
        {
            // item is BatchItem<OrderCreated> with Message, Offset, Partition, Key, Headers
            await ProcessOrderAsync(item.Message, ct);
        }
    }
}
```

**Registration:**
```csharp
.ConsumerGroup("orders-group", c =>
{
    c.AddConsumer<OrderBatchConsumer>(h =>
    {
        // Batch config on the consumer handler builder
    });
    c.Batch(batch =>
    {
        batch.MaxSize = 100;
        batch.Timeout = TimeSpan.FromSeconds(5);
    });
});
```

## New Types in `Emit.Abstractions`

```csharp
/// <summary>
/// A batch of messages accumulated from the transport layer. Implements
/// IReadOnlyList for easy iteration and LINQ support.
/// </summary>
public sealed class MessageBatch<T> : IReadOnlyList<BatchItem<T>>
{
    private readonly IReadOnlyList<BatchItem<T>> items;

    internal MessageBatch(IReadOnlyList<BatchItem<T>> items) => this.items = items;

    public int Count => items.Count;
    public BatchItem<T> this[int index] => items[index];
    public IEnumerator<BatchItem<T>> GetEnumerator() => items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class BatchItem<T>
{
    public required T Message { get; init; }
    public required string Topic { get; init; }
    public required int Partition { get; init; }
    public required long Offset { get; init; }
    public required IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}
```

## How It Works

The key insight: `MessageBatch<OrderCreated>` is just another `TValue`. The existing pipeline processes `ConsumeContext<MessageBatch<OrderCreated>>` through the same middleware chain.

### Worker Changes

When the registration detects that ANY consumer type implements `IConsumer<MessageBatch<T>>`:

1. Worker accumulates deserialized messages in a buffer
2. On flush (max size or timeout), constructs `MessageBatch<TValue>` from buffered items
3. Creates a single `ConsumeContext<MessageBatch<TValue>>` with the batch as the message
4. Runs through the EXISTING middleware pipeline (typed on `ConsumeContext<MessageBatch<TValue>>`)

### Pipeline Compatibility

**This is the critical advantage**: Because `MessageBatch<T>` is just the `TValue`, the entire middleware pipeline works unchanged:

- `ConsumeErrorMiddleware<MessageBatch<TValue>>` — catches batch handler errors, applies policy
- `RetryMiddleware<MessageBatch<TValue>>` — retries the entire batch on failure
- `ConsumeMetricsMiddleware<MessageBatch<TValue>>` — records batch processing metrics
- `ConsumeTracingMiddleware<MessageBatch<TValue>>` — creates a span for the batch
- Validation, filters, etc. — all work on `ConsumeContext<MessageBatch<TValue>>`

### Offset Handling

- Enqueue ALL offsets in the batch before pipeline invocation
- On pipeline success: mark ALL as processed
- On pipeline failure (after retry exhaustion): mark NONE → watermark stays

### Registration Detection

At registration time in `KafkaBuilder.RegisterConsumerGroup`, detect if any consumer type's generic argument is `MessageBatch<X>`:

```csharp
// When building the registration, check if batch consumers exist
var hasBatchConsumers = groupBuilder.ConsumerTypes.Any(t =>
    t.GetInterfaces().Any(i =>
        i.IsGenericType &&
        i.GetGenericTypeDefinition() == typeof(IConsumer<>) &&
        i.GenericTypeArguments[0].IsGenericType &&
        i.GenericTypeArguments[0].GetGenericTypeDefinition() == typeof(MessageBatch<>)));
```

### Topic Type Mismatch Problem

**Critical issue**: The topic is declared as `Topic<TKey, TValue>` where `TValue = OrderCreated`. But the consumer receives `MessageBatch<OrderCreated>`. This creates a type mismatch:

- Deserializer is `IDeserializer<OrderCreated>` — deserializes individual messages
- Pipeline is built for `ConsumeContext<MessageBatch<OrderCreated>>` — different `TValue`

**Solution**: The worker deserializes each message individually using `IDeserializer<TValue>`, then wraps them into `MessageBatch<TValue>`. The pipeline is built with a different `TValue` type than the topic declaration. This requires special handling in the registration — effectively the consumer group has TWO value types: the wire type (`TValue`) and the consumer type (`MessageBatch<TValue>`).

This is handled naturally by calling `Compose` (or `ConsumerPipelineComposer`) twice with different type arguments:
- Non-batch consumers: `Compose<TValue>` — pipeline typed on `ConsumeContext<TValue>` (existing)
- Batch consumers: `Compose<MessageBatch<TValue>>` — same composition function, different type argument

This is not a "two code paths" fork or code duplication. It is a second generic instantiation of the same composition function, which is exactly what generics are for. The middleware implementations (`ConsumeErrorMiddleware<T>`, `RetryMiddleware<T>`, etc.) are shared; only the type argument differs. The only genuinely new code is the `BatchHandlerInvoker<TValue>` terminal.

## Pros

- Uses the EXISTING `IConsumer<T>` interface — no new consumer interface needed
- The full middleware pipeline works because `MessageBatch<T>` is just another message type
- Retry middleware works naturally: retry the whole batch
- Error middleware works naturally: dead-letter the whole batch
- Tracing creates a single span per batch (correct semantics)
- Familiar pattern for MassTransit users

## Cons

- **Two pipeline instantiations per group**: The topic declares `TValue`, but batch consumers need `MessageBatch<TValue>`. Two generic instantiations of the same pipeline composition exist per group — this is type-safe and reuses all middleware implementations, but registration must wire them correctly.
- **Fan-out ambiguity**: If a group has both `IConsumer<OrderCreated>` and `IConsumer<MessageBatch<OrderCreated>>`, what happens? Both batch and single consumers need messages, but the accumulation model is fundamentally different for each.
- **Validation on MessageBatch<T>**: `IMessageValidator<MessageBatch<OrderCreated>>` is awkward — you'd validate a batch, not individual messages.
- **Dead letter**: When a batch is dead-lettered, what goes to the DLQ? The whole batch as one message? Each individual message? The `TransportContext.RawValue` would need to be all raw values concatenated or the first one.
- **ConsumeContext.Message**: Now `context.Message` is a `MessageBatch<OrderCreated>` instead of `OrderCreated` — requires the user to understand the wrapper.
- Registration type detection via reflection is fragile.

## Verdict

**Elegant middleware reuse with manageable registration work.** Two generic instantiations of the same `Compose` function is not code duplication — it is how generics work. The real complexity is (1) wiring the correct pipeline instantiation to batch vs. single consumers at registration, and (2) the fan-out ambiguity when a group has both consumer types. The "dual-type system" framing overstated the cost; the middleware layer is fully shared.

---

## Decision Critic Assessment

### Verdict Stress Test: VERDICT NOW CORRECTED
The original verdict's "dual-type system is complex" framing was misleading. Two generic instantiations of the same `Compose` function is not a dual system — it is standard generic programming. The registration work is incremental. The real costs (fan-out ambiguity, validation semantics, dead-letter granularity) are correctly identified in the Cons list and remain valid.

### Hidden Assumption: "Reflection-based detection is fragile"
The claim that checking `IConsumer<MessageBatch<T>>` via reflection is fragile assumes runtime detection. But this detection happens at **registration time** (app startup), not at runtime. If it fails, the app fails to start with a clear error. This is not fragile — it's fail-fast. The real issue is that the user registers `AddConsumer<MyBatchConsumer>()` without calling `Batch()`, or vice versa — a validation gap, not a reflection fragility.

### Overlooked Strength: This IS Approach 14 Without the Adapter
Approach 2 and Approach 14 are the same core idea. Approach 14 just adds `IBatchConsumer<T>` as sugar over `IConsumer<MessageBatch<T>>`. The document should have identified this earlier — it would have saved 12 approaches of exploration.

### Blind Spot: Dead Letter Granularity
The dead-letter problem is correctly identified but the solution (IBatchMessage marker interface) is not explored here. This omission makes the approach look worse than it is, since the solution discovered in Approach 13-14 applies equally here.

### Risk: Fan-out Ambiguity is a Real Killer
The document correctly flags that mixing `IConsumer<T>` and `IConsumer<MessageBatch<T>>` creates ambiguity. This is the real problem — not the type gymnastics. Enforcing "batch groups can't have single consumers" solves it, but the document doesn't propose this constraint explicitly.
