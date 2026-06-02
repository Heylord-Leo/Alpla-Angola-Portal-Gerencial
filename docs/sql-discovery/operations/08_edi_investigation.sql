-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 08: EDI Investigation
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Comprehensive investigation of EDI (Electronic Data Interchange) and
--   internal transfer mechanisms in AlplaPROD. The inter-plant material
--   transfer relies on "internal EDI" between plants. This script searches
--   broadly for any tables, columns, stored procedures, or views related
--   to EDI, transmission, journals, queues, and message processing.
--
--   This is the most critical discovery script because EDI is the glue
--   between the requesting plant (PO) and the supplying plant (Delivery).
-- ============================================================================

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 1: Broad table search for EDI/transmission/journal entities
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: Tables with EDI-related names
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    t.create_date       AS CreatedDate,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%EDI%'
   OR t.name LIKE '%Edi%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: Tables with Journal-related names
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    t.create_date       AS CreatedDate,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Journal%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: Tables with Transfer/Transmission/Import/Export names
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    t.create_date       AS CreatedDate,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Transfer%'
   OR t.name LIKE '%Transmis%'
   OR t.name LIKE '%Uebertrag%'
   OR t.name LIKE '%Import%'
   OR t.name LIKE '%Export%'
   OR t.name LIKE '%Nachricht%'
   OR t.name LIKE '%Message%'
   OR t.name LIKE '%Queue%'
   OR t.name LIKE '%Warteschlange%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: Tables with Beleg (Document) names — may be EDI document tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    t.create_date       AS CreatedDate,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Beleg%'
   OR t.name LIKE '%Dokument%'
   OR t.name LIKE '%Document%'
ORDER BY t.name;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 2: Column-level search for EDI references across ALL tables
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: Columns with EDI in the name (any table)
-- Business question: Which tables have EDI-related columns?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%EDI%'
   OR c.COLUMN_NAME LIKE '%Edi%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- Q6: Columns with Journal in the name (any table)
-- Business question: Which tables reference journal entries?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Journal%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- Q7: Columns with Transmission/Transfer/Übertragung in the name
-- Business question: How is transmission status tracked?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Uebertrag%'
   OR c.COLUMN_NAME LIKE '%Transmis%'
   OR c.COLUMN_NAME LIKE '%Transfer%'
   OR c.COLUMN_NAME LIKE '%Sent%'
   OR c.COLUMN_NAME LIKE '%Received%'
   OR c.COLUMN_NAME LIKE '%Gesendet%'
   OR c.COLUMN_NAME LIKE '%Empfangen%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- Q8: Columns with Import/Export in the name
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Import%'
   OR c.COLUMN_NAME LIKE '%Export%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 3: Show full structure of discovered EDI/Journal tables
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q9: Full column listing for ALL tables matching EDI/Journal/Transfer names
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
    c.IS_NULLABLE, c.COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%EDI%'
       OR t.name LIKE '%Journal%'
       OR t.name LIKE '%Transfer%'
       OR t.name LIKE '%Transmis%'
       OR t.name LIKE '%Uebertrag%'
       OR t.name LIKE '%Import%'
       OR t.name LIKE '%Export%'
       OR t.name LIKE '%Message%'
       OR t.name LIKE '%Queue%'
       OR t.name LIKE '%Beleg%'
)
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 4: Sample data from EDI/Journal tables
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q10: Sample recent records from EDI tables (most recent first)
-- NOTE: Replace [EDITableName] with actual table names from Q1-Q4
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (uncomment and adjust):
--
-- SELECT TOP 50 *
-- FROM [dbo].[EDITableName]
-- ORDER BY 1 DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q11: Sample recent journal records
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT TOP 50 *
-- FROM [dbo].[JournalTableName]
-- ORDER BY 1 DESC;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 5: Trace EDI linkages for known example records
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q12: Find EDI entries linked to Purchase Order 26
-- Business question: PO → EDI transmission link
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (adjust based on discovered structure):
--
-- -- Option A: PO table has a JournalId/EDIId column
-- SELECT *
-- FROM [dbo].[PurchaseOrderTable]
-- WHERE [PrimaryKeyColumn] = 26;
-- -- Then look up that JournalId in the Journal table
--
-- -- Option B: EDI/Journal table has a reference to PO
-- SELECT *
-- FROM [dbo].[EDIJournalTable]
-- WHERE [ReferenceIdColumn] = 26
--    OR [DocumentRefColumn] LIKE '%26%';

