# Operations Timeline — Prototype SQL Queries

> **Status**: DOCUMENTATION-ONLY PROTOTYPE  
> **Date**: 2026-05-29  
> **Updated**: 2026-05-31  
> **Strategy**: D — Hybrid Timeline (EAI Journal + Entity Snapshots)  
> **Parent Documents**:  
> - [OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md)  
> - [OPERATIONS_ENTITY_MAP.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_ENTITY_MAP.md)  
> - [OPERATIONS_MODULE_TECHNICAL_DESIGN.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md)

---

## 1. Purpose

This document contains **read-only prototype SQL queries** for the future Operations module timeline.

> [!IMPORTANT]
> These queries are **NOT application code**. They are **documentation-only prototypes** created to:
> 1. Validate that the timeline concept works against real AlplaPROD table structures
> 2. Confirm the `UNION ALL` approach produces a usable normalized timeline
> 3. Serve as a specification for future backend implementation
> 4. Document the exact fields, joins, and event ordering
>
> **Do not deploy these queries as backend endpoints.**

### What these queries demonstrate

- How to build a transfer timeline from `Add_Date`/`Upd_Date` entity snapshots
- How to incorporate `T_EAIJournal` events for EDI milestones
- How to handle the V1 vs V3 dual-model architecture with conditional pipeline selection
- The normalized output shape that the future Operations module will consume

---

## 2. Read-Only Safety Rules

> [!CAUTION]
> Every query in this document MUST comply with these rules:

| Rule | Constraint |
|------|-----------|
| ✅ Allowed | `SELECT` statements only |
| ❌ Forbidden | `INSERT`, `UPDATE`, `DELETE`, `MERGE` |
| ❌ Forbidden | `DROP`, `ALTER`, `CREATE`, `TRUNCATE` |
| ❌ Forbidden | `EXEC`, `EXECUTE`, `sp_executesql` |
| ❌ Forbidden | Database credentials, passwords, connection strings |
| ❌ Forbidden | `xp_cmdshell`, `OPENROWSET`, `OPENDATASOURCE` |
| ✅ Required | All queries must be executable in SSMS as read-only |
| ✅ Required | Use `DECLARE` variables as input parameters (no hardcoded IDs) |

---

## 3. Timeline Strategy Summary

### Strategy D — Hybrid Timeline: EAI Journal + Entity Snapshots

**Chosen because** (confirmed by Scripts 11 + 13):

| Factor | Finding |
|--------|---------|
| Status-history tables for logistics? | ❌ **None** — 28 history tables exist, but none for PO/Delivery/GR |
| SQL Server temporal tables? | ❌ **Zero** — `TemporalType = 0` for all tables |
| SQL Server Change Tracking? | ❌ **Disabled** — `ChangeTrackingEnabled = 0` for both V1 and V3 |
| CDC (Change Data Capture)? | ❌ **Not detected** |
| Old → New status columns? | ❌ **Zero** — no `*Vorher*`/`*Nachher*` columns exist |
| EAI Journal as event source? | ✅ **Yes** — `T_EAIJournal` (59 cols) with `JournalDatum`, `Exportiert`, `IdJournalStatus` |
| Entity audit columns? | ✅ **Universal** — every logistics table has `Add_Date`, `Upd_Date`, `Add_User`, `Upd_User` |

### How the Hybrid Timeline works

