-- ============================================================================
-- MAIMS — Municipal Asset & Inventory Management System
-- Database initialisation script for MySQL 8 (InnoDB / utf8mb4).
--
-- RUN ORDER (recommended — "Option A: Fresh install" in README.md):
--   1. This script (00_init.sql)         — creates DB + app user (broad grants)
--   2. MAIMS app first launch            — EnsureCreatedAsync creates ALL tables
--                                          + seeds departments/roles/categories/admin
--   3. 10_audit_log_immutable.sql        — adds BEFORE UPDATE/DELETE triggers
--   4. 20_audit_log_grants.sql           — restricts audit_log to INSERT-only
--
-- Alternative (Option B — EF Core migrations):
--   1. This script (00_init.sql)
--   2. `dotnet ef database update`       — creates schema via migration
--   3. 10_audit_log_immutable.sql
--   4. 20_audit_log_grants.sql
--
-- IMPORTANT: Do NOT mix both options. If 00_init.sql creates the database,
-- EnsureCreatedAsync becomes a no-op and seeding will fail. Pick ONE path.
--
-- Why split? MySQL cannot REVOKE table-level privileges on a table that does
-- not exist yet. The audit_log table is created by the app/migrations (step 2),
-- so the table-level REVOKE must run AFTER (step 4).
-- ============================================================================

CREATE DATABASE IF NOT EXISTS maims
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_0900_ai_ci;

USE maims;

-- ---------------------------------------------------------------------------
-- Application user. Replace CHANGE_ME with a strong password.
--
-- This script grants SELECT/INSERT/UPDATE/DELETE on ALL tables in maims.*.
-- The audit_log table is later locked down to INSERT-only in
-- 20_audit_log_grants.sql (run after the app creates the table).
--
-- Until then, immutability of audit_log is still enforced by:
--   (a) Application code — AuditLog entity has no Update/Remove code path
--   (b) MySQL triggers   — 10_audit_log_immutable.sql blocks UPDATE/DELETE
-- The MySQL user-level restriction (this script) is the THIRD layer of defence.
-- ---------------------------------------------------------------------------
CREATE USER IF NOT EXISTS 'maims_app'@'%' IDENTIFIED BY 'Pass1234';

GRANT SELECT, INSERT, UPDATE, DELETE ON maims.* TO 'maims_app'@'%';

-- Allow a separate migration user to evolve the schema.
-- CREATE USER IF NOT EXISTS 'maims_migration'@'localhost' IDENTIFIED BY 'Pass1234';
-- GRANT ALL PRIVILEGES ON maims.* TO 'maims_migration'@'localhost';

FLUSH PRIVILEGES;

-- ---------------------------------------------------------------------------
-- Performance / connection tuning recommendations (server-level, not schema):
--   innodb_buffer_pool_size = 70% of server RAM
--   innodb_log_file_size    = 512M
--   max_connections         = 200
--   character-set-server    = utf8mb4
--   collation-server        = utf8mb4_0900_ai_ci
-- ---------------------------------------------------------------------------

-- The schema itself is created by the MAIMS app on first launch
-- (EnsureCreatedAsync) OR by EF Core migrations (`dotnet ef database update`).
-- This script only prepares the database and user so the app can connect.
