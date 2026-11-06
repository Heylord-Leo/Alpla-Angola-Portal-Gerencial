# Operations — Viana 3 Production Monitor (read-only TV page + shared layout editor)

> Status: released in **v2.249.9** (2026-11-06) for manual validation in TEST (both migrations applied and verified in `Portal-Gerencial-Test` on 2026-11-06; not in PROD).
> **Pending (owner decision 2026-11-06):** guided tours for the layout editor and the card-design editor (`page-*` tours, anchors, registry) were deferred to a follow-up. Iteration 2 (2026-11-04) adds the shared
> layout editor; iteration 3 (2026-11-04) adds **multiple independent TV screens**; iteration 4 (2026-11-04) adds the
> **machine-card design editor** (§11). **Two additive migrations** (`20261104134729_AddOperationsBoardLayout`,
> `20261104151526_AddOperationsCardDesign`) — applied to the local DEV clone (Development auto-migrate) and to TEST (2026-11-06); not to PROD.
> Reference query: `AlplaPROD_aovia3_tv_preform_monitor_v5.sql` (operator's Downloads; the "v5 (1)" wording carries the
> decision of 2026-11: the historical postings of lots 2687/2700 are NOT corrected and stay off the main screen).
> Related: `OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md` (routing / fullscreen / anonymous-access model, reused unchanged),
> `OPERATIONS_MODULE_TECHNICAL_DESIGN.md §3` (AlplaPROD connection factory).

## 1. Purpose

A full-screen TV page for the Viana 3 blow-moulding hall. One panel per machine shows the current production order,
the Warehouse 3 stock of the preform that order needs, a planned / reported / scanned comparison, the AlplaLABEL Waste
and the two previous production orders. Historical / system-balance diagnostics live on a separate view.

The Portal backend is the only client of AlplaPROD; the TV talks to the Portal API only. Every query is a SELECT.

## 2. How to reach it (TEST)

| Screen | URL | Access |
|--------|-----|--------|
| TV page | `/operations/viana3-monitor?fullscreen=true` (optional `&refresh=60..300`) | Public standalone route, outside `ProtectedRoute` / `AppShell` — same model as the Live Board |
| Diagnostic view | `/operations/viana3-monitor/diagnostics` | Inside `AppShell`, `AdminRoute allowedRoles=[OPERATIONS]` (System Administrator always allowed) — restricted; not used by the TV |
| API (monitor) | `GET /api/operations/viana3-monitor?refreshSeconds=120` | `[AllowAnonymous]` |
| API (diagnostics) | `GET /api/operations/viana3-monitor/diagnostics` | `[Authorize(Roles = "System Administrator,OPERATIONS")]` — 401 without token, 403 without the role |
| **TV screen** (dedicated) | `/operations/viana3-monitor/screen/{screenKey}?fullscreen=true` | Public standalone route; one URL per TV; `{screenKey}` is the stable generated key of the screen (e.g. `sdzri4r`); unknown key → "Ecrã da TV não encontrado" |
| **Layout editor** (computer) | `/operations/viana3-monitor/layout[?screen={screenKey}]` | Inside `AppShell`, `AdminRoute allowedRoles=[OPERATIONS]` (System Administrator always allowed); nav "Operações → Monitor Viana 3 — Layout TV" |
| **Card-design editor** (computer) | `/operations/viana3-monitor/card-design[?machine={id}\|template]` | Same guard; nav "Operações → Monitor Viana 3 — Design dos cartões" (§11) |
| API (card design, TV) | `GET /api/operations/viana3-monitor/card-design`, `GET …/card-design/assets/{id}` | `[AllowAnonymous]` — template + machine designs (no audit names) and raster image bytes |
| API (card design, editor) | `GET …/card-design/catalog`, `GET`/`PUT`/`DELETE …/card-design/template` and `…/{machineId}`, `GET`/`POST …/card-design/assets`, `DELETE …/assets/{id}`, `POST …/assets/cleanup` | `[Authorize(Roles = "System Administrator,OPERATIONS")]` |
| API (default screen, read) | `GET /api/operations/viana3-monitor/layout` | `[AllowAnonymous]` — layout of the default screen (historical TV URL) |
| API (screen, read) | `GET /api/operations/viana3-monitor/screens/{screenKey}/layout` | `[AllowAnonymous]` — display preferences only (machine ids, order, sizes, hidden flags); the TV reads it with every refresh; 404 when unknown |
| API (screens, editor) | `GET`/`POST /api/operations/viana3-monitor/screens`, `PUT`/`DELETE …/screens/{screenKey}`, `PUT`/`DELETE …/screens/{screenKey}/layout` | `[Authorize(Roles = "System Administrator,OPERATIONS")]` — list / create / rename / delete / save layout / reset layout |

