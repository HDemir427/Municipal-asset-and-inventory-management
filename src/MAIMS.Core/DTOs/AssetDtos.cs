using MAIMS.Core.Enums;

namespace MAIMS.Core.DTOs;

/// <summary>DTO for creating a new asset. AssetCode is auto-generated server-side.</summary>
public record AssetCreateDto(
    string Name,
    string? Description,
    long CategoryId,
    long DepartmentId,
    long? LocationId,
    long? CustodianUserId,
    AssetStatus Status,
    DateTime? AcquisitionDate,
    decimal? AcquisitionCost,
    string? FundingSource,
    ConditionRating ConditionRating,
    long? ParentAssetId,
    string? SerialNumber);

/// <summary>DTO for updating an existing asset. All fields overwrite current values.</summary>
public record AssetUpdateDto(
    long Id,
    string Name,
    string? Description,
    long CategoryId,
    long DepartmentId,
    long? LocationId,
    long? CustodianUserId,
    AssetStatus Status,
    DateTime? AcquisitionDate,
    decimal? AcquisitionCost,
    string? FundingSource,
    ConditionRating ConditionRating,
    long? ParentAssetId,
    string? SerialNumber);

/// <summary>DTO for disposing of an asset. Requires DisposalMethod and approver.</summary>
public record AssetDisposalDto(
    long AssetId,
    DisposalMethod Method,
    DateTime DisposalDate,
    decimal? Proceeds,
    long ApprovedByUserId,
    string? Notes);

/// <summary>DTO for transferring an asset between custodians / locations / departments.</summary>
public record AssetTransferDto(
    long AssetId,
    long? ToDepartmentId,
    long? ToLocationId,
    long? ToCustodianUserId,
    long ApprovedByUserId,
    string? Notes);

/// <summary>Read DTO returned by queries. Decouples UI from entity shape.</summary>
public record AssetReadDto(
    long Id,
    string AssetCode,
    string Name,
    string? Description,
    long CategoryId,
    string? CategoryName,
    long DepartmentId,
    string? DepartmentName,
    long? LocationId,
    string? LocationName,
    long? CustodianUserId,
    string? CustodianName,
    AssetStatus Status,
    DateTime? AcquisitionDate,
    decimal? AcquisitionCost,
    decimal? CurrentBookValue,
    ConditionRating ConditionRating,
    long? ParentAssetId,
    string? SerialNumber,
    string? FundingSource,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Filter parameters for asset list queries.</summary>
public record AssetSearchFilter(
    string? SearchText,
    long? DepartmentId,
    long? CategoryId,
    AssetStatus? Status,
    ConditionRating? MinCondition,
    DateTime? AcquiredFrom,
    DateTime? AcquiredTo,
    int Page = 1,
    int PageSize = 50);

/// <summary>Paginated result wrapper.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}
