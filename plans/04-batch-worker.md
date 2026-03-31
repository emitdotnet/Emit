# Approach 4: Dedicated `BatchConsumerWorker<TKey, TValue>`

## Concept

Create a parallel worker class that accumulates messages before dispatching. The poll loop detects batch mode and routes to a batch-aware worker instead of the single-message `ConsumerWorker`.

## User Experience

```csharp
public class OrderBatchConsumer : IBatchConsumer<OrderCreated>
{
    public async Task ConsumeAsync(
        BatchConsumeContext<OrderCreated> context,
        CancellationToken ct)
    {
        foreach (var item in context.Items)
        {
            await db.UpsertOrderAsync(item.Message, ct);
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
    c.OnError(e => e.DeadLetter());
});
```

## Internal Architecture

### New: `BatchConsumerWorker<TKey, TValue>`

A new worker class parallel to `ConsumerWorker<TKey, TValue>`:

```csharp
internal sealed class BatchConsumerWorker<TKey, TValue>
{
    private readonly Channel<ConsumeResult<byte[], byte[]>> channel;
    private readonly int maxBatchSize;
    private readonly TimeSpan batchTimeout;
    private readonly IReadOnlyList<BatchConsumerPipelineEntry<TValue>> batchPipelines;

    public async Task RunAsync(CancellationToken ct)
    {
        var reader = channel.Reader;
        while (await reader.WaitToReadAsync(ct))
        {
            var batch = await AccumulateBatchAsync(reader, ct);
            await DispatchBatchAsync(batch, ct);
        }
    }

    private async Task<List<(ConsumeResult<byte[], byte[]> Raw, DeserializedMessage<TKey, TValue> Deserialized)>>
        AccumulateBatchAsync(ChannelReader<ConsumeResult<byte[], byte[]>> reader, CancellationToken ct)
    {
        var batch = new List<...>();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(batchTimeout);

        while (batch.Count < maxBatchSize)
        {
            if (reader.TryRead(out var raw))
            {
                var deserialized = await DeserializeAsync(raw);
                batch.Add((raw, deserialized));
            }
            else if (batch.Count > 0)
            {
                // Have some messages, wait for more or timeout
                try
                {
                    await reader.WaitToReadAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    break; // Timeout reached, flush what we have
                }
            }
            else
            {
                // No messages yet, wait indefinitely
                await reader.WaitToReadAsync(ct);
            }
        }

        return batch;
    }

    private async Task DispatchBatchAsync(
        List<(ConsumeResult Raw, DeserializedMessage Deserialized)> batch,
        CancellationToken ct)
    {
        // Enqueue all offsets
        foreach (var (raw, _) in batch)
            offsetManager.Enqueue(raw.Topic, raw.Partition.Value, raw.Offset.Value);

        try
        {
            // Build batch context and fan out to batch consumer pipelines
            foreach (var entry in batchPipelines)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var batchContext = BuildBatchConsumeContext(batch, scope, ct);
                await entry.Pipeline.InvokeAsync(batchContext);
            }

            // Success: mark all offsets
            foreach (var (raw, _) in batch)
                offsetManager.MarkAsProcessed(raw.Topic, raw.Partition.Value, raw.Offset.Value);
        }
        catch
        {
            // Failure: don't mark offsets, they'll be redelivered
            // Note: offsets were enqueued but never marked processed,
            // so watermark stays at pre-batch position
            throw; // Let supervisor detect worker fault and restart
        }
    }
}
```

### Worker Pool Supervisor Changes

`WorkerPoolSupervisor` creates either `ConsumerWorker` or `BatchConsumerWorker` based on registration config.

### Pipeline for Batches

Three options for connecting the accumulated batch to middleware:

**Option A: Separate pipeline typed on `BatchConsumeContext<TValue>`**
- `ConsumeErrorMiddleware<BatchConsumeContext<TValue>>`, `ConsumeTracingMiddleware<BatchConsumeContext<TValue>>`, etc.
- `BatchHandlerInvoker<TValue>` — resolves `IBatchConsumer<TValue>` and calls it
- Requires making all middleware generic on context type, not just `ConsumeContext<TValue>` — significant change.

