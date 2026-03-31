# Analysis: Mixing Single and Batch Consumers in the Same Consumer Group

## Context

Approach 14 (dual-interface-with-adapter) is the recommended batch consumption design. It uses:

- `IConsumer<TValue>` for single-message consumers, pipeline typed on `ConsumeContext<TValue>`
- `IBatchConsumer<TValue>` adapted to `IConsumer<MessageBatch<TValue>>`, pipeline typed on `ConsumeContext<MessageBatch<TValue>>`
- Worker reads from bounded channel, deserializes, fans out to consumer pipelines
- Offset commits via watermark-based per-partition tracking (`PartitionOffsets`)

Previous plans stated "cannot mix single + batch consumers in the same group." This analysis evaluates whether mixing is feasible, useful, and worth the complexity.

## What Mixing Looks Like

A single consumer group registration contains both single and batch consumers for the same topic and value type:

```csharp
topic.ConsumerGroup("orders-group", group =>
{
    group.Batch(batch =>
    {
        batch.MaxSize = 100;
        batch.Timeout = TimeSpan.FromSeconds(5);
    });

    // Single-message consumer: per-message validation/enrichment
    group.AddConsumer<OrderValidator>();

    // Batch consumer: bulk DB insert
    group.AddBatchConsumer<OrderBulkWriter>();
});
```

Every message is delivered to single consumers immediately (existing fan-out). Messages are also accumulated into batches and delivered to batch consumers when batch triggers fire. A single message participates in both paths.

## Execution Models

### Option A: Sequential (Single-First, Then Batch)

Single consumers process each message as it arrives. After all single consumers complete, the message enters the batch accumulator. When the batch fires, batch consumers process the batch.

**Ordering**: Single consumers see messages in arrival order. Batch consumers see messages in batch order (which preserves arrival order within each batch).

**Offset model**: The offset cannot advance until both the single consumer(s) AND the batch consumer(s) have completed for that message. Since the batch consumer fires later, the single consumer's completion is necessary but not sufficient.

**Worker loop change**: After `FanOutAsync` (single pipelines), push the deserialized message into a batch accumulator. Do NOT call `offsetManager.MarkAsProcessed` yet. When the batch fires, invoke batch pipelines, then mark all batch offsets as processed.

### Option B: Parallel (Independent Schedules)

Single consumers and batch consumers run independently. Single consumers process messages immediately. The batch accumulator collects messages on its own schedule. Both operate on the same stream.

**Offset model**: Same constraint as Option A. The offset can only advance when ALL consumers (single AND batch) have completed for that message. This requires a per-message completion counter or a "two-phase" offset tracker.

**Ordering**: Single consumers see messages in order. Batch consumers see messages in batch order. No cross-type ordering guarantee (single consumer for message N might run after batch consumer processes a batch containing N).

### Option C: Exclusive (Either/Or)

Each message goes to either single or batch consumers, not both. This is a content-based routing decision.

**Not evaluated further**: This is just routing, not mixing. Use `AddRouter` for this.

## The Offset Problem (Critical)

The current offset model is:

```
Poll loop → offsetManager.Enqueue(topic, partition, offset)
Worker → deserialize → FanOutAsync → offsetManager.MarkAsProcessed(topic, partition, offset)
```

`MarkAsProcessed` is called once per message, after all single consumer pipelines complete. The watermark algorithm in `PartitionOffsets` advances the committable offset when a contiguous prefix of received offsets has been marked processed.

### With Mixing

If a message must pass through both single AND batch consumers, `MarkAsProcessed` cannot be called after single consumer fan-out alone. The batch consumer hasn't seen the message yet.

**Two-phase tracking is required**: Each message needs N completions before it is considered "processed" (where N = number of consumer paths: single pipeline count + batch pipeline count). Only after all paths complete can the offset be marked.

This changes `PartitionOffsets` from a simple enqueue/complete tracker to a reference-counted tracker:

```
Enqueue(offset, expectedCompletions=2)  // 1 for single fan-out, 1 for batch fan-out
MarkCompleted(offset, path="single")    // Decrements counter
MarkCompleted(offset, path="batch")     // Decrements to 0, watermark can advance
```

### Latency Impact on Offset Commits

With batch consumers, a message might sit in the accumulator for up to `batch.Timeout` (e.g., 5 seconds) before the batch fires. During this window, the offset cannot advance past this message, even if all single consumers completed instantly.

