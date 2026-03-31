# Approach 10: Consumer Mode Enum — Single Worker with Conditional Accumulation

## Concept

Extend `ConsumerWorker<TKey, TValue>` with a `ConsumerMode` (Single or Batch). When in Batch mode, the worker's inner loop switches from process-immediately to accumulate-then-dispatch. The pipeline type problem is solved by making batches a top-level concept in the pipeline composer.

This is the most pragmatic approach that addresses the pipeline problem head-on.

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
            await db.UpsertAsync(item.Message, ct);
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

### New Types

```csharp
// Emit.Abstractions
public interface IBatchConsumer<TValue>
{
    Task ConsumeAsync(BatchConsumeContext<TValue> context, CancellationToken ct);
}

public class BatchConsumeContext<T> : MessageContext
{
    public required IReadOnlyList<BatchItem<T>> Items { get; init; }
    public required TransportContext TransportContext { get; init; } // synthetic batch transport context
}

public sealed class BatchItem<T>
{
    public required T Message { get; init; }
    public required TransportContext TransportContext { get; init; }
}
```

### Pipeline Composer Handles Batch Consumers

`ConsumerPipelineComposer<TValue>` gains a new `ComposeBatch` method that builds a pipeline for `BatchConsumeContext<TValue>`. **Note**: this `ComposeBatch` fork is the primary maintainability problem in this approach. It must be kept in sync with `Compose` for every future cross-cutting concern — new middleware layers, ordering changes, error policy additions. The shell middleware classes themselves are acceptable; the separate composer fork is what creates the ongoing tax.

```csharp
public ConsumerPipelineEntry<BatchConsumeContext<TValue>> ComposeBatch(
    IHandlerInvoker<BatchConsumeContext<TValue>> terminal,
    string identifier,
    Type consumerType)
{
    // Build pipeline: error → observer → tracing → metrics → handler
    // Skip: validation (batch-level validation is a future concern)
    // Skip: retry (batch retry = re-accumulate, handled by worker re-trying the whole batch)
    // Same layering structure, just typed on BatchConsumeContext<TValue>
}
```

**Key insight**: The cross-cutting middleware (error, tracing, metrics, observers) can be made generic across context types. Only the typed middleware (validation, handler invocation) needs specific context types.

### Making Cross-Cutting Middleware Work for Both Contexts

The middleware hierarchy:
- `ConsumeErrorMiddleware<TMessage>` wraps `ConsumeContext<TMessage>` — but `BatchConsumeContext<T>` is NOT a `ConsumeContext<T>`

**Solution**: Make error/tracing/metrics middleware work on `MessageContext` (the base), not `ConsumeContext<T>`:

Actually, this won't work because `IMiddleware<TContext>` requires `TContext : MessageContext`. The pipeline is typed — you can't mix `IMiddleware<ConsumeContext<T>>` and `IMiddleware<BatchConsumeContext<T>>` in the same chain.

**Better solution**: Build a SEPARATE pipeline chain for batch consumers using new instances of the same middleware types, but parameterized for `BatchConsumeContext<T>`.

Create batch-specific variants:
```csharp
internal sealed class BatchErrorMiddleware<TValue> : IMiddleware<BatchConsumeContext<TValue>>
{
    // Same logic as ConsumeErrorMiddleware, but typed on BatchConsumeContext
    // Both share a common helper for DLQ routing
}
```

This IS code duplication, but it's duplication of the thin middleware shell, not the business logic. The DLQ routing, metric recording, and tracing logic are extracted into shared helpers.

### Worker Changes

`ConsumerWorker` gains batch accumulation in its `RunAsync`:

```csharp
public async Task RunAsync(CancellationToken ct)
{
    if (registration.BatchConfig is not null)
        await RunBatchLoopAsync(ct);
    else
        await RunSingleMessageLoopAsync(ct); // existing code
}

private async Task RunBatchLoopAsync(CancellationToken ct)
{
    var reader = channel.Reader;
    while (await reader.WaitToReadAsync(ct))
    {
        var batch = await AccumulateBatchAsync(reader, ct);
        if (batch.Count == 0) continue;

        // Enqueue all offsets
        foreach (var (raw, _) in batch)
            offsetManager.Enqueue(raw.Topic, raw.Partition.Value, raw.Offset.Value);

        try
        {
            await FanOutBatchAsync(batch, ct);

            // Success: mark all processed
            foreach (var (raw, _) in batch)
                offsetManager.MarkAsProcessed(raw.Topic, raw.Partition.Value, raw.Offset.Value);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // Batch failed entirely — don't mark offsets
            // Error middleware in the batch pipeline already handled the exception
            // (dead-letter or discard the batch)
            logger.LogError(ex, "Batch processing failed");
        }
    }
}
```

### ConsumerGroupRegistration Changes

```csharp
internal sealed class ConsumerGroupRegistration<TKey, TValue>
{
    // Existing fields...

    // New batch fields
    public BatchConfig? BatchConfig { get; init; }
    public Func<IReadOnlyList<ConsumerPipelineEntry<BatchConsumeContext<TValue>>>>? BuildBatchConsumerPipelines { get; init; }
}

internal sealed class BatchConfig
{
    public required int MaxSize { get; init; }
    public required TimeSpan Timeout { get; init; }
}
```

