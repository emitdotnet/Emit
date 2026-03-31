# Approach 19: TPL Dataflow — BatchBlock<T> as Accumulator

## Inspiration

`System.Threading.Tasks.Dataflow.BatchBlock<T>` — built-in .NET type that groups inputs into arrays of a specified size, with timer-based triggers.

## Concept

Replace the worker's `Channel<ConsumeResult>` with a `BatchBlock<ConsumeResult>` (or insert one between channel and processing). The `BatchBlock` handles accumulation, and the worker processes `ConsumeResult[]` batches.

## How It Works

```csharp
// Worker setup:
var batchBlock = new BatchBlock<ConsumeResult<byte[], byte[]>>(maxBatchSize,
    new GroupingDataflowBlockOptions { BoundedCapacity = bufferSize });

// Timer trigger for partial batches:
var timer = new Timer(_ => batchBlock.TriggerBatch(), null, timeout, timeout);

// Processing:
var actionBlock = new ActionBlock<ConsumeResult<byte[], byte[]>[]>(async batch =>
{
    await DispatchBatchAsync(batch, ct);
});

batchBlock.LinkTo(actionBlock);

// Poll loop writes to batchBlock:
await batchBlock.SendAsync(result, ct);
```

## Analysis

### Advantages

1. **Built-in .NET**: `System.Threading.Tasks.Dataflow` is a Microsoft package, well-tested
2. **`BatchBlock<T>`** handles count-based and timer-based triggering natively via `TriggerBatch()`
3. **Backpressure**: `BoundedCapacity` on the block provides natural backpressure
4. **Linking**: Blocks can be linked to form processing pipelines

### Problems

1. **New dependency**: While `System.Threading.Tasks.Dataflow` is a Microsoft package, it's not in the base framework. Adding it as a dependency for one feature is questionable.
2. **Architecture mismatch**: The worker already uses `Channel<T>`, which is the modern replacement for Dataflow blocks. Adding Dataflow alongside channels creates two competing buffering mechanisms.
3. **Timer-based TriggerBatch**: Requires a separate `Timer` to call `TriggerBatch()` — not significantly simpler than `CancellationTokenSource.CancelAfter()` in a channel-based loop.
4. **Offset management**: Same challenge — when do offsets get enqueued and marked? The `ActionBlock` callback doesn't have direct access to the offset manager without closure capture.
5. **Error propagation**: Dataflow blocks have their own error/completion model that doesn't map cleanly to the worker's error handling.
6. **Lifecycle**: Dataflow blocks need explicit completion signaling (`Complete()` + `Completion` task). This adds complexity to shutdown and rebalancing.

### Comparison: BatchBlock vs Channel Loop

| Feature | BatchBlock | Channel + CancelAfter |
|---------|-----------|----------------------|
| Count-based | ✅ Built-in (batch size param) | ✅ Loop with counter |
| Time-based | ⚠️ Manual timer + TriggerBatch() | ✅ CancelAfter on linked CTS |
| Backpressure | ✅ BoundedCapacity | ✅ BoundedChannelOptions |
| Dependencies | System.Threading.Tasks.Dataflow | None (built-in) |
| Error handling | Dataflow faulted model | Try-catch in loop |
| Lifecycle | Complete() + Completion | Writer.TryComplete() |
| Lines of code | ~30 | ~25 |

The channel-based approach is equally simple, doesn't require a dependency, and integrates naturally with the existing worker model.

## Verdict

**Equivalent capability, unnecessary dependency.** `BatchBlock<T>` solves the same problem as a channel-based accumulation loop with roughly the same amount of code but adds a dependency and a second buffering abstraction. The channel-based approach in Approach 12/14 is simpler and more consistent with the existing architecture.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes `System.Threading.Tasks.Dataflow` is "not in the base framework." It's actually included in the .NET SDK as a runtime library, though it's a separate NuGet package for targeting. The dependency concern is slightly overstated.
- Assumes the channel-based approach and Dataflow approach are equivalent in complexity. For the SIMPLE case (size + timer), they are. But Dataflow's linking model becomes more valuable if the pipeline grows (e.g., adding a TransformBlock for deserialization before the BatchBlock).

### Risks Not Discussed
- **Dataflow deprecation risk**: While not deprecated, Dataflow has received minimal updates. Microsoft's investment is in `System.Threading.Channels`, which is the modern replacement. Adopting Dataflow risks building on a stagnant API.
- **Mixing patterns**: Having both `Channel<T>` and `BatchBlock<T>` in the same worker means developers must understand two different async buffering models. This cognitive overhead affects maintainability.

### Verdict Justification
The verdict ("equivalent capability, unnecessary dependency") is **correct and the comparison table supports it**. The line-by-line comparison shows no meaningful simplification from Dataflow. The ~30 vs ~25 lines comparison demonstrates that the custom approach is actually simpler.

### Blind Spots
- Doesn't mention that `BatchBlock<T>` has built-in completion propagation via `LinkTo`, which could simplify shutdown coordination. The channel approach requires explicit `Writer.TryComplete()` and `Reader.Completion` handling.
- Doesn't discuss that Dataflow blocks are inherently thread-safe with configurable parallelism, while the channel approach relies on single-reader semantics.

### Strongest Argument
The document's comparison table is well-constructed and clearly shows feature parity. The dependency argument is the decisive factor — when two approaches are functionally equivalent, prefer the one with fewer dependencies.

**Overall: 3/10** — Correctly rejected. Functionally equivalent to the channel approach with more baggage.
