namespace Emit.EntityFrameworkCore;

using Emit.Abstractions;
using Emit.EntityFrameworkCore.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

/// <summary>
/// Entity Framework Core (PostgreSQL) implementation of <see cref="IInboxRepository"/>. Records are inserted at once
/// on the user's scoped <see cref="DbContext"/>, in the transaction of the current unit of work, so a concurrent
/// insert of the same record waits for that transaction to end: a commit makes this one a duplicate, a rollback lets
/// it through.
/// </summary>
/// <typeparam name="TDbContext">The user's DbContext type.</typeparam>
internal sealed class EfCoreInboxRepository<TDbContext>(
    TDbContext dbContext,
    IOptions<EntityFrameworkCoreInboxOptions> options,
    TimeProvider timeProvider) : IInboxRepository
    where TDbContext : DbContext
{
    /// <inheritdoc/>
    public async Task<bool> TryRecordAsync(string handler, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(messageId);
        EnsureTransaction();

        var (handledAt, expiresAt) = Timestamps();
        var inserted = await dbContext.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO "{TableNames.Inbox}" (message_id, handler, handled_at, expires_at)
            VALUES (@messageId, @handler, @handledAt, @expiresAt)
            ON CONFLICT (message_id, handler) DO NOTHING
            """,
            [
                new NpgsqlParameter("messageId", messageId),
                new NpgsqlParameter("handler", handler),
                new NpgsqlParameter("handledAt", handledAt),
                new NpgsqlParameter("expiresAt", expiresAt),
            ],
            cancellationToken).ConfigureAwait(false);

        return inserted == 1;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<string>> RecordAsync(
        string handler,
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(messageIds);
        EnsureTransaction();

        if (messageIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        // One array parameter whatever the batch size, so a large batch never reaches the parameter limit
        var (handledAt, expiresAt) = Timestamps();
        var recorded = await dbContext.Database.SqlQueryRaw<string>(
            $"""
            INSERT INTO "{TableNames.Inbox}" (message_id, handler, handled_at, expires_at)
            SELECT message_id, @handler, @handledAt, @expiresAt FROM unnest(@messageIds) AS message_id
            ON CONFLICT (message_id, handler) DO NOTHING
            RETURNING message_id AS "Value"
            """,
            new NpgsqlParameter("messageIds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = messageIds.ToArray() },
            new NpgsqlParameter("handler", handler),
            new NpgsqlParameter("handledAt", handledAt),
            new NpgsqlParameter("expiresAt", expiresAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return recorded.ToHashSet(StringComparer.Ordinal);
    }

    private (DateTime HandledAt, DateTime ExpiresAt) Timestamps()
    {
        var handledAt = timeProvider.GetUtcNow().UtcDateTime;
        return (handledAt, handledAt + options.Value.Retention);
    }

    private void EnsureTransaction()
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "No transaction is active. The inbox records a message in the transaction of the consumer's unit of work.");
        }
    }
}
