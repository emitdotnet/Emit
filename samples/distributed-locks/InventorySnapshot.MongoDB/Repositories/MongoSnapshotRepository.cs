namespace InventorySnapshot.MongoDB.Repositories;

using global::MongoDB.Bson;
using global::MongoDB.Driver;
using InventorySnapshot.Common.Domain;
using InventorySnapshot.Common.Repositories;

internal sealed class MongoSnapshotRepository(IMongoDatabase database) : ISnapshotRepository
{
    private readonly IMongoCollection<BsonDocument> _collection =
        database.GetCollection<BsonDocument>("snapshots");

    public async Task SaveAsync(StockSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var doc = new BsonDocument
        {
            ["_id"]            = snapshot.Id.ToString(),
            ["taken_at"]       = snapshot.TakenAt,
            ["total_products"] = snapshot.TotalProducts,
            ["total_units"]    = snapshot.TotalUnits,
            ["warehouse_count"] = snapshot.WarehouseCount,
            ["duration_ms"]    = snapshot.Duration.TotalMilliseconds,
        };

        await _collection.InsertOneAsync(doc, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        var count = await _collection
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return (int)count;
    }
}
