-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 11: Business Event Candidates
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Identify which tables and columns can serve as the data source for
--   business events in the Operations timeline. The future Operations module
--   will display a visual timeline like:
--
--     Transfer / PO 26
--     ✓ PO Created → ✓ EDI Sent → ✓ EDI Received → ✓ Loading Created
--     → ✓ Loading Completed → ✓ Delivery Note → ✓ GR Completed
--     Current Status: Completed
--
--   For each business event, we need:
--   - A date/time field (when did it happen?)
--   - A status field (what state is the entity in?)
--   - A user/audit field (who triggered it?)
--   - A link field (how does this event connect to the overall transfer?)
--
--   This script identifies these fields on candidate tables.
-- ============================================================================

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 1: Date/Time fields on candidate tables
-- Business question: Which tables have timestamps that can power timeline events?
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q1: ALL datetime/date columns on logistics-related tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.DATA_TYPE IN ('datetime', 'datetime2', 'date', 'smalldatetime', 'datetimeoffset')
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
       OR t.name LIKE '%Barcode%'
       OR t.name LIKE '%LKW%'
       OR t.name LIKE '%Transport%'
       OR t.name LIKE '%Versand%'
       OR t.name LIKE '%Beleg%'
       OR t.name LIKE '%Artikel%'
  )
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- Q2: Creation date patterns — fields that indicate "when was this created?"
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.DATA_TYPE IN ('datetime', 'datetime2', 'date', 'smalldatetime')
  AND (
       c.COLUMN_NAME LIKE '%Erstellt%'
    OR c.COLUMN_NAME LIKE '%Created%'
    OR c.COLUMN_NAME LIKE '%Hinzugefuegt%'
    OR c.COLUMN_NAME LIKE '%Added%'
    OR c.COLUMN_NAME LIKE '%Angelegt%'
    OR c.COLUMN_NAME LIKE '%Insert%Date%'
    OR c.COLUMN_NAME LIKE '%Datum%Erstellt%'
  )
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q3: Modification date patterns — fields that indicate "when was this last changed?"
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.DATA_TYPE IN ('datetime', 'datetime2', 'date', 'smalldatetime')
  AND (
       c.COLUMN_NAME LIKE '%Geaendert%'
    OR c.COLUMN_NAME LIKE '%Modified%'
    OR c.COLUMN_NAME LIKE '%Updated%'
    OR c.COLUMN_NAME LIKE '%Changed%'
    OR c.COLUMN_NAME LIKE '%Bearbeitet%'
    OR c.COLUMN_NAME LIKE '%LastUpdate%'
  )
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 2: Status fields on candidate tables
-- Business question: Which tables track entity lifecycle status?
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q4: ALL status-like columns on logistics-related tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE (
       c.COLUMN_NAME LIKE '%Status%'
    OR c.COLUMN_NAME LIKE '%State%'
    OR c.COLUMN_NAME LIKE '%Zustand%'
    OR c.COLUMN_NAME LIKE '%Phase%'
    OR c.COLUMN_NAME LIKE '%Step%'
    OR c.COLUMN_NAME LIKE '%Schritt%'
    OR c.COLUMN_NAME LIKE '%Active%'
    OR c.COLUMN_NAME LIKE '%Aktiv%'
    OR c.COLUMN_NAME LIKE '%Complete%'
    OR c.COLUMN_NAME LIKE '%Abgeschlossen%'
    OR c.COLUMN_NAME LIKE '%Closed%'
    OR c.COLUMN_NAME LIKE '%Geschlossen%'
    OR c.COLUMN_NAME LIKE '%Cancelled%'
    OR c.COLUMN_NAME LIKE '%Storniert%'
    OR c.COLUMN_NAME LIKE '%Approved%'
    OR c.COLUMN_NAME LIKE '%Freigegeben%'
    OR c.COLUMN_NAME LIKE '%Confirmed%'
    OR c.COLUMN_NAME LIKE '%Bestaetigt%'
  )
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
       OR t.name LIKE '%LKW%'
       OR t.name LIKE '%Transport%'
       OR t.name LIKE '%Versand%'
       OR t.name LIKE '%Beleg%'
  )
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q5: Status + Date field pairs on the SAME table
-- Business question: Which tables have BOTH a status AND a date that could
-- represent "this status was set at this time"?
-- ────────────────────────────────────────────────────────────────────────────
SELECT DISTINCT
    s.TABLE_NAME,
    s.COLUMN_NAME AS StatusColumn,
    s.DATA_TYPE   AS StatusDataType,
    d.COLUMN_NAME AS DateColumn,
    d.DATA_TYPE   AS DateDataType
