-- ============================================================================
-- OPERATIONS MODULE â€” AlplaPROD Discovery
-- Script 14: Status Value Enumeration
-- ============================================================================
-- READ-ONLY: This script contains ONLY SELECT statements.
-- No INSERT, UPDATE, DELETE, MERGE, TRUNCATE, DROP, ALTER, or EXEC of
-- data-modifying procedures.
-- ============================================================================
-- PURPOSE:
--   Enumerate distinct status values, row counts, min/max dates, and sample
--   records for all status-bearing fields used by the Operations timeline.
--   Resolves open questions OQ1â€“OQ5.
--
-- COMPATIBILITY: SQL Server 2022 RTM (16.0.1000.6) â€” no STRING_AGG usage.
-- ============================================================================

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION A: T_Bestellungen (Purchase Orders) â€” OQ1
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- A1: Status distribution
SELECT 'T_Bestellungen' AS TableName, 'Status' AS StatusField,
    Status AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate,
    MIN(IdBestellung) AS MinPK, MAX(IdBestellung) AS MaxPK
FROM [dbo].[T_Bestellungen]
GROUP BY Status
ORDER BY Status;

GO

-- A2: UebermittlungsStatus distribution
SELECT 'T_Bestellungen' AS TableName, 'UebermittlungsStatus' AS StatusField,
    UebermittlungsStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate,
    MIN(IdBestellung) AS MinPK, MAX(IdBestellung) AS MaxPK
FROM [dbo].[T_Bestellungen]
GROUP BY UebermittlungsStatus
ORDER BY UebermittlungsStatus;

GO

-- A3: Bestaetigt distribution
SELECT 'T_Bestellungen' AS TableName, 'Bestaetigt' AS StatusField,
    Bestaetigt AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate,
    MIN(IdBestellung) AS MinPK, MAX(IdBestellung) AS MaxPK
FROM [dbo].[T_Bestellungen]
GROUP BY Bestaetigt
ORDER BY Bestaetigt;

GO

-- A4: Revision distribution
SELECT 'T_Bestellungen' AS TableName, 'Revision' AS StatusField,
    Revision AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate
FROM [dbo].[T_Bestellungen]
GROUP BY Revision
ORDER BY Revision;

GO

-- A5: Recent PO samples (TOP 20)
SELECT TOP 20
    IdBestellung, Status, UebermittlungsStatus, Bestaetigt, Revision,
    IdJournal, JournalNummer,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_Bestellungen]
ORDER BY IdBestellung DESC;

GO

-- A6: Status Ã— UebermittlungsStatus cross-tab
SELECT Status, UebermittlungsStatus, Bestaetigt, COUNT(*) AS Cnt
FROM [dbo].[T_Bestellungen]
GROUP BY Status, UebermittlungsStatus, Bestaetigt
ORDER BY Status, UebermittlungsStatus, Bestaetigt;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION B: T_BestellungenJournal (PO Revisions)
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- B1: Revision distribution
SELECT 'T_BestellungenJournal' AS TableName, 'Revision' AS StatusField,
    Revision AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate
FROM [dbo].[T_BestellungenJournal]
GROUP BY Revision
ORDER BY Revision;

GO

-- B2: Recent journal samples (TOP 20)
SELECT TOP 20
    IdBestellungJournal, IdBestellung, IdJournal, Revision,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_BestellungenJournal]
ORDER BY IdBestellungJournal DESC;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION C: T_EAIJournal (EAI Journal) â€” OQ2
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- C1: IdJournalStatus distribution
SELECT 'T_EAIJournal' AS TableName, 'IdJournalStatus' AS StatusField,
    IdJournalStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(JournalDatum) AS MinDate, MAX(JournalDatum) AS MaxDate,
    MIN(IdJournal) AS MinPK, MAX(IdJournal) AS MaxPK
FROM [dbo].[T_EAIJournal]
GROUP BY IdJournalStatus
ORDER BY IdJournalStatus;

GO

-- C2: IdJournalTyp distribution
SELECT 'T_EAIJournal' AS TableName, 'IdJournalTyp' AS StatusField,
    IdJournalTyp AS StatusValue, COUNT(*) AS Cnt,
    MIN(JournalDatum) AS MinDate, MAX(JournalDatum) AS MaxDate
FROM [dbo].[T_EAIJournal]
GROUP BY IdJournalTyp
ORDER BY IdJournalTyp;

GO

