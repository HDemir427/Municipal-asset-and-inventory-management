-- ============================================================================
-- MAIMS — Audit log user-level privilege restriction
--
-- RUN THIS AFTER the MAIMS app has created all tables (first launch) OR
-- after `dotnet ef database update` (migration path).
--
-- This script restricts the maims_app MySQL user so it can only INSERT into
-- audit_log (no UPDATE, no DELETE). This is the THIRD layer of immutability
-- defence, complementing:
--   (a) Application code — no Update/Remove code path on AuditLog entity
--   (b) BEFORE UPDATE/DELETE triggers (10_audit_log_immutable.sql)
--
-- How it works:
--   1. Drops ALL DB-level grants from maims_app (the broad grant from 00_init.sql)
--   2. Re-creates table-level grants per table (except audit_log) via a
--      stored procedure that iterates over all tables in the maims schema
--   3. Grants only SELECT + INSERT on audit_log (immutability)
--   4. Grants SELECT + INSERT on __EFMigrationsHistory (EF runtime needs it)
--
-- Why a stored procedure? MySQL cannot REVOKE table-level privileges on a
-- table that does not exist yet, and we need to iterate over all tables
-- dynamically. The procedure builds and executes GRANT statements per table.
-- ============================================================================

USE maims;

-- ---------------------------------------------------------------------------
-- Step 0: Verify audit_log table exists. If count = 0, run the MAIMS app
-- first (EnsureCreatedAsync) or `dotnet ef database update` — this script
-- needs the tables to exist.
-- ---------------------------------------------------------------------------
SELECT COUNT(*) AS audit_log_exists
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = 'maims' AND TABLE_NAME = 'audit_log';

-- ---------------------------------------------------------------------------
-- Step 1: Drop ALL DB-level grants from maims_app for both host patterns.
-- This removes the broad GRANT issued in 00_init.sql.
-- ---------------------------------------------------------------------------
REVOKE ALL PRIVILEGES ON maims.* FROM 'maims_app'@'%';
REVOKE ALL PRIVILEGES ON maims.* FROM 'maims_app'@'localhost';

-- Note: REVOKE returns Error 1147 if no matching grant exists for a host
-- pattern. This is harmless — the stored procedure below handles grants
-- idempotently (GRANT is a no-op if the privilege already exists).

-- ---------------------------------------------------------------------------
-- Step 2: Re-grant table-level privileges via a stored procedure that
-- iterates over all tables in the maims schema. audit_log gets only
-- SELECT + INSERT; everything else gets the full CRUD set.
-- ---------------------------------------------------------------------------
DROP PROCEDURE IF EXISTS maims_regrant_tables;

DELIMITER $$
CREATE PROCEDURE maims_regrant_tables()
BEGIN
    DECLARE done INT DEFAULT FALSE;
    DECLARE tbl_name VARCHAR(64);
    DECLARE cur CURSOR FOR
        SELECT TABLE_NAME
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = 'maims'
          AND TABLE_NAME NOT IN ('audit_log', '__EFMigrationsHistory');
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = TRUE;

    -- Iterate over all tables except audit_log and __EFMigrationsHistory
    OPEN cur;
    read_loop: LOOP
        FETCH cur INTO tbl_name;
        IF done THEN LEAVE read_loop; END IF;

        -- Grant for '%' host (network connections)
        SET @sql1 = CONCAT(
            'GRANT SELECT, INSERT, UPDATE, DELETE ON maims.`', tbl_name, '` TO ''maims_app''@''%''');
        PREPARE stmt1 FROM @sql1;
        EXECUTE stmt1;
        DEALLOCATE PREPARE stmt1;

        -- Grant for 'localhost' host (local connections — important when app runs on the same machine)
        SET @sql2 = CONCAT(
            'GRANT SELECT, INSERT, UPDATE, DELETE ON maims.`', tbl_name, '` TO ''maims_app''@''localhost''');
        PREPARE stmt2 FROM @sql2;
        EXECUTE stmt2;
        DEALLOCATE PREPARE stmt2;
    END LOOP;
    CLOSE cur;
END$$
DELIMITER ;

CALL maims_regrant_tables();
DROP PROCEDURE maims_regrant_tables;

-- ---------------------------------------------------------------------------
-- Step 3: Grant SELECT + INSERT on audit_log only (no UPDATE, no DELETE).
-- This is the immutability restriction at the MySQL user level.
-- ---------------------------------------------------------------------------
GRANT SELECT, INSERT ON maims.audit_log TO 'maims_app'@'%';
GRANT SELECT, INSERT ON maims.audit_log TO 'maims_app'@'localhost';

-- ---------------------------------------------------------------------------
-- Step 4: Grant SELECT + INSERT on __EFMigrationsHistory so EF Core can
-- track applied migrations during future schema updates. (Note: actual
-- schema migrations should use a separate migration user — this is just
-- for the EF runtime to query the history table.)
-- ---------------------------------------------------------------------------
GRANT SELECT, INSERT ON maims.__EFMigrationsHistory TO 'maims_app'@'%';
GRANT SELECT, INSERT ON maims.__EFMigrationsHistory TO 'maims_app'@'localhost';

FLUSH PRIVILEGES;

-- ---------------------------------------------------------------------------
-- Verification: list all current grants for maims_app
-- ---------------------------------------------------------------------------
SHOW GRANTS FOR 'maims_app'@'%';
SHOW GRANTS FOR 'maims_app'@'localhost';