**Option B: Skip middleware for batches entirely**
- Worker dispatches directly to `IBatchConsumer<TValue>`, handling error policy, metrics, and tracing inline
- Simpler but loses composability; replicates cross-cutting logic that belongs in middleware.

**Option C: Existing `Compose` with `TValue = MessageBatch<T>` (preferred)**
- Call the existing `ConsumerPipelineComposer.Compose` method with `TValue = MessageBatch<T>`
- Produces a fully functional `IMiddlewarePipeline<ConsumeContext<MessageBatch<TValue>>>` — no new middleware types required
- All existing middleware implementations (`ConsumeErrorMiddleware<T>`, `RetryMiddleware<T>`, `ConsumeTracingMiddleware<T>`, etc.) are reused unchanged; they are simply instantiated with a different `T`
- A new `BatchHandlerInvoker<TValue>` terminal is the only genuinely new middleware component
- The worker builds a `ConsumeContext<MessageBatch<TValue>>` from the accumulated batch and dispatches it through this pipeline
- This is two generic instantiations of the same composition function, not two separate pipelines

## Pros

- Clean separation: batch workers are a distinct code path, no pollution of single-message worker
- Full control over batch accumulation, timeout, and offset management
- No middleware contract violations — the batch worker owns the lifecycle
- Timeout-based flushing is clean (timer in the accumulation loop, not in middleware)

## Cons

- **Significant code duplication**: `BatchConsumerWorker` duplicates most of `ConsumerWorker` (deserialization, DI scoping, error handling, observability, offset management)
- **Pipeline wiring requires care**: Option A (separate context type) duplicates middleware; Option B loses composability. Option C (existing `Compose` with `MessageBatch<TValue>`) resolves both — middleware implementations are fully reused, only the type argument changes.
- **Cannot mix single and batch consumers**: A consumer group is either batch or single-message
- **New worker class, new pipeline entries, new registration logic** — large surface area change
- **Supervisor and pool management** become more complex (two worker types)

## Verdict

**Mechanically sound; pipeline incompatibility is resolved by Option C.** The approach works well for offset management and accumulation. Using the existing `Compose` method with `TValue = MessageBatch<T>` removes the pipeline incompatibility entirely — no middleware duplication, no skipping the pipeline. The real costs are the dedicated worker class and the accumulation loop complexity (timeout, shutdown, rebalance edge cases).

---

## Decision Critic Assessment

### Verdict Stress Test: VERDICT NOW CORRECTED
The original verdict said "pipeline incompatibility remains" — this is wrong once you allow the existing `Compose` method to be called with `TValue = MessageBatch<T>`. That path was overlooked. The approach works mechanically, reuses all middleware, and requires only a new `BatchHandlerInvoker<TValue>` terminal. The remaining real cost is worker duplication (deserialization, DI scoping, offset management), which shared helpers or a common base address — exactly the mitigation Approach 12 applies.

### Hidden Assumption: "Worker duplication is inherently bad"
The real cost of duplication isn't the initial code — it's that bug fixes and feature additions must be applied twice. But the shared parts (deserialization, DI scoping, offset management) could be extracted into a base class or shared methods. The document doesn't explore this mitigation, making the duplication seem worse than necessary.

### Risk: Two Worker Types Complicates Supervisor
This is correctly identified but underweighted. `WorkerPoolSupervisor` manages worker lifecycle, fault detection, and restart. Adding a second worker type means the supervisor must handle both, or two supervisors must coexist. The pool's health monitoring (wait for first faulted task) must work with either worker type. This is non-trivial integration work.

### Overlooked Alternative: Single Worker, Conditional Inner Loop
The document proposes a separate `BatchConsumerWorker` class, but a simpler approach is an `if` branch in the existing worker's `RunAsync` that delegates to either `RunSingleMessageLoopAsync` or `RunBatchLoopAsync`. This avoids the supervisor complexity entirely. (This is what Approach 10 and 12 propose — the document should have converged earlier.)

### Strongest Argument: Clean Offset Semantics
The worker owns the full batch lifecycle: accumulate → enqueue offsets → dispatch → mark (or not). No contract violations, no middleware hacks. This is the correct level of abstraction for batching.
