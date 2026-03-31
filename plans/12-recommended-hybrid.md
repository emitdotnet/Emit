# Approach 12: Hybrid Batch Worker with Dedicated Pipeline

## Summary

This approach synthesizes ideas from all previous approaches:
- **Approach 10's clean API**: `IBatchConsumer<TValue>`, `BatchConsumeContext<TValue>`, explicit registration
- **Approach 11's channel draining**: Natural batching under load, no complex timer
- **Approach 11 + timeout**: Timer as fallback for the "max wait time" requirement
- **Shared middleware helpers**: Cross-cutting middleware logic extracted to reusable helpers; thin batch-specific shells avoid massive duplication
- **Worker conditional loop**: Single `ConsumerWorker<TKey, TValue>` with batch-aware inner loop

## User Experience

```csharp
// The user implements IBatchConsumer<T>
public class OrderBatchConsumer : IBatchConsumer<OrderCreated>
{
    private readonly IOrderRepository repository;

    public OrderBatchConsumer(IOrderRepository repository) => this.repository = repository;

    public async Task ConsumeAsync(BatchConsumeContext<OrderCreated> context, CancellationToken ct)
    {
        // Bulk insert — efficient because all messages are available at once
        var orders = context.Items.Select(item => item.Message).ToList();
        await repository.BulkInsertAsync(orders, ct);

        // Per-item metadata available if needed:
        foreach (var item in context.Items)
        {
            logger.LogDebug("Processed order from partition {P} offset {O}",
                item.TransportContext.Partition, item.TransportContext.Offset);
        }

        // If this throws → NO offsets committed → entire batch redelivered
        // Consumer must be idempotent for the whole batch
    }
}
```

**Registration:**
```csharp
services.AddEmit(emit =>
{
    emit.AddKafka(kafka =>
    {
        kafka.ConfigureClient(c => c.BootstrapServers = "localhost:9092");
        kafka.DeadLetter("dead-letters");

        kafka.Topic<string, OrderCreated>("orders", topic =>
        {
            topic.SetUtf8KeyDeserializer();
            topic.SetJsonValueDeserializer();

            topic.ConsumerGroup("orders-processor", group =>
            {
                group.Batch(batch =>
                {
                    batch.MaxSize = 100;            // Flush at 100 messages
                    batch.MaxTimeout = TimeSpan.FromSeconds(5); // Or after 5 seconds
                });

                group.AddBatchConsumer<OrderBatchConsumer>();

                group.OnError(e => e.Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(1)))
                                     .Then.DeadLetter());
            });
        });
    });
});
```

**Error policy on batch**: Retry retries the ENTIRE batch. After exhaustion, dead-letter produces ONE DLQ entry per batch item (so each original message is individually dead-lettered with its own partition/offset metadata).

## New Types

### `Emit.Abstractions`

```csharp
/// <summary>
/// Handles consumed messages in batches. Implementations must be idempotent
/// for the entire batch — if any item fails, the whole batch will be redelivered.
/// </summary>
public interface IBatchConsumer<TValue>
{
    Task ConsumeAsync(BatchConsumeContext<TValue> context, CancellationToken cancellationToken);
}

/// <summary>
/// Context for batch consumption. Contains all accumulated messages and
/// provides batch-level identity, scoping, and cancellation.
/// </summary>
public class BatchConsumeContext<T> : MessageContext
{
    /// <summary>Items in this batch, each with their own transport metadata.</summary>
    public required IReadOnlyList<BatchItem<T>> Items { get; init; }

    /// <summary>Synthetic transport context representing the batch as a whole.</summary>
    public required TransportContext TransportContext { get; init; }

    /// <summary>Current retry attempt (set by RetryMiddleware).</summary>
    public int RetryAttempt { get; set; }
}

/// <summary>
/// A single item within a batch, carrying the deserialized message
/// and its original transport metadata.
/// </summary>
public sealed class BatchItem<T>
{
    /// <summary>The deserialized message.</summary>
    public required T Message { get; init; }

    /// <summary>The original transport context for this message.</summary>
    public required TransportContext TransportContext { get; init; }

    /// <summary>Convenience: the Kafka partition this message came from.</summary>
    public int Partition => ((KafkaTransportContext)TransportContext).Partition;

    /// <summary>Convenience: the Kafka offset of this message.</summary>
    public long Offset => ((KafkaTransportContext)TransportContext).Offset;
}
```