-- ────────────────────────────────────────────────────────────────────────────
-- Q13: Find EDI entries linked to Abruf/Call-off 5939
-- Business question: EDI → Loading order/Delivery plan link
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[EDIJournalTable]
-- WHERE [ReferenceIdColumn] = 5939
--    OR [DocumentRefColumn] LIKE '%5939%';

-- ────────────────────────────────────────────────────────────────────────────
-- Q14: Find EDI entries linked to Goods Receipt 887
-- Business question: Delivery note EDI → Goods receipt link
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[EDIJournalTable]
-- WHERE [ReferenceIdColumn] = 887
--    OR [DocumentRefColumn] LIKE '%887%';

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 6: EDI status value discovery
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q15: Distinct transmission status values
-- Business question: What are the possible EDI states?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT DISTINCT [StatusColumn], COUNT(*) AS RecordCount
-- FROM [dbo].[EDITableName]
-- GROUP BY [StatusColumn]
-- ORDER BY RecordCount DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q16: Distinct journal entry types
-- Business question: What kinds of journal entries exist?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT DISTINCT [TypeColumn], COUNT(*) AS RecordCount
-- FROM [dbo].[JournalTableName]
-- GROUP BY [TypeColumn]
-- ORDER BY RecordCount DESC;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 7: Stored Procedures related to EDI/Transfer
-- (Listing names only — NOT executing)
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q17: Stored procedures with EDI/Journal/Transfer names
-- Business question: Is EDI processing done via SPs?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    p.name              AS ProcedureName,
    p.create_date       AS CreatedDate,
    p.modify_date       AS LastModifiedDate
FROM sys.procedures p
INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
WHERE p.name LIKE '%EDI%'
   OR p.name LIKE '%Journal%'
   OR p.name LIKE '%Transfer%'
   OR p.name LIKE '%Transmis%'
   OR p.name LIKE '%Uebertrag%'
   OR p.name LIKE '%Import%'
   OR p.name LIKE '%Export%'
   OR p.name LIKE '%Send%'
   OR p.name LIKE '%Receive%'
   OR p.name LIKE '%Process%'
   OR p.name LIKE '%Message%'
   OR p.name LIKE '%Queue%'
ORDER BY p.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q18: Views related to EDI/Transfer
-- Business question: Are there views that aggregate EDI data?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    v.name              AS ViewName,
    v.create_date       AS CreatedDate
FROM sys.views v
INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
WHERE v.name LIKE '%EDI%'
   OR v.name LIKE '%Journal%'
   OR v.name LIKE '%Transfer%'
   OR v.name LIKE '%Transmis%'
   OR v.name LIKE '%Import%'
   OR v.name LIKE '%Export%'
ORDER BY v.name;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 8: Cross-reference — which tables link TO EDI tables
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q19: Foreign keys pointing TO EDI/Journal tables
-- Business question: Which business tables reference EDI entities?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    fk.name                     AS ForeignKeyName,
    OBJECT_NAME(fk.parent_object_id)         AS ChildTable,
    cp.name                     AS ChildColumn,
    OBJECT_NAME(fk.referenced_object_id)     AS ParentTable,
    rp.name                     AS ParentColumn
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
INNER JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
INNER JOIN sys.columns rp ON fkc.referenced_object_id = rp.object_id AND fkc.referenced_column_id = rp.column_id
WHERE OBJECT_NAME(fk.referenced_object_id) LIKE '%EDI%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%Journal%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%Transfer%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%EDI%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Journal%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Transfer%'
ORDER BY ChildTable, ParentTable;

-- ============================================================================
-- END OF SCRIPT 08 — EDI Investigation
-- ============================================================================
