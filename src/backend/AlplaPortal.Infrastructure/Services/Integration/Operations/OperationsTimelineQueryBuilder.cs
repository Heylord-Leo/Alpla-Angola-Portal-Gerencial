namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Builds parameterized SQL queries for AlplaPROD transfer timeline events.
///
/// Two query models:
///   • Standard (Viana 1, Viana 2): 10-event UNION ALL
///   • Inhouse  (Viana 3):          7-event UNION ALL
///
/// All queries are READ-ONLY (SELECT only). Parameters: @IdBestellung.
/// Internal variable @IdJournal is derived via a prefix SELECT.
///
/// Source of truth: docs/sql-discovery/operations/OPERATIONS_TIMELINE_QUERY_PROTOTYPES.md
/// </summary>
public static class OperationsTimelineQueryBuilder
{
    /// <summary>
    /// SQL prefix that derives @IdJournal from the PO's most recent journal entry.
    /// Shared by both Standard and Inhouse queries.
    /// </summary>
    private const string JournalDerivation = @"
DECLARE @IdJournal INT;
SELECT TOP 1 @IdJournal = bj.IdJournal
FROM [dbo].[T_BestellungenJournal] bj
WHERE bj.IdBestellung = @IdBestellung
ORDER BY bj.Revision DESC;
";

    /// <summary>
    /// Simple existence check for the PO before running the full timeline.
    /// Returns 1 if the PO exists, 0 if not.
    /// </summary>
    public static string BuildExistenceCheckQuery()
    {
        return "SELECT COUNT(1) FROM [dbo].[T_Bestellungen] WHERE IdBestellung = @IdBestellung;";
    }

