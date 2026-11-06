using Microsoft.Data.SqlClient;

namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Builds the parameterised, READ-ONLY queries of the Viana 3 Production
/// Monitor. They are the four queries of the agreed reference
/// <c>AlplaPROD_aovia3_tv_preform_monitor_v5.sql</c>, rewritten for the
/// Portal connection (database selected by the connection, identifiers as
/// SQL parameters) and with the derived figures (deviation, unit estimates)
/// moved to the service so that every rule lives in one place.
///
/// Query 1 — current lot + previous production lots per machine (T_ProdPlanung,
///           AlplaREPORT report data, scanner postings, AlplaLABEL waste).
/// Query 2 — preform stock in Warehouse 3, agreed locations only (T_LagerPositionen).
/// Query 3 — open SYSTEM balance at the machine areas (diagnostic; not stock).
/// Query 4 — AlplaLABEL Waste without a valid production lot (diagnostic).
///
/// GETDATE() is the AlplaPROD server clock = Angola local time (UTC+1, no DST);
/// every datetime column is returned as recorded.
///
/// No INSERT / UPDATE / DELETE / MERGE is ever emitted.
/// </summary>
public static class Viana3ProductionMonitorQueryBuilder
{
    // ═══════════════════════════════════════════════════════════════════════
    // Query 1 — lots per machine
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds Query 1. When <paramref name="machineIds"/> is non-empty the machine
    /// list is fixed; otherwise it is discovered from lots ending within @LookbackDays.
    /// Parameters added to <paramref name="cmd"/>.
    /// </summary>
    public static string BuildLotsQuery(SqlCommand cmd, Viana3ProductionMonitorOptions o, IReadOnlyList<int> machineIds)
    {
        cmd.Parameters.AddWithValue("@LookbackDays", o.MachineLookbackDays);
        cmd.Parameters.AddWithValue("@PreviousLots", o.PreviousLots);
        cmd.Parameters.AddWithValue("@PreformTypeId", o.PreformArticleTypeId);
        cmd.Parameters.AddWithValue("@ScanReasonId", o.ScanReasonId);
        cmd.Parameters.AddWithValue("@ScanLocationId", o.ScanLocationId);
        cmd.Parameters.AddWithValue("@ScanConsumeOp", o.ScanConsumeOperation);
        cmd.Parameters.AddWithValue("@ScanUnconsumeOp", o.ScanUnconsumeOperation);
        cmd.Parameters.AddWithValue("@WasteReasonId", o.WasteReasonId);
        cmd.Parameters.AddWithValue("@WasteOriginatorId", o.WasteOriginatorId);
        cmd.Parameters.AddWithValue("@WasteBottlesArticleId", o.WasteBottlesArticleId);
        cmd.Parameters.AddWithValue("@WastePreformsArticleId", o.WastePreformsArticleId);

        string machinesCte;
        if (machineIds.Count > 0)
        {
            var values = new List<string>();
            for (var i = 0; i < machineIds.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@Mach{i}", machineIds[i]);
                values.Add($"(@Mach{i})");
            }
            machinesCte = $"machines AS (SELECT v.IdMaschine FROM (VALUES {string.Join(", ", values)}) v(IdMaschine))";
        }
        else
        {
            machinesCte = @"machines AS (
    SELECT DISTINCT pp.IdMaschine
    FROM [dbo].[T_ProdPlanung] pp
    WHERE pp.IdMaschine > 0 AND pp.PlanBis >= DATEADD(day, -@LookbackDays, @Now))";
        }

