# Operations Entity Map

> **Status**: DISCOVERY COMPLETE — All scripts (01–14) across V1/V2/V3. OQ1–OQ5 RESOLVED. Technical Design available.  
> **Date**: 2026-05-29  
> **Updated**: 2026-05-31 (Technical Design document created)  
> **Parent Document**: [OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md)  
> **Technical Design**: [OPERATIONS_MODULE_TECHNICAL_DESIGN.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md)  
> **Constraint**: Entries marked ✅ Confirmed are validated against real SQL output

> [!NOTE]
> **Pipeline Model CONFIRMED across all 3 plants:**
> - Viana 1 (`AlplaPROD_aovia1`) = **Standard Model** (10-event timeline)
> - Viana 2 (`AlplaPROD_aovia2`) = **Standard Model** (10-event timeline)
> - Viana 3 (`AlplaPROD_aovia3`) = **Inhouse Model** (7-event timeline)
>
> **Cross-plant linking** uses EAI/EDI web services (not linked servers).
> Each plant knows partners via `T_Adressen` (V1↔V2: IdAdressen 52↔25) and `T_EDIKonfigurationen`.
> `T_Werke` (536 rows) provides global plant registry with `werk_server`/`werk_db`/`inhouse` flag.
>
> **Material display**: `T_Artikelvarianten.Bezeichnung` is the primary display field.
> `T_ArtikelvariantenTyp.Bezeichnung` provides the type label (e.g. "HD-PE", "PET-P", "Purchased Preform").
> `T_VpkVorschrift.Bezeichnung` provides the packaging label (e.g. "$RESINA 1100 KGS BIG BAG").
>
> **T_InhouseBewegungen gap RESOLVED**: Has `IdArtikelVariante`, `Add_Date`, `Upd_Date`, `Add_User`.
> `T_InhouseLieferungen` has `IdJournal`, `JournalNummer`, `JournalPositionGuid` — can be included in V3 timeline.

---

## 1. Purpose

This document serves as the **practical working map** for the future Operations module implementation. It translates the technical discovery from the main report into actionable mappings:

1. **Entity Map** — Business entities → database tables + key fields
2. **Universal Reference Candidates** — Fields that could link the entire transfer flow end-to-end
3. **Timeline Event Map** — Business events → source data for the Operations timeline

### How to Use This Document

1. Run the SQL discovery scripts (Phase 1 and 2) against the real databases
2. Update the "Confirmed Table" and "Validation Status" columns with real findings
3. Once enough entities are confirmed, use this map to design the read-only service layer

---

## 2. Entity Map

### 2.1 Purchase Order Domain — ✅ CONFIRMED

| Business Entity | Business Event | Confirmed Table | Rows (V1) | Rows (V3) | Primary Key | Parent Key | Link Fields | Status Fields | Date Fields | Quantity Fields | Audit Fields | Confidence | Validation Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Purchase Order | PO Created | ✅ `dbo.T_Bestellungen` | 2,436 | 2,549 | `IdBestellung` | — | `IdJournal`, `JournalNummer`, `IdLieferant` | `Status` (int), `UebermittlungsStatus` (int) | `Add_Date`, `Upd_Date` | — | `Add_User`, `Upd_User` | ✅ Confirmed | ✅ Confirmed |
| PO Item | PO Item Added | ✅ `dbo.T_Bestellpositionen` | 2,623 | 2,507 | `IdBestellPosition` | `IdBestellung` | `IdJournal`, `IdJournalPosition`, `JournalNummer`, `IdArtikelVarianten`, `IdHauptmaterial` | `PositionsStatus` (int) | `BestellDatum`, `Lieferdatum`, `Add_Date`, `Upd_Date` | `BestellMenge`, `BestellMengeVPK` | `Add_User`, `Upd_User` | ✅ Confirmed | ✅ Confirmed |
| PO Journal | PO Change Logged | ✅ `dbo.T_BestellungenJournal` | 2,411 | 2,550 | — | — | — | — | — | — | — | ✅ Confirmed | ⚠️ Partial (columns TBD) |
| Supplier | — | ✅ `dbo.T_ZentraleLieferanten` | 1,147 | 909 | — | — | — | — | — | — | — | ✅ Confirmed | ⚠️ Partial |
| Address | — | ✅ `dbo.T_Adressen` | 111 | 12 | — | — | — | — | — | — | — | ✅ Confirmed | ⚠️ Partial |

### 2.2 EDI / Transmission Domain — ✅ CONFIRMED

