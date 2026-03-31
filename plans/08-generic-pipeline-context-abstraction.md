# Approach 8: Abstract Context Type — Pipeline Generalized to `MessageContext`

## Concept

Refactor the entire middleware pipeline to be generic on `MessageContext` (the abstract base) instead of `ConsumeContext<TValue>`. This allows both `ConsumeContext<TValue>` and `BatchConsumeContext<TValue>` to flow through the same pipeline. Middleware operates on the base type; terminals downcast to the specific type.

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
            await ProcessAsync(item.Message, ct);
        }
    }
}
```

## Internal Changes

### Make Middleware Pipeline Operate on `MessageContext`

```csharp
// Before:
public interface IMiddleware<TContext> where TContext : MessageContext
{
    Task InvokeAsync(TContext context, IMiddlewarePipeline<TContext> next);
}

// After: Keep as-is, but compose pipelines on MessageContext instead of ConsumeContext<T>
// The consumer pipeline becomes: IMiddlewarePipeline<MessageContext>
```

Wait — this doesn't work. The middleware needs the typed context to access `Message`. Cross-cutting middleware like error handling, tracing, and metrics DON'T need the message payload, but validation and handler invocation DO.

### Split: Cross-Cutting vs Typed Middleware

```
IMiddleware<MessageContext>          — error, tracing, metrics, observers (don't need T)
IMiddleware<ConsumeContext<TValue>>  — validation, handler invocation (need T)
IMiddleware<BatchConsumeContext<T>>  — batch handler invocation (need batch)
```

Pipeline composition:
```
Error<MC> → Observer<MC> → Tracing<MC> → Metrics<MC> → Typed branch:
    ├─ Single: Validation<CC<T>> → Retry<CC<T>> → Handler<CC<T>>
    └─ Batch:  BatchHandler<BCC<T>>
```

The pipeline is a single typed chain — it cannot natively branch. However, this does not rule out a bridge/adapter middleware that accepts `ConsumeContext<T>`, projects a `BatchConsumeContext<T>` internally, and forwards to a nested pipeline of a different type. Such a middleware would be a single link in the outer chain while managing its own typed inner chain. The chain type uniformity constraint applies to a given pipeline instance, not to the system as a whole.

### Alternative: `BatchConsumeContext<T>` inherits from `ConsumeContext<T>`

```csharp
public class BatchConsumeContext<T> : ConsumeContext<T>
{
    public required IReadOnlyList<BatchItem<T>> Items { get; init; }
    // Message = default(T) or first item? This is weird.
}
```

This allows the pipeline typed on `ConsumeContext<T>` to accept `BatchConsumeContext<T>`. Middleware that accesses `context.Message` sees the first item (or null). The terminal (handler invoker) checks if it's a batch context and downcasts.

**Problem**: `ConsumeContext<T>.Message` is `required` — it must have a value. For a batch, what is `Message`? First item? That's misleading. `default(T)`? Breaks middleware that reads it.

## Pros

- Pipeline type stays `IMiddlewarePipeline<ConsumeContext<T>>`
- Middleware that doesn't inspect `Message` works unchanged
- Terminal can downcast to determine batch vs single

## Cons

- **`Message` property is meaningless for batches**: The `required T Message` on `ConsumeContext<T>` has no good value for a batch
- **Violates Liskov Substitution Principle**: `BatchConsumeContext<T>` IS-A `ConsumeContext<T>` but doesn't behave like one (its `Message` is not the message being processed)
- **Breaking change to MessageContext**: If we change `Message` to nullable or optional, it breaks every consumer
- **Middleware that inspects Message breaks**: Validation reads `Message`, filters read `Message`, any custom middleware that reads `Message` would get wrong data
- **Fragile downcasting**: Terminal checks `if (context is BatchConsumeContext<T> batch)` — stringly typed dispatch

## Verdict

**Architecturally unsound.** Inheriting from `ConsumeContext<T>` while violating its contract (`Message` is not the real message) creates subtle bugs. The Liskov violation means existing middleware silently misbehaves rather than failing loudly.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes the pipeline MUST be a single typed chain. In reality, .NET's generic type system could support variance-based dispatch (covariant interfaces), but `IMiddleware<TContext>` is invariant on `TContext`. This is a real constraint, not an assumption, but the document doesn't explore whether making the interface covariant on `TContext` could work (it can't, because `TContext` appears in both input and output positions — but this should be explicitly stated).
- Assumes `required T Message` on `ConsumeContext<T>` is immutable. If `Message` were changed to `virtual` or made optional, the LSP violation weakens. However, this would be a much larger breaking change than the batch feature itself.

### Risks Not Discussed
- **Future type hierarchy debt**: If `BatchConsumeContext<T>` inherits from `ConsumeContext<T>`, every future change to `ConsumeContext<T>` (new required properties, sealed modifiers, etc.) must consider the batch subclass. This creates invisible coupling.
- **Serialization/logging**: Libraries that serialize or log `ConsumeContext<T>` (e.g., structured logging, distributed tracing) would see `Message = default` or `Message = firstItem`, producing misleading telemetry.

### Verdict Justification
The verdict ("architecturally unsound") is **correct and well-argued**. The LSP violation is real — `BatchConsumeContext<T>` does not satisfy the behavioral contract of `ConsumeContext<T>` (which guarantees `Message` is the message being processed). The document correctly identifies that this creates subtle bugs rather than loud failures, which is the worst kind of violation.

### Blind Spots
- Doesn't explore the "pipeline branching" idea from the middle of the document. The split between `IMiddleware<MessageContext>` (cross-cutting) and `IMiddleware<ConsumeContext<T>>` (typed) is actually the kernel of what Approach 10 does with its separate batch pipeline. This connection should be drawn.
- Doesn't mention that this approach would make the `where TContext : MessageContext` constraint meaningless — if we're downcasting at the terminal anyway, the generic constraint provides false safety.

### Strongest Argument
The document's exploration of the "split cross-cutting vs typed middleware" idea is valuable, even though the approach itself fails. This analysis informs why shell duplication is something to avoid — not accept — and why approaches like 13/14 sidestep the problem entirely by making `MessageBatch<T>` the `TMessage`. Approach 10's `ComposeBatch` fork is the canonical example of what this analysis warns against: accepting the duplication cost rather than resolving the type mismatch upstream.

**Overall: 2/10** — Correctly rejected. The LSP violation is fundamental, not a detail that can be worked around.
