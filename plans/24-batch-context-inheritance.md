# Plan 24: Should `BatchConsumeContext<T>` Inherit from `ConsumeContext<T>`?

## Context

Approach 14 (recommended) defines `BatchConsumeContext<T>` as extending `MessageContext` directly:

```csharp
public class BatchConsumeContext<T> : MessageContext
{
    public required IReadOnlyList<BatchItem<T>> Items { get; init; }
    public required TransportContext TransportContext { get; init; }
    public int RetryAttempt { get; set; }
}
```

The adapter receives `ConsumeContext<MessageBatch<T>>` from the pipeline and constructs a `BatchConsumeContext<T>` to pass to the user's `IBatchConsumer<T>`. The question: should `BatchConsumeContext<T>` inherit from some form of `ConsumeContext` instead of `MessageContext` directly?

## Property Inventory

The full property chain that `ConsumeContext<T>` carries:

| Property | Source | Type | Semantic for single message |
|----------|--------|------|-----------------------------|
| `Message` | `MessageContext<T>` | `T` | The deserialized message payload |
| `CancellationToken` | `MessageContext` | `CancellationToken` | Processing cancellation |
| `Services` | `MessageContext` | `IServiceProvider` | Scoped DI container |
| `Features` | `MessageContext` | `IFeatureCollection` | Extensible feature bag |
| `MessageId` | `MessageContext` | `string` | Unique message identifier |
| `Timestamp` | `MessageContext` | `DateTimeOffset` | When message was received |
| `DestinationAddress` | `MessageContext` | `Uri?` | Topic URI |
| `SourceAddress` | `MessageContext` | `Uri?` | Sender address |
| `TransportContext` | `ConsumeContext<T>` | `TransportContext` | Raw bytes, headers, provider metadata |
| `Headers` | `ConsumeContext<T>` | delegated from TransportContext | Message headers |
| `RetryAttempt` | `ConsumeContext<T>` | `int` | Current retry count |
| `Transaction` | `ConsumeContext<T>` | `ITransactionContext?` | Transaction context |

## Three Inheritance Options

### Option A: `BatchConsumeContext<T> : ConsumeContext<T>`

`Message` would be type `T` -- a single message. But the context represents a batch.

**Ghost properties:**
- **`Message` (type `T`)** -- FATAL. The context carries N items, not one. What value goes here? First item? Null? Any choice is misleading. This is the textbook ghost property.
- **`Headers`** -- delegates to `TransportContext.Headers`. For a batch, which message's headers? Same problem.

**Verdict: Introduces code smell.** The `Message` property is meaningless and misleading. A batch context is not a single-message context with extras bolted on.

### Option B: `BatchConsumeContext<T> : ConsumeContext<MessageBatch<T>>`

`Message` would be type `MessageBatch<T>` -- the batch itself. This is the pipeline's internal type.

**Ghost property audit:**

| Property | Ghost? | Rationale |
|----------|--------|-----------|
| `Message` (`MessageBatch<T>`) | No | The batch IS the message payload. Semantically correct. |
| `CancellationToken` | No | Applies to the whole batch operation. |
| `Services` | No | Scoped DI for the batch processing scope. |
| `Features` | No | Extensible bag, works for batches. |
| `MessageId` | No | Synthetic ID for the batch processing operation (already set by the worker for the `ConsumeContext<MessageBatch<T>>`). |
| `Timestamp` | No | When the batch was assembled/received. |
| `DestinationAddress` | No | Same topic for all items in the batch. |
| `SourceAddress` | No | Same source for all items. |
| `TransportContext` | **Synthetic** | No single Kafka message corresponds to the batch. Must be a synthetic `TransportContext` with `RawKey = null`, `RawValue = null`, and aggregated or empty headers. Already required by Approach 14 since the adapter must set `TransportContext` on the current `BatchConsumeContext`. |
| `Headers` | **Synthetic** | Delegates to the synthetic `TransportContext`. Empty or aggregated. Same situation as current plan. |
| `RetryAttempt` | No | Retry applies to the whole batch. |
| `Transaction` | No | Transaction wraps the whole batch operation. |

**No ghost properties.** `TransportContext` is synthetic but not meaningless -- it represents the batch-level transport metadata. This is the same synthetic context that already must exist in Approach 14 (the adapter sets `TransportContext` on `BatchConsumeContext` today).

**Redundancy question:** `context.Message` returns the `MessageBatch<T>`, and `context.Items` would return the same list of `BatchItem<T>`. Is `Items` redundant?
- `context.Message` is type `MessageBatch<T>` (which implements `IReadOnlyList<BatchItem<T>>`)
- `context.Items` is type `IReadOnlyList<BatchItem<T>>`
- They expose the same data. `Items` is a convenience alias. This is not redundancy -- it is a named accessor that communicates intent. The user writes `context.Items` in their consumer, which reads better than `context.Message`. But `context.Message` still works and makes sense ("the message is the batch").

### Option C: `BatchConsumeContext<T> : MessageContext` (current plan, no inheritance)

No inheritance from `ConsumeContext`. `BatchConsumeContext<T>` independently declares `TransportContext`, `RetryAttempt`, and `Items`.

**Ghost property audit:** N/A -- no inherited properties to evaluate.

