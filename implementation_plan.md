# HR Directory Mapping — Plant Suggestion & Filter Improvements

Enhance the HR Employee Directory at `/hr/directory` to include plant suggestions inferred from Primavera and comprehensive filtering to make bulk mapping more efficient.

## User Review Required

> [!IMPORTANT]
> **Read-Only Primavera Access**: This implementation queries Primavera `Funcionarios` tables to infer plant from `SourceCompany`. No writes are made to Primavera — all suggestions are stored in the Portal's local `HREmployee` table as advisory metadata.

> [!IMPORTANT]
> **No Auto-Mapping**: Suggestions are displayed alongside confirmed mapping fields but never auto-applied. HR/Admin must explicitly confirm any suggestion before it becomes a confirmed mapping.

## Open Questions

> [!IMPORTANT]
> **Plant Inference Strategy**: From research, the Primavera data model has **separate databases per company** (`ALPLAPLASTICO` → `PRI297514001`, `ALPLASOPRO` → `PRI297514003`). The employee's `EmployeeCode` (Innux `Numero` = Primavera `Codigo`) can be looked up in each Primavera company database. If found in `ALPLAPLASTICO`, the employee likely belongs to **Viana 1 or Viana 2** plants. If found in `ALPLASOPRO`, the employee belongs to **Viana 3**.
>
> This gives us company-level inference but not specific plant within a company. Is this granularity sufficient?
> - **ALPLAPLASTICO** → [Viana 1, Viana 2] — requires HR to pick one
> - **ALPLASOPRO** → [Viana 3] — auto-suggests directly
>
> **Alternatively**, should we also explore Primavera's `Funcionarios.CodDepartamento` → `Departamentos.Departamento` path to try to disambiguate within the company?

> [!WARNING]
> **Innux CostCenter**: The Innux `dbo.Funcionarios.CentroCusto` field is already synced into the `InnuxEmployeeDto`. If CostCenter values correlate with plant codes, we could use this as an additional inference signal during sync. Do you know if CostCenter maps to plant identifiers?

## Proposed Changes

### Phase 1: Backend — Plant Suggestion Enrichment

---

#### [MODIFY] [HREmployee.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/HREmployee.cs)

Add suggestion metadata fields (advisory only, never confused with confirmed mapping):

```csharp
// ─── Plant Suggestion Fields (advisory, from Primavera lookup) ───
public string? SuggestedPlantSource { get; set; }       // e.g., "PRIMAVERA:ALPLAPLASTICO"
public string? SuggestedPlantReason { get; set; }        // e.g., "Employee found in ALPLAPLASTICO database"
public DateTime? SuggestedPlantResolvedAtUtc { get; set; }
```

---

#### [NEW] [PrimaveraPlantSuggestionService.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Integration/PrimaveraPlantSuggestionService.cs)

New read-only service that:
1. For each unmapped HREmployee (no `PlantId`), queries `EmployeeCode` across all configured Primavera company databases
2. If found in exactly one company → resolves to the corresponding plant(s)
3. Stores the result as suggestion metadata on `HREmployee`
4. Uses the existing `PrimaveraConnectionFactory` — no new connections

Logic:
```
ALPLAPLASTICO match → suggest "Viana 1 or Viana 2" (company-level)
ALPLASOPRO match → suggest "Viana 3" (direct match)
Both matches → ambiguous, mark as "Multi-company" 
No match → mark as "Not found in Primavera"
```

---

#### [MODIFY] [HREmployeeSyncService.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Integration/HREmployeeSyncService.cs)

After employee sync completes, trigger suggestion enrichment for newly created or still-unmapped employees. Non-destructive: only updates suggestion fields, never touches confirmed `PlantId`.

---

#### [MODIFY] [HRLeaveController.cs](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Api/Controllers/HRLeaveController.cs)

**GetEmployees endpoint** — Add fields to the response DTO:
- `suggestedPlantSource`, `suggestedPlantReason`, `suggestedPlantResolvedAtUtc`

**Add new filter parameters**:
- `plantId` (int?) — filter by confirmed plant
- `departmentMasterId` (int?) — filter by confirmed department
- `mappingStatus` (string?) — `"mapped"`, `"unmapped"`, `"partial"` (has some but not all fields)
- `innuxDepartment` (string?) — filter by Innux source department name
- `hasSuggestion` (bool?) — filter employees that have a plant suggestion

**Add new endpoint**: `POST employees/resolve-suggestions` — triggers on-demand suggestion enrichment (Admin/HR only).

---

#### [NEW] EF Migration

Add migration for the 3 new nullable columns on `HREmployees`.

---

### Phase 2: Frontend — Filter Bar & Suggestion UI

---

#### [MODIFY] [HREmployeeDirectory.tsx](file:///c:/dev/alpla-portal/src/frontend/src/pages/HR/HREmployeeDirectory.tsx)

**A. Add Filter Bar** below the header, following the `RULE_KPI_DASHBOARD.md` patterns:

| Filter | Type | Source |
|--------|------|--------|
| Status | Chip group | `Todos`, `Mapeados`, `Não Mapeados`, `Com Sugestão` |
| Planta (Portal) | Select dropdown | `/api/v1/lookups/plants` |
| Departamento (Mestre) | Select dropdown | `/api/hr/leave/departments/master` |
| Origem (Innux) | Select dropdown | Distinct `innuxDepartmentName` from employee list |

Filter state persisted in URL query parameters for bookmark/share support.

**B. Employee Row — Suggestion Column**

For unmapped employees with a suggestion, show an inline hint:
```
🔍 Sugestão: Viana 3 (ALPLASOPRO)
   Fonte: Primavera | Atualizado: 2025-01-15
```

With a one-click "Accept Suggestion" button that pre-fills the Plant dropdown when entering edit mode.

**C. KPI Summary Bar** (top of page):
- Total Employees (active)
- Fully Mapped
- Unmapped
- With Suggestion

Cards are clickable and apply the corresponding filter.

---

#### [MODIFY] [api.ts](file:///c:/dev/alpla-portal/src/frontend/src/lib/api.ts)

Add:
- `resolveSuggestions()` method → `POST /api/hr/leave/employees/resolve-suggestions`
- Update `getEmployees()` to pass new filter params

---

### Phase 3: Documentation

---

#### [MODIFY] [CHANGELOG.md](file:///c:/dev/alpla-portal/docs/CHANGELOG.md)

Document the new features under the next version.

#### [MODIFY] [FRONTEND_FOUNDATION.md](file:///c:/dev/alpla-portal/docs/FRONTEND_FOUNDATION.md)

If the filter bar introduces a new reusable pattern, document it.

---

## Verification Plan

### Automated Tests
- Build backend: `dotnet build` — ensure no compilation errors after migration/new service
- EF migration: `dotnet ef migrations add PlantSuggestion` — verify clean migration generation

### Manual Verification
1. Navigate to `http://localhost:5173/hr/directory`
2. Verify filter bar renders with all 4 filter types
3. Verify KPI cards show correct counts
4. Click "Sincronizar Dados Mestre" — verify suggestions are populated for unmapped employees
5. Verify unmapped employees show suggestion hint in their row
6. Click "Accept Suggestion" → verify plant dropdown is pre-filled
7. Confirm saving works normally (no regression)
8. Test filter combinations — verify pagination updates correctly
9. Verify no writes are made to Primavera databases (check backend logs)
