using MAIMS.Core.Abstractions;
using MAIMS.Core.Entities;
using MAIMS.Data.Configurations;
using MAIMS.Data.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace MAIMS.Data;

/// <summary>
/// EF Core DbContext for MAIMS. Uses Pomelo MySQL provider with utf8mb4.
/// Registered as pooled DbContextFactory (not single DbContext) so each WinForms
/// operation gets a short-lived context bound to its own transaction.
///
/// IMPORTANT: DbContextPool requires EXACTLY ONE public constructor that takes
/// a single DbContextOptions parameter. The audit interceptor is added via
/// DbContextOptionsBuilder.AddInterceptors at DI registration time (see
/// ServiceCollectionExtensions.AddMaimsData), NOT via constructor injection.
/// </summary>
public class MaimsDbContext : DbContext
{
    public MaimsDbContext(DbContextOptions<MaimsDbContext> options) : base(options)
    {
    }

    // Organisation
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();

    // Assets
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetLifecycleEvent> AssetLifecycleEvents => Set<AssetLifecycleEvent>();
    public DbSet<AssetAttachment> AssetAttachments => Set<AssetAttachment>();

    // Inventory
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();

    // Audit (append-only)
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyMaimsConfigurations();
        base.OnModelCreating(modelBuilder);
    }
}
