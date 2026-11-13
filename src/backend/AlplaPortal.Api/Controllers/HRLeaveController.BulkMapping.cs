using AlplaPortal.Application.DTOs.HR;
using AlplaPortal.Application.Validation;
using AlplaPortal.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// HR directory: shared directory query builder and the "Mapeamento em lote" endpoints.
///
///   GET  employees/ids                    light rows of every employee matching the directory filters (select all filtered)
///   GET  employees/innux-departments      distinct Innux departments in the caller's scope (filter source)
///   POST employees/bulk-mapping/preview   exact employees and field changes a plan would produce (nothing written)
///   POST employees/bulk-mapping/apply     re-evaluates the plan, checks the values seen at preview time and writes all or nothing
///
/// Authorization: HR role or System Administrator (same as the single and legacy bulk mapping saves). Data scope: the
/// same GetScopedEmployeesQuery as the directory; an employee outside the caller's scope blocks the batch.
/// </summary>
public partial class HRLeaveController
{
    // ───────────────────────── shared directory query ─────────────────────────

    private sealed class DirectoryQuery
    {
        public IQueryable<HREmployee> BaseQuery { get; init; } = null!;
        public IQueryable<HREmployee> Query { get; init; } = null!;
        public Dictionary<Guid, string> SuggestionStateById { get; init; } = new();
        public string CurrentScope { get; init; } = string.Empty;
        public Func<string?, List<int>> CandidatePlants { get; init; } = _ => new List<int>();

        public string SuggestionStateOf(int? plantId, string? source, bool? complete, string? scope)
            => HRDirectoryMappingPolicy.ComputeSuggestionState(plantId, source, complete, scope, CurrentScope, CandidatePlants(source).Count);
    }

    /// <summary>
    /// Base scope (role scope + active + search + plant / department master / Innux department / suggested company) and the
    /// list filters (status chips) on top of it. The KPI summary of GetEmployees is computed on BaseQuery only.
    /// plantId = 0 means "without plant" (grid filter "Sem planta"); innuxDepartmentExact switches the Innux filter from
    /// "contains" to an exact (case-insensitive) match, used by the grid's department select.
    /// </summary>
    private async Task<DirectoryQuery> BuildDirectoryQueryAsync(
        string? search, bool? mapped, bool? active, int? plantId, int? departmentMasterId, string? mappingStatus,
        string? innuxDepartment, bool innuxDepartmentExact, bool? hasSuggestion, string? missingField,
        string? suggestionState, string? suggestedCompany)
    {
        var baseQuery = await GetScopedEmployeesQuery();

        if (active.HasValue)
            baseQuery = baseQuery.Where(e => e.IsActive == active.Value);
        else
            baseQuery = baseQuery.Where(e => e.IsActive); // Default: active only

        if (plantId.HasValue)
            baseQuery = plantId.Value == 0
                ? baseQuery.Where(e => !e.PlantId.HasValue)
                : baseQuery.Where(e => e.PlantId == plantId.Value);

        if (departmentMasterId.HasValue)
            baseQuery = baseQuery.Where(e => e.DepartmentMasterId == departmentMasterId.Value);

        if (!string.IsNullOrWhiteSpace(innuxDepartment))
        {
            var dept = innuxDepartment.Trim().ToLower();
            baseQuery = innuxDepartmentExact
                ? baseQuery.Where(e => e.InnuxDepartmentName != null && e.InnuxDepartmentName.ToLower() == dept)
                : baseQuery.Where(e => e.InnuxDepartmentName != null && e.InnuxDepartmentName.ToLower().Contains(dept));
        }

        if (!string.IsNullOrWhiteSpace(suggestedCompany))
        {
            var wanted = suggestedCompany.Trim().ToUpperInvariant();
            if (wanted == "MULTI")
                baseQuery = baseQuery.Where(e => e.SuggestedPlantSource == HRDirectoryMappingPolicy.SourceMulti);
            else if (wanted == "NONE")
                baseQuery = baseQuery.Where(e => e.SuggestedPlantSource == null
                    || e.SuggestedPlantSource == HRDirectoryMappingPolicy.SourceNotFound
                    || e.SuggestedPlantSource == HRDirectoryMappingPolicy.SourceNoCode);
            else
            {
                var source = HRDirectoryMappingPolicy.ByCompanyCode(wanted)?.SuggestedPlantSource ?? "\u0000"; // unknown code matches nothing
                baseQuery = baseQuery.Where(e => e.SuggestedPlantSource == source);
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            baseQuery = baseQuery.Where(e =>
                e.FullName.ToLower().Contains(term) ||
                e.EmployeeCode.ToLower().Contains(term) ||
                (e.InnuxDepartmentName != null && e.InnuxDepartmentName.ToLower().Contains(term))
            );
        }

        // ─── Suggestion context: one rule (HRDirectoryMappingPolicy) for the KPI, the "Aceitar" button and the bulk confirmation ───
        var currentScope = CurrentLookupScope();
        var activePlants = await _context.Plants
            .Where(p => p.IsActive)
            .Select(p => new { p.Id, CompanyCode = p.Company.Code })
            .ToListAsync();
        var plantIdsByCompany = activePlants
            .Where(p => p.CompanyCode != null)
            .GroupBy(p => p.CompanyCode!)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Id).OrderBy(id => id).ToList(), StringComparer.Ordinal);

