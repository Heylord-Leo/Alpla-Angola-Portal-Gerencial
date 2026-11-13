namespace AlplaPortal.Application.DTOs.HR;

/// <summary>
/// Plan for the "Mapeamento em lote" screen. One plan = one selection + one operation.
/// Mode ASSIGN: any combination of plant / department master / responsible person; empty fields are
/// filled by default and existing values are only replaced when the matching Overwrite flag is true.
/// Mode CONFIRM_SUGGESTIONS: the plant suggestion of each selected employee is confirmed with the
/// server rule (HRDirectoryMappingPolicy: ACTIONABLE and exactly one candidate plant); no field inputs.
/// Fields that are not part of the plan are always preserved.
/// </summary>
public class HRBulkMappingPlanRequest
{
    public List<Guid> EmployeeIds { get; set; } = new();
    public string Mode { get; set; } = "ASSIGN";
    public int? PlantId { get; set; }
    public int? DepartmentMasterId { get; set; }
    public Guid? ManagerUserId { get; set; }
    public bool OverwritePlant { get; set; }
    public bool OverwriteDepartment { get; set; }
    public bool OverwriteManager { get; set; }
}

/// <summary>Values the client saw at preview time; the server rejects the batch when any of them changed.</summary>
public class HRBulkMappingExpectedState
{
    public Guid EmployeeId { get; set; }
    public int? PlantId { get; set; }
    public int? DepartmentMasterId { get; set; }
    public Guid? ManagerUserId { get; set; }
}

public class HRBulkMappingApplyRequest : HRBulkMappingPlanRequest
{
    public List<HRBulkMappingExpectedState> Expected { get; set; } = new();
}

public class HRBulkMappingFieldValues
{
    public int? PlantId { get; set; }
    public string? PlantName { get; set; }
    public int? DepartmentMasterId { get; set; }
    public string? DepartmentMasterName { get; set; }
    public Guid? ManagerUserId { get; set; }
    public string? ManagerName { get; set; }
}

public class HRBulkMappingPreviewRow
{
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? InnuxDepartmentName { get; set; }
    public string SuggestionState { get; set; } = "NONE";
    public string? SuggestedCompanyCode { get; set; }
    public HRBulkMappingFieldValues Current { get; set; } = new();
    public HRBulkMappingFieldValues Proposed { get; set; } = new();
    /// <summary>PLANT / DEPARTMENT / MANAGER that will change.</summary>
    public List<string> ChangedFields { get; set; } = new();
    /// <summary>Fields the plan wanted to set but that keep their existing value (overwrite not requested).</summary>
    public List<string> PreservedFields { get; set; } = new();
    /// <summary>CHANGE / UNCHANGED / BLOCKED.</summary>
    public string Status { get; set; } = "UNCHANGED";
    public string? ReasonCode { get; set; }
    public string? Reason { get; set; }
}

public class HRBulkMappingConflict
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<Guid> EmployeeIds { get; set; } = new();
    public int Count => EmployeeIds.Count;
}

public class HRBulkMappingPreviewResult
{
    public string Mode { get; set; } = "ASSIGN";
    public int Selected { get; set; }
    public int WillChange { get; set; }
    public int Unchanged { get; set; }
    public int Blocked { get; set; }
    public int PlantChanges { get; set; }
    public int DepartmentChanges { get; set; }
    public int ManagerChanges { get; set; }
    /// <summary>True only when nothing is blocked and at least one row changes.</summary>
    public bool CanApply { get; set; }
    public string? Message { get; set; }
    public List<HRBulkMappingPreviewRow> Rows { get; set; } = new();
    public List<HRBulkMappingConflict> Conflicts { get; set; } = new();
    /// <summary>Current values of every found employee; the client echoes them in the apply request.</summary>
    public List<HRBulkMappingExpectedState> Expected { get; set; } = new();
}

public class HRBulkMappingApplyResult
{
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public string Message { get; set; } = string.Empty;
}
