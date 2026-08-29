namespace MAIMS.Core.Enums;

/// <summary>
/// Application-wide permissions. Stored as a JSON array in the Role.PermissionsJson column.
/// Each permission maps to a specific business capability — roles get exactly
/// the permissions they need for their job, no more, no less (principle of
/// least privilege per spec §9.1).
///
/// 31 permissions covering ALL modules added throughout the project:
///   - 10 asset permissions (view, create, edit, delete, transfer, dispose, inspect, attachments, maintenance, qrcode)
///   - 7  inventory permissions (view, receive, issue, adjust, writeoff, cyclecount, reorder)
///   - 1  cross-department permission
///   - 7  administration permissions (users, roles, depts, warehouses, locations, dashboard, suppliers)
///   - 3  audit permissions (view, export, purge)
///   - 3  reporting permissions (operational, financial, compliance)
/// </summary>
public static class Permissions
{
    // ── Asset management (10) ─────────────────────────────────────────
    public const string AssetView = "asset.view";                  // View asset registry + details + lifecycle history
    public const string AssetCreate = "asset.create";              // Register new assets
    public const string AssetEdit = "asset.edit";                  // Edit asset properties (name, cost, condition, etc.)
    public const string AssetDelete = "asset.delete";              // Soft-delete assets
    public const string AssetTransfer = "asset.transfer";          // Transfer assets between departments / locations / custodians
    public const string AssetDispose = "asset.dispose";            // Dispose assets (sale, scrap, donation, trade-in, loss)
    public const string AssetInspect = "asset.inspect";            // Record condition inspections (1-5 rating)
    public const string AssetAttachments = "asset.attachments";    // Upload / download / delete asset attachments (receipts, photos, manuals)
    public const string AssetMaintenance = "asset.maintenance";    // Put asset into / take out of maintenance (UnderMaintenance status)
    public const string AssetQrCode = "asset.qrcode";              // Generate and print QR/barcode labels for assets

    // ── Inventory management (7) ──────────────────────────────────────
    public const string InventoryView = "inventory.view";          // View item catalog + stock balances + transaction history
    public const string InventoryReceive = "inventory.receive";    // Receive stock from suppliers (Stock-In)
    public const string InventoryIssue = "inventory.issue";        // Issue stock to requesters / assets (Stock-Out)
    public const string InventoryAdjust = "inventory.adjust";      // Adjust stock quantities (damage, loss, count correction, expired)
    public const string InventoryWriteOff = "inventory.writeoff";  // Write off stock (remove with reason + approval)
    public const string InventoryCycleCount = "inventory.cyclecount"; // Perform cycle counts (physical inventory counting)
    public const string InventoryReorder = "inventory.reorder";    // View reorder suggestions (items below reorder point)

    // ── Cross-department (1) ──────────────────────────────────────────
    public const string CrossDepartmentView = "xdept.view";        // View data outside own department (needed for cross-dept transfers)

    // ── Administration (7) ────────────────────────────────────────────
    public const string UserManage = "admin.users";                // Create / edit / delete / activate / deactivate users
    public const string RoleManage = "admin.roles";                // Manage roles + their permission sets
    public const string DeptManage = "admin.depts";                // Manage departments (create, edit, hierarchy)
    public const string WarehouseManage = "admin.warehouses";      // Manage warehouses (create, edit, activate/deactivate)
    public const string LocationManage = "admin.locations";        // Manage locations (sites, buildings, floors, rooms)
    public const string DashboardView = "dashboard.view";          // View dashboard / overview metrics (asset counts, valuations, alerts)
    public const string SupplierView = "supplier.view";            // View supplier information (derived from items + transactions)

    // ── Audit (3) ────────────────────────────────────────────────────
    public const string AuditView = "audit.view";                  // View audit log entries
    public const string AuditExport = "audit.export";              // Export audit trail (CSV download)
    public const string AuditPurge = "audit.purge";                // Purge invalid audit log entries (requires root MySQL — admin only)

    // ── Reporting (3) ────────────────────────────────────────────────
    public const string ReportOperational = "report.operational";  // Operational reports (status distribution, low stock, transfers, dept assets)
    public const string ReportFinancial = "report.financial";      // Financial reports (asset valuation, depreciation, inventory valuation)
    public const string ReportCompliance = "report.compliance";    // Compliance reports (audit trail extract, asset existence verification)

