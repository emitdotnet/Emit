# Approach 17: Channel Transform Pipeline — BatchChannel<T> Wrapping Worker Channel

## Concept

Instead of modifying the worker, insert a **batching channel transform** between the poll loop's write and the worker's read. The poll loop writes individual messages to a `BatchChannel<T>`, which internally accumulates and produces batches to the worker's read channel.

## How It Works

```
Poll Loop → writes individual messages → BatchChannel<TKey, TValue>
    → internally buffers (max size / timeout)
    → writes List<ConsumeResult> batches → Worker reads batches
Worker → reads List<ConsumeResult> → deserializes batch → dispatches
```

### BatchChannel Implementation

```csharp
internal sealed class BatchChannel<T>
{
    private readonly Channel<T> input;        // Individual messages from poll loop
    private readonly Channel<List<T>> output;  // Batched messages for worker
    private readonly int maxSize;
    private readonly TimeSpan timeout;
    private readonly Task accumulator;

    public ChannelWriter<T> Writer => input.Writer;
    public ChannelReader<List<T>> Reader => output.Reader;

    public BatchChannel(int maxSize, TimeSpan timeout, int bufferSize)
    {
        this.maxSize = maxSize;
        this.timeout = timeout;
        input = Channel.CreateBounded<T>(bufferSize);
        output = Channel.CreateBounded<List<T>>(1); // Single batch buffer
        accumulator = AccumulateAsync();
    }

    private async Task AccumulateAsync()
    {
        while (await input.Reader.WaitToReadAsync())
        {
            var batch = new List<T>();
            using var cts = new CancellationTokenSource(timeout);
            // ... accumulate until maxSize or timeout ...
            await output.Writer.WriteAsync(batch);
        }
    }
}
```

### Worker Changes

The worker's channel type changes from `Channel<ConsumeResult>` to `Channel<List<ConsumeResult>>`:
```csharp
// Before: reader.TryRead(out var raw)
// After: reader.TryRead(out var rawBatch)
```

## Analysis

### Problems

1. **Changes the worker's channel type**: The `ConsumerWorker`'s channel is `Channel<ConsumeResult<byte[], byte[]>>`. Changing to `Channel<List<ConsumeResult<byte[], byte[]>>>` changes the worker's entire read API.
2. **Poll loop changes**: `ConsumerGroupWorker.RunPollLoopAsync` writes to `workers[i].Writer`. The writer type changes, affecting the poll loop too.
3. **Background accumulator task**: The `BatchChannel` runs its own background task for accumulation. This adds lifecycle management complexity (start, stop, dispose, error handling).
4. **Offset management**: The poll loop currently calls `offsetManager.Enqueue` per message. With a batch channel, when does enqueue happen? Before the batch is formed? After? The poll loop doesn't know about batch boundaries.
5. **Distribution strategy**: Messages are distributed to workers BEFORE batching. This means each worker gets a mix of messages, and the batch within a worker might contain messages from different partitions/keys. This is fine for batch semantics but changes the distribution model.

### Alternative: Batch Channel Between Distribution and Worker

Put the batch channel AFTER the distribution strategy:
```
Poll Loop → distribution strategy → writes to BatchChannel[workerIndex] → Worker reads batches
```

This preserves key-based distribution (same key goes to same worker's batch channel), so batches within a worker are key-consistent.

But this still changes the worker's read interface and requires lifecycle management for N batch channels.

## Verdict

**Interesting but over-engineered.** The batch channel is an additional architectural component that doesn't simplify the implementation — it just moves the accumulation logic to a different place while adding lifecycle management overhead. The worker's inner loop approach (Approach 12/14) is simpler and more direct, and avoids the need to fork the pipeline composition.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes the worker's channel type (`Channel<ConsumeResult<byte[], byte[]>>`) is a fundamental constraint. In reality, the channel type is an implementation detail of `ConsumerWorker` — it could be changed to a generic type or wrapped in an abstraction without affecting external contracts.
- Assumes the background accumulator task adds significant lifecycle complexity. While true, `BackgroundService` already manages similar background tasks, and the worker itself is already a background task.

### Risks Not Discussed
- **Batch channel as single point of failure**: If the accumulator task throws unexpectedly, the entire worker stalls — messages accumulate in the input channel but never reach the output. Without health monitoring on the accumulator task, this failure mode is silent.
- **Backpressure cascading**: With `output = Channel.CreateBounded<List<T>>(1)`, if the worker can't consume batches fast enough, the batch channel blocks, which blocks the input channel, which blocks the poll loop. This triple-layer backpressure is harder to diagnose than the current single-channel model.

### Verdict Justification
The verdict ("interesting but over-engineered") is **correct**. The batch channel doesn't simplify the implementation — it moves accumulation to a different location while adding lifecycle management. The comparison with the worker's inner loop approach (same behavior, fewer moving parts) is apt. The valid rejection reasons here are the accumulator lifecycle complexity and the worker channel type change — not a blanket constraint against new middleware classes or against mixing single and batch consumers.

### Blind Spots
- Doesn't consider that the batch channel could be useful as a reusable component. If multiple features need message accumulation (not just batch consumption), a `BatchChannel<T>` utility could be worth the investment.
- Doesn't mention testability. A `BatchChannel<T>` can be unit-tested in isolation (feed messages, verify batch output), while the worker's inner loop accumulation is harder to test independently.

### Strongest Argument
The separation of concerns argument has merit — keeping accumulation logic separate from the worker's main loop makes each component simpler in isolation. But this benefit doesn't outweigh the lifecycle complexity cost for a single use case.

**Overall: 3/10** — Correctly rejected. The abstraction adds more complexity than it removes, and the accumulation logic is simple enough to inline in the worker.
