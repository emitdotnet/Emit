namespace Emit.EntityFrameworkCore.Daemon;

using System.Data.Common;
using Emit.Abstractions.Daemon;
using Emit.EntityFrameworkCore.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

/// <summary>
/// Daemon that periodically removes expired inbox records, a batch at a time. Each record carries its own expiry, so
/// services sharing the database with different retentions each keep theirs; each service runs its own daemon, and
/// rows another service's daemon is deleting are skipped rather than waited on.
/// </summary>
/// <typeparam name="TDbContext">The user's DbContext type.</typeparam>
internal sealed class InboxCleanupDaemon<TDbContext>(
    IDbContextFactory<TDbContext> dbContextFactory,
    IOptions<EntityFrameworkCoreInboxOptions> options,
    TimeProvider timeProvider,
    ILogger<InboxCleanupDaemon<TDbContext>> logger)
    : ExpiredRowsCleanupDaemon<TDbContext>(dbContextFactory, timeProvider, logger)
    where TDbContext : DbContext
{
    /// <inheritdoc/>
    public override string DaemonId => "emit:inbox-cleanup";

    /// <inheritdoc/>
    protected override TimeSpan CleanupInterval => options.Value.CleanupInterval;

    /// <inheritdoc/>
    protected override async Task<int> DeleteExpiredAsync(
        DbConnection connection,
        DaemonRunContext context,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            DELETE FROM "{TableNames.Inbox}" WHERE ctid IN (
                SELECT ctid FROM "{TableNames.Inbox}" WHERE expires_at < @now LIMIT @batchSize FOR UPDATE SKIP LOCKED)
            """;

        var batchSize = options.Value.CleanupBatchSize;
        var total = 0;
        int deleted;
        do
        {
            if (!context.HoldsAssignment)
            {
                break;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("now", TimeProvider.GetUtcNow().UtcDateTime));
            command.Parameters.Add(new NpgsqlParameter("batchSize", batchSize));
            deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            total += deleted;
        }
        while (deleted == batchSize);

        return total;
    }
}
