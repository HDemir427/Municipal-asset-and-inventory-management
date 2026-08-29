-- ============================================================================
-- MAIMS — FULL DATABASE RESET (Hard Reset)
--
-- This script performs a COMPLETE hard reset of the MAIMS database:
--   • Deletes ALL business data (assets, stock, items, warehouses, locations)
--   • Deletes ALL users EXCEPT the bootstrap 'admin' user
--   • Deletes ALL audit_log entries (drops triggers temporarily)
--   • Resets ALL AUTO_INCREMENT counters to 1
--   • Recreates audit_log triggers (restores immutability)
--
-- KEEPS (does NOT delete):
--   ✓ Departments (17 realistic municipal departments)
--   ✓ Roles (7 built-in roles + permission sets)
--   ✓ Asset categories (8 categories)
--   ✓ Bootstrap 'admin' user (so you can still log in)
--
-- After running this script:
--   - All IDs (asset_id, item_id, stock_transaction_id, ...) start from 1
--   - Audit log is empty
--   - Only the admin user remains — log in with admin / Admin@123
--   - You can now insert fresh, realistic data
--
-- ────────────────────────────────────────────────────────────────────────────
-- USAGE:
-- ────────────────────────────────────────────────────────────────────────────
--   mysql -u root -p maims < db/50_full_reset.sql
--
--   (Run as ROOT, not maims_app — requires DDL for trigger drop/create + ALTER)
--
-- ────────────────────────────────────────────────────────────────────────────
-- IMPORTANT:
-- ────────────────────────────────────────────────────────────────────────────
--   • STOP the MAIMS WinForms application before running this script.
--     Otherwise EF Core's connection pool may hold locks on the tables.
--   • This is IRREVERSIBLE — all business data is permanently lost.
--   • Back up the database first if unsure:
--       mysqldump -u root -p maims > maims_backup_$(date +%Y%m%d).sql
-- ============================================================================

USE maims;

-- ════════════════════════════════════════════════════════════════════════════
-- Show counts BEFORE reset
-- ════════════════════════════════════════════════════════════════════════════
SELECT '═══ BEFORE FULL RESET ═══' AS info;
SELECT CONCAT('Departments:           ', COUNT(*)) AS info FROM department;
SELECT CONCAT('Roles:                 ', COUNT(*)) AS info FROM role;
SELECT CONCAT('Asset categories:      ', COUNT(*)) AS info FROM asset_category;
SELECT CONCAT('Users (total):         ', COUNT(*)) AS info FROM user;
SELECT CONCAT('  - admin user:        ', COUNT(*)) AS info FROM user WHERE username = 'admin';
SELECT CONCAT('  - non-admin users:   ', COUNT(*)) AS info FROM user WHERE username != 'admin';
SELECT CONCAT('Warehouses:            ', COUNT(*)) AS info FROM warehouse;
SELECT CONCAT('Locations:             ', COUNT(*)) AS info FROM location;
SELECT CONCAT('Items (catalog):       ', COUNT(*)) AS info FROM item;
SELECT CONCAT('Assets:                ', COUNT(*)) AS info FROM asset;
SELECT CONCAT('Asset lifecycle events:', COUNT(*)) AS info FROM asset_lifecycle_event;
SELECT CONCAT('Asset attachments:     ', COUNT(*)) AS info FROM asset_attachment;
SELECT CONCAT('Stock balances:        ', COUNT(*)) AS info FROM stock_balance;
SELECT CONCAT('Stock transactions:    ', COUNT(*)) AS info FROM stock_transaction;
SELECT CONCAT('Audit log entries:     ', COUNT(*)) AS info FROM audit_log;

-- ════════════════════════════════════════════════════════════════════════════
-- Step 1: Drop the audit_log triggers temporarily
-- (audit_log has BEFORE DELETE / BEFORE UPDATE triggers that block all writes)
-- ════════════════════════════════════════════════════════════════════════════
DROP TRIGGER IF EXISTS trg_audit_log_block_delete;
DROP TRIGGER IF EXISTS trg_audit_log_block_update;

-- ════════════════════════════════════════════════════════════════════════════
-- Step 2: HARD DELETE all business data (child tables first, then parents)
-- ════════════════════════════════════════════════════════════════════════════

