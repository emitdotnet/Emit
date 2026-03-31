# Per-Item Retry Semantics for Batch Consumption

## Problem Statement

Approach 14 retries the entire `MessageBatch<T>` on failure. If a batch of 100 items fails because item 47 hits a transient error, all 100 items are retried, including the 46 that already succeeded. This wastes work, and the consumer must be fully idempotent to handle re-processing of already-succeeded items.

The question: can the framework retry only the failed items while respecting Kafka's per-partition ordering guarantee?

## Current Architecture (Relevant)

**RetryMiddleware<TMessage>**: Generic over `TMessage`. For batches, `TMessage = MessageBatch<TValue>`. On exception, it re-invokes `next.InvokeAsync(context)` with the same `ConsumeContext<MessageBatch<TValue>>`. The entire batch is retried as a unit. The middleware has no concept of individual items within the batch.

**ConsumerWorker**: Processes messages one at a time. For batches (Approach 14), it will accumulate messages into a `MessageBatch<T>`, dispatch through the pipeline, then call `offsetManager.MarkAsProcessed()` for each message in the batch after success. Currently: all-or-nothing. Either all offsets are marked, or none.

**OffsetManager / PartitionOffsets**: Watermark-based. Each offset is enqueued before dispatch and marked as processed after. The watermark for a partition only advances when the lowest-offset message is marked complete. This already supports out-of-order completion natively — if offset 5 completes before offset 3, the watermark stays at 2 until offset 3 completes.

**ConsumeErrorMiddleware**: Terminal error handler. After retry exhaustion, applies error policy (dead-letter or discard). Already has the `IBatchMessage` hook planned for per-item dead-lettering.

## What "Per-Item Retry" Actually Means

### Option A: Retry a Single Item in Isolation

Consumer receives the full batch. Item N fails. Framework retries item N by calling the consumer again with a batch containing only item N.

**Problem**: The consumer already processed items 1..N-1 in the same call. Re-calling the consumer with just item N means the consumer sees a completely new batch context. If the consumer was doing a bulk insert (the primary batch use case), this defeats the purpose — instead of one INSERT of 100 rows, you get one INSERT of 99 rows + one INSERT of 1 row on retry. Acceptable but not ideal.

**Deeper problem**: The consumer has no way to know it already processed items 1..N-1. If the consumer does work beyond the batch itself (updating counters, sending notifications), single-item retry could cause side effects to fire twice for the already-succeeded items.

### Option B: Per-Item Sequential Processing

The framework calls the consumer once per item. Retry is per-item naturally.

**Problem**: This defeats the entire purpose of batching. If you want per-item processing with per-item retry, use the existing single-message `IConsumer<T>` pipeline. There is no reason for `IBatchConsumer<T>` to exist in this model.

**Verdict**: Rejected. This is not batch consumption.

### Option C: Consumer Reports Per-Item Outcomes

Consumer receives the batch, processes items, and reports success/failure per item back to the framework. Framework collects failed items into a retry mini-batch and dispatches again.

```csharp
public class OrderBatchConsumer : IBatchConsumer<OrderCreated>
{
    public async Task ConsumeAsync(BatchConsumeContext<OrderCreated> context, CancellationToken ct)
    {
        foreach (var item in context.Items)
        {
            try
            {
                await db.UpsertAsync(item.Message, ct);
                item.Acknowledge(); // Mark as succeeded
            }
            catch (Exception ex)
            {
                item.Fail(ex); // Mark as failed — framework will retry
            }
        }
    }
}
```

**This is the only model that makes semantic sense for per-item retry in a batch context.** The consumer is the only entity that knows which items succeeded and which failed. The framework cannot infer this from a thrown exception.

### Option D: Framework Infers Failed Items from Exception

Consumer throws. Framework somehow determines which items failed.

**Problem**: Impossible in general. An exception carries no information about which batch items caused it. Even if you added an `ItemFailedException(int index)`, the consumer might fail on a cross-cutting concern (DB connection lost during batch insert) where ALL items failed, not a specific one.

