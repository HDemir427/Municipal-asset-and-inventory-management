using MAIMS.Core.Enums;

namespace MAIMS.Core.DTOs;

public record StockReceiptDto(
    long ItemId,
    long WarehouseId,
    decimal Quantity,
    string? LotBatch,
    DateTime? ExpiryDate,
    string? Supplier,
    string? ReferenceDocNo,
    string? Notes,
    string? BinLocation = null);

public record StockIssueDto(
    long ItemId,
    long WarehouseId,
    decimal Quantity,
    long? ToAssetId,
    long? RequesterUserId,
    string? PurposeWorkOrder,
    string? ReferenceDocNo,
    string? Notes);

public record StockTransferDto(
    long ItemId,
    long FromWarehouseId,
    long ToWarehouseId,
    decimal Quantity,
    long ApprovedByUserId,
    string? ReferenceDocNo,
    string? Notes);

public record StockAdjustmentDto(
    long ItemId,
    long WarehouseId,
    decimal NewQuantity,           // absolute count after adjustment
    string ReasonCode,             // mandatory — see StockReasonCodes
    string? ReferenceDocNo,
    string? Notes);

public record StockWriteOffDto(
    long ItemId,
    long WarehouseId,
    decimal Quantity,
    string ReasonCode,
    long ApprovedByUserId,
    string? ReferenceDocNo,
    string? Notes);

public record StockBalanceReadDto(
    long ItemId,
    string Sku,
    string ItemName,
    long WarehouseId,
    string WarehouseName,
    string? BinLocation,
    decimal QtyOnHand,
    decimal QtyReserved,
    decimal QtyOnOrder,
    decimal ReorderPoint,
    bool BelowReorderPoint);

public record ItemCreateDto(
    string Sku,
    string Name,
    string? Description,
    string Category,
    UnitOfMeasure Uom,
    decimal ReorderPoint,
    decimal ReorderQty,
    decimal? UnitCost,
    string? PreferredSupplier,
    int? LeadTimeDays,
    bool HazardousFlag,
    string? StorageRequirements,
    string? Manufacturer,
    string? ManufacturerPartNumber);

/// <summary>
/// Used by IInventoryService.UpdateItemAsync. SKU is NOT included — SKU is
/// immutable after creation (it appears on printed labels, transaction history,
/// and audit logs). To change a SKU, delete the item and create a new one.
/// </summary>
public record ItemUpdateDto(
    long Id,
    string Name,
    string? Description,
    string Category,
    UnitOfMeasure Uom,
    decimal ReorderPoint,
    decimal ReorderQty,
    decimal? UnitCost,
    string? PreferredSupplier,
    int? LeadTimeDays,
    bool HazardousFlag,
    string? StorageRequirements,
    string? Manufacturer,
    string? ManufacturerPartNumber);

public record ItemReadDto(
    long Id,
    string Sku,
    string Name,
    string? Description,
    string Category,
    UnitOfMeasure Uom,
    decimal ReorderPoint,
    decimal ReorderQty,
    decimal? UnitCost,
    string? PreferredSupplier,
    int? LeadTimeDays,
    bool HazardousFlag,
    string? StorageRequirements,
    string? Manufacturer,
    string? ManufacturerPartNumber,
    DateTime CreatedAt);