-- Asset-related (child → parent)
DELETE FROM asset_attachment;
DELETE FROM asset_lifecycle_event;
DELETE FROM asset;

-- Inventory-related (child → parent)
DELETE FROM stock_transaction;
DELETE FROM stock_balance;
DELETE FROM item;

-- Reference data (locations & warehouses — optional, but full reset = yes)
DELETE FROM warehouse;
DELETE FROM location;

-- Audit log (ALL entries — clean slate)
DELETE FROM audit_log;

-- Users (delete all EXCEPT the bootstrap 'admin' user)
DELETE FROM user WHERE username != 'admin';

-- ════════════════════════════════════════════════════════════════════════════
-- Step 3: Reset ALL AUTO_INCREMENT counters to 1
-- (This is the KEY step — without it, new IDs continue from the old max+1)
-- ════════════════════════════════════════════════════════════════════════════
ALTER TABLE asset                AUTO_INCREMENT = 1;
ALTER TABLE asset_lifecycle_event AUTO_INCREMENT = 1;
ALTER TABLE asset_attachment     AUTO_INCREMENT = 1;
ALTER TABLE stock_transaction    AUTO_INCREMENT = 1;
ALTER TABLE stock_balance        AUTO_INCREMENT = 1;
ALTER TABLE item                 AUTO_INCREMENT = 1;
ALTER TABLE warehouse            AUTO_INCREMENT = 1;
ALTER TABLE location             AUTO_INCREMENT = 1;
ALTER TABLE audit_log            AUTO_INCREMENT = 1;
ALTER TABLE user                 AUTO_INCREMENT = 1;

-- ════════════════════════════════════════════════════════════════════════════
-- Step 4: Recreate the audit_log triggers (restore immutability protection)
-- ════════════════════════════════════════════════════════════════════════════
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

-- ════════════════════════════════════════════════════════════════════════════
-- Step 5: Update the admin user's LastLoginAt to NULL (fresh state)
-- ════════════════════════════════════════════════════════════════════════════
UPDATE user SET last_login_at = NULL WHERE username = 'admin';

-- ════════════════════════════════════════════════════════════════════════════
-- Show counts AFTER reset
-- ════════════════════════════════════════════════════════════════════════════
SELECT '═══ AFTER FULL RESET ═══' AS info;
SELECT CONCAT('Departments:           ', COUNT(*)) AS info FROM department;
SELECT CONCAT('Roles:                 ', COUNT(*)) AS info FROM role;
SELECT CONCAT('Asset categories:      ', COUNT(*)) AS info FROM asset_category;
SELECT CONCAT('Users (total):         ', COUNT(*)) AS info FROM user;
SELECT CONCAT('  - admin user:        ', COUNT(*)) AS info FROM user WHERE username = 'admin';
SELECT CONCAT('Warehouses:            ', COUNT(*)) AS info FROM warehouse;
SELECT CONCAT('Locations:             ', COUNT(*)) AS info FROM location;
SELECT CONCAT('Items (catalog):       ', COUNT(*)) AS info FROM item;
SELECT CONCAT('Assets:                ', COUNT(*)) AS info FROM asset;
SELECT CONCAT('Asset lifecycle events:', COUNT(*)) AS info FROM asset_lifecycle_event;
SELECT CONCAT('Asset attachments:     ', COUNT(*)) AS info FROM asset_attachment;
SELECT CONCAT('Stock balances:        ', COUNT(*)) AS info FROM stock_balance;
SELECT CONCAT('Stock transactions:    ', COUNT(*)) AS info FROM stock_transaction;
SELECT CONCAT('Audit log entries:     ', COUNT(*)) AS info FROM audit_log;

-- Verify triggers are recreated
SELECT '═══ AUDIT LOG TRIGGERS ═══' AS info;
SELECT TRIGGER_NAME, EVENT_MANIPULATION, ACTION_TIMING
FROM information_schema.TRIGGERS
WHERE TRIGGER_SCHEMA = 'maims' AND EVENT_OBJECT_TABLE = 'audit_log';

-- ════════════════════════════════════════════════════════════════════════════
-- DONE. You can now start the MAIMS app and insert fresh, realistic data.
-- Login: admin / Admin@123
-- ════════════════════════════════════════════════════════════════════════════
