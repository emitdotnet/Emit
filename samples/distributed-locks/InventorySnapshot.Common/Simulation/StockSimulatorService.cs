namespace InventorySnapshot.Common.Simulation;

using InventorySnapshot.Common.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Simulates warehouse activity by randomly adjusting stock levels — deliveries, outbound
/// shipments, and the occasional inventory discrepancy. Runs continuously in the background
/// so that each snapshot captures a different state.
/// </summary>
public sealed class StockSimulatorService(
    IProductRepository products,
    IConfiguration configuration,
    ILogger<StockSimulatorService> logger) : BackgroundService
{
    private static readonly string[] Skus =
    [
        "WRENCH-12", "BOLT-M8", "CABLE-CAT6", "GLOVE-L", "HELMET-XL",
        "PUMP-2HP", "TAPE-50M", "DRILL-BIT", "VALVE-1IN", "FILTER-OIL",
    ];

    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[Simulator] Stock simulator started — warehouse activity begins.");

        var periodStr = configuration["Simulation:Period"];
        var period = TimeSpan.TryParse(periodStr, out var parsed) ? parsed : TimeSpan.FromSeconds(5);
        using var timer = new PeriodicTimer(period);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SimulateActivityAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Simulator] Simulation tick failed — continuing.");
            }
        }

        logger.LogInformation("[Simulator] Stock simulator stopped.");
    }

    private async Task SimulateActivityAsync(CancellationToken ct)
    {
        // Pick 1–3 products to touch this tick
        var count = _random.Next(1, 4);

        for (var i = 0; i < count; i++)
        {
            var sku = Skus[_random.Next(Skus.Length)];
            var roll = _random.Next(100);

            // 50 % delivery (positive), 40 % shipment (negative), 10 % discrepancy (small negative)
            var delta = roll switch
            {
                < 50  => _random.Next(10, 51),       // delivery: +10 to +50 units
                < 90  => -_random.Next(1, 21),        // shipment: -1 to -20 units
                _     => -_random.Next(1, 6),         // shrinkage: -1 to -5 units
            };

            var eventType = delta switch
            {
                > 0   => "Delivery",
                < -10 => "Shipment",
                _     => "Discrepancy",
            };

            await products.AdjustStockAsync(sku, delta, ct).ConfigureAwait(false);

            logger.LogInformation(
                "[Simulator] {EventType} — {Sku} {Delta:+#;-#;0} units",
                eventType, sku, delta);
        }
    }
}