    /// <summary>
    /// Builds the Standard timeline query (10 events) for Viana 1 / Viana 2.
    ///
    /// Events:
    ///  1. PO_CREATED       (T_Bestellungen)
    ///  2. PO_REVISION       (T_BestellungenJournal)
    ///  3. EDI_CREATED       (T_EAIJournal)
    ///  4. EDI_EXPORTED      (T_EAIJournal)
    ///  5. EDI_SYNCED        (T_EAIJournalSynch)
    ///  6. CALLOFF_CREATED   (T_Abrufe)
    ///  7. LOADING_PLANNED   (T_LadePlanungen)
    ///  8. LOADING_ORDER     (T_LadeAuftraege)
    ///  9. GR_CREATED        (T_Wareneingaenge)
    /// 10. GR_COMPLETED      (T_Wareneingaenge, Status=21)
    /// </summary>
    public static string BuildStandardTimelineQuery()
    {
        return JournalDerivation + @"
-- Event 1: PO Created
SELECT
    10                          AS SortOrder,
    'PO_CREATED'                AS EventCode,
    N'Pedido de compra criado'  AS EventLabelPT,
    'T_Bestellungen'            AS SourceTable,
    b.Add_Date                  AS EventDate,
    b.Add_User                  AS EventUser,
    b.[Status]                  AS MainStatus,
    b.UebermittlungsStatus      AS SecondaryStatus,
    b.IdBestellung              AS IdBestellung,
    NULL                        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    CAST(b.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    b.Bemerkung                 AS Notes
FROM [dbo].[T_Bestellungen] b
WHERE b.IdBestellung = @IdBestellung

UNION ALL

-- Event 2: PO Revision
SELECT
    15                          AS SortOrder,
    'PO_REVISION'               AS EventCode,
    N'Revisão do pedido (Rev. ' + CAST(bj.Revision AS NVARCHAR(10)) + N')' AS EventLabelPT,
    'T_BestellungenJournal'     AS SourceTable,
    bj.Add_Date                 AS EventDate,
    bj.Add_User                 AS EventUser,
    bj.Revision                 AS MainStatus,
    NULL                        AS SecondaryStatus,
    bj.IdBestellung             AS IdBestellung,
    NULL                        AS IdBestellPosition,
    bj.IdJournal                AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    CAST(bj.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    NULL                        AS Notes
FROM [dbo].[T_BestellungenJournal] bj
WHERE bj.IdBestellung = @IdBestellung

UNION ALL

-- Event 3: EDI Created
SELECT
    20                          AS SortOrder,
    'EDI_CREATED'               AS EventCode,
    N'Documento EDI criado'     AS EventLabelPT,
    'T_EAIJournal'              AS SourceTable,
    j.JournalDatum              AS EventDate,
    j.Add_User                  AS EventUser,
    j.IdJournalStatus           AS MainStatus,
    j.IdJournalTyp              AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    j.IdJournal                 AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    j.JournalNummer             AS ReferenceNumber,
    j.JournalNummer             AS JournalNummer,
    NULL                        AS Quantity,
    j.Bemerkung                 AS Notes
FROM [dbo].[T_EAIJournal] j
WHERE j.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL

UNION ALL

-- Event 4: EDI Exported (uses JournalDatum, NOT Exportiert which is sentinel 1900-01-01)
SELECT
    25                          AS SortOrder,
    'EDI_EXPORTED'              AS EventCode,
    N'EDI exportado'            AS EventLabelPT,
    'T_EAIJournal'              AS SourceTable,
    j2.JournalDatum             AS EventDate,
    j2.Upd_User                 AS EventUser,
    j2.IdJournalStatus          AS MainStatus,
    NULL                        AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    j2.IdJournal                AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    j2.JournalNummer            AS ReferenceNumber,
    j2.JournalNummer            AS JournalNummer,
    NULL                        AS Quantity,
    NULL                        AS Notes
FROM [dbo].[T_EAIJournal] j2
WHERE j2.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL
  AND j2.IdJournalStatus IN (62, 64)

UNION ALL

-- Event 5: EDI Synced
SELECT
    30                          AS SortOrder,
    'EDI_SYNCED'                AS EventCode,
    N'Sincronização EDI'        AS EventLabelPT,
    'T_EAIJournalSynch'         AS SourceTable,
    js.Upd_Date                 AS EventDate,
    js.Add_User                 AS EventUser,
    js.[Status]                 AS MainStatus,
    js.Aktion                   AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    js.IdJournal                AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    NULL                        AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    js.Bemerkung                AS Notes
FROM [dbo].[T_EAIJournalSynch] js
WHERE js.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL

UNION ALL

-- Event 6: Call-off Created
SELECT
    40                          AS SortOrder,
    'CALLOFF_CREATED'           AS EventCode,
    N'Abruf criado'             AS EventLabelPT,
    'T_Abrufe'                  AS SourceTable,
    a.AbrufDatum                AS EventDate,
    a.Add_User                  AS EventUser,
    a.AbrufStatus               AS MainStatus,
    a.LadeStatus                AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    NULL                        AS IdJournal,
    a.IdAuftragsAbruf           AS IdAuftragsAbruf,
    a.IdAuftragsAbruf           AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    NULL                        AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    a.Bemerkung                 AS Notes
FROM [dbo].[T_Abrufe] a
WHERE a.IdAuftragsAbruf IN (
    SELECT DISTINCT ejp.IdAuftragsAbruf
    FROM [dbo].[T_EAIJournalPosition] ejp
    WHERE ejp.IdBestellung = @IdBestellung
      AND ejp.IdAuftragsAbruf IS NOT NULL
)

UNION ALL

-- Event 7: Loading Planned
SELECT
    50                          AS SortOrder,
    'LOADING_PLANNED'           AS EventCode,
    N'Carregamento planejado'   AS EventLabelPT,
    'T_LadePlanungen'           AS SourceTable,
    lp.Add_Date                 AS EventDate,
    lp.Add_User                 AS EventUser,
    lp.LadeStatus               AS MainStatus,
    lp.[Status]                 AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    lp.IdAbrufe                 AS IdAbrufe,
    lp.IdLadePlanung            AS IdLadePlanung,
    lp.IdLadeAuftrag            AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    NULL                        AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    lp.Bemerkung                AS Notes
FROM [dbo].[T_LadePlanungen] lp
WHERE lp.IdAbrufe IN (
    SELECT DISTINCT ejp2.IdAuftragsAbruf
    FROM [dbo].[T_EAIJournalPosition] ejp2
    WHERE ejp2.IdBestellung = @IdBestellung
      AND ejp2.IdAuftragsAbruf IS NOT NULL
)

UNION ALL

-- Event 8: Loading Order
SELECT
    60                          AS SortOrder,
    'LOADING_ORDER'             AS EventCode,
    N'Ordem de carregamento'    AS EventLabelPT,
    'T_LadeAuftraege'           AS SourceTable,
    la.LadeDatum                AS EventDate,
    la.Add_User                 AS EventUser,
    la.[Status]                 AS MainStatus,
    la.LadeStatus               AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    la.IdLadeAuftrag            AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    NULL                        AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    la.Bemerkung                AS Notes
FROM [dbo].[T_LadeAuftraege] la
WHERE la.IdLadeAuftrag IN (
    SELECT DISTINCT lp2.IdLadeAuftrag
    FROM [dbo].[T_LadePlanungen] lp2
    WHERE lp2.IdAbrufe IN (
        SELECT DISTINCT ejp3.IdAuftragsAbruf
        FROM [dbo].[T_EAIJournalPosition] ejp3
        WHERE ejp3.IdBestellung = @IdBestellung
          AND ejp3.IdAuftragsAbruf IS NOT NULL
    )
    AND lp2.IdLadeAuftrag IS NOT NULL
)

UNION ALL

-- Event 9: Goods Receipt Created
SELECT
    80                          AS SortOrder,
    'GR_CREATED'                AS EventCode,
    N'Recebimento criado'       AS EventLabelPT,
    'T_Wareneingaenge'          AS SourceTable,
    w.Datum                     AS EventDate,
    w.Add_User                  AS EventUser,
    w.[Status]                  AS MainStatus,
    w.AbgleichStatus            AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    w.IdBestellPosition         AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    w.IdWareneingang            AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    NULL                        AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    w.Bemerkung                 AS Notes
FROM [dbo].[T_Wareneingaenge] w
WHERE w.IdBestellPosition IN (
    SELECT bp4.IdBestellPosition
    FROM [dbo].[T_Bestellpositionen] bp4
    WHERE bp4.IdBestellung = @IdBestellung
)

UNION ALL

-- Event 10: Goods Receipt Completed (primary condition: Status = 21)
SELECT
    90                          AS SortOrder,
    'GR_COMPLETED'              AS EventCode,
    N'Recebimento concluído'    AS EventLabelPT,
    'T_Wareneingaenge'          AS SourceTable,
    w2.Upd_Date                 AS EventDate,
    w2.Upd_User                 AS EventUser,
    w2.[Status]                 AS MainStatus,
    w2.AbgleichStatus           AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    w2.IdBestellPosition        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    w2.IdWareneingang           AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    NULL                        AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    w2.Bemerkung                AS Notes
FROM [dbo].[T_Wareneingaenge] w2
WHERE w2.IdBestellPosition IN (
    SELECT bp5.IdBestellPosition
    FROM [dbo].[T_Bestellpositionen] bp5
    WHERE bp5.IdBestellung = @IdBestellung
)
AND w2.[Status] = 21

ORDER BY SortOrder ASC, EventDate ASC;
";
    }

    /// <summary>
    /// Builds the Inhouse timeline query (7 events) for Viana 3.
    ///
    /// Events:
    ///  1. PO_CREATED         (T_Bestellungen)
    ///  2. PO_REVISION         (T_BestellungenJournal)
    ///  3. EDI_CREATED         (T_EAIJournal)
    ///  4. EDI_EXPORTED        (T_EAIJournal)
    ///  5. INHOUSE_DELIVERY    (T_InhouseLieferungen)
    ///  6. GR_CREATED          (T_Wareneingaenge)
    ///  7. GR_COMPLETED        (T_Wareneingaenge, Status=21)
    /// </summary>
    public static string BuildInhouseTimelineQuery()
    {
        return JournalDerivation + @"
-- Event 1: PO Created
SELECT
    10                          AS SortOrder,
    'PO_CREATED'                AS EventCode,
    N'Pedido de compra criado'  AS EventLabelPT,
    'T_Bestellungen'            AS SourceTable,
    b.Add_Date                  AS EventDate,
    b.Add_User                  AS EventUser,
    b.[Status]                  AS MainStatus,
    b.UebermittlungsStatus      AS SecondaryStatus,
    b.IdBestellung              AS IdBestellung,
    NULL                        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    CAST(b.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    b.Bemerkung                 AS Notes
FROM [dbo].[T_Bestellungen] b
WHERE b.IdBestellung = @IdBestellung

UNION ALL

-- Event 2: PO Revision
SELECT
    15                          AS SortOrder,
    'PO_REVISION'               AS EventCode,
    N'Revisão do pedido (Rev. ' + CAST(bj.Revision AS NVARCHAR(10)) + N')' AS EventLabelPT,
    'T_BestellungenJournal'     AS SourceTable,
    bj.Add_Date                 AS EventDate,
    bj.Add_User                 AS EventUser,
    bj.Revision                 AS MainStatus,
    NULL                        AS SecondaryStatus,
    bj.IdBestellung             AS IdBestellung,
    NULL                        AS IdBestellPosition,
    bj.IdJournal                AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    CAST(bj.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    NULL                        AS Notes
FROM [dbo].[T_BestellungenJournal] bj
WHERE bj.IdBestellung = @IdBestellung

UNION ALL

-- Event 3: EDI Created
SELECT
    20                          AS SortOrder,
    'EDI_CREATED'               AS EventCode,
    N'Documento EDI criado'     AS EventLabelPT,
    'T_EAIJournal'              AS SourceTable,
    j.JournalDatum              AS EventDate,
    j.Add_User                  AS EventUser,
    j.IdJournalStatus           AS MainStatus,
    j.IdJournalTyp              AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    j.IdJournal                 AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    j.JournalNummer             AS ReferenceNumber,
    j.JournalNummer             AS JournalNummer,
    NULL                        AS Quantity,
    j.Bemerkung                 AS Notes
FROM [dbo].[T_EAIJournal] j
WHERE j.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL

UNION ALL

-- Event 4: EDI Exported (uses JournalDatum, NOT Exportiert)
SELECT
    25                          AS SortOrder,
    'EDI_EXPORTED'              AS EventCode,
    N'EDI exportado'            AS EventLabelPT,
    'T_EAIJournal'              AS SourceTable,
    j2.JournalDatum             AS EventDate,
    j2.Upd_User                 AS EventUser,
    j2.IdJournalStatus          AS MainStatus,
    NULL                        AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    j2.IdJournal                AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    j2.JournalNummer            AS ReferenceNumber,
    j2.JournalNummer            AS JournalNummer,
    NULL                        AS Quantity,
    NULL                        AS Notes
FROM [dbo].[T_EAIJournal] j2
WHERE j2.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL
  AND j2.IdJournalStatus IN (62, 64)

UNION ALL

-- Event 5: Inhouse Delivery
SELECT
    50                          AS SortOrder,
    'INHOUSE_DELIVERY'          AS EventCode,
    N'Entrega interna criada'   AS EventLabelPT,
    'T_InhouseLieferungen'      AS SourceTable,
    ih.LieferscheinDatum        AS EventDate,
    ih.Add_User                 AS EventUser,
    NULL                        AS MainStatus,
    NULL                        AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    ih.IdJournal                AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    ih.IdInhouseLieferung       AS IdInhouseLieferung,
    NULL                        AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    NULL                        AS Notes
FROM [dbo].[T_InhouseLieferungen] ih
WHERE ih.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL

UNION ALL

-- Event 6: Goods Receipt Created
SELECT
    80                          AS SortOrder,
    'GR_CREATED'                AS EventCode,
    N'Recebimento criado'       AS EventLabelPT,
    'T_Wareneingaenge'          AS SourceTable,
    w.Datum                     AS EventDate,
    w.Add_User                  AS EventUser,
    w.[Status]                  AS MainStatus,
    w.AbgleichStatus            AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    w.IdBestellPosition         AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    w.IdWareneingang            AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    NULL                        AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    w.Bemerkung                 AS Notes
FROM [dbo].[T_Wareneingaenge] w
WHERE w.IdBestellPosition IN (
    SELECT bp.IdBestellPosition
    FROM [dbo].[T_Bestellpositionen] bp
    WHERE bp.IdBestellung = @IdBestellung
)

UNION ALL

-- Event 7: Goods Receipt Completed (primary condition: Status = 21)
SELECT
    90                          AS SortOrder,
    'GR_COMPLETED'              AS EventCode,
    N'Recebimento concluído'    AS EventLabelPT,
    'T_Wareneingaenge'          AS SourceTable,
    w2.Upd_Date                 AS EventDate,
    w2.Upd_User                 AS EventUser,
    w2.[Status]                 AS MainStatus,
    w2.AbgleichStatus           AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    w2.IdBestellPosition        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdAbrufe,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    w2.IdWareneingang           AS IdWareneingang,
    NULL                        AS IdInhouseLieferung,
    NULL                        AS ReferenceNumber,
    NULL                        AS JournalNummer,
    NULL                        AS Quantity,
    w2.Bemerkung                AS Notes
FROM [dbo].[T_Wareneingaenge] w2
WHERE w2.IdBestellPosition IN (
    SELECT bp2.IdBestellPosition
    FROM [dbo].[T_Bestellpositionen] bp2
    WHERE bp2.IdBestellung = @IdBestellung
)
AND w2.[Status] = 21

ORDER BY SortOrder ASC, EventDate ASC;
";
    }
}