**Trade-offs:**
- Must manually duplicate `TransportContext`, `Headers`, `RetryAttempt`, `Transaction` declarations
- The adapter must copy all properties from `ConsumeContext<MessageBatch<T>>` to `BatchConsumeContext<T>` field by field
- Extension methods or utilities written for `ConsumeContext<T>` do not work on `BatchConsumeContext<T>`
- If `ConsumeContext` gains a new property (e.g., `CorrelationId`), the adapter must be updated to forward it, and `BatchConsumeContext` must add a matching property. This is a maintenance coupling that inheritance would eliminate.

## Impact on the Adapter Pattern

### With Option B (`BatchConsumeContext<T> : ConsumeContext<MessageBatch<T>>`)

The adapter currently does this:

```csharp
// Current plan: construct BatchConsumeContext from scratch
var batchContext = new BatchConsumeContext<TValue>
{
    MessageId = context.MessageId,
    Timestamp = context.Timestamp,
    CancellationToken = context.CancellationToken,
    Services = context.Services,
    Items = context.Message.Items,
    TransportContext = context.TransportContext,
    RetryAttempt = context.RetryAttempt,
};
```

With inheritance, `BatchConsumeContext<T>` IS-A `ConsumeContext<MessageBatch<T>>`. The adapter receives `ConsumeContext<MessageBatch<T>>` from the pipeline. Two sub-options:

**B1: Adapter still creates a new `BatchConsumeContext<T>`**, copying properties from the pipeline context. Same as today but the type inherits from `ConsumeContext<MessageBatch<T>>` instead of `MessageContext`. The adapter pattern stays. Benefit: `BatchConsumeContext<T>` is assignment-compatible with `ConsumeContext<MessageBatch<T>>`, so any code accepting the latter also accepts the former.

**B2: The pipeline runs on `BatchConsumeContext<T>` directly** (since it IS-A `ConsumeContext<MessageBatch<T>>`). The worker constructs a `BatchConsumeContext<T>` instead of a `ConsumeContext<MessageBatch<T>>`, and the adapter just downcasts. This eliminates the property-copying step entirely. However, this means the pipeline must be built for `BatchConsumeContext<T>` specifically, which reintroduces a type distinction at the pipeline level. **Not recommended** -- it defeats the purpose of the adapter pattern.

**B1 is the natural choice.** The adapter pattern stays, but with less property-copying fragility because inherited properties come from the base.

### Does inheritance eliminate the adapter?

No. The adapter serves two purposes:
1. Convert `ConsumeContext<MessageBatch<T>>` to `BatchConsumeContext<T>` (the type conversion)
2. Resolve `IBatchConsumer<T>` from DI and invoke it (the dispatch)

Even with inheritance, purpose (2) remains. The adapter is still needed.

## Extension Method Compatibility

There are currently no extension methods on `ConsumeContext<T>` in the codebase. However:

- All middleware is typed on `ConsumeContext<T>` via `IMiddleware<ConsumeContext<T>>`. These already work with batches through generics (`ConsumeContext<MessageBatch<T>>`). Inheritance does not change this.
- If extension methods are added to `ConsumeContext<T>` in the future, with Option B they would work on `BatchConsumeContext<T>` (since it IS-A `ConsumeContext<MessageBatch<T>>`). With Option C they would not.
- User-written middleware typed on `ConsumeContext<T>` works via generics regardless of inheritance choice. The pipeline type is `ConsumeContext<MessageBatch<T>>`, not `ConsumeContext<T>`.

## Decision: Trade-off Summary

| Criterion | Option A (`ConsumeContext<T>`) | Option B (`ConsumeContext<MessageBatch<T>>`) | Option C (`MessageContext`, current) |
|-----------|-------------------------------|----------------------------------------------|--------------------------------------|
| Ghost properties | `Message` is fatal ghost | None | N/A |
| Property duplication | N/A | None -- inherited | Manual duplication of ~4 properties |
| Adapter fragility | N/A | Low -- fewer fields to copy | Higher -- every new `ConsumeContext` property must be forwarded |
| `context.Message` meaning | Single item (wrong) | The batch (correct) | No `Message` property |
| `context.Items` redundancy | N/A | Convenience alias for `Message.Items` | Primary accessor |
| Extension method compat | Misleading (single-item extensions on batch) | Works correctly | Incompatible |
| Conceptual clarity | Batch pretends to be single message | Batch IS-A consume context whose message is a batch | Batch is a separate thing |
| Type hierarchy depth | `MessageContext` -> `MessageContext<T>` -> `ConsumeContext<T>` -> `BatchConsumeContext<T>` | Same depth | `MessageContext` -> `BatchConsumeContext<T>` (shallower) |

## Escalation

This is a user-preference decision with two valid options:

**Option B** is technically clean -- no ghost properties, less adapter fragility, and the `Message` property is semantically correct ("the message is the batch"). The cost is that `context.Message` and `context.Items` expose the same data from two paths, which some may find redundant.

**Option C** (current plan) is simpler in type hierarchy -- `BatchConsumeContext<T>` is its own thing without inheriting pipeline machinery. The cost is manual property duplication and adapter forwarding fragility.

**Option A is ruled out.** The `Message` property ghost is a clear code smell.
