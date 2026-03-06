namespace Emit.EntityFrameworkCore.DependencyInjection;

using System.Text.Json;
using Emit.Models;
using Emit.EntityFrameworkCore.Models;
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
    /// <para>
    /// Call this method in your <see cref="DbContext.OnModelCreating"/> to add the outbox
    /// and lock tables to your database schema.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     modelBuilder.AddEmitModel(emit =&gt; emit.UseNpgsql());
    /// }
    /// </code>
    /// </para>
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

        // Primary key with value converter for object? type
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd()
            .HasConversion(
                v => v == null ? Guid.Empty : (Guid)v,
                v => (object?)v);

        // Sequence - auto-generated on add
        entity.Property(e => e.Sequence)
            .HasColumnName("sequence")
            .ValueGeneratedOnAdd();

        // String properties
        entity.Property(e => e.ProviderId)
            .HasColumnName("provider_id")
            .HasMaxLength(100)
            .IsRequired();

        entity.Property(e => e.RegistrationKey)
            .HasColumnName("registration_key")
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(e => e.GroupKey)
            .HasColumnName("group_key")
            .HasMaxLength(500)
            .IsRequired();

        entity.Property(e => e.TraceParent)
            .HasColumnName("trace_parent")
            .HasMaxLength(55); // W3C traceparent is exactly 55 chars

        entity.Property(e => e.TraceState)
            .HasColumnName("trace_state")
            .HasMaxLength(2048);

        // DateTime properties - UTC timestamps
        entity.Property(e => e.EnqueuedAt)
            .HasColumnName("enqueued_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Payload - binary blob
        entity.Property(e => e.Payload)
            .HasColumnName("payload")
            .IsRequired();

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

        // Indexes
        entity.HasIndex(e => new { e.GroupKey, e.Sequence })
            .IsUnique()
            .HasDatabaseName("ix_outbox_group_key_sequence");

        entity.HasIndex(e => e.TraceParent)
            .HasDatabaseName("ix_outbox_trace_parent");
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