| Business Entity | Business Event | Confirmed Table | Rows (V1) | Rows (V3) | Primary Key | Link Fields | Status Fields | Notes | Validation Status |
|---|---|---|---|---|---|---|---|---|---|
| EAI Journal | EDI Event | ✅ `dbo.T_EAIJournal` | 12,640 | 6,577 | `IdJournal`? | Links to PO via `T_EAIJournalBestellPosition` | TBD (Phase 2) | **Central EDI mechanism** | ⚠️ Partial |
| Journal PO Position | Journal → PO Link | ✅ `dbo.T_EAIJournalBestellPosition` | 4,392 | 2,929 | — | Bridges journal ↔ PO positions | — | **Critical bridge table** | ⚠️ Partial |
| Journal Delivery Pos | Journal → Delivery | ✅ `dbo.T_EAIJournalLieferPosition` | 129,885 | 0 | — | Bridges journal ↔ deliveries | — | V3=0 (no deliveries) | ⚠️ Partial |
| Journal Goods Pos | Journal → Goods | ✅ `dbo.T_EAIJournalWarenPosition` | 14,433 | 8,452 | — | Bridges journal ↔ goods positions | — | | ⚠️ Partial |
| Journal Sync | Sync Status | ✅ `dbo.T_EAIJournalSynch` | 11,882 | 9,416 | — | — | Sync status tracking | | ⚠️ Partial |
| EDI Document | EDI Message | ✅ `dbo.T_EDIDokumente` | 1,222 | 3,653 | — | — | — | Separate from EAI | ⚠️ Partial |
| EDI Config | EDI Routing | ✅ `dbo.T_EDIKonfigurationen` | 3 | 6 | — | — | — | V3 has more configs | ⚠️ Partial |

### 2.3 Delivery / Loading Domain (Supplying Plant) — ✅ CONFIRMED

| Business Entity | Business Event | Confirmed Table | Rows (V1) | Rows (V3) | Primary Key | Parent Key | Link Fields | Status Fields | Date Fields | Quantity Fields | Validation Status |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Call-off (Abruf) | Call-off Created | ✅ `dbo.T_Abrufe` | 4,041 | 0 | `IdAbrufe` | `IdAuftrag`, `IdAuftragsPosition`, `IdAuftragsAbruf` | `AuftragsNummer`, `Abrufnummer`, `IdKonto` | `Status`, `AbrufStatus`, `LieferStatus`, `LadeStatus`, `AbgleichTyp`, `AbgleichStatus` | `AbrufDatum`, `Lieferdatum`, `Add_Date`, `Upd_Date` | `Menge`, `MengeVPK`, `PlanMenge`, `LadeMenge` | ✅ Confirmed |
| Loading Plan | Loading Created | ✅ `dbo.T_LadePlanungen` | 6,970 | 0 | `IdLadePlanung` | `IdAbrufe` (FK✅), `IdLadeAuftrag` (FK✅) | `IdKonto`, `IdLieferschein`, `IdAuftragsBest`, `IdSpedAnfrage`, `IdLieferPlan`, `ExtLieferscheinNummer` | `LadeStatus`, `Status`, `Typ`, `PlanLogik` | `LieferDatum`, `AuftragsBestDatum`, `SpedAnfrageDatum`, `LieferPlanDatum`, `Add_Date`, `Upd_Date` | `Menge`, `MengeVPK`, `LadeMenge`, `LadeMengeVPK` | ✅ Phase 3 Confirmed |
| Loading Position | Item Loaded | ✅ `dbo.T_LadePositionen` | 127,653 | 0 | `IdLadePosition` | `IdLadePlanung` | `IdKonto`, `Barcode`, `Beleg` | — | `ProduktionsDatum`, `Add_Date`, `Upd_Date` | `Menge`, `MengeVPK` | ✅ Phase 3 Confirmed |
| Loading Order | Loading Ordered | ✅ `dbo.T_LadeAuftraege` | 6,461 | 0 | `IdLadeAuftrag` | `IdSpediteur`, `IdRampe` | `LKWNummer`, `LKWBezeichnung`, `IdLKWTyp`, `ExtLieferscheinNummer` | `Status`, `LadeStatus`, `LKWStatus`, `LieferplanStatus`, `WAAvisoStatus`, `LieferAvisoStatus`, `SpedAnfrageStatus` (7!) | `LadeDatum`, `Add_Date`, `Upd_Date` | `PlanMenge`, `PlanMengeVPK`, `TransportKosten`, `VolumenGeplant`, `VolumenGeliefert`, `BruttoGewicht` | ✅ Phase 3 Confirmed |
| Delivery | Delivery Made | ✅ `dbo.T_Lieferungen` | 6,224 | 0 | — | — | `IdJournal` (back to EAI) | — | — | — | ⚠️ Partial (Phase 3 row count confirmed) |
| Delivery Position | Item Delivered | ✅ `dbo.T_LieferPositionen` | 127,602 | 0 | — | — | — | — | — | — | ⚠️ Partial (Phase 3 row count confirmed) |
| Delivery Booking | Booking Record | ✅ `dbo.T_LieferBuchungen` | 189,576 | 0 | — | — | — | — | — | — | ⚠️ New (Phase 3 — V1 only) |
| Truck Type | — | ✅ `dbo.T_LKWTypen` | 1 | 1 | `IdLKWTyp` | — | — | — | — | — | ✅ Phase 3 Confirmed |
| Inhouse Movement | Inhouse Transfer | ✅ `dbo.T_InhouseBewegungen` | 0 | 4,179 | — | — | — | — | — | — | ✅ Phase 3 Confirmed (V3 only) |
| Inhouse Delivery | Inhouse Delivery | ✅ `dbo.T_InhouseLieferungen` | 0 | 4,353 | — | — | `IdJournal`, `JournalNummer` | — | — | — | ✅ Phase 3 Confirmed (V3 only) |

