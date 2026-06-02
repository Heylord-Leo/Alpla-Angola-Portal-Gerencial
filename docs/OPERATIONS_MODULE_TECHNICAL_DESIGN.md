# Operations Module — Technical Design

> **Status**: Phases 1–5 IMPLEMENTED (v2.162.0–v2.170.0) — Transfer details and barcode tracking remain design-only.  
> **Date**: 2026-06-01  
> **Author**: AI Agent (discovery-validated)  
> **Parent Documents**:  
> - [OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md)  
> - [OPERATIONS_ENTITY_MAP.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_ENTITY_MAP.md)  
> - [OPERATIONS_TIMELINE_QUERY_PROTOTYPES.md](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/OPERATIONS_TIMELINE_QUERY_PROTOTYPES.md)  
> - [INTEGRATION_PLAYBOOK.md](file:///c:/dev/alpla-portal/docs/INTEGRATION_PLAYBOOK.md)

> [!IMPORTANT]
> This document is a **technical design specification**. It proposes architecture, file structure, DTOs, and API contracts. **No code has been implemented.** Implementation requires explicit user approval and follows the standard task lifecycle.

---

## 1. Context Summary

### 1.1 Discovery Conclusions

14 SQL discovery scripts were executed across 3 AlplaPROD databases. Key findings:

| Finding | Result |
|---------|--------|
| Total tables per database | 421 |
| Database schema consistency | Identical across V1/V2/V3 |
| Status-history tables for logistics | None |
| SQL temporal tables | None (`TemporalType = 0` for all) |
| Change Tracking | Disabled |
| CDC | Not detected |
| Linked servers | None |
| Cross-database joins | None |
| EAI Journal as event source | ✅ Available |
| Entity audit columns (`Add_Date`/`Upd_Date`) | ✅ Universal |
| Status lookup tables | Do NOT exist — integer conventions only |

### 1.2 Plant Models

| Plant | Server | Database | Model |
|-------|--------|----------|-------|
| Viana 1 | `AOVIA1VMS006` | `AlplaPROD_aovia1` | **Standard Logistics** |
| Viana 2 | `AOVIA2VMS006` | `AlplaPROD_aovia2` | **Standard Logistics** |
| Viana 3 | `AOVIA1VMS006` | `AlplaPROD_aovia3` | **Inhouse** |

### 1.3 Pipeline Models

**Standard Logistics Model** (Viana 1, Viana 2):

```
PO → EAI Journal → Abruf → LadePlanungen → LadeAuftraege → Wareneingang
```

**Inhouse Model** (Viana 3):

```
PO → EAI Journal → InhouseLieferungen → Wareneingang
```

### 1.4 Timeline Strategy

**Strategy D — Hybrid Timeline** (EAI Journal + Entity Snapshots)

Rationale:
- No status-history tables exist for logistics entities
- No SQL temporal tables, Change Tracking, or CDC
- `T_EAIJournal` provides event data for EDI steps (12,641 records in V1)
- Entity tables provide reliable snapshot dates: `Add_Date`, `Upd_Date`, `JournalDatum`, `AbrufDatum`, `LadeDatum`, `LieferscheinDatum`, `Datum`
- Cross-tab analysis confirms no `*Vorher*`/`*Nachher*` (old/new) status columns exist

---

## 2. Read-Only Architecture

> [!CAUTION]
> The Operations module is **strictly read-only** against AlplaPROD databases. This is a permanent architectural constraint, not a phased limitation.

### 2.1 Hard Rules

| Rule | Enforcement |
|------|-------------|
| ✅ Allowed | `SELECT` statements only |
| ❌ Forbidden | `INSERT`, `UPDATE`, `DELETE`, `MERGE` |
| ❌ Forbidden | `TRUNCATE`, `DROP`, `ALTER`, `CREATE` |
| ❌ Forbidden | Data-changing `EXEC` / `sp_executesql` |
| ❌ Forbidden | `OPENROWSET`, `OPENDATASOURCE`, `xp_cmdshell` |
| ❌ Forbidden | Credentials or connection strings in source code |

### 2.2 SQL User

| Property | Value |
|----------|-------|
| Login | `alplaprod_viewer` |
| Role | `db_datareader` |
| Access | `HAS_DBACCESS = 1` on all 3 databases |
| Permissions | `SELECT` only — no write permissions |

---

## 3. Database Connection Architecture

### 3.1 Design: One Provider, Multi-Plant Configuration

Following the established Primavera pattern (`PrimaveraConnectionFactory` → `PrimaveraCompany` enum → per-company `DatabaseName`), AlplaPROD will use:

```
AlplaProdConnectionFactory → AlplaProdPlant enum → per-plant Server/DatabaseName
```

**Key difference from Primavera**: AlplaPROD has **two physical servers** (`AOVIA1VMS006`, `AOVIA2VMS006`), whereas Primavera has one server with multiple databases. The configuration must support per-plant `Server` overrides.

### 3.2 Configuration Model

```json
"Integrations": {
    "AlplaProd": {
        "Enabled": false,
        "AuthenticationMode": "SQL",
        "Username": "",
        "Password": "",
        "TimeoutSeconds": 30,
        "Plants": {
            "VIANA1": {
                "Server": "AOVIA1VMS006",
                "DatabaseName": "AlplaPROD_aovia1",
                "Enabled": true,
                "PipelineModel": "STANDARD"
            },
            "VIANA2": {
                "Server": "AOVIA2VMS006",
                "DatabaseName": "AlplaPROD_aovia2",
                "Enabled": true,
                "PipelineModel": "STANDARD"
            },
            "VIANA3": {
                "Server": "AOVIA1VMS006",
                "DatabaseName": "AlplaPROD_aovia3",
                "Enabled": true,
                "PipelineModel": "INHOUSE"
            }
        }
    }
}
```

### 3.3 Connection String Generation

```
Server={Plant.Server};
Database={Plant.DatabaseName};
User Id={AlplaProd.Username};
Password={AlplaProd.Password};
TrustServerCertificate=True;
Encrypt=Optional;
Connection Timeout={AlplaProd.TimeoutSeconds};
Application Name=AlplaPortal_Operations;
```

Notes:
- `TrustServerCertificate=True` — required for legacy SQL infrastructure (per Phase 1B learnings)
- `Encrypt=Optional` — avoids 21-second timeout on handshake negotiation (per Primavera stabilization findings)
- `Application Name` — for SQL Server activity monitoring and diagnostics
- Shared credentials across plants (same `alplaprod_viewer` login works on both servers)

### 3.4 Cross-Plant Independence

Discovery confirmed:
- No linked servers between `AOVIA1VMS006` and `AOVIA2VMS006`
- No cross-database synonyms or 4-part-name views
- No cross-server stored procedure references
- Each database is self-contained
- Inter-plant communication uses EAI/EDI Web Services (`T_EDIKonfigurationen`)

**Consequence**: The Portal queries each plant database independently. Cross-plant correlation is performed in application memory using `T_Adressen` (V1↔V2: IdAdressen 52↔25) and `T_Werke` metadata.

---

## 4. Backend Design

### 4.1 Layer Architecture

```
┌─────────────────────────────────────────────────────┐
│  Frontend: OperationsTransfers.tsx                  │
│  (filters, list, timeline drawer, status badges)    │
├─────────────────────────────────────────────────────┤
│  API: OperationsController                          │
│  GET  /api/operations/transfers                     │
│  GET  /api/operations/transfers/{id}/timeline       │
│  GET  /api/operations/transfers/{id}/details        │
│  GET  /api/operations/plants                        │
├─────────────────────────────────────────────────────┤
│  Service: IOperationsService                        │
│  (orchestrates plant resolution + query execution)  │
├─────────────────────────────────────────────────────┤
│  Timeline: IOperationsTimelineService               │
│  (builds UNION ALL timeline from entity snapshots)  │
├─────────────────────────────────────────────────────┤
│  Plant: IOperationsPlantResolver                    │
│  (resolves config, validates plant, detects model)  │
├─────────────────────────────────────────────────────┤
│  Query: OperationsTimelineQueryBuilder              │
│  (generates parameterized SQL for Standard/Inhouse) │
├─────────────────────────────────────────────────────┤
│  Connection: AlplaProdConnectionFactory             │
│  (shared read-only factory, plant-aware routing)    │
├─────────────────────────────────────────────────────┤
│  Provider: AlplaProdIntegrationProvider              │
│  (IIntegrationProvider for health checks)           │
└─────────────────────────────────────────────────────┘
```

### 4.2 File Structure (Proposed)

#### Infrastructure Layer

```
AlplaPortal.Infrastructure/Services/Integration/
├── AlplaProdConnectionFactory.cs          # Shared connection factory
├── AlplaProdIntegrationProvider.cs        # IIntegrationProvider for health
├── AlplaProdPlant.cs                      # Enum: VIANA1, VIANA2, VIANA3
├── AlplaProdPipelineModel.cs              # Enum: STANDARD, INHOUSE
└── Operations/
    ├── OperationsService.cs               # IOperationsService implementation
    ├── OperationsTimelineService.cs        # Timeline query composition
    ├── OperationsTimelineQueryBuilder.cs   # SQL generation (Standard + Inhouse)
    ├── OperationsPlantResolver.cs          # Plant config resolution + model detection
    └── OperationsPipelineDetector.cs       # Runtime STANDARD/INHOUSE/PARTIAL detection
```

#### Application Layer (DTOs + Interfaces)

```
AlplaPortal.Application/
├── DTOs/Operations/
│   ├── OperationsTransferListDto.cs       # Transfer list item
│   ├── OperationsTransferDetailDto.cs     # Full transfer details
│   ├── OperationsTimelineEventDto.cs      # Normalized timeline event
│   ├── OperationsTimelineResponseDto.cs   # Timeline with metadata
│   ├── OperationsMaterialDto.cs           # Article/variant display
│   ├── OperationsPackagingDto.cs          # Packaging display
│   ├── OperationsPlantDto.cs              # Plant information
│   └── OperationsTransferFilterDto.cs     # List filter parameters
├── Interfaces/
│   ├── IOperationsService.cs
│   ├── IOperationsTimelineService.cs
│   └── IOperationsPlantResolver.cs
```

#### API Layer

```
AlplaPortal.Api/Controllers/
└── OperationsController.cs                # REST endpoints
```

### 4.3 AlplaProdPlant Enum

```csharp
public enum AlplaProdPlant
{
    VIANA1,   // AOVIA1VMS006 / AlplaPROD_aovia1 — Standard Logistics
    VIANA2,   // AOVIA2VMS006 / AlplaPROD_aovia2 — Standard Logistics
    VIANA3    // AOVIA1VMS006 / AlplaPROD_aovia3 — Inhouse
}
```

### 4.4 AlplaProdPipelineModel Enum

```csharp
public enum AlplaProdPipelineModel
{
    STANDARD,   // V1/V2: PO → EAI → Abruf → Loading → GR
    INHOUSE,    // V3: PO → EAI → InhouseLieferungen → GR
    PARTIAL     // Only PO/EAI/GR data available (incomplete pipeline)
}
```

### 4.5 AlplaProdConnectionFactory

Mirrors `PrimaveraConnectionFactory` pattern:

| Method | Purpose |
|--------|---------|
| `CreateConnection(AlplaProdPlant)` | Returns `SqlConnection` for given plant |
| `IsPlantConfigured(AlplaProdPlant)` | Checks if plant has valid config |
| `GetConfiguredPlants()` | Returns all enabled plants |

Key design:
- Reads `Integrations:AlplaProd` from `IConfiguration`
- Resolves per-plant `Server` + `DatabaseName`
- Builds connection string with shared credentials
- Throws `InvalidOperationException` if plant not configured/disabled

### 4.6 AlplaProdIntegrationProvider

Follows `IIntegrationProvider` contract:

```csharp
public class AlplaProdIntegrationProvider : IIntegrationProvider
{
    public string Code => "ALPLAPROD";
    public string ProviderType => "PRODUCTION";
    public string ConnectionType => "SQL";

    // Tests first enabled plant connection
    public async Task<IntegrationConnectionTestResult> TestConnectionAsync(CancellationToken ct);
}
```

Seed data:
```csharp
new IntegrationProvider
{
    Id = <next_id>,
    Code = "ALPLAPROD",
    Name = "AlplaPROD 1.0 (Production)",
    ProviderType = "PRODUCTION",
    ConnectionType = "SQL",
    Description = "AlplaPROD 1.0 production databases — Viana 1, 2, 3",
    Environment = "PRODUCTION",
    IsEnabled = false,
    IsPlanned = true,
    DisplayOrder = 40,
    Capabilities = "[\"OPERATIONS\",\"TRANSFERS\",\"LOGISTICS\"]"
}
```

---

## 5. Main Backend Use Cases

### 5.1 Use Case 1 — List Transfers

**Purpose**: Show a paginated list of inter-plant transfers / purchase orders.

**Endpoint**: `GET /api/operations/transfers`

**Parameters**:

| Param | Type | Required | Notes |
|-------|------|----------|-------|
| `plant` | `string` | Yes | `VIANA1`, `VIANA2`, `VIANA3`, or `ALL` |
| `dateFrom` | `date` | Yes | Minimum date filter (prevents full table scans) |
| `dateTo` | `date` | Yes | Maximum date filter |
| `status` | `string` | No | `ACTIVE`, `COMPLETED`, `CANCELLED` |
| `pipelineModel` | `string` | No | `STANDARD`, `INHOUSE`, `PARTIAL` |
| `articleSearch` | `string` | No | Search by `T_Artikelvarianten.Bezeichnung` |
| `poSearch` | `string` | No | Search by PO number (`JournalNummer`) |
| `page` | `int` | No | Default 1 |
| `pageSize` | `int` | No | Default 25, max 100 |

**Source query** (per plant):
```sql
SELECT TOP (@pageSize)
    b.IdBestellung, b.Status, b.Revision, b.Bestaetigt,
    b.Add_Date, b.Upd_Date, b.Add_User,
    ej.IdJournal, ej.JournalNummer, ej.IdJournalStatus,
    ej.JournalDatum, ej.Exportiert,
    -- Material display
    av.Bezeichnung AS MaterialName,
    avt.Bezeichnung AS VariantTypeName,
    at2.Bezeichnung AS ArticleTypeName
FROM dbo.T_Bestellungen b
LEFT JOIN dbo.T_EAIJournal ej ON b.IdJournal = ej.IdJournal
LEFT JOIN dbo.T_Bestellpositionen bp ON b.IdBestellung = bp.IdBestellung
LEFT JOIN dbo.T_Artikelvarianten av ON bp.IdArtikelvarianten = av.IdArtikelvarianten
LEFT JOIN dbo.T_ArtikelvariantenTyp avt ON av.IdArtikelvariantenTyp = avt.IdArtikelvariantenTyp
LEFT JOIN dbo.T_Artikeltyp at2 ON av.IdArtikeltyp = at2.IdArtikeltyp
WHERE b.Add_Date BETWEEN @dateFrom AND @dateTo
  AND b.Status >= 1
ORDER BY b.Add_Date DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
```

### 5.2 Use Case 2 — Get Transfer Timeline

**Purpose**: Return normalized timeline events for one transfer.

**Endpoint**: `GET /api/operations/transfers/{plant}/{idBestellung}/timeline`

**Parameters**:

| Param | Type | Required | Notes |
|-------|------|----------|-------|
| `plant` | `string` | Yes | Route param |
| `idBestellung` | `int` | Yes | Route param |

**Response**: `OperationsTimelineResponseDto` containing an array of `OperationsTimelineEventDto`.

**Logic**:
1. Resolve plant connection via `AlplaProdConnectionFactory`
2. Detect pipeline model via `OperationsPipelineDetector`
3. Execute Standard or Inhouse timeline query via `OperationsTimelineQueryBuilder`
4. Return ordered timeline events

### 5.3 Use Case 3 — Get Transfer Details

**Purpose**: Return header details, material, quantities, packaging, status, and key references.

**Endpoint**: `GET /api/operations/transfers/{plant}/{idBestellung}/details`

**Response**: `OperationsTransferDetailDto` with sections:
- Header (PO data, dates, users)
- Material (article variant, type, classification, color)
- Packaging (VpkVorschrift, quantities per packaging)
- Status (current status values with human-readable meanings)
- References (IdJournal, JournalNummer, IdAuftragsAbruf, etc.)
- Pipeline model (STANDARD / INHOUSE / PARTIAL)

### 5.4 Use Case 4 — Detect Pipeline Model

**Purpose**: Determine whether a transfer should render as STANDARD, INHOUSE, or PARTIAL.

**Detection Logic**:

```
Input: plant, IdBestellung, IdJournal

1. Check T_Abrufe for matching IdAuftragsAbruf (linked via T_Bestellpositionen):
   IF rows found → candidate = STANDARD

2. Check T_InhouseLieferungen for matching IdJournal:
   IF rows found → candidate = INHOUSE

3. Check T_LadePlanungen / T_LadeAuftraege for matching IdAbrufe:
   IF rows found → confirmed = STANDARD

4. If no Abrufe and no InhouseLieferungen → model = PARTIAL

Return: AlplaProdPipelineModel
```

> [!NOTE]
> For V1/V2, pipeline detection can shortcut to `STANDARD` based on plant config (`PipelineModel = "STANDARD"`). Runtime detection is still valuable for edge cases and V3 where both models coexist theoretically.

---

## 6. Timeline DTO Design

### 6.1 OperationsTimelineEventDto

```csharp
public class OperationsTimelineEventDto
{
    // Display ordering
    public int SortOrder { get; set; }

    // Event identification
    public string EventCode { get; set; }           // e.g. "PO_CREATED"
    public string EventLabelPT { get; set; }         // "Pedido de compra criado"
    public string EventLabelEN { get; set; }         // "Purchase order created"

    // Source traceability
    public string SourceTable { get; set; }          // "T_Bestellungen"

    // Event timing
    public DateTime? EventDate { get; set; }
    public string EventUser { get; set; }

    // Status context
    public int? MainStatus { get; set; }
    public int? SecondaryStatus { get; set; }
    public string StatusMeaning { get; set; }        // Human-readable interpretation

    // Severity / visual
    public string Severity { get; set; }             // "success", "info", "warning", "error"
    public bool IsCompleted { get; set; }
    public bool IsTechnical { get; set; }            // True for EDI_SYNCED, etc.

    // Entity references
    public int? IdBestellung { get; set; }
    public int? IdBestellPosition { get; set; }
    public int? IdJournal { get; set; }
    public string JournalNummer { get; set; }
    public int? IdAuftragsAbruf { get; set; }
    public int? IdAbrufe { get; set; }
    public int? IdLadePlanung { get; set; }
    public int? IdLadeAuftrag { get; set; }
    public int? IdWareneingang { get; set; }
    public int? IdInhouseLieferung { get; set; }

    // Display data
    public string ReferenceNumber { get; set; }      // PO number, journal number
    public string MaterialName { get; set; }
    public string ArticleAlias { get; set; }
    public string ArticleVariantType { get; set; }
    public string PackagingName { get; set; }
    public double? Quantity { get; set; }
    public string QuantityUnit { get; set; }
    public string Notes { get; set; }
}
```

### 6.2 OperationsTimelineResponseDto

```csharp
public class OperationsTimelineResponseDto
{
    // Plant context
    public string Plant { get; set; }                // "VIANA1"
    public string PlantServer { get; set; }          // "AOVIA1VMS006"
    public string PlantDatabase { get; set; }        // "AlplaPROD_aovia1"

    // Transfer identity
    public int IdBestellung { get; set; }
    public string JournalNummer { get; set; }

    // Pipeline model
    public string PipelineModel { get; set; }        // "STANDARD", "INHOUSE", "PARTIAL"
    public int ExpectedEventCount { get; set; }      // 10 or 7
    public int CompletedEventCount { get; set; }

    // Timeline events
    public List<OperationsTimelineEventDto> Events { get; set; }

    // Metadata
    public DateTime QueryTimestamp { get; set; }
    public long QueryDurationMs { get; set; }
}
```

---

## 7. Timeline Event Rules

### 7.1 Standard Timeline — Viana 1 / Viana 2 (10 Events)

| # | EventCode | Table | Date Field | Condition | Sort | Label PT | Label EN | Severity |
|---|-----------|-------|------------|-----------|------|----------|----------|----------|
| 1 | `PO_CREATED` | `T_Bestellungen` | `Add_Date` | `Status >= 1` | 10 | Pedido de compra criado | Purchase order created | info |
| 2 | `PO_REVISION` | `T_BestellungenJournal` | `Add_Date` | `Revision > 1` | 15 | Revisão do pedido | Purchase order revision | info |
| 3 | `EDI_CREATED` | `T_EAIJournal` | `JournalDatum` | `IdJournalStatus IN (11, 91)` | 20 | Documento EDI criado | EDI document created | info |
| 4 | `EDI_EXPORTED` | `T_EAIJournal` | `JournalDatum` | `IdJournalStatus IN (62, 64)` | 25 | EDI exportado | EDI exported | info |
| 5 | `EDI_SYNCED` | `T_EAIJournalSynch` | `Upd_Date` | `Status = 1` | 30 | Sincronização EDI | EDI synchronized | success |
| 6 | `CALLOFF_CREATED` | `T_Abrufe` | `AbrufDatum` | `AbrufStatus >= 1` | 40 | Abruf criado | Call-off created | info |
| 7 | `LOADING_PLANNED` | `T_LadePlanungen` | `Add_Date` | `Status IN (1, 11, 21)` | 50 | Carregamento planejado | Loading planned | info |
| 8 | `LOADING_ORDER` | `T_LadeAuftraege` | `Add_Date` | `Status IN (1, 11, 21)` | 60 | Ordem de carregamento | Loading order | info |
| 9 | `GR_CREATED` | `T_Wareneingaenge` | `Add_Date` | `Status >= 0` | 70 | Recebimento criado | Goods receipt created | info |
| 10 | `GR_COMPLETED` | `T_Wareneingaenge` | `Upd_Date` | `Status = 21` | 80 | Recebimento concluído | Goods receipt completed | success |

### 7.2 Inhouse Timeline — Viana 3 (7 Events)

| # | EventCode | Table | Date Field | Condition | Sort | Label PT | Label EN |
|---|-----------|-------|------------|-----------|------|----------|----------|
| 1 | `PO_CREATED` | `T_Bestellungen` | `Add_Date` | `Status >= 1` | 10 | Pedido de compra criado | Purchase order created |
| 2 | `PO_REVISION` | `T_BestellungenJournal` | `Add_Date` | `Revision > 1` | 15 | Revisão do pedido | Purchase order revision |
| 3 | `EDI_CREATED` | `T_EAIJournal` | `JournalDatum` | `IdJournalStatus IN (11, 91)` | 20 | Documento EDI criado | EDI document created |
| 4 | `EDI_EXPORTED` | `T_EAIJournal` | `JournalDatum` | `IdJournalStatus IN (62, 64)` | 25 | EDI exportado | EDI exported |
| 5 | `INHOUSE_DELIVERY` | `T_InhouseLieferungen` | `Add_Date` | Row exists | 45 | Entrega interna criada | Inhouse delivery created |
| 6 | `GR_CREATED` | `T_Wareneingaenge` | `Add_Date` | `Status >= 0` | 70 | Recebimento criado | Goods receipt created |
| 7 | `GR_COMPLETED` | `T_Wareneingaenge` | `Upd_Date` | `Status = 21` | 80 | Recebimento concluído | Goods receipt completed |

---

## 8. Status Interpretation Table

> [!NOTE]
> All status values are integer conventions validated by Script 14 across V1/V2/V3. No lookup tables exist in the database — interpretations are documented here and must be hardcoded in the application layer.

### 8.1 T_Bestellungen (Purchase Orders)

| Status | Meaning | Badge | Is Terminal |
|--------|---------|-------|-------------|
| 1 | Draft / New | `Rascunho` | No |
| 2 | Submitted | `Submetido` | No |
| 3 | Cancelled | `Cancelado` | Yes |
| 4 | Pending Review | `Em Revisão` | No |
| 5 | Partially Processed | `Parcial` | No |
| 6 | Active / Exported | `Ativo` | No |
| 7 | Completed (legacy) | `Concluído` | Yes |
| 8 | Completed (confirmed) | `Concluído` | Yes |
| 11 | Special / Error | `Erro` | No |

**`UebermittlungsStatus`**: Always `1` — do not use for timeline decisions.

**`Bestaetigt`**: `0` = unconfirmed, `1` = confirmed. Status 7+8 always have `Bestaetigt=1`.

**`Revision`**: `-1` = draft, `1` = initial, `≥2` = revision.

### 8.2 T_EAIJournal (EAI Journal)

| IdJournalStatus | Meaning | JournalTyp | QuellModul |
|-----------------|---------|------------|------------|
| 11 | PO Created | 1 (PO) | -1 / 10 |
| 12 | PO Error | 1 (PO) | 10 |
| 62 | Delivery Note / Loading | 6 (Delivery) | 5 |
| 64 | Delivery Discrepancy | 6 (Delivery) | 5 |
| 91 | PO Revision Active | 9 (Revision) | 14 |
| 92 | PO Revision Completed | 9 (Revision) | 14 |
| 93 | PO Revision Cancelled | 9 (Revision) | 14 |
| 94 | PO Revision Closed | 9 (Revision) | 14 |
| 101 | Transfer | 10 | 5 |

> [!WARNING]
> **`Exportiert` is a sentinel date** (`1900-01-01`) — it is always set, never `NULL`. Do NOT use it as a timestamp. Use `JournalDatum` instead.

### 8.3 T_EAIJournalSynch (EAI Sync)

| Status | Meaning | Badge |
|--------|---------|-------|
| 0 | Pending | `Pendente` |
| 1 | Synced / Complete | `Sincronizado` |
| 2 | Error | `Erro` |

| Aktion | Meaning |
|--------|---------|
| 1 | Export (outbound) |
| 2 | Import (inbound) |
| 3 | PO Sync |

### 8.4 T_Abrufe (Call-offs)

> [!WARNING]
> `T_Abrufe.Status` is always `0` — do NOT use as primary timeline status. Use `AbrufStatus` instead.

| AbrufStatus | Meaning | Badge |
|-------------|---------|-------|
| 1 | Open | `Aberto` |
| 2 | Partially Loaded | `Parcial` |
| 3 | Fully Loaded | `Carregado` |

| LadeStatus | Meaning |
|------------|---------|
| 0 | Not planned |
| 1 | Planning |
| 10 | Partially planned |
| 11 | Fully planned |
| 12 | Over-planned |

| LieferStatus | Meaning |
|--------------|---------|
| 0 | Not delivered |
| 20 | Partially delivered |
| 21 | Fully delivered |
| 22 | Over-delivered |

### 8.5 T_LadeAuftraege (Loading Orders)

| Status | Meaning | Badge |
|--------|---------|-------|
| 0 | Draft | `Rascunho` |
| 1 | New / Pending | `Pendente` |
| 6 | Cancelled | `Cancelado` |
| 11 | In Progress / Loaded | `Em Progresso` |
| 21 | Completed / Dispatched | `Concluído` |

| LadeStatus | Meaning |
|------------|---------|
| 0 | Not loaded |
| 1 | Loading started |
| 10 | Partially loaded |
| 11 | Fully loaded |
| 12 | Over-loaded |

### 8.6 T_Wareneingaenge (Goods Receipts)

| Status | Meaning | Badge | Is Terminal |
|--------|---------|-------|-------------|
| 0 | Draft / Pending | `Pendente` | No |
| 1 | New | `Novo` | No |
| 6 | Cancelled | `Cancelado` | Yes |
| 11 | In Progress | `Em Progresso` | No |
| 21 | Completed | `Concluído` | Yes |

**GR_COMPLETED rule**: Use `Upd_Date` only when `Status = 21`.

### 8.7 Stage Derivation — Partial Receipt Logic (v2.175.0)

> [!IMPORTANT]
> `GR_COMPLETED` in the timeline does **not** automatically mean the full PO is complete.
> A transfer can have one or more completed receipt transactions (`T_Wareneingaenge.Status = 21`) but still be only partially received at PO level.

The frontend `deriveCurrentStage()` function uses both timeline events and detail quantity data to determine the correct stage label.

**Priority rules:**

| Priority | Condition | Etapa atual |
|----------|-----------|-------------|
| 1 | PO status = `Parcialmente entregue` (Status 5) | `Parcialmente recebido` |
| 2 | Detail qty: `orderedQty > 0`, `receivedQty > 0`, `receivedQty < orderedQty` | `Parcialmente recebido` |
| 3 | PO status = `Concluído` OR `receivedQty >= orderedQty` OR `openQty = 0` | `Recebimento concluído` |
| 4 | `GR_CREATED` event exists but is not completed | `Aguardando recebimento` |
| 5 | Fallback to timeline-based stage priority | (varies by event) |

**Data sources:**
- PO status: From `PO_CREATED` event's `statusMeaning` (resolved by `OperationsStatusMapper`)
- Quantity data: From detail endpoint (`OperationsTransferDetailDto.quantity`) — available when the drawer is open
- When detail data is unavailable (manual lookup), the function falls back to PO status + timeline-only logic

**Validation references:**
- PO `#3429` — partial receipt → `Parcialmente recebido`
- PO `#3579` — fully completed → `Recebimento concluído`
- PO `#3581` — pending receipt → `Aguardando recebimento`

---

## 9. Material Display Design

### 9.1 Primary Display Fields

| Purpose | Table | Column | Example |
|---------|-------|--------|---------|
| **Primary name** | `T_Artikelvarianten` | `Bezeichnung` | "MM JADE CZ-328" |
| **Alias** | `T_Artikelvarianten` | `Alias` | "MM JADE CZ-328" |
| **Color** | `T_Artikelvarianten` | `Farbbezeichnung` | "Cristal" |
| **Variant type** | `T_ArtikelvariantenTyp` | `Bezeichnung` | "HD-PE", "PET-P" |
| **Article type** | `T_Artikeltyp` | `Bezeichnung` | "Production", "Raw Material" |
| **Classification** | `T_ArtikelKlassifikationen` | `Klassifikation_en` | "Beverage", "Pharma" |

### 9.2 Packaging Display Fields

| Purpose | Table | Column | Example |
|---------|-------|--------|---------|
| **Packaging name** | `T_VpkVorschrift` | `Bezeichnung` | "$RESINA 1100 KGS BIG BAG" |
| **Qty per packaging** | `T_VpkVorschrift` | `AnzahlAVProVpk` | 1100 |
| **Packaging-position link** | `T_VpkPos` | `IdVpkVorschrift` + `IdArtikelvarianten` | FK link |

### 9.3 OperationsMaterialDto

```csharp
public class OperationsMaterialDto
{
    public int IdArtikelvarianten { get; set; }
    public string Bezeichnung { get; set; }       // Primary display name
    public string Alias { get; set; }
    public string Farbbezeichnung { get; set; }   // Color
    public string VariantTypeName { get; set; }    // e.g. "HD-PE"
    public string ArticleTypeName { get; set; }    // e.g. "Production"
    public string Classification { get; set; }     // e.g. "Beverage"
    public string PackagingName { get; set; }      // e.g. "$RESINA 1100 KGS"
    public double? QuantityPerPackaging { get; set; }
}
```

### 9.4 Barcode-Level Tracking

**Deferred from MVP.**

Reason: Some column names in `T_EtikettenGedruckt`, `T_WareneingangPositionen`, and `T_LadePositionen` didn't match assumptions during Script 10 execution. These entities were validated through earlier scripts but barcode-level tracking adds significant complexity for marginal MVP value. Can be added as a detail drill-down in a later phase.

---

## 10. Frontend Design — Conceptual Only

> [!NOTE]
> This section describes the **conceptual** frontend. No implementation code should be created yet.

### 10.1 Primary Screen: Operations > Logistics Transfers

**URL**: `/operations/transfers`

**Layout areas**:

```
┌──────────────────────────────────────────────┐
│  [Filters]                                   │
│  Plant | Date range | Status | Article | PO  │
├──────────────────────────────────────────────┤
│  [Transfer List]                             │
│  ┌──────────────────────────────────────────┐│
│  │ PO #102411 | Viana 1 | STANDARD | Ativo ││
│  │ MM JADE CZ-328 (HD-PE) | 235,008 pcs    ││
│  │ 29/05/2026 | 8/10 events completed       ││
│  ├──────────────────────────────────────────┤│
│  │ PO #102410 | Viana 2 | STANDARD | Sub.  ││
│  │ ...                                      ││
│  └──────────────────────────────────────────┘│
├──────────────────────────────────────────────┤
│  [Timeline Drawer] (right panel or detail)   │
│  ✅ Pedido de compra criado     29/05 09:51  │
│  ✅ Documento EDI criado        29/05 09:52  │
│  ✅ EDI exportado               29/05 19:48  │
│  ✅ Sincronização EDI           29/05 19:49  │
│  ✅ Abruf criado                29/05 19:04  │
│  ✅ Carregamento planejado      29/05 19:18  │
│  ⏳ Ordem de carregamento       —            │
│  ⏳ Recebimento criado           —            │
│  ⏳ Recebimento concluído        —            │
└──────────────────────────────────────────────┘
```

### 10.2 UI Behavior Rules

| Element | Behavior |
|---------|----------|
| Completed events | Green checkmark `✅` |
| Pending/inferred events | Grey clock `⏳` |
| Error/discrepancy events | Red warning `⚠️` (e.g. `IdJournalStatus = 64`) |
| Technical EDI details | Collapsed by default, expandable |
| User-facing labels | Portuguese (`EventLabelPT`) |
| Technical fields | Available in expanded diagnostic section |
| Pipeline model badge | `STANDARD` = blue, `INHOUSE` = purple, `PARTIAL` = grey |
| PO status badge | Color-coded by status interpretation table |

### 10.3 Filter Bar

| Filter | Type | Options |
|--------|------|---------|
| Plant | Dropdown | Viana 1, Viana 2, Viana 3, All |
| Date range | Date picker pair | Required — prevents full table scans |
| Status | Multi-select | Active, Completed, Cancelled |
| Pipeline model | Multi-select | Standard, Inhouse, Partial |
| Article search | Text input | Search by Bezeichnung |
| PO search | Text input | Search by JournalNummer |

---

## 11. Security and Performance Design

### 11.1 Security Constraints

| Constraint | Enforcement |
|------------|-------------|
| Read-only DB user | `alplaprod_viewer` with `db_datareader` only |
| Query timeout | 30 seconds per query (configurable) |
| No raw SQL in responses | Parameterized queries only |
| No internal IDs exposed | Unless in diagnostic/admin mode |
| Role-based access | Minimum `Logistics` or `Operations` role |
| Admin diagnostics | `System Administrator` role for technical details |

### 11.2 Performance Constraints

| Constraint | Enforcement |
|------------|-------------|
| **Date range required** | List views must always include date filters |
| **Pagination required** | Max 100 items per page |
| **No unlimited scans** | `OFFSET/FETCH` or `TOP N` in all queries |
| **No cross-database joins** | Each plant queried independently |
| **LEFT JOIN patterns** | Most relationships are implicit (no FK enforcement) |
| **Cache reference data** | `T_Artikeltyp`, `T_ArtikelvariantenTyp`, `T_ArtikelKlassifikationen` (slow-changing) |
| **Connection pooling** | Leverage ADO.NET connection pooling per connection string |
| **Query timing** | Log `QueryDurationMs` in every response for monitoring |

### 11.3 Reference Data Caching Strategy

| Table | Rows | Change Frequency | Cache TTL |
|-------|------|-------------------|-----------|
| `T_Artikeltyp` | 6 | Never | 24h |
| `T_ArtikelvariantenTyp` | 26–36 | Rare | 24h |
| `T_ArtikelKlassifikationen` | 9 | Never | 24h |
| `T_Werke` | 536 | Rare | 12h |
| `T_Adressen` (partner plants) | ~100 relevant | Rare | 12h |

Use `IMemoryCache` with sliding expiration. Invalidation on demand via admin endpoint.

---

## 12. Implementation Phases

> [!IMPORTANT]
> These phases are proposals. Implementation requires explicit user approval.

### Phase 1 — Backend Foundation ✅ IMPLEMENTED (v2.162.0)

**Scope**: Connection infrastructure and health checks only.

| Task | Description | Status |
|------|-------------|--------|
| `AlplaProdPlant` enum | `VIANA1`, `VIANA2`, `VIANA3` | ✅ Done |
| `AlplaProdPipelineModel` enum | `STANDARD`, `INHOUSE`, `PARTIAL` | ✅ Done |
| `AlplaProdConnectionFactory` | Plant-aware, multi-server connection factory | ✅ Done |
| `AlplaProdIntegrationProvider` | `IIntegrationProvider` health check — tests ALL enabled plants | ✅ Done |
| `appsettings.json` | `Integrations:AlplaProd` section (placeholders only) | ✅ Done |
| Seed data | `IntegrationProvider` Id=5 + `IntegrationConnectionStatus` Id=5 | ✅ Done |
| DI registration | Register provider in `Program.cs` | ✅ Done |
| Health check | Visible on `/admin/health` when enabled | ✅ Done |

**Verification**: Connection test from admin health page — tests all enabled plants with aggregated diagnostic.
**Diagnostic query**: `SELECT @@SERVERNAME, DB_NAME(), SYSTEM_USER, GETDATE();`

---

### Phase 2 — Timeline API ✅ IMPLEMENTED (v2.163.0)

**Scope**: Core API endpoint for transfer timeline.

| Task | Description | Status |
|------|-------------|--------|
| `OperationsTimelineEventDto` | Normalized event DTO | ✅ Done |
| `OperationsTimelineResponseDto` | Timeline response wrapper | ✅ Done |
| `IOperationsTimelineService` | Timeline service interface | ✅ Done |
| `OperationsTimelineService` | Orchestrates connection, query, mapping | ✅ Done |
| `OperationsTimelineQueryBuilder` | SQL generation (Standard + Inhouse) | ✅ Done |
| `OperationsPipelineDetector` | Config-based model detection | ✅ Done |
| `OperationsStatusMapper` | Portuguese status labels + severity | ✅ Done |
| `OperationsController` | Timeline REST endpoint | ✅ Done |

**Endpoint implemented**:
- `GET /api/operations/transfers/{plant}/{idBestellung}/timeline`

**Not yet implemented**:
- `GET /api/operations/transfers` (transfer list)
- `GET /api/operations/transfers/{plant}/{id}/details` (transfer details)
- `GET /api/operations/plants`

**Verification**: Executed timeline queries against V1/V2/V3 with live data. Validated event ordering, severity mapping, pipeline detection.

---

### Phase 3 — Frontend MVP ✅ IMPLEMENTED (v2.164.0)

**Scope**: First user-facing screen — manual timeline lookup.

| Task | Description | Status |
|------|-------------|--------|
| Operations menu item | `Operações > Transferências Logísticas` in sidebar | ✅ Done |
| `OperationsTransfersPage.tsx` | Manual timeline lookup page | ✅ Done |
| `operationsApi.ts` | API client helper | ✅ Done |
| `operations.types.ts` | TypeScript DTOs | ✅ Done |
| Route registration | `/operations/transfers` with `AdminRoute` guard | ✅ Done |
| Pipeline model badge | STANDARD (blue), INHOUSE (purple), PARTIAL (orange) | ✅ Done |
| Severity coloring | success/info/warning/error with colored borders | ✅ Done |
| Completion icons | ✓ check / ○ pending | ✅ Done |
| Technical events | `Técnico` badge with quieter styling | ✅ Done |
| Error handling | 400/404/503/500 with Portuguese messages | ✅ Done |
| Loading/empty states | Animated with framer-motion | ✅ Done |

**Access roles**: System Administrator, Local Manager, Buyer.

**Note**: This is the Phase 3 MVP — a manual lookup screen. The full transfer list, filter bar, and timeline drawer are deferred to a future phase when the transfer list API is implemented.

**Not yet implemented**:
- Full transfer list page (requires `GET /api/operations/transfers`)
- Transfer details page (requires `GET /api/operations/transfers/{plant}/{id}/details`)
- Filter bar (date range, status, article search, PO search)
- Barcode tracking

**Verification**: Navigate to `/operations/transfers`, select plant, enter IdBestellung, view timeline.

---

### Phase 4 — Transfer List API ✅ IMPLEMENTED (v2.165.0)

**Scope**: Backend endpoint for paginated, filterable PO listing from AlplaPROD.

| Task | Description | Status |
|------|-------------|--------|
| `OperationsTransferListItemDto` | List item DTO with confirmed column names | ✅ |
| `OperationsTransferListResponseDto` | Paginated response wrapper | ✅ |
| `IOperationsTransferListService` | Service interface (separate from timeline) | ✅ |
| `OperationsTransferListQueryBuilder` | SQL builder with OUTER APPLY TOP 1 pattern | ✅ |
| `OperationsTransferListService` | Service orchestrating count + data queries | ✅ |
| Controller action | `GET /api/operations/transfers` with full validation | ✅ |
| DI registration | `IOperationsTransferListService → OperationsTransferListService` | ✅ |
| Status filter | ACTIVE (1,2,6), COMPLETED (7,8), CANCELLED (3) | ✅ |
| Search filters | `poSearch` (IdBestellung, JournalNummer), `articleSearch` (Bezeichnung, Alias) | ✅ |
| Pagination | OFFSET/FETCH, default 25, max 100, max 90-day range | ✅ |
| Date filter | `T_Bestellungen.Add_Date` inclusive start / exclusive end | ✅ |
| Error handling | 400/401/503/500 with Portuguese messages | ✅ |

**Deferred from Phase 4:**
- `CompletedEventCount` = null (too expensive per-row; use timeline endpoint)
- `PackagingName` = null (T_VpkVorschrift join unreliable across plants)
- `QuantityUnit` = null (T_Bestellpositionen has no unit column)
- `ALL` plant aggregation (requires multi-server merge)

**Verification**: Tested all 3 plants with date ranges, status filters, pagination. Timeline endpoint confirmed unbroken.

---

### Phase 5 — Hardening

**Scope**: Production readiness.

| Task | Description |
|------|-------------|
| Performance optimization | Query plans, index usage analysis |
| Reference data caching | `IMemoryCache` for slow-changing tables |
| Error handling | Graceful degradation per plant |
| Role-based access | `Logistics` / `Operations` permission |
| Query timeout tuning | Per-plant timeout configuration |
| Diagnostics endpoint | Admin-only technical details |
| Documentation update | API docs, CHANGELOG |

---

## 13. Open Questions / Deferred Items

| # | Item | Status | Notes |
|---|------|--------|-------|
| 1 | Barcode-level tracking | **Deferred** | Too complex for MVP; add as detail drill-down later |
| 2 | `T_InhouseBewegungen` detail | **Deferred** | Can show production movement in V3 detail, not required for timeline |
| 3 | Status badge wording | **Open** | Exact Portuguese labels for every status value need business review |
| 4 | `EDI_SYNCED` visibility | **Open** | Show to end users or technical-only? |
| 5 | Partial transfer grouping | **Open** | Whether partial transfers need grouped timeline events |
| 6 | V3 production movement detail | **Open** | Optional detail in Inhouse timeline? |
| 7 | Article classification in list | **Open** | Show in list view or only in detail view? |
| 8 | Multi-plant aggregated view | **Deferred** | `ALL` plant filter queries all 3 plants independently and merges |
| 9 | Script `12` STRING_AGG V2 fix | **Low priority** | Non-blocking for implementation |

---

## 14. Related Documents

| Document | Purpose |
|----------|---------|
| [OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md) | Complete discovery findings, Appendix E status values |
| [OPERATIONS_ENTITY_MAP.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_ENTITY_MAP.md) | Entity-to-table mapping, timeline event map |
| [OPERATIONS_TIMELINE_QUERY_PROTOTYPES.md](file:///c:/dev/alpla-portal/docs/sql-discovery/operations/OPERATIONS_TIMELINE_QUERY_PROTOTYPES.md) | Prototype SQL queries for timeline |
| [INTEGRATION_PLAYBOOK.md](file:///c:/dev/alpla-portal/docs/INTEGRATION_PLAYBOOK.md) | Established integration patterns (IIntegrationProvider, ConnectionFactory) |
| [ARCHITECTURE.md](file:///c:/dev/alpla-portal/docs/ARCHITECTURE.md) | Overall system architecture |
| [OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md](file:///c:/dev/alpla-portal/docs/OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md) | Live Transfer Board design — TV/kiosk visual operations board |