Under low throughput, every message blocks offset advancement for the full batch timeout. This means:

- Rebalance during the batch window redelivers all uncommitted messages
- Graceful shutdown must flush the batch accumulator (fire a partial batch) to avoid redelivery
- Backpressure from the batch consumer delays offset commits for single consumers that already succeeded

This is the central trade-off. The batch consumer's accumulation window becomes the floor for offset commit latency.

## Pipeline Implications

Single and batch consumers use different pipeline type parameters:

| Consumer Type | Pipeline Type | Context Type |
|---------------|--------------|--------------|
| Single | `IMiddlewarePipeline<ConsumeContext<TValue>>` | `ConsumeContext<TValue>` |
| Batch | `IMiddlewarePipeline<ConsumeContext<MessageBatch<TValue>>>` | `ConsumeContext<MessageBatch<TValue>>` |

These are independent generic instantiations of the same `ConsumerPipelineComposer.Compose`. No forked composition is needed. The worker must maintain two lists of pipeline entries and fan out to both, which is straightforward.

Group-level middleware registered as `IMiddleware<ConsumeContext<TValue>>` applies only to single consumers. Group-level middleware for batch consumers would need to be `IMiddleware<ConsumeContext<MessageBatch<TValue>>>`. This means:

- User-registered group middleware only applies to one path
- Or the builder must accept middleware for both paths separately
- Or the framework duplicates group middleware across both type instantiations (impossible without double-registering)

This is an API clarity problem, not a technical blocker. The user must understand which middleware applies where.

## Error Handling Implications

### Single Consumer Fails, Batch Consumer Succeeds

The single consumer fails for message N. The error policy dead-letters or discards it. The batch accumulator still holds message N. Should it be removed from the batch? If not, the batch consumer processes a message that the single consumer already dead-lettered.

Options:
1. **Remove from batch on single failure**: Requires the batch accumulator to support removal. Adds coupling between single and batch paths.
2. **Leave in batch regardless**: The batch consumer is independent. It processes all messages, even those that failed in single consumers. The user must handle this (e.g., idempotent batch operations).
3. **Fail both paths**: If the single consumer fails, skip the batch path too. But then the batch consumer never sees the message, violating the "fan-out to both" contract.

Option 2 is the only one that preserves independence between paths, but it creates confusing semantics: a message can be dead-lettered by one consumer and successfully processed by another in the same group.

### Batch Consumer Fails

The batch consumer fails for a batch of 100 messages. All 100 messages are retried (or dead-lettered). But the single consumers already processed all 100 successfully. Their side effects (e.g., dashboard updates) have already been applied. The batch retry causes the single consumers to run again on redelivery, producing duplicate side effects.

This is the standard at-least-once redelivery problem, but mixing amplifies it: the single consumer path is strictly more likely to re-execute because it depends on the batch consumer path's success.

## Worker Implementation Sketch

```
ConsumerWorker.RunAsync:
    while (messages available):
        deserialize message
        FanOutSingleAsync(message)          // Existing: iterate consumerPipelines
        batchAccumulator.Add(message)       // New: push to accumulator

        if batchAccumulator.ShouldFlush():
            FanOutBatchAsync(batchAccumulator.Drain())
            MarkBatchOffsetsProcessed()

    // On shutdown/timeout:
    if batchAccumulator.HasPending():
        FanOutBatchAsync(batchAccumulator.Drain())
        MarkBatchOffsetsProcessed()
```

Key change: `offsetManager.MarkAsProcessed` moves from after `FanOutAsync` to after both paths complete. For the sequential model, this means offsets are only marked after batch flush.

## Is This Actually Useful?

### Claimed Use Cases

1. **Per-message validation + bulk insert**: Single consumer validates/enriches each message. Batch consumer does bulk DB insert.
   - **Counter**: Validation can be a filter or middleware on the batch consumer pipeline. The batch consumer receives pre-validated messages. No mixing needed.

2. **Real-time dashboard + periodic aggregation**: Single consumer updates a real-time view. Batch consumer does periodic aggregation.
   - **Counter**: Two consumer groups achieve the same thing. Group A runs `DashboardConsumer` (single). Group B runs `AggregationConsumer` (batch). Both read from the same topic. Kafka supports multiple consumer groups on the same topic natively. No mixing needed.