Precondition in TEST: the AlplaPROD integration must be enabled and the plant **VIANA3** configured (Integration
Settings → AlplaPROD: server `AOVIA1VMS006` / alias `aovia3prod.alpla.net`, database `AlplaPROD_aovia3`, login
`alplaprod_viewer`, db_datareader). The monitor reuses `AlplaProdConnectionFactory.CreateConnectionAsync(VIANA3)`; when the
plant or the integration is disabled the endpoint answers `503 SERVICE_UNAVAILABLE` (or the stale snapshot, see §6).

Anonymous access follows the Live Board decision (`OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md §15/§19`): the payload carries no
financial values and no usernames (the `Add_User` column of reference Query 4 is deliberately not selected).

## 3. Files

| Layer | File | Role |
|-------|------|------|
| Application | `DTOs/Operations/Viana3ProductionMonitorDtos.cs` | Response contracts (monitor + diagnostics) |
| Application | `Interfaces/Operations/IViana3ProductionMonitorService.cs` | Service contract |
| Infrastructure | `Services/Integration/Operations/Viana3ProductionMonitorOptions.cs` | Configuration (`Integrations:AlplaProd:Viana3Monitor`), defaults = reference query |
| Infrastructure | `Services/Integration/Operations/Viana3ProductionMonitorQueryBuilder.cs` | Queries 1–4 of the reference, parameterised, `[dbo].` only |
| Infrastructure | `Services/Integration/Operations/Viana3ProductionMonitorService.cs` | Query execution, mapping, rules, `IMemoryCache` snapshot |
| Api | `Controllers/OperationsController.cs` | Monitor, diagnostics and layout endpoints (see §2) |
| Domain | `Entities/OperationsBoardLayout.cs` | Shared TV screen layout (one row per board × screen, JSON document) |
| Infrastructure | `Data/Configurations/OperationsBoardLayoutConfiguration.cs`, `Data/Migrations/20261104134729_AddOperationsBoardLayout.cs` | Table `OperationsBoardLayouts` (additive migration, unique BoardKey+ScreenKey) |
| Application | `DTOs/Operations/Viana3MonitorLayoutDtos.cs`, `Interfaces/Operations/IViana3MonitorLayoutService.cs` | Screen / layout contracts, validation rules |
| Infrastructure | `Services/Operations/Viana3MonitorLayoutService.cs` | Screens: list / create / rename / delete; layouts: get / save (validated) / reset; AdminLog audit |
| Api | `Program.cs`, `appsettings.json` | DI registration; `Viana3Monitor` section (documentation values) |
| Frontend | `types/operations.types.ts` | `Viana3*` types |
| Frontend | `lib/operationsApi.ts` | `fetchViana3Monitor`, `fetchViana3MonitorDiagnostics`, `fetch/save/resetViana3MonitorLayout` |
| Frontend | `pages/Operations/viana3/viana3Layout.ts` | Layout model: sizes, `resolveLayout`, deterministic `placeCards`, equality |
| Frontend | `pages/Operations/viana3/Viana3Board.tsx` | Shared renderer: machine panels, 1920×1080 stage, board, content fitting |
| Frontend | `pages/Operations/Viana3ProductionMonitorPage.tsx` | TV page (read-only; polls data + layout) |
| Frontend | `pages/Operations/Viana3MonitorLayoutEditorPage.tsx` | Layout editor: screens (create/rename/delete/copy TV URL) and per screen machine selection, drag/↑↓ order, size, hide/restore, columns, 1920×1080 preview, save/cancel/reset |
| Frontend | `pages/Operations/Viana3MonitorDiagnosticsPage.tsx` | Diagnostic view |
| Domain | `Entities/OperationsCardDesign.cs` | `OperationsCardDesign` (template = MachineId 0, per-machine overrides, JSON document, save token) and `OperationsDesignAsset` (raster images, two-phase deletion bookkeeping) |
| Infrastructure | `Data/Configurations/OperationsCardDesignConfiguration.cs`, `Data/Migrations/20261104151526_AddOperationsCardDesign.cs` | Tables `OperationsCardDesigns`, `OperationsDesignAssets` (unique board+machine; filtered unique board+hash on active assets) |
| Application | `DTOs/Operations/Viana3CardDesignDtos.cs`, `Interfaces/Operations/IViana3CardDesignService.cs` | Design document, catalogue, asset contracts, limits (`Viana3CardDesignRules`), exceptions |
| Infrastructure | `Services/Operations/Viana3BoardLock.cs` | Per-board `sp_getapplock` transaction wrapper shared by design and asset writes |
| Infrastructure | `Services/Operations/Viana3CardItemCatalog.cs` | Item catalogue + runtime verification against the rendered queries and the DTO contracts |
| Infrastructure | `Services/Operations/Viana3CardDesignService.cs`, `Services/Operations/Viana3DesignAssetService.cs` | Designs (validate, idempotent save, reset) and assets (normalise, hash reuse, quota, tombstone/purge, sweep) |
| Api | `Controllers/Viana3CardDesignController.cs` | Card-design endpoints and error mapping |
| Frontend | `types/viana3CardDesign.types.ts`, `lib/viana3CardDesignApi.ts` | Contracts and API client |
| Frontend | `pages/Operations/viana3/viana3CardDesign.ts`, `pages/Operations/viana3/Viana3DesignedPanel.tsx` | Design model (default design, resolution, number formats, readability) and the designed-card renderer |
| Frontend | `pages/Operations/Viana3CardDesignEditorPage.tsx` | Card-design editor (targets, canvas, properties, source panel, readability, images) |
| Frontend | `App.tsx`, `constants/navigation.tsx` | Public routes (TV default, TV per screen) + protected routes (diagnostics, layout editor, card-design editor); navigation entries |

