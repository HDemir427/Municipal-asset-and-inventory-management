using MAIMS.Core.DTOs;

namespace MAIMS.Core.Interfaces;

/// <summary>
/// Asset management operations. All methods are async, permission-checked, and audit-logged.
/// </summary>
public interface IAssetService
{
    Task<AssetReadDto> CreateAsync(AssetCreateDto dto, CancellationToken ct = default);
    Task<AssetReadDto> UpdateAsync(AssetUpdateDto dto, CancellationToken ct = default);
    Task<AssetReadDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<PagedResult<AssetReadDto>> SearchAsync(AssetSearchFilter filter, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<AssetReadDto> TransferAsync(AssetTransferDto dto, CancellationToken ct = default);
    Task<AssetReadDto> DisposeAsync(AssetDisposalDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<AssetReadDto>> GetChildrenAsync(long parentAssetId, CancellationToken ct = default);
    Task<byte[]> GenerateBarcodeAsync(long assetId, CancellationToken ct = default);
}

/// <summary>
/// Inventory management operations. All mutations are atomic: stock balance update + audit log
/// are committed in the same transaction (IDbContextTransaction).
/// </summary>
public interface IInventoryService
{
    // Item catalog
    Task<ItemReadDto> CreateItemAsync(ItemCreateDto dto, CancellationToken ct = default);
    Task<ItemReadDto> UpdateItemAsync(ItemUpdateDto dto, CancellationToken ct = default);
    Task<ItemReadDto> GetItemAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<ItemReadDto>> SearchItemsAsync(string? searchText, CancellationToken ct = default);
    Task DeleteItemAsync(long id, CancellationToken ct = default);

    // Stock transactions
    Task<StockBalanceReadDto> ReceiveAsync(StockReceiptDto dto, CancellationToken ct = default);
    Task<StockBalanceReadDto> IssueAsync(StockIssueDto dto, CancellationToken ct = default);
    Task TransferAsync(StockTransferDto dto, CancellationToken ct = default);
    Task<StockBalanceReadDto> AdjustAsync(StockAdjustmentDto dto, CancellationToken ct = default);
    Task WriteOffAsync(StockWriteOffDto dto, CancellationToken ct = default);

    // Stock queries
    Task<IReadOnlyList<StockBalanceReadDto>> GetBalancesAsync(long warehouseId, CancellationToken ct = default);
    Task<IReadOnlyList<StockBalanceReadDto>> GetLowStockAsync(CancellationToken ct = default);
}

/// <summary>
/// Append-only audit log writer. The standard CRUD operations NEVER expose
/// update/delete on audit_log — the table is immutable by design (spec §9.2).
///
/// The PurgeInvalidEntriesAsync method is the ONLY exception: it allows an
/// administrator to clean up entries with invalid entity_id (≤ 0) that were
/// written by a previous bug. It requires ROOT credentials because it must
/// temporarily drop the BEFORE DELETE trigger, perform the delete, then
/// recreate the trigger.
/// </summary>
public interface IAuditService
{
    Task LogAsync(string entityType, long entityId, string action,
        string? beforeJson, string? afterJson, CancellationToken ct = default);

    Task<IReadOnlyList<AuditLogEntry>> SearchAsync(AuditSearchFilter filter, CancellationToken ct = default);

    Task<byte[]> ExportAsync(AuditSearchFilter filter, string format, CancellationToken ct = default);

    /// <summary>
    /// Purges audit_log entries with invalid entity_id (≤ 0) or all entries
    /// if purgeAll=true. Requires a ROOT connection string because the
    /// BEFORE DELETE trigger must be temporarily dropped.
    ///
    /// This method:
    ///   1. Connects as root
    ///   2. DROP TRIGGER trg_audit_log_block_delete
    ///   3. DELETE FROM audit_log WHERE entity_id &lt;= 0 (or all)
    ///   4. CREATE TRIGGER trg_audit_log_block_delete (recreates immutability)
    ///   5. Returns the number of rows deleted
    /// </summary>
    Task<int> PurgeInvalidEntriesAsync(string rootConnectionString, bool purgeAll = false, CancellationToken ct = default);
}

public record AuditLogEntry(
    long Id,
    string EntityType,
    long EntityId,
    string Action,
    long? ChangedBy,
    string? ChangedByName,
    DateTime ChangedAt,
    string? BeforeJson,
    string? AfterJson,
    string? MachineName);

public record AuditSearchFilter(
    string? EntityType,
    long? EntityId,
    long? ChangedByUserId,
    string? Action,
    DateTime? From,
    DateTime? To,
    int Page = 1,
    int PageSize = 100);

/// <summary>
/// Authentication and RBAC. Wraps BCrypt password verification and permission checks.
/// </summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default);
    Task LogoutAsync();
    bool HasPermission(string permission);
    bool HasCrossDepartmentAccess();
    long? CurrentUserId { get; }
    long? CurrentDepartmentId { get; }
    string? CurrentRoleName { get; }
    string? CurrentUserName { get; }
}

public record AuthResult(bool Success, string? ErrorMessage, string? UserName, string? RoleName);