        return $@"
-- ══════════════════════════════════════════════════════════════
-- Viana 3 Production Monitor — Query 1: current lot + previous production lots per machine
-- READ-ONLY. Reference: AlplaPROD_aovia3_tv_preform_monitor_v5.sql
-- ══════════════════════════════════════════════════════════════
DECLARE @Now datetime = GETDATE();   -- AlplaPROD server clock = Angola local time

WITH {machinesCte},
lot AS (
    SELECT pp.IdProdPlanung, pp.IdMaschine, pp.IdArtikelvarianten, pp.IdHauptmaterial, pp.PlanMenge,
           pp.PlanVon, pp.PlanBis, pp.Status, av.Bezeichnung AS Article,
           CASE WHEN av.Bezeichnung LIKE '$Manut%'    THEN 'Maintenance'
                WHEN av.Bezeichnung LIKE '$No-Order%' THEN 'NoOrder'
                WHEN av.Bezeichnung LIKE '$%'         THEN 'Placeholder'
                ELSE 'Production' END AS LotKind
    FROM [dbo].[T_ProdPlanung] pp
    JOIN [dbo].[T_Artikelvarianten] av ON av.IdArtikelvarianten = pp.IdArtikelvarianten
    WHERE pp.IdMaschine > 0),
cur AS (
    SELECT m.IdMaschine, c.IdProdPlanung, c.PlanVon
    FROM machines m
    OUTER APPLY (SELECT TOP 1 l.IdProdPlanung, l.PlanVon FROM lot l
                 WHERE l.IdMaschine = m.IdMaschine AND l.PlanVon <= @Now AND l.PlanBis > @Now
                 ORDER BY l.PlanVon DESC) c),
slots AS (
    SELECT cur.IdMaschine, 0 AS SlotNo, cur.IdProdPlanung FROM cur
    UNION ALL
    SELECT p.IdMaschine, p.rn, p.IdProdPlanung
    FROM (SELECT l.IdMaschine, l.IdProdPlanung,
                 ROW_NUMBER() OVER (PARTITION BY l.IdMaschine ORDER BY l.PlanVon DESC) AS rn
          FROM lot l JOIN cur ON cur.IdMaschine = l.IdMaschine
          WHERE l.LotKind = 'Production'
            AND l.PlanVon < COALESCE(cur.PlanVon, @Now)
            AND (cur.IdProdPlanung IS NULL OR l.IdProdPlanung <> cur.IdProdPlanung)) p
    WHERE p.rn <= @PreviousLots)
SELECT  s.IdMaschine                                  AS Machine,
        mach.Bezeichnung                              AS MachineName,
        s.SlotNo                                      AS SlotNo,
        l.LotKind                                     AS LotKind,
        l.IdProdPlanung                               AS Lot,
        l.Status                                      AS LotStatus,
        l.Article                                     AS Bottle,
        bt.ArtikelGewicht                             AS BottleWeight_g,
        l.IdHauptmaterial                             AS PreformId,
        pf.Bezeichnung                                AS PlannedPreform,
        pf.ArtikelGewicht                             AS PreformWeight_g,
        l.PlanVon, l.PlanBis,
        CAST(l.PlanMenge AS decimal(18,3))            AS PlannedBottles,
        rep.ReportedBottles, rep.LastReportedDay, rep.ReportLastUpdated,
        scan.ScanCount, scan.ScannedPreforms, scan.LastScan,
        oth.OtherPreformScanCount, oth.OtherPreformsScanned,
        w.WasteBookings, w.RejectedBottles_kg, w.RejectedPreforms_kg, w.LastWasteBooking
FROM    slots s
LEFT JOIN lot l ON l.IdProdPlanung = s.IdProdPlanung
LEFT JOIN [dbo].[T_LagerAbteilungen] mach ON mach.IdLagerAbteilung = s.IdMaschine   -- machine areas = line names
LEFT JOIN [dbo].[T_Artikelvarianten] bt   ON bt.IdArtikelvarianten = l.IdArtikelvarianten
LEFT JOIN [dbo].[T_Artikelvarianten] pf   ON pf.IdArtikelvarianten = l.IdHauptmaterial
OUTER APPLY (SELECT CAST(SUM(d.ProdMenge) AS decimal(18,3)) AS ReportedBottles,
                    MAX(CASE WHEN d.ProdMenge <> 0 THEN d.ProduktionsTag END) AS LastReportedDay,
                    MAX(d.Upd_Date) AS ReportLastUpdated
             FROM [dbo].[T_ProduktionsRueckmeldungDaten] d
             JOIN [dbo].[T_ProduktionsRueckmeldungen] r ON r.IdProduktionsRueckmeldung = d.ProductionReport_ProductionReportId
             WHERE r.IdProdPlanung = l.IdProdPlanung) rep
OUTER APPLY (SELECT SUM(CASE WHEN b.Urheber = @ScanConsumeOp THEN 1 ELSE 0 END) AS ScanCount,
                    CAST(SUM(CASE WHEN b.Bewegung = 1 THEN b.Menge ELSE -b.Menge END) AS decimal(18,3)) AS ScannedPreforms,
                    MAX(b.BuchungsDatum) AS LastScan
             FROM [dbo].[T_ProduktionsBuchungen] b
             JOIN [dbo].[T_Konten] k ON k.IdKonto = b.IdKonto
             WHERE b.ProduktionsLos = l.IdProdPlanung AND b.IdBuchungsGrund = @ScanReasonId
               AND b.IdLagerAbteilung = @ScanLocationId AND b.Urheber IN (@ScanConsumeOp, @ScanUnconsumeOp)
               AND k.IdArtikelVarianten = l.IdHauptmaterial) scan
OUTER APPLY (SELECT SUM(CASE WHEN b.Urheber = @ScanConsumeOp THEN 1 ELSE 0 END) AS OtherPreformScanCount,
                    CAST(SUM(CASE WHEN b.Bewegung = 1 THEN b.Menge ELSE -b.Menge END) AS decimal(18,3)) AS OtherPreformsScanned
             FROM [dbo].[T_ProduktionsBuchungen] b
             JOIN [dbo].[T_Konten] k ON k.IdKonto = b.IdKonto
             WHERE b.ProduktionsLos = l.IdProdPlanung AND b.IdBuchungsGrund = @ScanReasonId
               AND b.IdLagerAbteilung = @ScanLocationId AND b.Urheber IN (@ScanConsumeOp, @ScanUnconsumeOp)
               AND k.IdArtikelVariantenTyp = @PreformTypeId AND k.IdArtikelVarianten <> l.IdHauptmaterial) oth
OUTER APPLY (SELECT COUNT(*) AS WasteBookings,
                    CAST(SUM(CASE WHEN k.IdArtikelVarianten = @WasteBottlesArticleId  THEN CASE WHEN b.Bewegung = 1 THEN b.Menge ELSE -b.Menge END END) AS decimal(18,3)) AS RejectedBottles_kg,
                    CAST(SUM(CASE WHEN k.IdArtikelVarianten = @WastePreformsArticleId THEN CASE WHEN b.Bewegung = 1 THEN b.Menge ELSE -b.Menge END END) AS decimal(18,3)) AS RejectedPreforms_kg,
                    MAX(b.Buchungsdatum) AS LastWasteBooking
             FROM [dbo].[T_LagerBuchungen] b
             JOIN [dbo].[T_Konten] k ON k.IdKonto = b.IdKonto
             WHERE b.ProduktionsLos = l.IdProdPlanung
               AND k.IdArtikelVarianten IN (@WasteBottlesArticleId, @WastePreformsArticleId)
               AND b.IdBuchungsGrund = @WasteReasonId AND b.Urheber = @WasteOriginatorId) w
ORDER BY s.IdMaschine, s.SlotNo;";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Query 2 — Warehouse 3 preform stock, agreed locations only
    // ═══════════════════════════════════════════════════════════════════════

