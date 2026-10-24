namespace Emit.Kafka.Tracing;

using System.Diagnostics;
using Emit.Abstractions.Tracing;

/// <summary>
/// Adds the Kafka attributes of a message being produced to its <c>create</c> span: its key, when it is a string (a
/// key of another type has no canonical string form to report), and whether it is a tombstone.
/// </summary>
internal sealed class KafkaActivityEnricher : IActivityEnricher
{
    /// <inheritdoc />
    public void Enrich(Activity activity, EnrichmentContext context)
    {
        if (context.Phase != EnrichmentPhases.Produce || context.MessageContext.TryGetPayload<KafkaProducePayload>() is not { } payload)
        {
            return;
        }

        if (payload.Key is string key)
        {
            activity.SetTag(KafkaActivityTagNames.MessageKey, key);
        }

        if (payload.IsTombstone)
        {
            activity.SetTag(KafkaActivityTagNames.MessageTombstone, true);
        }
    }
}
