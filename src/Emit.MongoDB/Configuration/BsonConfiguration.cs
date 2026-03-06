namespace Emit.MongoDB.Configuration;

using Emit.Models;
using Emit.MongoDB.Models;
using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization;
using global::MongoDB.Bson.Serialization.Conventions;
using global::MongoDB.Bson.Serialization.IdGenerators;
using global::MongoDB.Bson.Serialization.Serializers;

/// <summary>
/// Configures BSON class maps and conventions for MongoDB serialization.
/// </summary>
/// <remarks>
/// This class configures:
/// <list type="bullet">
/// <item><description>CamelCase naming convention for all fields</description></item>
/// <item><description>UTC representation for all DateTime fields</description></item>
/// <item><description>ObjectId mapping for OutboxEntry.Id</description></item>
/// </list>
/// The configuration is applied once at application startup via <see cref="Configure"/>.
/// </remarks>
internal static class BsonConfiguration
{
    private static readonly object LockObject = new();
    private static bool isConfigured;

    /// <summary>
    /// Configures BSON serialization conventions and class maps.
    /// </summary>
    /// <remarks>
    /// This method is thread-safe and idempotent - calling it multiple times has no effect.
    /// </remarks>
    public static void Configure()
    {
        lock (LockObject)
        {
            if (isConfigured)
            {
                return;
            }

            RegisterConventions();
            RegisterClassMaps();
            isConfigured = true;
        }
    }

    private static void RegisterConventions()
    {
        // Register camelCase naming convention globally
        var conventionPack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true)
        };

        ConventionRegistry.Register(
            "EmitConventions",
            conventionPack,
            t => t.Namespace?.StartsWith("Emit", StringComparison.Ordinal) == true);
    }

    private static void RegisterClassMaps()
    {
        // Register OutboxEntry class map
        if (!BsonClassMap.IsClassMapRegistered(typeof(OutboxEntry)))
        {
            BsonClassMap.RegisterClassMap<OutboxEntry>(cm =>
            {
                cm.AutoMap();

                // Map Id to ObjectId with auto-generation
                // Use ObjectSerializer to handle the object? type, with ObjectId representation
                cm.MapIdMember(x => x.Id)
                    .SetIdGenerator(ObjectIdGenerator.Instance)
                    .SetSerializer(new ObjectSerializer(ObjectSerializer.DefaultAllowedTypes));

                // Ensure DateTime fields are stored as UTC
                cm.MapMember(x => x.EnqueuedAt)
                    .SetSerializer(CreateUtcDateTimeSerializer());
            });
        }

        // Register LockDocument class map
        if (!BsonClassMap.IsClassMapRegistered(typeof(LockDocument)))
        {
            BsonClassMap.RegisterClassMap<LockDocument>(cm =>
            {
                cm.AutoMap();

                cm.MapIdMember(x => x.Key);

                cm.MapMember(x => x.LockId)
                    .SetSerializer(new GuidSerializer(GuidRepresentation.Standard));

                cm.MapMember(x => x.ExpiresAt)
                    .SetSerializer(CreateUtcDateTimeSerializer());
            });
        }

        // Register LeaderDocument class map
        if (!BsonClassMap.IsClassMapRegistered(typeof(LeaderDocument)))
        {
            BsonClassMap.RegisterClassMap<LeaderDocument>(cm =>
            {
                cm.AutoMap();

                cm.MapIdMember(x => x.Key);

                cm.MapMember(x => x.NodeId)
                    .SetSerializer(new GuidSerializer(GuidRepresentation.Standard));

                cm.MapMember(x => x.ExpiresAt)
                    .SetSerializer(CreateUtcDateTimeSerializer());
            });
        }

        // Register NodeDocument class map
        if (!BsonClassMap.IsClassMapRegistered(typeof(NodeDocument)))
        {
            BsonClassMap.RegisterClassMap<NodeDocument>(cm =>
            {
                cm.AutoMap();

                cm.MapIdMember(x => x.NodeId)
                    .SetSerializer(new GuidSerializer(GuidRepresentation.Standard));

                cm.MapMember(x => x.StartedAt)
                    .SetSerializer(CreateUtcDateTimeSerializer());

                cm.MapMember(x => x.LastSeenAt)
                    .SetSerializer(CreateUtcDateTimeSerializer());
            });
        }

        // Register DaemonAssignmentDocument class map
        if (!BsonClassMap.IsClassMapRegistered(typeof(DaemonAssignmentDocument)))
        {
            BsonClassMap.RegisterClassMap<DaemonAssignmentDocument>(cm =>
            {
                cm.AutoMap();

                cm.MapIdMember(x => x.DaemonId);

                cm.MapMember(x => x.AssignedNodeId)
                    .SetSerializer(new GuidSerializer(GuidRepresentation.Standard));

                cm.MapMember(x => x.AssignedAt)
                    .SetSerializer(CreateUtcDateTimeSerializer());

                cm.MapMember(x => x.DrainDeadline)
                    .SetSerializer(new NullableSerializer<DateTime>(CreateUtcDateTimeSerializer()));
            });
        }
    }

    private static IBsonSerializer<DateTime> CreateUtcDateTimeSerializer()
    {
        return new DateTimeSerializer(DateTimeKind.Utc, BsonType.DateTime);
    }

}
