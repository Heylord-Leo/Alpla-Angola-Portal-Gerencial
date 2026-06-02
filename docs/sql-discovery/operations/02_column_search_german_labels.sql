-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 02: Column Search by German UI Labels
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Search database metadata (INFORMATION_SCHEMA and sys catalogs) for
--   tables and columns whose names match the German UI labels visible
--   in the AlplaPURCHASE, AlplaSTOCK Delivery, and AlplaSTOCK Goods
--   Receipt screenshots.
--
--   This helps map UI fields to actual database columns.
-- ============================================================================

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION A: AlplaPURCHASE — Purchase Order UI Labels
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- A1: Search for "Bestellung" (Purchase Order)
-- UI context: Main entity on the Purchase Orders screen
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE,
    c.ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME LIKE '%Bestell%'
   OR c.COLUMN_NAME LIKE '%Bestell%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- A2: Search for "Lieferant" (Supplier)
-- UI context: Supplier reference on PO header
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME LIKE '%Lieferant%'
   OR c.COLUMN_NAME LIKE '%Lieferant%'
   OR c.COLUMN_NAME LIKE '%Supplier%'
   OR c.COLUMN_NAME LIKE '%Vendor%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- A3: Search for "Lieferadresse" / "Rechnungsadresse" (Delivery/Invoice Address)
-- UI context: Address fields on PO header
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Adress%'
   OR c.COLUMN_NAME LIKE '%Address%'
   OR c.COLUMN_NAME LIKE '%Liefer%Adress%'
   OR c.COLUMN_NAME LIKE '%Rechnung%Adress%'
   OR c.TABLE_NAME LIKE '%Adress%'
   OR c.TABLE_NAME LIKE '%Address%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- A4: Search for "Bestellstatus" / "Übertragungsstatus" (Order Status / Transmission Status)
-- UI context: Status fields on PO header — critical for EDI flow tracking
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Status%'
   OR c.COLUMN_NAME LIKE '%Uebertrag%'
   OR c.COLUMN_NAME LIKE '%Transmis%'
   OR c.COLUMN_NAME LIKE '%Transfer%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- A5: Search for "Journal" / "Journal-Nr." (Journal / Journal Number)
-- UI context: EDI transmission journal reference on PO
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Journal%'
   OR c.TABLE_NAME LIKE '%Journal%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- A6: Search for "Revision" (Document revision tracking)
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Revision%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- A7: Search for "Artikel" / "Artikelalias" / "Artikelvariante" (Article / Alias / Variant)
-- UI context: Item/material references on PO lines and delivery positions
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME LIKE '%Artikel%'
   OR c.COLUMN_NAME LIKE '%Artikel%'
   OR c.TABLE_NAME LIKE '%Article%'
   OR c.COLUMN_NAME LIKE '%Article%'
   OR c.COLUMN_NAME LIKE '%Variante%'
   OR c.COLUMN_NAME LIKE '%Variant%'
   OR c.COLUMN_NAME LIKE '%Alias%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- A8: Search for quantity fields: "Menge" / "Menge VPK" / "Preis" / "Preis Total"
-- UI context: Quantity and pricing columns on PO lines
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
    c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Menge%'
   OR c.COLUMN_NAME LIKE '%Quantity%'
   OR c.COLUMN_NAME LIKE '%Preis%'
   OR c.COLUMN_NAME LIKE '%Price%'
   OR c.COLUMN_NAME LIKE '%VPK%'
   OR c.COLUMN_NAME LIKE '%Verpackung%'
   OR c.COLUMN_NAME LIKE '%Packaging%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- A9: Search for date fields: "Bestelldatum" / "Liefertermin" / "Erstellt" / "Geändert"
-- UI context: Date fields on PO header and lines
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Datum%'
   OR c.COLUMN_NAME LIKE '%Date%'
   OR c.COLUMN_NAME LIKE '%Termin%'
   OR c.COLUMN_NAME LIKE '%Erstellt%'
   OR c.COLUMN_NAME LIKE '%Created%'
   OR c.COLUMN_NAME LIKE '%Geaendert%'
   OR c.COLUMN_NAME LIKE '%Modified%'
   OR c.COLUMN_NAME LIKE '%Hinzugefuegt%'
   OR c.COLUMN_NAME LIKE '%Added%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION B: AlplaSTOCK — Delivery Plan / Loading UI Labels
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- B1: Search for "Abruf" / "Abrufe" (Call-off / Delivery)
-- UI context: Main entity on the delivery/loading screen
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME LIKE '%Abruf%'
   OR c.COLUMN_NAME LIKE '%Abruf%'
   OR c.TABLE_NAME LIKE '%CallOff%'
   OR c.COLUMN_NAME LIKE '%CallOff%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- B2: Search for "Lieferplan" (Delivery Plan)
