-- ============================================================================
-- ATM Inventory System — MySQL catch-up migration
-- ============================================================================
-- WHY THIS EXISTS
-- The app's own schema-evolution mechanism (in Program.cs, "lightweight
-- migration" blocks) is SQLite-only — it's gated behind `if (isSqlite)` and
-- uses SQLite-specific DDL (PRAGMA table_info, etc.). On MySQL, only
-- `EnsureCreated()` runs at startup, which creates any TABLE that's entirely
-- missing (using the current C# model, with correct MySQL types) but never
-- ALTERs a table that already exists. So a MySQL database created at some
-- earlier point in the project's life is missing every COLUMN added to an
-- already-existing table since then.
--
-- This script adds exactly those columns. It does NOT create tables — if an
-- entire table is missing (e.g. WithdrawBatches, PartUnits, SavedAddresses),
-- just start the API once against this database and EnsureCreated() will
-- create it correctly on its own; re-run this script afterward for safety.
--
-- SAFE TO RUN MULTIPLE TIMES: every ADD COLUMN is guarded by an
-- INFORMATION_SCHEMA check first, so re-running is a no-op for anything
-- already present.
--
-- NOT INCLUDED (deliberately): destructive/legacy operations from the
-- SQLite migration history (dropping Parts.StockQuantity, renaming
-- PartStocks.DefectiveQty→BadQty, rebuilding the old single-part-request
-- Tickets table, the WithdrawBatch data backfill from old Ticket rows).
-- Those only apply to a database from a MUCH earlier point in this
-- project's history than any MySQL instance is likely to be on. If your
-- database really is that old, say so and this script gets extended —
-- don't run guessed DROP/RENAME statements against real data.
--
-- HOW TO RUN
--   mysql -u <user> -p <database_name> < mysql-migration-catchup.sql
-- or paste into MySQL Workbench / any client against the target schema.
-- Take a backup first regardless (mysqldump) — standard practice before
-- any schema change on a database you care about.
-- ============================================================================

DELIMITER $$

DROP PROCEDURE IF EXISTS _AddColumnIfMissing $$
CREATE PROCEDURE _AddColumnIfMissing(
    IN p_table VARCHAR(128),
    IN p_column VARCHAR(128),
    IN p_definition VARCHAR(255)
)
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table AND COLUMN_NAME = p_column
    ) THEN
        SET @ddl = CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `', p_column, '` ', p_definition, ';');
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
        SELECT CONCAT('✅ added ', p_table, '.', p_column) AS migration_log;
    ELSE
        SELECT CONCAT('— skipped ', p_table, '.', p_column, ' (already exists)') AS migration_log;
    END IF;
END $$

DELIMITER ;

-- ── Parts ────────────────────────────────────────────────────────────────
CALL _AddColumnIfMissing('Parts', 'MainUnit',   'LONGTEXT NULL');
CALL _AddColumnIfMissing('Parts', 'Remark',     'LONGTEXT NULL');
CALL _AddColumnIfMissing('Parts', 'ImagePath',  'LONGTEXT NULL');
CALL _AddColumnIfMissing('Parts', 'Zone',       'LONGTEXT NULL');
CALL _AddColumnIfMissing('Parts', 'DeviceType', 'LONGTEXT NULL');
CALL _AddColumnIfMissing('Parts', 'AddedBy',    'LONGTEXT NULL');
CALL _AddColumnIfMissing('Parts', 'Lot',        'LONGTEXT NULL');
CALL _AddColumnIfMissing('Parts', 'Project',    'LONGTEXT NULL');
CALL _AddColumnIfMissing('Parts', 'AddedDate',  'LONGTEXT NULL');
CALL _AddColumnIfMissing('Parts', 'RowVersion', "VARCHAR(255) NOT NULL DEFAULT ''");

-- ── PartStocks ───────────────────────────────────────────────────────────
CALL _AddColumnIfMissing('PartStocks', 'UpdatedAt',  "DATETIME(6) NOT NULL DEFAULT '1970-01-01 00:00:00'");
CALL _AddColumnIfMissing('PartStocks', 'RowVersion', "VARCHAR(255) NOT NULL DEFAULT ''");
CALL _AddColumnIfMissing('PartStocks', 'RepairQty',  'INT NOT NULL DEFAULT 0');
-- NOTE: if this MySQL DB still has a `DefectiveQty` column instead of `BadQty`,
-- that's the one legacy rename this script does NOT auto-apply (data-bearing
-- rename) — tell me if that's the case and I'll add it deliberately.

-- ── TicketPartLines ──────────────────────────────────────────────────────
CALL _AddColumnIfMissing('TicketPartLines', 'Condition',       'LONGTEXT NULL');
CALL _AddColumnIfMissing('TicketPartLines', 'OriginalPartNo',  'LONGTEXT NULL');
CALL _AddColumnIfMissing('TicketPartLines', 'ConfirmedQty',    'INT NOT NULL DEFAULT 0');
CALL _AddColumnIfMissing('TicketPartLines', 'WithdrawBatchId', 'INT NULL');

