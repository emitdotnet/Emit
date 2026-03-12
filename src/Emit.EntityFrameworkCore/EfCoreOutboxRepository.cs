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
    public async Task DeleteAsync(object entryId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryId);

        var id = ConvertToGuid(entryId);

        // Worker operation - create own DbContext via factory
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var deletedCount = await dbContext.Set<OutboxEntry>()
            .Where(e => e.Id != null && (Guid)e.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deletedCount == 0)
        {
            logger.LogWarning("Outbox entry not found for deletion: id={Id}", id);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxEntry>> GetGroupHeadsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        // Worker operation - create own DbContext via factory
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // DISTINCT ON (group_key) with the (group_key, sequence) index does an index skip scan —
        // one probe per group, never visiting interior rows. The outer ORDER BY + LIMIT picks
        // the globally oldest group heads without materializing the full table.
        // Build SQL as a plain string to avoid EF1002 (no user input — table name is a
        // compile-time constant, limit is validated > 0 above).
        var sql = $"""
            SELECT * FROM (
                SELECT DISTINCT ON (group_key) *
                FROM "{TableNames.Outbox}"
                ORDER BY group_key, sequence
            ) heads
            ORDER BY sequence
            LIMIT {limit}
            """;

        return await dbContext.Set<OutboxEntry>()
            .FromSqlRaw(sql)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxEntry>> GetBatchAsync(
        IEnumerable<string> eligibleGroups,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eligibleGroups);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var groupsList = eligibleGroups.ToList();
        if (groupsList.Count == 0)
        {
            return [];
        }

        // Worker operation - create own DbContext via factory
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entries = await dbContext.Set<OutboxEntry>()
            .Where(e => groupsList.Contains(e.GroupKey))
            .OrderBy(e => e.GroupKey)
            .ThenBy(e => e.Sequence)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entries;
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