### KafkaConsumerGroupBuilder Changes

```csharp
public sealed class KafkaConsumerGroupBuilder<TKey, TValue>
{
    // Existing...

    private readonly List<Type> batchConsumerTypes = [];
    private BatchConfig? batchConfig;

    public KafkaConsumerGroupBuilder<TKey, TValue> Batch(Action<BatchConfigBuilder> configure)
    {
        var builder = new BatchConfigBuilder();
        configure(builder);
        batchConfig = builder.Build();
        return this;
    }

    public void AddBatchConsumer<TConsumer>() where TConsumer : class, IBatchConsumer<TValue>
    {
        batchConsumerTypes.Add(typeof(TConsumer));
    }
}
```

### Validation: Cannot Mix Single + Batch

At registration time, validate that a consumer group has EITHER single consumers OR batch consumers, not both:
```csharp
if (groupBuilder.ConsumerTypes.Count > 0 && groupBuilder.BatchConsumerTypes.Count > 0)
    throw new InvalidOperationException("Cannot mix single and batch consumers in the same group.");
```

**Note**: This restriction is a design choice, not a hard architectural requirement. If mixing single and batch consumers in the same group works reliably — delivering each message type to the correct consumer with correct offset semantics — the validation guard can be removed. The restriction exists to avoid ambiguity, not because the implementation cannot support it.

## Pros

- **Clear user API**: `IBatchConsumer<T>` is explicit about batch semantics
- **Clean offset management**: Worker controls all-or-nothing commit
- **Validation at registration**: Cannot mix single/batch consumers — avoids ambiguity
- **Middleware reuse**: Cross-cutting concerns share helper logic, even if the middleware shell is duplicated
- **No pipeline contract violation**: Batch pipeline is a separate pipeline with the correct type
- **Worker code changes are minimal**: Conditional branch in `RunAsync`

## Cons

- **Middleware duplication**: Need batch-specific middleware shells for error, tracing, metrics, observers. About 4-5 thin wrappers.
- **Pipeline composer changes**: Need a `ComposeBatch` method alongside `Compose`
- **Two registration code paths**: `RegisterConsumerGroup` needs logic for both batch and single consumers
- **Cannot have both single and batch consumers in the same group**: Users must choose one mode
- **Retry semantics**: Retry for batches means retrying the entire batch — configurable via error policy but different from per-message retry
- **New types**: `IBatchConsumer<T>`, `BatchConsumeContext<T>`, `BatchItem<T>`, `BatchConfig`

## Verdict

**Practical and honest.** Acknowledges the pipeline type problem and solves it with a small amount of duplication. The middleware shell duplication is manageable (thin wrappers delegating to shared helpers). The user API is clear and safe. The worker changes are contained. This is a solid foundation.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes that cross-cutting middleware (error, tracing, metrics, observers) MUST be duplicated as separate batch-typed classes. In reality, Approaches 13/14 prove this assumption false by using `MessageBatch<T>` as the `TMessage` parameter, getting all middleware for free via generics.
- Assumes shared helper extraction is sufficient to avoid duplication. While helpers reduce logic duplication, the shell middleware classes still need to be maintained, tested, and kept in sync with their single-message counterparts.

### Risks Not Discussed
- **Shell drift**: Over time, the single-message middleware evolves (new error policies, new metrics dimensions, new tracing attributes). Each change must be manually replicated in the batch shell middleware. Without compile-time enforcement, these shells will drift.
- **ComposeBatch is the root cause**: The `ComposeBatch` fork is not a minor inconvenience — it is a permanent parallel maintenance obligation. Every addition to `Compose` must be consciously replicated in `ComposeBatch`. Shell classes that are forgotten here cause batch consumers to silently miss cross-cutting behavior: no tracing, no metrics, no error handling for batches. The shell middleware classes individually are an acceptable cost; the forked composer compounds that cost indefinitely.
- **Testing surface**: Each batch middleware shell needs its own unit tests, roughly mirroring the single-message middleware tests. This doubles the test maintenance burden for cross-cutting concerns.

### Verdict Justification
The verdict ("practical and honest") is **accurate but generous**. The approach works and is straightforward, but it accepts duplication that Approaches 13/14 demonstrate is avoidable. The document deserves credit for honestly naming the trade-offs rather than pretending the duplication doesn't exist.

### Blind Spots
- Doesn't quantify the maintenance cost. "4-5 thin wrappers" sounds small, but each wrapper must track changes in its single-message counterpart, creating an ongoing tax.
- Doesn't discuss what happens if user-registered group-level middleware (`group.AddMiddleware<T>()`) needs to work for both single and batch consumers. The current design silently ignores user middleware for batches.

### Strongest Argument
The explicit nature of this approach is its real strength. Every component is visible, testable, and debuggable with no hidden adapters or marker interfaces. For teams that value "no magic," this is compelling. The worker changes are also well-designed — the conditional branch in `RunAsync` is clean.

**Overall: 6/10** — A solid, workable approach that's been superseded by the insight in Approach 13/14. Good as a fallback if the adapter pattern introduces unforeseen issues.
