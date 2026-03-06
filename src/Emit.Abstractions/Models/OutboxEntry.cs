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
    /// The type is provider-specific (ObjectId for MongoDB, UUID for PostgreSQL).
    /// </remarks>
    public object? Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the outbox provider (e.g., "kafka").
    /// </summary>
    /// <remarks>
    /// Each provider exposes a well-known ID. Used to dispatch entries to the correct
    /// provider during processing.
    /// </remarks>
    public required string ProviderId { get; set; }

    /// <summary>
    /// Gets or sets the key name used during registration.
    /// </summary>
    /// <remarks>
    /// A well-known sentinel value (e.g., "__default__") indicates non-keyed registration.
    /// Used by the worker to resolve the correct real producer.
    /// </remarks>
    public required string RegistrationKey { get; set; }

    /// <summary>
    /// Gets or sets the group key that determines sequential processing order.
    /// </summary>
    /// <remarks>
    /// Entries in the same group are processed strictly in sequence.
    /// Format: "{providerId}:{destination}" (e.g., "kafka:orders"). Also used as the
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
    /// Gets or sets the opaque binary blob containing provider-specific data.
    /// </summary>
    /// <remarks>
    /// Only the originating provider knows how to serialize and deserialize this.
    /// </remarks>
    public required byte[] Payload { get; set; }

    /// <summary>
    /// Gets or sets arbitrary key-value metadata populated by the provider at enqueue time.
    /// </summary>
    /// <remarks>
    /// Not used by the core engine for processing logic — purely for observability, metrics,
    /// and dashboards (e.g., destination, valueType).
    /// </remarks>
    public Dictionary<string, string> Properties { get; set; } = [];

    /// <summary>
    /// Gets or sets the W3C trace context for distributed tracing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contains the traceparent value (version-traceid-spanid-flags) from the Activity
    /// that enqueued this entry. Automatically populated when tracing is enabled.
    /// </para>
    /// <para>
    /// Format: "00-{trace-id}-{span-id}-{flags}" (55 characters)
    /// </para>
    /// <para>
    /// Example: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
    /// </para>
    /// </remarks>
    public string? TraceParent { get; set; }

    /// <summary>
    /// Gets or sets the W3C tracestate for vendor-specific trace data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Preserves vendor extensions (Application Insights, Datadog, etc.) across the async boundary.
    /// Automatically populated when tracing is enabled and the Activity has tracestate.
    /// </para>
    /// <para>
    /// Format: comma-separated vendor entries, max 2048 characters.
    /// </para>
    /// <para>
    /// Example: "congo=t61rcWkgMzE,rojo=00f067aa0ba902b7"
    /// </para>
    /// </remarks>
    public string? TraceState { get; set; }
}
