using FluentAssertions;
using MAIMS.Core.Abstractions;
using MAIMS.Core.DTOs;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MAIMS.Services.Tests;

public class AssetServiceTests
{
    [Fact]
    public async Task CreateAsync_AsAdmin_GeneratesAssetCode_AndLifecycleEvent()
    {
        var (sp, ctx, catId, deptId) = await TestHost.BuildAsync();
        var svc = sp.GetRequiredService<IAssetService>();

        var dto = new AssetCreateDto(
            Name: "Ford Transit #42",
            Description: "Cargo van for Parks dept",
            CategoryId: catId,
            DepartmentId: deptId,
            LocationId: null,
            CustodianUserId: null,
            Status: AssetStatus.InService,
            AcquisitionDate: new DateTime(2024, 1, 15),
            AcquisitionCost: 42000m,
            FundingSource: "Capital Budget 2024",
            ConditionRating: ConditionRating.Good,
            ParentAssetId: null,
            SerialNumber: "FT-2024-0042");

        var result = await svc.CreateAsync(dto);

        result.Id.Should().BeGreaterThan(0);
        result.AssetCode.Should().StartWith("PW-VEHICLES-");
        result.Name.Should().Be("Ford Transit #42");
        result.Status.Should().Be(AssetStatus.InService);

        var ev = await ctx.AssetLifecycleEvents.FirstOrDefaultAsync(e => e.AssetId == result.Id);
        ev.Should().NotBeNull();
        ev!.EventType.Should().Be(AssetEventType.Acquisition);
    }

    [Fact]
    public async Task CreateAsync_AsFieldWorker_Denied()
    {
        var (sp, _, catId, deptId) = await TestHost.BuildAsync(session: FakeSession.FieldWorkerNoPerms);
        var svc = sp.GetRequiredService<IAssetService>();

        var dto = new AssetCreateDto(
            "Test", null, catId, deptId, null, null,
            AssetStatus.Planned, null, null, null,
            ConditionRating.Fair, null, null);

        var act = () => svc.CreateAsync(dto);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*asset.create*");
    }

    [Fact]
    public async Task SearchAsync_AsAdmin_ReturnsAllAssetsInDept()
    {
        var (sp, _, catId, deptId) = await TestHost.BuildAsync();
        var svc = sp.GetRequiredService<IAssetService>();

        for (int i = 0; i < 3; i++)
        {
            await svc.CreateAsync(new AssetCreateDto(
                $"Asset {i}", null, catId, deptId, null, null,
                AssetStatus.InService, DateTime.Today, 1000m * (i + 1), null,
                ConditionRating.Good, null, $"S{i}"));
        }

        var result = await svc.SearchAsync(new AssetSearchFilter(null, null, null, null, null, null, null, 1, 50));

        result.TotalCount.Should().Be(3);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task TransferAsync_MovesAssetBetweenDepartments_WhenCrossDeptAllowed()
    {
        var (sp, ctx, catId, deptId) = await TestHost.BuildAsync();
        var svc = sp.GetRequiredService<IAssetService>();

        var created = await svc.CreateAsync(new AssetCreateDto(
            "Asset X", null, catId, deptId, null, null,
            AssetStatus.InService, null, null, null,
            ConditionRating.Good, null, null));

        // Add a second department to transfer to.
        ctx.Departments.Add(new MAIMS.Core.Entities.Department { Name = "Parks", Code = "PRK" });
        await ctx.SaveChangesAsync();
        var newDeptId = await ctx.Departments.Where(d => d.Code == "PRK").Select(d => d.Id).FirstAsync();

        var updated = await svc.TransferAsync(new AssetTransferDto(
            created.Id, ToDepartmentId: newDeptId, ToLocationId: null, ToCustodianUserId: null,
            ApprovedByUserId: 1, Notes: "Move to Parks"));

        updated.DepartmentId.Should().Be(newDeptId);

        var ev = await ctx.AssetLifecycleEvents
            .Where(e => e.AssetId == created.Id && e.EventType == AssetEventType.Transfer)
            .FirstOrDefaultAsync();
        ev.Should().NotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_RejectsWhenApproverIsInitiator()
    {
        var (sp, _, catId, deptId) = await TestHost.BuildAsync();
        var svc = sp.GetRequiredService<IAssetService>();

        // Admin's user ID is 1 in FakeSession.Admin.
        var asset = await svc.CreateAsync(new AssetCreateDto(
            "Asset Z", null, catId, deptId, null, null,
            AssetStatus.InService, null, 1000m, null,
            ConditionRating.Fair, null, null));

        var act = () => svc.DisposeAsync(new AssetDisposalDto(
            asset.Id, DisposalMethod.Sale, DateTime.Today, 500m,
            ApprovedByUserId: 1, Notes: "Self-approved — should fail"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*separation of duties*");
    }

    [Fact]
    public async Task DeleteAsync_PerformsSoftDelete()
    {
        var (sp, ctx, catId, deptId) = await TestHost.BuildAsync();
        var svc = sp.GetRequiredService<IAssetService>();

        var asset = await svc.CreateAsync(new AssetCreateDto(
            "Asset D", null, catId, deptId, null, null,
            AssetStatus.InService, null, null, null,
            ConditionRating.Fair, null, null));

        await svc.DeleteAsync(asset.Id);

        // Hard lookup bypasses the query filter (which excludes IsDeleted=true).
        // Use AsNoTracking + a fresh DbContext to avoid stale change tracker entries.
        var raw = await ctx.Assets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == asset.Id);
        raw.Should().NotBeNull();
        raw!.IsDeleted.Should().BeTrue();
        raw.DeletedAt.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that deleting a non-existent asset throws KeyNotFoundException
    /// rather than silently succeeding. This is the safety net requested by the
    /// user: "örnek olarak olmayan kullanıcı kayıdı silinmesin."
    /// </summary>
    [Fact]
    public async Task DeleteAsync_NonExistentAsset_ThrowsKeyNotFound()
    {
        var (sp, _, _, _) = await TestHost.BuildAsync();
        var svc = sp.GetRequiredService<IAssetService>();

        // Attempt to delete an asset ID that was never created.
        var act = () => svc.DeleteAsync(99999);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*99999*");
    }

    /// <summary>
    /// Verifies that deleting the same asset twice (second call after the first
    /// soft-deleted it) throws KeyNotFoundException because the query filter
    /// hides soft-deleted rows from subsequent lookups.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_AlreadyDeleted_ThrowsKeyNotFound()
    {
        var (sp, _, catId, deptId) = await TestHost.BuildAsync();
        var svc = sp.GetRequiredService<IAssetService>();

        var asset = await svc.CreateAsync(new AssetCreateDto(
            "Double-Delete Test", null, catId, deptId, null, null,
            AssetStatus.InService, null, null, null,
            ConditionRating.Fair, null, null));

        // First delete succeeds.
        await svc.DeleteAsync(asset.Id);

        // Second delete should fail because the asset is now soft-deleted and
        // hidden by the IsDeleted query filter.
        var act = () => svc.DeleteAsync(asset.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{asset.Id}*");
    }
}
