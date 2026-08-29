using MAIMS.Core.Abstractions;
using MAIMS.Core.DTOs;
using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MAIMS.Services.Inventory;

/// <summary>
/// Inventory management: item catalog, stock receipt/issue/transfer/adjustment/write-off.
/// Every mutation runs in a transaction that updates StockBalance AND writes a StockTransaction
/// AND fires the audit interceptor — all atomic.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentSession _session;

    public InventoryService(IUnitOfWork uow, ICurrentSession session)
    {
        _uow = uow;
        _session = session;
    }

    public async Task<ItemReadDto> CreateItemAsync(ItemCreateDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryAdjust))
            throw new UnauthorizedAccessException("Missing permission: inventory.adjust");

        var ctx = GetContext();
        if (await ctx.Set<Item>().AnyAsync(i => i.Sku == dto.Sku, ct))
            throw new InvalidOperationException($"SKU '{dto.Sku}' already exists.");

        var item = new Item
        {
            Sku = dto.Sku,
            Name = dto.Name,
            Description = dto.Description,
            Category = dto.Category,
            Uom = dto.Uom,
            ReorderPoint = dto.ReorderPoint,
            ReorderQty = dto.ReorderQty,
            UnitCost = dto.UnitCost,
            PreferredSupplier = dto.PreferredSupplier,
            LeadTimeDays = dto.LeadTimeDays,
            HazardousFlag = dto.HazardousFlag,
            StorageRequirements = dto.StorageRequirements,
            Manufacturer = dto.Manufacturer,
            ManufacturerPartNumber = dto.ManufacturerPartNumber
        };
        ctx.Set<Item>().Add(item);
        await _uow.SaveChangesAsync(ct);

        return await GetItemAsync(item.Id, ct);
    }

    public async Task<ItemReadDto> UpdateItemAsync(ItemUpdateDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryAdjust))
            throw new UnauthorizedAccessException("Missing permission: inventory.adjust");

        var ctx = GetContext();
        var item = await ctx.Set<Item>().FirstOrDefaultAsync(i => i.Id == dto.Id, ct)
            ?? throw new KeyNotFoundException($"Item {dto.Id} not found.");

        // SKU is immutable — not updated here.
        item.Name = dto.Name;
        item.Description = dto.Description;
        item.Category = dto.Category;
        item.Uom = dto.Uom;
        item.ReorderPoint = dto.ReorderPoint;
        item.ReorderQty = dto.ReorderQty;
        item.UnitCost = dto.UnitCost;
        item.PreferredSupplier = dto.PreferredSupplier;
        item.LeadTimeDays = dto.LeadTimeDays;
        item.HazardousFlag = dto.HazardousFlag;
        item.StorageRequirements = dto.StorageRequirements;
        item.Manufacturer = dto.Manufacturer;
        item.ManufacturerPartNumber = dto.ManufacturerPartNumber;

        await _uow.SaveChangesAsync(ct);
        return await GetItemAsync(item.Id, ct);
    }

    public async Task DeleteItemAsync(long id, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryAdjust))
            throw new UnauthorizedAccessException("Missing permission: inventory.adjust");

        var ctx = GetContext();
        var item = await ctx.Set<Item>().FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException($"Item {id} not found.");

        // Safety check: refuse to delete if the item has stock balances with non-zero quantities.
        var balances = await ctx.Set<StockBalance>()
            .Where(b => b.ItemId == id)
            .ToListAsync(ct);
        if (balances.Any(b => b.QtyOnHand > 0 || b.QtyReserved > 0 || b.QtyOnOrder > 0))
            throw new InvalidOperationException(
                "Cannot delete an item that has non-zero stock balances. " +
                "Issue or write off all stock first, then delete the item.");

        // Safety check: refuse to delete if any stock transactions reference this item
        // (audit trail integrity — historical transactions must remain valid).
        var hasTransactions = await ctx.Set<StockTransaction>()
            .AnyAsync(t => t.ItemId == id, ct);
        if (hasTransactions)
            throw new InvalidOperationException(
                "Cannot delete an item that has historical stock transactions. " +
                "Historical transactions reference this item for audit trail integrity. " +
                "Consider deactivating the item instead (set ReorderPoint = 0 and " +
                "update Name to indicate it is obsolete).");

        // Soft-delete the item. The EF Core global query filter
        // (HasQueryFilter(!IsDeleted)) will automatically hide it from
        // SearchItemsAsync and other queries.
        item.IsDeleted = true;
        item.DeletedAt = DateTime.UtcNow;
        ctx.Set<Item>().Update(item);

        // Also soft-delete any zero-quantity StockBalance rows for this item
        // (clean-up — these are empty balance records that serve no purpose
        // once the item itself is deleted).
        foreach (var bal in balances)
        {
            bal.IsDeleted = true;
            bal.DeletedAt = DateTime.UtcNow;
            ctx.Set<StockBalance>().Update(bal);
        }

        await _uow.SaveChangesAsync(ct);
    }

    public async Task<ItemReadDto> GetItemAsync(long id, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryView))
            throw new UnauthorizedAccessException("Missing permission: inventory.view");

        var ctx = GetContext();
        var item = await ctx.Set<Item>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException($"Item {id} not found.");

        return new ItemReadDto(
            item.Id, item.Sku, item.Name, item.Description, item.Category,
            item.Uom, item.ReorderPoint, item.ReorderQty, item.UnitCost,
            item.PreferredSupplier, item.LeadTimeDays, item.HazardousFlag,
            item.StorageRequirements, item.Manufacturer, item.ManufacturerPartNumber, item.CreatedAt);
    }

    public async Task<IReadOnlyList<ItemReadDto>> SearchItemsAsync(string? searchText, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryView))
            throw new UnauthorizedAccessException("Missing permission: inventory.view");

        var ctx = GetContext();
        var q = ctx.Set<Item>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var s = searchText.Trim();
            q = q.Where(i => i.Sku.Contains(s) || i.Name.Contains(s)
                || (i.ManufacturerPartNumber != null && i.ManufacturerPartNumber.Contains(s)));
        }
        var rows = await q.OrderByDescending(i => i.CreatedAt).Take(500)
            .Select(i => new ItemReadDto(
                i.Id, i.Sku, i.Name, i.Description, i.Category,
                i.Uom, i.ReorderPoint, i.ReorderQty, i.UnitCost,
                i.PreferredSupplier, i.LeadTimeDays, i.HazardousFlag,
                i.StorageRequirements, i.Manufacturer, i.ManufacturerPartNumber, i.CreatedAt))
            .ToListAsync(ct);
        return rows;
    }

    public async Task<StockBalanceReadDto> ReceiveAsync(StockReceiptDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryReceive))
            throw new UnauthorizedAccessException("Missing permission: inventory.receive");

        if (dto.Quantity <= 0) throw new ArgumentException("Quantity must be positive.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        var ctx = GetContext();

        var balance = await GetOrCreateBalanceAsync(ctx, dto.ItemId, dto.WarehouseId, dto.BinLocation, ct);
        balance.QtyOnHand += dto.Quantity;

        ctx.Set<StockTransaction>().Add(new StockTransaction
        {
            TransactionType = StockTransactionType.Receipt,
            ItemId = dto.ItemId,
            WarehouseId = dto.WarehouseId,
            Quantity = dto.Quantity,
            PerformedBy = _session.UserId,
            TransactionDate = DateTime.UtcNow,
            LotBatch = dto.LotBatch,
            ExpiryDate = dto.ExpiryDate,
            Supplier = dto.Supplier,
            ReferenceDocNo = dto.ReferenceDocNo,
            Notes = dto.Notes
        });

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ReadBalanceAsync(ctx, dto.ItemId, dto.WarehouseId, ct);
    }

    public async Task<StockBalanceReadDto> IssueAsync(StockIssueDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryIssue))
            throw new UnauthorizedAccessException("Missing permission: inventory.issue");

        if (dto.Quantity <= 0) throw new ArgumentException("Quantity must be positive.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        var ctx = GetContext();

        var balance = await GetOrCreateBalanceAsync(ctx, dto.ItemId, dto.WarehouseId, null, ct);
        if (balance.QtyOnHand < dto.Quantity)
            throw new InvalidOperationException($"Insufficient stock. On hand: {balance.QtyOnHand}, requested: {dto.Quantity}.");

        balance.QtyOnHand -= dto.Quantity;

        ctx.Set<StockTransaction>().Add(new StockTransaction
        {
            TransactionType = StockTransactionType.Issue,
            ItemId = dto.ItemId,
            WarehouseId = dto.WarehouseId,
            Quantity = -dto.Quantity,
            ToAssetId = dto.ToAssetId,
            RequesterUserId = dto.RequesterUserId,
            PerformedBy = _session.UserId,
            TransactionDate = DateTime.UtcNow,
            ReferenceDocNo = dto.ReferenceDocNo,
            Notes = dto.PurposeWorkOrder is null ? dto.Notes : $"WO: {dto.PurposeWorkOrder}. {dto.Notes ?? ""}"
        });

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ReadBalanceAsync(ctx, dto.ItemId, dto.WarehouseId, ct);
    }

    public async Task TransferAsync(StockTransferDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryIssue))
            throw new UnauthorizedAccessException("Missing permission: inventory.issue");

        if (dto.Quantity <= 0) throw new ArgumentException("Quantity must be positive.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        var ctx = GetContext();

        var fromBal = await GetOrCreateBalanceAsync(ctx, dto.ItemId, dto.FromWarehouseId, null, ct);
        if (fromBal.QtyOnHand < dto.Quantity)
            throw new InvalidOperationException($"Insufficient stock at source. On hand: {fromBal.QtyOnHand}.");

        var toBal = await GetOrCreateBalanceAsync(ctx, dto.ItemId, dto.ToWarehouseId, null, ct);
        fromBal.QtyOnHand -= dto.Quantity;
        toBal.QtyOnHand += dto.Quantity;

        ctx.Set<StockTransaction>().Add(new StockTransaction
        {
            TransactionType = StockTransactionType.Transfer,
            ItemId = dto.ItemId,
            WarehouseId = dto.FromWarehouseId,
            FromWarehouseId = dto.FromWarehouseId,
            ToWarehouseId = dto.ToWarehouseId,
            Quantity = dto.Quantity,
            PerformedBy = _session.UserId,
            TransactionDate = DateTime.UtcNow,
            ReferenceDocNo = dto.ReferenceDocNo,
            Notes = dto.Notes
        });

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<StockBalanceReadDto> AdjustAsync(StockAdjustmentDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryAdjust))
            throw new UnauthorizedAccessException("Missing permission: inventory.adjust");

        if (string.IsNullOrWhiteSpace(dto.ReasonCode))
            throw new ArgumentException("ReasonCode is mandatory for adjustments.");
        if (dto.NewQuantity < 0) throw new ArgumentException("NewQuantity cannot be negative.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        var ctx = GetContext();

        var balance = await GetOrCreateBalanceAsync(ctx, dto.ItemId, dto.WarehouseId, null, ct);
        var delta = dto.NewQuantity - balance.QtyOnHand;

        ctx.Set<StockTransaction>().Add(new StockTransaction
        {
            TransactionType = StockTransactionType.Adjustment,
            ItemId = dto.ItemId,
            WarehouseId = dto.WarehouseId,
            Quantity = delta,
            PerformedBy = _session.UserId,
            TransactionDate = DateTime.UtcNow,
            ReasonCode = dto.ReasonCode,
            ReferenceDocNo = dto.ReferenceDocNo,
            Notes = dto.Notes
        });

        balance.QtyOnHand = dto.NewQuantity;

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ReadBalanceAsync(ctx, dto.ItemId, dto.WarehouseId, ct);
    }

    public async Task WriteOffAsync(StockWriteOffDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryWriteOff))
            throw new UnauthorizedAccessException("Missing permission: inventory.writeoff");

        if (_session.UserId == dto.ApprovedByUserId)
            throw new InvalidOperationException("Write-off initiator cannot be the approver (separation of duties).");

        if (dto.Quantity <= 0) throw new ArgumentException("Quantity must be positive.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        var ctx = GetContext();

        var balance = await GetOrCreateBalanceAsync(ctx, dto.ItemId, dto.WarehouseId, null, ct);
        if (balance.QtyOnHand < dto.Quantity)
            throw new InvalidOperationException($"Cannot write off {dto.Quantity}; on hand: {balance.QtyOnHand}.");

        balance.QtyOnHand -= dto.Quantity;

        ctx.Set<StockTransaction>().Add(new StockTransaction
        {
            TransactionType = StockTransactionType.WriteOff,
            ItemId = dto.ItemId,
            WarehouseId = dto.WarehouseId,
            Quantity = -dto.Quantity,
            PerformedBy = _session.UserId,
            TransactionDate = DateTime.UtcNow,
            ReasonCode = dto.ReasonCode,
            ReferenceDocNo = dto.ReferenceDocNo,
            Notes = dto.Notes
        });

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<StockBalanceReadDto>> GetBalancesAsync(long warehouseId, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryView))
            throw new UnauthorizedAccessException("Missing permission: inventory.view");

        var ctx = GetContext();
        var q = from b in ctx.Set<StockBalance>().AsNoTracking()
                join i in ctx.Set<Item>().AsNoTracking() on b.ItemId equals i.Id
                join w in ctx.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.Id
                where b.WarehouseId == warehouseId
                select new StockBalanceReadDto(
                    b.ItemId, i.Sku, i.Name, b.WarehouseId, w.Name,
                    b.BinLocation, b.QtyOnHand, b.QtyReserved, b.QtyOnOrder,
                    i.ReorderPoint, b.QtyOnHand <= i.ReorderPoint);

        return await q.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockBalanceReadDto>> GetLowStockAsync(CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.InventoryView))
            throw new UnauthorizedAccessException("Missing permission: inventory.view");

        var ctx = GetContext();
        var q = from b in ctx.Set<StockBalance>().AsNoTracking()
                join i in ctx.Set<Item>().AsNoTracking() on b.ItemId equals i.Id
                join w in ctx.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.Id
                where b.QtyOnHand <= i.ReorderPoint
                select new StockBalanceReadDto(
                    b.ItemId, i.Sku, i.Name, b.WarehouseId, w.Name,
                    b.BinLocation, b.QtyOnHand, b.QtyReserved, b.QtyOnOrder,
                    i.ReorderPoint, true);

        return await q.ToListAsync(ct);
    }

    private static async Task<StockBalance> GetOrCreateBalanceAsync(DbContext ctx, long itemId, long warehouseId, string? bin, CancellationToken ct)
    {
        var balance = await ctx.Set<StockBalance>()
            .FirstOrDefaultAsync(b => b.ItemId == itemId && b.WarehouseId == warehouseId
                && (b.BinLocation ?? "") == (bin ?? ""), ct);

        if (balance is null)
        {
            balance = new StockBalance { ItemId = itemId, WarehouseId = warehouseId, BinLocation = bin };
            ctx.Set<StockBalance>().Add(balance);
            await ctx.SaveChangesAsync(ct);
        }
        return balance;
    }

    private static async Task<StockBalanceReadDto> ReadBalanceAsync(DbContext ctx, long itemId, long warehouseId, CancellationToken ct)
    {
        var q = from b in ctx.Set<StockBalance>().AsNoTracking()
                join i in ctx.Set<Item>().AsNoTracking() on b.ItemId equals i.Id
                join w in ctx.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.Id
                where b.ItemId == itemId && b.WarehouseId == warehouseId
                select new StockBalanceReadDto(
                    b.ItemId, i.Sku, i.Name, b.WarehouseId, w.Name,
                    b.BinLocation, b.QtyOnHand, b.QtyReserved, b.QtyOnOrder,
                    i.ReorderPoint, b.QtyOnHand <= i.ReorderPoint);
        return await q.FirstAsync(ct);
    }

    private DbContext GetContext() => (_uow as dynamic)?.Context as DbContext
        ?? throw new InvalidOperationException("UnitOfWork does not expose DbContext.");
}
