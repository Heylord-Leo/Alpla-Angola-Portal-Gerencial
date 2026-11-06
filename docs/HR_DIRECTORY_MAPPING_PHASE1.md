# HR Directory — Mapping Workflow, Phase 1

**Scope:** `/hr/directory` (HR → Directory and Mapping). Manual mapping of Innux employees to Plant, Department Master and Responsible Person.
**Status:** released in **v2.249.9** (2026-11-06) for manual validation in TEST; migration `20261105163725` applied and verified in `Portal-Gerencial-Test` on 2026-11-06. One additive migration. No behaviour change for the Innux employee sync.

## 1. Company map (confirmed against PROD on 2026-11-05)

| Primavera company | `HREmployees.SuggestedPlantSource` | `Companies.Code` | `DepartmentMasters.CompanyCode` | Source database |
|---|---|---|---|---|
| `ALPLAPLASTICO` | `PRIMAVERA:ALPLAPLASTICO` | `APA` | `AlplaPLASTICOS` | `PRI2975140` |
| `ALPLASOPRO` | `PRIMAVERA:ALPLASOPRO` | `APS` | `AlplaSOPRO` | `PRI297514003` |

Single source of truth: `HRDirectoryMappingPolicy.CompanyMap` (`src/backend/AlplaPortal.Application/Validation/HRDirectoryMappingPolicy.cs`). Labels are exact and case-sensitive (final `S` in `AlplaPLASTICOS`). Plants are linked through `Plant.CompanyId`; plant display names are never parsed.

## 2. Mapping state and the stored flag

- `mappingState` (computed, returned by `GET /api/hr/leave/employees`): `COMPLETE` = plant + department master + manager; `UNMAPPED` = none of the three; `PARTIAL` = otherwise. The legacy `PortalDepartmentId` does not count.
- `HREmployees.IsMapped` keeps its legacy meaning (any of plant, portal department, department master, manager) and its writers, so the `mapped` query parameter, the index and older callers are unchanged. The UI no longer reads it.
- KPI summary is computed on the **base scope** only: role scope, active, free-text search, plant, department-master and Innux-department filters. The status chips (`mappingStatus=complete|partial|unmapped`, `missingField`, `suggestionState`, legacy `hasSuggestion`/`mapped`) filter the list only, so chip counts stay stable. `fullyMapped` and `withSuggestion` remain in the payload for compatibility.

## 3. Plant suggestion: completeness and actionability

`PrimaveraPlantSuggestionService` now fails closed: if any configured Primavera company does not answer, **nothing is persisted** (`lookupComplete = false`, message "Consulta incompleta"). Successful runs stamp `SuggestedPlantLookupScope` (canonical, e.g. `APA|APS`) and `SuggestedPlantLookupComplete = 1`.

`suggestionState` (computed by `HRDirectoryMappingPolicy.ComputeSuggestionState`, used by both the "Sugestão aplicável" KPI and the "Aceitar" button):

| State | Condition | UI |
|---|---|---|
| `ACTIONABLE` | no plant ∧ lookup complete for the current scope ∧ found in exactly one company ∧ that company has exactly one active plant | "✓ Aceitar Sugestão" pre-fills that plant |
| `NEEDS_CHOICE` | same, but the company has several active plants (APA → Viana 1 / Viana 2) | "✎ Escolher planta"; suggested plants listed first |
| `MANUAL` | found in both companies (`PRIMAVERA:MULTI`) | "✎ Mapear Manualmente" |
| `UNVERIFIED` | `SuggestedPlantLookupComplete` is null/false, or the scope differs from the current configuration (legacy rows) | shown "não verificada" until the next complete "Sincronizar" |
| `NONE` | plant already set, or `NOT_FOUND` / `NO_CODE` / no suggestion | hint hidden (NOT_FOUND shows a muted note) |

Migration `20261105163725_AddHREmployeeSuggestionLookupScope` adds the two nullable columns (`nvarchar(64)`, `bit`). After deployment, run "Sincronizar" once with both companies reachable to refresh every pending row; until then legacy suggestions are UNVERIFIED by design.

## 4. Department filtering and company consistency

- The department picker is filtered by the company of the **plant selected by HR** (`Plant.CompanyId` → map → master label). The suggested company only orders the plant dropdown (suggested plants first, marked ★); the full list stays available. Changing the plant to another company clears the department in the form.
- Server-side rule (`HRDirectoryMappingPolicy.IsCompanyConsistent`): a plant and a department master must share a company.
  - `PUT employees/{id}/mapping`: validated whenever the plant or the department master changes. A manager-only edit on a record with a legacy mismatch is allowed.
  - `PUT employees/bulk/mapping`: whenever the request touches plant or department (including department-only updates or `clearDepartmentMaster`), the **final** pair of every affected employee is validated and the whole batch is rejected with 400 `COMPANY_MISMATCH` (employee codes listed) before any write. Manager-only batches skip the check.