### `Emit.Kafka.DependencyInjection`

```csharp
/// <summary>
/// Configures batch accumulation for a consumer group.
/// </summary>
public sealed class BatchConfigBuilder
{
    /// <summary>Maximum messages per batch. Required.</summary>
    public int MaxSize { get; set; } = 100;

    /// <summary>Maximum time to wait before flushing a partial batch.</summary>
    public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromSeconds(5);

    internal BatchConfig Build() => new()
    {
        MaxSize = MaxSize,
        MaxTimeout = MaxTimeout,
    };
}
```

## Internal Changes (Exhaustive)

### 1. `KafkaConsumerGroupBuilder<TKey, TValue>` — Batch Registration

Add batch consumer registration alongside existing single consumer registration:

```csharp
// New fields
private readonly List<Type> batchConsumerTypes = [];
private readonly HashSet<Type> registeredBatchConsumerTypes = [];
private readonly Dictionary<Type, IMessagePipelineBuilder> batchConsumerPipelines = new();
private Action<BatchConfigBuilder>? batchConfigAction;

// New properties
internal IReadOnlyList<Type> BatchConsumerTypes => batchConsumerTypes;
internal IReadOnlyDictionary<Type, IMessagePipelineBuilder> BatchConsumerPipelines => batchConsumerPipelines;
internal Action<BatchConfigBuilder>? BatchConfigAction => batchConfigAction;

// New methods
public KafkaConsumerGroupBuilder<TKey, TValue> Batch(Action<BatchConfigBuilder> configure)
{
    ArgumentNullException.ThrowIfNull(configure);
    if (batchConfigAction is not null)
        throw new InvalidOperationException("Batch has already been configured.");
    batchConfigAction = configure;
    return this;
}

public void AddBatchConsumer<TConsumer>() where TConsumer : class, IBatchConsumer<TValue>
{
    var type = typeof(TConsumer);
    if (!registeredBatchConsumerTypes.Add(type))
        throw new InvalidOperationException($"Batch consumer '{type.Name}' already registered.");
    batchConsumerTypes.Add(type);
}

public void AddBatchConsumer<TConsumer>(Action<KafkaConsumerHandlerBuilder<TValue>> configure)
    where TConsumer : class, IBatchConsumer<TValue>
{
    // Per-consumer middleware support for batch consumers
    ArgumentNullException.ThrowIfNull(configure);
    var type = typeof(TConsumer);
    if (!registeredBatchConsumerTypes.Add(type))
        throw new InvalidOperationException($"Batch consumer '{type.Name}' already registered.");
    var handlerBuilder = new KafkaConsumerHandlerBuilder<TValue>();
    configure(handlerBuilder);
    batchConsumerTypes.Add(type);
    if (handlerBuilder.Pipeline.Descriptors.Count > 0)
        batchConsumerPipelines[type] = handlerBuilder.Pipeline;
}
```

**Validation at registration**:
```csharp
// In KafkaBuilder.RegisterConsumerGroup:
// NOTE: This is a design choice, not a hard platform constraint. Mixing could be
// supported if the semantics were defined (e.g., single consumers get every message
// AND batch consumers get batches). This validation enforces a strict separation for now.
if (groupBuilder.ConsumerTypes.Count > 0 && groupBuilder.BatchConsumerTypes.Count > 0)
    throw new InvalidOperationException(
        "Consumer group cannot have both single and batch consumers. Use separate consumer groups.");

if (groupBuilder.BatchConsumerTypes.Count > 0 && groupBuilder.BatchConfigAction is null)
    throw new InvalidOperationException(
        "Batch consumers require batch configuration. Call Batch() on the consumer group builder.");
```

