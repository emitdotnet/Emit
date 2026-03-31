# Approach 5: Polymorphic ConsumerWorker with Strategy Pattern

## Concept

Instead of a separate batch worker class, make `ConsumerWorker<TKey, TValue>` polymorphic via a **dispatch strategy** that controls whether messages are processed individually or accumulated into batches. The worker's core loop (read from channel, deserialize, manage offsets) stays the same, but the dispatch behavior is pluggable.

## User Experience

Same as Approach 1:
```csharp
public class OrderBatchConsumer : IBatchConsumer<OrderCreated>
{
    public async Task ConsumeAsync(
        BatchConsumeContext<OrderCreated> context,
        CancellationToken ct)
    {
        foreach (var item in context.Items)
        {
            await ProcessAsync(item.Message, ct);
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
    c.AddBatchConsumer<OrderBatchConsumer>();
});
```

## Internal Architecture

### Dispatch Strategy Interface

```csharp
internal interface IDispatchStrategy<TKey, TValue>
{
    Task DispatchAsync(
        ChannelReader<ConsumeResult<byte[], byte[]>> reader,
        Func<ConsumeResult<byte[], byte[]>, Task<DeserializedMessage<TKey, TValue>>> deserialize,
        OffsetManager offsetManager,
        CancellationToken ct);
}
```

### Single-Message Strategy (Current Behavior)

```csharp
internal sealed class SingleMessageDispatch<TKey, TValue> : IDispatchStrategy<TKey, TValue>
{
    public async Task DispatchAsync(...)
    {
        while (reader.TryRead(out var raw))
        {
            var deserialized = await deserialize(raw);
            await FanOutAsync(raw, deserialized, ct);
            offsetManager.MarkAsProcessed(...);
        }
    }
}
```

### Batch Dispatch Strategy

```csharp
internal sealed class BatchDispatch<TKey, TValue> : IDispatchStrategy<TKey, TValue>
{
    private readonly int maxBatchSize;
    private readonly TimeSpan batchTimeout;

    public async Task DispatchAsync(...)
    {
        var batch = AccumulateBatch(reader, ct);
        EnqueueAllOffsets(batch, offsetManager);
        try
        {
            await DispatchBatchAsync(batch, ct);
            MarkAllProcessed(batch, offsetManager);
        }
        catch { /* don't mark offsets */ throw; }
    }
}
```

### ConsumerWorker Changes

```csharp
// Before:
public async Task RunAsync(CancellationToken ct)
{
    while (await reader.WaitToReadAsync(ct))
    {
        while (reader.TryRead(out var raw))
        {
            // deserialize, fan out, mark offset
        }
    }
}

// After:
public async Task RunAsync(CancellationToken ct)
{
    await dispatchStrategy.DispatchAsync(channel.Reader, DeserializeAsync, offsetManager, ct);
}
```

## The Same Pipeline Problem

This is cleaner than Approach 4 structurally, but the batch dispatch strategy still needs to invoke a pipeline for the batch. The fundamental pipeline typing issue is the same:

- Existing pipeline: `IMiddlewarePipeline<ConsumeContext<TValue>>`
- Batch needs: `IMiddlewarePipeline<ConsumeContext<MessageBatch<TValue>>>` or `IMiddlewarePipeline<BatchConsumeContext<TValue>>`

The strategy pattern helps with worker-level concerns (accumulation, offset management) but doesn't solve the middleware type problem.

## Pros

- Worker code stays mostly unified — strategy pattern avoids duplicating the entire worker
- Clean separation of single vs batch dispatch logic
- Offset management is clean (strategy controls when to mark)
- Testable: strategies can be unit-tested independently

## Cons

- Still has the pipeline typing problem
- Strategy interface is wide (needs access to many worker internals: deserialization, fan-out, offset management, DI scoping)
- The strategy essentially IS the worker body — the "strategy" is doing all the interesting work, and the worker shell becomes a thin wrapper
- Doesn't solve the fan-out question (can you mix single and batch consumers?)
- The `IDispatchStrategy` interface is hard to get right — too narrow and it can't do the job, too wide and it's just another worker class

## Verdict

**Better structure than Approach 4, same core problems.** The strategy pattern is a good refactoring of the worker code, but doesn't address the fundamental pipeline typing challenge. This could be combined with another approach that solves the pipeline problem.

---

## Decision Critic Assessment

### Verdict Stress Test: JUSTIFIED
The analysis correctly identifies that the strategy pattern moves code around without solving the pipeline typing problem. The approach is a structural improvement over Approach 4 but not a conceptual advance.

### Hidden Assumption: "The strategy interface would be too wide"
The document claims the strategy "essentially IS the worker body." This is true, which means the abstraction adds indirection without value. However, if the strategy only controlled **accumulation** (not dispatch), the interface would be narrow and useful: `Task<List<ConsumeResult>> AccumulateAsync(ChannelReader, CancellationToken)`. The dispatch logic (fan-out, offset management) stays in the worker. This narrower formulation isn't explored.

### Composability Note: A Valid Worker-Tier Component
The strategy pattern is an implementation technique for the worker tier, not a complete architectural solution. That does not make it invalid — it makes it composable. Any approach that modifies the worker (10, 11, 12, 14) could adopt this structure: accumulation and offset management live in the strategy, while the pipeline-level solution is handled separately. The two concerns are orthogonal. Dismissing the pattern outright understates its value as a clean, testable component.

### Risk: Over-Abstraction if Applied Naively
Introducing a strategy interface specifically for two fixed variants (single vs batch) adds indirection. A conditional branch in the worker is simpler if no further dispatch modes are anticipated. The pattern earns its keep if accumulation logic grows complex enough to warrant independent testing — which batch windowing and timeout logic likely does.

### Strongest Argument: Testability
The one real advantage: strategies can be unit-tested independently of the worker. But the worker's `RunBatchLoopAsync` can also be tested via the worker's public `RunAsync` method with appropriate channel setup, so the testability gain is marginal.
