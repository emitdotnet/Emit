# Approach 3: Batching as a Middleware (KafkaFlow-Style)

## Inspiration

KafkaFlow treats batching as a middleware concern. Messages flow individually through the pipeline until they hit a batching middleware that accumulates them and dispatches the batch downstream.

## User Experience

```csharp
public class OrderBatchConsumer : IBatchConsumer<OrderCreated>
{
    public async Task ConsumeAsync(
        IReadOnlyList<ConsumeContext<OrderCreated>> batch,
        CancellationToken ct)
    {
        foreach (var context in batch)
        {
            await ProcessAsync(context.Message, ct);
        }
    }
}
```

**Registration:**
```csharp
.ConsumerGroup("orders-group", c =>
{
    c.AddBatchConsumer<OrderBatchConsumer>(batch =>
    {
        batch.MaxSize = 100;
        batch.Timeout = TimeSpan.FromSeconds(5);
    });
    c.OnError(e => e.DeadLetter());
});
```

## How It Works

### Batching Middleware Position in Pipeline

```
Error → Observer → Tracing → Metrics → Global/Provider/Group MW
    → [BatchAccumulatorMiddleware]  ← NEW: accumulates, then dispatches batch
        → Validation → Retry → Per-Entry → Handler
```

The `BatchAccumulatorMiddleware<TValue>` sits in the pipeline and:
1. Receives individual `ConsumeContext<TValue>` calls
2. Buffers them internally
3. When batch triggers (size or timeout), invokes the downstream pipeline once with a special batch dispatch

### Internal Design

```csharp
internal class BatchAccumulatorMiddleware<TValue> : IMiddleware<ConsumeContext<TValue>>
{
    private readonly List<ConsumeContext<TValue>> buffer = [];
    private readonly int maxSize;
    private readonly TimeSpan timeout;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? timerCts;

    public async Task InvokeAsync(
        ConsumeContext<TValue> context,
        IMiddlewarePipeline<ConsumeContext<TValue>> next)
    {
        await gate.WaitAsync(context.CancellationToken);
        try
        {
            buffer.Add(context);
            if (buffer.Count >= maxSize)
            {
                await FlushAsync(next, context.CancellationToken);
            }
            else if (buffer.Count == 1)
            {
                StartTimer(next);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task FlushAsync(
        IMiddlewarePipeline<ConsumeContext<TValue>> next,
        CancellationToken ct)
    {
        var batch = buffer.ToList();
        buffer.Clear();
        timerCts?.Cancel();

        // Problem: next.InvokeAsync expects a SINGLE ConsumeContext<TValue>
        // We need to pass the batch somehow...
        // Option: store batch in Features collection of the first context
        batch[0].SetPayload(new BatchPayload<TValue>(batch));
        await next.InvokeAsync(batch[0]);
    }
}
```

### The Fundamental Problem

The middleware pipeline is `IMiddlewarePipeline<ConsumeContext<TValue>>` — it processes **one context at a time**. A batching middleware needs to:
1. Accept N calls to `InvokeAsync` (one per message)
2. NOT call `next` immediately
3. After accumulating N messages, call `next` ONCE with all N messages

But the pipeline contract expects `InvokeAsync` to be called and eventually call `next`. If we don't call `next` for individual messages, the caller (ConsumerWorker.FanOutAsync) will think processing succeeded and mark the offset.

**This breaks the offset contract**: The worker calls `entry.Pipeline.InvokeAsync(context)` and then marks the offset as processed. If the middleware buffers the context without calling `next`, the worker marks the offset done even though the message hasn't been processed yet.

### Workarounds

**Workaround A: Don't mark offset until batch flushes**
- The middleware controls when offsets are marked
- Problem: The offset marking happens in `ConsumerWorker.RunAsync`, outside the pipeline. The middleware can't control it.

**Workaround B: Batch at the worker level, dispatch through a batch-typed pipeline**
- Worker accumulates messages and dispatches a batch through a pipeline instantiated on `ConsumeContext<MessageBatch<TValue>>` (or a `BatchConsumeContext<TValue>`)
- A new `BatchHandlerInvoker<TValue>` terminal middleware resolves the batch consumer at the end of that pipeline
- This IS adding middleware to the existing pipeline — specifically, a new terminal middleware in a new generic instantiation of the same composition infrastructure
- The accumulation logic moves to the worker, which is the correct level of abstraction (see Approach 4), but the cross-cutting concerns (error handling, tracing, metrics) remain in the middleware pipeline unchanged