> [!WARNING]
> Viana 3 uses **Inhouse** tables instead of standard delivery tables.
> `T_Abrufe`=0, `T_LadePlanungen`=0, `T_Lieferungen`=0 in V3, but
> `T_InhouseBewegungen`=4,179 and `T_InhouseLieferungen`=4,353.

### 2.4 Goods Receipt Domain (Requesting Plant) — ✅ CONFIRMED

| Business Entity | Business Event | Confirmed Table | Rows (V1) | Rows (V3) | Primary Key | Parent Key | Link Fields | Status Fields | Date Fields | Quantity Fields | Validation Status |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Goods Receipt | GR Created | ✅ `dbo.T_Wareneingaenge` | 2,716 | 2,497 | `IdWareneingang` | `IdBestellung`, `IdBestellPosition` | `IdJournal`, `IdJournalPosition`, `IdJournalWarenPosition`, `Beleg`, `IdAuftragsAbruf`, `IdArtikelVarianten` | `Status`, `AbgleichStatus`, `Typ`, `BestellPositionStatus`, `EurologStatus` | `Datum`, `Add_Date`, `Upd_Date` | `SollMenge`, `SollMengeVPK`, `IstMenge`, `IstMengeVPK` | ✅ Confirmed |
| GR Order | GR Processing | ✅ `dbo.T_WareneingangAuftraege` | 2,471 | 3,655 | `IdWareneingangAuftrag` | — | `LKWNummer`, `LKWBezeichnung`, `Beleg`, `IdSpediteur`, `IdRampe`, `IdLadeAuftrag`, `ExtLieferscheinNummer`, `ExtLieferscheinDatum` | `Status`, `WEAvisoStatus`, `Typ` | `Datum`, `Add_Date`, `Upd_Date` | `FertigungsmittelMengeTE`, `FertigwarenMengeTE` | ✅ Phase 3 Confirmed |
| GR Plan | GR Planned | ✅ `dbo.T_WareneingangPlanungen` | 2,617 | 3,965 | `IdWareneingangPlanung` | `IdWareneingangAuftrag` | `IdJournal`, `IdJournalPosition`, `IdJournalWarenPosition`, `IdLadePlanung`, `IdWareneingang`, `Beleg`, `IdLieferschein`, `IdEDIDokumentPosition` | `Status`, `LadeStatus`, `Typ` | `Add_Date`, `Upd_Date` | `Menge`, `MengeVPK`, `NOKMenge`, `NOKMengeVPK`, `EntladeMenge`, `EntladeMengeVPK` | ✅ Phase 3 Confirmed |
| GR Position | Item Received | ✅ `dbo.T_WareneingangPositionen` | 57,591 | 58,038 | `IdWareneingangPosition` | `IdWareneingangPlanung` | `IdKonto`, `Barcode`, `Beleg` | — | `Add_Date`, `Upd_Date` | `Menge`, `MengeVPK` | ✅ Phase 3 Confirmed |
| GR Evaluation | GR Evaluated | ✅ `dbo.T_WareneingangBewertungen` | 2,548 | 3,904 | `IdWareneingangBewertung` | `IdWareneingangPlanung` | — | `MaterialQualitaet`, `VerpackungsQualitaet`, `AxExportiert` | `Add_Date`, `Upd_Date` | — | ✅ Phase 3 Confirmed |

### 2.5 Article / Material Domain — ✅ CONFIRMED

| Business Entity | Confirmed Table | Rows (V1) | Rows (V3) | Primary Key | Notes | Validation Status |
|---|---|---|---|---|---|---|
| Article Variant | ✅ `dbo.T_Artikelvarianten` | 454 | 309 | `IdArtikelVarianten`? | Referenced by PO items and GR | ⚠️ Partial |
| Variant Type | ✅ `dbo.T_ArtikelvariantenTyp` | 36 | 26 | — | | ⚠️ Partial |
| Packaging | ✅ `dbo.T_Vpk` | 133 | 19 | — | | ⚠️ Partial |
| Barcode / Label | ✅ `dbo.T_EtikettenHistorie` | 194,598 | 55,199 | — | Tracks label lifecycle | ⚠️ Partial |
| Plant | ✅ `dbo.T_Werke` | 536 | 526 | — | Master data for all plants | ⚠️ Partial |
| Account | ✅ `dbo.T_Konten` | 2,167 | 569 | `IdKonto`? | Referenced by `T_Abrufe.IdKonto` | ⚠️ Partial |
| Stock Movements | ✅ `dbo.T_LagerBuchungen` | 1,091,127 | 221,066 | — | **Largest table — 1M+ rows in V1** | ⚠️ Partial |

---

## 3. Universal Reference Candidates

> [!IMPORTANT]
> **This is the most critical discovery item.** Without a universal (or semi-universal)
> reference, we cannot reliably link the entire transfer flow in the Operations timeline.

### 3.1 Candidate Fields — ✅ Phase 2 Confirmed

