using MAIMS.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MAIMS.Data.Configurations;

/// <summary>
/// Centralised EF Core Fluent API configurations. Keeping them in one file
/// per aggregate keeps the DbContext OnModelCreating short and reviewable.
/// Conventions enforced here: BIGINT UNSIGNED PKs, utf8mb4, FK cascading rules,
/// indexes on hot lookup paths.
/// </summary>
public static class ModelBuilderExtensions
{
    public static void ApplyMaimsConfigurations(this ModelBuilder mb)
    {
        ConfigureOrganisation(mb);
        ConfigureAssets(mb);
        ConfigureInventory(mb);
        ConfigureAuditLog(mb);
    }

    private static void ConfigureOrganisation(ModelBuilder mb)
    {
        mb.Entity<Department>(b =>
        {
            b.ToTable("department");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.HeadUserId).HasColumnName("head_user_id");
            b.Property(x => x.ParentDepartmentId).HasColumnName("parent_department_id");
            b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentDepartmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Head).WithMany().HasForeignKey(x => x.HeadUserId).OnDelete(DeleteBehavior.SetNull);
            ApplyAuditColumns<Department>(b);
        });

        mb.Entity<User>(b =>
        {
            b.ToTable("user");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
            b.HasIndex(x => x.Email).IsUnique();
            b.Property(x => x.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.Username).IsUnique();
            b.Property(x => x.RoleId).HasColumnName("role_id");
            b.Property(x => x.DepartmentId).HasColumnName("department_id");
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(120).IsRequired();
            b.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
            b.Property(x => x.FailedLoginAttempts).HasColumnName("failed_login_attempts");
            b.HasOne(x => x.Role).WithMany(x => x.Users).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Department).WithMany(x => x.Users).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            ApplyAuditColumns<User>(b);
        });

        mb.Entity<Role>(b =>
        {
            b.ToTable("role");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            b.HasIndex(x => x.Name).IsUnique();
            b.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
            b.Property(x => x.PermissionsJson).HasColumnName("permissions_json").HasColumnType("JSON").IsRequired();
            ApplyAuditColumns<Role>(b);
        });
    }

    private static void ConfigureAssets(ModelBuilder mb)
    {
        mb.Entity<AssetCategory>(b =>
        {
            b.ToTable("asset_category");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            b.Property(x => x.CategoryType).HasColumnName("category_type");
            b.Property(x => x.ParentId).HasColumnName("parent_id");
            b.Property(x => x.DepreciationMethod).HasColumnName("depreciation_method").HasMaxLength(30);
            b.Property(x => x.UsefulLifeYears).HasColumnName("useful_life_years");
            b.Property(x => x.SalvageValuePct).HasColumnName("salvage_value_pct").HasColumnType("DECIMAL(5,2)");
            b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            ApplyAuditColumns<AssetCategory>(b);
        });

        mb.Entity<Location>(b =>
        {
            b.ToTable("location");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.LocationType).HasColumnName("type").HasMaxLength(30).IsRequired();
            b.Property(x => x.ParentId).HasColumnName("parent_id");
            b.Property(x => x.GpsLat).HasColumnName("gps_lat").HasColumnType("DECIMAL(10,7)");
            b.Property(x => x.GpsLng).HasColumnName("gps_lng").HasColumnType("DECIMAL(10,7)");
            b.Property(x => x.Address).HasColumnName("address").HasMaxLength(500);
            b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            ApplyAuditColumns<Location>(b);
        });

        mb.Entity<Asset>(b =>
        {
            b.ToTable("asset");
            b.HasKey(x => x.Id);
            b.Property(x => x.AssetCode).HasColumnName("asset_code").HasMaxLength(40).IsRequired();
            b.HasIndex(x => x.AssetCode).IsUnique();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
            b.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
            b.Property(x => x.CategoryId).HasColumnName("category_id");
            b.Property(x => x.DepartmentId).HasColumnName("department_id");
            b.Property(x => x.LocationId).HasColumnName("location_id");
            b.Property(x => x.CustodianUserId).HasColumnName("custodian_user_id");
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.AcquisitionDate).HasColumnName("acquisition_date");
            b.Property(x => x.AcquisitionCost).HasColumnName("acquisition_cost").HasColumnType("DECIMAL(14,2)");
            b.Property(x => x.CurrentBookValue).HasColumnName("current_book_value").HasColumnType("DECIMAL(14,2)");
            b.Property(x => x.ConditionRating).HasColumnName("condition_rating");
            b.Property(x => x.ParentAssetId).HasColumnName("parent_asset_id");
            b.Property(x => x.SerialNumber).HasColumnName("serial_number").HasMaxLength(80);
            b.Property(x => x.FundingSource).HasColumnName("funding_source").HasMaxLength(80);
            b.Property(x => x.BarcodePayload).HasColumnName("barcode_payload").HasMaxLength(200);
            b.Property(x => x.DisposalMethod).HasColumnName("disposal_method");
            b.Property(x => x.DisposalDate).HasColumnName("disposal_date");
            b.Property(x => x.DisposalProceeds).HasColumnName("disposal_proceeds").HasColumnType("DECIMAL(14,2)");
            b.Property(x => x.DisposalApprovedBy).HasColumnName("disposal_approved_by");

            b.HasIndex(x => new { x.DepartmentId, x.Status });
            b.HasIndex(x => x.CategoryId);
            b.HasIndex(x => x.CustodianUserId);
            b.HasIndex(x => x.SerialNumber);

            b.HasOne(x => x.Category).WithMany(x => x.Assets).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Custodian).WithMany().HasForeignKey(x => x.CustodianUserId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.ParentAsset).WithMany(x => x.ChildAssets).HasForeignKey(x => x.ParentAssetId).OnDelete(DeleteBehavior.Restrict);
            ApplyAuditColumns<Asset>(b);
        });

        mb.Entity<AssetLifecycleEvent>(b =>
        {
            b.ToTable("asset_lifecycle_event");
            b.HasKey(x => x.Id);
            b.Property(x => x.AssetId).HasColumnName("asset_id");
            b.Property(x => x.EventType).HasColumnName("event_type");
            b.Property(x => x.EventDate).HasColumnName("event_date");
            b.Property(x => x.PerformedBy).HasColumnName("performed_by");
            b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
            b.Property(x => x.FromStatus).HasColumnName("from_status");
            b.Property(x => x.ToStatus).HasColumnName("to_status");
            b.Property(x => x.Cost).HasColumnName("cost").HasColumnType("DECIMAL(14,2)");
            b.HasIndex(x => new { x.AssetId, x.EventDate });
            // Restrict: lifecycle events are the audit trail of an asset — they must
            // survive even if the asset is hard-deleted (defence in depth).
            b.HasOne(x => x.Asset).WithMany(x => x.LifecycleEvents).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            ApplyAuditColumns<AssetLifecycleEvent>(b);
        });

        mb.Entity<AssetAttachment>(b =>
        {
            b.ToTable("asset_attachment");
            b.HasKey(x => x.Id);
            b.Property(x => x.AssetId).HasColumnName("asset_id");
            b.Property(x => x.FilePath).HasColumnName("file_path").HasMaxLength(500).IsRequired();
            b.Property(x => x.FileType).HasColumnName("file_type").HasMaxLength(80).IsRequired();
            b.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255);
            b.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
            b.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
            // Restrict: attachments are document references (receipts, photos, manuals)
            // and should not be cascade-deleted with the asset — the file on disk
            // would remain, leaving an orphaned reference.
            b.HasOne(x => x.Asset).WithMany(x => x.Attachments).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            ApplyAuditColumns<AssetAttachment>(b);
        });
    }

    private static void ConfigureInventory(ModelBuilder mb)
    {
        mb.Entity<Item>(b =>
        {
            b.ToTable("item");
            b.HasKey(x => x.Id);
            b.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(40).IsRequired();
            b.HasIndex(x => x.Sku).IsUnique();
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
            b.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
            b.Property(x => x.Category).HasColumnName("category").HasMaxLength(80).IsRequired();
            b.Property(x => x.Uom).HasColumnName("uom");
            b.Property(x => x.ReorderPoint).HasColumnName("reorder_point").HasColumnType("DECIMAL(14,3)");
            b.Property(x => x.ReorderQty).HasColumnName("reorder_qty").HasColumnType("DECIMAL(14,3)");
            b.Property(x => x.UnitCost).HasColumnName("unit_cost").HasColumnType("DECIMAL(14,4)");
            b.Property(x => x.PreferredSupplier).HasColumnName("preferred_supplier").HasMaxLength(200);
            b.Property(x => x.LeadTimeDays).HasColumnName("lead_time_days");
            b.Property(x => x.HazardousFlag).HasColumnName("hazardous_flag");
            b.Property(x => x.StorageRequirements).HasColumnName("storage_requirements").HasMaxLength(200);
            b.Property(x => x.Manufacturer).HasColumnName("manufacturer").HasMaxLength(150);
            b.Property(x => x.ManufacturerPartNumber).HasColumnName("manufacturer_part_number").HasMaxLength(80);
            ApplyAuditColumns<Item>(b);
        });

        mb.Entity<Warehouse>(b =>
        {
            b.ToTable("warehouse");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.DepartmentId).HasColumnName("department_id");
            b.Property(x => x.LocationId).HasColumnName("location_id");
            b.Property(x => x.IsActive).HasColumnName("is_active");
            b.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
            ApplyAuditColumns<Warehouse>(b);
        });

        mb.Entity<StockBalance>(b =>
        {
            b.ToTable("stock_balance");
            b.HasKey(x => x.Id);
            b.Property(x => x.ItemId).HasColumnName("item_id");
            b.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            b.Property(x => x.BinLocation).HasColumnName("bin_location").HasMaxLength(40);
            b.Property(x => x.QtyOnHand).HasColumnName("qty_on_hand").HasColumnType("DECIMAL(14,3)");
            b.Property(x => x.QtyReserved).HasColumnName("qty_reserved").HasColumnType("DECIMAL(14,3)");
            b.Property(x => x.QtyOnOrder).HasColumnName("qty_on_order").HasColumnType("DECIMAL(14,3)");
            b.HasIndex(x => new { x.ItemId, x.WarehouseId, x.BinLocation }).IsUnique();
            // Restrict (not Cascade): if an Item is hard-deleted, the StockBalance
            // rows must NOT be cascade-deleted because they are referenced by
            // StockTransaction rows (audit trail). Soft-delete is the normal path
            // and does not trigger cascade, but Restrict is a safety net in case
            // a future hard-delete is performed.
            b.HasOne(x => x.Item).WithMany(x => x.StockBalances).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Warehouse).WithMany(x => x.StockBalances).HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            ApplyAuditColumns<StockBalance>(b);
        });

        mb.Entity<StockTransaction>(b =>
        {
            b.ToTable("stock_transaction");
            b.HasKey(x => x.Id);
            b.Property(x => x.TransactionType).HasColumnName("transaction_type");
            b.Property(x => x.ItemId).HasColumnName("item_id");
            b.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            b.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("DECIMAL(14,3)");
            b.Property(x => x.FromWarehouseId).HasColumnName("from_warehouse_id");
            b.Property(x => x.ToWarehouseId).HasColumnName("to_warehouse_id");
            b.Property(x => x.ToAssetId).HasColumnName("to_asset_id");
            b.Property(x => x.RequesterUserId).HasColumnName("requester_user_id");
            b.Property(x => x.PerformedBy).HasColumnName("performed_by");
            b.Property(x => x.TransactionDate).HasColumnName("transaction_date");
            b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(40);
            b.Property(x => x.ReferenceDocNo).HasColumnName("reference_doc_no").HasMaxLength(60);
            b.Property(x => x.LotBatch).HasColumnName("lot_batch").HasMaxLength(60);
            b.Property(x => x.ExpiryDate).HasColumnName("expiry_date");
            b.Property(x => x.Supplier).HasColumnName("supplier").HasMaxLength(200);
            b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
            b.HasIndex(x => new { x.ItemId, x.TransactionDate });
            b.HasIndex(x => x.TransactionType);
            b.HasOne(x => x.Item).WithMany(x => x.Transactions).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.FromWarehouse).WithMany().HasForeignKey(x => x.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ToWarehouse).WithMany().HasForeignKey(x => x.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ToAsset).WithMany().HasForeignKey(x => x.ToAssetId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.SetNull);
            ApplyAuditColumns<StockTransaction>(b);
        });
    }

    private static void ConfigureAuditLog(ModelBuilder mb)
    {
        mb.Entity<AuditLog>(b =>
        {
            b.ToTable("audit_log");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
            b.Property(x => x.EntityId).HasColumnName("entity_id");
            b.Property(x => x.Action).HasColumnName("action").HasMaxLength(20).IsRequired();
            b.Property(x => x.ChangedBy).HasColumnName("changed_by");
            b.Property(x => x.ChangedAt).HasColumnName("changed_at");
            b.Property(x => x.BeforeJson).HasColumnName("before_json").HasColumnType("JSON");
            b.Property(x => x.AfterJson).HasColumnName("after_json").HasColumnType("JSON");
            b.Property(x => x.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
            b.Property(x => x.MachineName).HasColumnName("machine_name").HasMaxLength(100);
            b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(50);
            b.HasIndex(x => new { x.EntityType, x.EntityId });
            b.HasIndex(x => x.ChangedAt);
            b.HasIndex(x => x.ChangedBy);
        });
    }

    /// <summary>
    /// Maps the common audit columns (created_at, updated_at, created_by, updated_by,
    /// is_deleted, deleted_at) for any entity implementing IAuditable + ISoftDeletable.
    /// </summary>
    private static void ApplyAuditColumns<TEntity>(EntityTypeBuilder<TEntity> b)
        where TEntity : class
    {
        b.Property(nameof(BaseEntity.CreatedAt)).HasColumnName("created_at");
        b.Property(nameof(BaseEntity.UpdatedAt)).HasColumnName("updated_at");
        b.Property(nameof(BaseEntity.CreatedBy)).HasColumnName("created_by");
        b.Property(nameof(BaseEntity.UpdatedBy)).HasColumnName("updated_by");
        b.Property(nameof(BaseEntity.IsDeleted)).HasColumnName("is_deleted");
        b.Property(nameof(BaseEntity.DeletedAt)).HasColumnName("deleted_at");
        b.HasIndex(nameof(BaseEntity.IsDeleted));
        b.HasQueryFilter(e => !EF.Property<bool>(e, nameof(BaseEntity.IsDeleted)));
    }
}
