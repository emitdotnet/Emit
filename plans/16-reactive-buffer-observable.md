# Approach 16: Reactive Extensions (Rx) — Observable Buffer Pattern

## Inspiration

Rx.NET's `Buffer(count, timeSpan)` operator, which naturally accumulates elements by count or time window.

## Concept

Instead of a custom accumulation loop, wire the worker's channel as an `IObservable<T>` and use `.Buffer(maxSize, timeout)` to batch messages. The batch consumer subscribes to the buffered observable.

## User Experience

Same as other approaches — user implements `IBatchConsumer<T>`. The Rx integration is purely internal.

## How It Works

```csharp
// In BatchConsumerWorker:
var observable = channel.Reader.ReadAllAsync(ct).ToObservable();
var buffered = observable.Buffer(TimeSpan.FromSeconds(5), maxBatchSize);
await buffered.ForEachAsync(async batch =>
{
    if (batch.Count == 0) return;
    await DispatchBatchAsync(batch, ct);
}, ct);
```

## Analysis

### Problems

1. **New dependency**: Requires `System.Reactive` NuGet package. CLAUDE.md says "no commercial libraries" but Rx.NET is MIT. However, adding a dependency for one feature is heavyweight.
2. **Rx lifecycle complexity**: Observable subscriptions, disposal, error propagation, backpressure — all need careful handling.
3. **Offset management doesn't fit Rx**: Rx buffers are fire-and-forget. We need to control when offsets are committed based on processing success. This requires breaking out of the Rx pipeline for offset management.
4. **Threading model mismatch**: Rx schedulers vs worker threads. Rx may schedule callbacks on thread pool threads, conflicting with the worker's single-reader channel model.
5. **Error semantics**: Rx errors terminate the observable. We need errors to be handled per-batch without terminating the stream.
6. **Debugging difficulty**: Rx stack traces are notoriously hard to read.

### Benefits (If Used Correctly)

- `.Buffer(count, timeSpan)` is a well-tested, proven implementation
- Handles the "whichever triggers first" requirement natively
- Composable: could add other operators (throttle, sample, window)

### Why Not

The `.Buffer(count, timeSpan)` behavior is exactly 20 lines of code to implement with `ChannelReader` + `CancellationTokenSource.CancelAfter`. Adding a dependency for 20 lines of equivalent code violates the "avoid unnecessary dependencies" principle.

## Verdict

**Overkill dependency for a simple accumulation loop.** The buffer behavior is trivial to implement with channels and timers. Rx adds complexity, a dependency, and threading concerns without meaningful benefit.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes Rx.NET is only useful for the `.Buffer()` operator. In reality, Rx provides a complete streaming model that could benefit other Emit features (throttling, sampling, windowing). The "overkill for one feature" argument is weaker if Emit plans to add more streaming operators.
- Assumes the channel-based accumulation loop is equivalent to Rx.Buffer. It's functionally equivalent for the simple case, but Rx.Buffer handles edge cases (disposal, error propagation, scheduler management) that a hand-written loop must handle manually.

### Risks Not Discussed
- **Reinvention risk**: By rejecting Rx, the project commits to maintaining its own accumulation implementation. If bugs are found in the custom accumulation logic (timer races, partial-batch leaks), the fix burden falls on the Emit team rather than leveraging Rx.NET's mature, well-tested codebase.
- **Future operator requests**: If users later request windowed processing, sliding windows, or debounced consumption, the custom implementation will need to grow. Rx provides these out of the box.

### Verdict Justification
The verdict ("overkill dependency") is **reasonable for the current scope**. The 20-lines-of-code comparison is compelling — adding a ~2MB dependency for 20 lines of equivalent behavior is hard to justify. The threading model mismatch (Rx schedulers vs worker threads) is a genuine concern that would require careful integration work.

### Blind Spots
- Doesn't mention that `System.Reactive` is an official .NET Foundation package (not a third-party library), which somewhat weakens the "external dependency" concern.
- Doesn't discuss the "observable per worker" model. Each worker could have its own observable pipeline, which would naturally provide per-worker isolation without the channel-based buffer management.

### Strongest Argument
The 20-lines comparison is the strongest argument against Rx. When the needed functionality is simple, well-understood, and easily implemented, adding a dependency introduces more risk than it removes.

**Overall: 3/10** — Correctly rejected for the current scope. The dependency-to-value ratio is too high. But the document should note that if Emit ever needs more streaming operators, Rx should be reconsidered.