FROM INFORMATION_SCHEMA.COLUMNS s
INNER JOIN INFORMATION_SCHEMA.COLUMNS d
    ON s.TABLE_NAME = d.TABLE_NAME
WHERE s.COLUMN_NAME LIKE '%Status%'
  AND d.DATA_TYPE IN ('datetime', 'datetime2', 'date', 'smalldatetime')
  AND d.COLUMN_NAME NOT LIKE '%Status%'
ORDER BY s.TABLE_NAME, s.COLUMN_NAME, d.COLUMN_NAME;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 3: User/Audit fields on candidate tables
-- Business question: Which tables track who created or modified records?
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q6: User/audit columns on logistics-related tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE (
       c.COLUMN_NAME LIKE '%User%'
    OR c.COLUMN_NAME LIKE '%Benutzer%'
    OR c.COLUMN_NAME LIKE '%CreatedBy%'
    OR c.COLUMN_NAME LIKE '%ErstelltVon%'
    OR c.COLUMN_NAME LIKE '%HinzugefuegtVon%'
    OR c.COLUMN_NAME LIKE '%ModifiedBy%'
    OR c.COLUMN_NAME LIKE '%GeaendertVon%'
    OR c.COLUMN_NAME LIKE '%BearbeitetVon%'
    OR c.COLUMN_NAME LIKE '%Operator%'
    OR c.COLUMN_NAME LIKE '%Author%'
    OR c.COLUMN_NAME LIKE '%Verfasser%'
  )
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
       OR t.name LIKE '%LKW%'
       OR t.name LIKE '%Transport%'
       OR t.name LIKE '%Beleg%'
  )
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION 4: Complete event candidate profile per table
-- Each query below returns all date, status, and user fields for one domain
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- Q7: Event profile for Purchase Order tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    CASE
        WHEN c.DATA_TYPE IN ('datetime','datetime2','date','smalldatetime') THEN 'DATE'
        WHEN c.COLUMN_NAME LIKE '%Status%' OR c.COLUMN_NAME LIKE '%State%' THEN 'STATUS'
        WHEN c.COLUMN_NAME LIKE '%User%' OR c.COLUMN_NAME LIKE '%Von%'
             OR c.COLUMN_NAME LIKE '%By%' OR c.COLUMN_NAME LIKE '%Benutzer%' THEN 'USER'
        ELSE 'OTHER'
    END AS FieldCategory
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%Bestell%' OR t.name LIKE '%Order%'
)
AND (
    c.DATA_TYPE IN ('datetime','datetime2','date','smalldatetime')
    OR c.COLUMN_NAME LIKE '%Status%' OR c.COLUMN_NAME LIKE '%State%'
    OR c.COLUMN_NAME LIKE '%User%' OR c.COLUMN_NAME LIKE '%Von%'
    OR c.COLUMN_NAME LIKE '%By%' OR c.COLUMN_NAME LIKE '%Benutzer%'
)
ORDER BY c.TABLE_NAME, FieldCategory, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q8: Event profile for Delivery/Loading tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    CASE
        WHEN c.DATA_TYPE IN ('datetime','datetime2','date','smalldatetime') THEN 'DATE'
        WHEN c.COLUMN_NAME LIKE '%Status%' OR c.COLUMN_NAME LIKE '%State%' THEN 'STATUS'
        WHEN c.COLUMN_NAME LIKE '%User%' OR c.COLUMN_NAME LIKE '%Von%'
             OR c.COLUMN_NAME LIKE '%By%' OR c.COLUMN_NAME LIKE '%Benutzer%' THEN 'USER'
        ELSE 'OTHER'
    END AS FieldCategory
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%Abruf%' OR t.name LIKE '%Liefer%'
       OR t.name LIKE '%Lade%' OR t.name LIKE '%LKW%'
       OR t.name LIKE '%Transport%' OR t.name LIKE '%Versand%'
)
AND (
    c.DATA_TYPE IN ('datetime','datetime2','date','smalldatetime')
    OR c.COLUMN_NAME LIKE '%Status%' OR c.COLUMN_NAME LIKE '%State%'
    OR c.COLUMN_NAME LIKE '%User%' OR c.COLUMN_NAME LIKE '%Von%'
    OR c.COLUMN_NAME LIKE '%By%' OR c.COLUMN_NAME LIKE '%Benutzer%'
)
ORDER BY c.TABLE_NAME, FieldCategory, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q9: Event profile for Goods Receipt tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    CASE
        WHEN c.DATA_TYPE IN ('datetime','datetime2','date','smalldatetime') THEN 'DATE'
        WHEN c.COLUMN_NAME LIKE '%Status%' OR c.COLUMN_NAME LIKE '%State%' THEN 'STATUS'
        WHEN c.COLUMN_NAME LIKE '%User%' OR c.COLUMN_NAME LIKE '%Von%'
             OR c.COLUMN_NAME LIKE '%By%' OR c.COLUMN_NAME LIKE '%Benutzer%' THEN 'USER'
        ELSE 'OTHER'
    END AS FieldCategory
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%Warenein%' OR t.name LIKE '%Receipt%'
       OR t.name LIKE '%Eingang%'
)
AND (
    c.DATA_TYPE IN ('datetime','datetime2','date','smalldatetime')
    OR c.COLUMN_NAME LIKE '%Status%' OR c.COLUMN_NAME LIKE '%State%'
    OR c.COLUMN_NAME LIKE '%User%' OR c.COLUMN_NAME LIKE '%Von%'
    OR c.COLUMN_NAME LIKE '%By%' OR c.COLUMN_NAME LIKE '%Benutzer%'
)
ORDER BY c.TABLE_NAME, FieldCategory, c.COLUMN_NAME;