-- ── TicketAttachments ────────────────────────────────────────────────────
CALL _AddColumnIfMissing('TicketAttachments', 'WithdrawBatchId', 'INT NULL');

-- ── Tickets ──────────────────────────────────────────────────────────────
CALL _AddColumnIfMissing('Tickets', 'WithdrawDescription', 'LONGTEXT NULL');
CALL _AddColumnIfMissing('Tickets', 'WithdrawSlipNo',      'LONGTEXT NULL');
CALL _AddColumnIfMissing('Tickets', 'WithdrawDate',        'DATETIME(6) NULL');
CALL _AddColumnIfMissing('Tickets', 'EmployeeCode',        'LONGTEXT NULL');
CALL _AddColumnIfMissing('Tickets', 'UsageStatus',         'LONGTEXT NULL');
CALL _AddColumnIfMissing('Tickets', 'TechSupportName',     'LONGTEXT NULL');
CALL _AddColumnIfMissing('Tickets', 'EmailSentAt',         'DATETIME(6) NULL');
CALL _AddColumnIfMissing('Tickets', 'ReturnEmailSentAt',   'DATETIME(6) NULL');

-- ── WithdrawBatches (DHL form fields + batch-scoped return leg) ─────────
CALL _AddColumnIfMissing('WithdrawBatches', 'NeededByDate',        'DATETIME(6) NULL');
CALL _AddColumnIfMissing('WithdrawBatches', 'FeId',                'LONGTEXT NULL');
CALL _AddColumnIfMissing('WithdrawBatches', 'Sla',                 'LONGTEXT NULL');
CALL _AddColumnIfMissing('WithdrawBatches', 'AtmCode',             'LONGTEXT NULL');
CALL _AddColumnIfMissing('WithdrawBatches', 'WaitingSinceAt',      'DATETIME(6) NULL');
CALL _AddColumnIfMissing('WithdrawBatches', 'ReturnStatus',        'LONGTEXT NULL');
CALL _AddColumnIfMissing('WithdrawBatches', 'ReturnRejectReason',  'LONGTEXT NULL');
CALL _AddColumnIfMissing('WithdrawBatches', 'ReturnApproverName',  'LONGTEXT NULL');
CALL _AddColumnIfMissing('WithdrawBatches', 'ReturnApprovedAt',    'DATETIME(6) NULL');
CALL _AddColumnIfMissing('WithdrawBatches', 'ReturnAddress',       'LONGTEXT NULL');
CALL _AddColumnIfMissing('WithdrawBatches', 'ReturnEmailSentAt',   'DATETIME(6) NULL');

-- ── DailyReportImportRows ────────────────────────────────────────────────
CALL _AddColumnIfMissing('DailyReportImportRows', 'CaseNo',          'LONGTEXT NULL');
CALL _AddColumnIfMissing('DailyReportImportRows', 'WithdrawBatchId', 'INT NULL');

-- ── StockMovements ───────────────────────────────────────────────────────
CALL _AddColumnIfMissing('StockMovements', 'PartId',     'INT NOT NULL DEFAULT 0');
CALL _AddColumnIfMissing('StockMovements', 'PartUnitId', 'INT NULL');

-- ── Transaction tables — PartId (FK to Part) ────────────────────────────
CALL _AddColumnIfMissing('GoodsReceiptLines',        'PartId', 'INT NOT NULL DEFAULT 0');
CALL _AddColumnIfMissing('ReturnRequests',           'PartId', 'INT NOT NULL DEFAULT 0');
CALL _AddColumnIfMissing('StockTransfers',           'PartId', 'INT NOT NULL DEFAULT 0');
CALL _AddColumnIfMissing('StockCountLines',          'PartId', 'INT NOT NULL DEFAULT 0');
CALL _AddColumnIfMissing('DisposalRequests',         'PartId', 'INT NOT NULL DEFAULT 0');
CALL _AddColumnIfMissing('AtmModelParts',            'PartId', 'INT NOT NULL DEFAULT 0');
CALL _AddColumnIfMissing('EquivalentGroupMembers',   'PartId', 'INT NOT NULL DEFAULT 0');

-- ── Cleanup ──────────────────────────────────────────────────────────────
DROP PROCEDURE IF EXISTS _AddColumnIfMissing;

SELECT '✅ Migration script complete. Restart the API — EnsureCreated() will
create any table that is still entirely missing (WithdrawBatches, PartUnits,
DailyReportImportBatches/Rows, TicketAttachments, PartImages, SavedAddresses,
etc.) using the current model, with correct MySQL types.' AS final_note;
