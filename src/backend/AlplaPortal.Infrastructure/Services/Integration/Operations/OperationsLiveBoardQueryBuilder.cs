namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Builds parameterized SQL queries for the Operations Live Transfer Board.
///
/// Returns active (non-terminal) POs plus recently-completed POs
/// in a single optimized query with pre-joined material, quantity, and
/// timeline stage data — designed for TV refresh every 60 seconds.
///
/// Key design decisions:
///   • Single query per plant (no N+1)
///   • Uses OUTER APPLY TOP 1 for representative material
///   • Aggregate subqueries for goods receipt via T_WareneingangPlanungen.EntladeMenge
///     (T_Wareneingaenge.IstMenge is ALWAYS 0 — v2.174.0 correction)
///   • Detects timeline stage via joined entity existence checks
///   • Returns up to maxInbound + maxOutbound rows
///   • All queries are READ-ONLY. No writes are ever performed.
///
/// Parameters: @ActiveStatuses (1,2,5,6), @CompletedStatuses (7,8),
///             @CompletedCutoff (DateTime), @MaxRows (int)
///
/// Design reference: docs/OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md §9–§10
/// </summary>
public static class OperationsLiveBoardQueryBuilder
{
    /// <summary>
    /// Builds the Live Board query for Standard pipeline plants (VIANA1, VIANA2).
    ///
    /// Returns active POs + recently-completed POs with:
    ///   • PO header (status, dates)
    ///   • Representative material name (OUTER APPLY TOP 1)
    ///   • Ordered quantity (from first position)
    ///   • Received quantity (SUM of EntladeMenge via T_WareneingangPlanungen)
    ///   • Timeline stage indicators:
    ///     - HasEdiSync (T_EAIJournalSynch exists with Status=1)
    ///     - HasLoadingOrder (T_LadeAuftraege exists via bridge)
    ///     - HasGoodsReceipt (T_Wareneingaenge exists)
    ///     - GrMaxStatus (max receipt status)
    ///   • Journal number
    ///   • Last event date (MAX of available dates)
    /// </summary>
    public static string BuildStandardLiveBoardQuery()
    {
        return @"
-- ══════════════════════════════════════════════════════════════
-- Live Board Query — Standard Pipeline (Viana 1 / Viana 2)
-- Optimized single-pass query for TV refresh.
-- ══════════════════════════════════════════════════════════════

DECLARE @IdJournalLookup TABLE (IdBestellung INT, IdJournal INT);

-- Pre-resolve IdJournal for all matching POs via latest journal revision
INSERT INTO @IdJournalLookup (IdBestellung, IdJournal)
SELECT bj.IdBestellung, bj.IdJournal
FROM (
    SELECT bj2.IdBestellung, bj2.IdJournal,
           ROW_NUMBER() OVER (PARTITION BY bj2.IdBestellung ORDER BY bj2.Revision DESC) AS rn
    FROM [dbo].[T_BestellungenJournal] bj2
    WHERE bj2.IdBestellung IN (
        SELECT b0.IdBestellung FROM [dbo].[T_Bestellungen] b0
        WHERE (b0.[Status] IN (1,2,5,6))
           OR (b0.[Status] IN (7,8) AND b0.Upd_Date >= @CompletedCutoff)
    )
) bj
WHERE bj.rn = 1;

SELECT TOP (@MaxRows)
    b.IdBestellung,
    b.[Status]              AS MainStatus,
    b.Add_Date              AS CreatedDate,
    b.Upd_Date              AS UpdatedDate,

    -- Journal
    jl.IdJournal,
    j.JournalNummer,

    -- Material (representative)
    pos.MaterialName,

    -- Quantity
    pos.BestellMenge        AS OrderedQuantity,
    gr.TotalReceivedQty     AS ReceivedQuantity,

    -- Timeline stage indicators
    CASE WHEN sync.SyncCount > 0 THEN 1 ELSE 0 END AS HasEdiSync,
    CASE WHEN ld.LoadingCount > 0 THEN 1 ELSE 0 END AS HasLoadingOrder,
    CASE WHEN gr.ReceiptCount > 0 THEN 1 ELSE 0 END AS HasGoodsReceipt,
    gr.GrMaxStatus,

    -- Last event date (best effort: max of available dates)
    (
        SELECT MAX(d) FROM (VALUES
            (b.Add_Date),
            (b.Upd_Date),
            (sync.LastSyncDate),
            (ld.LastLoadDate),
            (gr.LastReceiptDate)
        ) AS dates(d)
    ) AS LastEventAt

FROM [dbo].[T_Bestellungen] b

-- Journal lookup
LEFT JOIN @IdJournalLookup jl ON jl.IdBestellung = b.IdBestellung
LEFT JOIN [dbo].[T_EAIJournal] j ON j.IdJournal = jl.IdJournal

-- Representative position (first by IdBestellPosition)
OUTER APPLY (
    SELECT TOP 1
        av.Bezeichnung  AS MaterialName,
        bp.BestellMenge
    FROM [dbo].[T_Bestellpositionen] bp
    LEFT JOIN [dbo].[T_Artikelvarianten] av
        ON av.IdArtikelVarianten = bp.IdArtikelVarianten
    WHERE bp.IdBestellung = b.IdBestellung
    ORDER BY bp.IdBestellPosition ASC
) pos

-- EDI Sync indicator
OUTER APPLY (
    SELECT
        COUNT(*) AS SyncCount,
        MAX(s.Upd_Date) AS LastSyncDate
    FROM [dbo].[T_EAIJournalSynch] s
    WHERE s.IdJournal = jl.IdJournal
      AND jl.IdJournal IS NOT NULL
      AND s.[Status] = 1
) sync

-- Loading indicator (via EAIJournalPosition → Abrufe → LadePlanungen → LadeAuftraege)
OUTER APPLY (
    SELECT
        COUNT(*) AS LoadingCount,
        MAX(la.LadeDatum) AS LastLoadDate
    FROM [dbo].[T_EAIJournalPosition] ejp
    INNER JOIN [dbo].[T_Abrufe] a ON a.IdAuftragsAbruf = ejp.IdAuftragsAbruf
    INNER JOIN [dbo].[T_LadePlanungen] lp ON lp.IdAbrufe = a.IdAuftragsAbruf
    INNER JOIN [dbo].[T_LadeAuftraege] la ON la.IdLadeAuftrag = lp.IdLadeAuftrag
    WHERE ejp.IdBestellung = b.IdBestellung
      AND ejp.IdAuftragsAbruf IS NOT NULL
      AND lp.IdLadeAuftrag IS NOT NULL
) ld

-- Goods receipt aggregate
OUTER APPLY (
    SELECT
        COUNT(*)              AS ReceiptCount,
        MAX(w.[Status])       AS GrMaxStatus,
        MAX(w.Datum)          AS LastReceiptDate,
        (
            SELECT SUM(CASE WHEN wp.EntladeMenge IS NOT NULL AND wp.EntladeMenge > 0
                            THEN wp.EntladeMenge ELSE 0 END)
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
        ) AS TotalReceivedQty
    FROM [dbo].[T_Wareneingaenge] w
    WHERE w.IdBestellPosition IN (
        SELECT bp2.IdBestellPosition
        FROM [dbo].[T_Bestellpositionen] bp2
        WHERE bp2.IdBestellung = b.IdBestellung
    )
    HAVING COUNT(*) > 0
) gr

WHERE (b.[Status] IN (1,2,5,6))
   OR (b.[Status] IN (7,8) AND b.Upd_Date >= @CompletedCutoff)

ORDER BY
    -- Priority: active before completed
    CASE WHEN b.[Status] IN (7,8) THEN 1 ELSE 0 END ASC,
    -- Then by most recent activity
    b.Upd_Date DESC;
";
    }

