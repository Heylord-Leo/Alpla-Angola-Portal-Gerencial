-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 05: Purchase Order Trace
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Trace a known purchase order through the AlplaPROD database.
--   Uses example values from the screenshots as investigation references.
--
--   Screenshot references (from AlplaPURCHASE):
--   - Purchase Order ID: 26
--   - Purchase Order Item/Position ID: 94
--   - Article: 2295 / MM JADE CZ-328
--
--   NOTE: These are starting points. Adjust the IDs after running
--   Script 01 to discover the actual table/column names.
-- ============================================================================

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 1: Discover purchase order tables
-- Run these first to identify the actual table names
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: Find all tables with "Bestell" (purchase order) in the name
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Bestell%'
   OR t.name LIKE '%PurchaseOrder%'
   OR t.name LIKE '%Order%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: Show ALL columns for each candidate purchase order table
-- (Adjust table names after Q1 results are known)
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
    c.IS_NULLABLE, c.COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%Bestell%'
       OR t.name LIKE '%PurchaseOrder%'
)
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 2: Sample data from candidate tables
-- Adjust table/column names after Step 1 results are known
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: Sample purchase order header records (most recent first)
-- NOTE: Replace [PurchaseOrderTableName] with the actual table name from Q1
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (uncomment and adjust after discovering table names):
--
-- SELECT TOP 50 *
-- FROM [dbo].[PurchaseOrderTableName]
-- ORDER BY 1 DESC;  -- Assumes first column is PK/ID

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: Sample purchase order line item records
-- NOTE: Replace [PurchaseOrderItemTableName] with the actual table name
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (uncomment and adjust):
--
-- SELECT TOP 50 *
-- FROM [dbo].[PurchaseOrderItemTableName]
-- ORDER BY 1 DESC;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 3: Trace specific example PO (once tables are confirmed)
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: Find PO with ID = 26 (screenshot reference)
-- NOTE: Replace table/column names after discovery
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[PurchaseOrderTable]
-- WHERE [PrimaryKeyColumn] = 26
--    OR [OrderNumberColumn] = '26';

-- ────────────────────────────────────────────────────────────────────────────
-- Q6: Find PO items for PO ID = 26 (screenshot reference: item 94)
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[PurchaseOrderItemTable]
-- WHERE [OrderIdColumn] = 26
-- ORDER BY [PositionColumn];

-- ────────────────────────────────────────────────────────────────────────────
-- Q7: Find records referencing article 2295 / "MM JADE CZ-328"
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[PurchaseOrderItemTable]
-- WHERE [ArticleIdColumn] = 2295
--    OR [ArticleDescColumn] LIKE '%MM JADE%'
--    OR [ArticleDescColumn] LIKE '%CZ-328%';

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 4: Identify PO status and transmission fields
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q8: Find distinct status values on purchase order tables
-- Business question: What are the possible PO statuses?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT DISTINCT [StatusColumn], COUNT(*) AS RecordCount
-- FROM [dbo].[PurchaseOrderTable]
-- GROUP BY [StatusColumn]
-- ORDER BY RecordCount DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q9: Find distinct transmission/transfer status values
-- Business question: How is the EDI transmission status tracked?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT DISTINCT [TransmissionStatusColumn], COUNT(*) AS RecordCount
-- FROM [dbo].[PurchaseOrderTable]
-- GROUP BY [TransmissionStatusColumn]
-- ORDER BY RecordCount DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q10: Find journal references linked to PO 26
-- Business question: Which EDI journal entry corresponds to this PO?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[PurchaseOrderTable]
-- WHERE [PrimaryKeyColumn] = 26;
-- -- Look for JournalId, JournalNr, or similar columns in the result

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 5: Generic broad search for PO-related data
-- These queries work even before table names are confirmed
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q11: Search ALL tables for a column containing the value 26
-- (to find which tables reference PO 26)
-- WARNING: This is a metadata-only search. For data search,
-- you would need to query each table individually.
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.DATA_TYPE IN ('int', 'bigint', 'smallint')
  AND (
       c.COLUMN_NAME LIKE '%Bestell%'
    OR c.COLUMN_NAME LIKE '%Order%'
    OR c.COLUMN_NAME LIKE '%PO%'
  )
ORDER BY c.TABLE_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q12: Find tables with both order-related AND article-related columns
-- Business question: Which tables link purchase orders to articles?
-- ────────────────────────────────────────────────────────────────────────────
SELECT DISTINCT c1.TABLE_NAME
FROM INFORMATION_SCHEMA.COLUMNS c1
WHERE (c1.COLUMN_NAME LIKE '%Bestell%' OR c1.COLUMN_NAME LIKE '%Order%')
  AND EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS c2
    WHERE c2.TABLE_NAME = c1.TABLE_NAME
      AND (c2.COLUMN_NAME LIKE '%Artikel%' OR c2.COLUMN_NAME LIKE '%Article%' OR c2.COLUMN_NAME LIKE '%Item%')
  )
ORDER BY c1.TABLE_NAME;

-- ============================================================================
-- END OF SCRIPT 05 — Purchase Order Trace
-- ============================================================================
