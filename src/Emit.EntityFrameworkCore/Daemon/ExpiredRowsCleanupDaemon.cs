namespace Emit.EntityFrameworkCore.Daemon;

using System.Data;
using System.Data.Common;
using Emit.Abstractions.Daemon;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Base of the daemons that periodically remove expired rows from one of Emit's tables. As daemons they run on one
/// node of the service, so adding replicas adds no cleanup queries.
/// </summary>
/// <typeparam name="TDbContext">The user's DbContext type.</typeparam>
internal abstract class ExpiredRowsCleanupDaemon<TDbContext>(
    IDbContextFactory<TDbContext> dbContextFactory,
    TimeProvider timeProvider,
    ILogger logger) : IDaemonAgent
    where TDbContext : DbContext
{
    /// <inheritdoc/>
    public abstract string DaemonId { get; }

    /// <summary>
    /// Gets the interval between two cleanups.
    /// </summary>
    protected abstract TimeSpan CleanupInterval { get; }

    /// <summary>
    /// Gets the clock expiries are compared against.
    /// </summary>
    protected TimeProvider TimeProvider => timeProvider;

    /// <inheritdoc/>
    public async Task RunAsync(DaemonRunContext context)
    {
        var cancellationToken = context.AssignmentToken;
        using var timer = new PeriodicTimer(CleanupInterval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var deleted = await CleanupAsync(context, cancellationToken).ConfigureAwait(false);
                    if (deleted > 0)
                    {
                        logger.LogInformation("{DaemonId} deleted {Count} expired row(s)", DaemonId, deleted);
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // The next tick tries again; a node that lost the database also loses the assignment
                    logger.LogError(ex, "{DaemonId} failed to delete expired rows", DaemonId);
                }
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // The assignment ended: whatever the cancelled cleanup threw, the run is over
        }
    }

    /// <summary>
    /// Deletes expired rows on an open connection to the primary.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="context">The run, read before each delete.</param>
    /// <param name="cancellationToken">Cancelled when the run must stop.</param>
    /// <returns>The number of rows deleted.</returns>
    protected abstract Task<int> DeleteExpiredAsync(
        DbConnection connection,
        DaemonRunContext context,
        CancellationToken cancellationToken);

    private async Task<int> CleanupAsync(DaemonRunContext context, CancellationToken cancellationToken)
    {
        if (!context.HoldsAssignment)
        {
            return 0;
        }

        await using var dbContext = await dbContextFactory.CreateOnPrimaryAsync(cancellationToken).ConfigureAwait(false);
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        return await DeleteExpiredAsync(connection, context, cancellationToken).ConfigureAwait(false);
    }
}
