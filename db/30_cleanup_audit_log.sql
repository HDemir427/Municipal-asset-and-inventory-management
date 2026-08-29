-- ============================================================================
-- MAIMS — Audit log cleanup script (manual)
--
-- Run this script MANUALLY with MySQL root if you need to purge old
-- audit_log entries with invalid entity_id (≤ 0) that were created by a
-- previous bug in the audit interceptor.
--
-- Usage:
--   mysql -u root -p maims < db/30_cleanup_audit_log.sql
--
-- What this script does:
--   1. Drops the BEFORE DELETE trigger on audit_log
--   2. Deletes entries with entity_id <= 0 (or NULL)
--   3. Optionally deletes ALL entries (uncomment the line below)
--   4. Recreates the BEFORE DELETE trigger (restores immutability)
--
-- IMPORTANT: This script MUST be run with root privileges because the
-- maims_app user does not have DROP / CREATE TRIGGER privileges.
-- ============================================================================

USE maims;

-- Show count of invalid entries BEFORE purge
SELECT 'BEFORE: invalid entries (entity_id <= 0 or NULL):' AS info;
SELECT COUNT(*) AS invalid_count FROM audit_log WHERE entity_id <= 0 OR entity_id IS NULL;
SELECT 'BEFORE: total entries:' AS info;
SELECT COUNT(*) AS total_count FROM audit_log;

-- Step 1: Drop the BEFORE DELETE trigger
DROP TRIGGER IF EXISTS trg_audit_log_block_delete;

-- Step 2: Delete invalid entries (entity_id <= 0 or NULL)
DELETE FROM audit_log WHERE entity_id <= 0 OR entity_id IS NULL;

-- To delete ALL entries (use with caution!), uncomment the next line:
-- DELETE FROM audit_log;

-- Step 3: Recreate the BEFORE DELETE trigger (restore immutability)
DELIMITER $$

CREATE TRIGGER trg_audit_log_block_delete
BEFORE DELETE ON audit_log
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'audit_log is append-only. DELETE is not permitted.';
END$$

DELIMITER ;

-- Show count AFTER purge
SELECT 'AFTER: total entries:' AS info;
SELECT COUNT(*) AS total_count FROM audit_log;

-- Verify trigger exists
SELECT 'Trigger recreated:' AS info;
SELECT TRIGGER_NAME, EVENT_MANIPULATION, ACTION_TIMING
FROM information_schema.TRIGGERS
WHERE TRIGGER_SCHEMA = 'maims' AND EVENT_OBJECT_TABLE = 'audit_log';
