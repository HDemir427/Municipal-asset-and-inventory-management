using MAIMS.Core.Abstractions;
using MAIMS.Core.DTOs;
using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.Services.Validation;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace MAIMS.Services.Asset;

/// <summary>
/// Business logic for fixed-asset management. Enforces RBAC at the service layer
/// (UI also disables buttons but UI-only enforcement is forbidden by spec §9.1).
/// Every CUD operation goes through IUnitOfWork so the audit interceptor fires.
/// </summary>
public class AssetService : IAssetService
{
    private readonly IUnitOfWork _uow;
    private readonly IAssetCodeGenerator _codeGen;
    private readonly ICurrentSession _session;
    private readonly AssetCreateValidator _createValidator;
    private readonly AssetUpdateValidator _updateValidator;
    private readonly AssetDisposalValidator _disposalValidator;
    private readonly AssetTransferValidator _transferValidator;

    public AssetService(
        IUnitOfWork uow,
        IAssetCodeGenerator codeGen,
        ICurrentSession session)
    {
        _uow = uow;
        _codeGen = codeGen;
        _session = session;
        _createValidator = new AssetCreateValidator();
        _updateValidator = new AssetUpdateValidator();
        _disposalValidator = new AssetDisposalValidator();
        _transferValidator = new AssetTransferValidator();
    }

