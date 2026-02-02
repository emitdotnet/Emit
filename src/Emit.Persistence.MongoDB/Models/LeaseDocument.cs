namespace Emit.Persistence.MongoDB.Models;

/// <summary>
/// Represents the global lease document for worker coordination.
/// </summary>
/// <remarks>
/// Only one lease document exists with a well-known ID. Workers compete
/// to acquire and renew this lease for exclusive processing rights.
/// </remarks>
internal sealed class LeaseDocument
{
    /// <summary>
    /// The well-known document ID for the global lease.
    /// </summary>
    public const string GlobalLeaseId = "global";

    /// <summary>
    /// Gets or sets the document ID (always "global").
    /// </summary>
    public string Id { get; set; } = GlobalLeaseId;

    /// <summary>
    /// Gets or sets the ID of the worker currently holding the lease.
    /// </summary>
    public string? WorkerId { get; set; }

    /// <summary>
    /// Gets or sets when the lease expires (UTC).
    /// </summary>
    /// <remarks>
    /// A worker can acquire the lease if LeaseUntil is in the past
    /// or if the worker already owns the lease.
    /// </remarks>
    public DateTime LeaseUntil { get; set; }
}