| Candidate Field | Confirmed In Table Count | Key Tables | Links To | Confidence | Phase 2 Evidence |
|---|---|---|---|---|---|
| `IdJournal` (int) | **16 tables** | `T_Bestellungen`, `T_Bestellpositionen`, `T_BestellungenJournal`, `T_EAIJournal`, `T_EAIJournalPosition`, `T_InhouseLieferungen`, `T_Lieferungen`, `T_Wareneingaenge`, `T_WareneingangPlanungen` | PO ↔ EAI ↔ Delivery ↔ GR | 🟢 **CONFIRMED** | Script `12` Q2: appears on 16 tables across all domains |
| `IdBestellung` (int) | **7 tables** | `T_Bestellungen`, `T_Bestellpositionen`, `T_Bestellung`, `T_BestellungenJournal`, `T_EAIJournalPosition`, `T_Wareneingaenge` | GR → PO (direct) | 🟢 **CONFIRMED** | GR header has direct implicit FK to PO |
| `IdAuftragsAbruf` (int) | **7 tables** | `T_Abrufe`, `T_AuftragsAbrufe`, `T_AuftragsAbrufeHistorie`, `T_EAIJournalPosition`, `T_KundenAbrufe`, `T_LieferscheinPositionen`, `T_Wareneingaenge` | GR ↔ Abruf (bridge) | 🟢 **CONFIRMED** | Critical: bridges delivery note positions to GR |
| `JournalNummer` (nvarchar) | **6 tables** | `T_Bestellpositionen`, `T_Bestellungen`, `T_EAIJournal`, `T_EAIJournalEx`, `T_InhouseLieferungen`, `T_Lieferungen` | Human-readable ref | 🟢 **CONFIRMED** | Separate numbering from IdJournal — display-friendly |
| `IdJournalPosition` (int) | **9 tables** | `T_Bestellpositionen`, `T_EAIJournalPosition`, `T_EAIJournalWarenPosition`, `T_EAIJournalText`, `T_Wareneingaenge`, `T_WareneingangPlanungen` | Position-level detail | 🟢 **CONFIRMED** | Granular position linking across EAI and PO |
| `IdJournalBestellPosition` (int) | **4 tables** | `T_Bestellpositionen`, `T_EAIJournalBestellPosition`, `T_EAIJournalPosition`, `T_Wareneingaenge` | PO position → EAI bridge | 🟢 **CONFIRMED** | Links GR directly to specific EAI PO position record |
| `Beleg` (nvarchar) | **11 tables** | `T_EAIJournalLieferPosition`, `T_EntladePositionen`, `T_LadePositionen`, `T_LieferBuchungen`, `T_SperrJournal`, `T_Wareneingaenge`, `T_WareneingangAuftraege`, `T_WareneingangPlanungen`, `T_WareneingangPositionen` | Document/voucher ref | 🟡 **Medium** | Appears on many entities but format unknown |
| `GUID` (uniqueidentifier) | **18 tables** | `T_EAIJournal`, `T_EAIJournalPosition`, `T_EAIJournalBestellPosition`, `T_EAIJournalWarenPosition`, `T_AuftragsAbrufe`, `T_SperrJournal`, `T_LieferantenReklamationen` | Cross-system correlation | 🟡 **Medium** | Broadly present; may enable cross-plant matching |
| `AXAuftragsNummer` (nvarchar) | **3 tables** | `T_Bestellungen`, `T_EAIJournal`, `T_Wareneingaenge` | AX ERP integration | 🟡 **Medium** | Cross-system ref to Microsoft Dynamics AX |
| `Status` (int) | **18 tables** | `T_Bestellungen`, `T_Wareneingaenge`, `T_Abrufe`, `T_LadeAuftraege`, `T_LieferantenReklamationen`, `T_Lieferscheine` | State tracking | 🟢 **CONFIRMED** | Key for timeline events |

### 3.2 Confirmed Reference Chain Pattern — ✅ Phase 2 Validated

Based on Phase 2 FK, EDI, and universal reference analysis:

```
Pattern B2: Journal-mediated chain with direct FK shortcuts — ✅ CONFIRMED

T_Bestellungen.IdBestellung (PK)
  ├─ T_Bestellpositionen.IdBestellung (IMPLICIT FK) ✅
  │    └─ IdJournalBestellPosition (links to EAI PO position bridge)
  ├─ T_BestellungenJournal.IdBestellung + IdJournal + Revision (history) ✅
  └─ T_Bestellungen.IdJournal → T_EAIJournal.IdJournal (PK) ✅
      ├─ T_EAIJournalPosition (91 cols!) — IdBestellung + IdAuftragsAbruf ✅
      │    ├─ BestellungNummer, LieferscheinNummer, ReferenzBestellungNummer
      │    ├─ T_EAIJournalWarenPosition (goods detail) ✅
      │    └─ T_EAIJournalLieferPosition (delivery detail, barcode) ✅
      ├─ T_EAIJournalBestellPosition (PO position bridge) ✅
      ├─ T_EAIJournalSynch (sync status, TransaktionUID) ✅
      └─ [Cross-plant EDI replication via T_EDIUpload / T_EAIJournalSynch]
          └─ T_Abrufe (created from inbound EDI)
              ├─ IdAuftrag + IdAuftragsAbruf ✅
              └─ T_LadePlanungen.IdAbrufe (EXPLICIT FK ✅) + IdLadeAuftrag (EXPLICIT FK ✅)
                  └─ T_LadePositionen / T_LieferPositionen
                      └─ T_Lieferungen.IdJournal (back to EAI Journal) ✅

  T_Wareneingaenge (GR — 4 independent paths back):
    ├─ IdBestellung (IMPLICIT FK → PO) ✅
    ├─ IdBestellPosition (IMPLICIT FK → PO item) ✅
    ├─ IdJournal (IMPLICIT FK → EAI Journal) ✅
    └─ IdAuftragsAbruf (IMPLICIT FK → Call-off) ✅
        └─ T_WareneingangPlanungen (IdJournal + IdWareneingang) ✅
```