    public async Task<AssetReadDto> CreateAsync(AssetCreateDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AssetCreate))
            throw new UnauthorizedAccessException("Missing permission: asset.create");

        await _createValidator.ValidateAndThrowAsync(dto, ct);

        var assetCode = await _codeGen.GenerateAsync(dto.DepartmentId, dto.CategoryId, ct);

        var asset = new MAIMS.Core.Entities.Asset
        {
            AssetCode = assetCode,
            Name = dto.Name,
            Description = dto.Description,
            CategoryId = dto.CategoryId,
            DepartmentId = dto.DepartmentId,
            LocationId = dto.LocationId,
            CustodianUserId = dto.CustodianUserId,
            Status = dto.Status,
            AcquisitionDate = dto.AcquisitionDate,
            AcquisitionCost = dto.AcquisitionCost,
            CurrentBookValue = dto.AcquisitionCost,
            ConditionRating = dto.ConditionRating,
            ParentAssetId = dto.ParentAssetId,
            SerialNumber = dto.SerialNumber,
            FundingSource = dto.FundingSource,
            BarcodePayload = assetCode
        };

        await using var tx = await _uow.BeginTransactionAsync(ct);
        var ctx = (_uow as dynamic)?.Context as DbContext
            ?? throw new InvalidOperationException("UnitOfWork does not expose DbContext.");
        ctx.Set<MAIMS.Core.Entities.Asset>().Add(asset);
        await _uow.SaveChangesAsync(ct);

        // Lifecycle event: Acquisition (or Planned if status is Planned)
        ctx.Set<AssetLifecycleEvent>().Add(new AssetLifecycleEvent
        {
            AssetId = asset.Id,
            EventType = dto.Status == AssetStatus.Planned ? AssetEventType.StatusChange : AssetEventType.Acquisition,
            EventDate = DateTime.UtcNow,
            PerformedBy = _session.UserId,
            ToStatus = dto.Status,
            Notes = "Initial registration"
        });
        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetByIdAsync(asset.Id, ct);
    }

    public async Task<AssetReadDto> UpdateAsync(AssetUpdateDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AssetEdit))
            throw new UnauthorizedAccessException("Missing permission: asset.edit");

        await _updateValidator.ValidateAndThrowAsync(dto, ct);

        var ctx = GetContext();
        var asset = await ctx.Set<MAIMS.Core.Entities.Asset>().FirstOrDefaultAsync(a => a.Id == dto.Id, ct)
            ?? throw new KeyNotFoundException($"MAIMS.Core.Entities.Asset {dto.Id} not found.");

        // Enforce department isolation: a user without cross-dept access cannot edit another dept's asset.
        if (asset.DepartmentId != _session.DepartmentId && !_session.HasCrossDepartmentAccess())
            throw new UnauthorizedAccessException("You do not have access to assets outside your department.");

        asset.Name = dto.Name;
        asset.Description = dto.Description;
        asset.CategoryId = dto.CategoryId;
        asset.DepartmentId = dto.DepartmentId;
        asset.LocationId = dto.LocationId;
        asset.CustodianUserId = dto.CustodianUserId;
        asset.Status = dto.Status;
        asset.AcquisitionDate = dto.AcquisitionDate;
        asset.AcquisitionCost = dto.AcquisitionCost;
        asset.ConditionRating = dto.ConditionRating;
        asset.ParentAssetId = dto.ParentAssetId;
        asset.SerialNumber = dto.SerialNumber;
        asset.FundingSource = dto.FundingSource;

        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(asset.Id, ct);
    }

    public async Task<AssetReadDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AssetView))
            throw new UnauthorizedAccessException("Missing permission: asset.view");

        var ctx = GetContext();
        var q = ctx.Set<MAIMS.Core.Entities.Asset>().AsNoTracking().Where(a => a.Id == id);
        q = ApplyDepartmentScope(q);

        var asset = await q
            .Select(a => new AssetReadDto(
                a.Id, a.AssetCode, a.Name, a.Description,
                a.CategoryId, a.Category != null ? a.Category.Name : null,
                a.DepartmentId, a.Department != null ? a.Department.Name : null,
                a.LocationId, a.Location != null ? a.Location.Name : null,
                a.CustodianUserId, a.Custodian != null ? a.Custodian.Name : null,
                a.Status, a.AcquisitionDate, a.AcquisitionCost, a.CurrentBookValue,
                a.ConditionRating, a.ParentAssetId, a.SerialNumber, a.FundingSource,
                a.CreatedAt, a.UpdatedAt))
            .FirstOrDefaultAsync(ct);

        return asset ?? throw new KeyNotFoundException($"MAIMS.Core.Entities.Asset {id} not found.");
    }

    public async Task<PagedResult<AssetReadDto>> SearchAsync(AssetSearchFilter filter, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AssetView))
            throw new UnauthorizedAccessException("Missing permission: asset.view");

        var ctx = GetContext();
        var q = ctx.Set<MAIMS.Core.Entities.Asset>().AsNoTracking().AsQueryable();
        q = ApplyDepartmentScope(q);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            q = q.Where(a => a.AssetCode.Contains(s) || a.Name.Contains(s) || (a.SerialNumber != null && a.SerialNumber.Contains(s)));
        }
        if (filter.DepartmentId is long d) q = q.Where(a => a.DepartmentId == d);
        if (filter.CategoryId is long c) q = q.Where(a => a.CategoryId == c);
        if (filter.Status is AssetStatus s2) q = q.Where(a => a.Status == s2);
        if (filter.MinCondition is ConditionRating r) q = q.Where(a => a.ConditionRating >= r);
        if (filter.AcquiredFrom is DateTime af) q = q.Where(a => a.AcquisitionDate >= af);
        if (filter.AcquiredTo is DateTime at) q = q.Where(a => a.AcquisitionDate <= at);

        var totalCount = await q.CountAsync(ct);
        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 1, 500);

        var items = await q
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(a => new AssetReadDto(
                a.Id, a.AssetCode, a.Name, a.Description,
                a.CategoryId, a.Category != null ? a.Category.Name : null,
                a.DepartmentId, a.Department != null ? a.Department.Name : null,
                a.LocationId, a.Location != null ? a.Location.Name : null,
                a.CustodianUserId, a.Custodian != null ? a.Custodian.Name : null,
                a.Status, a.AcquisitionDate, a.AcquisitionCost, a.CurrentBookValue,
                a.ConditionRating, a.ParentAssetId, a.SerialNumber, a.FundingSource,
                a.CreatedAt, a.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<AssetReadDto>(items, totalCount, page, size);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AssetDelete))
            throw new UnauthorizedAccessException("Missing permission: asset.delete");

        var ctx = GetContext();
        var asset = await ctx.Set<MAIMS.Core.Entities.Asset>().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new KeyNotFoundException($"MAIMS.Core.Entities.Asset {id} not found.");

        if (asset.DepartmentId != _session.DepartmentId && !_session.HasCrossDepartmentAccess())
            throw new UnauthorizedAccessException("Cannot delete assets outside your department.");

        // Explicit soft-delete: mark IsDeleted/DeletedAt. The audit interceptor
        // (in production) ALSO converts hard Delete -> Modified+IsDeleted as defence
        // in depth, but we don't rely on it here so tests without the interceptor pass.
        asset.IsDeleted = true;
        asset.DeletedAt = DateTime.UtcNow;
        ctx.Set<MAIMS.Core.Entities.Asset>().Update(asset);

        await _uow.SaveChangesAsync(ct);
    }

    public async Task<AssetReadDto> TransferAsync(AssetTransferDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AssetTransfer))
            throw new UnauthorizedAccessException("Missing permission: asset.transfer");

        await _transferValidator.ValidateAndThrowAsync(dto, ct);

        var ctx = GetContext();
        var asset = await ctx.Set<MAIMS.Core.Entities.Asset>().FirstOrDefaultAsync(a => a.Id == dto.AssetId, ct)
            ?? throw new KeyNotFoundException($"MAIMS.Core.Entities.Asset {dto.AssetId} not found.");

        var fromDeptId = asset.DepartmentId;
        var fromLocId = asset.LocationId;
        var fromCustId = asset.CustodianUserId;

        if (dto.ToDepartmentId is long d && d != fromDeptId && !_session.HasCrossDepartmentAccess())
            throw new UnauthorizedAccessException("Cross-department transfers require xdept.view permission.");

        if (dto.ToDepartmentId is long nd) asset.DepartmentId = nd;
        if (dto.ToLocationId is long nl) asset.LocationId = nl;
        if (dto.ToCustodianUserId is long nc) asset.CustodianUserId = nc;

        ctx.Set<AssetLifecycleEvent>().Add(new AssetLifecycleEvent
        {
            AssetId = asset.Id,
            EventType = AssetEventType.Transfer,
            EventDate = DateTime.UtcNow,
            PerformedBy = _session.UserId,
            FromStatus = asset.Status,
            ToStatus = asset.Status,
            Notes = $"Transfer: dept {fromDeptId}->{asset.DepartmentId}, loc {fromLocId}->{asset.LocationId}, cust {fromCustId}->{asset.CustodianUserId}. {dto.Notes ?? ""}"
        });

        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(asset.Id, ct);
    }

    public async Task<AssetReadDto> DisposeAsync(AssetDisposalDto dto, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AssetDispose))
            throw new UnauthorizedAccessException("Missing permission: asset.dispose");

        // Separation of duties: the disposer cannot be the approver.
        if (_session.UserId == dto.ApprovedByUserId)
            throw new InvalidOperationException("Disposal initiator cannot be the approver (separation of duties).");

        await _disposalValidator.ValidateAndThrowAsync(dto, ct);

        var ctx = GetContext();
        var asset = await ctx.Set<MAIMS.Core.Entities.Asset>().FirstOrDefaultAsync(a => a.Id == dto.AssetId, ct)
            ?? throw new KeyNotFoundException($"MAIMS.Core.Entities.Asset {dto.AssetId} not found.");

        var prevStatus = asset.Status;
        asset.Status = AssetStatus.Disposed;
        asset.DisposalMethod = dto.Method;
        asset.DisposalDate = dto.DisposalDate;
        asset.DisposalProceeds = dto.Proceeds;
        asset.DisposalApprovedBy = dto.ApprovedByUserId;

        ctx.Set<AssetLifecycleEvent>().Add(new AssetLifecycleEvent
        {
            AssetId = asset.Id,
            EventType = AssetEventType.Disposal,
            EventDate = dto.DisposalDate,
            PerformedBy = _session.UserId,
            FromStatus = prevStatus,
            ToStatus = AssetStatus.Disposed,
            Cost = dto.Proceeds,
            Notes = $"Disposal via {dto.Method}. {dto.Notes ?? ""}"
        });

        await _uow.SaveChangesAsync(ct);
        return await GetByIdAsync(asset.Id, ct);
    }

    public async Task<IReadOnlyList<AssetReadDto>> GetChildrenAsync(long parentAssetId, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AssetView))
            throw new UnauthorizedAccessException("Missing permission: asset.view");

        var ctx = GetContext();
        var q = ctx.Set<MAIMS.Core.Entities.Asset>().AsNoTracking().Where(a => a.ParentAssetId == parentAssetId);
        q = ApplyDepartmentScope(q);
        var rows = await q
            .Select(a => new AssetReadDto(
                a.Id, a.AssetCode, a.Name, a.Description,
                a.CategoryId, a.Category != null ? a.Category.Name : null,
                a.DepartmentId, a.Department != null ? a.Department.Name : null,
                a.LocationId, a.Location != null ? a.Location.Name : null,
                a.CustodianUserId, a.Custodian != null ? a.Custodian.Name : null,
                a.Status, a.AcquisitionDate, a.AcquisitionCost, a.CurrentBookValue,
                a.ConditionRating, a.ParentAssetId, a.SerialNumber, a.FundingSource,
                a.CreatedAt, a.UpdatedAt))
            .ToListAsync(ct);
        return rows;
    }

    public async Task<byte[]> GenerateBarcodeAsync(long assetId, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AssetView))
            throw new UnauthorizedAccessException("Missing permission: asset.view");

        var ctx = GetContext();
        var asset = await ctx.Set<MAIMS.Core.Entities.Asset>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, ct)
            ?? throw new KeyNotFoundException($"MAIMS.Core.Entities.Asset {assetId} not found.");

        var payload = string.IsNullOrWhiteSpace(asset.BarcodePayload) ? asset.AssetCode : asset.BarcodePayload;

        // Barcode rendering is handled in MAIMS.WinUI via QRCoder (the QRCoder
        // package is referenced by MAIMS.WinUI, not by MAIMS.Services, to keep
        // the service layer free of UI rendering dependencies). This method
        // resolves the barcode payload and throws NotImplementedException so
        // the WinForms caller can catch it and render the QR code locally.
        // The payload (asset code or custom BarcodePayload) is included in the
        // exception message for the caller to use.
        throw new NotImplementedException(
            "Barcode rendering is performed in MAIMS.WinUI via QRCoder. " +
            "Call the WinForms barcode helper instead. Payload=" + payload);
    }

    /// <summary>
    /// Returns the inner DbContext from the UnitOfWork. Done via dynamic
    /// dispatch to avoid leaking the DbContext type through the IUnitOfWork
    /// interface (the interface exposes only SaveChangesAsync/BeginTransactionAsync).
    /// </summary>
    private DbContext GetContext() => (_uow as dynamic)?.Context as DbContext
        ?? throw new InvalidOperationException("UnitOfWork does not expose DbContext.");

    private IQueryable<MAIMS.Core.Entities.Asset> ApplyDepartmentScope(IQueryable<MAIMS.Core.Entities.Asset> q)
    {
        if (_session.HasCrossDepartmentAccess()) return q;
        if (_session.DepartmentId is long dept) return q.Where(a => a.DepartmentId == dept);
        return q.Where(a => false); // no department = no rows
    }
}
