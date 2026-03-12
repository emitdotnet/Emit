namespace InventorySnapshot.Common.Domain;

/// <summary>
/// Canonical product catalog used to seed the inventory database.
/// Both persistence backends reference this single source of truth.
/// </summary>
public static class ProductCatalog
{
    public static readonly IReadOnlyList<Product> Seeds =
    [
        new() { Sku = "WRENCH-12",  Name = "12mm Socket Wrench",  WarehouseId = "WH-NORTH", Stock = 250  },
        new() { Sku = "BOLT-M8",    Name = "M8 Hex Bolt (pack)",  WarehouseId = "WH-NORTH", Stock = 1500 },
        new() { Sku = "CABLE-CAT6", Name = "CAT6 Cable 5m",       WarehouseId = "WH-SOUTH", Stock = 340  },
        new() { Sku = "GLOVE-L",    Name = "Work Gloves (L)",      WarehouseId = "WH-EAST",  Stock = 85   },
        new() { Sku = "HELMET-XL",  Name = "Safety Helmet (XL)",   WarehouseId = "WH-EAST",  Stock = 42   },
        new() { Sku = "PUMP-2HP",   Name = "2HP Water Pump",       WarehouseId = "WH-SOUTH", Stock = 18   },
        new() { Sku = "TAPE-50M",   Name = "Electrical Tape 50m",  WarehouseId = "WH-NORTH", Stock = 620  },
        new() { Sku = "DRILL-BIT",  Name = "HSS Drill Bit Set",    WarehouseId = "WH-WEST",  Stock = 95   },
        new() { Sku = "VALVE-1IN",  Name = "Ball Valve 1-inch",    WarehouseId = "WH-WEST",  Stock = 160  },
        new() { Sku = "FILTER-OIL", Name = "Oil Filter (generic)", WarehouseId = "WH-EAST",  Stock = 430  },
    ];
}
