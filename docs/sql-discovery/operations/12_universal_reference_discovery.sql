-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 12: Universal Reference Discovery
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   This is the MOST CRITICAL discovery script for the Operations module.
--
--   The Operations timeline needs ONE reference (or a chain of references)
--   that links the entire inter-plant transfer from PO to Goods Receipt.
--
--   Without this "universal reference", we cannot build the timeline:
--
--     Transfer ??? / PO 26
--     ✓ PO Created → ✓ EDI Sent → ... → ✓ GR Completed
--
--   This script searches for columns that could serve as that link:
--   - Document reference fields (BelegNr, DocumentNr, ReferenceNr)
--   - Parent/child document fields (ParentId, SourceId, TargetId)
--   - External reference fields (ExternalRef, ExternNr, FremdNr)
--   - Cross-entity link fields (BestellungId on non-PO tables, etc.)
--   - EDI message IDs (EDIMessageId, JournalNr)
--   - GUID/UUID fields that might serve as correlation IDs
-- ============================================================================

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 1: Cross-entity reference columns
-- The most likely universal reference is a column that appears across
-- multiple entity tables with the same name or naming pattern
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: Find columns whose name appears in 3+ different tables
-- Business question: Which column names are shared across many tables?
-- A universal reference would appear in PO, Abruf, Loading, and GR tables.
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.COLUMN_NAME,
    COUNT(DISTINCT c.TABLE_NAME) AS TableCount,
    STRING_AGG(c.TABLE_NAME, ', ') WITHIN GROUP (ORDER BY c.TABLE_NAME) AS Tables,
    MIN(c.DATA_TYPE) AS DataType
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t WHERE t.is_ms_shipped = 0
)
GROUP BY c.COLUMN_NAME
HAVING COUNT(DISTINCT c.TABLE_NAME) >= 3
ORDER BY TableCount DESC, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: Same as Q1 but restricted to logistics candidate tables only
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.COLUMN_NAME,
    COUNT(DISTINCT c.TABLE_NAME) AS TableCount,
    STRING_AGG(c.TABLE_NAME, ', ') WITHIN GROUP (ORDER BY c.TABLE_NAME) AS Tables,
    MIN(c.DATA_TYPE) AS DataType
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%Bestell%'
       OR t.name LIKE '%Order%'
       OR t.name LIKE '%Abruf%'
       OR t.name LIKE '%Liefer%'
       OR t.name LIKE '%Lade%'
       OR t.name LIKE '%Warenein%'
       OR t.name LIKE '%EDI%'
       OR t.name LIKE '%Journal%'
       OR t.name LIKE '%Transfer%'
       OR t.name LIKE '%Barcode%'
       OR t.name LIKE '%LKW%'
       OR t.name LIKE '%Transport%'
       OR t.name LIKE '%Beleg%'
)
GROUP BY c.COLUMN_NAME
HAVING COUNT(DISTINCT c.TABLE_NAME) >= 2
ORDER BY TableCount DESC, c.COLUMN_NAME;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 2: Document / reference number columns
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: Document reference columns across ALL tables
-- Business question: Is there a "BelegNr" or "DocumentNr" that links entities?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Beleg%'
   OR c.COLUMN_NAME LIKE '%Document%'
   OR c.COLUMN_NAME LIKE '%Dokument%'
   OR c.COLUMN_NAME LIKE '%Ref%Nr%'
   OR c.COLUMN_NAME LIKE '%Reference%'
   OR c.COLUMN_NAME LIKE '%Referenz%'
   OR c.COLUMN_NAME LIKE '%Vorgang%'
   OR c.COLUMN_NAME LIKE '%Transaction%'
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: Parent/child/source/target document link columns
-- Business question: Do entities reference their "parent" or "source" document?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Parent%'
   OR c.COLUMN_NAME LIKE '%Eltern%'
   OR c.COLUMN_NAME LIKE '%Source%Doc%'
   OR c.COLUMN_NAME LIKE '%Quell%Dok%'
   OR c.COLUMN_NAME LIKE '%Target%Doc%'
   OR c.COLUMN_NAME LIKE '%Ziel%Dok%'
   OR c.COLUMN_NAME LIKE '%Origin%'
   OR c.COLUMN_NAME LIKE '%Ursprung%'
   OR c.COLUMN_NAME LIKE '%BasedOn%'
   OR c.COLUMN_NAME LIKE '%DerivedFrom%'
   OR c.COLUMN_NAME LIKE '%LinkedTo%'
   OR c.COLUMN_NAME LIKE '%RelatedTo%'
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: External reference columns
-- Business question: Is there a reference to the "other plant's" document?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Extern%'
   OR c.COLUMN_NAME LIKE '%External%'
   OR c.COLUMN_NAME LIKE '%Fremd%'
   OR c.COLUMN_NAME LIKE '%Foreign%'
   OR c.COLUMN_NAME LIKE '%Remote%'
   OR c.COLUMN_NAME LIKE '%Partner%'
   OR c.COLUMN_NAME LIKE '%Counter%Part%'
   OR c.COLUMN_NAME LIKE '%Gegen%'
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 3: GUID / Correlation ID columns
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q6: GUID/uniqueidentifier columns (potential correlation IDs)
-- Business question: Are there GUIDs used as cross-entity correlation keys?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.DATA_TYPE = 'uniqueidentifier'
  AND c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%Bestell%'
       OR t.name LIKE '%Order%'
       OR t.name LIKE '%Abruf%'
       OR t.name LIKE '%Liefer%'
       OR t.name LIKE '%Lade%'
       OR t.name LIKE '%Warenein%'
       OR t.name LIKE '%EDI%'
       OR t.name LIKE '%Journal%'
       OR t.name LIKE '%Transfer%'
       OR t.name LIKE '%Beleg%'
  )
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q7: ALL GUID columns across the entire database
-- (broader search — universal reference might be a GUID)
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.DATA_TYPE = 'uniqueidentifier'
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 4: PO reference on non-PO tables (chain tracing)
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q8: Which non-PO tables have a column referencing a Purchase Order?
-- Business question: Can we trace from GR/Abruf/Loading back to the PO?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE (
       c.COLUMN_NAME LIKE '%Bestell%'
    OR c.COLUMN_NAME LIKE '%Order%Id%'
    OR c.COLUMN_NAME LIKE '%PO%Id%'
    OR c.COLUMN_NAME LIKE '%Purchase%'
  )
  AND c.TABLE_NAME NOT LIKE '%Bestell%'
  AND c.TABLE_NAME NOT LIKE '%Order%'
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q9: Which tables have a column referencing an Abruf/Call-off?
-- Business question: Can we trace from Loading/GR back to the call-off?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE (
       c.COLUMN_NAME LIKE '%Abruf%'
    OR c.COLUMN_NAME LIKE '%CallOff%'
  )
  AND c.TABLE_NAME NOT LIKE '%Abruf%'
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q10: Which tables have a column referencing a Wareneingang/GR?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE (
       c.COLUMN_NAME LIKE '%Warenein%'
    OR c.COLUMN_NAME LIKE '%Receipt%'
    OR c.COLUMN_NAME LIKE '%Eingang%'
  )
  AND c.TABLE_NAME NOT LIKE '%Warenein%'
  AND c.TABLE_NAME NOT LIKE '%Receipt%'
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q11: Which tables have a column referencing a Journal?
-- Business question: Is JournalNr the universal link?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Journal%'
  AND c.TABLE_NAME NOT LIKE '%Journal%'
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 5: Trace the reference chain for example PO 26
-- (run AFTER table names are confirmed from scripts 01+02)
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q12: For a known PO, extract ALL reference/link fields
-- TEMPLATE (uncomment and adjust after table discovery):
-- ────────────────────────────────────────────────────────────────────────────
-- SELECT
--     [PrimaryKeyColumn],
--     [JournalNrColumn],
--     [TransmissionStatusColumn],
--     [BelegNrColumn],
--     [ExternalRefColumn],
--     [GUIDColumn]
-- FROM [dbo].[PurchaseOrderTable]
-- WHERE [PrimaryKeyColumn] = 26;

