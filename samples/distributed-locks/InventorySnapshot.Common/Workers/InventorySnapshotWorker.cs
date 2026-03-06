namespace InventorySnapshot.Common.Workers;

using System.Diagnostics;
using Emit.Abstractions;
using InventorySnapshot.Common.Domain;
using InventorySnapshot.Common.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Background worker that takes a periodic inventory snapshot under a distributed lock.
/// Only one instance across the cluster will compute and persist the snapshot per interval;
/// all others detect lock contention and skip that cycle.
/// </summary>
public sealed class InventorySnapshotWorker(
    IProductRepository products,
    ISnapshotRepository snapshots,
    IDistributedLockProvider lockProvider,
    IOptions<SnapshotWorkerOptions> options,
    ILogger<InventorySnapshotWorker> logger) : BackgroundService
{
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(30);
    private const string LockKey = "inventory-snapshot";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "[Snapshot] Worker started — snapshot interval: {Interval}",
            options.Value.SnapshotInterval);

        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(options.Value.SnapshotInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await TryTakeSnapshotAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Snapshot] Snapshot cycle failed — will retry next interval.");
            }
        }

        logger.LogInformation("[Snapshot] Worker stopped.");
    }

    private async Task TryTakeSnapshotAsync(CancellationToken ct)
    {
        // timeout = TimeSpan.Zero = single non-blocking attempt
        await using var handle = await lockProvider
            .TryAcquireAsync(LockKey, LockTtl, timeout: TimeSpan.Zero, ct)
            .ConfigureAwait(false);

        if (handle is null)
        {
            logger.LogInformation(
                "[Snapshot] Lock contention — another instance is already running the snapshot. Skipping this cycle.");
            return;
        }

        logger.LogInformation("[Snapshot] Lock acquired — computing inventory snapshot...");

        var sw = Stopwatch.StartNew();

        var allProducts = await products.GetAllAsync(ct).ConfigureAwait(false);

        var snapshot = new StockSnapshot
        {
            Id             = Guid.NewGuid(),
            TakenAt        = DateTime.UtcNow,
            TotalProducts  = allProducts.Count,
            TotalUnits     = allProducts.Sum(p => p.Stock),
            WarehouseCount = allProducts.Select(p => p.WarehouseId).Distinct().Count(),
            Duration       = sw.Elapsed,
        };

        await snapshots.SaveAsync(snapshot, ct).ConfigureAwait(false);

        var snapshotCount = await snapshots.GetCountAsync(ct).ConfigureAwait(false);

        logger.LogInformation(
            "[Snapshot] #{Count} — {Products} products, {Units} units across {Warehouses} warehouse(s) in {DurationMs}ms",
            snapshotCount,
            snapshot.TotalProducts,
            snapshot.TotalUnits,
            snapshot.WarehouseCount,
            snapshot.Duration.TotalMilliseconds.ToString("F1"));
    }
}

/// <summary>Configuration options for <see cref="InventorySnapshotWorker"/>.</summary>
public sealed class SnapshotWorkerOptions
{
    /// <summary>How often to attempt a snapshot. Defaults to 30 seconds.</summary>
    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromSeconds(30);
}
