# Comparison Matrix & Recommendation

## Quick Reference

> **Column note — "Code Duplication"**: This column merges two distinct concerns. "Full duplication" (🔴) means a forked pipeline composition — a separate `ComposeBatch` method and parallel shell middleware classes that mirror the existing pipeline. This is the problematic form. "Some" (🟡) means new middleware classes purpose-built for batch contexts. Adding new middleware classes is explicitly fine; forking the pipeline composition is what creates maintainability problems.

| # | Approach | Pipeline Compat | Code Duplication | API Clarity | Offset Safety | Verdict |
|---|---------|----------------|-----------------|-------------|---------------|---------|
| 1 | Dedicated `IBatchConsumer<T>` | ❌ Incompatible types | 🔴 Forked pipeline | ✅ Excellent | ✅ Safe | Clean API, bad internals |
| 2 | `MessageBatch<T>` as message type | ✅ Works via generics | ✅ None | ⚠️ Longer type name | ✅ Safe | Elegant but dual-type registration |
| 3 | Batching middleware (KafkaFlow) | ❌ Breaks contract | ✅ None | ✅ Good | ❌ Broken | Architecturally incompatible |
| 4 | Dedicated BatchConsumerWorker | ⚠️ Forked pipeline | 🔴 Forked pipeline | ✅ Good | ✅ Safe | Works but forked pipeline |
| 5 | Polymorphic worker (strategy) | ⚠️ Same as #4 | 🟡 Strategy helps | ✅ Good | ✅ Safe | Better #4, same core issue |
| 6 | `ConsumeContext<IReadOnlyList<T>>` | ✅ Works | ✅ None | ❌ Misleading | ✅ Safe | Lossy — no per-item metadata |
| 7 | Features bag accessor | ✅ Works | ✅ None | ❌ Dangerous | ✅ Safe | Silent data loss risk |
| 8 | Inherit `ConsumeContext<T>` | ⚠️ LSP violation | 🟡 Some | ⚠️ Confusing | ✅ Safe | Architecturally unsound |
| 9 | Two-phase (pipeline then batch) | ⚠️ Partial | 🟡 Some | ⚠️ Complex | ❌ Inconsistent | Semantically broken |
| 10 | Consumer mode + separate pipeline | ⚠️ Batch pipeline | 🟡 New batch middleware | ✅ Good | ✅ Safe | Practical, moderate duplication |
| 11 | Channel draining (no timer) | N/A (incomplete) | N/A | ✅ Simple | ✅ Safe | Missing timeout requirement |
| **12** | **Hybrid worker + dedicated pipeline** | ⚠️ Batch pipeline | 🟡 New batch middleware | ✅ Good | ✅ Safe | **Recommended if you want max control** |
| 13 | `IConsumer<MessageBatch<T>>` | ✅ Works via generics | ✅ None | ⚠️ Longer type name | ✅ Safe | Great, but no `IBatchConsumer<T>` |
| **14** | **`IBatchConsumer<T>` adapter** | ✅ Works via generics | ✅ None | ✅ Excellent | ✅ Safe | **⭐ RECOMMENDED** |
| 15 | Spring Kafka attributes | ❌ Wrong paradigm | N/A | ❌ Not type-safe | N/A | Wrong paradigm for Emit |
| 16 | Rx Observable Buffer | ✅ Possible | ✅ None | ✅ Good | ⚠️ Complex | Overkill dependency |
| 17 | BatchChannel transform | ⚠️ Changes interfaces | 🟡 Some | ✅ Good | ✅ Safe | Over-engineered |
| 18 | Wolverine saga/coordinator | ❌ Breaks contract (this design) | N/A | ⚠️ Complex | ❌ Broken | Wrong design (doesn't call next) |
| 19 | TPL Dataflow BatchBlock | ✅ Possible | ✅ None | ✅ Good | ✅ Safe | Unnecessary dependency |

## Top 3 Contenders

### 🥇 Approach 14: `IBatchConsumer<T>` Adapter to `IConsumer<MessageBatch<T>>`

**Why it wins:**
- **Clean user API**: `IBatchConsumer<TValue>` is explicit, discoverable, compile-time safe
- **Zero forked pipeline composition**: ALL middleware works unchanged via generics; no `ComposeBatch` method needed
- **Zero pipeline composer changes**: Existing `Compose` method handles `MessageBatch<T>`
- **Per-item dead-lettering**: `IBatchMessage` marker interface with tiny error middleware addition
- **Adapter pattern is proven**: Common pattern in .NET DI — adapter registered alongside real consumer

**Trade-offs accepted:**
- One level of adapter indirection (negligible perf impact)
- `IBatchMessage` marker interface couples error middleware to batch concept (tiny, well-contained)
- `MessageBatch<T>` type exists internally (users never interact with it directly)

### 🥈 Approach 12: Hybrid Batch Worker with Dedicated Pipeline

**Why it's runner-up:**
- Same clean user API as #14
- Full explicit control over batch pipeline composition
- No adapter indirection — direct dispatch
- But: requires a `ComposeBatch` method and a forked pipeline composition path — this is the real cost. The associated shell middleware classes are new purpose-built classes (which is fine), but they exist only because the pipeline is forked. If the fork were avoided, those classes wouldn't be needed.

**Choose this if:** You want maximum explicitness and zero "magic" — every component is purpose-built for batches, no adapter/marker patterns, and you accept the forked pipeline composition as a trade-off.

### 🥉 Approach 13: `IConsumer<MessageBatch<T>>` (No IBatchConsumer)

**Why it's third:**
- Zero forked pipeline composition (same advantage as #14)
- Even fewer new files (no adapter class)
- But: user writes `IConsumer<MessageBatch<OrderCreated>>` instead of `IBatchConsumer<OrderCreated>` — less clear API
- Registration detection via reflection is fragile

**Choose this if:** You don't mind the longer type name and prefer even fewer abstractions.

## Recommendation

**Approach 14 (IBatchConsumer<T> as Adapter to IConsumer<MessageBatch<T>>)** is the recommended path forward.

It delivers:
1. ✅ User receives batch of messages in their consumer
2. ✅ Batch triggered by max size OR max timeout (whichever first)
3. ✅ Exception → no offsets committed → entire batch redelivered
4. ✅ User responsible for idempotent batch processing
5. ✅ Clean, discoverable, compile-time safe API
6. ✅ Full middleware pipeline support (error, retry, tracing, metrics, observers)
7. ✅ Per-item dead-lettering preserving original message metadata
8. ✅ Zero forked pipeline composition — no `ComposeBatch`, no parallel middleware shells

### Implementation Order

1. New abstractions: `IBatchConsumer<T>`, `BatchConsumeContext<T>`, `BatchItem<T>`, `MessageBatch<T>`
2. Adapter: `BatchConsumerAdapter<TValue, TConcrete>`
3. Config: `BatchConfigBuilder`, `BatchConfig`
4. Registration: `KafkaConsumerGroupBuilder.Batch()`, `AddBatchConsumer<T>()`
5. Worker: batch accumulation loop in `ConsumerWorker<TKey, TValue>`
6. Error middleware: `IBatchMessage` check for per-item DLQ routing
7. Registration wiring: `KafkaBuilder.RegisterConsumerGroup` batch path
8. Tests: unit + integration

## Industry Alignment

| Framework | Batch Pattern | Emit Alignment |
|-----------|---------------|---------------|
| MassTransit | `IConsumer<Batch<T>>` | Approach 14 adapter + `IBatchConsumer<T>` surface |
| KafkaFlow | `IMessageBatchHandler<T>` | Separate interface, same as our `IBatchConsumer<T>` |
| Spring Kafka | `@KafkaListener(batch=true)` + `List<T>` param | Attribute-based — not applicable |
| Confluent .NET | Manual `ConsumeBatch` loop | Our worker loop internals |
| Azure Service Bus | `ServiceBusProcessor.ProcessMessageBatchAsync` | Separate handler for batches |
| AWS SQS | `ReceiveMessageRequest.MaxNumberOfMessages` | Transport-level batching |

The industry consensus is: **a separate handler/interface for batches, with the framework handling accumulation**. Approach 14 aligns with MassTransit (closest ecosystem match) and KafkaFlow.

---

## Decision Critic Assessment

### Assessment of the Comparison Matrix
The matrix is well-constructed and covers the right dimensions (pipeline compat, code duplication, API clarity, offset safety). The verdicts are consistent with the detailed analysis in each approach's file.

### Hidden Assumptions in Recommendation
- **Assumes "minimal code changes" is a priority.** The recommendation and its runner-up notes cite file counts as ranking factors. The user has not stated that minimal code changes is a goal — clean architecture, no code duplication, no maintainability problems, and reliability are the stated priorities. Code volume is irrelevant if the design is correct. The recommendation is still sound, but not because it involves fewer files.
- Assumes the adapter pattern has no long-term maintenance cost. In practice, adapters can become "god objects" if they accumulate forwarding logic (retry attempt, transaction context, future properties).

### Risks in Recommendation
- **"Zero forked pipeline composition" is precise; "zero middleware duplication" was not**: The `IBatchMessage` check in error middleware is an extension to an existing middleware class to handle a new case — this is explicitly the kind of change that is acceptable. It is not duplication. The claim is correct when stated as "zero forked pipeline composition."
- **Implementation order risk**: The recommended order (abstractions → adapter → config → registration → worker → error middleware → tests) puts tests last. Tests should be written alongside each component, not as a final step.

### Verdict Justification
The recommendation of Approach 14 is **well-justified by the evidence across all 19 alternative approaches**. The comparison with Approach 12 (runner-up) is fair and quantitative. The industry alignment table provides external validation. The implementation order is reasonable (though tests should be parallel, not final).

### What's Missing
- **Migration guide**: How do existing single-consumer users adopt batch consumption? Is there a migration path that doesn't require rewriting consumers?
- **Performance considerations**: No approach discusses the memory overhead of holding N deserialized messages in memory during accumulation. For large messages or large batches, this could be significant.
- **Configuration validation**: What happens if `MaxSize = 0` or `Timeout = TimeSpan.Zero`? The `BatchConfigBuilder` needs validation, which should be called out.

**Overall Assessment: The recommendation is sound.** Approach 14 is the correct choice. The only actionable improvements are: (1) test alongside implementation, not after, (2) add configuration validation, (3) document the migration path.
