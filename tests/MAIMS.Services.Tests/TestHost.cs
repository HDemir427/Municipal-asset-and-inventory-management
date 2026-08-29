using MAIMS.Core.Abstractions;
using MAIMS.Core.DTOs;
using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.Data.AssetCodeGeneration;
using MAIMS.Services.Asset;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.Services.Tests;

/// <summary>
/// Shared test helpers. Builds an in-memory DbContext + UnitOfWork + AssetService
/// with a fake current session (full permissions) so tests can focus on business logic.
/// </summary>
internal static class TestHost
{
    public static async Task<(IServiceProvider sp, MaimsDbContext ctx, long categoryId, long departmentId)> BuildAsync(
        ICurrentSession? session = null)
    {
        var sessionInstance = session ?? FakeSession.Admin;

        var services = new ServiceCollection();
        services.AddDbContext<MaimsDbContext>(opts =>
            opts.UseInMemoryDatabase($"maims-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MaimsDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAssetCodeGenerator, SequentialAssetCodeGenerator>();
        // Register the FakeSession as both itself AND as ICurrentSession (singleton, same instance).
        services.AddSingleton(sessionInstance);
        services.AddSingleton<ICurrentSession>(sessionInstance);
        services.AddScoped<IAssetService, AssetService>();

        // IAuditWriter needs a no-op queue (in-memory tests don't need audit flushing).
        services.AddSingleton<MAIMS.Data.Interceptors.IAuditWriter, NullAuditWriter>();

        var sp = services.BuildServiceProvider();

        var ctx = sp.GetRequiredService<MaimsDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        // Seed minimum references.
        var dept = new Department { Name = "Public Works", Code = "PW" };
        ctx.Departments.Add(dept);
        var cat = new AssetCategory { Name = "Vehicles", CategoryType = AssetCategoryType.Vehicles, DepreciationMethod = "STRAIGHT_LINE", UsefulLifeYears = 8 };
        ctx.AssetCategories.Add(cat);
        await ctx.SaveChangesAsync();

        return (sp, ctx, cat.Id, dept.Id);
    }
}

internal sealed class FakeSession : ICurrentSession
{
    public static readonly FakeSession Admin = new(
        userId: 1, departmentId: 1, roleName: "SystemAdministrator",
        permissions: MAIMS.Core.Enums.Permissions.DefaultRolePermissions["SystemAdministrator"]);

    public static FakeSession FieldWorkerNoPerms = new(
        userId: 2, departmentId: 1, roleName: "FieldWorker",
        permissions: MAIMS.Core.Enums.Permissions.DefaultRolePermissions["FieldWorker"]);

    private readonly HashSet<string> _perms;
    public FakeSession(long userId, long departmentId, string roleName, IEnumerable<string> permissions)
    {
        UserId = userId;
        DepartmentId = departmentId;
        RoleName = roleName;
        UserName = $"user{userId}";
        Email = $"user{userId}@maims.local";
        DepartmentName = $"Department {departmentId}";
        LastLoginAt = null;
        _perms = permissions.ToHashSet(StringComparer.Ordinal);
    }

    public long? UserId { get; }
    public long? DepartmentId { get; }
    public string? DepartmentName { get; }
    public string? RoleName { get; }
    public IReadOnlyCollection<string> Permissions => _perms;
    public string? UserName { get; }
    public string? Email { get; }
    public DateTime? LastLoginAt { get; }
    public string? IpAddress => "127.0.0.1";
    public string? MachineName => "TEST";
    public bool HasPermission(string permission) => _perms.Contains(permission);
    public bool HasCrossDepartmentAccess() => _perms.Contains(MAIMS.Core.Enums.Permissions.CrossDepartmentView);
}

internal sealed class NullAuditWriter : MAIMS.Data.Interceptors.IAuditWriter
{
    public void Enqueue(MAIMS.Data.Interceptors.AuditLogEntryPending entry) { /* no-op */ }
}
