namespace Emit.Pipeline;

using Emit.Abstractions.Pipeline;

/// <summary>
/// Extension methods for silently dropping null-payload messages before the consumer handler sees them.
/// </summary>
public static class NullPayloadFilterExtensions
{
    /// <summary>
    /// Registers a filter that silently drops messages with a <see langword="null"/> payload.
    /// Works uniformly for both single-message and batch consumers. Dropped messages
    /// produce no dead-letter entry and throw no exception; offsets advance normally.
    /// </summary>
    /// <typeparam name="TMessage">The message type. Constrained to reference types because
    /// <see langword="null"/> is only meaningful for reference-typed deserializer results.</typeparam>
    /// <param name="builder">The consumer group builder.</param>
    /// <returns>The builder for continued chaining.</returns>
    public static IConsumerGroupConfigurable<TMessage> SkipNullPayloads<TMessage>(
        this IConsumerGroupConfigurable<TMessage> builder)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Filter(static (ctx, _) => ValueTask.FromResult(ctx.Message is not null));
        return builder;
    }
}
