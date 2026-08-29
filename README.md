# Municipal-asset-and-inventory-management

WinForms .NET 8 + MySQL 8 line-of-business application for tracking municipal
fixed assets and consumable inventory across departments, with role-based
access control (RBAC) and an immutable audit trail.

## Features

- **Fixed Assets** — register, transfer, inspect, dispose, and track the full lifecycle of assets with automatic code generation (`DEPT-CATEGORY-SEQ`)
- **Inventory** — receive, issue, adjust, transfer, and cycle-count stock across multiple warehouses with reorder-point alerts
- **RBAC** — 7 built-in roles with 31 fine-grained permissions, enforced at both service and UI layers
- **Immutable Audit Trail** — every CUD operation is logged with before/after JSON snapshots; `audit_log` table is protected by MySQL triggers + INSERT-only grants
- **Reporting** — asset status distribution, valuation, depreciation, inventory valuation, and department asset counts
- **Dashboard** — KPI cards, asset status/condition breakdown, top low-stock items, and recent activity feed


## Architecture

```
┌─────────────────────────────────────────────┐
│ MAIMS.WinUI (WinForms .NET 8)               │
│  32 forms · DI container · theming          │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│ MAIMS.Services (async, permission-checked)  │
│  AssetService · InventoryService ·          │
│  AuditService · AuthService ·               │
│  AssetAttachmentService · ReferenceData     │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│ MAIMS.Data (EF Core 8 + Pomelo MySQL)       │
│  Pooled DbContext · Audit interceptor ·     │
│  Global query filter (soft-delete)          │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│ MAIMS.Core (no dependencies)                │
│  Entities · Enums · DTOs · Interfaces       │
└─────────────────────────────────────────────┘
```

## Tech Stack

| Layer | Technology |
|---|---|
| UI | WinForms (.NET 8, `net8.0-windows`) |
| ORM | EF Core 8.0.10 + Pomelo MySQL 8.0.2 |
| Database | MySQL 8 (InnoDB, utf8mb4) |
| Auth | BCrypt.Net-Next (cost 11) |
| Validation | FluentValidation 11.10 |
| Logging | Serilog (File + Console) |
| Export | ClosedXML (Excel) · QuestPDF (PDF) |
| QR Codes | QRCoder 1.6 |
| Testing | xUnit · FluentAssertions · Moq · EF InMemory |

## Solution Structure

```
MAIMS.sln
├── src/
│   ├── MAIMS.Core/         Domain entities, enums, DTOs, interfaces
│   ├── MAIMS.Data/         DbContext, migrations, audit interceptor, seeder
│   ├── MAIMS.Services/     Business services (RBAC, validation, transactions)
│   ├── MAIMS.WinUI/        WinForms app — 32 forms, theming, DI
│   └── MAIMS.Reports/      ClosedXML Excel + QuestPDF exporters
├── tests/
│   ├── MAIMS.Core.Tests/       Permission set tests
│   ├── MAIMS.Services.Tests/   Service-layer unit tests (EF InMemory)
│   └── MAIMS.Data.Tests/       DbContext configuration tests
└── db/
    ├── 00_init.sql             Database + app user
    ├── 10_audit_log_immutable.sql   Audit triggers
    ├── 20_audit_log_grants.sql      INSERT-only grants
    ├── 30–41_cleanup*.sql       Data cleanup scripts
    └── 50_full_reset.sql        Full database reset
```

## Getting Started

### Prerequisites

- .NET 8 SDK
- MySQL 8 server
- Visual Studio 2022 17.8+ (optional) or `dotnet` CLI

### Setup

```bash
# 1. Create database + app user
mysql -u root -p < db/00_init.sql

# 2. Run the app — it creates all tables + seeds data automatically
cd src/MAIMS.WinUI
dotnet run
```

On first launch, the seeder creates:
- 17 municipal departments
- 7 built-in roles with default permission sets
- 8 asset categories
- A bootstrap `admin` user (password: `Admin@123` — **change immediately!**)

### Post-Setup (Audit Hardening)

After the app has created the schema, run these to lock down `audit_log`:

```bash
mysql -u root -p maims < db/10_audit_log_immutable.sql
mysql -u root -p maims < db/20_audit_log_grants.sql
```

## Configuration

Edit `src/MAIMS.WinUI/appsettings.json`:

```json
"ConnectionStrings": {
  "MaimsDb": "Server=localhost;Port=3306;Database=maims;User=maims_app;Password=CHANGE_ME;CharSet=utf8mb4;SslMode=Required;Pooling=true;"
}
```

> **Production:** Use `SslMode=Required`, store credentials in Windows DPAPI / user secrets, and disable `EnableSensitiveDataLogging()` in `MAIMS.Data/ServiceCollectionExtensions.cs`.

## Testing

```bash
dotnet test
```

## Database Reset

To wipe all test data and start fresh with clean IDs:

```bash
mysql -u root -p maims < db/50_full_reset.sql
```

## Roles & Permissions

| Role | Key Capabilities |
|---|---|
| SystemAdministrator | Full system access (users, roles, departments, all modules) |
| AssetManager | Asset lifecycle management, financial reports |
| DepartmentHead | Own-department assets, approve transfers |
| InventoryClerk | Stock receive/issue/adjust/count |
| FieldWorker | View assigned assets, report condition |
| Auditor | Read-only access to everything + export |
| FinanceOfficer | Valuations, depreciation, compliance reports |