-- C3: IdJournalQuellModul distribution
SELECT 'T_EAIJournal' AS TableName, 'IdJournalQuellModul' AS StatusField,
    IdJournalQuellModul AS StatusValue, COUNT(*) AS Cnt,
    MIN(JournalDatum) AS MinDate, MAX(JournalDatum) AS MaxDate
FROM [dbo].[T_EAIJournal]
GROUP BY IdJournalQuellModul
ORDER BY IdJournalQuellModul;

GO

-- C4: Exportiert date analysis (NULL vs set)
SELECT 'T_EAIJournal' AS TableName, 'Exportiert' AS StatusField,
    CASE WHEN Exportiert IS NULL THEN 'NULL (not exported)'
         ELSE 'SET (exported)' END AS StatusValue,
    COUNT(*) AS Cnt,
    MIN(JournalDatum) AS MinJournalDate, MAX(JournalDatum) AS MaxJournalDate,
    MIN(Exportiert) AS MinExportDate, MAX(Exportiert) AS MaxExportDate
FROM [dbo].[T_EAIJournal]
GROUP BY CASE WHEN Exportiert IS NULL THEN 'NULL (not exported)'
              ELSE 'SET (exported)' END;

GO

-- C5: IdJournalStatus Ã— IdJournalTyp cross-tab
SELECT IdJournalStatus, IdJournalTyp, IdJournalQuellModul, COUNT(*) AS Cnt,
    MIN(JournalDatum) AS MinDate, MAX(JournalDatum) AS MaxDate
FROM [dbo].[T_EAIJournal]
GROUP BY IdJournalStatus, IdJournalTyp, IdJournalQuellModul
ORDER BY IdJournalStatus, IdJournalTyp, IdJournalQuellModul;

GO

-- C6: Recent EAI Journal samples (TOP 20)
SELECT TOP 20
    IdJournal, JournalNummer, IdJournalStatus, IdJournalTyp,
    IdJournalQuellModul, Exportiert, JournalDatum,
    IdLadeAuftrag, IdBestellVorschlag,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_EAIJournal]
ORDER BY IdJournal DESC;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION D: T_EAIJournalSynch (EAI Sync) â€” OQ2 continued
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- D1: Status distribution
SELECT 'T_EAIJournalSynch' AS TableName, 'Status' AS StatusField,
    Status AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate,
    MIN(IdEAIJournalSynch) AS MinPK, MAX(IdEAIJournalSynch) AS MaxPK
FROM [dbo].[T_EAIJournalSynch]
GROUP BY Status
ORDER BY Status;

GO

-- D2: Aktion distribution
SELECT 'T_EAIJournalSynch' AS TableName, 'Aktion' AS StatusField,
    Aktion AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate
FROM [dbo].[T_EAIJournalSynch]
GROUP BY Aktion
ORDER BY Aktion;

GO

-- D3: Status Ã— Aktion cross-tab
SELECT Status, Aktion, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate
FROM [dbo].[T_EAIJournalSynch]
GROUP BY Status, Aktion
ORDER BY Status, Aktion;

GO

-- D4: Recent sync samples (TOP 20)
SELECT TOP 20
    IdEAIJournalSynch, IdJournal, IdEDIKonfiguration,
    Status, Aktion, TransaktionUID, Dateiname,
    Plandatum, Fehler, Bemerkung,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_EAIJournalSynch]
ORDER BY IdEAIJournalSynch DESC;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION E: T_Abrufe (Call-offs) â€” OQ3
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- E1: Status distribution
SELECT 'T_Abrufe' AS TableName, 'Status' AS StatusField,
    Status AS StatusValue, COUNT(*) AS Cnt,
    MIN(AbrufDatum) AS MinDate, MAX(AbrufDatum) AS MaxDate,
    MIN(IdAbrufe) AS MinPK, MAX(IdAbrufe) AS MaxPK
FROM [dbo].[T_Abrufe]
GROUP BY Status
ORDER BY Status;

GO

-- E2: AbrufStatus distribution
SELECT 'T_Abrufe' AS TableName, 'AbrufStatus' AS StatusField,
    AbrufStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(AbrufDatum) AS MinDate, MAX(AbrufDatum) AS MaxDate
FROM [dbo].[T_Abrufe]
GROUP BY AbrufStatus
ORDER BY AbrufStatus;

GO

