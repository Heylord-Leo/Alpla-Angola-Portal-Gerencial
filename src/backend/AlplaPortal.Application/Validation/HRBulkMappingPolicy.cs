namespace AlplaPortal.Application.Validation;

/// <summary>
/// Pure rules for the "Mapeamento em lote" screen (HR directory). No I/O.
///
/// - Fill-by-default: a planned value is written only where the field is empty; an existing, different
///   value is kept unless the overwrite flag for that field is set. Fields outside the plan are untouched.
/// - Company consistency of the FINAL (plant, department master) pair of each employee, with the same rule
///   as the single and legacy bulk saves (<see cref="HRDirectoryMappingPolicy.IsCompanyConsistent"/>).
///   A legacy mismatch that the plan does not touch is allowed (manager-only edits keep working).
/// - Suggestion confirmation reuses <see cref="HRDirectoryMappingPolicy.ComputeSuggestionState"/>: only an
///   ACTIONABLE suggestion with exactly one candidate plant is confirmed. A shared Innux department never
///   chooses between plants of the same company; that choice is always an explicit ASSIGN by HR.
/// </summary>
public static class HRBulkMappingPolicy
{
    public const string ModeAssign = "ASSIGN";
    public const string ModeConfirmSuggestions = "CONFIRM_SUGGESTIONS";

    public const string RowChange = "CHANGE";
    public const string RowUnchanged = "UNCHANGED";
    public const string RowBlocked = "BLOCKED";

    public const string FieldPlant = "PLANT";
    public const string FieldDepartment = "DEPARTMENT";
    public const string FieldManager = "MANAGER";

    public const string ReasonCompanyMismatchExistingPlant = "COMPANY_MISMATCH_EXISTING_PLANT";
    public const string ReasonCompanyMismatchExistingDepartment = "COMPANY_MISMATCH_EXISTING_DEPARTMENT";
    public const string ReasonCompanyMismatchRequested = "COMPANY_MISMATCH_REQUESTED";
    public const string ReasonSuggestionNotActionable = "SUGGESTION_NOT_ACTIONABLE";
    public const string ReasonPlantAlreadySet = "PLANT_ALREADY_SET";
    public const string ReasonNoChange = "NO_CHANGE";
    public const string ReasonEmployeeNotFound = "EMPLOYEE_NOT_FOUND";
    public const string ReasonEmployeeOutOfScope = "EMPLOYEE_OUT_OF_SCOPE";
    public const string ReasonEmployeeInactive = "EMPLOYEE_INACTIVE";

    public sealed record CurrentState(
        int? PlantId, string? PlantCompanyCode,
        int? DepartmentMasterId, string? DepartmentMasterCompanyCode,
        Guid? ManagerUserId);

    public sealed record Assignment(
        int? PlantId, string? PlantCompanyCode,
        int? DepartmentMasterId, string? DepartmentMasterCompanyCode,
        Guid? ManagerUserId,
        bool OverwritePlant, bool OverwriteDepartment, bool OverwriteManager)
    {
        public bool HasAnyField => PlantId.HasValue || DepartmentMasterId.HasValue || ManagerUserId.HasValue;
    }

    public sealed record Outcome(
        string Status,
        int? PlantId, int? DepartmentMasterId, Guid? ManagerUserId,
        IReadOnlyList<string> ChangedFields,
        IReadOnlyList<string> PreservedFields,
        string? ReasonCode, string? Reason);

    public static bool IsKnownMode(string? mode)
        => mode == ModeAssign || mode == ModeConfirmSuggestions;

