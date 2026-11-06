# Implementation Plan — Viana 3 Production Monitor (TV screens, layout editor, card-design editor)

> Status: **Implemented and validated in local DEV (2026-11-04) — awaiting manual validation in TEST.** Backend build 0 errors,
> `tsc -b` clean, Vite build OK. No commit/push/version change until `/task-publish`.
> **Two additive EF migrations** are pending for TEST: `20261104134729_AddOperationsBoardLayout` (screens) and
> `20261104151526_AddOperationsCardDesign` (card designs + design assets). Both were applied to the **local DEV clone only** by the
> Development auto-migrate (`Program.cs` runs `Database.Migrate()` in Development when `execution/restart_services.ps1` starts the
> backend). TEST/PROD untouched. Existing DEV screens ("Ecrã principal", "Envase 1", "Envase 2 - Norte") preserved.
> Reference query: `AlplaPROD_aovia3_tv_preform_monitor_v5.sql` (Downloads, "v5 (1)" wording).

## Scope delivered (four iterations, all uncommitted)

1. TV page + diagnostics (read-only AlplaPROD projection, rules: scanner ≠ consumption, provisional deviation, Waste separate, retention apart).
2. Read-only TV + shared layout editor; "Maximum update depth" crash fixed (change-only fit reporting).
3. Multiple independent TV screens with dedicated URLs (`/operations/viana3-monitor/screen/{key}`).
4. **Machine-card design editor** (`/operations/viana3-monitor/card-design`): template + per-machine designs shared across screens,
   drag/resize/rotate/colour/visibility/display properties/images, read-only verified data-source panel, readability warnings,
   raster-only assets with hash reuse, 50-image quota, two-phase deletion and reconciliation, per-board application lock.

## Decisions honoured (card design)

| Decision | Implementation |
|----------|----------------|
| No SVG; PNG/JPEG/WebP only | `Viana3DesignAssetService.UploadAsync`: extension, declared type, magic bytes, Magick.NET decode + re-encode; content type served from the row only. |
| Board-wide template; machine override; built-in fallback | `OperationsCardDesign.MachineId = 0` = template; `resolveDesign` own → template → null; `MachinePanel` kept for undesigned machines. |
| Full copy on personalise; reset removes the override | Save on a machine without a row creates it from the document the editor shows; `DELETE …/{machineId}` removes it. |
| Six free items | `Viana3CardDesignRules.MaxFreeItems` enforced server-side and in the editor. |
| Readability 14 / 11 px, visible, non-blocking | `computeReadability` per screen placement (`cardBoxFor` mirrors the board geometry); chips in the editor; never blocks saving. |
| Previous orders and pallet stack designable, internal layout fixed | Items `previousOrders` / `palletStack` are positioned boxes rendering the existing components scaled to fit. |
| Catalogue = source of truth, verified against the real query | `Viana3CardItemCatalog` + `Viana3CardCatalogVerifier` (renders Q1/Q2 text from the builder, checks alias/table/field, reflection for service methods and DTO fields; expression extracted at verification time, never stored). |
| Hash reuse before quota; quota authoritative inside the lock | Upload: lookup by (board, hash, active) first; then `COUNT(active) < 50` inside the `sp_getapplock` transaction before the insert; post-commit count is a diagnostic log only. |
| Two-phase deletion + reconciliation | Tombstone (`DeletedAtUtc`) committed first; purge in a separate locked transaction; `PurgeAttempts`/`LastPurgeError`; sweep purges tombstones, removes age-guarded orphan files, reports missing files. |
| Per-board lock shared by design and asset writes; conflicts returned once | `Viana3BoardLock` (transaction-owned `sp_getapplock`); `BOARD_BUSY` (+Retry-After, editor retries once), `ASSET_UNAVAILABLE`, `ASSET_REFERENCED`, `QUOTA_EXCEEDED`, `DESIGN_MODIFIED`; duplicate upload → reuse. |
| Retry-safe file work | File written under its unique id before the delegate; delegate database-only and idempotent on asset id / save token; reuse or failure deletes this request's file; crashes leave orphans for the sweep. |

## Files (iteration 4)

### Backend
- `AlplaPortal.Domain/Entities/OperationsCardDesign.cs` — `OperationsCardDesign`, `OperationsDesignAsset`.
- `AlplaPortal.Infrastructure/Data/Configurations/OperationsCardDesignConfiguration.cs`; `Data/ApplicationDbContext.cs` (DbSets); `Data/Migrations/20261104151526_AddOperationsCardDesign.cs` (+ Designer, snapshot).
- `AlplaPortal.Application/DTOs/Operations/Viana3CardDesignDtos.cs`; `Interfaces/Operations/IViana3CardDesignService.cs`.
- `AlplaPortal.Infrastructure/Services/Operations/Viana3BoardLock.cs`, `Viana3CardItemCatalog.cs`, `Viana3CardDesignService.cs`, `Viana3DesignAssetService.cs`.
- `AlplaPortal.Api/Controllers/Viana3CardDesignController.cs`; `Program.cs` (DI); `appsettings.json` (`AppConfig:OperationsCardDesignStoragePath`).

