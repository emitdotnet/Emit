namespace Emit.Kafka.Tracing;

using System.Diagnostics;
using Emit.Abstractions.Tracing;

/// <summary>
/// Adds the Kafka attributes of a message being produced to its <c>create</c> span: its key, when it is a string (a
/// key of another type has no canonical string form to report), and whether it is a tombstone, a message without a
/// value.
/// </summary>
internal sealed class KafkaActivityEnricher : IActivityEnricher
{
    /// <inheritdoc />
    public void Enrich(Activity activity, EnrichmentContext context)
    {
        if (context.Phase != EnrichmentPhases.Produce)
        {
            return;
        }

        if (context.MessageContext.TryGetPayload<KafkaMessageKey<string>>() is { } key)
        {
            activity.SetTag(KafkaActivityTagNames.MessageKey, key.Key);
        }

        if (context.MessageContext.Message is null)
        {
            activity.SetTag(KafkaActivityTagNames.MessageTombstone, true);
        }
    }
}