-- ────────────────────────────────────────────────────────────────────────────
-- Q10: Event profile for EDI/Journal/Transfer tables
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    CASE
        WHEN c.DATA_TYPE IN ('datetime','datetime2','date','smalldatetime') THEN 'DATE'
        WHEN c.COLUMN_NAME LIKE '%Status%' OR c.COLUMN_NAME LIKE '%State%' THEN 'STATUS'
        WHEN c.COLUMN_NAME LIKE '%User%' OR c.COLUMN_NAME LIKE '%Von%'
             OR c.COLUMN_NAME LIKE '%By%' OR c.COLUMN_NAME LIKE '%Benutzer%' THEN 'USER'
        ELSE 'OTHER'
    END AS FieldCategory
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%EDI%' OR t.name LIKE '%Journal%'
       OR t.name LIKE '%Transfer%' OR t.name LIKE '%Transmis%'
       OR t.name LIKE '%Beleg%' OR t.name LIKE '%Uebertrag%'
)
AND (
    c.DATA_TYPE IN ('datetime','datetime2','date','smalldatetime')
    OR c.COLUMN_NAME LIKE '%Status%' OR c.COLUMN_NAME LIKE '%State%'
    OR c.COLUMN_NAME LIKE '%User%' OR c.COLUMN_NAME LIKE '%Von%'
    OR c.COLUMN_NAME LIKE '%By%' OR c.COLUMN_NAME LIKE '%Benutzer%'
)
ORDER BY c.TABLE_NAME, FieldCategory, c.COLUMN_NAME;

-- ============================================================================
-- END OF SCRIPT 11 — Business Event Candidates
-- ============================================================================
