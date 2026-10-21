namespace Emit.EntityFrameworkCore;

using Emit.Abstractions;
using Emit.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Entity Framework Core implementation of the outbox repository.
/// </summary>
/// <typeparam name="TDbContext">The user's DbContext type.</typeparam>
/// <remarks>
/// <para>
/// This implementation uses EF Core with Npgsql for PostgreSQL persistence.
/// The sequence is managed using IDENTITY columns, which auto-increment on insert.
/// </para>
/// <para>
/// For user operations (EnqueueAsync), this repository uses the scoped DbContext instance
/// to ensure atomicity with the user's business data. For background worker operations
/// (DeleteAsync, Get*), it creates its own DbContext via the factory.
/// </para>
/// </remarks>
internal sealed class EfCoreOutboxRepository<TDbContext> : IOutboxRepository
    where TDbContext : DbContext
{
    private readonly TDbContext dbContext;
    private readonly IDbContextFactory<TDbContext> dbContextFactory;
    private readonly ILogger<EfCoreOutboxRepository<TDbContext>> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreOutboxRepository{TDbContext}"/> class.
    /// </summary>
    /// <param name="dbContext">The user's scoped DbContext instance.</param>
    /// <param name="dbContextFactory">The DbContext factory for background worker operations.</param>
    /// <param name="logger">The logger.</param>
    public EfCoreOutboxRepository(
        TDbContext dbContext,
        IDbContextFactory<TDbContext> dbContextFactory,
        ILogger<EfCoreOutboxRepository<TDbContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        this.dbContext = dbContext;
        this.dbContextFactory = dbContextFactory;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public Task EnqueueAsync(
        OutboxEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Use injected scoped DbContext - same instance user has
        // Transaction is already on the DbContext (if user created one)
        dbContext.Set<OutboxEntry>().Add(entry);

        // DO NOT call SaveChangesAsync - user controls this
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(IReadOnlyCollection<object> entryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);

        List<Guid> ids = [.. entryIds.Select(ConvertToGuid)];
        if (ids is [])
        {
            return;
        }

        // Worker operation - create own DbContext via factory
        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken)
            .ConfigureAwait(false);

        var deletedCount = await dbContext.Set<OutboxEntry>()
            .Where(e => e.Id != null && ids.Contains((Guid)e.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deletedCount < ids.Count)
        {
            logger.LogWarning("{Missing} of {Count} outbox entries were not found for deletion", ids.Count - deletedCount, ids.Count);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxEntry>> GetBatchAsync(
        string serviceName,
        IReadOnlyCollection<string> systemIds,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        // Worker operation - create own DbContext via factory
        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken)
            .ConfigureAwait(false);

        if (systemIds.Count == 0)
        {
            return [];
        }

        // Each provider's oldest entries are read in sequence order from the index, then merged: a single condition
        // on all providers would make the database read and sort every pending entry of the service
        var entries = dbContext.Set<OutboxEntry>().AsNoTracking();
        return await systemIds
            .Select(systemId => entries
                .Where(e => e.ServiceName == serviceName && e.SystemId == systemId)
                .OrderBy(e => e.Sequence)
                .Take(batchSize))
            .Aggregate(Queryable.Concat)
            .OrderBy(e => e.Sequence)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static Guid ConvertToGuid(object entryId)
    {
        return entryId switch
        {
            Guid guid => guid,
            string stringId => Guid.Parse(stringId),
            _ => throw new ArgumentException(
                $"Cannot convert {entryId.GetType().Name} to Guid. " +
                "Entry ID must be a Guid or a valid Guid string.",
                nameof(entryId))
        };
    }
}
