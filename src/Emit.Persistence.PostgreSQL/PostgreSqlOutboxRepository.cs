namespace Emit.Persistence.PostgreSQL;

using System.Data;
using Emit.Abstractions;
using Emit.Models;
using Emit.Persistence.PostgreSQL.Configuration;
using Emit.Persistence.PostgreSQL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transactional.Abstractions;
using Transactional.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of the outbox repository using Entity Framework Core.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses EF Core with Npgsql for PostgreSQL persistence.
/// The sequence is managed using IDENTITY columns, which auto-increment on insert.
/// </para>
/// <para>
/// Transaction integration is achieved by using the <see cref="IPostgresTransactionContext"/>
/// to share the transaction between the user's DbContext and the outbox DbContext.
/// </para>
/// </remarks>
internal sealed class PostgreSqlOutboxRepository : IOutboxRepository, ILeaseRepository
{
    private readonly IDbContextFactory<OutboxDbContext> dbContextFactory;
    private readonly PostgreSqlOptions options;
    private readonly ILogger<PostgreSqlOutboxRepository> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOutboxRepository"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The DbContext factory for creating outbox contexts.</param>
    /// <param name="options">The PostgreSQL options.</param>
    /// <param name="logger">The logger.</param>
    public PostgreSqlOutboxRepository(
        IDbContextFactory<OutboxDbContext> dbContextFactory,
        IOptions<PostgreSqlOptions> options,
        ILogger<PostgreSqlOutboxRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.dbContextFactory = dbContextFactory;
        this.options = options.Value;
        this.logger = logger;

        logger.LogDebug(
            "PostgreSqlOutboxRepository initialized with table={TableName}",
            this.options.TableName);
    }

