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

    private static void ResolveCollections(
        MongoDbContext context,
        bool outboxEnabled,
        bool distributedLockEnabled)
    {
        if (outboxEnabled)
        {
            context.OutboxCollection = context.Database
                .GetCollection<OutboxEntry>(CollectionNames.Outbox)
                .WithWriteConcern(WriteConcern.WMajority);
            context.SequenceCollection = context.Database
                .GetCollection<SequenceCounter>(CollectionNames.Sequence)
                .WithWriteConcern(WriteConcern.WMajority);
        }

        if (distributedLockEnabled)
        {
            context.LockCollection = context.Database
                .GetCollection<LockDocument>(CollectionNames.Locks)
                .WithWriteConcern(WriteConcern.WMajority);
        }

        context.LeaderCollection = context.Database
            .GetCollection<LeaderDocument>(CollectionNames.Leader)
            .WithWriteConcern(WriteConcern.WMajority);

        context.NodeCollection = context.Database
            .GetCollection<NodeDocument>(CollectionNames.Nodes)
            .WithWriteConcern(WriteConcern.WMajority);

        context.DaemonAssignmentCollection = context.Database
            .GetCollection<DaemonAssignmentDocument>(CollectionNames.DaemonAssignments)
            .WithWriteConcern(WriteConcern.WMajority);
    }

    private static void CreateIndexes(
        MongoDbContext context,
        bool outboxEnabled,
        bool distributedLockEnabled)
    {
        if (outboxEnabled)
        {
            context.OutboxCollection!.Indexes.CreateOne(
                new CreateIndexModel<OutboxEntry>(
                    Builders<OutboxEntry>.IndexKeys.Ascending(x => x.Sequence),
                    new CreateIndexOptions { Name = "sequence" }));
        }

        if (distributedLockEnabled)
        {
            context.LockCollection!.Indexes.CreateOne(
                new CreateIndexModel<LockDocument>(
                    Builders<LockDocument>.IndexKeys.Ascending(x => x.ExpiresAt),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }));
        }

        context.DaemonAssignmentCollection.Indexes.CreateOne(
            new CreateIndexModel<DaemonAssignmentDocument>(
                Builders<DaemonAssignmentDocument>.IndexKeys.Ascending(x => x.AssignedNodeId)));
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
