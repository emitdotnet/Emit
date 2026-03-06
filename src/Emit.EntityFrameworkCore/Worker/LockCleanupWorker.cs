namespace Emit.EntityFrameworkCore.Worker;

using System.Data;
using Emit.EntityFrameworkCore.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that periodically cleans up expired lock rows from PostgreSQL.
/// </summary>
/// <typeparam name="TDbContext">The user's DbContext type.</typeparam>
internal sealed class LockCleanupWorker<TDbContext>(
    IDbContextFactory<TDbContext> dbContextFactory,
    IOptions<LockCleanupOptions> options,
    ILogger<LockCleanupWorker<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Lock cleanup worker starting: cleanupInterval={CleanupInterval}",
            options.Value.CleanupInterval);

        using var timer = new PeriodicTimer(options.Value.CleanupInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var deleted = await DeleteExpiredLocksAsync(stoppingToken).ConfigureAwait(false);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} expired lock(s)", deleted);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during lock cleanup");
            }
        }

        // Normal shutdown
    }

    private async Task<int> DeleteExpiredLocksAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            DELETE FROM "{TableNames.Locks}" WHERE expires_at < clock_timestamp()
            """;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
