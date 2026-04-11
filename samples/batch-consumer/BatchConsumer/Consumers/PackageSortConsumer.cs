namespace BatchConsumer.Consumers;

using System.Diagnostics;
using BatchConsumer.Domain;
using BatchConsumer.Repositories;
using Emit.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Batch consumer that processes conveyor scan events, upserts package journeys,
/// and produces reroute commands for mis-sorted packages via the transactional outbox.
/// </summary>
[Transactional]
public sealed class PackageSortConsumer(
    IPackageJourneyRepository journeyRepository,
    IEventProducer<string, RerouteCommand> rerouteProducer,
    ILogger<PackageSortConsumer> logger) : IBatchConsumer<PackageScan>
{
    public async Task ConsumeAsync(
        ConsumeContext<MessageBatch<PackageScan>> context,
        CancellationToken cancellationToken)
    {
        var batch = context.Message;
        var sw = Stopwatch.StartNew();
        var rerouteCount = 0;

        logger.LogInformation("Processing batch of {Count} scans", batch.Count);

        foreach (var item in batch.Items)
        {
            var scan = item.Message;

            await journeyRepository.UpsertAsync(scan, cancellationToken).ConfigureAwait(false);

            var correctLane = DetermineCorrectLane(scan.DestinationZip);

            if (scan.Lane != correctLane)
            {
                logger.LogWarning(
                    "Mis-sorted: {Barcode} on lane {CurrentLane}, should be lane {CorrectLane} (zip {Zip})",
                    scan.Barcode, scan.Lane, correctLane, scan.DestinationZip);

                await journeyRepository.MarkReroutedAsync(scan.Barcode, cancellationToken)
                    .ConfigureAwait(false);

                var command = new RerouteCommand(
                    scan.Barcode,
                    scan.Lane,
                    correctLane,
                    scan.FacilityId,
                    DateTimeOffset.UtcNow);

                await rerouteProducer.ProduceAsync(scan.Barcode, command, cancellationToken)
                    .ConfigureAwait(false);

                rerouteCount++;
            }
        }

        sw.Stop();

        logger.LogInformation(
            "Batch complete: {Count} scans, {Reroutes} reroutes, {ElapsedMs} ms",
            batch.Count, rerouteCount, sw.ElapsedMilliseconds);
    }

    private static int DetermineCorrectLane(string destinationZip)
    {
        if (string.IsNullOrEmpty(destinationZip))
            return 1;

        return (destinationZip[0] - '0') switch
        {
            >= 0 and <= 2 => 1,
            >= 3 and <= 4 => 2,
            >= 5 and <= 6 => 3,
            _ => 4
        };
    }
}