    public static string BuildStockQuery(SqlCommand cmd, Viana3ProductionMonitorOptions o)
    {
        cmd.Parameters.AddWithValue("@PreformTypeId", o.PreformArticleTypeId);
        cmd.Parameters.AddWithValue("@WarehouseId", o.WarehouseId);
        cmd.Parameters.AddWithValue("@RetentionLocationId", o.RetentionLocationId);

        var locParams = new List<string>();
        for (var i = 0; i < o.LocationIds.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@Loc{i}", o.LocationIds[i]);
            locParams.Add($"@Loc{i}");
        }
        // An empty configuration must never widen the scope: fall back to an impossible id.
        var locList = locParams.Count > 0 ? string.Join(", ", locParams) : "-1";

        return $@"
-- ══════════════════════════════════════════════════════════════
-- Viana 3 Production Monitor — Query 2: preform stock, Warehouse 3 agreed locations only
-- READ-ONLY. Counts stored positions (rows of T_LagerPositionen) and units (EinlagerungsMenge).
-- ══════════════════════════════════════════════════════════════
SELECT  av.IdArtikelvarianten                   AS PreformId,
        av.Bezeichnung                          AS Preform,
        la.IdLagerAbteilung                     AS LocationId,
        la.Bezeichnung                          AS Location,
        COUNT(*)                                AS Positions,
        CAST(SUM(CASE WHEN la.IdLagerAbteilung <> @RetentionLocationId AND p.GesperrtAktiv = 0 THEN p.EinlagerungsMenge ELSE 0 END) AS decimal(18,3)) AS AvailableQty,
        SUM(CASE WHEN la.IdLagerAbteilung <> @RetentionLocationId AND p.GesperrtAktiv = 0 THEN 1 ELSE 0 END)                                          AS AvailablePositions,
        CAST(SUM(CASE WHEN la.IdLagerAbteilung =  @RetentionLocationId OR  p.GesperrtAktiv = 1 THEN p.EinlagerungsMenge ELSE 0 END) AS decimal(18,3)) AS BlockedQty,
        SUM(CASE WHEN la.IdLagerAbteilung =  @RetentionLocationId OR  p.GesperrtAktiv = 1 THEN 1 ELSE 0 END)                                          AS BlockedPositions,
        MAX(p.Upd_Date)                         AS LastChange
FROM    [dbo].[T_LagerPositionen] p
JOIN    [dbo].[T_Konten] k             ON k.IdKonto = p.IdKonto
JOIN    [dbo].[T_Artikelvarianten] av  ON av.IdArtikelvarianten = k.IdArtikelVarianten
JOIN    [dbo].[T_LagerAbteilungen] la  ON la.IdLagerAbteilung = p.IdLagerAbteilung
WHERE   k.IdArtikelVariantenTyp = @PreformTypeId
  AND   la.IdWarenLager = @WarehouseId
  AND   la.IdLagerAbteilung IN ({locList})
GROUP BY av.IdArtikelvarianten, av.Bezeichnung, la.IdLagerAbteilung, la.Bezeichnung
ORDER BY av.Bezeichnung, la.IdLagerAbteilung;";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Query 3 — open SYSTEM balance at the machine areas (diagnostic)
    // ═══════════════════════════════════════════════════════════════════════

    public static string BuildOpenSystemBalanceQuery(SqlCommand cmd, Viana3ProductionMonitorOptions o)
    {
        cmd.Parameters.AddWithValue("@PreformTypeId", o.PreformArticleTypeId);
        cmd.Parameters.AddWithValue("@AreaMin", o.MachineAreaMinId);
        cmd.Parameters.AddWithValue("@AreaMax", o.MachineAreaMaxId);

        return @"
-- ══════════════════════════════════════════════════════════════
-- Viana 3 Production Monitor — Query 3: open SYSTEM balance at a machine area (diagnostic)
-- Booking balance only — NOT physical stock and NOT Warehouse 3 stock.
-- Decision 2026-11: historical postings (lots 2687/2700) are NOT corrected; shown as recorded.
-- READ-ONLY.
-- ══════════════════════════════════════════════════════════════
DECLARE @Now datetime = GETDATE();

WITH bal AS (
    SELECT  b.IdLagerAbteilung, k.IdArtikelVarianten, b.Barcode,
            SUM(CASE WHEN b.Bewegung = 1 THEN b.Menge ELSE -b.Menge END) AS Qty,
            MAX(b.BuchungsDatum) AS LastMovement
    FROM    [dbo].[T_ProduktionsBuchungen] b
    JOIN    [dbo].[T_Konten] k ON k.IdKonto = b.IdKonto
    WHERE   k.IdArtikelVariantenTyp = @PreformTypeId
      AND   b.IdLagerAbteilung BETWEEN @AreaMin AND @AreaMax
    GROUP BY b.IdLagerAbteilung, k.IdArtikelVarianten, b.Barcode
    HAVING  ABS(SUM(CASE WHEN b.Bewegung = 1 THEN b.Menge ELSE -b.Menge END)) > 0.001)
SELECT  bal.IdLagerAbteilung                     AS Machine,
        la.Bezeichnung                           AS MachineName,
        av.IdArtikelvarianten                    AS PreformId,
        av.Bezeichnung                           AS Preform,
        bal.Barcode,
        lm.ProduktionsLos                        AS LastLot,
        lm.Urheber                               AS LastOperation,
        CAST(bal.Qty AS decimal(18,3))           AS OpenSystemBalance,
        bal.LastMovement,
        DATEDIFF(day, bal.LastMovement, @Now)    AS DaysSinceLastMovement
FROM    bal
JOIN    [dbo].[T_Artikelvarianten] av  ON av.IdArtikelvarianten = bal.IdArtikelVarianten
LEFT JOIN [dbo].[T_LagerAbteilungen] la ON la.IdLagerAbteilung = bal.IdLagerAbteilung
OUTER APPLY (SELECT TOP 1 b.ProduktionsLos, b.Urheber
             FROM [dbo].[T_ProduktionsBuchungen] b
             WHERE b.Barcode = bal.Barcode AND b.IdLagerAbteilung = bal.IdLagerAbteilung
             ORDER BY b.BuchungsDatum DESC, b.IdProduktionsBuchung DESC) lm
ORDER BY bal.IdLagerAbteilung, bal.LastMovement;";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Query 4 — AlplaLABEL Waste without a valid production lot (diagnostic)
    // ═══════════════════════════════════════════════════════════════════════

    public static string BuildOrphanWasteQuery(SqlCommand cmd, Viana3ProductionMonitorOptions o)
    {
        cmd.Parameters.AddWithValue("@WasteTypeId", o.WasteArticleTypeId);
        cmd.Parameters.AddWithValue("@WasteReasonId", o.WasteReasonId);
        cmd.Parameters.AddWithValue("@WasteOriginatorId", o.WasteOriginatorId);
        cmd.Parameters.AddWithValue("@WindowDays", o.OrphanWasteDays);

        return @"
-- ══════════════════════════════════════════════════════════════
-- Viana 3 Production Monitor — Query 4: AlplaLABEL Waste whose lot does not exist in the plan (diagnostic)
-- READ-ONLY. The booking user is deliberately NOT selected (no usernames on screen).
-- ══════════════════════════════════════════════════════════════
DECLARE @Now datetime = GETDATE();

SELECT  b.IdLagerBuchung                         AS BookingId,
        b.Buchungsdatum                          AS BookedAt,
        k.IdArtikelVarianten                     AS WasteArticleId,
        CAST(CASE WHEN b.Bewegung = 1 THEN b.Menge ELSE -b.Menge END AS decimal(18,3)) AS Waste_kg,
        b.ProduktionsLos                         AS LotOnBooking,
        b.IdMaschine                             AS Machine,
        b.UrheberReferenz                        AS BottleArticleId,
        bt.Bezeichnung                           AS BottleArticle,
        bt.ArtikelGewicht                        AS BottleWeight_g
FROM    [dbo].[T_LagerBuchungen] b
JOIN    [dbo].[T_Konten] k              ON k.IdKonto = b.IdKonto
LEFT JOIN [dbo].[T_ProdPlanung] pp      ON pp.IdProdPlanung = b.ProduktionsLos
LEFT JOIN [dbo].[T_Artikelvarianten] bt ON bt.IdArtikelvarianten = b.UrheberReferenz
WHERE   k.IdArtikelVariantenTyp = @WasteTypeId
  AND   b.IdBuchungsGrund = @WasteReasonId AND b.Urheber = @WasteOriginatorId
  AND   b.Buchungsdatum >= DATEADD(day, -@WindowDays, @Now)
  AND   pp.IdProdPlanung IS NULL
ORDER BY b.Buchungsdatum;";
    }
}