### 2. `ConsumerGroupRegistration<TKey, TValue>` — Batch Config

```csharp
// New properties
public BatchConfig? BatchConfig { get; init; }
public Func<IReadOnlyList<ConsumerPipelineEntry<BatchConsumeContext<TValue>>>>? BuildBatchConsumerPipelines { get; init; }
```

### 3. `ConsumerPipelineComposer<TValue>` — `ComposeBatch` Method

> **Architectural note**: `ComposeBatch` is a forked pipeline composition — a parallel entry point that mirrors `Compose` for a different context type. This is the primary duplication concern with this approach. Any change to the middleware ordering in `Compose` (new rate-limiting middleware, new observability layer, etc.) must be manually replicated in `ComposeBatch`. There is no compile-time enforcement of this synchronization.

New method that builds a pipeline for `BatchConsumeContext<TValue>`. The cross-cutting middleware (error, tracing, metrics, observers) is implemented as thin wrappers that delegate to shared helper classes:

```csharp
public ConsumerPipelineEntry<BatchConsumeContext<TValue>> ComposeBatch(
    IMiddlewarePipeline<BatchConsumeContext<TValue>> terminal,
    IMessagePipelineBuilder? perEntryPipeline,
    string identifier,
    Type consumerType)
{
    // 1. Terminal (batch handler invoker) — already provided

    // 2. RetryMiddleware<BatchConsumeContext<TValue>> — retries the batch handler
    if (RetryConfig is not null)
    {
        var retryMw = new BatchRetryMiddleware<TValue>(RetryConfig, ...);
        terminal = new MiddlewarePipeline<BatchConsumeContext<TValue>>(retryMw, terminal);
    }

    // 3-5. Skip validation, group/provider/global user MW for now
    //       (batch-level user MW could be added later)

    // 6. Metrics
    var metricsMw = new BatchMetricsMiddleware<TValue>(..., identifier);
    var pipeline = new MiddlewarePipeline<BatchConsumeContext<TValue>>(metricsMw, terminal);

    // 7. Tracing
    var tracingMw = new BatchTracingMiddleware<TValue>(..., identifier, consumerType);
    pipeline = new MiddlewarePipeline<BatchConsumeContext<TValue>>(tracingMw, pipeline);

    // 8. Observers
    var observerMw = new BatchObserverMiddleware<TValue>(ConsumeObservers, ...);
    pipeline = new MiddlewarePipeline<BatchConsumeContext<TValue>>(observerMw, pipeline);

    // 9. Error handling (outermost)
    var errorMw = new BatchErrorMiddleware<TValue>(..., identifier, consumerType);
    pipeline = new MiddlewarePipeline<BatchConsumeContext<TValue>>(errorMw, pipeline);

    return new ConsumerPipelineEntry<BatchConsumeContext<TValue>>
    {
        Identifier = identifier,
        Kind = ConsumerKind.Direct,
        ConsumerType = consumerType,
        Pipeline = pipeline,
    };
}
```

### 4. Batch Middleware Shells (Thin Wrappers)

> **Structural duplication concern**: Even though each shell delegates to a shared helper, there are 5–6 of them — one for each cross-cutting concern. Each shell must track changes in its single-message counterpart. If `ConsumeErrorMiddleware` gains a new error policy type, `BatchErrorMiddleware` must gain it too. This is ongoing maintenance cost, not just creation cost. Approach 14 avoids this entirely by running the same middleware on `ConsumeContext<MessageBatch<T>>` through generics.

These are NOT full copies of the single-message middleware. They're thin shells delegating to shared logic:

