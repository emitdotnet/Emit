namespace Emit.Persistence.MongoDB.Configuration;

using Emit.Models;
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
                cm.MapMember(x => x.CompletedAt)
                    .SetSerializer(CreateNullableUtcDateTimeSerializer());
                cm.MapMember(x => x.LastAttemptedAt)
                    .SetSerializer(CreateNullableUtcDateTimeSerializer());

                // Map the backing field for Attempts (readonly property with private field)
                cm.MapField("attempts")
                    .SetElementName("attempts");
            });
        }

        // Register OutboxAttempt class map
        if (!BsonClassMap.IsClassMapRegistered(typeof(OutboxAttempt)))
        {
            BsonClassMap.RegisterClassMap<OutboxAttempt>(cm =>
            {
                cm.AutoMap();

                // Map record constructor parameters
                cm.MapCreator(x => new OutboxAttempt(
                    x.AttemptedAt,
                    x.Reason,
                    x.Message,
                    x.ExceptionType));

                // Ensure DateTime is stored as UTC
                cm.MapMember(x => x.AttemptedAt)
                    .SetSerializer(CreateUtcDateTimeSerializer());
            });
        }

        // Register OutboxStatus enum (enum representation is handled by convention)
        if (!BsonClassMap.IsClassMapRegistered(typeof(OutboxStatus)))
        {
            BsonClassMap.RegisterClassMap<OutboxStatus>();
        }
    }

    private static IBsonSerializer<DateTime> CreateUtcDateTimeSerializer()
    {
        return new DateTimeSerializer(DateTimeKind.Utc, BsonType.DateTime);
    }

    private static IBsonSerializer<DateTime?> CreateNullableUtcDateTimeSerializer()
    {
        return new NullableSerializer<DateTime>(
            new DateTimeSerializer(DateTimeKind.Utc, BsonType.DateTime));
    }
}