> [!NOTE]
> **Status values validated by Script 14 (OQ1–OQ5).** See [Appendix E](file:///c:/dev/alpla-portal/docs/OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md) for full enumeration.

```
Timeline = (EAI Journal events) UNION ALL (Entity creation/modification snapshots)

EAI Journal events:
  → EDI Created   = T_EAIJournal.JournalDatum  WHERE IdJournalStatus IN (11, 91)
  → EDI Exported  = T_EAIJournal.JournalDatum  WHERE IdJournalStatus IN (62, 64)
  → EDI Synced    = T_EAIJournalSynch.Upd_Date WHERE Status = 1

Entity snapshots:
  → PO Created       = T_Bestellungen.Add_Date         (Status >= 1)
  → PO Revision      = T_BestellungenJournal.Add_Date   (Revision > 1)
  → Call-off Created  = T_Abrufe.AbrufDatum              (AbrufStatus >= 1)
  → Loading Planned   = T_LadePlanungen.Add_Date         (Status IN (1, 11, 21))
  → Loading Order     = T_LadeAuftraege.Add_Date         (Status IN (1, 11, 21))
  → GR Created        = T_Wareneingaenge.Add_Date        (Status >= 0)
  → GR Completed      = T_Wareneingaenge.Upd_Date        (Status = 21)
  → Inhouse Delivery  = T_InhouseLieferungen.Add_Date    (V3 only — existence)

Key findings:
  - Exportiert column: sentinel date 1900-01-01 always set, NOT usable as timestamp
  - T_Abrufe.Status: always 0 (not meaningful). Use AbrufStatus instead.
  - UebermittlungsStatus, AbgleichTyp, SpedAnfrageStatus: always constant, skip.
  - T_JournalStatus/T_JournalTyp lookup tables do NOT exist — codes are conventions.
```

### Normalized Output Shape

Every timeline query returns the same column set:

| Column | Type | Description |
|--------|------|-------------|
| `SortOrder` | `INT` | Display ordering (10, 15, 20, 25, ...) |
| `EventCode` | `VARCHAR(30)` | Machine-readable event code (e.g. `PO_CREATED`) |
| `EventLabelPT` | `NVARCHAR(100)` | Portuguese display label |
| `SourceTable` | `VARCHAR(50)` | Source table name for traceability |
| `EventDate` | `DATETIME` | When the event occurred |
| `EventUser` | `NVARCHAR(50)` | Who performed the action |
| `MainStatus` | `INT` | Primary status value at time of event |
| `SecondaryStatus` | `INT` | Secondary status (e.g. `UebermittlungsStatus`) |
| `IdBestellung` | `INT` | Purchase Order ID |
| `IdBestellPosition` | `INT` | PO Line Item ID |
| `IdJournal` | `INT` | EAI Journal ID |
| `IdAuftragsAbruf` | `INT` | Call-off ID |
| `IdLadePlanung` | `INT` | Loading Plan ID |
| `IdLadeAuftrag` | `INT` | Loading Order ID |
| `IdWareneingang` | `INT` | Goods Receipt ID |
| `ReferenceNumber` | `NVARCHAR(50)` | Human-readable reference (PO number, journal number) |
| `Quantity` | `FLOAT` | Quantity if applicable |
| `Notes` | `NVARCHAR(255)` | Remarks / Bemerkung |

---

## 4. Viana 1 / Viana 2 — Standard Timeline MVP (10 Events)

> [!NOTE]
> **Confirmed**: Both Viana 1 (`AlplaPROD_aovia1`) and Viana 2 (`AlplaPROD_aovia2`) use this model.
> V2 Phase 1 shows active `T_Abrufe` (1,661), `T_LadeAuftraege` (2,254), `T_Lieferungen` (2,733)
> and empty `T_InhouseLieferungen` (0) / `T_InhouseBewegungen` (0).

### Pipeline

```
PO → EAI Journal → Abruf → LadePlanungen → LadeAuftraege → Wareneingang
```

### Prototype Query

```sql
-- ============================================================================
-- OPERATIONS TIMELINE — VIANA 1 STANDARD MODEL
-- Strategy D: Hybrid (EAI Journal + Entity Snapshots)
-- READ-ONLY: SELECT only. No INSERT, UPDATE, DELETE, MERGE, DROP, ALTER, EXEC.
-- No credentials or connection strings.
-- ============================================================================

-- Input parameters (replace with actual values when testing in SSMS)
DECLARE @IdBestellung INT = 26;  -- Example PO ID

-- Derive related IdJournal from T_BestellungenJournal (if linked)
DECLARE @IdJournal INT;
SELECT TOP 1 @IdJournal = bj.IdJournal
FROM [dbo].[T_BestellungenJournal] bj
WHERE bj.IdBestellung = @IdBestellung
ORDER BY bj.Revision DESC;

-- ============================================================================
-- TIMELINE QUERY: 10 events via UNION ALL
-- ============================================================================

-- Event 1: Pedido de compra criado
SELECT
    10                          AS SortOrder,
    'PO_CREATED'                AS EventCode,
    N'Pedido de compra criado'  AS EventLabelPT,
    'T_Bestellungen'            AS SourceTable,
    b.Add_Date                  AS EventDate,
    b.Add_User                  AS EventUser,
    b.Status                    AS MainStatus,
    b.UebermittlungsStatus      AS SecondaryStatus,
    b.IdBestellung              AS IdBestellung,
    NULL                        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    CAST(b.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber,
    NULL                        AS Quantity,
    b.Bemerkung                 AS Notes
FROM [dbo].[T_Bestellungen] b
WHERE b.IdBestellung = @IdBestellung

UNION ALL

-- Event 2: Revisão do pedido (one row per revision)
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
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    CAST(bj.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber,
    NULL                        AS Quantity,
    NULL                        AS Notes
FROM [dbo].[T_BestellungenJournal] bj
WHERE bj.IdBestellung = @IdBestellung

UNION ALL

-- Event 3: Documento EDI criado
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
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    j.JournalNummer             AS ReferenceNumber,
    NULL                        AS Quantity,
    j.Bemerkung                 AS Notes
FROM [dbo].[T_EAIJournal] j
WHERE j.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL

UNION ALL

-- Event 4: EDI exportado
SELECT
    25                          AS SortOrder,
    'EDI_EXPORTED'              AS EventCode,
    N'EDI exportado'            AS EventLabelPT,
    'T_EAIJournal'              AS SourceTable,
    j.Exportiert                AS EventDate,
    j.Upd_User                  AS EventUser,
    j.IdJournalStatus           AS MainStatus,
    NULL                        AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    j.IdJournal                 AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    j.JournalNummer             AS ReferenceNumber,
    NULL                        AS Quantity,
    NULL                        AS Notes
FROM [dbo].[T_EAIJournal] j
WHERE j.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL
  AND j.Exportiert IS NOT NULL

UNION ALL

-- Event 5: Sincronização EDI
SELECT
    30                          AS SortOrder,
    'EDI_SYNCED'                AS EventCode,
    N'Sincronização EDI'        AS EventLabelPT,
    'T_EAIJournalSynch'         AS SourceTable,
    js.Add_Date                 AS EventDate,
    js.Add_User                 AS EventUser,
    js.Status                   AS MainStatus,
    js.Aktion                   AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    js.IdJournal                AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS ReferenceNumber,
    NULL                        AS Quantity,
    js.Bemerkung                AS Notes
FROM [dbo].[T_EAIJournalSynch] js
WHERE js.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL

UNION ALL

-- Event 6: Abruf criado
-- NOTE: Links from T_Abrufe to T_Bestellungen via T_AuftragsAbrufe or
-- via T_Bestellpositionen.IdBestellung. The exact join path depends on
-- the business context. This prototype uses a subquery approach.
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
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS ReferenceNumber,
    NULL                        AS Quantity,
    a.Bemerkung                 AS Notes
FROM [dbo].[T_Abrufe] a
WHERE a.IdAuftragsAbruf IN (
    SELECT aa.IdAuftragsAbruf
    FROM [dbo].[T_AuftragsAbrufe] aa
    WHERE aa.IdAuftrag IN (
        SELECT bp.IdAuftrag
        FROM [dbo].[T_Bestellpositionen] bp
        WHERE bp.IdBestellung = @IdBestellung
          AND bp.IdAuftrag IS NOT NULL
    )
)

UNION ALL

-- Event 7: Carregamento planejado
SELECT
    50                          AS SortOrder,
    'LOADING_PLANNED'           AS EventCode,
    N'Carregamento planejado'   AS EventLabelPT,
    'T_LadePlanungen'           AS SourceTable,
    lp.Add_Date                 AS EventDate,
    lp.Add_User                 AS EventUser,
    lp.LadeStatus               AS MainStatus,
    lp.Status                   AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    NULL                        AS IdJournal,
    lp.IdAbrufe                 AS IdAuftragsAbruf,
    lp.IdLadePlanung            AS IdLadePlanung,
    lp.IdLadeAuftrag            AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS ReferenceNumber,
    NULL                        AS Quantity,
    lp.Bemerkung                AS Notes
FROM [dbo].[T_LadePlanungen] lp
WHERE lp.IdAbrufe IN (
    SELECT a2.IdAuftragsAbruf
    FROM [dbo].[T_Abrufe] a2
    WHERE a2.IdAuftragsAbruf IN (
        SELECT aa2.IdAuftragsAbruf
        FROM [dbo].[T_AuftragsAbrufe] aa2
        WHERE aa2.IdAuftrag IN (
            SELECT bp2.IdAuftrag
            FROM [dbo].[T_Bestellpositionen] bp2
            WHERE bp2.IdBestellung = @IdBestellung
              AND bp2.IdAuftrag IS NOT NULL
        )
    )
)

UNION ALL

-- Event 8: Ordem de carregamento
SELECT
    60                          AS SortOrder,
    'LOADING_ORDER'             AS EventCode,
    N'Ordem de carregamento'    AS EventLabelPT,
    'T_LadeAuftraege'           AS SourceTable,
    la.LadeDatum                AS EventDate,
    la.Add_User                 AS EventUser,
    la.Status                   AS MainStatus,
    la.LadeStatus               AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdLadePlanung,
    la.IdLadeAuftrag            AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS ReferenceNumber,
    NULL                        AS Quantity,
    la.Bemerkung                AS Notes
FROM [dbo].[T_LadeAuftraege] la
WHERE la.IdLadeAuftrag IN (
    SELECT DISTINCT lp2.IdLadeAuftrag
    FROM [dbo].[T_LadePlanungen] lp2
    WHERE lp2.IdAbrufe IN (
        SELECT a3.IdAuftragsAbruf
        FROM [dbo].[T_Abrufe] a3
        WHERE a3.IdAuftragsAbruf IN (
            SELECT aa3.IdAuftragsAbruf
            FROM [dbo].[T_AuftragsAbrufe] aa3
            WHERE aa3.IdAuftrag IN (
                SELECT bp3.IdAuftrag
                FROM [dbo].[T_Bestellpositionen] bp3
                WHERE bp3.IdBestellung = @IdBestellung
                  AND bp3.IdAuftrag IS NOT NULL
            )
        )
    )
    AND lp2.IdLadeAuftrag IS NOT NULL
)

UNION ALL

-- Event 9: Recebimento criado
SELECT
    80                          AS SortOrder,
    'GR_CREATED'                AS EventCode,
    N'Recebimento criado'       AS EventLabelPT,
    'T_Wareneingaenge'          AS SourceTable,
    w.Datum                     AS EventDate,
    w.Add_User                  AS EventUser,
    w.Status                    AS MainStatus,
    w.AbgleichStatus            AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    w.IdBestellPosition         AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    w.IdWareneingang            AS IdWareneingang,
    NULL                        AS ReferenceNumber,
    NULL                        AS Quantity,
    w.Bemerkung                 AS Notes
FROM [dbo].[T_Wareneingaenge] w
WHERE w.IdBestellPosition IN (
    SELECT bp4.IdBestellPosition
    FROM [dbo].[T_Bestellpositionen] bp4
    WHERE bp4.IdBestellung = @IdBestellung
)

UNION ALL

-- Event 10: Recebimento concluído
SELECT
    90                          AS SortOrder,
    'GR_COMPLETED'              AS EventCode,
    N'Recebimento concluído'    AS EventLabelPT,
    'T_Wareneingaenge'          AS SourceTable,
    w2.Upd_Date                 AS EventDate,
    w2.Upd_User                 AS EventUser,
    w2.Status                   AS MainStatus,
    w2.AbgleichStatus           AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    w2.IdBestellPosition        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    w2.IdWareneingang           AS IdWareneingang,
    NULL                        AS ReferenceNumber,
    NULL                        AS Quantity,
    w2.Bemerkung                AS Notes
FROM [dbo].[T_Wareneingaenge] w2
WHERE w2.IdBestellPosition IN (
    SELECT bp5.IdBestellPosition
    FROM [dbo].[T_Bestellpositionen] bp5
    WHERE bp5.IdBestellung = @IdBestellung
)
AND w2.Upd_Date IS NOT NULL
AND w2.Upd_Date <> w2.Add_Date  -- Only show if actually updated after creation

-- Final ordering: by SortOrder first, then by EventDate
ORDER BY SortOrder ASC, EventDate ASC;
```

### Expected Output Example

```
SortOrder | EventCode       | EventLabelPT                | SourceTable            | EventDate           | EventUser
----------|-----------------|-----------------------------|-----------------------|---------------------|----------
10        | PO_CREATED      | Pedido de compra criado     | T_Bestellungen        | 2026-05-01 09:30:00 | user.name
15        | PO_REVISION     | Revisão do pedido (Rev. 1)  | T_BestellungenJournal | 2026-05-01 09:30:05 | user.name
15        | PO_REVISION     | Revisão do pedido (Rev. 2)  | T_BestellungenJournal | 2026-05-01 10:15:00 | user.name
20        | EDI_CREATED     | Documento EDI criado        | T_EAIJournal          | 2026-05-01 10:16:00 | SYSTEM
25        | EDI_EXPORTED    | EDI exportado               | T_EAIJournal          | 2026-05-01 10:16:05 | SYSTEM
30        | EDI_SYNCED      | Sincronização EDI           | T_EAIJournalSynch     | 2026-05-01 10:17:00 | SYSTEM
40        | CALLOFF_CREATED | Abruf criado                | T_Abrufe              | 2026-05-02 08:00:00 | user.name
50        | LOADING_PLANNED | Carregamento planejado      | T_LadePlanungen       | 2026-05-02 08:30:00 | user.name
60        | LOADING_ORDER   | Ordem de carregamento       | T_LadeAuftraege       | 2026-05-03 07:00:00 | user.name
80        | GR_CREATED      | Recebimento criado          | T_Wareneingaenge      | 2026-05-04 08:00:00 | user.name
90        | GR_COMPLETED    | Recebimento concluído       | T_Wareneingaenge      | 2026-05-04 10:00:00 | user.name
```

---

## 5. Viana 3 — Inhouse Timeline MVP (7 Events)

### Pipeline

```
PO → EAI Journal → InhouseLieferungen → Wareneingang
```

### Prototype Query

```sql
-- ============================================================================
-- OPERATIONS TIMELINE — VIANA 3 INHOUSE MODEL
-- Strategy D: Hybrid (EAI Journal + Entity Snapshots)
-- READ-ONLY: SELECT only. No INSERT, UPDATE, DELETE, MERGE, DROP, ALTER, EXEC.
-- No credentials or connection strings.
-- ============================================================================

-- Input parameters (replace with actual values when testing in SSMS)
DECLARE @IdBestellung INT = 26;  -- Example PO ID

-- Derive related IdJournal from T_BestellungenJournal (if linked)
DECLARE @IdJournal INT;
SELECT TOP 1 @IdJournal = bj.IdJournal
FROM [dbo].[T_BestellungenJournal] bj
WHERE bj.IdBestellung = @IdBestellung
ORDER BY bj.Revision DESC;

-- ============================================================================
-- TIMELINE QUERY: 7 events via UNION ALL
-- ============================================================================

-- Event 1: Pedido de compra criado
SELECT
    10                          AS SortOrder,
    'PO_CREATED'                AS EventCode,
    N'Pedido de compra criado'  AS EventLabelPT,
    'T_Bestellungen'            AS SourceTable,
    b.Add_Date                  AS EventDate,
    b.Add_User                  AS EventUser,
    b.Status                    AS MainStatus,
    b.UebermittlungsStatus      AS SecondaryStatus,
    b.IdBestellung              AS IdBestellung,
    NULL                        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    CAST(b.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber,
    NULL                        AS Quantity,
    b.Bemerkung                 AS Notes
FROM [dbo].[T_Bestellungen] b
WHERE b.IdBestellung = @IdBestellung

UNION ALL

-- Event 2: Revisão do pedido (one row per revision)
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
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    CAST(bj.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber,
    NULL                        AS Quantity,
    NULL                        AS Notes
FROM [dbo].[T_BestellungenJournal] bj
WHERE bj.IdBestellung = @IdBestellung

UNION ALL

-- Event 3: Documento EDI criado
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
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    j.JournalNummer             AS ReferenceNumber,
    NULL                        AS Quantity,
    j.Bemerkung                 AS Notes
FROM [dbo].[T_EAIJournal] j
WHERE j.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL

UNION ALL

-- Event 4: EDI exportado
SELECT
    25                          AS SortOrder,
    'EDI_EXPORTED'              AS EventCode,
    N'EDI exportado'            AS EventLabelPT,
    'T_EAIJournal'              AS SourceTable,
    j2.Exportiert               AS EventDate,
    j2.Upd_User                 AS EventUser,
    j2.IdJournalStatus          AS MainStatus,
    NULL                        AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    NULL                        AS IdBestellPosition,
    j2.IdJournal                AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    j2.JournalNummer            AS ReferenceNumber,
    NULL                        AS Quantity,
    NULL                        AS Notes
FROM [dbo].[T_EAIJournal] j2
WHERE j2.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL
  AND j2.Exportiert IS NOT NULL

UNION ALL

-- Event 5: Entrega interna criada
-- NOTE: T_InhouseLieferungen links to T_EAIJournal via IdJournal.
-- The join from PO to InhouseLieferungen goes via IdJournal or via
-- T_Bestellpositionen linking. This prototype uses the IdJournal path.
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
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    NULL                        AS IdWareneingang,
    NULL                        AS ReferenceNumber,
    NULL                        AS Quantity,
    NULL                        AS Notes
FROM [dbo].[T_InhouseLieferungen] ih
WHERE ih.IdJournal = @IdJournal
  AND @IdJournal IS NOT NULL

UNION ALL

-- Event 6: Recebimento criado
SELECT
    80                          AS SortOrder,
    'GR_CREATED'                AS EventCode,
    N'Recebimento criado'       AS EventLabelPT,
    'T_Wareneingaenge'          AS SourceTable,
    w.Datum                     AS EventDate,
    w.Add_User                  AS EventUser,
    w.Status                    AS MainStatus,
    w.AbgleichStatus            AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    w.IdBestellPosition         AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    w.IdWareneingang            AS IdWareneingang,
    NULL                        AS ReferenceNumber,
    NULL                        AS Quantity,
    w.Bemerkung                 AS Notes
FROM [dbo].[T_Wareneingaenge] w
WHERE w.IdBestellPosition IN (
    SELECT bp.IdBestellPosition
    FROM [dbo].[T_Bestellpositionen] bp
    WHERE bp.IdBestellung = @IdBestellung
)

UNION ALL

-- Event 7: Recebimento concluído
SELECT
    90                          AS SortOrder,
    'GR_COMPLETED'              AS EventCode,
    N'Recebimento concluído'    AS EventLabelPT,
    'T_Wareneingaenge'          AS SourceTable,
    w2.Upd_Date                 AS EventDate,
    w2.Upd_User                 AS EventUser,
    w2.Status                   AS MainStatus,
    w2.AbgleichStatus           AS SecondaryStatus,
    @IdBestellung               AS IdBestellung,
    w2.IdBestellPosition        AS IdBestellPosition,
    NULL                        AS IdJournal,
    NULL                        AS IdAuftragsAbruf,
    NULL                        AS IdLadePlanung,
    NULL                        AS IdLadeAuftrag,
    w2.IdWareneingang           AS IdWareneingang,
    NULL                        AS ReferenceNumber,
    NULL                        AS Quantity,
    w2.Bemerkung                AS Notes
FROM [dbo].[T_Wareneingaenge] w2
WHERE w2.IdBestellPosition IN (
    SELECT bp2.IdBestellPosition
    FROM [dbo].[T_Bestellpositionen] bp2
    WHERE bp2.IdBestellung = @IdBestellung
)
AND w2.Upd_Date IS NOT NULL
AND w2.Upd_Date <> w2.Add_Date  -- Only show if actually updated after creation

-- Final ordering: by SortOrder first, then by EventDate
ORDER BY SortOrder ASC, EventDate ASC;
```

---

## 6. `T_InhouseBewegungen` Gap

> [!WARNING]
> **`T_InhouseBewegungen` was NOT captured by Script 11** (business event candidates).
> This means the table may lack standard `Add_User`/`Upd_User` audit columns,
> or its structure may differ from the standard pattern that Script 11 searches for.

### What we know

| Fact | Source |
|------|--------|
| Table exists in V3 (`AlplaPROD_aovia3`) | Script `01` schema inspection |
| Row count: **4,179 rows** in V3, **0 rows** in V1 | Script `06` delivery trace |
| Linked to `T_InhouseLieferungen` | Naming convention |
| NOT captured by Script 11 | Script `11` output — no `T_InhouseBewegungen` rows |

### What we do NOT know

- Does it have `Add_Date`/`Upd_Date` columns?
- Does it have `Add_User`/`Upd_User` columns?
- Does it have status fields?
- What are its primary key and foreign key columns?
- How does it link to `T_InhouseLieferungen`?

### Resolution options

1. **Run a targeted schema query** on `T_InhouseBewegungen` in SSMS:
   ```sql
   -- READ-ONLY: SELECT only
   SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
   FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_NAME = 'T_InhouseBewegungen'
   ORDER BY ORDINAL_POSITION;
   ```

2. **Include it in Script `10`** (article/variant trace) which may discover it via sample data.

3. **Defer**: The V3 MVP timeline works without it — `T_InhouseLieferungen` covers the in-house delivery step. `T_InhouseBewegungen` would add granularity (individual movements within a delivery) but is not required for the first MVP.

### Recommendation

**Defer for MVP**. The V3 in-house timeline already captures the key delivery milestone via `T_InhouseLieferungen`. Add `T_InhouseBewegungen` as a post-MVP enhancement once its schema is confirmed.

---

## 7. Conditional Timeline Rendering Logic

The future Operations module must support **conditional timeline rendering** because the three plants use two different logistics models.

### Plant-to-Model Mapping — ✅ CONFIRMED

| Plant | Database | Server | Pipeline Model | Timeline Events |
|-------|----------|--------|---------------|----------------|
| Viana 1 | `AlplaPROD_aovia1` | `AOVIA1VMS006` | **Standard** | 10 events |
| Viana 2 | `AlplaPROD_aovia2` | `AOVIA2VMS006` | **Standard** | 10 events |
| Viana 3 | `AlplaPROD_aovia3` | `AOVIA1VMS006` | **Inhouse** | 7 events |

### Detection Logic

```
IF (standard logistics rows exist for this PO):
    → Use Standard Pipeline Timeline (10 events) — applies to Viana 1 and Viana 2

ELSE IF (inhouse rows exist for this PO):
    → Use Inhouse Pipeline Timeline (7 events) — applies to Viana 3

ELSE:
    → Show partial timeline (PO + EAI + GR events only)
```

### Detection SQL (Documentation Only)

```sql
-- READ-ONLY: SELECT only. No INSERT, UPDATE, DELETE, MERGE, DROP, ALTER, EXEC.
-- Determines which pipeline model to use for a given PO.

DECLARE @IdBestellung INT = 26;

-- Check for standard logistics rows
DECLARE @HasStandardPipeline BIT = 0;
IF EXISTS (
    SELECT 1
    FROM [dbo].[T_Abrufe] a
    INNER JOIN [dbo].[T_AuftragsAbrufe] aa ON a.IdAuftragsAbruf = aa.IdAuftragsAbruf
    INNER JOIN [dbo].[T_Bestellpositionen] bp ON aa.IdAuftrag = bp.IdAuftrag
    WHERE bp.IdBestellung = @IdBestellung
      AND bp.IdAuftrag IS NOT NULL
)
SET @HasStandardPipeline = 1;

-- Check for inhouse rows
DECLARE @HasInhousePipeline BIT = 0;
IF EXISTS (
    SELECT 1
    FROM [dbo].[T_InhouseLieferungen] ih
    INNER JOIN [dbo].[T_BestellungenJournal] bj ON ih.IdJournal = bj.IdJournal
    WHERE bj.IdBestellung = @IdBestellung
)
SET @HasInhousePipeline = 1;

-- Result
SELECT
    @HasStandardPipeline AS HasStandardPipeline,
    @HasInhousePipeline  AS HasInhousePipeline,
    CASE
        WHEN @HasStandardPipeline = 1 THEN 'STANDARD'
        WHEN @HasInhousePipeline = 1  THEN 'INHOUSE'
        ELSE 'PARTIAL'
    END AS PipelineModel;
```

### Pipeline Model Summary

| Model | Plants | Detection | Timeline Events | Tables Used |
|-------|--------|-----------|----------------|-------------|
| **STANDARD** | V1, V2 | `T_Abrufe` rows exist for this PO | 10 events | `T_Bestellungen`, `T_BestellungenJournal`, `T_EAIJournal`, `T_EAIJournalSynch`, `T_Abrufe`, `T_LadePlanungen`, `T_LadeAuftraege`, `T_Wareneingaenge` |
| **INHOUSE** | V3 | `T_InhouseLieferungen` rows exist | 7 events | `T_Bestellungen`, `T_BestellungenJournal`, `T_EAIJournal`, `T_InhouseLieferungen`, `T_Wareneingaenge` |
| **PARTIAL** | Any | Neither standard nor inhouse rows exist | 3-4 events | `T_Bestellungen`, `T_BestellungenJournal`, `T_EAIJournal`, `T_Wareneingaenge` |

### Application-Level Implementation Notes

When implemented in the backend:

1. **Call the detection query first** to determine the pipeline model
2. **Execute the appropriate timeline query** (Standard or Inhouse)
3. **Return the normalized result** to the frontend
4. The frontend renders the timeline **identically** regardless of model — only the number of steps differs

---

## 8. Open Questions

> [!IMPORTANT]
> These questions must be answered before the timeline queries can be promoted from prototype to production.

### Status Value Interpretation

| # | Question | Impact | Resolution |
|---|----------|--------|-----------|
| OQ1 | Which `T_Bestellungen.Status` values mean "created", "finalized", "completed", "cancelled"? | Determines which POs appear in timeline and final status display | Run `SELECT DISTINCT Status, COUNT(*) FROM T_Bestellungen GROUP BY Status` |
| OQ2 | Which `T_EAIJournal.IdJournalStatus` values mean "created", "exported", "completed"? | Determines EDI event filtering | Run `SELECT DISTINCT IdJournalStatus, COUNT(*) FROM T_EAIJournal GROUP BY IdJournalStatus` |
| OQ3 | Which `T_Abrufe.AbrufStatus` values mean "open", "in-progress", "completed"? | Determines call-off display and status badges | Run status value enumeration query |
| OQ4 | Which `T_LadeAuftraege.LadeStatus` / `Status` values indicate loading start vs completion? | Determines whether to show "Loading Started" vs "Loading Completed" | Run status value enumeration query |
| OQ5 | Which `T_Wareneingaenge.Status` values indicate "created" vs "completed"? | Determines whether to show GR_COMPLETED event | Run status value enumeration query |

### Timeline Behavior

| # | Question | Impact |
|---|----------|--------|
| OQ6 | Should `GR Completed` use `Upd_Date` only when `Status` indicates a final state, or always? | Current prototype shows GR_COMPLETED whenever `Upd_Date ≠ Add_Date` — may include intermediate updates |
| OQ7 | How should duplicated/repeated EAI events be displayed? (e.g. multiple `T_EAIJournalSynch` rows per journal) | May need `ROW_NUMBER()` or aggregation to show only the latest sync event |
| OQ8 | Should EDI Sync (`T_EAIJournalSynch`) be shown to end users or hidden as a technical detail? | Affects MVP event count — V1 could drop from 10 to 9 events |
| OQ9 | How should partial/incomplete transfers be displayed? (e.g. PO exists but no GR yet) | Timeline should gracefully handle NULL events — already handled by UNION ALL approach |
| OQ10 | How should Viana 2 behave once `AOVIA2VMS006` is analyzed? | V2 may follow V1 standard model, V3 inhouse model, or a new variant — must be confirmed via Phase 1 |

### Referencing and Joining

| # | Question | Impact |
|---|----------|--------|
| OQ11 | Is the `T_Abrufe.IdAuftragsAbruf → T_AuftragsAbrufe → T_Bestellpositionen.IdAuftrag` join path always valid? | If `IdAuftrag` is NULL on some rows, the Call-off event may be missed |
| OQ12 | Does `T_InhouseLieferungen.IdJournal` always match `T_BestellungenJournal.IdJournal`? | If the join path is different, the V3 Inhouse Delivery event may need a different linking strategy |
| OQ13 | Can a single PO have rows in BOTH standard and inhouse pipelines? | If yes, the detection logic must be extended to handle mixed-model transfers |

---

## 9. Recommended Next Step

Based on the current discovery state:

| Option | Priority | Rationale |
|--------|----------|-----------|
| **Move to AOVIA2VMS006 (Viana 2)** | 🟢 **Recommended** | Phase 1+2+3 for V2 is the biggest remaining gap. Without V2 data, we cannot confirm which pipeline model it uses. Run scripts `01`, `02`, `03`, `08`, `12`, `13`, `11` on `AlplaPROD_aovia2`. |
| Run Script `10` (article trace) | 🟡 Medium | Would resolve the `T_InhouseBewegungen` gap and validate article structure. Can be done on V1+V3 while V2 is in progress. |
| Run Script `09` (cross-plant) | 🟡 Medium | Cross-plant comparison is Phase 4 — lower priority than completing V2 baseline. |
| Create real validation script for these prototypes | 🟡 Medium | Useful but depends on actual test data. Best done after V2 is understood. |

**Recommendation**: Start **AOVIA2VMS006 Phase 1** (scripts `01` + `02`) to establish the Viana 2 baseline. This is the most impactful next action because it resolves OQ10 (how V2 behaves) and enables Phase 4 cross-plant comparison.

---

## Appendix: Script Catalog Reference

| Script | Purpose | V1 Status | V3 Status |
|--------|---------|-----------|-----------|
| `01` | Schema inspection | ✅ Done | ✅ Done |
| `02` | German label search | ✅ Done | ✅ Done |
| `03` | FK inspection | ✅ Done | ✅ Done |
| `05` | PO domain trace | ✅ Done | ✅ Done |
| `06` | Delivery domain trace | ✅ Done | ✅ Done |
| `07` | GR domain trace | ✅ Done | ✅ Done |
| `08` | EDI investigation | ✅ Done | ✅ Done |
| `09` | Cross-plant linking | ⬜ Pending | ⬜ Pending |
| `10` | Article/variant trace | ⬜ Pending | ⬜ Pending |
| `11` | Business event candidates | ✅ Done | ✅ Done |
| `12` | Universal reference discovery | ✅ Done | ✅ Done |
| `13` | Audit/status/history discovery | ✅ Done | ✅ Done |