    /// <summary>
    /// Default permission sets for the seven built-in roles. Used by the DB seeder.
    ///
    /// DESIGN PRINCIPLES:
    ///   1. Principle of least privilege — each role gets EXACTLY what it needs.
    ///   2. Separation of duties — the person who creates an asset cannot approve its disposal.
    ///   3. Read-only roles (Auditor, FinanceOfficer) cannot modify any business data.
    ///   4. Department-scoped roles (DepartmentHead, FieldWorker) default to own-department data.
    ///   5. Cross-department access is granted only to roles that need it for their job.
    ///
    /// ┌──────────────────────┬────────────────────────────────────────────────────────────┐
    /// │ Role                 │ Key Responsibilities                                        │
    /// ├──────────────────────┼────────────────────────────────────────────────────────────┤
    /// │ SystemAdministrator  │ IT staff — full system config, user/role/dept management    │
    /// │ AssetManager         │ Finance/Asset Office — full asset lifecycle management     │
    /// │ DepartmentHead       │ Dept manager — own dept assets + approve transfers         │
    /// │ InventoryClerk       │ Warehouse staff — stock receive/issue/adjust/count         │
    /// │ FieldWorker          │ Operations — view assigned assets + report condition       │
    /// │ Auditor              │ Internal/External audit — read-only everything + export    │
    /// │ FinanceOfficer       │ Finance — valuations, depreciation, compliance reports     │
    /// └──────────────────────┴────────────────────────────────────────────────────────────┘
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultRolePermissions =
        new Dictionary<string, string[]>
        {
            // ════════════════════════════════════════════════════════════════
            // SystemAdministrator — IT staff who configure and maintain MAIMS.
            // Can do EVERYTHING: manage users, roles, departments, warehouses,
            // locations, assets, inventory, audit, and all reports.
            // 31 permissions (all).
            // ════════════════════════════════════════════════════════════════
            ["SystemAdministrator"] = new[]
            {
                // Assets — full CRUD + lifecycle
                AssetView, AssetCreate, AssetEdit, AssetDelete,
                AssetTransfer, AssetDispose, AssetInspect,
                AssetAttachments, AssetMaintenance, AssetQrCode,
                // Inventory — full CRUD
                InventoryView, InventoryReceive, InventoryIssue,
                InventoryAdjust, InventoryWriteOff,
                InventoryCycleCount, InventoryReorder,
                // Cross-department
                CrossDepartmentView,
                // Administration — full
                DashboardView, UserManage, RoleManage,
                DeptManage, WarehouseManage, LocationManage, SupplierView,
                // Audit — full (including purge)
                AuditView, AuditExport, AuditPurge,
                // Reports — all
                ReportOperational, ReportFinancial, ReportCompliance
            },

            // ════════════════════════════════════════════════════════════════
            // AssetManager — Finance / Asset Office staff who manage the
            // asset register. Creates, edits, transfers, disposes, inspects,
            // and maintains assets. Views inventory and suppliers. Runs
            // financial + compliance reports.
            // Does NOT manage users, roles, departments, warehouses, or locations.
            // Does NOT handle stock operations (that's InventoryClerk's job).
            // 18 permissions.
            // ════════════════════════════════════════════════════════════════
            ["AssetManager"] = new[]
            {
                // Assets — full lifecycle (no delete — disposal is the proper path)
                AssetView, AssetCreate, AssetEdit,
                AssetTransfer, AssetDispose,
                AssetInspect, AssetAttachments,
                AssetMaintenance, AssetQrCode,
                // Inventory — view only (asset managers don't handle stock)
                InventoryView,
                InventoryReorder,  // can see what needs reordering for asset-related purchases
                // Cross-department + overview
                CrossDepartmentView, DashboardView, SupplierView,
                // Reports — financial + compliance (no operational — that's dept-level)
                ReportFinancial, ReportCompliance
            },

            // ════════════════════════════════════════════════════════════════
            // DepartmentHead — Heads of departments who manage their own
            // department's assets. Can edit + transfer assets (including
            // cross-department transfers with approval), inspect condition,
            // and manage attachments. Views inventory and can issue stock
            // for their department. Runs operational reports.
            // No create/dispose (that's AssetManager's job).
            // No delete (disposal is the proper path).
            // 12 permissions.
            // ════════════════════════════════════════════════════════════════
            ["DepartmentHead"] = new[]
            {
                // Assets — edit + transfer + inspect + attachments + maintenance
                // (no create, no delete, no dispose — those go through AssetManager)
                AssetView, AssetEdit, AssetTransfer,
                AssetInspect, AssetAttachments, AssetMaintenance,
                // Inventory — view + issue (dept heads can request stock for their dept)
                InventoryView, InventoryIssue,
                // Cross-dept (needed for cross-department transfers)
                CrossDepartmentView,
                // Dashboard for dept overview
                DashboardView,
                // Reports — operational only (status, low stock, transfers)
                ReportOperational
            },

            // ════════════════════════════════════════════════════════════════
            // InventoryClerk — Warehouse staff who handle stock operations.
            // Receives, issues, adjusts, writes off, cycle-counts stock.
            // Manages warehouses and locations. Views reorder suggestions.
            // Can view assets (to know what they're issuing to) but cannot
            // modify them.
            // 13 permissions.
            // ════════════════════════════════════════════════════════════════
            ["InventoryClerk"] = new[]
            {
                // Assets — view only (needed when issuing stock to an asset)
                AssetView,
                // Inventory — full stock operations
                InventoryView, InventoryReceive, InventoryIssue,
                InventoryAdjust, InventoryWriteOff,
                InventoryCycleCount, InventoryReorder,
                // Warehouse + Location management (warehouse staff manages these)
                WarehouseManage, LocationManage,
                // Dashboard for warehouse overview
                DashboardView,
                // Reports — operational (low stock, stock balances, transactions)
                ReportOperational
            },

            // ════════════════════════════════════════════════════════════════
            // FieldWorker — Operations staff who use assets in the field.
            // Can view assets assigned to them and report condition (inspect).
            // Can view inventory to check if parts are available.
            // Cannot modify anything.
            // 3 permissions.
            // ════════════════════════════════════════════════════════════════
            ["FieldWorker"] = new[]
            {
                // Assets — view + inspect (report condition in the field)
                AssetView, AssetInspect,
                // Inventory — view only (check if parts/supplies are available)
                InventoryView
            },

            // ════════════════════════════════════════════════════════════════
            // Auditor — Internal/external auditors with read-only access to
            // everything. Can view all data across departments, export audit
            // trail, and run all reports. Cannot modify any business data.
            // Cannot purge audit entries (that's admin-only).
            // 11 permissions.
            // ════════════════════════════════════════════════════════════════
            ["Auditor"] = new[]
            {
                // Assets — view only
                AssetView,
                // Inventory — view only
                InventoryView,
                // Cross-department — full visibility (auditors see everything)
                CrossDepartmentView,
                // Dashboard + suppliers
                DashboardView, SupplierView,
                // Audit — view + export (NO purge — that's admin-only)
                AuditView, AuditExport,
                // Reports — all (operational + financial + compliance)
                ReportOperational, ReportFinancial, ReportCompliance
            },

            // ════════════════════════════════════════════════════════════════
            // FinanceOfficer — Finance department staff who handle valuation
            // and reporting. Views assets + inventory across departments for
            // valuation purposes. Runs financial and compliance reports
            // (valuation, depreciation, audit extract). Can view and export
            // audit log for compliance purposes.
            // Cannot modify any data. Cannot purge audit entries.
            // 10 permissions.
            // ════════════════════════════════════════════════════════════════
            ["FinanceOfficer"] = new[]
            {
                // Assets — view only
                AssetView,
                // Inventory — view only
                InventoryView,
                // Cross-department + overview
                CrossDepartmentView, DashboardView, SupplierView,
                // Audit — view + export (for compliance verification)
                // NO purge (admin-only)
                AuditView, AuditExport,
                // Reports — financial + compliance (no operational — they care about money)
                ReportFinancial, ReportCompliance
            }
        };

    /// <summary>
    /// Built-in role names that cannot be deleted from the system.
    /// </summary>
    public static readonly IReadOnlyList<string> SystemRoles = DefaultRolePermissions.Keys.ToArray();
}
