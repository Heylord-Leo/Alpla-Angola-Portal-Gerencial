-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 01: Schema Inspection
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Inspect the overall database structure of an AlplaPROD database.
--   Run this script first on each of the 3 databases to understand the
--   table landscape before targeted investigation.
--
-- TARGET DATABASES:
--   - AOVIA1VMS006 → AlplaPROD Viana 1
--   - AOVIA1VMS006 → AlplaPROD Viana 3
--   - AOVIA2VMS006 → AlplaPROD Viana 2
--
-- CREDENTIALS:
--   Use the designated read-only SQL login. Do NOT hardcode credentials.
--   Connection should be established via SSMS or a secure connection tool.
-- ============================================================================

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: Database identity — confirm which server and database we are connected to
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    @@SERVERNAME        AS ServerName,
    DB_NAME()           AS DatabaseName,
    @@VERSION           AS SQLServerVersion,
    GETDATE()           AS QueryExecutedAt;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: List ALL schemas in the database
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    s.schema_id         AS SchemaId
FROM sys.schemas s
WHERE s.principal_id = 1  -- dbo-owned schemas
   OR s.schema_id > 4     -- exclude system schemas (sys, INFORMATION_SCHEMA, guest, db_*)
ORDER BY s.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: List ALL user tables with approximate row counts and creation dates
-- Business question: What tables exist and how large are they?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    t.create_date       AS CreatedDate,
    t.modify_date       AS LastModifiedDate
FROM sys.tables t
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
ORDER BY s.name, t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: Tables sorted by row count (largest first)
-- Business question: Which tables hold the most data?
-- ────────────────────────────────────────────────────────────────────────────
SELECT TOP 50
    s.name              AS SchemaName,
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    t.create_date       AS CreatedDate
FROM sys.tables t
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
ORDER BY p.rows DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: Tables with names matching logistics/purchase/delivery/receipt keywords
-- Business question: Which tables are likely related to inter-plant transfers?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    t.name              AS TableName,
    p.rows              AS ApproxRowCount
FROM sys.tables t
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
  AND (
       t.name LIKE '%Order%'
    OR t.name LIKE '%Bestell%'
    OR t.name LIKE '%Purchase%'
    OR t.name LIKE '%Delivery%'
    OR t.name LIKE '%Liefer%'
    OR t.name LIKE '%Abruf%'
    OR t.name LIKE '%Receipt%'
    OR t.name LIKE '%Warenein%'
    OR t.name LIKE '%Loading%'
    OR t.name LIKE '%Lade%'
    OR t.name LIKE '%Truck%'
    OR t.name LIKE '%LKW%'
    OR t.name LIKE '%Shipment%'
    OR t.name LIKE '%Versand%'
    OR t.name LIKE '%Stock%'
    OR t.name LIKE '%Lager%'
    OR t.name LIKE '%EDI%'
    OR t.name LIKE '%Journal%'
    OR t.name LIKE '%Transfer%'
    OR t.name LIKE '%Transmis%'
    OR t.name LIKE '%Article%'
    OR t.name LIKE '%Artikel%'
    OR t.name LIKE '%Item%'
    OR t.name LIKE '%Supplier%'
    OR t.name LIKE '%Lieferant%'
    OR t.name LIKE '%Customer%'
    OR t.name LIKE '%Kunde%'
    OR t.name LIKE '%Plant%'
    OR t.name LIKE '%Werk%'
    OR t.name LIKE '%Address%'
    OR t.name LIKE '%Adress%'
    OR t.name LIKE '%Barcode%'
    OR t.name LIKE '%Label%'
    OR t.name LIKE '%Etikett%'
    OR t.name LIKE '%Packaging%'
    OR t.name LIKE '%Verpackung%'
    OR t.name LIKE '%VPK%'
  )
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q6: List ALL views (AlplaPROD may expose data via views)
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    v.name              AS ViewName,
    v.create_date       AS CreatedDate,
    v.modify_date       AS LastModifiedDate
FROM sys.views v
INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
WHERE v.is_ms_shipped = 0
ORDER BY s.name, v.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q7: List stored procedures (may reveal business logic names)
-- NOTE: We are NOT executing these — only listing names for discovery.
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SchemaName,
    p.name              AS ProcedureName,
    p.create_date       AS CreatedDate,
    p.modify_date       AS LastModifiedDate
FROM sys.procedures p
INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
WHERE p.is_ms_shipped = 0
ORDER BY s.name, p.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q8: Count summary — tables, views, procedures, functions
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    'Tables' AS ObjectType, COUNT(*) AS ObjectCount
FROM sys.tables WHERE is_ms_shipped = 0
UNION ALL
SELECT
    'Views', COUNT(*)
FROM sys.views WHERE is_ms_shipped = 0
UNION ALL
SELECT
    'Stored Procedures', COUNT(*)
FROM sys.procedures WHERE is_ms_shipped = 0
UNION ALL
SELECT
    'Functions', COUNT(*)
FROM sys.objects WHERE type IN ('FN', 'IF', 'TF') AND is_ms_shipped = 0;

-- ============================================================================
-- END OF SCRIPT 01 — Schema Inspection
-- ============================================================================