-- E3: LadeStatus distribution
SELECT 'T_Abrufe' AS TableName, 'LadeStatus' AS StatusField,
    LadeStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(LadeDatum) AS MinDate, MAX(LadeDatum) AS MaxDate
FROM [dbo].[T_Abrufe]
GROUP BY LadeStatus
ORDER BY LadeStatus;

GO

-- E4: LieferStatus distribution
SELECT 'T_Abrufe' AS TableName, 'LieferStatus' AS StatusField,
    LieferStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(Lieferdatum) AS MinDate, MAX(LetztesLieferDatum) AS MaxDate
FROM [dbo].[T_Abrufe]
GROUP BY LieferStatus
ORDER BY LieferStatus;

GO

-- E5: AbgleichStatus distribution
SELECT 'T_Abrufe' AS TableName, 'AbgleichStatus' AS StatusField,
    AbgleichStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(AbrufDatum) AS MinDate, MAX(AbrufDatum) AS MaxDate
FROM [dbo].[T_Abrufe]
GROUP BY AbgleichStatus
ORDER BY AbgleichStatus;

GO

-- E6: AbgleichTyp distribution
SELECT 'T_Abrufe' AS TableName, 'AbgleichTyp' AS StatusField,
    AbgleichTyp AS StatusValue, COUNT(*) AS Cnt,
    MIN(AbrufDatum) AS MinDate, MAX(AbrufDatum) AS MaxDate
FROM [dbo].[T_Abrufe]
GROUP BY AbgleichTyp
ORDER BY AbgleichTyp;

GO

-- E7: Status Ã— AbrufStatus Ã— LadeStatus Ã— LieferStatus cross-tab
SELECT Status, AbrufStatus, LadeStatus, LieferStatus, AbgleichStatus,
    COUNT(*) AS Cnt
FROM [dbo].[T_Abrufe]
GROUP BY Status, AbrufStatus, LadeStatus, LieferStatus, AbgleichStatus
ORDER BY Status, AbrufStatus, LadeStatus, LieferStatus, AbgleichStatus;

GO

-- E8: Recent call-off samples (TOP 20)
SELECT TOP 20
    IdAbrufe, IdAuftragsAbruf, Status, AbrufStatus, LadeStatus,
    LieferStatus, AbgleichStatus, AbgleichTyp,
    AbrufDatum, LadeDatum, Lieferdatum, LetztesLieferDatum,
    Abrufnummer, Menge, LadeMenge, PlanMenge,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_Abrufe]
ORDER BY IdAbrufe DESC;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION F: T_LadePlanungen (Loading Plans) â€” OQ3 continued
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- F1: Status distribution
SELECT 'T_LadePlanungen' AS TableName, 'Status' AS StatusField,
    Status AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate,
    MIN(IdLadePlanung) AS MinPK, MAX(IdLadePlanung) AS MaxPK
FROM [dbo].[T_LadePlanungen]
GROUP BY Status
ORDER BY Status;

GO

-- F2: LadeStatus distribution
SELECT 'T_LadePlanungen' AS TableName, 'LadeStatus' AS StatusField,
    LadeStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate
FROM [dbo].[T_LadePlanungen]
GROUP BY LadeStatus
ORDER BY LadeStatus;

GO

-- F3: Typ distribution
SELECT 'T_LadePlanungen' AS TableName, 'Typ' AS StatusField,
    Typ AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate
FROM [dbo].[T_LadePlanungen]
GROUP BY Typ
ORDER BY Typ;

GO

-- F4: PlanLogik distribution
SELECT 'T_LadePlanungen' AS TableName, 'PlanLogik' AS StatusField,
    PlanLogik AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate
FROM [dbo].[T_LadePlanungen]
GROUP BY PlanLogik
ORDER BY PlanLogik;

GO

-- F5: Status Ã— LadeStatus cross-tab
SELECT Status, LadeStatus, Typ, PlanLogik, COUNT(*) AS Cnt
FROM [dbo].[T_LadePlanungen]
GROUP BY Status, LadeStatus, Typ, PlanLogik
ORDER BY Status, LadeStatus, Typ, PlanLogik;

GO

-- F6: Recent loading plan samples (TOP 20)
SELECT TOP 20
    IdLadePlanung, IdAbrufe, IdLadeAuftrag, IdLieferschein,
    Status, LadeStatus, Typ, PlanLogik,
    LieferDatum, Menge, LadeMenge,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_LadePlanungen]
