-- ============================================================================
-- MAIMS — Audit log immutability enforcement
-- Apply AFTER the schema has been created by EF Core migrations.
-- Adds a BEFORE UPDATE/DELETE trigger that blocks any mutation of audit_log
-- rows. This is defence in depth: even if a future bug grants maims_app
-- UPDATE/DELETE on audit_log, the trigger prevents the mutation.
-- ============================================================================

USE maims;

DELIMITER $$

DROP TRIGGER IF EXISTS trg_audit_log_block_update$$
CREATE TRIGGER trg_audit_log_block_update
BEFORE UPDATE ON audit_log
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'audit_log is append-only. UPDATE is not permitted.';
END$$

DROP TRIGGER IF EXISTS trg_audit_log_block_delete$$
CREATE TRIGGER trg_audit_log_block_delete
BEFORE DELETE ON audit_log
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'audit_log is append-only. DELETE is not permitted.';
END$$

DELIMITER ;
