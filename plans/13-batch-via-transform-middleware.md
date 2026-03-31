# Approach 13: Batch via Transform — Worker Produces `ConsumeContext<MessageBatch<T>>`

## Concept

A refinement of Approach 2 (Batch as Message Type), solving the dual-type registration problem. The topic declares `TValue`, but when batch mode is enabled, the worker internally produces `ConsumeContext<MessageBatch<TValue>>` and feeds it into a pipeline built for that type. The key insight: the **deserialization type** and the **pipeline type** are decoupled — the worker bridges them.

## User Experience

```csharp
// User implements the standard IConsumer<T> with MessageBatch<T> as the message type
public class OrderBatchConsumer : IConsumer<MessageBatch<OrderCreated>>
{
    public async Task ConsumeAsync(
        ConsumeContext<MessageBatch<OrderCreated>> context,
        CancellationToken ct)
    {
        foreach (var item in context.Message)
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
    group.AddConsumer<OrderBatchConsumer>(); // AddConsumer, NOT AddBatchConsumer
});
```

## How It Works

1. Topic declared as `Topic<string, OrderCreated>` — deserializer is for `OrderCreated`
2. Worker deserializes each message as `OrderCreated` (using existing deserializer)
3. When batch configured, worker wraps accumulated messages into `MessageBatch<OrderCreated>`
4. Worker creates `ConsumeContext<MessageBatch<OrderCreated>>` and invokes pipeline

### Type Detection at Registration

At registration, check if any consumer's `TValue` in `IConsumer<TValue>` is `MessageBatch<X>`:

```csharp
static bool IsMessageBatchConsumer(Type consumerType, Type topicValueType)
{
    var iface = consumerType.GetInterfaces()
        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>));
    if (iface is null) return false;
    var valueType = iface.GenericTypeArguments[0];
    return valueType.IsGenericType
        && valueType.GetGenericTypeDefinition() == typeof(MessageBatch<>)
        && valueType.GenericTypeArguments[0] == topicValueType;
}
```

### Pipeline Construction

The registration builds TWO pipeline types:
- `IMiddlewarePipeline<ConsumeContext<OrderCreated>>` for single consumers
- `IMiddlewarePipeline<ConsumeContext<MessageBatch<OrderCreated>>>` for batch consumers

The second pipeline uses the SAME middleware infrastructure:
- `ConsumeErrorMiddleware<MessageBatch<OrderCreated>>`
- `RetryMiddleware<MessageBatch<OrderCreated>>`
- `HandlerInvoker<MessageBatch<OrderCreated>>` → resolves `IConsumer<MessageBatch<OrderCreated>>`

All existing middleware is open-generic on `TMessage` — so `ConsumeErrorMiddleware<TMessage>` works with ANY `TMessage`, including `MessageBatch<OrderCreated>`.

### The `MessageBatch<T>` Type

```csharp
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
    public required TransportContext TransportContext { get; init; }
}
```

## Key Advantage Over Approach 12

**Zero middleware duplication.** Because `MessageBatch<T>` is just another `TMessage`, ALL existing middleware works unchanged:
- `ConsumeErrorMiddleware<MessageBatch<OrderCreated>>` — ✅ works
- `RetryMiddleware<MessageBatch<OrderCreated>>` — ✅ works (retries the batch)
- `ConsumeTracingMiddleware<MessageBatch<OrderCreated>>` — ✅ works
- `ConsumeMetricsMiddleware<MessageBatch<OrderCreated>>` — ✅ works
- `ValidationMiddleware<MessageBatch<OrderCreated>>` — ✅ works (if user provides validator)
- `HandlerInvoker<MessageBatch<OrderCreated>>` — ✅ works (resolves `IConsumer<MessageBatch<OrderCreated>>`)

No forked pipeline composition. No batch middleware shells that must track their single-message counterparts.

## Key Disadvantage vs Approach 12

**Dead-lettering granularity**: When `ConsumeErrorMiddleware<MessageBatch<OrderCreated>>` dead-letters, it sends `TransportContext.RawValue` to the DLQ. But for a batch, `TransportContext.RawValue` would be... what? The first message's raw bytes? All messages concatenated? This is where the single-message DLQ contract breaks.

**Possible solutions**:
- Dead-letter each item individually: One option is a `BatchDeadLetterMiddleware` that plugs into the pipeline for `ConsumeContext<MessageBatch<T>>`. Alternatively, use the `IBatchMessage` marker interface to extend the existing error middleware so it iterates items when the message is a batch.
- Dead-letter the batch as a single DLQ entry: Loses per-item identity. Not ideal.
- Don't dead-letter batches (discard only): Too restrictive.
- Add a `BatchDeadLetterMiddleware` that plugs into the existing pipeline typed on `ConsumeContext<MessageBatch<T>>`. This is a new middleware class, but it is not a forked pipeline shell — it runs in the same pipeline composition as all other middleware.
- Use the `IBatchMessage` marker interface to extend the existing error middleware with a per-item iteration path. This is a small change to an existing file, not a new parallel structure.

### Dead-Letter Solution: Feature-Based Batch Detection in Error Middleware

Add a small check in `ConsumeErrorMiddleware`:
```csharp
// In ExecuteDeadLetterAsync:
if (context is ConsumeContext<MessageBatch<TMessage>> batchContext)  // Wait, this doesn't compile —
// TMessage IS MessageBatch<X>, so context.Message is MessageBatch<X>
```

Actually, `ConsumeErrorMiddleware<TMessage>` where `TMessage = MessageBatch<OrderCreated>`. So `context.Message` is `MessageBatch<OrderCreated>`. But the middleware doesn't know `TMessage` is a `MessageBatch<X>` — it just sees `TMessage`.

