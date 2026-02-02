namespace Emit.Worker;

using Emit.Abstractions;
using Emit.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that periodically purges completed outbox entries older than the retention period.
/// </summary>
/// <remarks>
/// <para>
/// This worker runs independently of the main <see cref="OutboxWorker"/> and is responsible for
/// cleaning up completed entries to prevent unbounded growth of the outbox table/collection.
/// </para>
/// <para>
/// The cleanup runs at the configured <see cref="CleanupOptions.CleanupInterval"/> and deletes
/// entries in batches to avoid overwhelming the database.
/// </para>
/// </remarks>
internal sealed class CompletedEntriesCleanupWorker : BackgroundService
{
    private readonly IOutboxRepository repository;
    private readonly CleanupOptions options;
    private readonly ILogger<CompletedEntriesCleanupWorker> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompletedEntriesCleanupWorker"/> class.
    /// </summary>
    /// <param name="repository">The outbox repository.</param>
    /// <param name="options">The cleanup options.</param>
    /// <param name="logger">The logger.</param>
    public CompletedEntriesCleanupWorker(
        IOutboxRepository repository,
        IOptions<CleanupOptions> options,
        ILogger<CompletedEntriesCleanupWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.repository = repository;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Cleanup worker starting: retentionPeriod={RetentionPeriod}, cleanupInterval={CleanupInterval}, batchSize={BatchSize}",
            options.RetentionPeriod,
            options.CleanupInterval,
            options.BatchSize);

        using var timer = new PeriodicTimer(options.CleanupInterval);

        try
        {
            // Run once at startup, then on interval
            await RunCleanupAsync(stoppingToken).ConfigureAwait(false);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunCleanupAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Cleanup worker stopping due to cancellation");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cleanup worker encountered fatal error");
            throw;
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        var cutoffDate = DateTime.UtcNow - options.RetentionPeriod;

        logger.LogDebug(
            "Starting cleanup of completed entries older than {CutoffDate}",
            cutoffDate);

        try
        {
            var totalDeleted = 0;
            int deletedInBatch;

            // Delete in batches until no more entries match
            do
            {
                deletedInBatch = await repository.DeleteCompletedEntriesAsync(
                    cutoffDate,
                    options.BatchSize,
                    cancellationToken).ConfigureAwait(false);

                totalDeleted += deletedInBatch;

                if (deletedInBatch > 0)
                {
                    logger.LogDebug(
                        "Deleted {BatchCount} completed entries (total so far: {TotalCount})",
                        deletedInBatch,
                        totalDeleted);
                }
            }
            while (deletedInBatch == options.BatchSize && !cancellationToken.IsCancellationRequested);

            if (totalDeleted > 0)
            {
                logger.LogInformation(
                    "Cleanup completed: deleted {TotalCount} entries older than {CutoffDate}",
                    totalDeleted,
                    cutoffDate);
            }
            else
            {
                logger.LogDebug(
                    "Cleanup completed: no entries older than {CutoffDate} found",
                    cutoffDate);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Cleanup failed: error deleting completed entries older than {CutoffDate}",
                cutoffDate);
        }
    }
}