ORDER BY IdLadePlanung DESC;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION G: T_LadeAuftraege (Loading Orders) â€” OQ3 continued
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- G1: Status distribution
SELECT 'T_LadeAuftraege' AS TableName, 'Status' AS StatusField,
    Status AS StatusValue, COUNT(*) AS Cnt,
    MIN(LadeDatum) AS MinDate, MAX(LadeDatum) AS MaxDate,
    MIN(IdLadeAuftrag) AS MinPK, MAX(IdLadeAuftrag) AS MaxPK
FROM [dbo].[T_LadeAuftraege]
GROUP BY Status
ORDER BY Status;

GO

-- G2: LadeStatus distribution
SELECT 'T_LadeAuftraege' AS TableName, 'LadeStatus' AS StatusField,
    LadeStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(LadeDatum) AS MinDate, MAX(LadeDatum) AS MaxDate
FROM [dbo].[T_LadeAuftraege]
GROUP BY LadeStatus
ORDER BY LadeStatus;

GO

-- G3: LKWStatus distribution
SELECT 'T_LadeAuftraege' AS TableName, 'LKWStatus' AS StatusField,
    LKWStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(LadeDatum) AS MinDate, MAX(LadeDatum) AS MaxDate
FROM [dbo].[T_LadeAuftraege]
GROUP BY LKWStatus
ORDER BY LKWStatus;

GO

-- G4: LieferplanStatus distribution
SELECT 'T_LadeAuftraege' AS TableName, 'LieferplanStatus' AS StatusField,
    LieferplanStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(LadeDatum) AS MinDate, MAX(LadeDatum) AS MaxDate
FROM [dbo].[T_LadeAuftraege]
GROUP BY LieferplanStatus
ORDER BY LieferplanStatus;

GO

-- G5: WAAvisoStatus distribution
SELECT 'T_LadeAuftraege' AS TableName, 'WAAvisoStatus' AS StatusField,
    WAAvisoStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(LadeDatum) AS MinDate, MAX(LadeDatum) AS MaxDate
FROM [dbo].[T_LadeAuftraege]
GROUP BY WAAvisoStatus
ORDER BY WAAvisoStatus;

GO

-- G6: LieferAvisoStatus distribution
SELECT 'T_LadeAuftraege' AS TableName, 'LieferAvisoStatus' AS StatusField,
    LieferAvisoStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(LadeDatum) AS MinDate, MAX(LadeDatum) AS MaxDate
FROM [dbo].[T_LadeAuftraege]
GROUP BY LieferAvisoStatus
ORDER BY LieferAvisoStatus;

GO

-- G7: SpedAnfrageStatus distribution
SELECT 'T_LadeAuftraege' AS TableName, 'SpedAnfrageStatus' AS StatusField,
    SpedAnfrageStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(LadeDatum) AS MinDate, MAX(LadeDatum) AS MaxDate
FROM [dbo].[T_LadeAuftraege]
GROUP BY SpedAnfrageStatus
ORDER BY SpedAnfrageStatus;

GO

-- G8: Status Ã— LadeStatus Ã— LKWStatus cross-tab
SELECT Status, LadeStatus, LKWStatus, LieferplanStatus, COUNT(*) AS Cnt
FROM [dbo].[T_LadeAuftraege]
GROUP BY Status, LadeStatus, LKWStatus, LieferplanStatus
ORDER BY Status, LadeStatus, LKWStatus, LieferplanStatus;

GO

-- G9: Recent loading order samples (TOP 20)
SELECT TOP 20
    IdLadeAuftrag, Status, LadeStatus, LKWStatus,
    LieferplanStatus, WAAvisoStatus, LieferAvisoStatus, SpedAnfrageStatus,
    LadeDatum, LKWNummer, LKWBezeichnung,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_LadeAuftraege]
ORDER BY IdLadeAuftrag DESC;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION H: T_Wareneingaenge (Goods Receipts) â€” OQ4, OQ5
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- H1: Status distribution
SELECT 'T_Wareneingaenge' AS TableName, 'Status' AS StatusField,
    Status AS StatusValue, COUNT(*) AS Cnt,
    MIN(Datum) AS MinDate, MAX(Datum) AS MaxDate,
    MIN(IdWareneingang) AS MinPK, MAX(IdWareneingang) AS MaxPK
FROM [dbo].[T_Wareneingaenge]
GROUP BY Status
ORDER BY Status;

GO