The existing `/operations/live-board/:plant` page and its endpoint are not modified.

## 4. Data-to-screen mapping

### 4.1 Machine panel (reference Query 1, one row per machine × slot)

| Screen element | Source | Notes |
|----------------|--------|-------|
| Machine / line name | `T_LagerAbteilungen.Bezeichnung` for `IdMaschine` (areas 1–6) | Machines discovered from lots ending in the last 60 days (or fixed by `MachineIds`) |
| Current order | `T_ProdPlanung` row with `PlanVon <= now < PlanBis` (latest `PlanVon`) | `lotKind` from the article name: `$Manut%` Maintenance, `$No-Order%` No order, other `$…` Placeholder, else Production |
| Status chip | derived from the current lot kind | RUNNING / MAINTENANCE / NO_ORDER / PLACEHOLDER / IDLE (no lot) |
| Bottle | `T_Artikelvarianten.Bezeichnung` of `IdArtikelvarienten`; weight `ArtikelGewicht` (g) | |
| Preform required | `T_Artikelvarianten` of `IdHauptmaterial`; weight `ArtikelGewicht` (g) | Drives the stock lookup |
| Planned (bars, reference line) | `T_ProdPlanung.PlanMenge` | plan |
| Reported bottles (blue) | `SUM(T_ProduktionsRueckmeldungDaten.ProdMenge)` via `T_ProduktionsRueckmeldungen.IdProdPlanung` | manual report (AlplaREPORT); `reportLastUpdated` shown |
| Scanned preforms (orange) | `T_ProduktionsBuchungen` reason 231, location 0, op 1420 (+) / 1460 (−), account of the lot's `IdHauptmaterial` | Label "Lidas no scanner", never "consumidas". `scanCount` = number of 1420 postings |
| "Sem leitura de scanner" | `scanCount = 0` | `scannedPreforms` is sent as `null`; no bar, no deviation |
| Other preform scanned | same postings, type 13, article ≠ `IdHauptmaterial` | Small informative line only |
| Deviation chip | computed in the service (§5) | Always labelled "provisório" |
| Waste — rejected bottles | `T_LagerBuchungen` reason 140 / Urheber 1100, account article **56**, kg | units = kg × 1000 / bottle weight, labelled "estimativa" |
| Waste — rejected preforms | same, account article **55**, kg | units = kg × 1000 / planned-preform weight, labelled "estimativa" |
| Previous orders (2) | the two latest Production lots with `PlanVon` before the current lot | Same figures in compact form + deviation chip |

### 4.2 Preform pallets (reference Query 2)

| Screen element | Source | Notes |
|----------------|--------|-------|
| Pallet icons + "un. disponíveis" | `T_LagerPositionen` rows (accounts of type 13) in `IdWarenLager = 3` and `IdLagerAbteilung IN (30003, 30004, 30005, 30008, 30009, 30010, 30011, 30012)` | Matched to the current lot's `IdHauptmaterial` |
| Available | positions with `IdLagerAbteilung <> 30012` and `GesperrtAktiv = 0`: `COUNT` → `availablePositions`, `SUM(EinlagerungsMenge)` → `availableQty` | |
| Retention / blocked | positions in 30012 **or** `GesperrtAktiv = 1`: `blockedPositions`, `blockedQty` | Shown on its own line, never in the available figure |
| Location groups (payload only) | 30012 BLOCKED_RETENTION, 30003 BUFFER, others LINE_SIDE | Available on `preformStock[].locations` for a future detail view |