3. **Side-effect + bulk persistence**: Single consumer fires webhooks per message. Batch consumer does bulk persistence.
   - **Counter**: Same as use case 2. Two groups. Independent failure domains. Independent offset tracking. Simpler.

### Why Two Consumer Groups Is Simpler

| Aspect | Mixed Group | Two Groups |
|--------|-------------|------------|
| Offset tracking | Two-phase completion counter | Independent per-group watermarks |
| Error isolation | Cross-path failure coupling | Fully independent |
| Redelivery scope | Batch failure redelivers all messages (single re-executes) | Each group redelivers independently |
| Middleware | Ambiguous which path middleware applies to | Clear: each group has its own pipeline |
| Shutdown | Must flush batch accumulator AND drain single pipelines | Each group shuts down independently |
| Complexity | New offset model, accumulator integration, API for mixed middleware | Zero new infrastructure |
| Partition assignment | Shared partitions, shared workers | Independent partition assignment (can even scale differently) |
| Scaling | Single and batch paths share worker pool | Each group scales independently (more workers for single, fewer for batch) |
| Consumer lag | Batch timeout floor applies to single consumer offset commits | Single consumer commits independently, no batch delay |

The only downside of two groups is **double the Kafka consumer connections** (one consumer per group). For most deployments this is negligible. The only scenario where it matters is when the cluster is connection-limited and the team cannot afford a second consumer group per topic.

### The Connection Argument

Two consumer groups means two Kafka consumers, each maintaining connections to all brokers in the cluster. For a cluster with 3 brokers and 50 topics, this doubles from 50 to 100 consumer connections. In practice, Kafka handles thousands of consumer connections per broker. This is not a real constraint for any reasonably-provisioned cluster.

If connection count is genuinely a constraint, the solution is to use a single batch consumer with batch size 1 (which behaves like a single-message consumer) and handle per-message logic inside the batch consumer. This is ugly but avoids the mixing problem entirely.

## Decision Log

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Mixing is technically feasible | The pipeline architecture supports two generic instantiations (`ConsumeContext<TValue>` and `ConsumeContext<MessageBatch<TValue>>`) in the same worker. The worker can maintain two pipeline lists and fan out to both. |
| 2 | Offset tracking requires a new model | The current single-completion watermark algorithm cannot represent "message processed by path A but not path B." A reference-counted or two-phase completion model is required. |
| 3 | Error semantics are ambiguous | When one path fails and the other succeeds, the meaning of "processed" is unclear. No clean resolution exists without coupling the two paths. |
| 4 | Every claimed use case is better served by two consumer groups | Two groups provide independent offset tracking, independent error handling, independent scaling, and independent failure domains with zero new infrastructure. |
| 5 | Batch timeout floors offset commits for single consumers | In a mixed group, the single consumer's offset cannot advance past messages sitting in the batch accumulator. This penalizes single consumer commit latency for no benefit. |

## Rejected Alternative: Support Mixing

**Why not**: The offset tracking change (two-phase completion) is a foundational change to `PartitionOffsets` that adds complexity to the hot path of every consumer group, even those that don't mix. The error handling ambiguity has no clean resolution. Every use case is achievable with two consumer groups at lower complexity and better operational characteristics.

**The only argument for mixing** is reduced Kafka connections (one group instead of two). This is a negligible operational cost that does not justify the architectural complexity.

## Verdict

**Do not support mixing single and batch consumers in the same consumer group.** Enforce this at registration time: if `group.Batch(...)` is called, only `AddBatchConsumer<T>()` is allowed; if `AddConsumer<T>()` is called, `Batch(...)` is rejected. This is the existing plan in Approach 14 and it is correct.

The validation should throw `InvalidOperationException` with a clear message:

> "Cannot register single-message consumers and batch consumers in the same consumer group. Use separate consumer groups for independent processing modes."

Two consumer groups on the same topic is the idiomatic Kafka pattern for independent processing modes. It provides better failure isolation, simpler offset semantics, independent scaling, and zero new infrastructure. The added Kafka connection cost is negligible.

### Confidence

**High**. The offset tracking problem alone is sufficient to reject mixing. The error handling ambiguity reinforces the decision. The lack of a compelling use case that cannot be served by two groups confirms it. This is not a case of "too complex to build" -- it is a case of "the simpler alternative is strictly better on every axis except connection count."
