namespace InventorySnapshot.MongoDB.Repositories;

using global::MongoDB.Bson;
using global::MongoDB.Driver;
using InventorySnapshot.Common.Domain;
using InventorySnapshot.Common.Repositories;

internal sealed class MongoProductRepository : IProductRepository
{
    private static readonly string[] Seeds =
    [
        "WRENCH-12", "BOLT-M8", "CABLE-CAT6", "GLOVE-L", "HELMET-XL",
        "PUMP-2HP", "TAPE-50M", "DRILL-BIT", "VALVE-1IN", "FILTER-OIL",
    ];

    private static readonly Dictionary<string, (string Name, string WarehouseId, int InitialStock)> Catalog = new()
    {
        ["WRENCH-12"]   = ("12mm Socket Wrench",     "WH-NORTH", 250),
        ["BOLT-M8"]     = ("M8 Hex Bolt (pack)",      "WH-NORTH", 1500),
        ["CABLE-CAT6"]  = ("CAT6 Cable 5m",           "WH-SOUTH", 340),
        ["GLOVE-L"]     = ("Work Gloves (L)",          "WH-EAST",  85),
        ["HELMET-XL"]   = ("Safety Helmet (XL)",       "WH-EAST",  42),
        ["PUMP-2HP"]    = ("2HP Water Pump",           "WH-SOUTH", 18),
        ["TAPE-50M"]    = ("Electrical Tape 50m",      "WH-NORTH", 620),
        ["DRILL-BIT"]   = ("HSS Drill Bit Set",        "WH-WEST",  95),
        ["VALVE-1IN"]   = ("Ball Valve 1-inch",        "WH-WEST",  160),
        ["FILTER-OIL"]  = ("Oil Filter (generic)",     "WH-EAST",  430),
    };

    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoProductRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<BsonDocument>("products");
        SeedIfEmpty();
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var docs = await _collection
            .Find(FilterDefinition<BsonDocument>.Empty)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return docs.Select(d => new Product
        {
            Sku         = d["sku"].AsString,
            Name        = d["name"].AsString,
            WarehouseId = d["warehouse_id"].AsString,
            Stock       = d["stock"].AsInt32,
        }).ToList();
    }

    public async Task AdjustStockAsync(string sku, int delta, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("sku", sku);
        var update = Builders<BsonDocument>.Update.Inc("stock", delta);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private void SeedIfEmpty()
    {
        if (_collection.CountDocuments(FilterDefinition<BsonDocument>.Empty) > 0)
        {
            return;
        }

        var docs = Seeds.Select(sku =>
        {
            var (name, warehouseId, stock) = Catalog[sku];
            return new BsonDocument
            {
                ["sku"]          = sku,
                ["name"]         = name,
                ["warehouse_id"] = warehouseId,
                ["stock"]        = stock,
            };
        }).ToList();

        _collection.InsertMany(docs);
    }
}
