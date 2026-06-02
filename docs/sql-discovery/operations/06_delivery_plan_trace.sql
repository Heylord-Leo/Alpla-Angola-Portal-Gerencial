-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 06: Delivery Plan Trace
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Trace delivery plans, call-offs (Abrufe), loading plans, and truck
--   assignments through the AlplaPROD database.
--
--   Screenshot references (from AlplaSTOCK delivery/loading screens):
--   - Abruf (Call-off): 5939
--   - Lieferplan (Delivery Plan): 16391
--   - Article Variant: 1269 / MM PET CR-8828F
--   - Packaging: SRESINA 1100 KGS BIG BAG
-- ============================================================================

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 1: Discover delivery/call-off tables
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: Find tables related to call-offs (Abrufe)
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Abruf%'
   OR t.name LIKE '%CallOff%'
   OR t.name LIKE '%Delivery%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: Find tables related to delivery plans (Lieferplan)
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Lieferplan%'
   OR t.name LIKE '%DeliveryPlan%'
   OR t.name LIKE '%Liefer%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: Find tables related to loading plans (Ladeplan)
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Lade%'
   OR t.name LIKE '%Load%'
   OR t.name LIKE '%Loading%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: Find tables related to trucks and carriers
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%LKW%'
   OR t.name LIKE '%Truck%'
   OR t.name LIKE '%Spediteur%'
   OR t.name LIKE '%Carrier%'
   OR t.name LIKE '%Transport%'
   OR t.name LIKE '%Versand%'
   OR t.name LIKE '%Shipment%'
ORDER BY t.name;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 2: Show columns for each candidate table
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: Columns for ALL delivery-related candidate tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
    c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%Abruf%'
       OR t.name LIKE '%Lieferplan%'
       OR t.name LIKE '%Lade%'
       OR t.name LIKE '%LKW%'
       OR t.name LIKE '%Truck%'
       OR t.name LIKE '%Spediteur%'
       OR t.name LIKE '%Transport%'
       OR t.name LIKE '%Versand%'
)
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 3: Trace specific examples (adjust after table discovery)
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q6: Find Abruf (call-off) with ID = 5939
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (uncomment and adjust after discovering table names):
--
-- SELECT *
-- FROM [dbo].[AbrufTable]
-- WHERE [PrimaryKeyColumn] = 5939
--    OR [AbrufNrColumn] = 5939;

-- ────────────────────────────────────────────────────────────────────────────
-- Q7: Find Lieferplan (delivery plan) with ID = 16391
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[LieferplanTable]
-- WHERE [PrimaryKeyColumn] = 16391
--    OR [LieferplanNrColumn] = 16391;

-- ────────────────────────────────────────────────────────────────────────────
-- Q8: Find the relationship between Abruf 5939 and Lieferplan 16391
-- Business question: How is a call-off linked to its delivery plan?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[AbrufTable] a
-- LEFT JOIN [dbo].[LieferplanTable] lp ON a.[LieferplanIdColumn] = lp.[PrimaryKeyColumn]
-- WHERE a.[PrimaryKeyColumn] = 5939;

-- ────────────────────────────────────────────────────────────────────────────
-- Q9: Find loading plans linked to Abruf 5939
-- Business question: Which loading operations were created for this call-off?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[LadeplanTable]
-- WHERE [AbrufIdColumn] = 5939;

-- ────────────────────────────────────────────────────────────────────────────
-- Q10: Find loading positions (individual items loaded)
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[LadepositionTable]
-- WHERE [LadeplanIdColumn] IN (
--     SELECT [PrimaryKeyColumn] FROM [dbo].[LadeplanTable]
--     WHERE [AbrufIdColumn] = 5939
-- );

-- ────────────────────────────────────────────────────────────────────────────
-- Q11: Find truck/LKW assignments for this delivery
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[LKWTable]
-- WHERE [AbrufIdColumn] = 5939
--    OR [LadeplanIdColumn] IN (
--        SELECT [PrimaryKeyColumn] FROM [dbo].[LadeplanTable]
--        WHERE [AbrufIdColumn] = 5939
--    );

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 4: Investigate packaging references
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q12: Find packaging/VPK tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Verpackung%'
   OR t.name LIKE '%VPK%'
   OR t.name LIKE '%Packaging%'
   OR t.name LIKE '%Container%'
   OR t.name LIKE '%BigBag%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q13: Search for "SRESINA" packaging reference from screenshot
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (requires knowing the packaging table):
--
-- SELECT *
-- FROM [dbo].[PackagingTable]
-- WHERE [DescriptionColumn] LIKE '%SRESINA%'
--    OR [DescriptionColumn] LIKE '%BIG BAG%';

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 5: Discover status values for delivery entities
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q14: Distinct status values on Abruf/call-off table
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT DISTINCT [StatusColumn], COUNT(*) AS RecordCount
-- FROM [dbo].[AbrufTable]
-- GROUP BY [StatusColumn]
-- ORDER BY RecordCount DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q15: Distinct status values on loading plan table
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT DISTINCT [StatusColumn], COUNT(*) AS RecordCount
-- FROM [dbo].[LadeplanTable]
-- GROUP BY [StatusColumn]
-- ORDER BY RecordCount DESC;

-- ============================================================================
-- END OF SCRIPT 06 — Delivery Plan Trace
-- ============================================================================
