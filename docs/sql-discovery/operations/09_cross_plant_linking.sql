-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 09: Cross-Plant Linking
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Investigate how AlplaPROD databases on different servers reference
--   each other. The inter-plant transfer process spans multiple databases:
--
--   - AOVIA1VMS006 → Viana 1 database (supplying plant)
--   - AOVIA1VMS006 → Viana 3 database
--   - AOVIA2VMS006 → Viana 2 database (requesting plant)
--
--   This script searches for:
--   1. Plant/Werk identifiers within a single database
--   2. Cross-database reference columns (foreign plant IDs)
--   3. Linked server definitions
--   4. Synonyms pointing to remote databases
--   5. Tables that distinguish "source plant" from "destination plant"
-- ============================================================================

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 1: Plant / Werk identification within the current database
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: Tables that might define plants/sites/locations
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Plant%'
   OR t.name LIKE '%Werk%'
   OR t.name LIKE '%Standort%'
   OR t.name LIKE '%Site%'
   OR t.name LIKE '%Location%'
   OR t.name LIKE '%Filiale%'
   OR t.name LIKE '%Branch%'
   OR t.name LIKE '%Company%'
   OR t.name LIKE '%Firma%'
   OR t.name LIKE '%Mandant%'
   OR t.name LIKE '%Tenant%'
   OR t.name LIKE '%Viana%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: Sample data from plant/site tables (if they exist)
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (uncomment after Q1 identifies actual tables):
--
-- SELECT TOP 50 *
-- FROM [dbo].[PlantTable]
-- ORDER BY 1;

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: Columns referencing plant identifiers across ALL tables
-- Business question: Which tables have a plant/site/location column?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%PlantId%'
   OR c.COLUMN_NAME LIKE '%WerkId%'
   OR c.COLUMN_NAME LIKE '%Plant%'
   OR c.COLUMN_NAME LIKE '%Werk%'
   OR c.COLUMN_NAME LIKE '%Standort%'
   OR c.COLUMN_NAME LIKE '%SiteId%'
   OR c.COLUMN_NAME LIKE '%LocationId%'
   OR c.COLUMN_NAME LIKE '%BranchId%'
   OR c.COLUMN_NAME LIKE '%CompanyId%'
   OR c.COLUMN_NAME LIKE '%MandantId%'
   OR c.COLUMN_NAME LIKE '%FirmId%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 2: Source ↔ Destination plant references
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: Columns suggesting source/origin vs. destination/target plant
-- Business question: How does the DB track "from plant" vs "to plant"?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Source%'
   OR c.COLUMN_NAME LIKE '%Quelle%'
   OR c.COLUMN_NAME LIKE '%Origin%'
   OR c.COLUMN_NAME LIKE '%Herkunft%'
   OR c.COLUMN_NAME LIKE '%Destination%'
   OR c.COLUMN_NAME LIKE '%Ziel%'
   OR c.COLUMN_NAME LIKE '%Target%'
   OR c.COLUMN_NAME LIKE '%From%Plant%'
   OR c.COLUMN_NAME LIKE '%To%Plant%'
   OR c.COLUMN_NAME LIKE '%Von%Werk%'
   OR c.COLUMN_NAME LIKE '%Nach%Werk%'
   OR c.COLUMN_NAME LIKE '%Absender%'
   OR c.COLUMN_NAME LIKE '%Empfaenger%'
   OR c.COLUMN_NAME LIKE '%Sender%'
   OR c.COLUMN_NAME LIKE '%Receiver%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: Tables with BOTH customer AND supplier columns
-- Business question: Inter-plant transfers may model plants as both
-- "customer" (requesting) and "supplier" (providing). Which tables
-- have both roles in the same record?
-- ────────────────────────────────────────────────────────────────────────────
SELECT DISTINCT c1.TABLE_NAME
FROM INFORMATION_SCHEMA.COLUMNS c1
WHERE (c1.COLUMN_NAME LIKE '%Kunde%' OR c1.COLUMN_NAME LIKE '%Customer%')
  AND EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS c2
    WHERE c2.TABLE_NAME = c1.TABLE_NAME
      AND (c2.COLUMN_NAME LIKE '%Lieferant%' OR c2.COLUMN_NAME LIKE '%Supplier%')
  )
