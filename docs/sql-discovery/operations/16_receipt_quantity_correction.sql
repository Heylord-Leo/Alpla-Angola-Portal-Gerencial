-- ============================================================================
-- OPERATIONS MODULE — AlplaPROD Discovery
-- Script 16: Receipt Quantity Correction
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Investigate why PO #3579 (VIANA1) shows received quantity = 0
--   despite receipt being marked as completed (Status 21).
--   Find the correct source for received quantity.
--
-- RUN AGAINST: AOVIA1VMS006 / AlplaPROD_aovia1
-- ============================================================================

-- ════════════════════════════════════════════════════════════════════════════
-- Q1: T_Wareneingaenge header for PO #3579 — check IstMenge and SollMenge
-- ════════════════════════════════════════════════════════════════════════════

SELECT
    w.IdWareneingang,
    w.IdBestellung,
    w.IdBestellPosition,
    w.[Status],
    w.Datum,
    w.SollMenge,
    w.SollMengeVPK,
    w.IstMenge,
    w.IstMengeVPK,
    w.Typ,
    w.IdJournal,
    w.IdJournalPosition,
    w.IdAuftragsAbruf,
    w.IdArtikelVarianten,
    w.Bemerkung,
    w.Add_User,
    w.Add_Date,
    w.BestellPositionStatus
FROM [dbo].[T_Wareneingaenge] w
WHERE w.IdBestellung = 3579
   OR w.IdBestellPosition IN (
       SELECT bp.IdBestellPosition
       FROM [dbo].[T_Bestellpositionen] bp
       WHERE bp.IdBestellung = 3579
   )
ORDER BY w.IdWareneingang;

-- ════════════════════════════════════════════════════════════════════════════
-- Q2: T_Bestellpositionen for PO #3579 — check BestellMenge
-- ════════════════════════════════════════════════════════════════════════════

SELECT
    bp.IdBestellPosition,
    bp.IdBestellung,
    bp.BestellMenge,
    bp.BestellMengeVPK,
    bp.IdArtikelVarianten
FROM [dbo].[T_Bestellpositionen] bp
WHERE bp.IdBestellung = 3579
ORDER BY bp.IdBestellPosition;

-- ════════════════════════════════════════════════════════════════════════════
-- Q3: T_WareneingangPlanungen for receipts of PO #3579
--     Check if Menge is populated at planning level
-- ════════════════════════════════════════════════════════════════════════════

SELECT
    wep.IdWareneingangPlanung,
    wep.IdWareneingangAuftrag,
    wep.IdWareneingang,
    wep.Menge,
    wep.MengeVPK,
    wep.NOKMenge,
    wep.NOKMengeVPK,
    wep.EntladeMenge,
    wep.EntladeMengeVPK,
    wep.[Status],
    wep.LadeStatus,
    wep.IdJournal,
    wep.IdJournalPosition,
    wep.Typ,
    wep.Reihenfolge
FROM [dbo].[T_WareneingangPlanungen] wep
WHERE wep.IdWareneingang IN (
    SELECT w.IdWareneingang
    FROM [dbo].[T_Wareneingaenge] w
    WHERE w.IdBestellung = 3579
       OR w.IdBestellPosition IN (
           SELECT bp.IdBestellPosition
           FROM [dbo].[T_Bestellpositionen] bp
           WHERE bp.IdBestellung = 3579
       )
)
ORDER BY wep.IdWareneingangPlanung;

-- ════════════════════════════════════════════════════════════════════════════
-- Q4: T_WareneingangPositionen for receipts of PO #3579
--     Check if Menge is at barcode position level
-- ════════════════════════════════════════════════════════════════════════════

SELECT
    wpos.IdWareneingangPosition,
    wpos.IdWareneingangPlanung,
    wpos.Menge,
    wpos.MengeVPK,
    wpos.Barcode,
    wpos.Beleg,
    wpos.Add_Date,
    wpos.LfdNrIntern,
    wpos.LfdNrExtern
FROM [dbo].[T_WareneingangPositionen] wpos
WHERE wpos.IdWareneingangPlanung IN (
    SELECT wep.IdWareneingangPlanung
    FROM [dbo].[T_WareneingangPlanungen] wep
    WHERE wep.IdWareneingang IN (
        SELECT w.IdWareneingang
        FROM [dbo].[T_Wareneingaenge] w
        WHERE w.IdBestellung = 3579
           OR w.IdBestellPosition IN (
               SELECT bp.IdBestellPosition
               FROM [dbo].[T_Bestellpositionen] bp
               WHERE bp.IdBestellung = 3579
           )
    )
)
ORDER BY wpos.IdWareneingangPosition;

-- ════════════════════════════════════════════════════════════════════════════
-- Q5: SUM comparison — which level has the real received quantity?
-- ════════════════════════════════════════════════════════════════════════════

SELECT 'Header: T_Wareneingaenge' AS Source,
       COUNT(*) AS Cnt,
       SUM(w.SollMenge) AS TotalSollMenge,
       SUM(w.IstMenge) AS TotalIstMenge,
       SUM(w.SollMengeVPK) AS TotalSollMengeVPK,
       SUM(w.IstMengeVPK) AS TotalIstMengeVPK