    /// <summary>Mode ASSIGN for one employee.</summary>
    public static Outcome EvaluateAssignment(CurrentState current, Assignment plan)
    {
        var changed = new List<string>();
        var preserved = new List<string>();

        var plantId = current.PlantId;
        var plantCompany = current.PlantCompanyCode;
        var deptId = current.DepartmentMasterId;
        var deptCompany = current.DepartmentMasterCompanyCode;
        var managerId = current.ManagerUserId;

        if (plan.PlantId.HasValue)
        {
            if (!current.PlantId.HasValue || plan.OverwritePlant)
            {
                if (current.PlantId != plan.PlantId)
                {
                    plantId = plan.PlantId;
                    plantCompany = plan.PlantCompanyCode;
                    changed.Add(FieldPlant);
                }
            }
            else if (current.PlantId != plan.PlantId)
            {
                preserved.Add(FieldPlant);
            }
        }

        if (plan.DepartmentMasterId.HasValue)
        {
            if (!current.DepartmentMasterId.HasValue || plan.OverwriteDepartment)
            {
                if (current.DepartmentMasterId != plan.DepartmentMasterId)
                {
                    deptId = plan.DepartmentMasterId;
                    deptCompany = plan.DepartmentMasterCompanyCode;
                    changed.Add(FieldDepartment);
                }
            }
            else if (current.DepartmentMasterId != plan.DepartmentMasterId)
            {
                preserved.Add(FieldDepartment);
            }
        }

        if (plan.ManagerUserId.HasValue)
        {
            if (!current.ManagerUserId.HasValue || plan.OverwriteManager)
            {
                if (current.ManagerUserId != plan.ManagerUserId)
                {
                    managerId = plan.ManagerUserId;
                    changed.Add(FieldManager);
                }
            }
            else if (current.ManagerUserId != plan.ManagerUserId)
            {
                preserved.Add(FieldManager);
            }
        }

        // Final pair check. Only a pair that this plan produces or touches can block; a legacy mismatch
        // that the plan leaves alone (e.g. manager-only) is allowed, as in the single save.
        if (plantId.HasValue && deptId.HasValue && !HRDirectoryMappingPolicy.IsCompanyConsistent(plantCompany, deptCompany))
        {
            var touchesPlant = changed.Contains(FieldPlant);
            var touchesDept = changed.Contains(FieldDepartment);
            string? code = (touchesPlant, touchesDept) switch
            {
                (true, true) => ReasonCompanyMismatchRequested,
                (false, true) => ReasonCompanyMismatchExistingPlant,
                (true, false) => ReasonCompanyMismatchExistingDepartment,
                _ => null,
            };
            if (code != null)
            {
                var reason = code switch
                {
                    ReasonCompanyMismatchRequested => $"A planta e o departamento escolhidos pertencem a empresas diferentes ({plantCompany} vs {deptCompany}).",
                    ReasonCompanyMismatchExistingPlant => $"O departamento escolhido é da empresa {deptCompany}, mas a planta atual do funcionário é da empresa {plantCompany}. Reduza a seleção ou atribua também a planta.",
                    _ => $"A planta escolhida é da empresa {plantCompany}, mas o departamento atual do funcionário é da empresa {deptCompany}. Substitua também o departamento ou reduza a seleção.",
                };
                return new Outcome(RowBlocked, plantId, deptId, managerId, changed, preserved, code, reason);
            }
        }

        if (changed.Count == 0)
        {
            var why = preserved.Count > 0
                ? "Valores existentes mantidos (substituição não pedida)."
                : "Já tem os valores escolhidos.";
            return new Outcome(RowUnchanged, plantId, deptId, managerId, changed, preserved, ReasonNoChange, why);
        }

        return new Outcome(RowChange, plantId, deptId, managerId, changed, preserved, null, null);
    }

    /// <summary>Mode CONFIRM_SUGGESTIONS for one employee.</summary>
    public static Outcome EvaluateSuggestionConfirmation(
        CurrentState current,
        string suggestionState,
        string? suggestedCompanyCode,
        IReadOnlyList<int> candidatePlantIds,
        string? candidatePlantCompanyCode)
    {
        var none = Array.Empty<string>();

        if (current.PlantId.HasValue)
            return new Outcome(RowUnchanged, current.PlantId, current.DepartmentMasterId, current.ManagerUserId, none, none,
                ReasonPlantAlreadySet, "Já tem planta atribuída.");

        if (suggestionState == HRDirectoryMappingPolicy.SuggestionActionable && candidatePlantIds.Count == 1)
        {
            var plantId = candidatePlantIds[0];
            if (current.DepartmentMasterId.HasValue &&
                !HRDirectoryMappingPolicy.IsCompanyConsistent(candidatePlantCompanyCode, current.DepartmentMasterCompanyCode))
            {
                return new Outcome(RowBlocked, plantId, current.DepartmentMasterId, current.ManagerUserId, none, none,
                    ReasonCompanyMismatchExistingDepartment,
                    $"A planta sugerida é da empresa {candidatePlantCompanyCode}, mas o departamento atual é da empresa {current.DepartmentMasterCompanyCode}.");
            }
            return new Outcome(RowChange, plantId, current.DepartmentMasterId, current.ManagerUserId, new[] { FieldPlant }, none, null, null);
        }

        var reason = suggestionState switch
        {
            HRDirectoryMappingPolicy.SuggestionNeedsChoice =>
                $"A empresa identificada ({suggestedCompanyCode}) tem mais do que uma planta. Escolha a planta explicitamente com \"Atribuir equipa\".",
            HRDirectoryMappingPolicy.SuggestionManual =>
                "Funcionário encontrado nas duas empresas Primavera. Mapeamento manual.",
            HRDirectoryMappingPolicy.SuggestionUnverified =>
                "Sugestão não verificada (consulta anterior incompleta ou de outro âmbito). Execute \"Sincronizar\" ou mapeie manualmente.",
            _ => "Sem sugestão de planta aplicável.",
        };
        return new Outcome(RowBlocked, current.PlantId, current.DepartmentMasterId, current.ManagerUserId, none, none,
            ReasonSuggestionNotActionable, reason);
    }
}
