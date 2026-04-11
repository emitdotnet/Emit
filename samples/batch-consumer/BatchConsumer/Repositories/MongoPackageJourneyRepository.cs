namespace BatchConsumer.Repositories;

using BatchConsumer.Domain;
using Emit.MongoDB;
using Emit.MongoDB.Configuration;
using global::MongoDB.Bson.Serialization.Attributes;
using global::MongoDB.Driver;

internal sealed class MongoPackageJourneyRepository(
    MongoDbContext context,
    IMongoSessionAccessor sessionAccessor) : IPackageJourneyRepository
{
    private readonly IMongoCollection<PackageJourneyDocument> collection =
        context.Database.GetCollection<PackageJourneyDocument>("package_journeys");

    public async Task UpsertAsync(PackageScan scan, CancellationToken cancellationToken)
    {
        var session = sessionAccessor.Session
            ?? throw new InvalidOperationException(
                "No active MongoDB session. Ensure a unit of work has been started " +
                "(e.g., via [Transactional] attribute or IUnitOfWork.BeginAsync).");

        var filter = Builders<PackageJourneyDocument>.Filter.Eq(d => d.Barcode, scan.Barcode);

        var update = Builders<PackageJourneyDocument>.Update
            .Set(d => d.FacilityId, scan.FacilityId)
            .Set(d => d.CurrentLane, scan.Lane)
            .Set(d => d.DestinationZip, scan.DestinationZip)
            .Set(d => d.LastSeenAt, scan.ScannedAt)
            .Inc(d => d.ScanCount, 1)
            .SetOnInsert(d => d.Rerouted, false);

        await collection.UpdateOneAsync(
            session,
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkReroutedAsync(string barcode, CancellationToken cancellationToken)
    {
        var session = sessionAccessor.Session
            ?? throw new InvalidOperationException(
                "No active MongoDB session. Ensure a unit of work has been started " +
                "(e.g., via [Transactional] attribute or IUnitOfWork.BeginAsync).");

        var filter = Builders<PackageJourneyDocument>.Filter.Eq(d => d.Barcode, barcode);
        var update = Builders<PackageJourneyDocument>.Update.Set(d => d.Rerouted, true);

        await collection.UpdateOneAsync(session, filter, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class PackageJourneyDocument
{
    [BsonId] public string Barcode { get; set; } = default!;
    public string FacilityId { get; set; } = default!;
    public int CurrentLane { get; set; }
    public string DestinationZip { get; set; } = default!;
    public int ScanCount { get; set; }
    public bool Rerouted { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