**Example — `BatchErrorMiddleware<TValue>`:**
```csharp
internal sealed class BatchErrorMiddleware<TValue>(
    Func<Exception, ErrorAction>? evaluatePolicy,
    IDeadLetterSink? deadLetterSink,
    EmitMetrics emitMetrics,
    INodeIdentity nodeIdentity,
    ILogger logger,
    string identifier,
    Type? consumerType,
    ICircuitBreakerNotifier? circuitBreakerNotifier)
    : IMiddleware<BatchConsumeContext<TValue>>
{
    public async Task InvokeAsync(
        BatchConsumeContext<TValue> context,
        IMiddlewarePipeline<BatchConsumeContext<TValue>> next)
    {
        try
        {
            await next.InvokeAsync(context).ConfigureAwait(false);
            if (circuitBreakerNotifier is not null)
                await circuitBreakerNotifier.ReportSuccessAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Dead-letter EACH item individually (preserving per-message metadata)
            if (evaluatePolicy?.Invoke(ex) is ErrorAction.DeadLetterAction && deadLetterSink is not null)
            {
                foreach (var item in context.Items)
                {
                    await DeadLetterHelpers.ProduceAsync(
                        deadLetterSink, item.TransportContext, ex,
                        identifier, consumerType, context.RetryAttempt,
                        emitMetrics, nodeIdentity, logger,
                        context.CancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                logger.LogWarning(ex, "Discarding batch of {Count} messages for consumer {Consumer}",
                    context.Items.Count, identifier);
                emitMetrics.RecordErrorAction("discard");
            }

            if (circuitBreakerNotifier is not null)
                await circuitBreakerNotifier.ReportFailureAsync(ex).ConfigureAwait(false);
        }
    }
}
```

**Shared helper extracted from `ConsumeErrorMiddleware`:**
```csharp
internal static class DeadLetterHelpers
{
    public static async Task ProduceAsync(
        IDeadLetterSink sink,
        TransportContext transportContext,
        Exception exception,
        string consumer,
        Type? consumerType,
        int retryAttempt,
        EmitMetrics metrics,
        INodeIdentity nodeIdentity,
        ILogger logger,
        CancellationToken ct)
    {
        // DLQ header construction + produce — shared between single and batch error middleware
        // Extracted from ConsumeErrorMiddleware.ExecuteDeadLetterAsync
    }
}
```

Similarly for tracing, metrics, and observers: extract the core logic into helpers, keep thin middleware shells.

### 5. `ConsumerWorker<TKey, TValue>` — Batch Loop

Add a batch processing path to `RunAsync`:

