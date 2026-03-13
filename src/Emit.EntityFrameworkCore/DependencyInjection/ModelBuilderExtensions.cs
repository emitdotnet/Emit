namespace Emit.EntityFrameworkCore.DependencyInjection;

using System.Text.Json;
using Emit.EntityFrameworkCore.Models;
using Emit.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

/// <summary>
/// Extension methods for configuring the Emit outbox model on <see cref="ModelBuilder"/>.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Adds the Emit outbox model to the <see cref="ModelBuilder"/>.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="configure">
    /// Optional configuration action for applying database-specific optimizations.
    /// Call <see cref="EmitModelBuilder.UseNpgsql"/> for PostgreSQL.
    /// </param>
    /// <returns>The model builder for method chaining.</returns>
    /// <remarks>
    /// Call this method in your <see cref="DbContext.OnModelCreating"/> to add the outbox
    /// and lock tables to your database schema.
    /// </remarks>
    public static ModelBuilder AddEmitModel(this ModelBuilder modelBuilder, Action<EmitModelBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureOutboxEntry(modelBuilder);
        ConfigureLockEntity(modelBuilder);
        ConfigureLeaderEntity(modelBuilder);
        ConfigureNodeEntity(modelBuilder);
        ConfigureDaemonAssignmentEntity(modelBuilder);

        if (configure is not null)
        {
            var builder = new EmitModelBuilder(modelBuilder);
            configure(builder);
        }

        return modelBuilder;
    }

    private static void ConfigureOutboxEntry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OutboxEntry>();

        entity.ToTable(TableNames.Outbox);

        // Primary key with value converter for object type
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd()
            .HasConversion(
                v => (Guid)v,
                v => v);

        // Sequence - auto-generated on add
        entity.Property(e => e.Sequence)
            .HasColumnName("sequence")
            .ValueGeneratedOnAdd();

        // String properties
        entity.Property(e => e.SystemId)
            .HasColumnName("system_id")
            .HasMaxLength(100)
            .IsRequired();

        entity.Property(e => e.Destination)
            .HasColumnName("destination")
            .HasMaxLength(2048)
            .IsRequired();

        entity.Property(e => e.ConversationId)
            .HasColumnName("conversation_id")
            .HasMaxLength(200);

        entity.Property(e => e.GroupKey)
            .HasColumnName("group_key")
            .HasMaxLength(500)
            .IsRequired();

        // DateTime properties - UTC timestamps
        entity.Property(e => e.EnqueuedAt)
            .HasColumnName("enqueued_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        entity.Property(e => e.NodeId)
            .HasColumnName("node_id")
            .IsRequired();

        // Body - nullable binary blob (null for tombstones)
        entity.Property(e => e.Body)
            .HasColumnName("body");

        // Headers - JSON-serialized list of key-value pairs with base64-encoded byte values
        var headersComparer = new ValueComparer<List<KeyValuePair<string, byte[]>>>(
            (a, b) => a != null && b != null && a.Count == b.Count &&
                      a.Zip(b).All(p => p.First.Key == p.Second.Key && p.First.Value.SequenceEqual(p.Second.Value)),
            v => v.Aggregate(0, (h, p) => HashCode.Combine(h, p.Key.GetHashCode(), p.Value.Length)),
            v => new List<KeyValuePair<string, byte[]>>(v.Select(p => new KeyValuePair<string, byte[]>(p.Key, p.Value.ToArray()))));

        entity.Property(e => e.Headers)
            .HasColumnName("headers")
            .HasConversion(
                v => SerializeHeaders(v),
                v => DeserializeHeaders(v),
                headersComparer);

        // Properties - JSON-serialized dictionary
        var propertiesComparer = new ValueComparer<Dictionary<string, string>>(
            (a, b) => a != null && b != null && a.Count == b.Count && !a.Except(b).Any(),
            v => v.Aggregate(0, (h, p) => HashCode.Combine(h, p.Key.GetHashCode(), p.Value == null ? 0 : p.Value.GetHashCode())),
            v => new Dictionary<string, string>(v));

        entity.Property(e => e.Properties)
            .HasColumnName("properties")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new Dictionary<string, string>(),
                propertiesComparer);

        // Index for ORDER BY sequence LIMIT N in outbox polling
        entity.HasIndex(e => e.Sequence)
            .HasDatabaseName("ix_outbox_sequence");
    }

    private static string SerializeHeaders(List<KeyValuePair<string, byte[]>> headers)
    {
        var pairs = headers.Select(h => new Dictionary<string, string>
        {
            ["k"] = h.Key,
            ["v"] = Convert.ToBase64String(h.Value)
        });
        return JsonSerializer.Serialize(pairs, JsonSerializerOptions.Default);
    }

    private static List<KeyValuePair<string, byte[]>> DeserializeHeaders(string json)
    {
        var pairs = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json, JsonSerializerOptions.Default);
        if (pairs is null) return [];

        return pairs
            .Select(p => new KeyValuePair<string, byte[]>(p["k"], Convert.FromBase64String(p["v"])))
            .ToList();
    }

    private static void ConfigureLockEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LockEntity>();

        entity.ToTable(TableNames.Locks);

        // Primary key - lock key
        entity.HasKey(e => e.Key);
        entity.Property(e => e.Key)
            .HasColumnName("key");

        // Lock holder ID
        entity.Property(e => e.LockId)
            .HasColumnName("lock_id")
            .IsRequired();

        // Lock expiry - UTC timestamp
        entity.Property(e => e.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Index for LockCleanupWorker DELETE WHERE expires_at < clock_timestamp()
        entity.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("ix_locks_expires_at");
    }

    private static void ConfigureLeaderEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LeaderEntity>();

        entity.ToTable(TableNames.Leader);

        // Primary key - always "leader"
        entity.HasKey(e => e.Key);
        entity.Property(e => e.Key)
            .HasColumnName("key")
            .HasMaxLength(50);

        // Leader node ID
        entity.Property(e => e.NodeId)
            .HasColumnName("node_id")
            .IsRequired();

        // Lease expiry - UTC timestamp
        entity.Property(e => e.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }

    private static void ConfigureNodeEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NodeEntity>();

        entity.ToTable(TableNames.Nodes);

        // Primary key - node ID
        entity.HasKey(e => e.NodeId);
        entity.Property(e => e.NodeId)
            .HasColumnName("node_id");

        // Instance identifier
        entity.Property(e => e.InstanceId)
            .HasColumnName("instance_id")
            .HasMaxLength(200)
            .IsRequired();

        // Started at - UTC timestamp
        entity.Property(e => e.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Last seen - UTC timestamp
        entity.Property(e => e.LastSeenAt)
            .HasColumnName("last_seen_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Index for RemoveExpiredNodesAsync DELETE WHERE last_seen_at < clock_timestamp() - @ttl
        entity.HasIndex(e => e.LastSeenAt)
            .HasDatabaseName("ix_nodes_last_seen_at");
    }

    private static void ConfigureDaemonAssignmentEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DaemonAssignmentEntity>();

        entity.ToTable(TableNames.DaemonAssignments);

        // Primary key - daemon ID
        entity.HasKey(e => e.DaemonId);
        entity.Property(e => e.DaemonId)
            .HasColumnName("daemon_id")
            .HasMaxLength(200);

        // Assigned node ID
        entity.Property(e => e.AssignedNodeId)
            .HasColumnName("assigned_node_id")
            .IsRequired();

        // Generation - fencing token
        entity.Property(e => e.Generation)
            .HasColumnName("generation")
            .IsRequired();

        // State
        entity.Property(e => e.State)
            .HasColumnName("state")
            .HasMaxLength(50)
            .IsRequired();

        // Assigned at - UTC timestamp
        entity.Property(e => e.AssignedAt)
            .HasColumnName("assigned_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Drain deadline - nullable UTC timestamp
        entity.Property(e => e.DrainDeadline)
            .HasColumnName("drain_deadline")
            .HasColumnType("timestamp with time zone");

        // Index on assigned_node_id for GetNodeAssignmentsAsync
        entity.HasIndex(e => e.AssignedNodeId)
            .HasDatabaseName("ix_daemon_assignments_assigned_node_id");
    }
}
