-- ============================================================================
-- MAIMS — Data cleanup script
--
-- Deletes all transactional data (assets, stock, audit logs) but KEEPS:
--   - Departments (17 realistic municipal departments)
--   - Roles (7 built-in roles + permissions)
--   - Asset categories (8 categories)
--   - Users (except optionally the admin user)
--   - Warehouses, Locations, Items catalog (optional — see below)
--
-- Usage:
--   mysql -u root -p maims < db/40_cleanup_data.sql
--
-- Or run in MySQL Workbench as root (maims_app can't DELETE from audit_log).
-- ============================================================================

USE maims;

-- Show counts BEFORE cleanup
SELECT '=== BEFORE CLEANUP ===' AS info;
SELECT CONCAT('Assets: ', COUNT(*)) AS info FROM asset;
SELECT CONCAT('Asset lifecycle events: ', COUNT(*)) AS info FROM asset_lifecycle_event;
SELECT CONCAT('Asset attachments: ', COUNT(*)) AS info FROM asset_attachment;
SELECT CONCAT('Stock balances: ', COUNT(*)) AS info FROM stock_balance;
SELECT CONCAT('Stock transactions: ', COUNT(*)) AS info FROM stock_transaction;
SELECT CONCAT('Audit log entries: ', COUNT(*)) AS info FROM audit_log;

-- ──────────────────────────────────────────────────────────────────────────
-- Step 1: Drop the audit_log triggers temporarily (needed for DELETE)
-- ──────────────────────────────────────────────────────────────────────────
DROP TRIGGER IF EXISTS trg_audit_log_block_delete;
DROP TRIGGER IF EXISTS trg_audit_log_block_update;

-- ──────────────────────────────────────────────────────────────────────────
-- Step 2: Delete transactional data (child tables first, then parent)
-- ──────────────────────────────────────────────────────────────────────────

-- Asset-related (child → parent)
DELETE FROM asset_attachment;
DELETE FROM asset_lifecycle_event;
DELETE FROM asset;

-- Inventory-related (child → parent)
DELETE FROM stock_transaction;
DELETE FROM stock_balance;

-- Audit log (ALL entries — clean slate)
DELETE FROM audit_log;

-- ──────────────────────────────────────────────────────────────────────────
-- Step 3 (OPTIONAL): Also delete item catalog, warehouses, locations
-- Uncomment the lines below if you want a FULL data wipe (keep only
-- departments, roles, categories, and users).
--
-- By default, these are commented out so the item catalog and warehouse/
-- location master data survive the cleanup — you can re-stock existing
-- items without re-creating their SKU master records.
--
-- For a COMPLETE wipe including these tables, use db/50_full_reset.sql
-- instead — it deletes everything except the bootstrap admin user.
-- ──────────────────────────────────────────────────────────────────────────
-- DELETE FROM item;            -- removes BOTH active and soft-deleted items
-- DELETE FROM warehouse;
-- DELETE FROM location;

-- ──────────────────────────────────────────────────────────────────────────
-- Step 4 (OPTIONAL): Also delete all users EXCEPT admin
-- Uncomment to remove non-admin users.
-- ──────────────────────────────────────────────────────────────────────────
-- DELETE FROM user WHERE username != 'admin';

-- ──────────────────────────────────────────────────────────────────────────
-- Step 5: Recreate the audit_log triggers (restore immutability)
-- ──────────────────────────────────────────────────────────────────────────
DELIMITER $$

CREATE TRIGGER trg_audit_log_block_update
BEFORE UPDATE ON audit_log
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'audit_log is append-only. UPDATE is not permitted.';
END$$

CREATE TRIGGER trg_audit_log_block_delete
BEFORE DELETE ON audit_log
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'audit_log is append-only. DELETE is not permitted.';
END$$

DELIMITER ;

-- ──────────────────────────────────────────────────────────────────────────
-- Step 6: Reset auto-increment counters (optional — makes new IDs start from 1)
-- ──────────────────────────────────────────────────────────────────────────
ALTER TABLE asset AUTO_INCREMENT = 1;
ALTER TABLE asset_lifecycle_event AUTO_INCREMENT = 1;
ALTER TABLE asset_attachment AUTO_INCREMENT = 1;
ALTER TABLE stock_transaction AUTO_INCREMENT = 1;
ALTER TABLE stock_balance AUTO_INCREMENT = 1;
ALTER TABLE audit_log AUTO_INCREMENT = 1;

-- Show counts AFTER cleanup
SELECT '=== AFTER CLEANUP ===' AS info;
SELECT CONCAT('Assets: ', COUNT(*)) AS info FROM asset;
SELECT CONCAT('Asset lifecycle events: ', COUNT(*)) AS info FROM asset_lifecycle_event;
SELECT CONCAT('Asset attachments: ', COUNT(*)) AS info FROM asset_attachment;
SELECT CONCAT('Stock balances: ', COUNT(*)) AS info FROM stock_balance;
SELECT CONCAT('Stock transactions: ', COUNT(*)) AS info FROM stock_transaction;
SELECT CONCAT('Audit log entries: ', COUNT(*)) AS info FROM audit_log;

-- Verify triggers are recreated
SELECT '=== TRIGGERS ===' AS info;
SELECT TRIGGER_NAME, EVENT_MANIPULATION, ACTION_TIMING
FROM information_schema.TRIGGERS
WHERE TRIGGER_SCHEMA = 'maims' AND EVENT_OBJECT_TABLE = 'audit_log';