```csharp
public async Task RunAsync(CancellationToken ct)
{
    var reader = channel.Reader;
    logger.LogDebug("{WorkerId} started, idle", id);

    try
    {
        if (registration.BatchConfig is not null)
            await RunBatchLoopAsync(reader, ct).ConfigureAwait(false);
        else
            await RunSingleMessageLoopAsync(reader, ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Normal shutdown
    }
}

private async Task RunSingleMessageLoopAsync(
    ChannelReader<ConsumeResult<byte[], byte[]>> reader,
    CancellationToken ct)
{
    // Existing RunAsync body (extracted, unchanged)
}

private async Task RunBatchLoopAsync(
    ChannelReader<ConsumeResult<byte[], byte[]>> reader,
    CancellationToken ct)
{
    var config = registration.BatchConfig!;

    while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
    {
        var batch = new List<(ConsumeResult<byte[], byte[]> Raw, DeserializedMessage<TKey, TValue> Deserialized)>();

        // Phase 1: Drain available messages (instant, no waiting)
        while (batch.Count < config.MaxSize && reader.TryRead(out var raw))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var deserialized = await DeserializeAsync(raw).ConfigureAwait(false);
                batch.Add((raw, deserialized));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                await HandleDeserializationErrorAsync(raw, ex, ct).ConfigureAwait(false);
                offsetManager.MarkAsProcessed(raw.Topic, raw.Partition.Value, raw.Offset.Value);
            }
        }

        // Phase 2: If batch isn't full, wait up to timeout for more messages
        if (batch.Count > 0 && batch.Count < config.MaxSize)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(config.MaxTimeout);

            try
            {
                while (batch.Count < config.MaxSize)
                {
                    if (!await reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false))
                        break;

                    while (batch.Count < config.MaxSize && reader.TryRead(out var raw))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var deserialized = await DeserializeAsync(raw).ConfigureAwait(false);
                            batch.Add((raw, deserialized));
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                        catch (Exception ex)
                        {
                            await HandleDeserializationErrorAsync(raw, ex, ct).ConfigureAwait(false);
                            offsetManager.MarkAsProcessed(raw.Topic, raw.Partition.Value, raw.Offset.Value);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout reached — flush what we have
            }
        }

        if (batch.Count == 0) continue;

        // Phase 3: Enqueue all offsets BEFORE dispatch
        foreach (var (raw, _) in batch)
            offsetManager.Enqueue(raw.Topic, raw.Partition.Value, raw.Offset.Value);

        try
        {
            // Phase 4: Dispatch batch through batch consumer pipelines
            await FanOutBatchAsync(batch, ct).ConfigureAwait(false);

            // Phase 5: Success — mark all offsets processed
            foreach (var (raw, _) in batch)
                offsetManager.MarkAsProcessed(raw.Topic, raw.Partition.Value, raw.Offset.Value);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            // Batch failed — offsets NOT marked → watermark stays → redelivery on restart
            logger.LogError(ex, "{WorkerId} batch of {Count} messages failed", id, batch.Count);
        }
    }
}

private async Task FanOutBatchAsync(
    List<(ConsumeResult<byte[], byte[]> Raw, DeserializedMessage<TKey, TValue> Deserialized)> batch,
    CancellationToken ct)
{
    var batchPipelines = registration.BuildBatchConsumerPipelines!();

    foreach (var entry in batchPipelines)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var items = batch.Select(b =>
        {
            var transportContext = new KafkaTransportContext<TKey>
            {
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = b.Deserialized.Timestamp ?? DateTimeOffset.UtcNow,
                CancellationToken = ct,
                Services = scope.ServiceProvider,
                RawKey = b.Raw.Message.Key,
                RawValue = b.Raw.Message.Value,
                Headers = b.Deserialized.Headers,
                ProviderId = "kafka",
                Topic = b.Deserialized.Topic,
                Partition = b.Deserialized.Partition,
                Offset = b.Deserialized.Offset,
                GroupId = groupId,
                Key = b.Deserialized.Key,
                DestinationAddress = destinationAddress,
            };

            return new BatchItem<TValue>
            {
                Message = b.Deserialized.Value,
                TransportContext = transportContext,
            };
        }).ToList();

        // Synthetic batch transport context (uses first item's metadata as representative)
        var firstRaw = batch[0].Raw;
        var batchTransportContext = new KafkaTransportContext<TKey>
        {
            MessageId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            CancellationToken = ct,
            Services = scope.ServiceProvider,
            RawKey = firstRaw.Message.Key,
            RawValue = firstRaw.Message.Value,
            Headers = [],
            ProviderId = "kafka",
            Topic = firstRaw.Topic,
            Partition = firstRaw.Partition.Value,
            Offset = firstRaw.Offset.Value,
            GroupId = groupId,
            Key = batch[0].Deserialized.Key,
            DestinationAddress = destinationAddress,
        };

        var batchContext = new BatchConsumeContext<TValue>
        {
            MessageId = batchTransportContext.MessageId,
            Timestamp = batchTransportContext.Timestamp,
            CancellationToken = ct,
            Services = scope.ServiceProvider,
            Items = items,
            TransportContext = batchTransportContext,
        };

        await entry.Pipeline.InvokeAsync(batchContext).ConfigureAwait(false);
    }
}
```

### 6. `BatchHandlerInvoker<TValue>` — Terminal

```csharp
internal sealed class BatchHandlerInvoker<TValue>(Type consumerType)
    : IHandlerInvoker<BatchConsumeContext<TValue>>
{
    public async Task InvokeAsync(BatchConsumeContext<TValue> context)
    {
        var consumer = (IBatchConsumer<TValue>)context.Services.GetRequiredService(consumerType);
        await consumer.ConsumeAsync(context, context.CancellationToken).ConfigureAwait(false);
    }
}
```