    /// <summary>
    /// Ensures the database schema and indexes are created.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Ensuring database schema exists");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // EnsureCreatedAsync creates the database and tables if neither exists
        // If the database exists but tables don't, it won't create the tables
        var created = await dbContext.Database.EnsureCreatedAsync(cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Database schema ensured (created={Created})", created);
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync(
        OutboxEntry entry,
        ITransactionContext? transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        logger.LogDebug(
            "Enqueuing outbox entry: groupKey={GroupKey}, providerId={ProviderId}",
            entry.GroupKey, entry.ProviderId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // If a transaction context is provided, enlist in that transaction
        if (transaction is IPostgresTransactionContext postgresTransaction)
        {
            await dbContext.Database.UseTransactionAsync(
                postgresTransaction.Transaction,
                cancellationToken).ConfigureAwait(false);
        }

        dbContext.OutboxEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // After SaveChanges, the Sequence will be populated by the IDENTITY column
        logger.LogInformation(
            "Outbox entry enqueued: id={Id}, groupKey={GroupKey}, sequence={Sequence}",
            entry.Id, entry.GroupKey, entry.Sequence);
    }

    /// <inheritdoc/>
    public async Task<long> GetNextSequenceAsync(
        string groupKey,
        ITransactionContext? transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupKey);

        logger.LogDebug("Getting next sequence for groupKey={GroupKey}", groupKey);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        if (transaction is IPostgresTransactionContext postgresTransaction)
        {
            await dbContext.Database.UseTransactionAsync(
                postgresTransaction.Transaction,
                cancellationToken).ConfigureAwait(false);
        }

        // Use atomic INSERT...ON CONFLICT to increment sequence counter
        // This ensures thread-safe sequence generation per group
        var sql = $"""
            INSERT INTO "{options.SequenceTableName}" (group_key, sequence)
            VALUES (@groupKey, 1)
            ON CONFLICT (group_key)
            DO UPDATE SET sequence = "{options.SequenceTableName}".sequence + 1
            RETURNING sequence
            """;

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@groupKey";
        parameter.Value = groupKey;
        command.Parameters.Add(parameter);

        // Use the existing transaction if present
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction() is { } dbTransaction)
        {
            command.Transaction = dbTransaction;
        }

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var nextSequence = Convert.ToInt64(result);

        logger.LogDebug("Next sequence for groupKey={GroupKey} is {Sequence}", groupKey, nextSequence);

        return nextSequence;
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(
        object entryId,
        OutboxStatus status,
        DateTime? completedAt,
        DateTime? lastAttemptedAt,
        int? retryCount,
        string? latestError,
        OutboxAttempt? attempt,
        int maxAttempts = OutboxEntry.DefaultMaxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryId);

        var id = ConvertToGuid(entryId);

        logger.LogDebug(
            "Updating outbox entry status: id={Id}, newStatus={Status}",
            id, status);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entry = await dbContext.OutboxEntries
            .FirstOrDefaultAsync(e => e.Id != null && (Guid)e.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            logger.LogWarning("Outbox entry not found for status update: id={Id}", id);
            return;
        }

        entry.Status = status;

        if (completedAt.HasValue)
        {
            entry.CompletedAt = completedAt.Value;
        }

        if (lastAttemptedAt.HasValue)
        {
            entry.LastAttemptedAt = lastAttemptedAt.Value;
        }

        if (retryCount.HasValue)
        {
            entry.RetryCount = retryCount.Value;
        }

        entry.LatestError = latestError;

        if (attempt is not null)
        {
            entry.AddAttempt(attempt, maxAttempts);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogDebug("Outbox entry status updated: id={Id}, status={Status}", id, status);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxEntry>> GetGroupHeadsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        logger.LogDebug("Getting group heads with limit={Limit}", limit);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Use a subquery approach: for each group, find the minimum sequence
        // among non-completed entries, then fetch those entries
        var groupHeads = await dbContext.OutboxEntries
            .Where(e => e.Status != OutboxStatus.Completed)
            .GroupBy(e => e.GroupKey)
            .Select(g => new
            {
                GroupKey = g.Key,
                MinSequence = g.Min(e => e.Sequence)
            })
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (groupHeads.Count == 0)
        {
            logger.LogDebug("No group heads found");
            return [];
        }

        // Build a list of (GroupKey, MinSequence) pairs to fetch
        var headEntries = new List<OutboxEntry>();
        foreach (var head in groupHeads)
        {
            var entry = await dbContext.OutboxEntries
                .FirstOrDefaultAsync(
                    e => e.GroupKey == head.GroupKey && e.Sequence == head.MinSequence,
                    cancellationToken)
                .ConfigureAwait(false);

            if (entry is not null)
            {
                headEntries.Add(entry);
            }
        }

        logger.LogDebug("Retrieved {Count} group heads", headEntries.Count);

        return headEntries;
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
            logger.LogDebug("No eligible groups provided, returning empty batch");
            return [];
        }

        logger.LogDebug(
            "Getting batch with {EligibleGroupsCount} eligible groups, batchSize={BatchSize}",
            groupsList.Count, batchSize);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entries = await dbContext.OutboxEntries
            .Where(e => groupsList.Contains(e.GroupKey) && e.Status != OutboxStatus.Completed)
            .OrderBy(e => e.GroupKey)
            .ThenBy(e => e.Sequence)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        logger.LogDebug("Retrieved batch of {Count} entries", entries.Count);

        return entries;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteCompletedEntriesAsync(
        DateTime completedBefore,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        logger.LogDebug(
            "Deleting completed entries before {CompletedBefore} with batchSize={BatchSize}",
            completedBefore, batchSize);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Use ExecuteDeleteAsync for bulk delete (EF Core 7+)
        var deletedCount = await dbContext.OutboxEntries
            .Where(e => e.Status == OutboxStatus.Completed && e.CompletedAt < completedBefore)
            .OrderBy(e => e.CompletedAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Deleted {Count} completed outbox entries", deletedCount);

        return deletedCount;
    }

    #region ILeaseRepository Implementation

    /// <inheritdoc/>
    public async Task<LeaseAcquisitionResult> TryAcquireOrRenewLeaseAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        }

        var now = DateTime.UtcNow;
        var leaseUntil = now.Add(leaseDuration);

        logger.LogDebug("Worker {WorkerId} attempting to acquire/renew lease", workerId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Try to acquire or renew: update where lease expired OR worker already owns it
        var rowsAffected = await dbContext.Leases
            .Where(l => l.Id == LeaseEntity.GlobalLeaseId &&
                       (l.LeaseUntil < now || l.WorkerId == workerId))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(l => l.WorkerId, workerId)
                    .SetProperty(l => l.LeaseUntil, leaseUntil),
                cancellationToken)
            .ConfigureAwait(false);

        if (rowsAffected > 0)
        {
            logger.LogInformation(
                "Worker {WorkerId} acquired/renewed lease until {LeaseUntil}",
                workerId, leaseUntil);
            return new LeaseAcquisitionResult(Acquired: true, LeaseUntil: leaseUntil);
        }

        // Lease is held by another worker - get the current state
        var currentLease = await dbContext.Leases
            .FirstOrDefaultAsync(l => l.Id == LeaseEntity.GlobalLeaseId, cancellationToken)
            .ConfigureAwait(false);

        if (currentLease is not null)
        {
            logger.LogDebug(
                "Lease held by {OtherWorkerId} until {LeaseUntil}",
                currentLease.WorkerId, currentLease.LeaseUntil);
            return new LeaseAcquisitionResult(
                Acquired: false,
                LeaseUntil: currentLease.LeaseUntil,
                CurrentHolderId: currentLease.WorkerId);
        }

        return new LeaseAcquisitionResult(Acquired: false, LeaseUntil: DateTime.MinValue);
    }

    /// <inheritdoc/>
    public async Task<bool> ReleaseLeaseAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        logger.LogDebug("Worker {WorkerId} releasing lease", workerId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Set LeaseUntil to now to allow immediate re-acquisition
        var rowsAffected = await dbContext.Leases
            .Where(l => l.Id == LeaseEntity.GlobalLeaseId && l.WorkerId == workerId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(l => l.LeaseUntil, DateTime.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);

        if (rowsAffected > 0)
        {
            logger.LogInformation("Worker {WorkerId} released lease", workerId);
            return true;
        }

        logger.LogDebug("Worker {WorkerId} had no lease to release", workerId);
        return false;
    }

    /// <inheritdoc/>
    public async Task EnsureLeaseExistsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Ensuring lease row exists");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var exists = await dbContext.Leases
            .AnyAsync(l => l.Id == LeaseEntity.GlobalLeaseId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            var lease = new LeaseEntity
            {
                Id = LeaseEntity.GlobalLeaseId,
                WorkerId = null,
                LeaseUntil = DateTime.MinValue
            };

            dbContext.Leases.Add(lease);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                logger.LogDebug("Lease row created");
            }
            catch (DbUpdateException)
            {
                // Race condition - another worker created the row
                logger.LogDebug("Lease row already exists (created by another worker)");
            }
        }
        else
        {
            logger.LogDebug("Lease row already exists");
        }
    }

    #endregion

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
