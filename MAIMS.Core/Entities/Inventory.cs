using MAIMS.Core.Enums;

namespace MAIMS.Core.Entities;

/// <summary>
/// Inventory item master record. One SKU = one Item.
/// Per-item reorder policy drives the low-stock dashboard.
/// </summary>
public class Item : BaseEntity
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public UnitOfMeasure Uom { get; set; } = UnitOfMeasure.EA;
    public decimal ReorderPoint { get; set; }
    public decimal ReorderQty { get; set; }
    public decimal? UnitCost { get; set; }
    public string? PreferredSupplier { get; set; }
    public int? LeadTimeDays { get; set; }
    public bool HazardousFlag { get; set; }
    public string? StorageRequirements { get; set; }
    public string? Manufacturer { get; set; }
    public string? ManufacturerPartNumber { get; set; }

    public ICollection<StockBalance> StockBalances { get; set; } = new List<StockBalance>();
    public ICollection<StockTransaction> Transactions { get; set; } = new List<StockTransaction>();
}

/// <summary>
/// Warehouse or storage area. Belongs to a department; cross-warehouse transfers require approval.
/// </summary>
public class Warehouse : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public long DepartmentId { get; set; }
    public long? LocationId { get; set; }
    public bool IsActive { get; set; } = true;

    public Department? Department { get; set; }
    public Location? Location { get; set; }
    public ICollection<StockBalance> StockBalances { get; set; } = new List<StockBalance>();
}

/// <summary>
/// Running balance per item per warehouse per bin. Updated atomically by every stock transaction.
/// </summary>
public class StockBalance : BaseEntity
{
    public long ItemId { get; set; }
    public long WarehouseId { get; set; }
    public string? BinLocation { get; set; }   // aisle-shelf-bin
    public decimal QtyOnHand { get; set; }
    public decimal QtyReserved { get; set; }
    public decimal QtyOnOrder { get; set; }

    public Item? Item { get; set; }
    public Warehouse? Warehouse { get; set; }
}

/// <summary>
/// Immutable record of a stock movement. One row per receipt/issue/transfer/adjustment/write-off.
/// Together with StockBalance this forms a double-entry-style audit trail for inventory.
/// </summary>
public class StockTransaction : BaseEntity
{
    public StockTransactionType TransactionType { get; set; }
    public long ItemId { get; set; }
    public long WarehouseId { get; set; }
    public decimal Quantity { get; set; }            // positive = in, negative = out
    public long? FromWarehouseId { get; set; }       // for transfers
    public long? ToWarehouseId { get; set; }         // for transfers
    public long? ToAssetId { get; set; }             // when issuing to an asset
    public long? RequesterUserId { get; set; }
    public long? PerformedBy { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    public string? ReasonCode { get; set; }
    public string? ReferenceDocNo { get; set; }
    public string? LotBatch { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Supplier { get; set; }
    public string? Notes { get; set; }

    public Item? Item { get; set; }
    public Warehouse? Warehouse { get; set; }
    public Warehouse? FromWarehouse { get; set; }
    public Warehouse? ToWarehouse { get; set; }
    public Asset? ToAsset { get; set; }
    public User? Requester { get; set; }
    public User? PerformedByUser { get; set; }
}