        List<int> CandidatePlants(string? source)
        {
            var entry = HRDirectoryMappingPolicy.BySuggestedPlantSource(source);
            return entry != null && plantIdsByCompany.TryGetValue(entry.CompanyCode, out var ids) ? ids : new List<int>();
        }

        var suggestionRows = await baseQuery
            .Select(e => new { e.Id, e.PlantId, e.SuggestedPlantSource, e.SuggestedPlantLookupComplete, e.SuggestedPlantLookupScope })
            .ToListAsync();
        var suggestionStateById = suggestionRows.ToDictionary(
            r => r.Id,
            r => HRDirectoryMappingPolicy.ComputeSuggestionState(
                r.PlantId, r.SuggestedPlantSource, r.SuggestedPlantLookupComplete, r.SuggestedPlantLookupScope,
                currentScope, CandidatePlants(r.SuggestedPlantSource).Count));

        // ─── List filters (status chips) applied on top of the base scope ───
        var query = baseQuery;

        if (mapped.HasValue) // legacy stored flag, unchanged semantics
            query = query.Where(e => e.IsMapped == mapped.Value);

        if (!string.IsNullOrWhiteSpace(mappingStatus))
        {
            query = mappingStatus.Trim().ToLower() switch
            {
                "mapped" or "complete" => query.Where(e => e.PlantId.HasValue && e.DepartmentMasterId.HasValue && e.ManagerUserId.HasValue),
                "unmapped" => query.Where(e => !e.PlantId.HasValue && !e.DepartmentMasterId.HasValue && !e.ManagerUserId.HasValue),
                "partial" => query.Where(e =>
                    !(e.PlantId.HasValue && e.DepartmentMasterId.HasValue && e.ManagerUserId.HasValue) &&
                    (e.PlantId.HasValue || e.DepartmentMasterId.HasValue || e.ManagerUserId.HasValue)),
                _ => query
            };
        }

        if (!string.IsNullOrWhiteSpace(missingField))
        {
            query = missingField.Trim().ToLower() switch
            {
                "plant" => query.Where(e => !e.PlantId.HasValue),
                "department" => query.Where(e => !e.DepartmentMasterId.HasValue),
                "manager" => query.Where(e => !e.ManagerUserId.HasValue),
                "any" => query.Where(e => !e.PlantId.HasValue || !e.DepartmentMasterId.HasValue || !e.ManagerUserId.HasValue),
                _ => query
            };
        }

        if (hasSuggestion.HasValue) // legacy filter, unchanged semantics
        {
            if (hasSuggestion.Value)
                query = query.Where(e => e.SuggestedPlantConfidence != null && e.SuggestedPlantConfidence != "NotFound");
            else
                query = query.Where(e => e.SuggestedPlantConfidence == null || e.SuggestedPlantConfidence == "NotFound");
        }

        if (!string.IsNullOrWhiteSpace(suggestionState))
        {
            var wanted = suggestionState.Trim().ToUpperInvariant();
            var ids = suggestionStateById.Where(kv => kv.Value == wanted).Select(kv => kv.Key).ToList();
            query = query.Where(e => ids.Contains(e.Id));
        }