### Frontend
- `types/viana3CardDesign.types.ts`; `lib/viana3CardDesignApi.ts`.
- `pages/Operations/viana3/viana3CardDesign.ts` (model, default design, resolution, formats, readability); `pages/Operations/viana3/Viana3DesignedPanel.tsx` (renderer).
- `pages/Operations/viana3/Viana3Board.tsx` (exports, `designs` prop, designed-card branch); `Viana3ProductionMonitorPage.tsx` and `Viana3MonitorLayoutEditorPage.tsx` (load the bundle).
- `pages/Operations/Viana3CardDesignEditorPage.tsx` (new editor); `App.tsx` (route); `constants/navigation.tsx` (menu entry).

### Docs
- `docs/OPERATIONS_VIANA3_PRODUCTION_MONITOR.md` §2/§3 updated, §11 added. `CHANGELOG.md` / `VERSION.md` / `DECISIONS.md` untouched (reserved for `/task-publish`).

## DEV validation summary (2026-11-04)
- Builds: backend 0 errors; `tsc -b` clean; Vite build OK (ESLint not part of the toolchain; no tests run or added, as instructed).
- Migration `20261104151526_AddOperationsCardDesign` applied to the DEV clone by auto-migrate (tables, unique and filtered indexes confirmed by query); screens intact.
- 2026-11-05 DEV incident: the clone was found with `AddOperationsCardDesign` reverted (history row and both tables gone, `AddOperationsBoardLayout` intact, API still running → 500 `Invalid object name 'OperationsCardDesigns'` on card-design/assets/template). Re-applied the same migration from the generated EF script (additive only) and rebuilt the lost rows from the audit log (designs of machines 2 and 3 = exact `VIANA3_CARD_DESIGN_SAVED` payloads 29288/29285; asset 6ee16907 from the file on disk + upload event 29268). TEST/PROD untouched.
- Catalogue: 17 items / 26 sources VERIFIED at runtime; anonymous catalogue request 401.
- Editor scenario (headless Chrome): template edited (accent, hide, move, label, COMPACT, PNG illustration, 15° rotation) and saved; machine 1 personalised (label, font 6 → "ilegível" band, font 12 → "texto pequeno"); template edited again → machine 1 unchanged, machine 3 follows; machine 1 reset → follows template.
- TV: both dedicated screens and the default URL rendered DESIGNED cards with the image illustration, green reported bar, hidden previous orders, COMPACT stock figure and the machine-1 scanner label; zero interactive controls.
- 2026-11-05 adjustable canvas width (600–1200 × fixed 440; server-validated; editor control with per-screen suggestions; items keep coordinates; readability/clipping per placement with the document canvas). DEV-validated on machine 2 (API 400 for 1300 and for height 400; editor 880→1000 saved via PUT 200 with items byte-identical; TV Grande cell content 1864×820 inside the full frame; machine 3 and layouts untouched; machine 2 restored to its saved design afterwards).
- 2026-11-05 fix: designed card's outer frame fills its grid cell (frame moved from the scaled canvas to the cell wrapper; canvas = transparent content layer). Measured at 1920×1080: TV default screen (machine 2, Grande, 2 columns) card rect == cell rect (0 px difference, 1864×946 stage units); editor preview with machine 1 (built-in) restored unsaved: both Grande cards 1864×466, each frame == cell. Saved layouts/designs untouched.
- Assets: valid PNG stored; identical upload reused; 4096 px, 2.4 MB, renamed executable, SVG and SVG-as-PNG refused (400); anonymous download 200 → 404 after deletion; referenced asset 409 (referencedBy [0,1]); quota: 49 new + 1 existing → 409 QUOTA_EXCEEDED ×3; bulk cleanup tombstoned and purged 49; orphan sweep removed a 3-hour-old orphan and kept a fresh one and a non-asset file.

## Validation (manual, TEST) — when authorised
1. Apply both migrations in TEST, deploy backend + frontend.
2. Screens and layout editor as before (§10 of the doc).
3. Card design: create the template, personalise one machine, confirm both TV screens; upload the transparent SBM PNG for the illustration; check the readability chips on a 2-column 3-row screen; try an SVG (refused); delete a referenced image (refused) and an unreferenced one; run "Limpar não utilizadas".
4. TV routes keep zero interactive controls; diagnostics and STALE behaviour unchanged.
