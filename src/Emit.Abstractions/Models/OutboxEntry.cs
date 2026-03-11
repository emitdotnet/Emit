namespace Emit.Models;

/// <summary>
/// Represents an entry in the transactional outbox.
/// </summary>
/// <remarks>
/// <para>
/// The outbox entry model is provider-agnostic. The persistence providers implement
/// the actual storage, but the schema is defined by the core library.
/// </para>
/// <para>
/// An entry's presence in the outbox means it is pending. On successful processing,
/// the entry is deleted. On failure, it stays and is retried on the next poll cycle.
/// </para>
/// </remarks>
public sealed class OutboxEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for the entry.
    /// </summary>
    /// <remarks>
    /// The type is provider-specific.
    /// </remarks>
    public object? Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the target system (e.g., "kafka").
    /// </summary>
    /// <remarks>
    /// Each provider exposes a well-known ID. Used to dispatch entries to the correct
    /// provider during processing.
    /// </remarks>
    public required string SystemId { get; set; }

    /// <summary>
    /// Gets or sets the destination address as a URI string.
    /// </summary>
    /// <remarks>
    /// Format: <c>{scheme}://{host}:{port}/{entityName}</c>.
    /// Parsed via <see cref="Emit.Abstractions.EmitEndpointAddress"/> at delivery time to extract the target entity.
    /// </remarks>
    public required string Destination { get; set; }

    /// <summary>
    /// Gets or sets the conversation identifier for causal chain tracking.
    /// </summary>
    /// <remarks>
    /// Links related messages across a causal chain (e.g., command → event → reaction).
    /// Null if not participating in a conversation.
    /// </remarks>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Gets or sets the group key that determines sequential processing order.
    /// </summary>
    /// <remarks>
    /// Entries in the same group are processed strictly in sequence.
    /// Format is provider-specific and opaque to the core engine. Also used as the
    /// sharding key for MongoDB.
    /// </remarks>
    public required string GroupKey { get; set; }

    /// <summary>
    /// Gets or sets the monotonically increasing sequence number within a group.
    /// </summary>
    /// <remarks>
    /// Assigned by the repository during enqueue. Guarantees strict ordering within a group.
    /// </remarks>
    public long Sequence { get; set; }

    /// <summary>
    /// Gets or sets when the entry was added to the outbox (UTC).
    /// </summary>
    public DateTime EnqueuedAt { get; set; }

    /// <summary>
    /// Gets or sets the serialized message value bytes.
    /// </summary>
    /// <remarks>
    /// Null for tombstone messages (e.g., Kafka log compaction deletes).
    /// </remarks>
    public byte[]? Body { get; set; }

    /// <summary>
    /// Gets or sets the transport headers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Preserves header semantics of the target transport (e.g., Kafka allows duplicate keys
    /// and ordering is significant). Includes W3C trace context headers (traceparent, tracestate)
    /// when distributed tracing is enabled.
    /// </para>
    /// <para>
    /// Never null — empty when no headers are present.
    /// </para>
    /// </remarks>
    public List<KeyValuePair<string, byte[]>> Headers { get; set; } = [];

    /// <summary>
    /// Gets or sets provider-specific metadata for observability and delivery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contains provider-defined key-value pairs such as topic name, message type names,
    /// and the serialized message key. Not used by the core engine for processing logic.
    /// </para>
    /// <para>
    /// Never null — empty when no properties are present.
    /// </para>
    /// </remarks>
    public Dictionary<string, string> Properties { get; set; } = [];
}
