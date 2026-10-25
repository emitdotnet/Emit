namespace InventorySnapshot.MongoDB.Repositories;

using global::MongoDB.Bson.Serialization;
using global::MongoDB.Driver;
using InventorySnapshot.Common.Domain;
using InventorySnapshot.Common.Repositories;

internal sealed class MongoProductRepository : IProductRepository
{
    private readonly IMongoCollection<Product> _collection;

    static MongoProductRepository()
    {
        // The SKU identifies a product, so it is the document id
        BsonClassMap.TryRegisterClassMap<Product>(map =>
        {
            map.AutoMap();
            map.MapIdMember(p => p.Sku);
        });
    }

    public MongoProductRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<Product>("products");
        SeedIfEmpty();
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(FilterDefinition<Product>.Empty)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AdjustStockAsync(string sku, int delta, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Product>.Filter.Eq(p => p.Sku, sku);
        var update = Builders<Product>.Update.Inc(p => p.Stock, delta);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private void SeedIfEmpty()
    {
        if (_collection.CountDocuments(FilterDefinition<Product>.Empty) > 0)
        {
            return;
        }

        _collection.InsertMany(ProductCatalog.Seeds);
    }
}
