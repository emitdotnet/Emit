namespace Emit.Persistence.PostgreSQL.Configuration;

/// <summary>
/// Configuration options for the PostgreSQL persistence provider.
/// </summary>
public sealed class PostgreSqlOptions
{
    /// <summary>
    /// Gets or sets the PostgreSQL connection string.
    /// </summary>
    /// <remarks>
    /// This is a required field and must be a valid PostgreSQL connection string.
    /// </remarks>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the table name for outbox entries.
    /// </summary>
    /// <remarks>
    /// Defaults to "outbox".
    /// </remarks>
    public string TableName { get; set; } = "outbox";

    /// <summary>
    /// Gets or sets the table name for global lease management.
    /// </summary>
    /// <remarks>
    /// Defaults to "outbox_lease". This table stores the global lease
    /// document used for worker coordination.
    /// </remarks>
    public string LeaseTableName { get; set; } = "outbox_lease";

    /// <summary>
    /// Gets or sets the table name for sequence counters.
    /// </summary>
    /// <remarks>
    /// Defaults to "outbox_sequences". This table stores atomic counters
    /// for generating unique sequence numbers per GroupKey.
    /// </remarks>
    public string SequenceTableName { get; set; } = "outbox_sequences";

    /// <summary>
    /// Gets or sets the retention period for completed entries.
    /// </summary>
    /// <remarks>
    /// Completed entries older than this period are eligible for cleanup.
    /// Minimum allowed value is 1 hour. Defaults to 7 days.
    /// </remarks>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets the maximum number of attempt records to store per entry.
    /// </summary>
    /// <remarks>
    /// When this limit is exceeded, older attempt records are removed.
    /// Defaults to 10.
    /// </remarks>
    public int MaxAttemptsPerEntry { get; set; } = 10;
}