-- H2: AbgleichStatus distribution
SELECT 'T_Wareneingaenge' AS TableName, 'AbgleichStatus' AS StatusField,
    AbgleichStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(Datum) AS MinDate, MAX(Datum) AS MaxDate
FROM [dbo].[T_Wareneingaenge]
GROUP BY AbgleichStatus
ORDER BY AbgleichStatus;

GO

-- H3: BestellPositionStatus distribution
SELECT 'T_Wareneingaenge' AS TableName, 'BestellPositionStatus' AS StatusField,
    BestellPositionStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(Datum) AS MinDate, MAX(Datum) AS MaxDate
FROM [dbo].[T_Wareneingaenge]
GROUP BY BestellPositionStatus
ORDER BY BestellPositionStatus;

GO

-- H4: EurologStatus distribution
SELECT 'T_Wareneingaenge' AS TableName, 'EurologStatus' AS StatusField,
    EurologStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(Datum) AS MinDate, MAX(Datum) AS MaxDate
FROM [dbo].[T_Wareneingaenge]
GROUP BY EurologStatus
ORDER BY EurologStatus;

GO

-- H5: Typ distribution
SELECT 'T_Wareneingaenge' AS TableName, 'Typ' AS StatusField,
    Typ AS StatusValue, COUNT(*) AS Cnt,
    MIN(Datum) AS MinDate, MAX(Datum) AS MaxDate
FROM [dbo].[T_Wareneingaenge]
GROUP BY Typ
ORDER BY Typ;

GO

-- H6: Status Ã— AbgleichStatus Ã— Typ cross-tab
SELECT Status, AbgleichStatus, BestellPositionStatus, EurologStatus, Typ,
    COUNT(*) AS Cnt
FROM [dbo].[T_Wareneingaenge]
GROUP BY Status, AbgleichStatus, BestellPositionStatus, EurologStatus, Typ
ORDER BY Status, AbgleichStatus, BestellPositionStatus, EurologStatus, Typ;

GO

-- H7: Recent GR samples (TOP 20)
SELECT TOP 20
    IdWareneingang, IdBestellung, IdBestellPosition, IdJournal, IdAuftragsAbruf,
    Status, AbgleichStatus, BestellPositionStatus, EurologStatus, Typ,
    Datum, SollMenge, IstMenge,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_Wareneingaenge]
ORDER BY IdWareneingang DESC;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION I: T_WareneingangPlanungen (GR Planning)
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- I1: Status distribution
SELECT 'T_WareneingangPlanungen' AS TableName, 'Status' AS StatusField,
    Status AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate,
    MIN(IdWareneingangPlanung) AS MinPK, MAX(IdWareneingangPlanung) AS MaxPK
FROM [dbo].[T_WareneingangPlanungen]
GROUP BY Status
ORDER BY Status;

GO

-- I2: LadeStatus distribution
SELECT 'T_WareneingangPlanungen' AS TableName, 'LadeStatus' AS StatusField,
    LadeStatus AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate
FROM [dbo].[T_WareneingangPlanungen]
GROUP BY LadeStatus
ORDER BY LadeStatus;

GO

-- I3: Typ distribution
SELECT 'T_WareneingangPlanungen' AS TableName, 'Typ' AS StatusField,
    Typ AS StatusValue, COUNT(*) AS Cnt,
    MIN(Add_Date) AS MinDate, MAX(Upd_Date) AS MaxDate
FROM [dbo].[T_WareneingangPlanungen]
GROUP BY Typ
ORDER BY Typ;

GO

-- I4: Recent GR planning samples (TOP 20)
SELECT TOP 20
    IdWareneingangPlanung, IdWareneingang, IdWareneingangAuftrag,
    IdJournal, IdLadePlanung,
    Status, LadeStatus, Typ,
    Menge, EntladeMenge,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_WareneingangPlanungen]
ORDER BY IdWareneingangPlanung DESC;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION J: T_InhouseLieferungen (Inhouse Deliveries â€” V3)
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- J1: Summary statistics (no explicit status field)
SELECT 'T_InhouseLieferungen' AS TableName, 'RowSummary' AS StatusField,
    'ALL' AS StatusValue, COUNT(*) AS Cnt,
    MIN(ProdTag) AS MinProdTag, MAX(ProdTag) AS MaxProdTag,
    MIN(Add_Date) AS MinAddDate, MAX(Upd_Date) AS MaxUpdDate
FROM [dbo].[T_InhouseLieferungen];

