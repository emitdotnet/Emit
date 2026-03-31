# Approach 15: Spring Kafka-Style — Attribute-Based Batch Mode Toggle

## Inspiration

Spring Kafka's `@KafkaListener` can switch between single and batch mode via configuration:
```java
@KafkaListener(topics = "orders", batch = "true")
public void listen(List<OrderCreated> orders) { ... }
```

The same listener method signature declares batch intent.

## User Experience

```csharp
// Attribute declares batch mode
[BatchConsumer(MaxSize = 100, MaxTimeout = "00:00:05")]
public class OrderConsumer : IConsumer<OrderCreated>
{
    // When decorated with [BatchConsumer], the framework collects messages
    // and calls ConsumeAsync once per batch.
    // The batch is accessible via context.TryGetPayload<BatchPayload<OrderCreated>>()
    public async Task ConsumeAsync(ConsumeContext<OrderCreated> context, CancellationToken ct)
    {
        var batch = context.GetRequiredPayload<BatchPayload<OrderCreated>>();
        foreach (var item in batch.Items)
        {
            await ProcessAsync(item, ct);
        }
    }
}

// Or with a convention: if the consumer accepts IReadOnlyList<ConsumeContext<T>>,
// it's automatically batch:
public class OrderConsumer : IConsumer<OrderCreated>
{
    public async Task ConsumeAsync(ConsumeContext<OrderCreated> context, CancellationToken ct)
    {
        // Single message path
    }

    // Convention-based batch method
    public async Task ConsumeBatchAsync(
        IReadOnlyList<ConsumeContext<OrderCreated>> batch,
        CancellationToken ct)
    {
        foreach (var ctx in batch) { ... }
    }
}
```

## Analysis

### Problems

1. **Attribute-based configuration**: Emit uses fluent builder configuration, not attributes. Mixing paradigms is inconsistent.
2. **Payload bag pattern (TryGetPayload)**: Makes the batch data optional and discoverable only at runtime. Same silent-data-loss risk as Approach 7.
3. **Convention-based method detection**: Requires reflection to discover `ConsumeBatchAsync` method. Fragile, not compile-time safe.
4. **The IConsumer<T> interface has one method**: `ConsumeAsync(ConsumeContext<T>, CancellationToken)`. Adding a second method changes the interface contract.
5. **context.Message is still a single item**: Same issue as Approach 7/8 — the context carries one message but the consumer needs many.

### Why This Doesn't Fit Emit

Spring Kafka's approach works because:
- Java uses runtime reflection extensively for DI and method invocation
- `@KafkaListener` is annotation-based — the framework inspects method signatures at startup
- Spring's container model supports method-level injection with complex parameter matching

Emit is a strongly-typed .NET library with:
- Compile-time generic type safety
- Interface-based contracts (not annotation-based)
- A middleware pipeline typed on the context generic parameter

The attribute/convention approach sacrifices all of Emit's type safety for no gain.

## Verdict

**Wrong paradigm for Emit.** Attribute/convention-based approaches fit annotation-driven frameworks (Spring, ASP.NET MVC controllers) but not interface-driven middleware pipelines. Emit's strength is compile-time type safety — this approach undermines it.

---

## Decision Critic Assessment

### Hidden Assumptions
- Assumes attribute-based configuration is fundamentally incompatible with Emit. While it's true Emit favors fluent builders, ASP.NET Core successfully mixes attributes (`[ApiController]`, `[Route]`) with builder-based configuration. The assumption should be "attribute-based is inconsistent with Emit's patterns," not "impossible."
- Assumes Spring's approach requires runtime reflection. In modern .NET, source generators could achieve compile-time attribute processing, avoiding the reflection fragility.

### Risks Not Discussed
- **Future API design debt**: If Emit later wants attribute-based configuration for other features (e.g., `[RateLimit]`, `[Retry]`), rejecting attributes now doesn't prevent the question from resurfacing.
- **Ergonomics**: While type safety matters, the Spring Kafka developer experience (one attribute, one method parameter change) is genuinely simpler than a separate interface + builder configuration. The document dismisses the ergonomic advantage too quickly.

### Verdict Justification
The verdict ("wrong paradigm") is **correct for Emit's current architecture**. The middleware pipeline's generic typing makes attribute-based configuration awkward — you'd need to bridge attributes to pipeline type selection, which is unnecessary complexity. However, the dismissal is slightly too absolute; the approach isn't inherently wrong, just wrong for THIS framework.

### Blind Spots
- Doesn't mention that .NET 8+ has `[GeneratedRegex]` and other source-generator patterns that make attribute-based code generation mainstream. The "reflection is fragile" argument is becoming less relevant.
- Doesn't acknowledge that the `batch = "true"` toggle concept (separate from the attribute mechanism) is valid. The idea of a simple boolean toggle on the consumer group builder is used in Approaches 10-14.

### Strongest Argument
The document correctly identifies that Emit's type-safe generic pipeline is the differentiator. Spring Kafka can use `List<T>` parameters because Java's type erasure makes the framework agnostic to collection types. C#'s reified generics make the pipeline type-aware, which is a strength worth preserving.

**Overall: 2/10** — Correctly rejected for Emit, though the dismissal could acknowledge the ergonomic trade-off more honestly.
