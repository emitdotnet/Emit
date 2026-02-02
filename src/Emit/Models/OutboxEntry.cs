namespace Emit.Models;

/// <summary>
/// Represents an entry in the transactional outbox.
/// </summary>
/// <remarks>
/// The outbox entry model is provider-agnostic. The persistence providers implement
/// the actual storage, but the schema is defined by the core library.
/// </remarks>
public sealed class OutboxEntry
{
    /// <summary>
    /// The default maximum number of attempts to retain in the <see cref="Attempts"/> collection.
    /// </summary>
    public const int DefaultMaxAttempts = 10;

    private readonly List<OutboxAttempt> attempts = [];

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
    /// For Kafka: cluster_identifier:topic_name. This field is also used as the
    /// sharding key for MongoDB.
    /// </remarks>
    public required string GroupKey { get; set; }

    /// <summary>
    /// Gets or sets the monotonically increasing sequence number within a group.
    /// </summary>
    /// <remarks>
    /// Assigned at enqueue time. Guarantees strict ordering within a group.
    /// </remarks>
    public long Sequence { get; set; }

    /// <summary>
    /// Gets or sets the processing status of the entry.
    /// </summary>
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>
    /// Gets or sets when the entry was added to the outbox (UTC).
    /// </summary>
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the entry was successfully processed (UTC).
    /// </summary>
    /// <remarks>
    /// CompletedAt - EnqueuedAt provides time-in-outbox metrics.
    /// </remarks>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of times processing has been attempted.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets or sets when the last processing attempt occurred (UTC).
    /// </summary>
    /// <remarks>
    /// Used by retry backoff calculations.
    /// </remarks>
    public DateTime? LastAttemptedAt { get; set; }

    /// <summary>
    /// Gets or sets the latest error message.
    /// </summary>
    /// <remarks>
    /// Set on failure, cleared (nulled) on success. Provides quick visibility into
    /// why an entry is stuck without scanning the attempts list.
    /// </remarks>
    public string? LatestError { get; set; }

    /// <summary>
    /// Gets the list of failed processing attempts.
    /// </summary>
    /// <remarks>
    /// Capped to a configurable maximum. Not populated on success to avoid bloat.
    /// </remarks>
    public IReadOnlyList<OutboxAttempt> Attempts => attempts;

    /// <summary>
    /// Gets or sets the opaque binary blob containing provider-specific data.
    /// </summary>
    /// <remarks>
    /// Only the provider knows how to serialize/deserialize this. Serialized using MessagePack.
    /// For Kafka: contains key bytes, value bytes, headers, topic name, partition, etc.
    /// </remarks>
    public required byte[] Payload { get; set; }

    /// <summary>
    /// Gets or sets arbitrary key-value metadata populated by the provider at enqueue time.
    /// </summary>
    /// <remarks>
    /// Not used by the core engine for processing logic - purely for observability, metrics,
    /// and dashboards. For Kafka: topic, cluster, valueType, etc.
    /// </remarks>
    public Dictionary<string, string> Properties { get; set; } = [];

    /// <summary>
    /// Gets or sets an optional trace/correlation ID for end-to-end tracing.
    /// </summary>
    /// <remarks>
    /// Allows users to trace an outbox entry back to the business operation that created it.
    /// </remarks>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Adds an attempt to the attempts list, maintaining the maximum capacity.
    /// </summary>
    /// <param name="attempt">The attempt to add.</param>
    /// <param name="maxAttempts">The maximum number of attempts to retain.</param>
    public void AddAttempt(OutboxAttempt attempt, int maxAttempts = DefaultMaxAttempts)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        attempts.Add(attempt);

        // Trim from the front to keep the most recent attempts
        while (attempts.Count > maxAttempts)
        {
            attempts.RemoveAt(0);
        }
    }

    /// <summary>
    /// Marks the entry as completed with the current UTC time.
    /// </summary>
    public void MarkCompleted()
    {
        Status = OutboxStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        LatestError = null;
    }

    /// <summary>
    /// Marks the entry as failed with the specified error.
    /// </summary>
    /// <param name="reason">Categorized reason for the failure.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="maxAttempts">The maximum number of attempts to retain.</param>
    public void MarkFailed(string reason, Exception exception, int maxAttempts = DefaultMaxAttempts)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = OutboxStatus.Failed;
        RetryCount++;
        LastAttemptedAt = DateTime.UtcNow;
        LatestError = exception.Message;

        AddAttempt(OutboxAttempt.FromException(reason, exception), maxAttempts);
    }

    /// <summary>
    /// Restores the attempts list from persisted data.
    /// </summary>
    /// <param name="persistedAttempts">The attempts loaded from the database.</param>
    internal void RestoreAttempts(IEnumerable<OutboxAttempt> persistedAttempts)
    {
        attempts.Clear();
        attempts.AddRange(persistedAttempts);
    }
}