> [!IMPORTANT]
> **Phase 2 key validation**: The GR table (`T_Wareneingaenge`) has **four** independent
> reference paths back to the PO: `IdBestellung`, `IdBestellPosition`, `IdJournal`,
> and `IdAuftragsAbruf`. This is even stronger than Phase 1 predicted (which found three).
> **Additionally**, the only 2 explicit FKs in the logistics domain are in
> `T_LadePlanungen` — confirming this is the loading/delivery planning hub.

### 3.3 Validation Strategy — Phase 2 Status

| Step | Action | Script | Status |
|------|--------|--------|--------|
| 1 | Confirm `IdJournal` reference across PO, GR, and EAI tables | `03`, `12` | ✅ **CONFIRMED** — 16 tables share IdJournal |
| 2 | Confirm `IdBestellung` reference from GR to PO | `03`, `12` | ✅ **CONFIRMED** — Implicit FK, 7 tables |
| 3 | Discover how `T_Abrufe` links to `T_Bestellungen` | `03`, `12` | ✅ **CONFIRMED** — Via `T_EAIJournalPosition.IdAuftragsAbruf` + `IdBestellung` |
| 4 | Check if `IdAuftragsAbruf` on GR matches call-off | `12` Q12-Q14 | ✅ **CONFIRMED** — Both T_Wareneingaenge and T_AuftragsAbrufe share the column |
| 5 | Deep schema inspection of PO, Delivery, GR domain tables | `05`, `06`, `07` | ✅ **Phase 3 COMPLETE** — 90 tables inspected, column schemas confirmed, V1 vs V3 dual-model proven |

---

## 4. Timeline Event Map

### 4.1 Target Timeline

The Operations module will display a visual timeline for each inter-plant transfer. Each step below represents a **business event** that the Logistics user wants to see:

```
Transfer / PO 26
───────────────────────────────────────────────────────
✓ PO Created         │ 2026-05-01 09:30  │ user.name
✓ PO Finalized       │ 2026-05-01 10:15  │ user.name
✓ EDI Sent           │ 2026-05-01 10:16  │ SYSTEM
✓ EDI Received       │ 2026-05-01 10:17  │ SYSTEM
✓ Call-off Created   │ 2026-05-02 08:00  │ user.name
✓ Delivery Planned   │ 2026-05-02 08:30  │ user.name
✓ Loading Started    │ 2026-05-03 07:00  │ user.name
✓ Loading Completed  │ 2026-05-03 11:30  │ user.name
✓ Delivery Note      │ 2026-05-03 12:00  │ SYSTEM
✓ DN EDI Sent        │ 2026-05-03 12:01  │ SYSTEM
✓ DN EDI Received    │ 2026-05-03 12:02  │ SYSTEM
✓ GR Created         │ 2026-05-04 08:00  │ user.name
✓ Items Received     │ 2026-05-04 09:30  │ user.name
✓ GR Completed       │ 2026-05-04 10:00  │ user.name
───────────────────────────────────────────────────────
Current Status: ✅ Completed
```

### 4.2 Event-to-Data Mapping — ✅ VALIDATED (Script 11)

> [!TIP]
> All fields below have been confirmed by Script 11 against real database schemas.
> Previous speculative field names (e.g. `Erstellt`, `GeaendertAm`, `HinzugefuegtVon`) have been
> replaced with confirmed column names (`Add_Date`, `Upd_Date`, `Add_User`, `Upd_User`).

#### A. Viana 1 Standard Timeline MVP (10 events)