- `GET departments/master?search=&companyCode=` narrows by name or code and by company (accepts `APA`/`APS` or the master label).

## 5. Responsible person

`GET employees/mapping-context` returns active plants (with `companyCode` and `departmentMasterCompanyCode`), the company map, the current lookup scope and the manager candidates. **All active users remain selectable** while HR reviews the eligibility policy. Users meeting Option C (active `DepartmentManagers` row, Local Manager, HR, System Administrator) are grouped first with their reasons. The current assignment is always kept in the editor, even if the user is inactive or outside Option C. `eligibilityPolicy = "OPTION_C_HIGHLIGHT_ONLY"`: nothing is enforced.

## 6. TEST validation checklist

Preconditions: migration `20261105163725` applied (additive; rehearsed on a copy of the PROD baseline), backend + frontend deployed, Primavera and Innux providers enabled in `/admin/integrations`.

1. **Complete lookup.** With both Primavera companies reachable, run "Sincronizar". Expected: message with `[Sugestões] Alta/Ambígua/N/E` counts; rows without a plant show "Sugestão: Viana 3 — Aplicável" (APS) or "Viana 1 ou Viana 2 — Escolha necessária" (APA); `SuggestedPlantLookupScope = 'APA|APS'`, `SuggestedPlantLookupComplete = 1` on refreshed rows; KPI "Sugestão aplicável" equals the number of "✓ Aceitar Sugestão" buttons across all pages.
2. **Failed lookup.** Disable the ALPLASOPRO company (or make it unreachable) and run "Sincronizar" again. Expected: resolution reports "Consulta incompleta (1/2 empresas responderam). Nenhuma sugestão foi gravada."; no row changes (`SuggestedPlantResolvedAtUtc` unchanged); rows previously resolved stay as they were. Re-enable and sync: rows refreshed.
3. **Legacy rows.** Any row with `SuggestedPlantLookupComplete IS NULL` shows "não verificada" and no "Aceitar" button until step 1 refreshes it.
4. **APS suggestion overridden by an APA plant.** Pick an employee with "Viana 3 — Aplicável". Click "Mapear", choose **Viana 1** in the plant list (Viana 3 is listed first under "Sugeridas"). Expected: department picker shows only `AlplaPLASTICOS` masters; if a department had been chosen before the plant change it is cleared with the "Departamento Mestre limpo" message; save succeeds; the row shows Viana 1 and the hint disappears.
5. **Single-save mismatch.** Via the API (or by editing a row whose stored department is from the other company), send plant Viana 1 with an `AlplaSOPRO` department master. Expected: 400 `COMPANY_MISMATCH` with the employee code; nothing saved.
6. **Department-only bulk mismatch.** Select two employees already on Viana 1 and one on Viana 3; bulk-assign only an `AlplaPLASTICOS` department master (no plant). Expected: 400 `COMPANY_MISMATCH` listing the Viana 3 employee's code; none of the three rows changed. Repeat with the Viana 3 employee deselected: all updated.
7. **Manager-only edit on a legacy mismatch.** For a row flagged with the red building icon (plant and department from different companies), change only the responsible person and save. Expected: saved; the flag remains until the pair is corrected.
8. **Responsible-person picker.** Open "Mapear": the list shows "Elegíveis — Opção C" first with reasons, then "Outros utilizadores ativos"; a row whose current manager is inactive still shows "(atual — utilizador inativo)" selected; saving without changing the manager keeps it.
9. **KPIs and chips.** Completos + Parciais + Não Mapeados = Total under every filter; clicking a chip changes the list but not the KPI counts; legacy `?mapped=true` and `?hasSuggestion=true` still work.
10. **Department search.** In the picker, type part of a department name or code: the list narrows; empty term shows the company's full list.

## 7. Pending decisions

- HR: eligibility rule (Option A role filter, B `DepartmentManagers`, or C module access) before enforcement; authoritative department when Innux and Primavera disagree; treatment of employees present in both companies; validation of the master ↔ Portal department link before any manager suggestion.
- Technical: none blocking. The diagnostic package (scratchpad, not in the repo) is to be run per `docs`-independent instructions supplied in the review.