GO

-- J2: IdVpkVorschrift distribution
SELECT 'T_InhouseLieferungen' AS TableName, 'IdVpkVorschrift' AS StatusField,
    IdVpkVorschrift AS StatusValue, COUNT(*) AS Cnt,
    MIN(ProdTag) AS MinDate, MAX(ProdTag) AS MaxDate
FROM [dbo].[T_InhouseLieferungen]
GROUP BY IdVpkVorschrift
ORDER BY Cnt DESC;

GO

-- J3: IdAdresse distribution (receiving plant)
SELECT 'T_InhouseLieferungen' AS TableName, 'IdAdresse' AS StatusField,
    IdAdresse AS StatusValue, COUNT(*) AS Cnt,
    MIN(ProdTag) AS MinDate, MAX(ProdTag) AS MaxDate
FROM [dbo].[T_InhouseLieferungen]
GROUP BY IdAdresse
ORDER BY Cnt DESC;

GO

-- J4: Recent inhouse delivery samples (TOP 20)
SELECT TOP 20
    IdInhouseLieferung, ProdTag, IdArtikelVariante, IdVpkVorschrift,
    IdHauptmaterial, IdAdresse, LieferMenge, BufferRetourMenge,
    LieferMengeVereinbart, IdJournal, JournalNummer,
    JournalPositionGuid, SammelGuid, LieferscheinDatum,
    KundenAuftragsNummer, KundenPositionsNummer,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_InhouseLieferungen]
ORDER BY IdInhouseLieferung DESC;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION K: T_InhouseBewegungen (Inhouse Movements â€” V3)
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- K1: Summary statistics (no explicit status field)
SELECT 'T_InhouseBewegungen' AS TableName, 'RowSummary' AS StatusField,
    'ALL' AS StatusValue, COUNT(*) AS Cnt,
    MIN(ProdTag) AS MinProdTag, MAX(ProdTag) AS MaxProdTag,
    MIN(Add_Date) AS MinAddDate, MAX(Upd_Date) AS MaxUpdDate
FROM [dbo].[T_InhouseBewegungen];

GO

-- K2: Recent inhouse movement samples (TOP 20)
SELECT TOP 20
    IdInhouseBewegung, ProdTag, IdArtikelVariante,
    ProduzierteMenge, FehlMenge, BufferEinlagerungsMenge,
    BufferLieferMenge, BufferRetourMenge, LieferMenge,
    Add_User, Add_Date, Upd_User, Upd_Date
FROM [dbo].[T_InhouseBewegungen]
ORDER BY IdInhouseBewegung DESC;

GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- SECTION L: Reference/Lookup tables for status values
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

-- L1: T_JournalStatus (if exists â€” lookup for IdJournalStatus)
SELECT * FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'T_JournalStatus'
ORDER BY ORDINAL_POSITION;

GO

-- L2: T_JournalStatus data (if table exists)
IF OBJECT_ID('dbo.T_JournalStatus', 'U') IS NOT NULL
    SELECT * FROM [dbo].[T_JournalStatus] ORDER BY 1;

GO

-- L3: T_JournalTyp (if exists â€” lookup for IdJournalTyp)
IF OBJECT_ID('dbo.T_JournalTyp', 'U') IS NOT NULL
    SELECT * FROM [dbo].[T_JournalTyp] ORDER BY 1;

GO

-- L4: T_JournalQuellModul (if exists â€” lookup for IdJournalQuellModul)
IF OBJECT_ID('dbo.T_JournalQuellModul', 'U') IS NOT NULL
    SELECT * FROM [dbo].[T_JournalQuellModul] ORDER BY 1;

GO

-- L5: Any other status lookup tables
SELECT t.name AS TableName, p.rows AS ApproxRowCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE '%Status%'
   OR t.name LIKE '%JournalTyp%'
   OR t.name LIKE '%JournalQuell%'
   OR t.name LIKE '%LadeStatus%'
   OR t.name LIKE '%AbrufStatus%'
ORDER BY t.name;

GO

-- L6: Show columns of any status lookup tables found
SELECT c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME IN (
    SELECT t.name FROM sys.tables t
    WHERE t.name LIKE '%JournalStatus%'
       OR t.name LIKE '%JournalTyp%'
       OR t.name LIKE '%JournalQuell%'
)
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

GO

-- ============================================================================
-- END OF SCRIPT 14 â€” Status Value Enumeration
-- ============================================================================

