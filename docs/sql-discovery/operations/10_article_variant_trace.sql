-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 10: Article Variant Trace
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Trace article (Artikel) and article variant (Artikelvariante) structures
--   in AlplaPROD. Articles and their variants are referenced across all
--   entities in the inter-plant transfer process: purchase orders, delivery
--   plans, loading positions, goods receipts, and barcodes.
--
--   Screenshot references:
--   - Article ID: 2295 / Description: MM JADE CZ-328
--   - Article Variant ID: 1269 / Description: MM PET CR-8828F
--   - Packaging: SRESINA 1100 KGS BIG BAG
--
--   Understanding the article structure is essential because:
--   1. The same physical material may have different article codes per plant
--   2. Variants represent specific configurations (size, color, resin type)
--   3. Barcodes link physical units to article variants
-- ============================================================================

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 1: Discover article-related tables
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: Find ALL article-related tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Artikel%'
   OR t.name LIKE '%Article%'
   OR t.name LIKE '%Variante%'
   OR t.name LIKE '%Variant%'
   OR t.name LIKE '%Material%'
   OR t.name LIKE '%Product%'
   OR t.name LIKE '%Produkt%'
   OR t.name LIKE '%Alias%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: Find barcode-related tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Barcode%'
   OR t.name LIKE '%Label%'
   OR t.name LIKE '%Etikett%'
   OR t.name LIKE '%SerialNumber%'
   OR t.name LIKE '%Laufend%'
ORDER BY t.name;

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: Find packaging-related tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    t.name              AS TableName,
    p.rows              AS ApproxRowCount,
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS ColumnCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Verpackung%'
   OR t.name LIKE '%VPK%'
   OR t.name LIKE '%Packaging%'
   OR t.name LIKE '%Container%'
   OR t.name LIKE '%Unit%'
   OR t.name LIKE '%Einheit%'
ORDER BY t.name;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 2: Show columns for candidate tables
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: Full column listing for article/variant/barcode/packaging tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
    c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
    c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%Artikel%'
       OR t.name LIKE '%Article%'
       OR t.name LIKE '%Variante%'
       OR t.name LIKE '%Variant%'
       OR t.name LIKE '%Barcode%'
       OR t.name LIKE '%Etikett%'
       OR t.name LIKE '%Verpackung%'
       OR t.name LIKE '%VPK%'
       OR t.name LIKE '%Alias%'
)
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 3: Trace specific article examples
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: Find article 2295 (MM JADE CZ-328)
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE (uncomment and adjust after discovering table names):
--
-- SELECT *
-- FROM [dbo].[ArticleTable]
-- WHERE [PrimaryKeyColumn] = 2295
--    OR [ArticleCodeColumn] = '2295'
--    OR [DescriptionColumn] LIKE '%MM JADE%'
--    OR [DescriptionColumn] LIKE '%CZ-328%';

-- ────────────────────────────────────────────────────────────────────────────
-- Q6: Find article variant 1269 (MM PET CR-8828F)
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[ArticleVariantTable]
-- WHERE [PrimaryKeyColumn] = 1269
--    OR [VariantCodeColumn] = '1269'
--    OR [DescriptionColumn] LIKE '%MM PET%'
--    OR [DescriptionColumn] LIKE '%CR-8828F%';

-- ────────────────────────────────────────────────────────────────────────────
-- Q7: Find all variants for article 2295
-- Business question: How are variants linked to their parent article?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[ArticleVariantTable]
-- WHERE [ArticleIdColumn] = 2295
-- ORDER BY [PrimaryKeyColumn];

-- ────────────────────────────────────────────────────────────────────────────
-- Q8: Find article aliases for article 2295
-- Business question: Can the same article have different codes/names?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[ArticleAliasTable]
-- WHERE [ArticleIdColumn] = 2295;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 4: Article → other entities cross-references
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q9: Find all purchase order positions for article 2295
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[PurchaseOrderItemTable]
-- WHERE [ArticleIdColumn] = 2295;

-- ────────────────────────────────────────────────────────────────────────────
-- Q10: Find all call-offs/deliveries for article variant 1269
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[AbrufTable]
-- WHERE [ArticleVariantIdColumn] = 1269;

-- ────────────────────────────────────────────────────────────────────────────
-- Q11: Find all barcodes/labels for article variant 1269
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT TOP 50 *
-- FROM [dbo].[BarcodeTable]
-- WHERE [ArticleVariantIdColumn] = 1269
-- ORDER BY 1 DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q12: Find all goods receipt positions for article 2295
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[GoodsReceiptPositionTable]
-- WHERE [ArticleIdColumn] = 2295
--    OR [ArticleVariantIdColumn] = 1269;

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 5: Article/variant type classifications
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q13: Distinct article types
-- Business question: What types of articles exist? (raw material, finished product, etc.)
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT DISTINCT [ArticleTypeColumn], COUNT(*) AS RecordCount
-- FROM [dbo].[ArticleTable]
-- GROUP BY [ArticleTypeColumn]
-- ORDER BY RecordCount DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q14: Distinct article variant types (Artikelvariantentyp from screenshot)
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT DISTINCT [VariantTypeColumn], COUNT(*) AS RecordCount
-- FROM [dbo].[ArticleVariantTable]
-- GROUP BY [VariantTypeColumn]
-- ORDER BY RecordCount DESC;

-- ────────────────────────────────────────────────────────────────────────────
-- Q15: Search for packaging description "SRESINA 1100 KGS BIG BAG"
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT *
-- FROM [dbo].[PackagingTable]
-- WHERE [DescriptionColumn] LIKE '%SRESINA%'
--    OR [DescriptionColumn] LIKE '%BIG BAG%'
--    OR [DescriptionColumn] LIKE '%1100%KGS%';

-- ════════════════════════════════════════════════════════════════════════════
-- STEP 6: Article cross-plant consistency check
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q16: Article master data summary for comparison across plants
-- Run on each database and compare: Do the same article codes exist?
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
--
-- SELECT TOP 100
--     [ArticleCodeColumn],
--     [DescriptionColumn],
--     [ArticleTypeColumn],
--     [UnitColumn]
-- FROM [dbo].[ArticleTable]
-- ORDER BY [ArticleCodeColumn];

-- ============================================================================
-- END OF SCRIPT 10 — Article Variant Trace
-- ============================================================================
