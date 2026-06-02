-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 04: Index Inspection
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Inspect indexes on candidate tables to understand:
--   1. Primary keys (clustered indexes)
--   2. Unique constraints (business keys)
--   3. Non-clustered indexes (query optimization clues about access patterns)
--   4. Index columns reveal which fields are frequently used for lookups
-- ============================================================================

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: Primary keys for ALL tables
-- Business question: What is the primary key structure of each table?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    t.name              AS TableName,
    i.name              AS IndexName,
    i.type_desc         AS IndexType,
    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.is_primary_key = 1
  AND t.is_ms_shipped = 0
GROUP BY s.name, t.name, i.name, i.type_desc
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: Unique indexes / constraints (business keys)
-- Business question: Which columns enforce uniqueness?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    t.name              AS TableName,
    i.name              AS IndexName,
    i.type_desc         AS IndexType,
    i.is_unique_constraint AS IsUniqueConstraint,
    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.is_unique = 1
  AND i.is_primary_key = 0
  AND t.is_ms_shipped = 0
GROUP BY s.name, t.name, i.name, i.type_desc, i.is_unique_constraint
ORDER BY t.name, i.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: ALL indexes on candidate logistics tables
-- Business question: What access patterns does the app use on these tables?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    i.name              AS IndexName,
    i.type_desc         AS IndexType,
    i.is_primary_key    AS IsPK,
    i.is_unique         AS IsUnique,
    STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE t.is_ms_shipped = 0
  AND (
       t.name LIKE '%Bestell%'
    OR t.name LIKE '%Order%'
    OR t.name LIKE '%Abruf%'
    OR t.name LIKE '%Liefer%'
    OR t.name LIKE '%Lade%'
    OR t.name LIKE '%Warenein%'
    OR t.name LIKE '%EDI%'
    OR t.name LIKE '%Journal%'
    OR t.name LIKE '%Artikel%'
    OR t.name LIKE '%Barcode%'
    OR t.name LIKE '%LKW%'
    OR t.name LIKE '%Transport%'
  )
GROUP BY t.name, i.name, i.type_desc, i.is_primary_key, i.is_unique
ORDER BY t.name, i.is_primary_key DESC, i.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: Tables WITHOUT any primary key (potential issues)
-- Business question: Are there tables with no PK? These may be log/staging tables.
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    t.name              AS TableName,
    p.rows              AS ApproxRowCount
FROM sys.tables t
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
  AND NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id = t.object_id AND i.is_primary_key = 1
  )
ORDER BY p.rows DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: Identity columns (auto-increment PKs)
-- Business question: Which tables use auto-incrementing IDs?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    t.name              AS TableName,
    c.name              AS ColumnName,
    c.is_identity       AS IsIdentity,
    IDENT_SEED(s.name + '.' + t.name) AS IdentitySeed,
    IDENT_INCR(s.name + '.' + t.name) AS IdentityIncrement,
    IDENT_CURRENT(s.name + '.' + t.name) AS CurrentIdentityValue
FROM sys.columns c
INNER JOIN sys.tables t ON c.object_id = t.object_id
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE c.is_identity = 1
  AND t.is_ms_shipped = 0
ORDER BY t.name;

-- ============================================================================
-- END OF SCRIPT 04 — Index Inspection
-- ============================================================================
