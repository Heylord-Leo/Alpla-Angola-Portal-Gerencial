namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Builds parameterized SQL queries for AlplaPROD transfer detail retrieval.
///
/// Two query variants:
///   • Standard (Viana 1, Viana 2): Full loading chain via T_EAIJournalPosition bridge
///   • Inhouse  (Viana 3):          T_InhouseLieferungen delivery path
///
/// Key design decisions:
///   • Returns a single-row result (not UNION ALL like timeline)
///   • Uses OUTER APPLY TOP 1 for representative position / loading rows
///   • Aggregate subqueries for goods receipt summary
///   • NULL returned for missing data, never misleading 0
///   • All queries are READ-ONLY (SELECT only). No writes are ever performed.
///
/// Phase 6.1 schema correction (v2.172.0):
///   • Color:            av.Farbbezeichnung  (confirmed Viana 1/2/3)
///   • PalletQuantity:   bp.BestellMengeVPK  (packaging unit qty, confirmed all plants)
///   • TruckNumber:      la.LKWNummer        (confirmed Viana 1/2)
///   • TruckDescription: la.LKWBezeichnung   (confirmed Viana 1/2)
///   • DeliveryNumber:   la.ExtLieferscheinNummer (external delivery note, confirmed Viana 1/2)
///   • ReceivedQuantity: SUM(wp.EntladeMenge) via T_WareneingangPlanungen (confirmed all plants)
///   •                   T_Wareneingaenge.IstMenge is ALWAYS 0 — NOT usable (v2.174.0 correction)
///   • DeliveryDate:     NULL — no equivalent column in T_LadeAuftraege (deferred)
///
/// Parameters: @IdBestellung
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §9 (Phase 6)
/// </summary>
public static class OperationsTransferDetailQueryBuilder
{
    /// <summary>
    /// Builds the Standard detail query for Viana 1 / Viana 2.
    ///
    /// Sources:
    ///   • T_Bestellungen — PO header
    ///   • T_BestellungenJournal — Latest journal (for IdJournal derivation)
    ///   • T_EAIJournal — Journal document info
    ///   • T_Bestellpositionen — Representative material/position (OUTER APPLY TOP 1)
    ///   • T_Artikelvarianten — Article details (LEFT JOIN from position)
    ///   • T_EAIJournalPosition → T_Abrufe → T_LadePlanungen → T_LadeAuftraege — Loading chain
    ///   • T_Wareneingaenge — Goods receipt aggregate
    ///
    /// Loading bridge: T_EAIJournalPosition.IdAuftragsAbruf → T_Abrufe →
    ///                 T_LadePlanungen.IdAbrufe → T_LadeAuftraege
    ///
    /// IMPORTANT: Does NOT assume T_Bestellpositionen has IdAuftrag.
    /// </summary>
    public static string BuildStandardDetailQuery()
    {
        return @"
-- Derive @IdJournal from the most recent journal entry
DECLARE @IdJournal INT;
SELECT TOP 1 @IdJournal = bj.IdJournal
FROM [dbo].[T_BestellungenJournal] bj
WHERE bj.IdBestellung = @IdBestellung
ORDER BY bj.Revision DESC;

SELECT
    -- ── Header ──
    b.IdBestellung,
    b.[Status]              AS MainStatus,
    b.Add_Date              AS CreatedDate,
    b.Upd_Date              AS UpdatedDate,
    b.Add_User              AS CreatedBy,
    b.Upd_User              AS UpdatedBy,
    b.Bemerkung             AS Notes,
    @IdJournal              AS IdJournal,
    j.JournalNummer,

    -- ── Material (representative from first position) ──
    pos.MaterialName,
    pos.ArticleAlias,
    pos.Color,
    pos.IdArtikelVarianten,
    pos.IdBestellPosition,

    -- ── Quantity (from representative position) ──
    pos.BestellMenge        AS OrderedQuantity,
    pos.PalettenMenge       AS PalletQuantity,

    -- ── Loading (OUTER APPLY TOP 1 through the validated bridge) ──
    ld.IdLadeAuftrag,
    ld.IdLadePlanung,
    ld.LadeDatum,
    ld.LoadingStatus,
    ld.TruckNumber,
    ld.TruckDescription,
    ld.DeliveryNumber,
    ld.DeliveryDate,
    ld.IdAuftragsAbruf,
    ld.IdAbrufe,

    -- ── Goods Receipt (aggregate) ──
    gr.ReceiptCount,
    gr.FirstReceiptId       AS IdWareneingang,
    gr.FirstReceiptDate     AS ReceiptDate,
    gr.LastReceiptDate,
    gr.LastStatus           AS ReceiptStatus,
    gr.TotalReceivedQty     AS ReceivedQuantity,
    gr.IsCompleted          AS ReceiptIsCompleted,

    -- ── Reference ──
    CAST(b.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber

FROM [dbo].[T_Bestellungen] b

-- Journal link
LEFT JOIN [dbo].[T_EAIJournal] j
    ON j.IdJournal = @IdJournal
    AND @IdJournal IS NOT NULL

-- Representative position (first by IdBestellPosition)
OUTER APPLY (
    SELECT TOP 1
        av.Bezeichnung       AS MaterialName,
        av.Alias              AS ArticleAlias,
        av.Farbbezeichnung    AS Color,
        bp.IdArtikelVarianten,
        bp.IdBestellPosition,
        bp.BestellMenge,
        bp.BestellMengeVPK    AS PalettenMenge
    FROM [dbo].[T_Bestellpositionen] bp
    LEFT JOIN [dbo].[T_Artikelvarianten] av
        ON av.IdArtikelVarianten = bp.IdArtikelVarianten
    WHERE bp.IdBestellung = b.IdBestellung
    ORDER BY bp.IdBestellPosition ASC
) pos

-- Loading chain via T_EAIJournalPosition bridge
-- T_EAIJournalPosition → T_Abrufe → T_LadePlanungen → T_LadeAuftraege
OUTER APPLY (
    SELECT TOP 1
        la.IdLadeAuftrag,
        lp.IdLadePlanung,
        la.LadeDatum,
        la.[Status]           AS LoadingStatus,
        la.LKWNummer          AS TruckNumber,
        la.LKWBezeichnung     AS TruckDescription,
        la.ExtLieferscheinNummer AS DeliveryNumber,
        NULL                  AS DeliveryDate,
        ejp.IdAuftragsAbruf,
        lp.IdAbrufe
    FROM [dbo].[T_EAIJournalPosition] ejp
    INNER JOIN [dbo].[T_Abrufe] a
        ON a.IdAuftragsAbruf = ejp.IdAuftragsAbruf
    INNER JOIN [dbo].[T_LadePlanungen] lp
        ON lp.IdAbrufe = a.IdAuftragsAbruf
    INNER JOIN [dbo].[T_LadeAuftraege] la
        ON la.IdLadeAuftrag = lp.IdLadeAuftrag
    WHERE ejp.IdBestellung = b.IdBestellung
      AND ejp.IdAuftragsAbruf IS NOT NULL
      AND lp.IdLadeAuftrag IS NOT NULL
    ORDER BY la.LadeDatum DESC
) ld

-- Goods receipt aggregate across all positions
-- NOTE: T_Wareneingaenge.IstMenge is ALWAYS 0 in AlplaPROD.
-- The actual received quantity is in T_WareneingangPlanungen.EntladeMenge.
-- Confirmed with PO #3579 (22000) and PO #3425 (261120) — both IstMenge=0.
OUTER APPLY (
    SELECT
        COUNT(*)                AS ReceiptCount,
        MIN(w.IdWareneingang)   AS FirstReceiptId,
        MIN(w.Datum)            AS FirstReceiptDate,
        MAX(w.Datum)            AS LastReceiptDate,
        MAX(w.[Status])         AS LastStatus,
        (
            SELECT SUM(CASE WHEN wp.EntladeMenge IS NOT NULL AND wp.EntladeMenge > 0 THEN wp.EntladeMenge ELSE 0 END)
            FROM [dbo].[T_WareneingangPlanungen] wp
            WHERE wp.IdWareneingang IN (
                SELECT w2.IdWareneingang
                FROM [dbo].[T_Wareneingaenge] w2
                WHERE w2.IdBestellPosition IN (
                    SELECT bp3.IdBestellPosition
                    FROM [dbo].[T_Bestellpositionen] bp3
                    WHERE bp3.IdBestellung = b.IdBestellung
                )
            )
        ) AS TotalReceivedQty,
        CASE WHEN MAX(w.[Status]) = 21 THEN 1 ELSE 0 END AS IsCompleted
    FROM [dbo].[T_Wareneingaenge] w
    WHERE w.IdBestellPosition IN (
        SELECT bp2.IdBestellPosition
        FROM [dbo].[T_Bestellpositionen] bp2
        WHERE bp2.IdBestellung = b.IdBestellung
    )
    HAVING COUNT(*) > 0
) gr

WHERE b.IdBestellung = @IdBestellung;
";
    }

    /// <summary>
    /// Builds the Inhouse detail query for Viana 3.
    ///
    /// Sources:
    ///   • T_Bestellungen — PO header
    ///   • T_BestellungenJournal — Latest journal (for IdJournal derivation)
    ///   • T_EAIJournal — Journal document info
    ///   • T_Bestellpositionen — Representative material/position (OUTER APPLY TOP 1)
    ///   • T_Artikelvarianten — Article details
    ///   • T_InhouseLieferungen — Inhouse delivery info (via IdJournal)
    ///   • T_Wareneingaenge — Goods receipt aggregate
    ///
    /// T_InhouseBewegungen is deferred — kept out of scope per user requirement.
    /// Barcode-level details are excluded.
    /// </summary>
    public static string BuildInhouseDetailQuery()
    {
        return @"
-- Derive @IdJournal from the most recent journal entry
DECLARE @IdJournal INT;
SELECT TOP 1 @IdJournal = bj.IdJournal
FROM [dbo].[T_BestellungenJournal] bj
WHERE bj.IdBestellung = @IdBestellung
ORDER BY bj.Revision DESC;

SELECT
    -- ── Header ──
    b.IdBestellung,
    b.[Status]              AS MainStatus,
    b.Add_Date              AS CreatedDate,
    b.Upd_Date              AS UpdatedDate,
    b.Add_User              AS CreatedBy,
    b.Upd_User              AS UpdatedBy,
    b.Bemerkung             AS Notes,
    @IdJournal              AS IdJournal,
    j.JournalNummer,

    -- ── Material (representative from first position) ──
    pos.MaterialName,
    pos.ArticleAlias,
    pos.Color,
    pos.IdArtikelVarianten,
    pos.IdBestellPosition,

    -- ── Quantity (from representative position) ──
    pos.BestellMenge        AS OrderedQuantity,
    pos.PalettenMenge       AS PalletQuantity,

    -- ── Inhouse Delivery (OUTER APPLY TOP 1 via IdJournal) ──
    ih.IdInhouseLieferung,
    ih.LieferscheinDatum,
    ih.ProdTag,
    ih.InhouseIdJournal,
    ih.InhouseJournalNummer,

    -- ── Goods Receipt (aggregate) ──
    gr.ReceiptCount,
    gr.FirstReceiptId       AS IdWareneingang,
    gr.FirstReceiptDate     AS ReceiptDate,
    gr.LastReceiptDate,
    gr.LastStatus           AS ReceiptStatus,
    gr.TotalReceivedQty     AS ReceivedQuantity,
    gr.IsCompleted          AS ReceiptIsCompleted,

    -- ── Reference ──
    CAST(b.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber

FROM [dbo].[T_Bestellungen] b

-- Journal link
LEFT JOIN [dbo].[T_EAIJournal] j
    ON j.IdJournal = @IdJournal
    AND @IdJournal IS NOT NULL

-- Representative position (first by IdBestellPosition)
OUTER APPLY (
    SELECT TOP 1
        av.Bezeichnung       AS MaterialName,
        av.Alias              AS ArticleAlias,
        av.Farbbezeichnung    AS Color,
        bp.IdArtikelVarianten,
        bp.IdBestellPosition,
        bp.BestellMenge,
        bp.BestellMengeVPK    AS PalettenMenge
    FROM [dbo].[T_Bestellpositionen] bp
    LEFT JOIN [dbo].[T_Artikelvarianten] av
        ON av.IdArtikelVarianten = bp.IdArtikelVarianten
    WHERE bp.IdBestellung = b.IdBestellung
    ORDER BY bp.IdBestellPosition ASC
) pos

-- Inhouse delivery via IdJournal
OUTER APPLY (
    SELECT TOP 1
        ihl.IdInhouseLieferung,
        ihl.LieferscheinDatum,
        ihl.ProdTag,
        ihl.IdJournal         AS InhouseIdJournal,
        j2.JournalNummer      AS InhouseJournalNummer
    FROM [dbo].[T_InhouseLieferungen] ihl
    LEFT JOIN [dbo].[T_EAIJournal] j2
        ON j2.IdJournal = ihl.IdJournal
    WHERE ihl.IdJournal = @IdJournal
      AND @IdJournal IS NOT NULL
    ORDER BY ihl.LieferscheinDatum DESC
) ih

-- Goods receipt aggregate across all positions
-- NOTE: T_Wareneingaenge.IstMenge is ALWAYS 0 in AlplaPROD.
-- The actual received quantity is in T_WareneingangPlanungen.EntladeMenge.
OUTER APPLY (
    SELECT
        COUNT(*)                AS ReceiptCount,
        MIN(w.IdWareneingang)   AS FirstReceiptId,
        MIN(w.Datum)            AS FirstReceiptDate,
        MAX(w.Datum)            AS LastReceiptDate,
        MAX(w.[Status])         AS LastStatus,
        (
            SELECT SUM(CASE WHEN wp.EntladeMenge IS NOT NULL AND wp.EntladeMenge > 0 THEN wp.EntladeMenge ELSE 0 END)
            FROM [dbo].[T_WareneingangPlanungen] wp
            WHERE wp.IdWareneingang IN (
                SELECT w2.IdWareneingang
                FROM [dbo].[T_Wareneingaenge] w2
                WHERE w2.IdBestellPosition IN (
                    SELECT bp3.IdBestellPosition
                    FROM [dbo].[T_Bestellpositionen] bp3
                    WHERE bp3.IdBestellung = b.IdBestellung
                )
            )
        ) AS TotalReceivedQty,
        CASE WHEN MAX(w.[Status]) = 21 THEN 1 ELSE 0 END AS IsCompleted
    FROM [dbo].[T_Wareneingaenge] w
    WHERE w.IdBestellPosition IN (
        SELECT bp2.IdBestellPosition
        FROM [dbo].[T_Bestellpositionen] bp2
        WHERE bp2.IdBestellung = b.IdBestellung
    )
    HAVING COUNT(*) > 0
) gr

WHERE b.IdBestellung = @IdBestellung;
";
    }
}