### 7. `KafkaBuilder.RegisterConsumerGroup` — Batch Registration Path

Add batch pipeline building in the registration closure (alongside existing single-consumer logic):

```csharp
// Build batch config
BatchConfig? batchConfig = null;
if (groupBuilder.BatchConfigAction is not null)
{
    var batchConfigBuilder = new BatchConfigBuilder();
    groupBuilder.BatchConfigAction(batchConfigBuilder);
    batchConfig = batchConfigBuilder.Build();
}

// Build batch invoker entries
var batchInvokerEntries = groupBuilder.BatchConsumerTypes
    .Select(t => (
        ConsumerType: t,
        Invoker: (IHandlerInvoker<BatchConsumeContext<TValue>>)new BatchHandlerInvoker<TValue>(t),
        Pipeline: groupBuilder.BatchConsumerPipelines.GetValueOrDefault(t)))
    .ToList();

// In the registration closure:
registration = new ConsumerGroupRegistration<TKey, TValue>
{
    // ... existing fields ...
    BatchConfig = batchConfig,
    BuildBatchConsumerPipelines = batchInvokerEntries.Count > 0 ? () =>
    {
        var entries = new List<ConsumerPipelineEntry<BatchConsumeContext<TValue>>>();
        foreach (var entry in batchInvokerEntries)
        {
            entries.Add(composer.ComposeBatch(
                entry.Invoker, entry.Pipeline,
                entry.ConsumerType.Name, entry.ConsumerType));
        }
        return entries;
    } : null,
};
```

## Offset Semantics — Detailed

### Happy Path
1. Worker accumulates batch of N messages
2. `offsetManager.Enqueue(...)` for all N
3. Pipeline invoked → handler succeeds
4. `offsetManager.MarkAsProcessed(...)` for all N
5. Watermark advances, offsets committed on next timer tick

### Error Path (Handler Throws)
1. Worker accumulates batch of N messages
2. `offsetManager.Enqueue(...)` for all N
3. Pipeline invoked → error middleware catches exception
4. Error middleware: dead-letter each item OR discard
5. Pipeline returns normally (error was handled)
6. Worker calls `MarkAsProcessed(...)` for all N
7. Messages are considered handled (even if dead-lettered)

### Error Path (Handler Throws, No Error Policy)
1. Worker accumulates batch of N messages
2. `offsetManager.Enqueue(...)` for all N
3. Pipeline invoked → error middleware catches exception → discards with warning
4. Pipeline returns normally
5. Worker calls `MarkAsProcessed(...)` for all N
6. Messages discarded (logged as warning)

### Error Path (Retry Exhausted)
1. Worker accumulates batch of N messages
2. `offsetManager.Enqueue(...)` for all N
3. Pipeline invoked → retry middleware retries R times → all fail
4. Retry middleware rethrows → error middleware catches
5. Error middleware: dead-letter each item OR discard
6. Pipeline returns normally
7. Worker calls `MarkAsProcessed(...)` for all N

### Rebalance During Batch Accumulation
1. Worker has accumulated M of N messages
2. Rebalance triggers → `OnPartitionsRevoked` → supervisor stops workers
3. Worker task cancelled → accumulation loop exits
4. The M messages were NOT enqueued to offsetManager (enqueue happens after accumulation)
5. No offsets committed for these messages
6. Redelivered to new consumer

### Rebalance During Batch Processing
1. Worker has dispatched batch of N messages through pipeline
2. Rebalance triggers → CancellationToken cancelled → handler gets OperationCanceledException
3. Worker catches cancellation, returns
4. Offsets were enqueued but not all marked processed → watermark stays
5. Committed offsets on flush are only for fully-processed watermarks
6. Uncommitted messages redelivered to new consumer

## What Needs to Change (File-by-File Summary)