The source counts **stored positions** (rows of `T_LagerPositionen`, i.e. pallets / storage positions) and **units**
(`EinlagerungsMenge`). The screen therefore shows "paletes/posições" and units; the word "caixas" is not used because the
mapping position ↔ box is not confirmed in the data (see §7).

### 4.3 Header / footer

| Element | Source |
|---------|--------|
| "Atualizado HH:mm:ss (hora de Angola)" | `source.fetchedAtLocal` = UTC fetch time + 1 h (`AppTimeZone`), serialised without offset |
| AO VIVO / CACHE / DESATUALIZADO chip | `source.status` LIVE / CACHED / STALE |
| Freshness dot | green < 5 min, amber 5–15 min or STALE, red > 15 min or no data |
| Countdown bar | client refresh (`refreshSeconds`, 60–300, default 120) |
| Footer legend | fixed text: colours, "não é consumo físico", "desvio provisório", source and time zone |

All AlplaPROD datetimes are Angola wall-clock as recorded (server time zone UTC+1, no DST); they are transported with
`DateTimeKind.Unspecified` (no `Z`) and the page formats them without any time-zone conversion.

### 4.4 Diagnostic view (reference Queries 3 and 4)

| Table | Source | Notes |
|-------|--------|-------|
| Saldo histórico em aberto no sistema | `T_ProduktionsBuchungen` balance per (area 1–6, preform account, barcode), `ABS(balance) > 0.001`, with last lot / last operation | Label "não corrigido (apenas diagnóstico)"; **never** joined to stock. This is where the 91 392 units of lot 2687 appear |
| Waste sem lote válido | `T_LagerBuchungen` type 20, reason 140 / Urheber 1100, last 90 days, `ProduktionsLos` not in `T_ProdPlanung` | kg recorded; units estimated with the booking's bottle weight (proxy also for article 55 — assumption) |

## 5. Rules implemented in `Viana3ProductionMonitorService` (decimal arithmetic)

