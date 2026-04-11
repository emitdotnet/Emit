namespace BatchConsumer.Simulation;

using BatchConsumer.Domain;
using Emit.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Simulates conveyor belt barcode scanners producing package scan events at varying rates.
/// Cycles through Normal → Burst → Quiet phases to exercise batch accumulation at different volumes.
/// </summary>
public sealed class ConveyorSimulatorService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ConveyorSimulatorService> logger) : BackgroundService
{
    private static readonly string[] Facilities = ["FAC-NORTH", "FAC-SOUTH", "FAC-EAST", "FAC-WEST"];
    private static readonly string[] ZipPrefixes = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

    private enum Phase { Normal, Burst, Quiet }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[Simulator] ConveyorSimulatorService starting — waiting 5 s for infrastructure");

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

        logger.LogInformation("[Simulator] Starting simulation loop");

        var normalDuration = configuration.GetValue<TimeSpan?>("Simulation:NormalDuration") ?? TimeSpan.FromSeconds(30);
        var burstDuration = configuration.GetValue<TimeSpan?>("Simulation:BurstDuration") ?? TimeSpan.FromSeconds(8);
        var quietDuration = configuration.GetValue<TimeSpan?>("Simulation:QuietDuration") ?? TimeSpan.FromSeconds(15);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunPhaseAsync(Phase.Normal, normalDuration, delay: 3, stoppingToken).ConfigureAwait(false);
            await RunPhaseAsync(Phase.Burst, burstDuration, delay: 1, stoppingToken).ConfigureAwait(false);
            await RunPhaseAsync(Phase.Quiet, quietDuration, delay: 20, stoppingToken).ConfigureAwait(false);
        }

        logger.LogInformation("[Simulator] Simulation loop stopped");
    }

    private async Task RunPhaseAsync(Phase phase, TimeSpan duration, int delay, CancellationToken ct)
    {
        logger.LogInformation("[Simulator] Phase: {Phase} ({Duration}s, ~{Rate}/sec)",
            phase, duration.TotalSeconds, 1000 / delay);

        using var scope = scopeFactory.CreateScope();
        var producer = scope.ServiceProvider.GetRequiredService<IEventProducer<string, PackageScan>>();

        var deadline = DateTimeOffset.UtcNow + duration;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var scan = GenerateScan();

                await producer.ProduceAsync(scan.Barcode, scan, ct).ConfigureAwait(false);

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Simulator] Failed to produce scan — continuing");
            }
        }
    }

    private static PackageScan GenerateScan()
    {
        var roll = Random.Shared.Next(100);

        // 3% empty barcode
        var barcode = roll < 3
            ? ""
            : $"PKG-{Random.Shared.Next(100_000):D5}";

        // 2% negative weight (on valid barcodes)
        var weight = roll is >= 3 and < 5
            ? -1.0m
            : Math.Round((decimal)(Random.Shared.NextDouble() * 30 + 0.5), 2);

        // 1% unknown facility (on valid barcodes and weight)
        var facility = roll is >= 5 and < 6
            ? "FAC-UNKNOWN"
            : Facilities[Random.Shared.Next(Facilities.Length)];

        var zip = $"{ZipPrefixes[Random.Shared.Next(ZipPrefixes.Length)]}{Random.Shared.Next(1000, 9999)}";
        var correctLane = DetermineCorrectLane(zip);

        // 8% wrong lane (valid data, triggers reroute)
        var lane = roll is >= 6 and < 14
            ? WrongLane(correctLane)
            : correctLane;

        return new PackageScan(
            barcode,
            facility,
            lane,
            zip,
            weight,
            DateTimeOffset.UtcNow);
    }

    private static int DetermineCorrectLane(string zip)
    {
        return (zip[0] - '0') switch
        {
            >= 0 and <= 2 => 1,
            >= 3 and <= 4 => 2,
            >= 5 and <= 6 => 3,
            _ => 4
        };
    }

    private static int WrongLane(int correctLane)
    {
        int wrong;
        do
        {
            wrong = Random.Shared.Next(1, 5);
        } while (wrong == correctLane);

        return wrong;
    }
}
