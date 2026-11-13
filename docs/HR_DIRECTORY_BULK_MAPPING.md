# HR Directory — "Mapeamento em lote" (bulk mapping workspace)

**Scope:** `/hr/directory/bulk-mapping` (HR → Directório & Mapeamento → button "Mapeamento em lote"). Review and map Innux
employees by team: plant, department master and responsible person, with a server-side preview before every write.
**Status:** implemented in local DEV on 2026-11-09, DEV-verified (§6), published as v2.251.0 on 2026-11-13 for TEST validation. No migration. The existing directory and
its single "Mapear" flow are unchanged. Excel import and automatic department/manager inference are a later phase.

Builds on Phase 1 (`docs/HR_DIRECTORY_MAPPING_PHASE1.md`): company map, `mappingState`, `suggestionState`, company consistency.

## 1. Screen

| Area | Behaviour |
|---|---|
| KPIs | Computed on the current filter (base scope): no filtro, completos, parciais, não mapeados, sem planta, sugestão aplicável, escolher planta. |
| Filters | Search; **Empresa (sugestão Primavera)** APA / APS / Ambas (manual) / Sem sugestão; **Departamento Innux** (distinct values with "sem planta/total"); **Planta atribuída** (incl. "Sem planta"); **Campos em falta** (qualquer / planta / departamento / responsável); **Estado**. |
| Grid | Funcionário (nome, #número), Dept. Innux, Sugestão (empresa → planta + estado), Planta, Departamento mestre, Responsável, Estado, Ações (Editar = exceção individual, single save). |
| Selection | Persists across pages and filter changes. "Selecionar página (n)" / "Desmarcar página" vs "Selecionar todos os N filtrados" (server returns every matching id). The exact selected count is always shown, plus "nesta página" and "no filtro atual". |
| Panel — Rever sugestões de planta | **Confirmar sugestões aplicáveis**: only the selected employees whose `suggestionState` is ACTIONABLE (server rule) are included; count shown. **Escolher planta**: for employees whose suggested company has several plants (APA → Viana 1 / Viana 2) HR marks the subset and picks the plant explicitly; a shared Innux department never decides the plant. |
| Panel — Atribuir equipa | Any combination of plant / department master / responsável. Department picker is filtered by the company of the chosen plant; without a plant, by the single company of the selection's existing plants; with no plants in the selection HR chooses the company. Mixed companies without a plant ⇒ conflict box with counts and "Manter só APA/APS" to narrow. Per-field "Substituir…" checkbox (default off = fill empty only). |
| Preview modal | One row per selected employee: current → proposed per field, "mantido" tag when an existing value is preserved, CHANGE / UNCHANGED / BLOCKED with the reason, grouped conflicts with "Remover da seleção e recalcular". "Confirmar e gravar" enabled only when nothing is blocked and at least one row changes. |

## 2. API (all `IsAdminOrHR`; same data scope as the directory)

| Endpoint | Purpose |
|---|---|
| `GET /api/hr/leave/employees` | Unchanged contract; new filters `suggestedCompany=APA\|APS\|MULTI\|NONE`, `innuxDepartmentExact=true`, `plantId=0` (without plant), `missingField=any`. |
| `GET /api/hr/leave/employees/ids` | Same filters → `{ total, items[] }` light rows (id, code, name, plant + company, department, manager, mappingState, suggestionState, suggested company). |
| `GET /api/hr/leave/employees/innux-departments` | Distinct Innux departments `{ name, total, withoutPlant }`. |
| `POST /api/hr/leave/employees/bulk-mapping/preview` | Body `HRBulkMappingPlanRequest`. Returns rows, counts, conflicts, `canApply`, `expected` (current values). Nothing written. |
| `POST /api/hr/leave/employees/bulk-mapping/apply` | Body = plan + `expected[]`. One transaction: re-evaluates on current data; rejects before any write on 409 `STALE_SELECTION` (values changed since preview; employees listed), 400 `BULK_MAPPING_BLOCKED` (final plant/department pair of another company, suggestion not actionable, out of scope, inactive, not found; employees + reasons listed), 400 `PLANT_NOT_FOUND` / `DEPARTMENT_MASTER_NOT_FOUND` / `MANAGER_NOT_FOUND` / `NO_CHANGES`. Single `SaveChanges` → all or nothing. |

Plan: `{ employeeIds, mode: "ASSIGN" | "CONFIRM_SUGGESTIONS", plantId?, departmentMasterId?, managerUserId?, overwritePlant?, overwriteDepartment?, overwriteManager? }`.

## 3. Rules (`HRBulkMappingPolicy`, pure)

- **Fill by default**: a planned value is written only where the field is empty; an existing different value is kept (row/field marked "mantido") unless the field's overwrite flag is set. Fields not in the plan are never touched.
- **Final pair**: for every employee the resulting (plant, department master) pair must share a company (`HRDirectoryMappingPolicy.IsCompanyConsistent`). Reasons: `COMPANY_MISMATCH_EXISTING_PLANT` (department-only onto a plant of another company), `COMPANY_MISMATCH_EXISTING_DEPARTMENT` (plant onto a kept department of another company), `COMPANY_MISMATCH_REQUESTED`. A legacy mismatch the plan does not touch (e.g. manager-only) stays allowed, as in the single save.
- **Confirm suggestions**: `ComputeSuggestionState` must be ACTIONABLE with exactly one candidate plant (lookup complete for the current scope, one company, one active plant). NEEDS_CHOICE / MANUAL / UNVERIFIED rows are BLOCKED with the reason; rows that already have a plant are UNCHANGED.
- Any BLOCKED row blocks the whole batch; the UI offers to remove those employees from the selection and recalculate.

## 4. End-to-end example (DEV, Innux department LOGÍSTICA: 19 APA + 1 APS, all without plant)

1. Filter Departamento Innux = LOGÍSTICA → 20 rows; "Selecionar todos os 20 filtrados".
2. Tab "Rever sugestões de planta": 1 aplicável (APS → Viana 3) → Pré-visualizar confirmação (1) → 1 atualizado → Confirmar e gravar.
3. Still 19 selected (the saved employee left the selection). Uncheck the ones that belong to Viana 2, pick **Viana 1** → Pré-visualizar atribuição de Viana 1 (10) → Confirmar. Filter Planta = Sem planta → Selecionar página (9) → **Viana 2** → Pré-visualizar → Confirmar.
4. Tab "Atribuir equipa" with all 20 selected and no plant chosen: conflict "plantas de empresas diferentes: APA (19), APS (1)" → "Manter só APA (19)" → department (AlplaPLASTICOS list) + responsável → Pré-visualizar: 19 rows, Planta column unchanged, Departamento and Responsável vazio → valor → Confirmar.
5. Running the same assignment again with another department and "Substituir" off → 19 "Sem alteração (mantido)", nothing to save.

## 5. DEV fixture

The DEV clone was restored from PROD before the lookup-completeness columns existed, so every suggestion is UNVERIFIED. For the
DEV verification the rows with a Primavera suggestion were stamped with the current lookup scope and `SuggestedPlantLookupComplete = 1`
(DEV only, reversible: `UPDATE HREmployees SET SuggestedPlantLookupScope = NULL, SuggestedPlantLookupComplete = NULL`). In TEST/PROD
the same state is produced by "Sincronizar" with both companies reachable.

## 6. DEV verification (2026-11-09)

See the session report / `implementation_plan.md`. Cases: several APS suggestions confirmed together; an APA Innux department
split between Viana 1 and Viana 2 by explicit subsets; department + manager assigned without touching plants; existing values
preserved unless "Substituir"; department-only on mixed companies blocked (400, nothing written); stale batch rejected (409,
nothing written); non-HR role 403. DEV mapping columns restored to the pre-verification snapshot afterwards.