- **Deviation (provisional)** — `(scannedPreforms − reportedBottles) / reportedBottles`, rounded to 4 decimals, only when the lot is
  Production, `scanCount > 0` and `reportedBottles > 0`. Otherwise the status is `NOT_APPLICABLE`, `NO_SCAN` ("Sem leitura de
  scanner") or `NO_REPORT`. A missing scan is never turned into a zero-based −100 %. Waste is not part of the formula.
  Chip colour on the TV: |ratio| ≤ 2 % green, ≤ 5 % amber, else red — purely visual, the value stays provisional.
- **Waste units (estimate)** — `kg × 1000 / weight_g`, whole units, `null` when the weight is unknown or zero. Bottles (56) and
  preforms (55) are never summed and never mixed with the deviation.
- **Stock** — only the agreed Warehouse 3 locations; 30012 and `GesperrtAktiv` positions are blocked and excluded from
  `availableQty`. The machine-area balance (Query 3) is a separate payload and is not added anywhere.
- **Lots 2687 / 2700** — nothing is moved or re-assigned; lot 2700 shows "Sem leitura de scanner" until AlplaPROD itself has a
  posting; the 2687 balance is visible only in the diagnostic view.

## 6. Refresh, cache and unavailability

| Aspect | Behaviour |
|--------|-----------|
| TV refresh | client timer `refreshSeconds` (60–300, default 120); backend serves the cached snapshot while it is younger than that window, so several TVs share one query |
| Diagnostics | one-hour server cache (`DiagnosticsCacheSeconds`), hourly client timer, manual "Atualizar agora" |
| Snapshot | last successful result kept in `IMemoryCache` (no expiry) per payload; a `SemaphoreSlim` prevents duplicate queries |
| AlplaPROD unavailable | `SqlException` / `InvalidOperationException` (integration or plant disabled, credentials missing) / `TimeoutException` → the snapshot is returned with `source.status = "STALE"`, `fetchedAtLocal` of the snapshot and a short `unavailableReason`; the TV shows an amber banner with that time |
| No snapshot yet | the controller answers 503 (`SERVICE_UNAVAILABLE`, `QUERY_TIMEOUT`, `CONNECTION_ERROR`) and the TV shows the "Fonte indisponível" screen with the retry countdown |
| Portal unreachable | the TV keeps the last payload and shows the red "Sem ligação ao Portal" banner |

## 7. Configuration (`Integrations:AlplaProd:Viana3Monitor`)

Every key has a code default equal to the reference query, so the section is optional. Keys: `Plant`, `PlantName`,
`MachineIds` (empty = discover), `MachineLookbackDays` 60, `PreviousLots` 2, `WarehouseId` 3, `WarehouseName`, `LocationIds`,
`RetentionLocationId` 30012, `BufferLocationId` 30003, `PreformArticleTypeId` 13, `ScanReasonId` 231, `ScanLocationId` 0,
`ScanConsumeOperation` 1420, `ScanUnconsumeOperation` 1460, `WasteReasonId` 140, `WasteOriginatorId` 1100, `WasteArticleTypeId` 20,
`WasteBottlesArticleId` 56, `WastePreformsArticleId` 55, `MachineAreaMinId` 1, `MachineAreaMaxId` 6, `OrphanWasteDays` 90,
`DefaultRefreshSeconds` 120, `MinRefreshSeconds` 60, `MaxRefreshSeconds` 300, `DiagnosticsCacheSeconds` 3600,
`CommandTimeoutSeconds` 30. All ids reach SQL as parameters.

## 8. Open assumptions / to confirm in TEST

1. **Boxes vs positions** — the data counts `T_LagerPositionen` rows and `EinlagerungsMenge`; whether one position is one box,
   one pallet of several boxes, or a mixed position must be confirmed with the warehouse before the label "caixas" is used.
2. **1 preform = 1 bottle** — not confirmed; the deviation stays provisional and Waste is excluded.
3. **Preform waste weight** — in Query 1 the planned preform weight is used (lot's `IdHauptmaterial`); in the diagnostic view the
   bottle weight is a proxy (every checked pair had equal weights — assumption inherited from the reference).
4. **Machine names** — `T_LagerAbteilungen` 1–6 are used as line names (as in the reference); if a machine has no lot in the last
   60 days it is not shown unless listed in `MachineIds`.
5. **VIANA3 access in TEST** — the TEST Portal must have the VIANA3 plant enabled with the `alplaprod_viewer` credentials; the
   service itself has not been run against the live database from this workstation (no AlplaPROD access here).
6. **Anonymous endpoints** — same model as the Live Board; a kiosk token remains a future option. The layout `GET` is anonymous for
   the same reason (no production data, no usernames in it).
7. **Migration in TEST** — `OperationsBoardLayouts` must be created before the layout endpoints answer; until then `GET …/layout`
   returns 500 and the TV keeps the default layout (the data request is independent).

## 9. Two modes: read-only TV and shared layout editor (iteration 2, 2026-11-04)

### 9.1 Modes

| Mode | Where | What it does |
|------|-------|--------------|
| **TV display** | `/operations/viana3-monitor?fullscreen=true` on the production-area TV | Viewing only. No drag handles, edit buttons, menus or layout controls exist in the page. It polls the data **and** the shared layout together on every refresh (60–300 s), so an edit made in the Portal reaches every TV within one interval without touching the TV. A layout fetch failure keeps the last known layout (or the default); it never blocks the data. |
| **Layout editor** | `/operations/viana3-monitor/layout` in the Portal (computer), OPERATIONS / System Administrator | Drag cards (or ↑/↓) into order; size per card (Compacto 1×1, Normal 1×1, Largo 2×1, Grande 2×2); hide a card ("Ocultar") and restore it from the "Cartões ocultos" list; grid columns 2–4; live preview of the exact 1920×1080 composition (zoom fit / 50 % / 100 %); Save, Cancel (confirmed, reloads the saved layout) and Reset (confirmed, back to the default). Unsaved changes warn on refresh/close. |

"Hide" is a display flag in the layout document. It never deletes machine data and never changes AlplaPROD records (the
monitor service remains SELECT-only and untouched).

### 9.2 Persistence (why a table, not browser storage)

The layout must be identical on every TV and browser, so `localStorage` is excluded. The Portal has no generic settings store and
`IntegrationProviderSettings` is documented as strictly connection-oriented; the Portal precedent for a saved layout is a dedicated
entity with a versioned JSON document (`BadgeLayout.LayoutConfigJson`). The same pattern is applied:

| Column | Meaning |
|--------|---------|
| `BoardKey` (nvarchar 60) | `VIANA3_PRODUCTION_MONITOR` (other boards may reuse the table later) |
| `ScreenKey` (nvarchar 40) | `default` for the screen behind the historical URL, otherwise a generated stable key (`s` + 6 chars, e.g. `sdzri4r`); **unique with BoardKey** (`UX_OperationsBoardLayouts_Board_Screen`) |
| `Name` (nvarchar 80) | Screen name shown in the editor and on the TV header chip ("Envase 1"); unique per board (case-insensitive) |
| `IsDefault` (bit) | The default screen; never deleted |
| `SchemaVersion` | 1 |
| `LayoutJson` | `{ "schemaVersion": 1, "columns": 3, "includeNewMachines": false, "cards": [ { "machineId": 1, "size": "STANDARD", "hidden": false } ] }` — array order = display order |
| `UpdatedByUserId` → Users (SetNull), `CreatedAtUtc`, `UpdatedAtUtc` | audit |

No `default` row = default layout (`isPersisted: false`): discovery order, every machine visible, STANDARD. Server validation on save: columns
2–4, size ∈ {COMPACT, STANDARD, WIDE, LARGE}, positive unique machine ids, ≤ 24 cards; the validated model is re-serialized
(the raw body is never stored); `400 LAYOUT_INVALID` otherwise. Save/Reset write AdminLog events `VIANA3_MONITOR_LAYOUT_SAVED` /
`VIANA3_MONITOR_LAYOUT_RESET`. A corrupt document is logged and served as the default — the TV never goes down for a layout.

Resolution rule (`resolveLayout`, shared by TV and editor): saved cards first in saved order; when the screen has
`includeNewMachines = true` (default screen behaviour) machines present in the data but absent from the document are appended
visible at STANDARD, otherwise the screen shows exactly its selected cards; cards in the document whose machine has no current
planning are kept (the editor shows them as "sem dados atuais"; the TV simply does not draw them) so they return to their
position later.

### 9.3 Readability, no clipping, resolution safety

* The board is composed on a **fixed 1920×1080 stage** (`Viana3Stage`) and uniformly scaled to the viewport, centred and
  letter-boxed. A TV with another resolution or aspect ratio only changes the scale — placement and fitting are identical to the
  editor preview.
* Placement is explicit and deterministic (`placeCards`: row-major sparse packing of column/row spans, spans clamped to the column
  count), so the preview and the TV always agree; rows share the stage height equally.
* Each machine panel **fits itself**: it measures its content and, when it exceeds the card height, drops sections in a fixed
  order — (1) previous orders → (2) illustration (stock figures stay as text) → (3) the two Waste tiles become one line (values
  still separate, kg recorded, units marked estimate) → (4) the comparison bars become a one-line production summary
  (planned, reported, scanned, deviation chip) → (5) essential only: positions/retention lines hidden and the Waste kg folded
  into the summary line. If the content still does not fit at level 5 the text is shrunk with CSS `zoom` down to 70 %; beyond
  that the card is reported as clipped. COMPACT starts at level 2. The level/shrink reached is reported to the editor, which
  flags reduced cards per card ("⚠ ordens anteriores ocultas", "⚠ conteúdo essencial…", "⚠ texto a 80 %", "⚠ conteúdo não cabe
  (cortado)") and suggests fewer rows, another size or hiding cards. LARGE (2×2) applies `zoom: 1.25` for distance reading.
* Fit reporting is **change-only**: a panel keeps the last reported result in a ref and calls the editor only when level, shrink
  or the clipped flag changes; the editor mirrors its fit map in a ref and only sets state on a real change; the board and the
  panels are memoized. (2026-11-04 DEV defect: the panels reported on every render and the editor relied on a "return prev"
  equality inside the updater — React re-applied the queued updaters against an older base state, producing a new object every
  commit and a render → layout-effect → setState loop: "Maximum update depth exceeded". Fixed as described; the level reset
  on size/box/data change is now a derived-state adjustment during render instead of an effect.)
* Text lines are single-line with ellipsis where a long AlplaPROD article name could overflow.

### 9.4 What did not change

`Viana3ProductionMonitorService`, `...QueryBuilder`, `...Options`, the monitor and diagnostics endpoints, the diagnostics page, the
deviation/Waste/stock rules, the LIVE / CACHED / STALE indicators and the provisional labels are unchanged.

## 10. Multiple TV screens (iteration 3, 2026-11-04)

The production area has several TVs; each opens a **dedicated read-only URL** that loads its own saved configuration without any
interaction on the device.

| Concept | Rule |
|---------|------|
| Screen | One row of `OperationsBoardLayouts` per board × screen. Stable generated key in the URL; a rename never changes the key. |
| Default screen | `ScreenKey = default`, `IsDefault = 1`, name "Ecrã principal". Backs `/operations/viana3-monitor` (historical URL). Virtual (discovery layout) until saved; can be renamed and reset, never deleted (`400 SCREEN_INVALID`). |
| Named screen | Created in the editor ("Novo ecrã", optional copy of the current screen). URL `/operations/viana3-monitor/screen/{screenKey}?fullscreen=true`. Starts with no machines and `includeNewMachines = false`; machines are added one by one (a machine may be on several screens). |
| Independence | Each screen has its own columns, order, sizes, hidden cards and new-machine policy; saving one screen writes only its row. |
| Deleted screen | A TV still pointing at it shows "Ecrã da TV não encontrado" (API 404) and keeps re-checking at the refresh interval; it never falls back to another screen's layout. |
| Limits | ≤ 12 screens, names ≤ 80 chars unique per board, ≤ 24 cards per screen. |
| Audit | AdminLog `VIANA3_MONITOR_SCREEN_CREATED / RENAMED / DELETED`, `VIANA3_MONITOR_LAYOUT_SAVED / RESET` (with the screen key). |

Editor (`/operations/viana3-monitor/layout?screen={screenKey}`): screen bar (select, create), selected-screen header (rename,
copy TV URL to the clipboard, open TV, reset/empty, delete with confirmation), then the per-screen sections: grid (columns,
"Incluir automaticamente máquinas novas"), cards on the TV (drag/↑↓, size, hide), hidden cards (restore, remove from screen),
machines off this screen (add), and the 1920×1080 preview with the screen name chip. Switching screens with unsaved changes asks
for confirmation.

DEV validation 2026-11-04 (headless Chrome, DEV clone): created "Envase 1" (machines 1+2, 2 columns) and "Envase 2" (machines
1+3+4, 4 hidden, renamed to "Envase 2 - Norte" with the key unchanged); each dedicated URL showed only its machines and layout;
the default URL kept all six machines; the deleted screen's URL showed "Ecrã da TV não encontrado"; duplicate names are refused;
deleting the default screen is refused; unauthenticated writes are refused (401).

## 11. Machine-card design editor (iteration 4, 2026-11-04)

Separate from the screen layout: screens decide **which** machines appear and the order/size/visibility of their cards; the
card design decides the items **inside** a machine card and is shared by every screen where the machine appears.

### 11.1 Model and resolution

| Concept | Rule |
|---------|------|
| Design document | Schema 1, canvas **`canvasWidth` 600–1200 (step 10 in the editor) × 440 fixed** design units (2026-11-05: adjustable width; older documents without a width, or 600, render exactly as before), `background`, `items[]` with `key`, `enabled`, `x/y/w/h`, `rotation` (illustration, images, free text only), `z`, `style` (colour, background, border, accent, font size/weight, align, opacity, radius) and `props` (label, number format INT/THOUSANDS/COMPACT/DECIMAL1, unit suffix, secondary line, text, image asset, image fit). Validated and re-serialised server-side; ≤ 40 items, ≤ 6 free text/image items. |
| Catalogue items | machineName, status, lotLine, planWindow, illustration, palletStack, preformLabel, stockAvailable, stockPositions, stockBlocked, planned, reported, scanned, deviation, wasteBottles, wastePreforms, previousOrders (+ `free-text-n`, `free-image-n`). Previous orders and the pallet stack are positionable boxes with a fixed internal layout. |
| Resolution per card | machine's own design → board template (`MachineId = 0`) → built-in card (`MachinePanel`, with its fit levels). |
| Personalise | "Personalizar esta máquina" saves a full copy of the effective design for that machine; later template edits do not change it; "Repor para o modelo" deletes the override. "Repor cartão original" on the template deletes the template. |
| Canvas width | The editor's "Largura da área de desenho" control (number input, «600 (original)», and one suggestion per screen placement = the width whose 440-high aspect matches that card box) changes only `canvasWidth`: the height stays 440 and every item keeps its coordinates, so the new space appears on the right for manual repositioning; the width cannot go below the right-most item (no item is moved or clipped by the control); the server refuses widths outside 600–1200 and any height other than 440. Saved with the machine (or template) design, so every TV screen where the machine appears uses the same canvas. Readability and the "Itens fora da área de desenho" clipping warning are recomputed per placement with the document's canvas. |
| Rendering | `Viana3DesignedCard`'s **outer frame** (background, border, radius) fills the whole grid area the layout assigned — the same area a built-in `MachinePanel` of that size gets — and the 600×440 canvas is a transparent content layer scaled uniformly and centred inside it (never stretched, never clips; design space defines item coordinates only, never the card size — fixed 2026-11-05 after a Grande card in a 2-column layout rendered letterboxed). Production rules are enforced in the item renderers whatever the design: no scan → "Sem leitura de scanner" and no deviation; deviation chip always provisional; Waste kg recorded with units labelled estimate, never combined; blocked stock apart; number formats change presentation only. A non-production lot shows its explanation once (planned slot). |
| Concurrency | Every write (design save/reset, asset upload/delete/cleanup/sweep) runs in a `ReadCommitted` transaction that first takes `sp_getapplock` (`viana3-card-design:{BoardKey}`, exclusive, transaction-owned, 5 s). Saves carry a client `saveToken` (idempotent retry after a lost acknowledgement) and `expectedUpdatedAtUtc` (`409 DESIGN_MODIFIED`); referenced assets must be ACTIVE at save time (`409 ASSET_UNAVAILABLE`); lock timeout → `409 BOARD_BUSY` + `Retry-After` (editor retries once). |

### 11.2 Data-source catalogue (read-only)

The catalogue in `Viana3CardItemCatalog` is the maintained source of truth: per item the payload field, AlplaPROD table and
field, filters (expressed through the configured identifiers), the calculation and caveats, and a query reference (Q1 / Q2 /
SERVICE + alias). No SQL is copied into it. At catalogue time the backend renders the real queries from
`Viana3ProductionMonitorQueryBuilder` with the live options (text only, nothing executed) and verifies each source: the alias is
defined in that query, the table and field take part in it, service figures resolve to a method on
`Viana3ProductionMonitorService`, and payload fields resolve against the DTO JSON names. The expression shown as optional
"Detalhe técnico" is extracted from that rendered text; a failed check marks the source NOT_VERIFIED, hides the detail and logs
an error. DEV 2026-11-04: 17 items / 26 sources, all VERIFIED (hand-checked against the builder lines 103–160 and 182–197 and
the service's `DeriveMachineStatus`, `ComputeDeviation`, `EstimateUnits`). No connection detail appears anywhere.

### 11.3 Images

PNG, JPEG, WebP only (SVG refused); ≤ 2 MB and ≤ 2048 px; validated by extension, declared type, magic bytes, a real decode and
a re-encode with Magick.NET (metadata stripped, transparency kept); hashed (SHA-256 of the normalised bytes); identical content
reuses the active asset of the board **before** the quota check; quota = 50 active assets, enforced inside the locked
transaction before the insert (a post-commit count is diagnostic only). Files live under
`AppConfig:OperationsCardDesignStoragePath` (default `data/attachments/operations/card-design/{assetId}.{ext}`), written before
the database transaction so retried delegates stay database-only; served anonymously by id with `nosniff`, inline disposition
and immutable caching.

Deletion is two-phase: (1) under the lock, references are recomputed from the committed designs (`409 ASSET_REFERENCED`), the
row is tombstoned (`DeletedAtUtc`) and committed — unservable, unlisted, outside the quota; (2) in a new locked transaction the
file is deleted idempotently and the row removed; a failed file deletion keeps the tombstone with `PurgeAttempts` /
`LastPurgeError`. The reconciliation sweep (after each delete, bulk cleanup and at request) purges tombstones, removes files in
the dedicated folder that match the asset-id pattern, have no row and are older than one hour, and reports active rows whose
file is missing (never auto-deleted). "Limpar imagens não utilizadas" tombstones every unreferenced asset and sweeps.

### 11.4 Editor and readability

`/operations/viana3-monitor/card-design`: left, the targets (template + machines, tagged modelo guardado / personalizado /
segue o modelo / cartão original) and the item list; centre, the canvas (select, drag, 8 resize handles, rotate handle, arrow
keys, snap 4 units) over live data (sample data when AlplaPROD is unavailable); right, properties and "Origem dos dados".
Readability: for every screen placement of the machine (template: every placement of every machine without its own design) the
editor derives the card box on the 1920×1080 stage, the design scale and the smallest rendered text — ≥ 14 px OK, 11–13 px
"texto pequeno", < 11 px "ilegível numa TV" — plus overlaps; provisional, always visible, never blocking.

### 11.5 DEV validation (2026-11-04, headless Chrome + API)

Template: accent colour on the reported bar, previous orders disabled, pallet stack moved, stock label and COMPACT format, PNG
set as illustration and rotated 15°, saved → both screens and the default URL rendered the designed cards for every machine;
machine 1 personalised (scanner label), template edited again → machine 1 kept its copy while machine 3 followed the template;
reset of machine 1 → back to the template. Readability showed OK / texto pequeno / ilegível bands per screen and flagged a 6 px
font. Assets: valid PNG stored, identical upload reused, 4096 px / 2.4 MB / renamed executable / SVG / SVG-as-PNG refused,
anonymous download 200 then 404 after deletion, referenced asset refused (409 with referencing designs), quota reached after 50
active (409 QUOTA_EXCEEDED), bulk cleanup purged 49 unreferenced, orphan sweep removed a 3-hour-old orphan and kept a fresh one
and a non-asset file. TV routes kept zero interactive controls.
