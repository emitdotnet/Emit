namespace Emit.EntityFrameworkCore.DependencyInjection;

using Emit.Abstractions;
using Emit.Abstractions.Daemon;
using Emit.Abstractions.LeaderElection;
using Emit.Configuration;
using Emit.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for configuring Entity Framework Core persistence on <see cref="EmitBuilder"/>.
/// </summary>
public static class EntityFrameworkCoreEmitBuilderExtensions
{
    /// <summary>
    /// Adds Entity Framework Core as a persistence provider.
    /// </summary>
    /// <typeparam name="TDbContext">
    /// The user's <see cref="DbContext"/> type. Must have the Emit model configured
    /// via <see cref="ModelBuilderExtensions.AddEmitModel"/> in <c>OnModelCreating</c>.
    /// </typeparam>
    /// <param name="builder">The Emit builder.</param>
    /// <param name="configure">
    /// The configuration action. You must call a database provider method
    /// (e.g., <see cref="EntityFrameworkCoreBuilder.UseNpgsql"/>) inside this action.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no database provider method was called inside <paramref name="configure"/>.
    /// </exception>
    public static EmitBuilder AddEntityFrameworkCore<TDbContext>(
        this EmitBuilder builder,
        Action<EntityFrameworkCoreBuilder> configure)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        ValidateDbContextRegistration<TDbContext>(builder.Services);

        builder.Services.AddSingleton(new PersistenceProviderMarker("EntityFrameworkCore"));

        var efBuilder = new EntityFrameworkCoreBuilder(builder.Services);
        configure(efBuilder);

        if (!efBuilder.DatabaseProviderRegistered)
        {
            throw new InvalidOperationException(
                "No database provider was configured for Entity Framework Core. " +
                $"You must call a database provider method (e.g., {nameof(EntityFrameworkCoreBuilder.UseNpgsql)}()) inside the {nameof(AddEntityFrameworkCore)} configuration.");
        }

        // Register database-specific services
        if (efBuilder.NpgsqlSelected)
        {
            RegisterNpgsqlServices<TDbContext>(builder);
        }

        if (efBuilder.OutboxEnabled)
        {
            RegisterOutboxServices<TDbContext>(builder, efBuilder);
        }

        if (efBuilder.DistributedLockEnabled)
        {
            RegisterDistributedLockServices<TDbContext>(builder);
        }

        if (efBuilder.DistributedLockEnabled)
        {
            RegisterLockCleanupServices<TDbContext>(builder);
        }

        RegisterLeaderElectionServices<TDbContext>(builder);
        RegisterDaemonAssignmentServices<TDbContext>(builder);

        return builder;
    }

    private static void ValidateDbContextRegistration<TDbContext>(IServiceCollection services)
        where TDbContext : DbContext
    {
        var dbContextDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TDbContext));

        if (dbContextDescriptor == null)
        {
            throw new InvalidOperationException(
                $"{typeof(TDbContext).Name} is not registered in the service collection. " +
                $"Register the DbContext before calling {nameof(AddEntityFrameworkCore)}.");
        }

        if (dbContextDescriptor.Lifetime != ServiceLifetime.Scoped)
        {
            throw new InvalidOperationException(
                $"{typeof(TDbContext).Name} must be registered with {nameof(ServiceLifetime)}.{nameof(ServiceLifetime.Scoped)} lifetime, " +
                $"but is registered as {nameof(ServiceLifetime)}.{dbContextDescriptor.Lifetime}.");
        }

        var factoryDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IDbContextFactory<TDbContext>));

        if (factoryDescriptor == null)
        {
            throw new InvalidOperationException(
                $"IDbContextFactory<{typeof(TDbContext).Name}> is not registered. " +
                "Both the DbContext and IDbContextFactory must be registered.");
        }
    }

    private static void RegisterNpgsqlServices<TDbContext>(EmitBuilder builder)
        where TDbContext : DbContext
    {
        // No additional services needed for Npgsql
    }

    private static void RegisterOutboxServices<TDbContext>(EmitBuilder builder, EntityFrameworkCoreBuilder efBuilder)
        where TDbContext : DbContext
    {
        builder.Services.AddSingleton(new OutboxRegistrationMarker("EntityFrameworkCore"));

        var optionsBuilder = builder.Services.AddOptions<OutboxOptions>();
        if (efBuilder.OutboxOptionsConfiguration is { } configure)
        {
            optionsBuilder.Configure(configure);
        }

        // Register as SCOPED (not singleton) to use user's scoped DbContext
        builder.Services.AddScoped<EfCoreOutboxRepository<TDbContext>>();
        builder.Services.AddScoped<IOutboxRepository>(sp => sp.GetRequiredService<EfCoreOutboxRepository<TDbContext>>());

        builder.Services.AddScoped<IUnitOfWork>(sp =>
        {
            var dbContext = sp.GetRequiredService<TDbContext>();
            var emitContext = sp.GetRequiredService<IEmitContext>();
            return new EfCoreUnitOfWork<TDbContext>(dbContext, emitContext);
        });
    }

    private static void RegisterDistributedLockServices<TDbContext>(EmitBuilder builder)
        where TDbContext : DbContext
    {
        builder.Services.AddSingleton(new DistributedLockRegistrationMarker("EntityFrameworkCore"));

        builder.Services.TryAddSingleton<EfCoreDistributedLockProvider<TDbContext>>();
        builder.Services.AddSingleton<IDistributedLockProvider>(
            sp => sp.GetRequiredService<EfCoreDistributedLockProvider<TDbContext>>());
    }

    private static void RegisterLockCleanupServices<TDbContext>(EmitBuilder builder)
        where TDbContext : DbContext
    {
        builder.Services.AddOptions<Configuration.LockCleanupOptions>().ValidateOnStart();
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<Configuration.LockCleanupOptions>, Configuration.LockCleanupOptionsValidator>();
        builder.Services.AddHostedService<Worker.LockCleanupWorker<TDbContext>>();
    }

    private static void RegisterLeaderElectionServices<TDbContext>(EmitBuilder builder)
        where TDbContext : DbContext
    {
        builder.Services.TryAddSingleton<EfCoreLeaderElectionPersistence<TDbContext>>();
        builder.Services.TryAddSingleton<ILeaderElectionPersistence>(
            sp => sp.GetRequiredService<EfCoreLeaderElectionPersistence<TDbContext>>());
    }

    private static void RegisterDaemonAssignmentServices<TDbContext>(EmitBuilder builder)
        where TDbContext : DbContext
    {
        builder.Services.TryAddSingleton<EfCoreDaemonAssignmentPersistence<TDbContext>>();
        builder.Services.TryAddSingleton<IDaemonAssignmentPersistence>(
            sp => sp.GetRequiredService<EfCoreDaemonAssignmentPersistence<TDbContext>>());
    }
}
