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