| # | Timeline Event | Source Table | Date Field | Status Field(s) | User Field | Qty Field | Reference Key | Display Label (PT) | Sort | Confidence | MVP? |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | PO Created | `T_Bestellungen` | `Add_Date` | `Status`, `UebermittlungsStatus`, `Bestaetigt` | `Add_User` | — | `IdBestellung` | Pedido de compra criado | 10 | 🟢 High | ✅ |
| 2 | PO Revision | `T_BestellungenJournal` | `Add_Date` | — (Revision int) | `Add_User` | — | `IdBestellung`, `IdJournal`, `Revision` | Revisão do pedido | 15 | 🟢 High | ✅ |
| 3 | EDI Journal Created | `T_EAIJournal` | `JournalDatum` / `Add_Date` | `IdJournalStatus` | `Add_User` | — | `IdJournal`, `IdJournalTyp` | Documento EDI criado | 20 | 🟢 High | ✅ |
| 4 | EDI Exported | `T_EAIJournal` | `Exportiert` | `IdJournalStatus` | `Upd_User` | — | `IdJournal` | EDI exportado | 25 | 🟢 High | ✅ |
| 5 | EDI Synced | `T_EAIJournalSynch` | `Add_Date` | `Status` | `Add_User` | — | `IdJournal`, `Aktion` | Sincronização EDI | 30 | 🟢 High | ✅ |
| 6 | Call-off Created | `T_Abrufe` | `AbrufDatum` / `Add_Date` | `AbrufStatus`, `LadeStatus`, `LieferStatus`, `Status`, `AbgleichStatus` | `Add_User` | — | `IdAuftragsAbruf`, `IdAuftrag` | Abruf criado | 40 | 🟢 High | ✅ |
| 7 | Loading Planned | `T_LadePlanungen` | `Add_Date` / `LieferPlanDatum` | `LadeStatus`, `Status` | `Add_User` | — | `IdLadePlanung`, `IdAbrufe`, `IdLadeAuftrag` | Carregamento planejado | 50 | 🟢 High | ✅ |
| 8 | Loading Order | `T_LadeAuftraege` | `LadeDatum` / `Add_Date` | `LadeStatus`, `Status`, `LieferAvisoStatus`, `LieferplanStatus`, `LKWStatus`, `SpedAnfrageStatus`, `WAAvisoStatus` | `Add_User` | — | `IdLadeAuftrag` | Ordem de carregamento | 60 | 🟢 High | ✅ |
| 9 | GR Created | `T_Wareneingaenge` | `Datum` / `Add_Date` | `Status`, `AbgleichStatus`, `BestellPositionStatus`, `EurologStatus` | `Add_User` | — | `IdWareneingang`, `IdBestellPosition` | Recebimento criado | 80 | 🟢 High | ✅ |
| 10 | GR Completed | `T_Wareneingaenge` | `Upd_Date` | `Status` (final value) | `Upd_User` | — | `IdWareneingang` | Recebimento concluído | 90 | 🟡 Medium | ✅ |

**Supporting entities (not in MVP timeline but available for detail views):**

| # | Entity | Source Table | Date Field | Status Field | User Field | Reference Key | Confidence |
|---|--------|-------------|-----------|-------------|-----------|--------------|------------|
| S1 | PO Line Item | `T_Bestellpositionen` | `BestellDatum`, `Lieferdatum`, `Add_Date` | `PositionsStatus` | `Add_User` | `IdBestellPosition`, `IdBestellung` | 🟢 High |
| S2 | Loading Position | `T_LadePositionen` | `ProduktionsDatum`, `Add_Date` | — | `Add_User` | `IdLadePosition`, `IdLadePlanung` | 🟢 High |
| S3 | Delivery Booking | `T_LieferBuchungen` | `Buchungsdatum`, `LieferDatum`, `EinlagerungsDatum` | `GesperrtAktiv` | `Add_User` | `IdLieferBuchung` | 🟢 High |
| S4 | Delivery Note | `T_Lieferscheine` | `Lieferdatum`, `LadeDatum`, `Add_Date` | `Status` | `Add_User`, `Benutzer` | `IdLieferschein` | 🟢 High |
| S5 | GR Order | `T_WareneingangAuftraege` | `Datum`, `ExtLieferscheinDatum`, `Add_Date` | `Status`, `WEAvisoStatus` | `Add_User` | `IdWareneingangAuftrag`, `IdLadeAuftrag` | 🟢 High |
| S6 | GR Plan | `T_WareneingangPlanungen` | `Add_Date` | `LadeStatus`, `Status` | `Add_User` | `IdWareneingangPlanung`, `IdJournal`, `IdLadePlanung` | 🟢 High |
| S7 | GR Position | `T_WareneingangPositionen` | `Add_Date` | — | `Add_User` | `IdWareneingangPosition` | 🟢 High |
| S8 | Delivery | `T_Lieferungen` | `Lieferdatum`, `Add_Date` | — | `Add_User` | `IdLieferung` | 🟢 High |

#### B. Viana 3 In-House Timeline MVP (7 events)