**Solution**: `MessageBatch<T>` implements a marker interface `IBatchMessage` with a method to enumerate raw transport contexts:

```csharp
internal interface IBatchMessage
{
    IEnumerable<TransportContext> GetItemTransportContexts();
}

public sealed class MessageBatch<T> : IReadOnlyList<BatchItem<T>>, IBatchMessage
{
    IEnumerable<TransportContext> IBatchMessage.GetItemTransportContexts()
        => items.Select(i => i.TransportContext);
}
```

Then in `ConsumeErrorMiddleware.ExecuteDeadLetterAsync`:
```csharp
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
    await deadLetterSink.ProduceAsync(
        context.TransportContext.RawKey, context.TransportContext.RawValue, headers, ct);
}
```

This is a TINY change to existing middleware — just a type check and iteration.

## Pros

- **Zero forked pipeline composition**: No `ComposeBatch` method, no parallel composer entry point. All existing middleware works via generics on `MessageBatch<T>` through the same `Compose` path.
- **Zero structural duplication**: No batch middleware shells that must track their single-message counterparts.
- **Uses existing IConsumer<T> interface**: No `IBatchConsumer<T>` needed
- **Existing pipeline infrastructure unchanged**: Same `ConsumerPipelineComposer`, same `HandlerInvoker`
- **Minimal registration changes**: Just detect `MessageBatch<T>` and build the appropriate pipeline
- **Dead-letter per item**: Via `IBatchMessage` marker interface, existing error middleware iterates items
- **Familiar pattern**: Users who know MassTransit recognize `IConsumer<Batch<T>>`

## Cons

- **User registers `IConsumer<MessageBatch<OrderCreated>>`**: The type name is longer and arguably less clear than `IBatchConsumer<OrderCreated>`
- **Reflection-based detection at registration**: Fragile type inspection to determine if consumers expect batches
- **Dual pipeline types in one group**: Registration must build both `ConsumeContext<OrderCreated>` and `ConsumeContext<MessageBatch<OrderCreated>>` pipelines, which adds complexity
- **Mixing semantics are undefined**: Having both `IConsumer<OrderCreated>` and `IConsumer<MessageBatch<OrderCreated>>` in the same group is ambiguous — does the single consumer see every message AND the batch consumer see batches? This is a design question, not a constraint. Mixing could be explicitly allowed or disallowed depending on what semantics are chosen.
- **`IBatchMessage` is a leaky abstraction**: Error middleware needs to know about batch structure, breaking the "middleware doesn't know about batch" promise
- **TransportContext for batch**: The batch's `ConsumeContext.TransportContext` is synthetic — it doesn't represent a real Kafka message

## Verdict

**Zero forked pipeline composition at the cost of API clarity.** This approach achieves the goal of no structural duplication — the same pipeline composer and middleware run for both single and batch consumers. The trade-off is API clarity: `IConsumer<MessageBatch<T>>` is less discoverable than `IBatchConsumer<T>`, and the `IBatchMessage` marker interface adds a subtle coupling to error middleware. Approach 14 directly builds on this approach's core insight while adding a clean adapter layer to recover the API clarity.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes reflection-based type detection (`IsMessageBatchConsumer`) at registration time is reliable. In practice, generic type inspection via reflection is fragile — it can break with nested generics, interface covariance, or proxy types from DI containers.
- Assumes `IBatchMessage` marker interface is a "tiny" change to existing middleware. While the diff is small, it introduces a conceptual dependency: the error middleware now knows about batch structure. If other middleware layers need similar batch awareness (metrics counting per-item, tracing per-item), the marker pattern proliferates.

### Risks Not Discussed
- **Dual pipeline confusion**: Having both `IMiddlewarePipeline<ConsumeContext<OrderCreated>>` and `IMiddlewarePipeline<ConsumeContext<MessageBatch<OrderCreated>>>` for the same topic is confusing. Which pipeline runs for which message? The worker must know, but the registration must be clear.
- **Mixing semantics**: Having both `IConsumer<OrderCreated>` and `IConsumer<MessageBatch<OrderCreated>>` in the same group raises a design question. The document identifies this but doesn't propose a resolution. This is not a hard constraint — mixing could be explicitly allowed or disallowed at registration time as a deliberate design choice.
- **`AddConsumer` overloading**: Using the same `AddConsumer<T>()` method for both single and batch consumers (detected via reflection) violates the principle of explicit registration. The user's intent isn't clear from the registration code.

### Verdict Justification
The verdict ("minimal code duplication at the cost of API clarity") is **accurate and fair**. The approach achieves the goal of zero middleware duplication, which is significant. But the cost — reflection-based detection, implicit registration semantics, and the `IBatchMessage` marker — is real. The document honestly names these trade-offs.

### Blind Spots
- Doesn't discuss the discoverability problem. A new developer seeing `IConsumer<MessageBatch<OrderCreated>>` in the codebase won't immediately understand this is a batch consumer without reading documentation. `IBatchConsumer<OrderCreated>` is self-documenting.
- Doesn't mention that `MessageBatch<T>` being public and implementing `IReadOnlyList<BatchItem<T>>` means users might try to create instances manually (e.g., in tests). If the constructor is internal, testing becomes harder.

### Strongest Argument
The zero-middleware-duplication insight is the breakthrough contribution of this approach. Recognizing that `MessageBatch<T>` is "just another `TMessage`" that flows through generics is the key architectural insight. Approach 14 directly builds on this insight while adding the clean API layer.

**Overall: 7/10** — The architectural insight (batch-as-message-type through generics, enabling zero forked pipeline composition) is the most important contribution in the entire plan set. Loses points for API clarity and reflection fragility, but the core idea is what makes Approach 14 possible. Approach 14 takes this insight and adds the clean adapter layer on top.
