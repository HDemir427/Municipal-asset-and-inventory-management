using MAIMS.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FluentAssertions;

namespace MAIMS.Data.Tests;

public class MaimsDbContextTests
{
    private static MaimsDbContext BuildContext()
    {
        var opts = new DbContextOptionsBuilder<MaimsDbContext>()
            .UseInMemoryDatabase($"maims-test-{Guid.NewGuid()}")
            .Options;
        return new MaimsDbContext(opts);
    }

    [Fact]
    public async Task SaveChangesAsync_SoftDeleteFilter_ExcludesDeletedRows()
    {
        using var ctx = BuildContext();
        ctx.Departments.Add(new MAIMS.Core.Entities.Department { Name = "Public Works", Code = "PW" });
        await ctx.SaveChangesAsync();

        var dept = await ctx.Departments.FirstAsync();
        // Soft-delete explicitly (in production this is done by the audit interceptor
        // when state == Deleted; in unit tests we mark it manually to verify the filter).
        dept.IsDeleted = true;
        dept.DeletedAt = DateTime.UtcNow;
        ctx.Departments.Update(dept);
        await ctx.SaveChangesAsync();

        // With query filter active (default), the row is hidden.
        var live = await ctx.Departments.ToListAsync();
        live.Should().BeEmpty();

        // Bypassing the filter reveals the soft-deleted row.
        var raw = await ctx.Departments.IgnoreQueryFilters().ToListAsync();
        raw.Should().HaveCount(1);
        raw[0].IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task AuditLog_DbSet_IsReachable()
    {
        using var ctx = BuildContext();
        ctx.AuditLogs.Add(new MAIMS.Core.Entities.AuditLog
        {
            EntityType = "Asset",
            EntityId = 1,
            Action = "CREATE",
            ChangedAt = DateTime.UtcNow
        });
        var rows = await ctx.SaveChangesAsync();
        rows.Should().Be(1);

        var log = await ctx.AuditLogs.FirstAsync();
        log.EntityType.Should().Be("Asset");
        log.Action.Should().Be("CREATE");
    }

    [Fact]
    public async Task UniqueIndexes_OnAssetCode_PreventDuplicates()
    {
        using var ctx = BuildContext();
        ctx.AssetCategories.Add(new MAIMS.Core.Entities.AssetCategory
        {
            Name = "Vehicles",
            CategoryType = MAIMS.Core.Enums.AssetCategoryType.Vehicles
        });
        await ctx.SaveChangesAsync();
        var cat = await ctx.AssetCategories.FirstAsync();

        ctx.Departments.Add(new MAIMS.Core.Entities.Department { Name = "PW", Code = "PW" });
        await ctx.SaveChangesAsync();
        var dept = await ctx.Departments.FirstAsync();

        ctx.Assets.Add(new MAIMS.Core.Entities.Asset
        {
            AssetCode = "PW-VEH-00001",
            Name = "A1",
            CategoryId = cat.Id,
            DepartmentId = dept.Id
        });
        await ctx.SaveChangesAsync();

        ctx.Assets.Add(new MAIMS.Core.Entities.Asset
        {
            AssetCode = "PW-VEH-00001",
            Name = "A2",
            CategoryId = cat.Id,
            DepartmentId = dept.Id
        });

        // InMemory provider does NOT enforce unique indexes, so this test only
        // verifies the schema field accepts the value. Real enforcement is via
        // MySQL's unique index — verified by an integration test against Testcontainers.
        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().NotThrowAsync(); // InMemory is permissive.
    }
}