### New Files
1. `src/Emit.Abstractions/IBatchConsumer.cs` — interface
2. `src/Emit.Abstractions/BatchConsumeContext.cs` — context type
3. `src/Emit.Abstractions/BatchItem.cs` — batch item type
4. `src/Emit.Kafka/DependencyInjection/BatchConfigBuilder.cs` — batch config builder
5. `src/Emit.Kafka/Consumer/BatchConfig.cs` — immutable batch config record
6. `src/Emit/Pipeline/BatchHandlerInvoker.cs` — terminal for batch consumers
7. `src/Emit/Consumer/BatchErrorMiddleware.cs` — batch error handling shell
8. `src/Emit/Consumer/BatchRetryMiddleware.cs` — batch retry shell
9. `src/Emit/Consumer/DeadLetterHelpers.cs` — extracted DLQ logic (shared)
10. `src/Emit/Middleware/BatchTracingMiddleware.cs` — batch tracing shell
11. `src/Emit/Middleware/BatchMetricsMiddleware.cs` — batch metrics shell
12. `src/Emit/Middleware/BatchObserverMiddleware.cs` — batch observer shell

### Modified Files
1. `src/Emit.Kafka/DependencyInjection/KafkaConsumerGroupBuilder.cs` — add Batch(), AddBatchConsumer()
2. `src/Emit.Kafka/DependencyInjection/KafkaBuilder.cs` — add batch registration path
3. `src/Emit.Kafka/Consumer/ConsumerGroupRegistration.cs` — add batch config + pipeline factory
4. `src/Emit.Kafka/Consumer/ConsumerWorker.cs` — add RunBatchLoopAsync + FanOutBatchAsync
5. `src/Emit/Pipeline/ConsumerPipelineComposer.cs` — add ComposeBatch method
6. `src/Emit/Consumer/ConsumeErrorMiddleware.cs` — extract DLQ logic to shared helper

### Refactored (No Behavior Change)
1. `src/Emit/Consumer/ConsumeErrorMiddleware.cs` — extract `ExecuteDeadLetterAsync` to `DeadLetterHelpers`

## Pros

- **Clean, explicit API**: `IBatchConsumer<TValue>` makes it clear the consumer handles batches
- **Full pipeline support**: Error handling, retry, tracing, metrics, observers all work for batches
- **No middleware contract violations**: Batch pipeline is correctly typed
- **Minimal code duplication**: Middleware logic is shared via helpers; shells are thin
- **Correct offset semantics**: All-or-nothing commit with watermark algorithm
- **Handles all edge cases**: Rebalance, shutdown, deserialization errors, empty batches
- **Natural + timer batching**: Channel draining for high throughput, timer for latency guarantees
- **Per-item dead-lettering**: When a batch is dead-lettered, each item gets its own DLQ entry with original metadata
- **Per-item metadata preserved**: `BatchItem<T>.TransportContext` gives access to partition, offset, key, headers
- **Registration validation**: Cannot mix single/batch consumers by default — fails at startup, not runtime (this is a design choice, not a platform constraint)
- **Breaking changes allowed**: Leverage this to make a clean API without backward-compat hacks

## Cons

- **~12 new files**: Non-trivial surface area addition
- **Forked pipeline composition**: `ComposeBatch` must stay in sync with `Compose` — no compile-time enforcement. This is the primary architectural risk of this approach.
- **5–6 batch middleware shells**: Each shell must track changes in its single-message counterpart for the lifetime of the project. Helpers mitigate duplication but do not eliminate the synchronization burden.
- **Cannot mix single + batch consumers in one group**: Design choice enforced by registration validation (not a hard platform constraint — if mixing semantics were defined, it could be allowed).
- **Batch-level validation not included**: Individual message validation requires running the pipeline per-message (future enhancement)
- **Retry retries the entire batch**: If one item in the batch causes the failure, all items are retried
- **User middleware typed on ConsumeContext<T> doesn't apply to batches**: Custom middleware registered on the group pipeline won't run for batch consumers (different context type)

## Estimated Effort