FROM [dbo].[T_Wareneingaenge] w
WHERE w.IdBestellPosition IN (
    SELECT bp.IdBestellPosition
    FROM [dbo].[T_Bestellpositionen] bp
    WHERE bp.IdBestellung = 3579
)

UNION ALL

SELECT 'Planning: T_WareneingangPlanungen' AS Source,
       COUNT(*) AS Cnt,
       SUM(wep.Menge) AS TotalSollMenge,
       SUM(wep.EntladeMenge) AS TotalIstMenge,
       SUM(wep.MengeVPK) AS TotalSollMengeVPK,
       SUM(wep.EntladeMengeVPK) AS TotalIstMengeVPK
FROM [dbo].[T_WareneingangPlanungen] wep
WHERE wep.IdWareneingang IN (
    SELECT w.IdWareneingang
    FROM [dbo].[T_Wareneingaenge] w
    WHERE w.IdBestellPosition IN (
        SELECT bp.IdBestellPosition
        FROM [dbo].[T_Bestellpositionen] bp
        WHERE bp.IdBestellung = 3579
    )
)

UNION ALL

SELECT 'Positions: T_WareneingangPositionen' AS Source,
       COUNT(*) AS Cnt,
       NULL AS TotalSollMenge,
       SUM(wpos.Menge) AS TotalIstMenge,
       NULL AS TotalSollMengeVPK,
       SUM(wpos.MengeVPK) AS TotalIstMengeVPK
FROM [dbo].[T_WareneingangPositionen] wpos
WHERE wpos.IdWareneingangPlanung IN (
    SELECT wep.IdWareneingangPlanung
    FROM [dbo].[T_WareneingangPlanungen] wep
    WHERE wep.IdWareneingang IN (
        SELECT w.IdWareneingang
        FROM [dbo].[T_Wareneingaenge] w
        WHERE w.IdBestellPosition IN (
            SELECT bp.IdBestellPosition
            FROM [dbo].[T_Bestellpositionen] bp
            WHERE bp.IdBestellung = 3579
        )
    )
);

-- ════════════════════════════════════════════════════════════════════════════
-- Q6: Also check T_Bestellpositionen.LieferMenge as potential delivered qty
-- ════════════════════════════════════════════════════════════════════════════

SELECT 'Position: T_Bestellpositionen' AS Source,
       COUNT(*) AS Cnt,
       SUM(bp.BestellMenge) AS TotalOrderedQty,
       NULL AS TotalDeliveredQty,
       SUM(bp.BestellMengeVPK) AS TotalOrderedVPK,
       NULL AS TotalDeliveredVPK
FROM [dbo].[T_Bestellpositionen] bp
WHERE bp.IdBestellung = 3579;

-- ════════════════════════════════════════════════════════════════════════════
-- Q7: Cross-check with a second PO (VIANA1 #3425 for comparison)
-- ════════════════════════════════════════════════════════════════════════════

SELECT 'PO3425 Header: T_Wareneingaenge' AS Source,
       COUNT(*) AS Cnt,
       SUM(w.SollMenge) AS TotalSollMenge,
       SUM(w.IstMenge) AS TotalIstMenge
FROM [dbo].[T_Wareneingaenge] w
WHERE w.IdBestellPosition IN (
    SELECT bp.IdBestellPosition
    FROM [dbo].[T_Bestellpositionen] bp
    WHERE bp.IdBestellung = 3425
)

UNION ALL

SELECT 'PO3425 Planning: T_WareneingangPlanungen' AS Source,
       COUNT(*) AS Cnt,
       SUM(wep.Menge) AS TotalSollMenge,
       SUM(wep.EntladeMenge) AS TotalIstMenge
FROM [dbo].[T_WareneingangPlanungen] wep
WHERE wep.IdWareneingang IN (
    SELECT w.IdWareneingang
    FROM [dbo].[T_Wareneingaenge] w
    WHERE w.IdBestellPosition IN (
        SELECT bp.IdBestellPosition
        FROM [dbo].[T_Bestellpositionen] bp
        WHERE bp.IdBestellung = 3425
    )
)

UNION ALL

SELECT 'PO3425 Positions: T_WareneingangPositionen' AS Source,
       COUNT(*) AS Cnt,
       NULL AS TotalSollMenge,
       SUM(wpos.Menge) AS TotalIstMenge
FROM [dbo].[T_WareneingangPositionen] wpos
WHERE wpos.IdWareneingangPlanung IN (
    SELECT wep.IdWareneingangPlanung
    FROM [dbo].[T_WareneingangPlanungen] wep
    WHERE wep.IdWareneingang IN (
        SELECT w.IdWareneingang
        FROM [dbo].[T_Wareneingaenge] w
        WHERE w.IdBestellPosition IN (
            SELECT bp.IdBestellPosition
            FROM [dbo].[T_Bestellpositionen] bp
            WHERE bp.IdBestellung = 3425
        )
    )
)

UNION ALL

SELECT 'PO3425 Position: T_Bestellpositionen' AS Source,
       COUNT(*) AS Cnt,
       SUM(bp.BestellMenge) AS TotalOrderedQty,
       NULL AS TotalDeliveredQty
FROM [dbo].[T_Bestellpositionen] bp
WHERE bp.IdBestellung = 3425;

-- ============================================================================
-- END OF SCRIPT 16 — Receipt Quantity Correction Investigation
-- ============================================================================