        return new DirectoryQuery
        {
            BaseQuery = baseQuery,
            Query = query,
            SuggestionStateById = suggestionStateById,
            CurrentScope = currentScope,
            CandidatePlants = CandidatePlants,
        };
    }

    // ───────────────────────── selection helpers ─────────────────────────

    /// <summary>
    /// Light rows of EVERY employee matching the directory filters, in directory order. Used by "Selecionar todos os
    /// resultados filtrados" so the selection and its exact count never depend on the current page.
    /// </summary>
    [HttpGet("employees/ids")]
    public async Task<IActionResult> GetEmployeeIds(
        [FromQuery] string? search = null,
        [FromQuery] bool? mapped = null,
        [FromQuery] bool? active = null,
        [FromQuery] int? plantId = null,
        [FromQuery] int? departmentMasterId = null,
        [FromQuery] string? mappingStatus = null,
        [FromQuery] string? innuxDepartment = null,
        [FromQuery] bool innuxDepartmentExact = false,
        [FromQuery] bool? hasSuggestion = null,
        [FromQuery] string? missingField = null,
        [FromQuery] string? suggestionState = null,
        [FromQuery] string? suggestedCompany = null)
    {
        if (!IsAdminOrHR)
            return Forbid();

        var dq = await BuildDirectoryQueryAsync(search, mapped, active, plantId, departmentMasterId, mappingStatus,
            innuxDepartment, innuxDepartmentExact, hasSuggestion, missingField, suggestionState, suggestedCompany);

        var rows = await dq.Query
            .OrderBy(e => e.FullName)
            .Select(e => new
            {
                e.Id,
                e.EmployeeCode,
                e.FullName,
                e.PlantId,
                PlantCompanyCode = e.Plant != null ? e.Plant.Company.Code : null,
                e.DepartmentMasterId,
                e.ManagerUserId,
                e.SuggestedPlantSource,
            })
            .ToListAsync();

        var items = rows.Select(r => new
        {
            id = r.Id,
            employeeCode = r.EmployeeCode,
            fullName = r.FullName,
            plantId = r.PlantId,
            plantCompanyCode = r.PlantCompanyCode,
            departmentMasterId = r.DepartmentMasterId,
            managerUserId = r.ManagerUserId,
            mappingState = HRDirectoryMappingPolicy.ComputeMappingState(r.PlantId, r.DepartmentMasterId, r.ManagerUserId),
            suggestionState = dq.SuggestionStateById.TryGetValue(r.Id, out var s) ? s : HRDirectoryMappingPolicy.SuggestionNone,
            suggestedCompanyCode = HRDirectoryMappingPolicy.BySuggestedPlantSource(r.SuggestedPlantSource)?.CompanyCode,
        }).ToList();

        return Ok(new { total = items.Count, items });
    }

    /// <summary>Distinct Innux departments of the active employees in the caller's scope, with counts.</summary>
    [HttpGet("employees/innux-departments")]
    public async Task<IActionResult> GetInnuxDepartments()
    {
        if (!IsAdminOrHR)
            return Forbid();

        var scoped = await GetScopedEmployeesQuery();
        var rows = await scoped
            .Where(e => e.IsActive && e.InnuxDepartmentName != null && e.InnuxDepartmentName != "")
            .GroupBy(e => e.InnuxDepartmentName!)
            .Select(g => new { name = g.Key, total = g.Count(), withoutPlant = g.Count(e => !e.PlantId.HasValue) })
            .OrderBy(x => x.name)
            .ToListAsync();

        return Ok(rows);
    }

    // ───────────────────────── bulk mapping: preview / apply ─────────────────────────

    private sealed class PlanEvaluation
    {
        public HRBulkMappingPreviewResult Preview { get; } = new();
        public List<HREmployee> Employees { get; set; } = new();
        public Dictionary<Guid, HRBulkMappingPolicy.Outcome> Outcomes { get; } = new();
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public bool Failed => ErrorCode != null;

        public PlanEvaluation Fail(string code, string message) { ErrorCode = code; ErrorMessage = message; return this; }
    }

    /// <summary>
    /// Evaluates a plan against the CURRENT database state. Used by preview (untracked) and by apply (tracked, inside the
    /// write transaction) so that both see exactly the same rule. Nothing is written here.
    /// </summary>
    private async Task<PlanEvaluation> EvaluateBulkMappingPlanAsync(HRBulkMappingPlanRequest plan, bool track)
    {
        var ev = new PlanEvaluation();
        var ids = (plan.EmployeeIds ?? new List<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return ev.Fail("NO_SELECTION", "Nenhum funcionário selecionado.");

        var mode = (plan.Mode ?? string.Empty).Trim().ToUpperInvariant();
        if (!HRBulkMappingPolicy.IsKnownMode(mode))
            return ev.Fail("INVALID_MODE", "Operação desconhecida.");

        var isAssign = mode == HRBulkMappingPolicy.ModeAssign;
        if (isAssign && !plan.PlantId.HasValue && !plan.DepartmentMasterId.HasValue && !plan.ManagerUserId.HasValue)
            return ev.Fail("NO_FIELDS", "Indique pelo menos um campo a atribuir (planta, departamento mestre ou responsável).");

        // ─── Reference data ───
        var plants = await _context.Plants.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.IsActive, CompanyCode = p.Company.Code })
            .ToListAsync();
        var plantById = plants.ToDictionary(p => p.Id);
        var plantIdsByCompany = plants
            .Where(p => p.IsActive && p.CompanyCode != null)
            .GroupBy(p => p.CompanyCode!)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(p => p.Id).OrderBy(id => id).ToList(), StringComparer.Ordinal);
        var currentScope = CurrentLookupScope();

        string? requestedPlantCompany = null, requestedPlantName = null;
        if (isAssign && plan.PlantId.HasValue)
        {
            if (!plantById.TryGetValue(plan.PlantId.Value, out var p) || !p.IsActive)
                return ev.Fail("PLANT_NOT_FOUND", $"Planta {plan.PlantId} não encontrada ou inativa.");
            requestedPlantCompany = p.CompanyCode;
            requestedPlantName = p.Name;
        }

        string? requestedMasterCompany = null, requestedMasterName = null;
        if (isAssign && plan.DepartmentMasterId.HasValue)
        {
            var m = await _context.DepartmentMasters.AsNoTracking()
                .Where(x => x.Id == plan.DepartmentMasterId.Value && x.IsActive)
                .Select(x => new { x.DepartmentName, x.CompanyCode })
                .FirstOrDefaultAsync();
            if (m == null)
                return ev.Fail("DEPARTMENT_MASTER_NOT_FOUND", $"Departamento mestre {plan.DepartmentMasterId} não encontrado ou inativo.");
            requestedMasterCompany = m.CompanyCode;
            requestedMasterName = $"{m.DepartmentName} ({m.CompanyCode})";
        }

        string? requestedManagerName = null;
        if (isAssign && plan.ManagerUserId.HasValue)
        {
            var u = await _context.Users.AsNoTracking()
                .Where(x => x.Id == plan.ManagerUserId.Value && x.IsActive)
                .Select(x => new { x.FullName })
                .FirstOrDefaultAsync();
            if (u == null)
                return ev.Fail("MANAGER_NOT_FOUND", "Responsável não encontrado ou inativo.");
            requestedManagerName = u.FullName;
        }

        // ─── Employees (tracked only for apply) and visibility scope ───
        IQueryable<HREmployee> source = track ? _context.HREmployees : _context.HREmployees.AsNoTracking();
        var employees = await source
            .Include(e => e.Plant).ThenInclude(p => p!.Company)
            .Include(e => e.DepartmentMaster)
            .Include(e => e.ManagerUser)
            .Where(e => ids.Contains(e.Id))
            .ToListAsync();
        ev.Employees = employees;
        var byId = employees.ToDictionary(e => e.Id);

        var scoped = await GetScopedEmployeesQuery();
        var visible = (await scoped.Where(e => ids.Contains(e.Id)).Select(e => e.Id).ToListAsync()).ToHashSet();

        var assignment = new HRBulkMappingPolicy.Assignment(
            plan.PlantId, requestedPlantCompany,
            plan.DepartmentMasterId, requestedMasterCompany,
            plan.ManagerUserId,
            plan.OverwritePlant, plan.OverwriteDepartment, plan.OverwriteManager);

        var preview = ev.Preview;
        preview.Mode = mode;
        preview.Selected = ids.Count;

        var ordered = ids
            .Select(id => byId.TryGetValue(id, out var e) ? e : null)
            .Where(e => e != null)
            .OrderBy(e => e!.FullName)
            .Select(e => e!.Id)
            .Concat(ids.Where(id => !byId.ContainsKey(id)))
            .ToList();

        foreach (var id in ordered)
        {
            var row = new HRBulkMappingPreviewRow { EmployeeId = id };

            if (!byId.TryGetValue(id, out var emp))
            {
                row.Status = HRBulkMappingPolicy.RowBlocked;
                row.ReasonCode = HRBulkMappingPolicy.ReasonEmployeeNotFound;
                row.Reason = "Funcionário não encontrado (removido ou sincronização alterou o registo).";
                row.FullName = id.ToString();
                preview.Rows.Add(row);
                continue;
            }

            row.EmployeeCode = emp.EmployeeCode;
            row.FullName = emp.FullName;
            row.InnuxDepartmentName = emp.InnuxDepartmentName;

            var plantCompany = emp.Plant?.Company?.Code;
            var current = new HRBulkMappingPolicy.CurrentState(
                emp.PlantId, plantCompany,
                emp.DepartmentMasterId, emp.DepartmentMaster?.CompanyCode,
                emp.ManagerUserId);

            row.Current = new HRBulkMappingFieldValues
            {
                PlantId = emp.PlantId,
                PlantName = emp.Plant?.Name,
                DepartmentMasterId = emp.DepartmentMasterId,
                DepartmentMasterName = emp.DepartmentMaster != null ? $"{emp.DepartmentMaster.DepartmentName} ({emp.DepartmentMaster.CompanyCode})" : null,
                ManagerUserId = emp.ManagerUserId,
                ManagerName = emp.ManagerUser?.FullName,
            };
            preview.Expected.Add(new HRBulkMappingExpectedState
            {
                EmployeeId = emp.Id, PlantId = emp.PlantId, DepartmentMasterId = emp.DepartmentMasterId, ManagerUserId = emp.ManagerUserId,
            });

            var entry = HRDirectoryMappingPolicy.BySuggestedPlantSource(emp.SuggestedPlantSource);
            var candidates = entry != null && plantIdsByCompany.TryGetValue(entry.CompanyCode, out var c) ? c : Array.Empty<int>();
            var suggestionState = HRDirectoryMappingPolicy.ComputeSuggestionState(
                emp.PlantId, emp.SuggestedPlantSource, emp.SuggestedPlantLookupComplete, emp.SuggestedPlantLookupScope, currentScope, candidates.Count);
            row.SuggestionState = suggestionState;
            row.SuggestedCompanyCode = entry?.CompanyCode;

            HRBulkMappingPolicy.Outcome outcome;
            if (!visible.Contains(emp.Id))
            {
                outcome = new HRBulkMappingPolicy.Outcome(HRBulkMappingPolicy.RowBlocked, emp.PlantId, emp.DepartmentMasterId, emp.ManagerUserId,
                    Array.Empty<string>(), Array.Empty<string>(), HRBulkMappingPolicy.ReasonEmployeeOutOfScope, "Funcionário fora do seu âmbito de dados.");
            }
            else if (!emp.IsActive)
            {
                outcome = new HRBulkMappingPolicy.Outcome(HRBulkMappingPolicy.RowBlocked, emp.PlantId, emp.DepartmentMasterId, emp.ManagerUserId,
                    Array.Empty<string>(), Array.Empty<string>(), HRBulkMappingPolicy.ReasonEmployeeInactive, "Funcionário inativo.");
            }
            else if (isAssign)
            {
                outcome = HRBulkMappingPolicy.EvaluateAssignment(current, assignment);
            }
            else
            {
                var candidateCompany = candidates.Count == 1 && plantById.TryGetValue(candidates[0], out var cp) ? cp.CompanyCode : null;
                outcome = HRBulkMappingPolicy.EvaluateSuggestionConfirmation(current, suggestionState, entry?.CompanyCode, candidates, candidateCompany);
            }

            ev.Outcomes[emp.Id] = outcome;
            row.Status = outcome.Status;
            row.ReasonCode = outcome.ReasonCode;
            row.Reason = outcome.Reason;
            row.ChangedFields = outcome.ChangedFields.ToList();
            row.PreservedFields = outcome.PreservedFields.ToList();
            row.Proposed = new HRBulkMappingFieldValues
            {
                PlantId = outcome.PlantId,
                PlantName = outcome.PlantId.HasValue
                    ? (outcome.PlantId == emp.PlantId ? emp.Plant?.Name : (plantById.TryGetValue(outcome.PlantId.Value, out var pp) ? pp.Name : requestedPlantName))
                    : null,
                DepartmentMasterId = outcome.DepartmentMasterId,
                DepartmentMasterName = outcome.DepartmentMasterId.HasValue
                    ? (outcome.DepartmentMasterId == emp.DepartmentMasterId ? row.Current.DepartmentMasterName : requestedMasterName)
                    : null,
                ManagerUserId = outcome.ManagerUserId,
                ManagerName = outcome.ManagerUserId.HasValue
                    ? (outcome.ManagerUserId == emp.ManagerUserId ? emp.ManagerUser?.FullName : requestedManagerName)
                    : null,
            };

            if (outcome.Status == HRBulkMappingPolicy.RowChange)
            {
                preview.WillChange++;
                if (outcome.ChangedFields.Contains(HRBulkMappingPolicy.FieldPlant)) preview.PlantChanges++;
                if (outcome.ChangedFields.Contains(HRBulkMappingPolicy.FieldDepartment)) preview.DepartmentChanges++;
                if (outcome.ChangedFields.Contains(HRBulkMappingPolicy.FieldManager)) preview.ManagerChanges++;
            }
            else if (outcome.Status == HRBulkMappingPolicy.RowUnchanged)
                preview.Unchanged++;

            preview.Rows.Add(row);
        }

        preview.Blocked = preview.Rows.Count(r => r.Status == HRBulkMappingPolicy.RowBlocked);

        // ─── Conflicts grouped by reason (and, for company mismatches, by the employee's current company) ───
        foreach (var group in preview.Rows
                     .Where(r => r.Status == HRBulkMappingPolicy.RowBlocked)
                     .GroupBy(r => r.ReasonCode + "|" + (r.ReasonCode is HRBulkMappingPolicy.ReasonCompanyMismatchExistingPlant
                         ? (byId.TryGetValue(r.EmployeeId, out var e1) ? e1.Plant?.Company?.Code : null)
                         : r.ReasonCode is HRBulkMappingPolicy.ReasonCompanyMismatchExistingDepartment
                             ? (byId.TryGetValue(r.EmployeeId, out var e2) ? e2.DepartmentMaster?.CompanyCode : null)
                             : null)))
        {
            var first = group.First();
            var code = first.ReasonCode ?? "BLOCKED";
            var message = code switch
            {
                HRBulkMappingPolicy.ReasonCompanyMismatchExistingPlant =>
                    $"{group.Count()} funcionário(s) têm planta da empresa {group.Key.Split('|')[1]}; o departamento escolhido pertence a {requestedMasterCompany}. Reduza a seleção a uma empresa ou atribua também a planta.",
                HRBulkMappingPolicy.ReasonCompanyMismatchExistingDepartment =>
                    $"{group.Count()} funcionário(s) têm departamento da empresa {group.Key.Split('|')[1]}; a planta escolhida pertence a {requestedPlantCompany}. Substitua também o departamento ou reduza a seleção.",
                HRBulkMappingPolicy.ReasonCompanyMismatchRequested =>
                    $"A planta ({requestedPlantCompany}) e o departamento ({requestedMasterCompany}) escolhidos pertencem a empresas diferentes.",
                HRBulkMappingPolicy.ReasonSuggestionNotActionable =>
                    $"{group.Count()} funcionário(s) não têm sugestão aplicável (escolha necessária, manual ou não verificada).",
                HRBulkMappingPolicy.ReasonEmployeeNotFound => $"{group.Count()} funcionário(s) já não existem.",
                HRBulkMappingPolicy.ReasonEmployeeOutOfScope => $"{group.Count()} funcionário(s) estão fora do seu âmbito de dados.",
                HRBulkMappingPolicy.ReasonEmployeeInactive => $"{group.Count()} funcionário(s) estão inativos.",
                _ => $"{group.Count()} funcionário(s) bloqueados.",
            };
            preview.Conflicts.Add(new HRBulkMappingConflict { Code = code, Message = message, EmployeeIds = group.Select(r => r.EmployeeId).ToList() });
        }

        preview.CanApply = preview.Blocked == 0 && preview.WillChange > 0;
        preview.Message = preview.Blocked > 0
            ? $"{preview.Blocked} funcionário(s) bloqueiam a gravação. Remova-os da seleção ou ajuste a operação."
            : preview.WillChange == 0
                ? "Nenhuma alteração a gravar: os valores escolhidos já estão preenchidos ou foram mantidos."
                : $"{preview.WillChange} funcionário(s) serão atualizados; {preview.Unchanged} mantêm-se.";

        return ev;
    }

    /// <summary>Preview: the exact employees and field changes the plan would produce. Nothing is written.</summary>
    [HttpPost("employees/bulk-mapping/preview")]
    public async Task<IActionResult> PreviewBulkMapping([FromBody] HRBulkMappingPlanRequest plan)
    {
        if (!IsAdminOrHR)
            return Forbid();

        var ev = await EvaluateBulkMappingPlanAsync(plan, track: false);
        if (ev.Failed)
            return BadRequest(new { errorCode = ev.ErrorCode, message = ev.ErrorMessage });

        return Ok(ev.Preview);
    }

    /// <summary>
    /// Apply: re-evaluates the plan on the current state inside one transaction, rejects the whole batch when any selected
    /// employee changed since the preview (409 STALE_SELECTION), is blocked (400 BULK_MAPPING_BLOCKED) or would end with an
    /// inconsistent plant/department pair; writes all rows in one SaveChanges otherwise.
    /// </summary>
    [HttpPost("employees/bulk-mapping/apply")]
    public async Task<IActionResult> ApplyBulkMapping([FromBody] HRBulkMappingApplyRequest request)
    {
        if (!IsAdminOrHR)
            return Forbid();

        await using var tx = await _context.Database.BeginTransactionAsync();

        var ev = await EvaluateBulkMappingPlanAsync(request, track: true);
        if (ev.Failed)
            return BadRequest(new { errorCode = ev.ErrorCode, message = ev.ErrorMessage });

        var preview = ev.Preview;

        // ─── Stale check: every found employee must still hold the values the client saw at preview time ───
        var expectedById = (request.Expected ?? new List<HRBulkMappingExpectedState>())
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());
        var stale = ev.Employees
            .Where(e => !expectedById.TryGetValue(e.Id, out var x)
                        || x.PlantId != e.PlantId
                        || x.DepartmentMasterId != e.DepartmentMasterId
                        || x.ManagerUserId != e.ManagerUserId)
            .OrderBy(e => e.FullName)
            .Select(e => new { id = e.Id, employeeCode = e.EmployeeCode, fullName = e.FullName })
            .ToList();
        if (stale.Count > 0)
        {
            return Conflict(new
            {
                errorCode = "STALE_SELECTION",
                message = $"{stale.Count} funcionário(s) foram alterados desde a pré-visualização. Nenhum registo foi gravado. Volte a pré-visualizar.",
                employees = stale,
            });
        }

        if (preview.Blocked > 0)
        {
            var blocked = preview.Rows
                .Where(r => r.Status == HRBulkMappingPolicy.RowBlocked)
                .Select(r => new { id = r.EmployeeId, employeeCode = r.EmployeeCode, fullName = r.FullName, reasonCode = r.ReasonCode, reason = r.Reason })
                .ToList();
            return BadRequest(new
            {
                errorCode = "BULK_MAPPING_BLOCKED",
                message = $"Gravação rejeitada: {blocked.Count} funcionário(s) com mapeamento inválido ou indisponível. Nenhum registo foi gravado.",
                employees = blocked,
            });
        }

        if (preview.WillChange == 0)
            return BadRequest(new { errorCode = "NO_CHANGES", message = "Nenhuma alteração a gravar." });

        foreach (var emp in ev.Employees)
        {
            if (!ev.Outcomes.TryGetValue(emp.Id, out var outcome) || outcome.Status != HRBulkMappingPolicy.RowChange)
                continue;

            emp.PlantId = outcome.PlantId;
            emp.DepartmentMasterId = outcome.DepartmentMasterId;
            emp.ManagerUserId = outcome.ManagerUserId;
            // Legacy flag semantics kept for existing callers (any of the four fields).
            emp.IsMapped = HRDirectoryMappingPolicy.ComputeLegacyIsMapped(emp.PlantId, emp.PortalDepartmentId, emp.DepartmentMasterId, emp.ManagerUserId);
        }

        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        _logger.LogInformation("HR bulk mapping applied by {User}: mode={Mode} selected={Selected} updated={Updated} plant={Plant} department={Department} manager={Manager}",
            CurrentUserId, preview.Mode, preview.Selected, preview.WillChange, preview.PlantChanges, preview.DepartmentChanges, preview.ManagerChanges);

        return Ok(new HRBulkMappingApplyResult
        {
            Updated = preview.WillChange,
            Unchanged = preview.Unchanged,
            Message = $"Mapeamento gravado: {preview.WillChange} funcionário(s) atualizados, {preview.Unchanged} sem alteração.",
        });
    }
}