| # | Timeline Event | Source Table | Date Field | Status Field(s) | User Field | Qty Field | Reference Key | Display Label (PT) | Sort | Confidence | MVP? |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | PO Created | `T_Bestellungen` | `Add_Date` | `Status`, `UebermittlungsStatus` | `Add_User` | — | `IdBestellung` | Pedido de compra criado | 10 | 🟢 High | ✅ |
| 2 | PO Revision | `T_BestellungenJournal` | `Add_Date` | — (Revision int) | `Add_User` | — | `IdBestellung`, `IdJournal`, `Revision` | Revisão do pedido | 15 | 🟢 High | ✅ |
| 3 | EDI Journal Created | `T_EAIJournal` | `JournalDatum` / `Add_Date` | `IdJournalStatus` | `Add_User` | — | `IdJournal`, `IdJournalTyp` | Documento EDI criado | 20 | 🟢 High | ✅ |
| 4 | EDI Exported | `T_EAIJournal` | `Exportiert` | `IdJournalStatus` | `Upd_User` | — | `IdJournal` | EDI exportado | 25 | 🟢 High | ✅ |
| 5 | Inhouse Delivery | `T_InhouseLieferungen` | `LieferscheinDatum` / `Add_Date` | — | `Add_User` | — | `IdInhouseLieferung`, `IdJournal` | Entrega interna criada | 50 | 🟢 High | ✅ |
| 6 | GR Created | `T_Wareneingaenge` | `Datum` / `Add_Date` | `Status`, `AbgleichStatus`, `BestellPositionStatus`, `EurologStatus` | `Add_User` | — | `IdWareneingang`, `IdBestellPosition` | Recebimento criado | 80 | 🟢 High | ✅ |
| 7 | GR Completed | `T_Wareneingaenge` | `Upd_Date` | `Status` (final value) | `Upd_User` | — | `IdWareneingang` | Recebimento concluído | 90 | 🟡 Medium | ✅ |

> [!WARNING]
> **`T_InhouseBewegungen`** was NOT captured by Script 11 — it may lack standard `Add_User`/`Upd_User`
> audit columns. Script `10` (article trace) may help clarify this table's structure.
> For the V3 MVP, the Inhouse Delivery event from `T_InhouseLieferungen` covers the in-house pipeline.

#### C. Comparison: V1 vs V3 MVP Timeline

| Step | V1 Standard Event | V3 In-House Event | Shared? |
|------|------------------|-------------------|--------|
| 1 | PO Created | PO Created | ✅ Shared |
| 2 | PO Revision | PO Revision | ✅ Shared |
| 3 | EDI Journal Created | EDI Journal Created | ✅ Shared |
| 4 | EDI Exported | EDI Exported | ✅ Shared |
| 5 | EDI Synced | — (skipped) | ❌ V1 only |
| 6 | Call-off Created | — (skipped) | ❌ V1 only |
| 7 | Loading Planned | — (skipped) | ❌ V1 only |
| 8 | Loading Order | Inhouse Delivery | ❌ Different |
| 9 | GR Created | GR Created | ✅ Shared |
| 10 | GR Completed | GR Completed | ✅ Shared |

### 4.3 Timeline Data Sources: ✅ Strategy D (Hybrid) — DECIDED

Script `13` confirmed: **No temporal tables, no Change Tracking, no Old/New status columns.**
The timeline uses **Strategy D (Hybrid)**: EAI Journal events + Entity Snapshots.

> [!IMPORTANT]
> **Strategy A (History Tables)**: ❌ NOT viable — 28 history tables exist but NONE for the core logistics pipeline.
> **Strategy C (Temporal/CT)**: ❌ NOT viable — `ChangeTrackingEnabled=0`, 0 temporal tables.
> **Strategy D (Hybrid)**: ✅ SELECTED — EAI Journal events for EDI milestones + `Add_Date`/`Upd_Date` for entity milestones.

#### How the Hybrid Timeline Works

```
-- Strategy D: UNION ALL across entity creation/modification dates + journal events
-- V1 Standard Pipeline
SELECT 'PO Created' AS Event, b.Add_Date AS EventDate, b.Add_User AS EventUser
FROM [dbo].[T_Bestellungen] b WHERE b.IdBestellung = @POId

UNION ALL

SELECT 'EDI Sent' AS Event, j.Exportiert AS EventDate, j.Add_User AS EventUser
FROM [dbo].[T_EAIJournal] j WHERE j.IdJournal = @JournalId

UNION ALL

SELECT 'Call-off Created' AS Event, a.Add_Date AS EventDate, a.Add_User AS EventUser
FROM [dbo].[T_Abrufe] a WHERE a.IdAuftragsAbruf = @AbrufId

UNION ALL

-- ... Loading, Delivery, GR events ...

ORDER BY EventDate ASC
```

#### Strategy Comparison (Script 13 Result)

| Aspect | Strategy A (History) | Strategy C (Temporal) | ✅ Strategy D (Hybrid) |
|--------|---------------------|----------------------|----------------------|
| Availability | ❌ No logistics history tables | ❌ CT=0, Temporal=0 | ✅ Available on all tables |
| Timeline accuracy | N/A | N/A | 🟡 Creation + last modification per entity |
| Event granularity | N/A | N/A | 🟢 EDI events from journal + entity milestones |
| User attribution | N/A | N/A | ✅ `Add_User`/`Upd_User` on every entity |
| Intermediate states | N/A | N/A | ❌ Only 2 snapshots per entity (created, last modified) |
| Implementation | N/A | N/A | 🟡 UNION ALL across entity tables |

#### V1 vs V3 Conditional Pipeline

