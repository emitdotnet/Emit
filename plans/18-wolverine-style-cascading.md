# Approach 18: Wolverine-Style — Cascading Handlers with Batch Saga

## Inspiration

Wolverine's message handling model where handlers can return messages that trigger subsequent handlers. A "batch coordinator" accumulates messages and then triggers the real handler.

## Concept

Instead of batching at the transport level, use a saga/coordinator pattern:
1. Individual messages arrive normally through the pipeline
2. A `BatchCoordinator<T>` middleware accumulates them in memory
3. When the batch is ready, the coordinator dispatches to the batch handler

## How It Works

```csharp
// Middleware accumulates messages
internal sealed class BatchCoordinatorMiddleware<TValue> : IMiddleware<ConsumeContext<TValue>>
{
    private readonly ConcurrentDictionary<string, BatchState<TValue>> batches = new();

    public async Task InvokeAsync(ConsumeContext<TValue> context, IMiddlewarePipeline<ConsumeContext<TValue>> next)
    {
        var batchKey = GetBatchKey(context); // e.g., by consumer group
        var state = batches.GetOrAdd(batchKey, _ => new BatchState<TValue>(maxSize, timeout));

        state.Add(context);

        if (state.IsReady)
        {
            var batch = state.Flush();
            // Dispatch batch to IBatchConsumer<TValue>
            var batchConsumer = context.Services.GetRequiredService<IBatchConsumer<TValue>>();
            await batchConsumer.ConsumeAsync(new BatchConsumeContext<TValue> { Items = batch }, context.CancellationToken);
        }
        // else: message was buffered, don't call next
    }
}
```

## Analysis

### Same Core Problem as Approach 3: Not Calling `next`

> **Note**: The problems below apply specifically to the design shown above, where the middleware does not call `next`. This is not a blanket rejection of middleware-based batching solutions. A correctly-designed middleware that calls `next` for every message — and uses a separate dispatch mechanism only to trigger the batch consumer when the batch is ready — would avoid the pipeline contract violations. The problems here are about this specific design pattern, not about middleware as an implementation strategy.

1. **Pipeline contract violation**: Not calling `next` means the worker thinks processing is complete and marks the offset. But the message hasn't been processed yet — it's just buffered.
2. **Statefulness across messages**: The coordinator maintains state across invocations, but each invocation gets a different DI scope. The coordinator can't be scoped.
3. **Timer management**: The timeout for "max time before flush" requires a background timer, which doesn't fit the synchronous middleware invoke model.
4. **Partial batch at shutdown**: What happens to accumulated messages when the consumer shuts down? They're lost — offsets already committed but processing never completed.
5. **Thread safety**: Multiple workers might hit the same coordinator concurrently, requiring synchronization.

### Saga Variant

Use a proper saga (persistent state) to accumulate:
1. Each message updates a saga state in the database
2. When the saga detects enough messages, it triggers the batch handler
3. The saga commits its state transactionally with the batch processing

**Problems**:
- Requires a saga/workflow engine (Emit doesn't have one)
- Database round-trip per message defeats the purpose of batching
- Enormously complex for what should be a simple batching feature

## Verdict

**Wrong design, for this specific pattern.** The `BatchCoordinatorMiddleware` shown here doesn't call `next`, which breaks the pipeline contract. Using a saga or coordinator pattern to accumulate messages across invocations adds enormous complexity and has the offset-safety problems described above.

> **Design position, not a given**: The framing "batching is a transport-level concern, not an application-level concern" reflects an architectural position, not a user-imposed constraint. A middleware that correctly calls `next` on every message and accumulates batch state via a separate mechanism could potentially work without the violations described here. Whether that design is worth pursuing is a separate question — but it has not been foreclosed.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes that batching must be handled at exactly one level (transport or application). In reality, hybrid approaches (transport-level accumulation with application-level dispatch) exist and work well — Approach 12/14 are examples.
- Assumes a saga/workflow engine would be necessary. A simpler in-memory coordinator without persistence could work, but would still have the pipeline contract violations identified.

### Risks Not Discussed
- **Coordinator memory leaks**: If the coordinator accumulates messages but the batch never triggers (e.g., the timer breaks or the batch size is never reached), messages accumulate in memory indefinitely. Without a circuit breaker on the coordinator, this becomes a memory leak.
- **Testing complexity**: A saga-based approach requires testing state transitions, timeouts, and recovery — each of which is a test category that doesn't exist in Emit today.

### Verdict Justification
The verdict (wrong design for this specific pattern) is **correct**. The specific `BatchCoordinatorMiddleware` shown fails because it doesn't call `next`, breaking offset safety and pipeline semantics. However, the earlier framing that "middleware-level batching fails" as a category is too broad — the failure is specific to designs that skip `next`. A middleware solution that calls `next` on every message and accumulates batch state externally would not have these problems. The document's analysis of HOW the pipeline contract is violated remains the useful contribution here.

### Blind Spots
- Doesn't acknowledge that Wolverine's actual batching implementation (`BatchMessagesOf<T>`) works at the transport level using `BatchingChannel<Envelope>`, not as a saga. The document conflates the saga inspiration with the actual implementation.
- Doesn't discuss that the "coordinator across multiple handler invocations" pattern DOES work for in-memory aggregation in event sourcing contexts. It's wrong for Emit's use case but not universally wrong.

### Strongest Argument
The distinction between "transport-level concern" and "application-level concern" is the document's key contribution. This principle cleanly explains why Approaches 3, 7, 9, and 18 fail while Approaches 10, 12, 13, and 14 succeed.

**Overall: 2/10** — Correctly rejected. The approach is architecturally misguided, but the "abstraction level" analysis is valuable as a design principle.
