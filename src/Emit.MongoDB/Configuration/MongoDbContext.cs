namespace Emit.MongoDB.Configuration;

using Emit.Models;
using Emit.MongoDB.Models;
using global::MongoDB.Driver;

/// <summary>
/// Holds the MongoDB client, database, and pre-resolved collection references
/// for the Emit persistence provider. Registered as a singleton; all index creation
/// occurs once during service registration.
/// </summary>
public sealed class MongoDbContext
{
    /// <summary>
    /// Gets the MongoDB client instance.
    /// </summary>
    public required IMongoClient Client { get; set; }

    /// <summary>
    /// Gets the MongoDB database instance for Emit storage.
    /// </summary>
    public required IMongoDatabase Database { get; set; }

    /// <summary>
    /// Gets the outbox entries collection, or <see langword="null"/> if the outbox is not enabled.
    /// </summary>
    public IMongoCollection<OutboxEntry>? OutboxCollection { get; internal set; }

    /// <summary>
    /// Gets the outbox sequence counters collection, or <see langword="null"/> if the outbox is not enabled.
    /// </summary>
    internal IMongoCollection<SequenceCounter>? SequenceCollection { get; set; }

    /// <summary>
    /// Gets the distributed locks collection, or <see langword="null"/> if distributed locking is not enabled.
    /// </summary>
    internal IMongoCollection<LockDocument>? LockCollection { get; set; }

    /// <summary>
    /// Gets the leader election collection.
    /// </summary>
    internal IMongoCollection<LeaderDocument> LeaderCollection { get; set; } = null!;

    /// <summary>
    /// Gets the registered nodes collection.
    /// </summary>
    internal IMongoCollection<NodeDocument> NodeCollection { get; set; } = null!;

    /// <summary>
    /// Gets the daemon assignments collection.
    /// </summary>
    internal IMongoCollection<DaemonAssignmentDocument> DaemonAssignmentCollection { get; set; } = null!;
}