-- ────────────────────────────────────────────────────────────────────────────
-- Q13: For the linked Abruf, extract ALL reference/link fields
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
-- SELECT
--     [PrimaryKeyColumn],
--     [PORefColumn],
--     [LieferplanRefColumn],
--     [JournalNrColumn],
--     [BelegNrColumn],
--     [ExternalRefColumn]
-- FROM [dbo].[AbrufTable]
-- WHERE [PrimaryKeyColumn] = 5939;

-- ────────────────────────────────────────────────────────────────────────────
-- Q14: For the linked GR, extract ALL reference/link fields
-- ────────────────────────────────────────────────────────────────────────────
-- TEMPLATE:
-- SELECT
--     [PrimaryKeyColumn],
--     [PORefColumn],
--     [AbrufRefColumn],
--     [JournalNrColumn],
--     [BelegNrColumn],
--     [ExternalRefColumn]
-- FROM [dbo].[GoodsReceiptTable]
-- WHERE [PrimaryKeyColumn] = 887;

-- ────────────────────────────────────────────────────────────────────────────
-- Q15: Summary — find which reference value is COMMON across PO, Abruf, GR
-- This is the ultimate test: which field appears on all 3 entities
-- with the same value, linking them into one transfer?
-- ────────────────────────────────────────────────────────────────────────────
-- MANUAL ANALYSIS STEP:
-- After running Q12, Q13, Q14, compare the reference fields.
-- Look for a value that appears in ALL three results.
-- That is the universal (or semi-universal) reference.

-- ============================================================================
-- END OF SCRIPT 12 — Universal Reference Discovery
-- ============================================================================