    /// <summary>
    /// Builds the Live Board query for Inhouse pipeline plants (VIANA3).
    ///
    /// Same structure as Standard but uses T_InhouseLieferungen instead of
    /// the loading chain (T_Abrufe → T_LadePlanungen → T_LadeAuftraege).
    /// </summary>
    public static string BuildInhouseLiveBoardQuery()
    {
        return @"
-- ══════════════════════════════════════════════════════════════
-- Live Board Query — Inhouse Pipeline (Viana 3)
-- Optimized single-pass query for TV refresh.
-- ══════════════════════════════════════════════════════════════

DECLARE @IdJournalLookup TABLE (IdBestellung INT, IdJournal INT);

INSERT INTO @IdJournalLookup (IdBestellung, IdJournal)
SELECT bj.IdBestellung, bj.IdJournal
FROM (
    SELECT bj2.IdBestellung, bj2.IdJournal,
           ROW_NUMBER() OVER (PARTITION BY bj2.IdBestellung ORDER BY bj2.Revision DESC) AS rn
    FROM [dbo].[T_BestellungenJournal] bj2
    WHERE bj2.IdBestellung IN (
        SELECT b0.IdBestellung FROM [dbo].[T_Bestellungen] b0
        WHERE (b0.[Status] IN (1,2,5,6))
           OR (b0.[Status] IN (7,8) AND b0.Upd_Date >= @CompletedCutoff)
    )
) bj
WHERE bj.rn = 1;

SELECT TOP (@MaxRows)
    b.IdBestellung,
    b.[Status]              AS MainStatus,
    b.Add_Date              AS CreatedDate,
    b.Upd_Date              AS UpdatedDate,

    -- Journal
    jl.IdJournal,
    j.JournalNummer,

    -- Material (representative)
    pos.MaterialName,

    -- Quantity
    pos.BestellMenge        AS OrderedQuantity,
    gr.TotalReceivedQty     AS ReceivedQuantity,

    -- Timeline stage indicators
    CASE WHEN sync.SyncCount > 0 THEN 1 ELSE 0 END AS HasEdiSync,
    CASE WHEN ih.InhouseCount > 0 THEN 1 ELSE 0 END AS HasLoadingOrder,
    CASE WHEN gr.ReceiptCount > 0 THEN 1 ELSE 0 END AS HasGoodsReceipt,
    gr.GrMaxStatus,

    -- Last event date
    (
        SELECT MAX(d) FROM (VALUES
            (b.Add_Date),
            (b.Upd_Date),
            (sync.LastSyncDate),
            (ih.LastInhouseDate),
            (gr.LastReceiptDate)
        ) AS dates(d)
    ) AS LastEventAt

FROM [dbo].[T_Bestellungen] b

LEFT JOIN @IdJournalLookup jl ON jl.IdBestellung = b.IdBestellung
LEFT JOIN [dbo].[T_EAIJournal] j ON j.IdJournal = jl.IdJournal

-- Representative position
OUTER APPLY (
    SELECT TOP 1
        av.Bezeichnung  AS MaterialName,
        bp.BestellMenge
    FROM [dbo].[T_Bestellpositionen] bp
    LEFT JOIN [dbo].[T_Artikelvarianten] av
        ON av.IdArtikelVarianten = bp.IdArtikelVarianten
    WHERE bp.IdBestellung = b.IdBestellung
    ORDER BY bp.IdBestellPosition ASC
) pos

-- EDI Sync indicator
OUTER APPLY (
    SELECT
        COUNT(*) AS SyncCount,
        MAX(s.Upd_Date) AS LastSyncDate
    FROM [dbo].[T_EAIJournalSynch] s
    WHERE s.IdJournal = jl.IdJournal
      AND jl.IdJournal IS NOT NULL
      AND s.[Status] = 1
) sync

-- Inhouse delivery indicator
OUTER APPLY (
    SELECT
        COUNT(*) AS InhouseCount,
        MAX(ihl.LieferscheinDatum) AS LastInhouseDate
    FROM [dbo].[T_InhouseLieferungen] ihl
    WHERE ihl.IdJournal = jl.IdJournal
      AND jl.IdJournal IS NOT NULL
) ih

-- Goods receipt aggregate
OUTER APPLY (
    SELECT
        COUNT(*)              AS ReceiptCount,
        MAX(w.[Status])       AS GrMaxStatus,
        MAX(w.Datum)          AS LastReceiptDate,
        (
            SELECT SUM(CASE WHEN wp.EntladeMenge IS NOT NULL AND wp.EntladeMenge > 0
                            THEN wp.EntladeMenge ELSE 0 END)
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
        ) AS TotalReceivedQty
    FROM [dbo].[T_Wareneingaenge] w
    WHERE w.IdBestellPosition IN (
        SELECT bp2.IdBestellPosition
        FROM [dbo].[T_Bestellpositionen] bp2
        WHERE bp2.IdBestellung = b.IdBestellung
    )
    HAVING COUNT(*) > 0
) gr

WHERE (b.[Status] IN (1,2,5,6))
   OR (b.[Status] IN (7,8) AND b.Upd_Date >= @CompletedCutoff)

ORDER BY
    CASE WHEN b.[Status] IN (7,8) THEN 1 ELSE 0 END ASC,
    b.Upd_Date DESC;
";
    }
}
