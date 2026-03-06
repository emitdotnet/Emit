namespace Emit.EntityFrameworkCore.DependencyInjection;

using Emit.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Builder for configuring the Entity Framework Core persistence provider.
/// </summary>
/// <remarks>
/// <para>
/// You must call exactly one database provider method (e.g., <see cref="UseNpgsql"/>)
/// to specify which database engine to use with Entity Framework Core.
/// Optionally call <see cref="UseOutbox"/> to enable the transactional outbox, or
/// <see cref="UseDistributedLock"/> to expose <c>IDistributedLockProvider</c>.
/// </para>
/// </remarks>
public sealed class EntityFrameworkCoreBuilder
{
    internal EntityFrameworkCoreBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Gets the service collection for registering provider-specific services.
    /// </summary>
    internal IServiceCollection Services { get; }

    /// <summary>
    /// Gets a value indicating whether a database provider has been selected.
    /// </summary>
    internal bool DatabaseProviderRegistered { get; private set; }

    /// <summary>
    /// Gets a value indicating whether Npgsql (PostgreSQL) was selected.
    /// </summary>
    internal bool NpgsqlSelected { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the outbox is enabled.
    /// </summary>
    internal bool OutboxEnabled { get; private set; }

    /// <summary>
    /// Gets the stored outbox options configuration action.
    /// </summary>
    internal Action<OutboxOptions>? OutboxOptionsConfiguration { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the distributed lock is enabled.
    /// </summary>
    internal bool DistributedLockEnabled { get; private set; }

    /// <summary>
    /// Configures PostgreSQL via Npgsql as the database provider for Entity Framework Core.
    /// </summary>
    /// <returns>This builder instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers <c>IPostgresTransactionManager</c> for transaction support.
    /// </para>
    /// <para>
    /// You must also call <see cref="ModelBuilderExtensions.AddEmitModel"/> with
    /// <see cref="EmitModelBuilder.UseNpgsql"/> in your DbContext's <c>OnModelCreating</c>.
    /// </para>
    /// </remarks>
    public EntityFrameworkCoreBuilder UseNpgsql()
    {
        DatabaseProviderRegistered = true;
        NpgsqlSelected = true;
        return this;
    }

    /// <summary>
    /// Enables the transactional outbox using Entity Framework Core as the persistence store.
    /// </summary>
    /// <param name="configure">Optional configuration action for outbox worker options.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public EntityFrameworkCoreBuilder UseOutbox(Action<OutboxOptions>? configure = null)
    {
        OutboxEnabled = true;
        OutboxOptionsConfiguration = configure;
        return this;
    }

    /// <summary>
    /// Registers <c>IDistributedLockProvider</c> backed by Entity Framework Core for use in application code.
    /// </summary>
    /// <returns>This builder instance for method chaining.</returns>
    public EntityFrameworkCoreBuilder UseDistributedLock()
    {
        DistributedLockEnabled = true;
        return this;
    }
}