**Workaround C: Make the middleware stateful + use a completion signal**
- Middleware returns a `Task` that completes only when the batch flushes
- Each message's pipeline call "awaits" the batch completion
- Problem: This blocks N worker slots while accumulating, defeating the purpose of bounded channels.

## Pros

- Batching is a composable, reusable middleware concern
- KafkaFlow-proven pattern
- Middleware can be placed at different pipeline levels (per-group, per-consumer)

## Cons

- **Fundamental pipeline contract violation**: The `IMiddleware<TContext>` contract is invoke-one-at-a-time. Accumulation breaks the "each InvokeAsync eventually calls next" expectation.
- **Offset management breaks**: Worker marks offset after pipeline returns. Middleware buffering means pipeline returns before message is actually processed.
- **Timer complexity**: Background timer for timeout-based flushing introduces thread safety issues and partial-batch edge cases during shutdown/rebalance.
- **Pipeline type mismatch**: Downstream middleware still expects single `ConsumeContext<TValue>`. Need a way to pass batch data through a single-message pipeline.
- **Statefulness**: Middleware in Emit is typically stateless per message. A batching middleware is inherently stateful across messages, which conflicts with the DI scoping model (new scope per message).
- **Back-pressure interaction**: Rate limiting and circuit breaker operate per-message. Their semantics change when messages are batched.

## Verdict

**Inline accumulation middleware is architecturally incompatible, but the worker-accumulation path is not.** The KafkaFlow-style inline accumulation approach (a middleware that buffers N calls to `InvokeAsync` before calling `next` once) violates Emit's pipeline contract and offset semantics — this rejection stands. However, Workaround B was incorrectly dismissed as "not really a middleware anymore." Worker-level accumulation paired with a batch-typed pipeline and a new `BatchHandlerInvoker<TValue>` terminal IS a middleware solution — the accumulation simply happens at the correct layer (the worker), while the cross-cutting concerns remain in middleware. This path merges naturally with Approaches 2 and 4.

---

## Decision Critic Assessment

### Verdict Stress Test: JUSTIFIED FOR INLINE ACCUMULATION; WORKAROUND B INCORRECTLY DISMISSED
The rejection of KafkaFlow-style inline accumulation middleware is strongly justified — the pipeline contract violation is real and fundamental. However, the original verdict painted all batching-as-middleware solutions with the same brush. Workaround B (worker accumulation + batch-typed pipeline with `BatchHandlerInvoker` terminal) was incorrectly labeled as "not really a middleware anymore." It is a middleware solution; the accumulation is simply at the right layer. This distinction matters for evaluating Approaches 2 and 4.

### Counter-Evidence from Industry: KafkaFlow DOES This
KafkaFlow implements exactly this approach and ships it in production. The document correctly identifies WHY KafkaFlow can do it (different offset semantics via `ShouldStoreOffset` and `AutoMessageCompletion`), but understates that this is a **design choice** in Emit, not an immutable law. If Emit added an `IOffsetControl` interface to the pipeline context, this approach becomes viable. The rejection is valid given current architecture but should acknowledge it's a constraint that COULD be relaxed.

### Hidden Assumption: "Middleware is stateless per message"
This is presented as a fact, but it's actually a convention. `RateLimitMiddleware` is already stateful (holds a `RateLimiter` singleton). `CircuitBreakerObserver` is stateful across messages. The real issue isn't statefulness — it's that the middleware's return signals "processing complete" to the worker. A stateful middleware that returns before processing is complete is the actual contract violation.

### Workaround C Dismissed Too Quickly
"Blocking N worker slots while accumulating" is dismissed as defeating the purpose. But if `WorkerCount = 1` (common for ordered consumption), there's only one slot to block. For single-worker groups, Workaround C actually works correctly — the worker blocks on the first message, accumulates the rest from the channel while blocked, and flushes. This niche viability should be noted.

### Strongest Argument: Offset Contract is Non-Negotiable
The document's strongest point: "Worker marks offset after pipeline returns. Middleware buffering means pipeline returns before message is actually processed." This is irrefutable given the current architecture.
