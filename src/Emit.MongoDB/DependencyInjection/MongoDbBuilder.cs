namespace Emit.MongoDB.DependencyInjection;

using Emit.Configuration;
using Emit.MongoDB.Configuration;

/// <summary>
/// Builder for configuring the MongoDB persistence provider.
/// </summary>
/// <remarks>
/// <para>
/// You must call <see cref="Configure"/> to provide the MongoDB client and database.
/// Optionally call <see cref="UseOutbox"/> to enable the transactional outbox, or
/// <see cref="UseDistributedLock"/> to expose <c>IDistributedLockProvider</c>.
/// </para>
/// </remarks>
public sealed class MongoDbBuilder
{
    /// <summary>
    /// Gets the stored MongoDB configuration action.
    /// </summary>
    internal Action<IServiceProvider, MongoDbContext>? ConfigureAction { get; private set; }

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
    /// Configures the MongoDB client and database for persistence.
    /// </summary>
    /// <param name="configure">
    /// The configuration action with access to <see cref="IServiceProvider"/> for resolving
    /// services at resolution time.
    /// </param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <c>Configure</c> has already been called.</exception>
    public MongoDbBuilder Configure(Action<IServiceProvider, MongoDbContext> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (ConfigureAction is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(Configure)} has already been called. Only one MongoDB configuration is allowed.");
        }

        ConfigureAction = configure;
        return this;
    }

    /// <summary>
    /// Enables the transactional outbox using MongoDB as the persistence store.
    /// </summary>
    /// <param name="configure">Optional configuration action for outbox worker options.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public MongoDbBuilder UseOutbox(Action<OutboxOptions>? configure = null)
    {
        OutboxEnabled = true;
        OutboxOptionsConfiguration = configure;
        return this;
    }

    /// <summary>
    /// Registers <c>IDistributedLockProvider</c> backed by MongoDB for use in application code.
    /// </summary>
    /// <returns>This builder instance for method chaining.</returns>
    public MongoDbBuilder UseDistributedLock()
    {
        DistributedLockEnabled = true;
        return this;
    }
}
