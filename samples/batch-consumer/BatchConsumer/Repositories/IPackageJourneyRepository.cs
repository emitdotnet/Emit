namespace BatchConsumer.Repositories;

using BatchConsumer.Domain;

/// <summary>
/// Persistence contract for tracking package journeys through the sorting facility.
/// </summary>
public interface IPackageJourneyRepository
{
    /// <summary>
    /// Inserts or updates a package journey record from a scan event.
    /// </summary>
    Task UpsertAsync(PackageScan scan, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a package as rerouted.
    /// </summary>
    Task MarkReroutedAsync(string barcode, CancellationToken cancellationToken);
}
