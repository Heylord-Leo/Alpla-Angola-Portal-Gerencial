-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 03: Foreign Key Inspection
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Identify all declared foreign key relationships in the database.
--   AlplaPROD may or may not have explicit FK constraints (the Innux
--   database had zero FKs). This script reveals which tables have
--   formal relationships and which rely on implicit naming conventions.
-- ============================================================================

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: List ALL foreign key relationships in the database
-- Business question: Which tables have formally declared relationships?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    fk.name                     AS ForeignKeyName,
    OBJECT_SCHEMA_NAME(fk.parent_object_id)  AS ChildSchema,
    OBJECT_NAME(fk.parent_object_id)         AS ChildTable,
    cp.name                     AS ChildColumn,
    OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ParentSchema,
    OBJECT_NAME(fk.referenced_object_id)         AS ParentTable,
    rp.name                     AS ParentColumn,
    fk.delete_referential_action_desc AS OnDeleteAction,
    fk.update_referential_action_desc AS OnUpdateAction,
    fk.is_disabled              AS IsDisabled
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
INNER JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
INNER JOIN sys.columns rp ON fkc.referenced_object_id = rp.object_id AND fkc.referenced_column_id = rp.column_id
ORDER BY ChildTable, ForeignKeyName;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: FK count per table — which tables have the most relationships?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    OBJECT_NAME(fk.parent_object_id)  AS ChildTable,
    COUNT(DISTINCT fk.name)           AS ForeignKeyCount
FROM sys.foreign_keys fk
GROUP BY OBJECT_NAME(fk.parent_object_id)
ORDER BY ForeignKeyCount DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: FKs involving candidate logistics tables
-- Business question: How are purchase order, delivery, and receipt tables linked?
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
WHERE OBJECT_NAME(fk.parent_object_id) LIKE '%Bestell%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Order%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Abruf%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Liefer%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Lade%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Warenein%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%EDI%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Journal%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Artikel%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Barcode%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%LKW%'
   OR OBJECT_NAME(fk.parent_object_id) LIKE '%Transport%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%Bestell%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%Order%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%Abruf%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%Liefer%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%Lade%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%Warenein%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%EDI%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%Journal%'
   OR OBJECT_NAME(fk.referenced_object_id) LIKE '%Artikel%'
ORDER BY ChildTable, ParentTable;

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: If NO foreign keys exist, search for implicit relationships
-- by looking for columns with matching ID naming patterns
-- Business question: If FKs are implicit, which columns are likely join keys?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE (
       c.COLUMN_NAME LIKE '%Id'
    OR c.COLUMN_NAME LIKE '%ID'
    OR c.COLUMN_NAME LIKE '%_id'
    OR c.COLUMN_NAME LIKE '%Nr'
    OR c.COLUMN_NAME LIKE '%Nummer'
    OR c.COLUMN_NAME LIKE '%Number'
    OR c.COLUMN_NAME LIKE '%Ref'
    OR c.COLUMN_NAME LIKE '%Reference'
    OR c.COLUMN_NAME LIKE 'FK_%'
  )
  AND c.TABLE_NAME IN (
    SELECT t.TABLE_NAME FROM INFORMATION_SCHEMA.TABLES t
    WHERE t.TABLE_TYPE = 'BASE TABLE'
      AND (
           t.TABLE_NAME LIKE '%Bestell%'
        OR t.TABLE_NAME LIKE '%Order%'
        OR t.TABLE_NAME LIKE '%Abruf%'
        OR t.TABLE_NAME LIKE '%Liefer%'
        OR t.TABLE_NAME LIKE '%Lade%'
        OR t.TABLE_NAME LIKE '%Warenein%'
        OR t.TABLE_NAME LIKE '%EDI%'
        OR t.TABLE_NAME LIKE '%Journal%'
        OR t.TABLE_NAME LIKE '%Artikel%'
        OR t.TABLE_NAME LIKE '%Barcode%'
        OR t.TABLE_NAME LIKE '%LKW%'
        OR t.TABLE_NAME LIKE '%Transport%'
        OR t.TABLE_NAME LIKE '%Kunde%'
        OR t.TABLE_NAME LIKE '%Lieferant%'
      )
  )
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: Check if the database has ANY foreign keys at all
-- Business question: Does AlplaPROD use explicit FK constraints?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    'Total Foreign Keys in Database' AS Metric,
    COUNT(*) AS Count
FROM sys.foreign_keys;

-- ============================================================================
-- END OF SCRIPT 03 — Foreign Key Inspection
-- ============================================================================