**Verdict**: Rejected. The framework cannot infer per-item outcomes from an exception.

## The Ordering Constraint

Kafka ordering is per-partition. Messages from the same partition must be processed in order. This has direct consequences for per-item retry:

### Scenario: Single Partition Batch

Batch: `[offset=1, offset=2, offset=3]` — all partition 0.

Item at offset 2 fails. Can offset 3 proceed?

**No.** If offset 3 is allowed to succeed while offset 2 retries, the consumer has processed messages out of order within the partition. If the consumer's logic depends on ordering (e.g., applying events to an aggregate), this violates correctness.

### Scenario: Multi-Partition Batch

Batch: `[P0:offset=1, P1:offset=5, P0:offset=2, P1:offset=6]`

Item P0:offset=1 fails. What can proceed?

- P1:offset=5 and P1:offset=6 **can proceed** — different partition, independent ordering.
- P0:offset=2 **must wait** — same partition, depends on P0:offset=1.

**Per-item retry is really per-partition-group retry.** When an item fails, all subsequent items from the same partition are blocked. Items from other partitions are independent.

### Implication

A batch touching N partitions can be split into N independent retry tracks. If only one partition has failures, the other N-1 partitions can complete immediately and have their offsets committed. This is the throughput win.

## Offset Implications

The existing `OffsetManager` already supports this natively. Each partition's watermark is independent. If P1's items are all marked as processed while P0's items are still retrying, P1's watermark advances and offsets are committed. P0's watermark stays put until its items complete (or are dead-lettered).

The change required: instead of marking ALL items as processed after the batch pipeline completes, mark items as processed individually as their partition-group completes.

This means `MarkAsProcessed` calls move from a post-pipeline batch operation to a per-partition-group callback. The `ConsumerWorker` (or a new batch retry coordinator) must track which partition-groups have completed.

## Design Space: Where Per-Item Retry Lives

### Location 1: Inside RetryMiddleware (Framework-Managed)

`RetryMiddleware` becomes batch-aware. On failure, it inspects the `MessageBatch<T>` for per-item outcomes, partitions failed items by Kafka partition, and retries each partition-group independently.

**Problems**:
- `RetryMiddleware` is in core `Emit`, which must not know about Kafka partitions (project boundary isolation rule).
- `RetryMiddleware` is generic over `TMessage`. Making it batch-aware means it must special-case `MessageBatch<T>`, adding the same kind of "batch awareness leak" that `IBatchMessage` in error middleware introduces — but far more complex.
- Retry delay (backoff) becomes per-partition-group, not per-batch. Multiple concurrent retry timers within a single middleware invocation.

### Location 2: New BatchRetryMiddleware (Batch-Specific)

A separate `BatchRetryMiddleware<TValue>` that understands `MessageBatch<T>` and orchestrates per-partition-group retry. It replaces `RetryMiddleware` for batch consumer groups (not added alongside it).

**Where it lives**: `src/Emit/Consumer/BatchRetryMiddleware.cs` — core Emit, since it operates on `MessageBatch<T>` which is in Abstractions. But it needs partition information from `TransportContext`, which is transport-agnostic (good) but would need a partition concept at the abstraction level.

**Partition at the abstraction level**: `BatchItem<T>.TransportContext` already carries partition info (via `KafkaTransportContext.Partition`). But `BatchRetryMiddleware` in core Emit cannot cast to `KafkaTransportContext`. The fix: add an optional `Partition` or `OrderingKey` property to `TransportContext` (or `BatchItem<T>`). This is transport-agnostic — Azure Service Bus has sessions, RabbitMQ has routing keys. Calling it `OrderingKey` makes it generic.

### Location 3: Consumer Worker Level (Below Pipeline)

The `ConsumerWorker` itself manages retry. Instead of dispatching the whole batch through the pipeline once, it dispatches, collects per-item outcomes, and re-dispatches failed partition-groups through the pipeline again.

