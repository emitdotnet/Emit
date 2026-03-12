namespace InventorySnapshot.MongoDB.Repositories;

using global::MongoDB.Driver;
using InventorySnapshot.Common.Domain;
using InventorySnapshot.Common.Repositories;

internal sealed class MongoSnapshotRepository(IMongoDatabase database) : ISnapshotRepository
{
    private readonly IMongoCollection<StockSnapshot> _collection =
        database.GetCollection<StockSnapshot>("snapshots");

    public async Task SaveAsync(StockSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await _collection.InsertOneAsync(snapshot, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        var count = await _collection
            .CountDocumentsAsync(FilterDefinition<StockSnapshot>.Empty, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return (int)count;
    }
}
