namespace Emit.EntityFrameworkCore.Daemon;

using System.Data.Common;
using Emit.Abstractions.Daemon;
using Emit.EntityFrameworkCore.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Daemon that periodically removes expired distributed lock rows.
/// </summary>
/// <typeparam name="TDbContext">The user's DbContext type.</typeparam>
internal sealed class LockCleanupDaemon<TDbContext>(
    IDbContextFactory<TDbContext> dbContextFactory,
    IOptions<LockCleanupOptions> options,
    TimeProvider timeProvider,
    ILogger<LockCleanupDaemon<TDbContext>> logger)
    : ExpiredRowsCleanupDaemon<TDbContext>(dbContextFactory, timeProvider, logger)
    where TDbContext : DbContext
{
    /// <inheritdoc/>
    public override string DaemonId => "emit:lock-cleanup";

    /// <inheritdoc/>
    protected override TimeSpan CleanupInterval => options.Value.CleanupInterval;

    /// <inheritdoc/>
    protected override async Task<int> DeleteExpiredAsync(
        DbConnection connection,
        DaemonRunContext context,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM "{TableNames.Locks}" WHERE expires_at < clock_timestamp()
            """;
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