**Problem**: This puts retry logic outside the middleware pipeline, breaking the current architecture where retry is a middleware concern. It also means the pipeline's retry middleware would need to be disabled for batch consumer groups to avoid double-retry.

### Location 4: Consumer-Side (User Responsibility)

The framework provides no per-item retry. The consumer handles it:

```csharp
public async Task ConsumeAsync(BatchConsumeContext<OrderCreated> context, CancellationToken ct)
{
    foreach (var item in context.Items)
    {
        await RetryPolicy.ExecuteAsync(() => ProcessAsync(item, ct));
    }
}
```

**Problem**: The user re-implements retry (backoff, max attempts, metrics, tracing). The framework's retry configuration is ignored. No dead-letter integration for individual items.

**Counter-argument**: This is the simplest model. Users who want per-item retry are sophisticated enough to implement it. The framework provides batch-level retry (retry the whole batch) as the default, and per-item retry is a user concern.

## Approach Comparison

| Aspect | Whole-Batch Retry (Current) | Per-Item Retry (Option C + Location 2) |
|--------|---------------------------|--------------------------------------|
| Complexity | Zero new code | New middleware, outcome reporting API, partition grouping |
| User API | Throw = retry all | `item.Acknowledge()` / `item.Fail(ex)` per item |
| Ordering safety | Trivially safe (all or nothing) | Safe if partition-group isolation is correct |
| Throughput on partial failure | Poor (retry 100 items for 1 failure) | Good (retry only affected partition-group) |
| Idempotency requirement | Full batch idempotency | Per-item idempotency (weaker requirement) |
| Dead-lettering granularity | Whole batch or per-item via IBatchMessage | Per-item naturally |
| Offset commit granularity | All-or-nothing per batch | Per-partition-group (faster watermark advance) |
| Existing middleware reuse | 100% reuse | RetryMiddleware replaced for batch groups |
| Consumer code complexity | Simple (throw on failure) | Moderate (must report per-item outcomes) |
| Testing complexity | Simple | Moderate (must test partition-group isolation) |

## What Doors Per-Item Retry Opens

1. **Partial batch success**: 99 of 100 items succeed immediately. Only the failed partition-group retries. Throughput on partial failure improves dramatically.
2. **Granular dead-lettering**: A single poison message gets dead-lettered without affecting the other 99. Currently, the whole batch is dead-lettered (or the consumer must catch and handle internally).
3. **Per-partition isolation within a batch**: A transient failure on one partition doesn't block progress on other partitions. Multi-partition batches degrade gracefully.
4. **Weaker idempotency requirement**: The consumer only needs per-item idempotency, not full-batch idempotency. This is a meaningful reduction in burden for consumers doing side effects.
5. **Better observability**: Retry metrics and traces are per-partition-group, not per-batch. You can see which partitions are problematic.

## What Complexity Per-Item Retry Adds

1. **New user-facing API surface**: `item.Acknowledge()` and `item.Fail(ex)` on `BatchItem<T>`. Users must learn this protocol. If they forget to acknowledge, behavior is undefined (or requires a "default to success" convention).
2. **`OrderingKey` abstraction**: `TransportContext` (or `BatchItem<T>`) needs a partition/ordering concept at the abstraction level. This is a permanent API surface expansion.
3. **BatchRetryMiddleware**: A new middleware class (~150-200 lines) that manages per-partition-group retry loops with independent backoff timers. Concurrent retry of multiple partition-groups within a single middleware invocation.
4. **Offset marking granularity**: `ConsumerWorker` must mark offsets per partition-group completion, not all-at-once. Requires a callback or event mechanism from the retry middleware back to the worker.
5. **Edge cases**: What if ALL items in the batch fail? (Equivalent to whole-batch retry.) What if the consumer throws without calling `Acknowledge()` or `Fail()` on any item? (Must default to "all failed".) What if the consumer acknowledges item N but fails on item N+1 from the same partition? (Must block item N+1 and all subsequent same-partition items, but item N is already acknowledged — inconsistent state.)
6. **Pipeline composition change**: `ConsumerPipelineComposer` must use `BatchRetryMiddleware` instead of `RetryMiddleware` for batch consumer groups. This is a conditional fork in composition — not a full `ComposeBatch`, but a branch.