-- UI context: Delivery plan reference linked to a call-off
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME LIKE '%Lieferplan%'
   OR c.COLUMN_NAME LIKE '%Lieferplan%'
   OR c.TABLE_NAME LIKE '%DeliveryPlan%'
   OR c.COLUMN_NAME LIKE '%DeliveryPlan%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- B3: Search for "Ladeplan" / "Ladeposition" (Loading Plan / Loading Position)
-- UI context: Loading plan and individual loading positions with barcodes
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME LIKE '%Lade%'
   OR c.COLUMN_NAME LIKE '%Lade%'
   OR c.TABLE_NAME LIKE '%Load%'
   OR c.COLUMN_NAME LIKE '%Load%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- B4: Search for "LKW" / "Spediteur" (Truck / Carrier)
-- UI context: Transport/truck information on loading screen
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME LIKE '%LKW%'
   OR c.COLUMN_NAME LIKE '%LKW%'
   OR c.TABLE_NAME LIKE '%Truck%'
   OR c.COLUMN_NAME LIKE '%Truck%'
   OR c.TABLE_NAME LIKE '%Spediteur%'
   OR c.COLUMN_NAME LIKE '%Spediteur%'
   OR c.TABLE_NAME LIKE '%Carrier%'
   OR c.COLUMN_NAME LIKE '%Carrier%'
   OR c.TABLE_NAME LIKE '%Transport%'
   OR c.COLUMN_NAME LIKE '%Transport%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- B5: Search for "Kunde" / "Kunden Nr." (Customer / Customer Number)
-- UI context: Customer reference on delivery plans
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME LIKE '%Kunde%'
   OR c.COLUMN_NAME LIKE '%Kunde%'
   OR c.TABLE_NAME LIKE '%Customer%'
   OR c.COLUMN_NAME LIKE '%Customer%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION C: AlplaSTOCK — Goods Receipt UI Labels
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- C1: Search for "Wareneingang" (Goods Receipt)
-- UI context: Main entity on the goods receipt screen
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME LIKE '%Warenein%'
   OR c.COLUMN_NAME LIKE '%Warenein%'
   OR c.TABLE_NAME LIKE '%GoodsReceipt%'
   OR c.COLUMN_NAME LIKE '%GoodsReceipt%'
   OR c.TABLE_NAME LIKE '%Receipt%'
   OR c.COLUMN_NAME LIKE '%Receipt%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- C2: Search for "Beleg" (Document / Voucher)
-- UI context: Document reference on goods receipt header
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME LIKE '%Beleg%'
   OR c.COLUMN_NAME LIKE '%Beleg%'
   OR c.TABLE_NAME LIKE '%Document%'
   OR c.COLUMN_NAME LIKE '%Document%'
   OR c.TABLE_NAME LIKE '%Voucher%'
   OR c.COLUMN_NAME LIKE '%Voucher%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- C3: Search for "Planmenge" / "Offene Menge" (Planned Qty / Open Qty)
-- UI context: Quantity tracking fields on goods receipt
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Planmenge%'
   OR c.COLUMN_NAME LIKE '%PlannedQty%'
   OR c.COLUMN_NAME LIKE '%OffeneMenge%'
   OR c.COLUMN_NAME LIKE '%OpenQty%'
   OR c.COLUMN_NAME LIKE '%Offen%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ────────────────────────────────────────────────────────────────────────────
-- C4: Search for "Barcode" / "Laufende Nr." / "Externe Laufende Nr."
-- (Barcode / Serial Number / External Serial Number)
-- UI context: Position-level tracking on loading and goods receipt
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Barcode%'
   OR c.COLUMN_NAME LIKE '%Laufend%'
   OR c.COLUMN_NAME LIKE '%Serial%'
   OR c.COLUMN_NAME LIKE '%Running%'
   OR c.COLUMN_NAME LIKE '%Extern%Laufend%'
   OR c.TABLE_NAME LIKE '%Barcode%'
   OR c.TABLE_NAME LIKE '%Label%'
   OR c.TABLE_NAME LIKE '%Etikett%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ════════════════════════════════════════════════════════════════════════════
-- SECTION D: Cross-cutting — Plant / Werk / Standort references
-- ════════════════════════════════════════════════════════════════════════════

-- ────────────────────────────────────────────────────────────────────────────
-- D1: Search for plant/site/location identifiers
-- Business question: How does the database identify which plant a record belongs to?
-- ────────────────────────────────────────────────────────────────────────────
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.COLUMN_NAME LIKE '%Plant%'
   OR c.COLUMN_NAME LIKE '%Werk%'
   OR c.COLUMN_NAME LIKE '%Standort%'
   OR c.COLUMN_NAME LIKE '%Site%'
   OR c.COLUMN_NAME LIKE '%Location%'
   OR c.COLUMN_NAME LIKE '%Viana%'
   OR c.TABLE_NAME LIKE '%Plant%'
   OR c.TABLE_NAME LIKE '%Werk%'
   OR c.TABLE_NAME LIKE '%Standort%'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

-- ============================================================================
-- END OF SCRIPT 02 — Column Search by German UI Labels
-- ============================================================================
