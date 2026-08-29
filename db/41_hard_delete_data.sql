-- ============================================================================
-- MAIMS — Hard delete script for SOFT-DELETED business data
--
-- This script PERMANENTLY hard-deletes all transactional data from tables
-- that use soft-delete (is_deleted flag). These tables have NO triggers,
-- so we can DELETE directly without dropping/recreating anything.
--
-- WHAT THIS SCRIPT DELETES:
--   - Assets (including soft-deleted ones — bypasses the is_deleted filter)
--   - Asset lifecycle events
--   - Asset attachments
--   - Stock transactions
--   - Stock balances
--   - Items (including soft-deleted ones)
--
-- WHAT THIS SCRIPT DOES NOT DELETE:
--   - Warehouses (uncomment below if needed)
--   - Locations (uncomment below if needed)
--   - Audit log (see "Audit log" note below)
--   - Users, Departments, Roles, Asset categories (reference data)
--
-- AUDIT LOG:
--   This script does NOT touch audit_log. Audit log is supposed to be
--   IMMUTABLE (append-only). If you want to clear it, use one of:
--     Option A: "Purge Invalid…" button in the Audit Log Viewer (UI)
--     Option B: db/40_cleanup_data.sql (drops triggers temporarily, then deletes)
--     Option C: db/50_full_reset.sql (full nuclear reset, including audit_log)
--
-- AUTO_INCREMENT RESET:
--   After deleting, this script resets AUTO_INCREMENT counters to 1 for all
--   affected tables, so new records start from ID = 1 (clean slate).
--
-- CASCADE SAFETY:
--   All FKs on these tables use DeleteBehavior.Restrict (not Cascade), so
--   you MUST delete child tables before parent tables (order below matters).
--   The order in this script is: child → parent.
--
-- Usage:
--   mysql -u root -p maims < db/41_hard_delete_data.sql
--
--   Can also be run as maims_app (no trigger drop needed for these tables).
-- ============================================================================

USE maims;

-- Show counts BEFORE hard delete
SELECT '=== BEFORE HARD DELETE ===' AS info;
SELECT CONCAT('Assets (including soft-deleted): ', COUNT(*)) AS info FROM asset;
SELECT CONCAT('  - soft-deleted: ', SUM(is_deleted)) AS info FROM asset;
SELECT CONCAT('Asset lifecycle events: ', COUNT(*)) AS info FROM asset_lifecycle_event;
SELECT CONCAT('Asset attachments: ', COUNT(*)) AS info FROM asset_attachment;
SELECT CONCAT('Stock balances: ', COUNT(*)) AS info FROM stock_balance;
SELECT CONCAT('Stock transactions: ', COUNT(*)) AS info FROM stock_transaction;
SELECT CONCAT('Items (including soft-deleted): ', COUNT(*)) AS info FROM item;
SELECT CONCAT('  - soft-deleted: ', SUM(is_deleted)) AS info FROM item;
SELECT CONCAT('Warehouses: ', COUNT(*)) AS info FROM warehouse;
SELECT CONCAT('Locations: ', COUNT(*)) AS info FROM location;

-- ──────────────────────────────────────────────────────────────────────────
-- Hard delete all business data (child → parent order, no triggers on these)
-- ──────────────────────────────────────────────────────────────────────────

-- Asset-related (child → parent)
DELETE FROM asset_attachment;
DELETE FROM asset_lifecycle_event;
DELETE FROM asset;          -- removes BOTH active and soft-deleted assets

-- Inventory-related (child → parent)
DELETE FROM stock_transaction;
DELETE FROM stock_balance;
DELETE FROM item;           -- removes BOTH active and soft-deleted items

-- Reference data (optional — uncomment if you also want to clear these)
-- DELETE FROM warehouse;
-- DELETE FROM location;

-- ──────────────────────────────────────────────────────────────────────────
-- Reset auto-increment counters so new records start from ID = 1
-- ──────────────────────────────────────────────────────────────────────────
ALTER TABLE asset AUTO_INCREMENT = 1;
ALTER TABLE asset_lifecycle_event AUTO_INCREMENT = 1;
ALTER TABLE asset_attachment AUTO_INCREMENT = 1;
ALTER TABLE stock_transaction AUTO_INCREMENT = 1;
ALTER TABLE stock_balance AUTO_INCREMENT = 1;
ALTER TABLE item AUTO_INCREMENT = 1;

-- Show counts AFTER hard delete
SELECT '=== AFTER HARD DELETE ===' AS info;
SELECT CONCAT('Assets: ', COUNT(*)) AS info FROM asset;
SELECT CONCAT('Asset lifecycle events: ', COUNT(*)) AS info FROM asset_lifecycle_event;
SELECT CONCAT('Asset attachments: ', COUNT(*)) AS info FROM asset_attachment;
SELECT CONCAT('Stock balances: ', COUNT(*)) AS info FROM stock_balance;
SELECT CONCAT('Stock transactions: ', COUNT(*)) AS info FROM stock_transaction;
SELECT CONCAT('Items: ', COUNT(*)) AS info FROM item;

-- NOTE: audit_log is NOT touched by this script. To clear audit_log:
--   Option A: Use "Purge Invalid…" button in Audit Log Viewer (UI)
--   Option B: Run db/40_cleanup_data.sql (drops triggers temporarily)
--   Option C: Run db/50_full_reset.sql (full reset including audit_log)
