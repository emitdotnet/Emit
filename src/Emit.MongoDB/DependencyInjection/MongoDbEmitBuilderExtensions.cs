namespace Emit.MongoDB.DependencyInjection;

using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;
using Emit.Configuration;
using Emit.DependencyInjection;
using Emit.Models;
using Emit.MongoDB.Configuration;
using Emit.MongoDB.Models;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for configuring MongoDB persistence on <see cref="EmitBuilder"/>.
/// </summary>
public static class MongoDbEmitBuilderExtensions
{
    /// <summary>
    /// The index node registrations are read through.
    /// </summary>
    internal const string NodeServiceNameIndex = "serviceName";

    /// <summary>
    /// Name of the unique index of daemon assignments by service and daemon. A service's assignments are read
    /// through it: no write changes its keys, so a read never returns an assignment twice.
    /// </summary>
    internal const string AssignmentServiceNameDaemonIdIndex = "serviceName_daemonId";

    /// <summary>
    /// Adds MongoDB as a persistence provider.
    /// </summary>
    /// <param name="builder">The Emit builder.</param>
    /// <param name="configure">The configuration action for the MongoDB builder.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="MongoDbBuilder.Configure"/> was not called.
    /// </exception>
    public static EmitBuilder AddMongoDb(this EmitBuilder builder, Action<MongoDbBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var mongoBuilder = new MongoDbBuilder();
        configure(mongoBuilder);

        if (mongoBuilder.ConfigureAction is null)
        {
            throw new InvalidOperationException(
                $"{nameof(MongoDbBuilder.Configure)} must be called on the {nameof(MongoDbBuilder)} to provide MongoDB client and database configuration.");
        }

        builder.Services.AddSingleton(new PersistenceProviderMarker("MongoDB"));

        BsonConfiguration.Configure();

        var configureAction = mongoBuilder.ConfigureAction;
        var outboxEnabled = mongoBuilder.OutboxEnabled;
        var distributedLockEnabled = mongoBuilder.DistributedLockEnabled;

        builder.Services.AddSingleton(sp =>
        {
            var context = new MongoDbContext
            {
                Client = null!,
                Database = null!
            };

            configureAction(sp, context);

            ArgumentNullException.ThrowIfNull(context.Client, nameof(MongoDbContext.Client));
            ArgumentNullException.ThrowIfNull(context.Database, nameof(MongoDbContext.Database));

            ResolveCollections(context, outboxEnabled, distributedLockEnabled);
            CreateIndexes(context, outboxEnabled, distributedLockEnabled);

            return context;
        });

        RegisterUnitOfWorkServices(builder);

        if (outboxEnabled)
        {
            RegisterOutboxServices(builder, mongoBuilder);
        }

        if (distributedLockEnabled)
        {
            RegisterDistributedLockServices(builder);
        }

        RegisterLeaderElectionServices(builder);
        RegisterDaemonAssignmentServices(builder);

        return builder;
    }

    /// <summary>
    /// Resolves Emit's collections. They read from the primary whatever the client's read preference: a
    /// secondary that lags would show a node an assignment that already moved, or an outbox entry that was
    /// already delivered and deleted.
    /// </summary>
    private static void ResolveCollections(
        MongoDbContext context,
        bool outboxEnabled,
        bool distributedLockEnabled)
    {
        if (outboxEnabled)
        {
            context.OutboxCollection = context.Database
                .GetCollection<OutboxEntry>(CollectionNames.Outbox)
                .WithWriteConcern(WriteConcern.WMajority)
                .WithReadPreference(ReadPreference.Primary);
            context.SequenceCollection = context.Database
                .GetCollection<SequenceCounter>(CollectionNames.Sequence)
                .WithWriteConcern(WriteConcern.WMajority)
                .WithReadPreference(ReadPreference.Primary);
        }

        if (distributedLockEnabled)
        {
            context.LockCollection = context.Database
                .GetCollection<LockDocument>(CollectionNames.Locks)
                .WithWriteConcern(WriteConcern.WMajority)
                .WithReadPreference(ReadPreference.Primary);
        }

        context.LeaderCollection = context.Database
            .GetCollection<LeaderDocument>(CollectionNames.Leader)
            .WithWriteConcern(WriteConcern.WMajority)
                .WithReadPreference(ReadPreference.Primary);

        context.NodeCollection = context.Database
            .GetCollection<NodeDocument>(CollectionNames.Nodes)
            .WithWriteConcern(WriteConcern.WMajority)
                .WithReadPreference(ReadPreference.Primary);

        context.DaemonAssignmentCollection = context.Database
            .GetCollection<DaemonAssignmentDocument>(CollectionNames.DaemonAssignments)
            .WithWriteConcern(WriteConcern.WMajority)
                .WithReadPreference(ReadPreference.Primary);
    }