| Timeline Event | V1 Source (Standard) | V3 Source (In-House) |
|----------------|---------------------|---------------------|
| PO Created | `T_Bestellungen.Add_Date` | Same |
| PO Revision | `T_BestellungenJournal.Revision + Add_Date` | Same |
| EDI Sent | `T_EAIJournal.Exportiert + JournalDatum` | Same |
| EDI Synced | `T_EAIJournalSynch.Add_Date + Status` | Same |
| Call-off Created | `T_Abrufe.Add_Date` | — (skipped) |
| Loading Planned | `T_LadePlanungen.Add_Date` | — (skipped) |
| Loading Order | `T_LadeAuftraege.LadeDatum + LadeStatus` | — (skipped) |
| Loading Completed | `T_LadeAuftraege.Upd_Date + LadeStatus` | — (skipped) |
| Delivery Made | `T_Lieferungen.Add_Date + IdJournal` | `T_InhouseLieferungen.Add_Date + IdJournal` |
| Inhouse Movement | — (skipped) | `T_InhouseBewegungen.Add_Date` |
| GR Created | `T_Wareneingaenge.Datum + Add_Date` | Same |
| GR Completed | `T_Wareneingaenge.Upd_Date + Status` | Same |

### 4.4 Timeline Investigation Script Reference

| Aspect | Script | Key Queries |
|--------|--------|-------------|
| Date fields per entity | `11` | Q1-Q3 (datetime columns) |
| Status fields per entity | `11` | Q4-Q5 (status columns + date pairs) |
| User fields per entity | `11` | Q6 (user/audit columns) |
| Event profiles per domain | `11` | Q7-Q10 (complete profiles) |
| History/audit tables | `13` | Q1-Q6 (dedicated history tables) |
| Journal as history | `13` | Q13-Q15 (journal structure + sample) |
| Old/New status pairs | `13` | Q10 (status transition columns) |
| Created/Modified pairs | `13` | Q11-Q12 (timestamp pairs) |
| SQL Server change tracking | `13` | Q17-Q19 (temporal/CDC features) |

### 4.5 Timeline Prototype Queries — ✅ COMPLETE

Full documentation-only prototype SQL queries have been created:

👉 **[OPERATIONS_TIMELINE_QUERY_PROTOTYPES.md](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/OPERATIONS_TIMELINE_QUERY_PROTOTYPES.md)**

| Prototype | Events | Status |
|-----------|--------|--------|
| V1 Standard Timeline MVP | 10 events via `UNION ALL` | ✅ Created |
| V3 Inhouse Timeline MVP | 7 events via `UNION ALL` | ✅ Created |
| Conditional Pipeline Detection | `STANDARD` / `INHOUSE` / `PARTIAL` | ✅ Created |
| `T_InhouseBewegungen` Gap Analysis | Deferred for MVP | ✅ Documented |
| Open Questions | 13 questions catalogued (OQ1–OQ13) | ✅ Documented |

---

## 5. Validation Workflow

### How to Update This Document After Running Scripts

1. **Run Phase 1 scripts** (`01`, `02`) — Update "Candidate Table" → "Confirmed Table" for each entity
2. **Run Phase 2 scripts** (`03`, `04`, `08`, `12`) — Update "Link Field", "Primary Key", "Parent Key"
3. **Run Phase 3 scripts** (`05`, `06`, `07`, `10`, `11`) — Update date/status/user fields; mark events as validated
4. **Run Phase 4 scripts** (`09`, `13`) — Update source/target plant fields and timeline strategy

### Validation Status Legend

| Status | Meaning |
|--------|---------|
| ⬜ Not validated | No real SQL output yet |
| 🔍 Investigating | Script has been run, analyzing results |
| ✅ Confirmed | Real SQL output confirms the mapping |
| ❌ Refuted | Real SQL output shows this mapping is wrong |
| ⚠️ Partial | Some fields confirmed, others still unknown |
| 🔄 Revised | Original mapping was wrong, updated with new findings |

---

## 6. Cross-Reference to SQL Scripts

| Script | What It Validates In This Document |
|--------|-----------------------------------|
| [01](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/01_schema_inspection.sql) | Table names in Entity Map (Section 2) |
| [02](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/02_column_search_german_labels.sql) | Column names in Entity Map (Section 2) |
| [03](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/03_foreign_key_inspection.sql) | Parent Key and Link Field in Entity Map |
| [04](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/04_index_inspection.sql) | Primary Key in Entity Map |
| [05](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/05_purchase_order_trace.sql) | PO entities validation |
| [06](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/06_delivery_plan_trace.sql) | Delivery/Loading entities validation |
| [07](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/07_goods_receipt_trace.sql) | Goods Receipt entities validation |
| [08](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/08_edi_investigation.sql) | EDI entities in Entity Map + Universal Reference |
| [09](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/09_cross_plant_linking.sql) | Source/Target Plant fields |
| [10](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/10_article_variant_trace.sql) | Article/Variant/Packaging entities |
| [11](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/11_business_event_candidates.sql) | Timeline Event Map date/status/user fields |
| [12](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/12_universal_reference_discovery.sql) | Universal Reference Candidates (Section 3) |
| [13](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/13_audit_status_history_discovery.sql) | Timeline Strategy selection (Section 4.3) |
| [Prototype](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/OPERATIONS_TIMELINE_QUERY_PROTOTYPES.md) | Timeline prototype queries (Section 4.5) |