## The Edge Case That Kills Naive Per-Item Retry

Consider:

```csharp
public async Task ConsumeAsync(BatchConsumeContext<OrderCreated> context, CancellationToken ct)
{
    // Bulk insert all items in one DB call
    var orders = context.Items.Select(i => i.Message).ToList();
    await db.BulkInsertAsync(orders, ct); // Throws if ANY item fails constraint
}
```

The consumer cannot report per-item outcomes because the operation is atomic. The bulk insert either succeeds for all or fails for all. This is the primary batch consumption use case.

Per-item retry only helps when the consumer processes items individually within the batch:

```csharp
foreach (var item in context.Items)
{
    try { await ProcessAsync(item); item.Acknowledge(); }
    catch (Exception ex) { item.Fail(ex); }
}
```

But if you process items individually, why use a batch consumer? The answer: to amortize overhead (one DB connection, one transaction, one scope creation) across many items, even if processing is item-by-item within that scope. This is a valid use case, but it narrows the audience for per-item retry.

## Verdict

Per-item retry with ordering guarantees is **technically feasible** but introduces significant complexity for a narrow use case. The honest assessment:

**The throughput win is real but situational.** If your batch regularly has partial failures across multiple partitions, per-item retry avoids redundant re-processing. If failures are rare or all-or-nothing (bulk insert), the benefit is negligible.

**The ordering safety is achievable.** Per-partition-group isolation with independent retry tracks preserves Kafka ordering. The `OffsetManager` already supports this natively. The hard part is the middleware orchestration, not the offset management.

**The user API burden is non-trivial.** Requiring `item.Acknowledge()` / `item.Fail(ex)` changes the consumer contract from "throw on failure" to "report outcomes per item." This is a fundamentally different programming model.

**The complexity concentrates in one place.** `BatchRetryMiddleware` is the only complex new component. Everything else (ordering key abstraction, offset marking, pipeline composition branch) is straightforward.

## Design Choices for the User

### Choice 1: Ship without per-item retry (recommended starting point)

Whole-batch retry is the default. The consumer throws, the entire batch retries. Per-item dead-lettering is still available via `IBatchMessage` in error middleware (items are dead-lettered individually after retry exhaustion). This covers 80% of use cases with zero additional complexity.

**Add per-item retry later if demand materializes.** The Approach 14 architecture does not preclude this — `BatchRetryMiddleware` can be introduced as an opt-in replacement for `RetryMiddleware` on batch consumer groups without breaking changes.

### Choice 2: Ship with per-item retry as opt-in

```csharp
group.Batch(batch =>
{
    batch.MaxSize = 100;
    batch.Timeout = TimeSpan.FromSeconds(5);
    batch.PerItemRetry = true; // Opt-in
});
```

When enabled, `BatchRetryMiddleware` replaces `RetryMiddleware` in the pipeline composition. The consumer must use `item.Acknowledge()` / `item.Fail(ex)`. When disabled (default), whole-batch retry applies.

**Risk**: Two retry codepaths to maintain. The opt-in flag creates a conditional branch in pipeline composition.

### Choice 3: Ship with per-item retry as the default

All batch consumers use per-item retry. Items not explicitly acknowledged are treated as succeeded (convention: success is the default, failure must be explicit).

**Risk**: Forces all batch consumers to learn the per-item outcome reporting API, even if they do bulk operations where per-item reporting is impossible. The "default to success" convention means a consumer that throws without acknowledging anything silently succeeds all items — dangerous.

### Recommendation

**Choice 1.** Ship batch consumption with whole-batch retry. Per-item dead-lettering (already planned via `IBatchMessage`) provides per-item granularity at the terminal error boundary. The architecture is extensible toward Choice 2 if demand appears. Do not build per-item retry speculatively.