    private static void CreateIndexes(
        MongoDbContext context,
        bool outboxEnabled,
        bool distributedLockEnabled)
    {
        if (outboxEnabled)
        {
            // Each service's outbox daemon reads only its own entries of the providers its node has, in sequence order
            context.OutboxCollection!.Indexes.CreateOne(
                new CreateIndexModel<OutboxEntry>(
                    Builders<OutboxEntry>.IndexKeys.Ascending(x => x.ServiceName).Ascending(x => x.SystemId).Ascending(x => x.Sequence),
                    new CreateIndexOptions { Name = "serviceName_systemId_sequence" }));
        }

        if (distributedLockEnabled)
        {
            context.LockCollection!.Indexes.CreateOne(
                new CreateIndexModel<LockDocument>(
                    Builders<LockDocument>.IndexKeys.Ascending(x => x.ExpiresAt),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }));
        }

        // One lease per service
        context.LeaderCollection.Indexes.CreateOne(
            new CreateIndexModel<LeaderDocument>(
                Builders<LeaderDocument>.IndexKeys.Ascending(x => x.ServiceName),
                new CreateIndexOptions { Name = "serviceName", Unique = true }));

        // Keyed only on what a heartbeat never rewrites: an index scan can skip a document whose key moves behind
        // it, and a node missing from the leader's read is moved off at once
        context.NodeCollection.Indexes.CreateOne(
            new CreateIndexModel<NodeDocument>(
                Builders<NodeDocument>.IndexKeys.Ascending(x => x.ServiceName),
                new CreateIndexOptions { Name = NodeServiceNameIndex }));

        // One assignment per service and daemon
        context.DaemonAssignmentCollection.Indexes.CreateOne(
            new CreateIndexModel<DaemonAssignmentDocument>(
                Builders<DaemonAssignmentDocument>.IndexKeys.Ascending(x => x.ServiceName).Ascending(x => x.DaemonId),
                new CreateIndexOptions { Name = AssignmentServiceNameDaemonIdIndex, Unique = true }));

        // A node reads the assignments of its service
        context.DaemonAssignmentCollection.Indexes.CreateOne(
            new CreateIndexModel<DaemonAssignmentDocument>(
                Builders<DaemonAssignmentDocument>.IndexKeys.Ascending(x => x.ServiceName).Ascending(x => x.AssignedNodeId),
                new CreateIndexOptions { Name = "serviceName_assignedNodeId" }));
    }

    private static void RegisterOutboxServices(EmitBuilder builder, MongoDbBuilder mongoBuilder)
    {
        builder.Services.AddSingleton(new OutboxRegistrationMarker("MongoDB"));

        var optionsBuilder = builder.Services.AddOptions<OutboxOptions>();
        if (mongoBuilder.OutboxOptionsConfiguration is { } configure)
        {
            optionsBuilder.Configure(configure);
        }

        builder.Services.AddScoped<MongoDbOutboxRepository>();
        builder.Services.AddScoped<IOutboxRepository>(sp => sp.GetRequiredService<MongoDbOutboxRepository>());
    }

    // Transactions are a capability of the persistence provider, not of the outbox, so the
    // unit of work and the session accessor are available whether or not UseOutbox() is called.
    private static void RegisterUnitOfWorkServices(EmitBuilder builder)
    {
        builder.Services.AddScoped<MongoSessionHolder>();
        builder.Services.AddScoped<IMongoSessionAccessor>(sp => sp.GetRequiredService<MongoSessionHolder>());
        builder.Services.AddScoped<IUnitOfWork>(sp =>
        {
            var mongoContext = sp.GetRequiredService<MongoDbContext>();
            var emitContext = sp.GetRequiredService<IEmitContext>();
            var sessionHolder = sp.GetRequiredService<MongoSessionHolder>();
            return new MongoUnitOfWork(mongoContext, emitContext, sessionHolder);
        });
    }

    private static void RegisterDistributedLockServices(EmitBuilder builder)
    {
        builder.Services.AddSingleton(new DistributedLockRegistrationMarker("MongoDB"));

        builder.Services.TryAddSingleton<MongoDbDistributedLockProvider>();
        builder.Services.AddSingleton<IDistributedLockProvider>(
            sp => sp.GetRequiredService<MongoDbDistributedLockProvider>());
    }

    private static void RegisterLeaderElectionServices(EmitBuilder builder)
    {
        builder.Services.TryAddSingleton<MongoDbLeaderElectionPersistence>();
        builder.Services.TryAddSingleton<ILeaderElectionPersistence>(
            sp => sp.GetRequiredService<MongoDbLeaderElectionPersistence>());
    }

    private static void RegisterDaemonAssignmentServices(EmitBuilder builder)
    {
        builder.Services.TryAddSingleton<MongoDbDaemonAssignmentPersistence>();
        builder.Services.TryAddSingleton<IDaemonAssignmentPersistence>(
            sp => sp.GetRequiredService<MongoDbDaemonAssignmentPersistence>());
    }
}
