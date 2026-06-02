-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 07: Goods Receipt Trace
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Trace goods receipt (Wareneingang) records through the AlplaPROD database.
--   The goods receipt is the final step of the inter-plant transfer process
--   where the requesting plant confirms material arrival.
--
--   Screenshot references (from AlplaSTOCK goods receipt screens):
--   - Goods Receipt ID (Wareneingang): 887
--   - Goods Receipt Order (Wareneingangsauftrag): 1805
--   - Goods Receipt Plan (Wareneingangsplan): 1907
--   - Goods Receipt Positions: 13032, 13033, ...
--   - Linked Purchase Order (Bestellung): 26
-- ============================================================================

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 1: Discover goods receipt tables
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: Find all tables related to goods receipt
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Warenein%'
   OR t.name LIKE '%GoodsReceipt%'
   OR t.name LIKE '%Receipt%'
   OR t.name LIKE '%Eingang%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: Find tables potentially related to stock movements
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Stock%'
   OR t.name LIKE '%Lager%'
   OR t.name LIKE '%Bestand%'
   OR t.name LIKE '%Inventory%'
   OR t.name LIKE '%Movement%'
   OR t.name LIKE '%Bewegung%'
ORDER BY t.name;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 2: Show columns for candidate goods receipt tables
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: Columns for ALL goods receipt candidate tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
    c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%Warenein%'
       OR t.name LIKE '%GoodsReceipt%'
       OR t.name LIKE '%Receipt%'
)
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 3: Trace specific examples (adjust after table discovery)
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: Find Goods Receipt (Wareneingang) with ID = 887
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (uncomment and adjust after discovering table names):
--
-- SELECT *
-- FROM [dbo].[WareneingangTable]
-- WHERE [PrimaryKeyColumn] = 887;

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: Find Goods Receipt Order (Wareneingangsauftrag) with ID = 1805
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[WareneingangsauftragTable]
-- WHERE [PrimaryKeyColumn] = 1805;

-- ────────────────────────────────────────────────────────────────────────────
-- Q6: Find Goods Receipt Plan (Wareneingangsplan) with ID = 1907
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[WareneingangsplanTable]
-- WHERE [PrimaryKeyColumn] = 1907;

-- ────────────────────────────────────────────────────────────────────────────
-- Q7: Find Goods Receipt Positions (IDs 13032, 13033)
-- Business question: What items were received in this goods receipt?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[WareneingangspositionTable]
-- WHERE [PrimaryKeyColumn] IN (13032, 13033)
--    OR [WareneingangIdColumn] = 887;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 4: Trace the link from Goods Receipt back to Purchase Order
-- This is the critical closed-loop connection
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q8: Find how Goods Receipt 887 links to Purchase Order 26
-- Business question: Which column connects WE to Bestellung?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- -- Check if the GR header has a PO reference:
-- SELECT *
-- FROM [dbo].[WareneingangTable]
-- WHERE [PrimaryKeyColumn] = 887;
-- -- Look for columns like BestellungId, PurchaseOrderId, OrderRef, etc.
--
-- -- Or check if the GR positions reference PO items:
-- SELECT *
-- FROM [dbo].[WareneingangspositionTable]
-- WHERE [WareneingangIdColumn] = 887;
-- -- Look for columns like BestellpositionId, POItemId, etc.

-- ────────────────────────────────────────────────────────────────────────────
-- Q9: Discover the full chain: PO → Delivery → Loading → GR
-- Business question: Can we trace the complete flow for a single transaction?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (adjust all table/column names after discovery):
--
-- -- Full chain query (conceptual):
-- SELECT
--     po.[OrderNumber]        AS PurchaseOrder,
--     poi.[PositionNumber]    AS POPosition,
--     ab.[AbrufNr]            AS CallOff,
--     lp.[LieferplanNr]      AS DeliveryPlan,
--     ld.[LadeplanNr]        AS LoadingPlan,
--     we.[WareneingangNr]    AS GoodsReceipt,
--     wep.[PositionNr]       AS GRPosition
-- FROM [dbo].[PurchaseOrderTable] po
-- LEFT JOIN [dbo].[PurchaseOrderItemTable] poi ON po.[Id] = poi.[OrderId]
-- LEFT JOIN [dbo].[AbrufTable] ab ON poi.[Id] = ab.[POItemId]
-- LEFT JOIN [dbo].[LieferplanTable] lp ON ab.[LieferplanId] = lp.[Id]
-- LEFT JOIN [dbo].[LadeplanTable] ld ON ab.[Id] = ld.[AbrufId]
-- LEFT JOIN [dbo].[WareneingangTable] we ON po.[Id] = we.[BestellungId]
-- LEFT JOIN [dbo].[WareneingangspositionTable] wep ON we.[Id] = wep.[WareneingangId]
-- WHERE po.[Id] = 26;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 5: Goods Receipt status values and document references
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q10: Distinct goods receipt types (Typ field from screenshots)
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT DISTINCT [TypColumn], COUNT(*) AS RecordCount
-- FROM [dbo].[WareneingangTable]
-- GROUP BY [TypColumn]
-- ORDER BY RecordCount DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q11: Distinct goods receipt statuses
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT DISTINCT [StatusColumn], COUNT(*) AS RecordCount
-- FROM [dbo].[WareneingangTable]
-- GROUP BY [StatusColumn]
-- ORDER BY RecordCount DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q12: Check Beleg (document) references on goods receipt
-- Business question: What types of documents are linked to goods receipts?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT TOP 50
--     [PrimaryKeyColumn],
--     [BelegColumn],
--     [TypColumn],
--     [StatusColumn],
--     [DatumColumn]
-- FROM [dbo].[WareneingangTable]
-- ORDER BY [DatumColumn] DESC;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 6: Quantity tracking (planned vs open vs received)
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q13: Quantity analysis on goods receipt positions
-- Business question: How are planned, open, and received quantities tracked?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT TOP 50
--     [WareneingangIdColumn],
--     [PositionNrColumn],
--     [PlanmengeColumn]       AS PlannedQty,
--     [OffeneMengeColumn]     AS OpenQty,
--     [MengeColumn]           AS ReceivedQty,
--     [MengeVPKColumn]        AS PackagingQty,
--     [BarcodeColumn]
-- FROM [dbo].[WareneingangspositionTable]
-- WHERE [WareneingangIdColumn] = 887
-- ORDER BY [PositionNrColumn];

-- ============================================================================
-- END OF SCRIPT 07 — Goods Receipt Trace
-- ============================================================================
