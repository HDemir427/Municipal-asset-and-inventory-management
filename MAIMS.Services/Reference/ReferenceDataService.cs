using MAIMS.Core.Entities;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.Services.Reference;

/// <summary>
/// Read-only lookup service for reference data. Uses IServiceScopeFactory
/// because the service is registered as Scoped and the DbContext is also Scoped.
/// </summary>
public class ReferenceDataService : IReferenceDataService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ReferenceDataService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<IReadOnlyList<AssetCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
        return await ctx.AssetCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
        return await ctx.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Location>> GetLocationsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
        return await ctx.Locations
            .AsNoTracking()
            .OrderBy(l => l.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<User>> GetCustodiansAsync(long? departmentId = null, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
        var q = ctx.Users.AsNoTracking().AsQueryable();
        if (departmentId is long d) q = q.Where(u => u.DepartmentId == d);
        return await q.OrderBy(u => u.Name).ToListAsync(ct);
    }
}