- **New abstractions**: ~3 files, ~80 lines
- **New middleware shells**: ~5 files, ~200 lines (thin wrappers + shared helpers)
- **Worker changes**: ~150 lines (batch loop + fan-out)
- **Registration changes**: ~100 lines (builder + registration path)
- **Pipeline composer**: ~50 lines (ComposeBatch method)
- **Tests**: ~500 lines (unit + integration)
- **Total**: ~1,100 lines of production code, ~500 lines of tests

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes middleware shell duplication is "manageable." While each shell is thin, there are 5–6 of them, each requiring tests and each needing to track changes in the corresponding single-message middleware. The assumption that "helpers mitigate duplication" doesn't eliminate the synchronization burden. This is the central architectural weakness of this plan — not a minor tradeoff.
- Assumes `BatchItem<T>.Partition` and `BatchItem<T>.Offset` convenience properties (which cast to `KafkaTransportContext`) are safe. If Emit ever supports another transport with batching, these casts throw `InvalidCastException`. This bakes Kafka specifics into an abstractions-level type.
- Assumes that `BatchConsumeContext<T> : MessageContext` (not inheriting from `ConsumeContext<T>`) is the right hierarchy. This means `MessageContext.Message` doesn't exist on batch contexts, which is correct — but it also means any utility methods or extensions defined on `ConsumeContext<T>` won't work for batch consumers.

### Risks Not Discussed
- **Batch error middleware divergence**: `BatchErrorMiddleware<TValue>` and `ConsumeErrorMiddleware<TMessage>` will inevitably diverge as new error policies are added. Without a shared interface or abstract base, there's no compile-time guarantee they handle the same error policy types.
- **`ComposeBatch` method drift**: The `ComposeBatch` method must mirror `Compose` in middleware ordering. If a developer adds rate limiting to `Compose`, they must remember to add it to `ComposeBatch`. This is a maintenance risk without compile-time enforcement.
- **Synthetic `TransportContext` confusion**: The batch-level `KafkaTransportContext` uses the first item's metadata. This means batch-level tracing/metrics report the first message's partition and offset, which is misleading when the batch spans multiple partitions.

### Verdict Justification
The plan is **thorough, well-structured, and implementation-ready** in its specifics. The offset semantics are correctly detailed for all edge cases (happy path, error, retry exhaustion, rebalance during accumulation, rebalance during processing). The file-by-file change summary is actionable. However, the plan's core structure — `ComposeBatch` as a forked pipeline composer and 5–6 batch middleware shells — is precisely the architectural pattern to avoid: parallel code paths that must stay in sync with no compile-time enforcement. Approach 14 achieves the same user API without any of this forking.

### Blind Spots
- Doesn't compare the maintenance cost of ~12 new files vs Approach 14's ~8. The additional files aren't just creation cost — they're ongoing maintenance cost for the lifetime of the project.
- Doesn't discuss the impact on Emit's contributor experience. New contributors who understand `ConsumeErrorMiddleware` must also learn `BatchErrorMiddleware` and understand why they differ (or don't). This doubles the "middleware surface" to understand.
- The `FanOutBatchAsync` method creates per-item `KafkaTransportContext` objects with `new Guid().ToString()` message IDs. These synthetic IDs don't correspond to any real Kafka message, which could confuse downstream systems that expect real message IDs.

### Strongest Argument
The exhaustive offset semantics documentation (the "Offset Semantics — Detailed" section above) is this approach's standout contribution. The five scenarios (happy path, handler throws, no error policy, retry exhausted, rebalance during accumulation, rebalance during processing) provide a complete mental model for correct batch offset management. This section is a specification, not just a plan note — it applies to any batch implementation regardless of which architectural approach is chosen, and must be preserved.

**Overall: 5/10** — Well-specified, but the `ComposeBatch` fork and batch middleware shells are the exact architectural pattern to avoid. Approach 14 achieves the same user-facing goals without this structural duplication. The offset semantics and edge case documentation in this plan are excellent and should be carried forward as the specification for whichever approach is implemented — they represent the strongest contribution here.