ORDER BY c1.TABLE_NAME;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 3: Linked servers and cross-database references
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q6: List all linked servers
-- Business question: Are the other AlplaPROD databases accessible via
-- linked server from this database?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.server_id,
    s.name              AS LinkedServerName,
    s.product            AS Product,
    s.provider           AS OleDbProvider,
    s.data_source        AS DataSource,
    s.catalog            AS DefaultCatalog,
    s.is_linked          AS IsLinked,
    s.is_remote_login_enabled AS RemoteLoginEnabled,
    s.modify_date        AS LastModified
FROM sys.servers s
ORDER BY s.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q7: List all synonyms (may point to remote tables)
-- Business question: Are there synonyms that reference tables on other
-- servers or databases?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    s.name              AS SynonymName,
    s.base_object_name  AS TargetObject,
    SCHEMA_NAME(s.schema_id) AS SchemaName,
    s.create_date       AS CreatedDate
FROM sys.synonyms s
ORDER BY s.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q8: Search for four-part names in view definitions
-- Business question: Do any views reference [Server].[Database].[Schema].[Table]?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    v.name              AS ViewName,
    m.definition        AS ViewDefinition
FROM sys.views v
INNER JOIN sys.sql_modules m ON v.object_id = m.object_id
WHERE m.definition LIKE '%AOVIA1VMS006%'
   OR m.definition LIKE '%AOVIA2VMS006%'
   OR m.definition LIKE '%Viana%'
   OR m.definition LIKE '%\[%\].\[%\].\[%\].\[%\]%' ESCAPE '\'
ORDER BY v.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q9: Search for four-part names in stored procedure definitions
-- Business question: Do any SPs reference other databases/servers?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    OBJECT_NAME(m.object_id) AS ObjectName,
    m.definition        AS ObjectDefinition
FROM sys.sql_modules m
WHERE m.definition LIKE '%AOVIA1VMS006%'
   OR m.definition LIKE '%AOVIA2VMS006%'
   OR m.definition LIKE '%Viana%';

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 4: Inter-plant customer/supplier mapping
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q10: Customer table — search for inter-plant customers
-- Business question: Is Viana 2 registered as a "customer" in Viana 1's DB?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (adjust after discovering table names):
--
-- SELECT TOP 50 *
-- FROM [dbo].[CustomerTable]
-- WHERE [NameColumn] LIKE '%Viana%'
--    OR [NameColumn] LIKE '%Alpla%'
--    OR [CodeColumn] LIKE '%V1%'
--    OR [CodeColumn] LIKE '%V2%'
--    OR [CodeColumn] LIKE '%V3%';

-- ────────────────────────────────────────────────────────────────────────────
-- Q11: Supplier table — search for inter-plant suppliers
-- Business question: Is Viana 1 registered as a "supplier" in Viana 2's DB?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT TOP 50 *
-- FROM [dbo].[SupplierTable]
-- WHERE [NameColumn] LIKE '%Viana%'
--    OR [NameColumn] LIKE '%Alpla%'
--    OR [CodeColumn] LIKE '%V1%'
--    OR [CodeColumn] LIKE '%V2%'
--    OR [CodeColumn] LIKE '%V3%';

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 5: Database comparison preparation
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q12: Database fingerprint for comparison
-- Run this on EACH database to compare structures between plants.
-- Identical structures suggest shared schema; differences may reveal
-- plant-specific customizations.
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    @@SERVERNAME        AS ServerName,
    DB_NAME()           AS DatabaseName,
    (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) AS TableCount,
    (SELECT COUNT(*) FROM sys.views WHERE is_ms_shipped = 0) AS ViewCount,
    (SELECT COUNT(*) FROM sys.procedures WHERE is_ms_shipped = 0) AS SPCount,
    (SELECT COUNT(*) FROM sys.foreign_keys) AS FKCount,
    (SELECT COUNT(*) FROM sys.servers WHERE is_linked = 1) AS LinkedServerCount,
    (SELECT COUNT(*) FROM sys.synonyms) AS SynonymCount;

-- ────────────────────────────────────────────────────────────────────────────
-- Q13: Tables unique to this database (compare with other plant DBs)
-- Save the output of this query from each database, then diff them
-- to find plant-specific tables.
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
ORDER BY t.name;

-- ============================================================================
-- END OF SCRIPT 09 — Cross-Plant Linking
-- ============================================================================
