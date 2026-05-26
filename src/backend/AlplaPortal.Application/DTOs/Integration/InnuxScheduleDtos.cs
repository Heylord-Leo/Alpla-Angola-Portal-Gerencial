namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// Work plan summary for the consultation list view.
/// Maps from Innux dbo.PlanosTrabalho with employee count subquery.
/// </summary>
public class WorkPlanListItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Plan type: "Padrão", "Escala", "Automático", "Calendar".</summary>
    public string Type { get; set; } = "";

    /// <summary>Number of days in the cycle (7 for weekly, 0 for dynamic escala).</summary>
    public int CycleDays { get; set; }

    /// <summary>Default rest-day schedule description, null if not configured.</summary>
    public string? RestScheduleDescription { get; set; }

    /// <summary>Rest-day schedule sigla (e.g. "DC", "FG").</summary>
    public string? RestScheduleSigla { get; set; }

    /// <summary>Innux IDEntidade — 1 = AlplaPLASTICO, 6 = AlplaSOPRO.</summary>
    public int EntityId { get; set; }

    /// <summary>Entity/plant display name from Innux Entidades.</summary>
    public string? EntityName { get; set; }

    /// <summary>Number of active employees assigned to this plan.</summary>
    public int EmployeeCount { get; set; }

    /// <summary>Cycle-day composition — populated in detail view, null in list view.</summary>
    public List<PlanCycleDayDto>? CycleDays_Detail { get; set; }
}

/// <summary>
/// One day within a work plan's cycle, showing which schedule is assigned.
/// Maps from dbo.PlanosTrabalhoHorarios JOIN dbo.Horarios.
/// </summary>
public class PlanCycleDayDto
{
    /// <summary>0-based day index within the cycle.</summary>
    public int DayIndex { get; set; }

    /// <summary>Schedule ID assigned to this day.</summary>
    public int ScheduleId { get; set; }

    /// <summary>Schedule code (Horarios.Codigo).</summary>
    public string ScheduleCode { get; set; } = "";

    /// <summary>Schedule description.</summary>
    public string ScheduleDescription { get; set; } = "";

    /// <summary>Schedule short label (Sigla), e.g. "TM", "DC".</summary>
    public string? ScheduleSigla { get; set; }

    /// <summary>Whether this day's schedule is a rest day.</summary>
    public bool IsRestDay { get; set; }

    /// <summary>Normal working hours for this schedule (e.g. "8h", "12h").</summary>
    public string? NormalHours { get; set; }
}

/// <summary>
/// Schedule definition for the consultation list view.
/// Maps from Innux dbo.Horarios with period enrichment from dbo.HorariosPeriodos.
/// </summary>
public class ScheduleListItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Short label (Sigla), e.g. "TM", "TN", "DC", "SX".</summary>
    public string? Sigla { get; set; }

    /// <summary>Normal expected hours as formatted string, e.g. "08:00".</summary>
    public string? NormalHours { get; set; }

    /// <summary>Whether this schedule represents a rest day (DiaFolga).</summary>
    public bool IsRestDay { get; set; }

    /// <summary>Whether this is flagged as HorarioComoFolga.</summary>
    public bool IsScheduleAsRestDay { get; set; }

    /// <summary>Expected number of clock punches.</summary>
    public int ExpectedPunches { get; set; }

    /// <summary>Minimum number of punches expected.</summary>
    public int MinPunches { get; set; }

    /// <summary>Maximum daily tolerance in minutes.</summary>
    public int? MaxDailyToleranceMinutes { get; set; }

    /// <summary>Innux IDEntidade — 1 = AlplaPLASTICO, 6 = AlplaSOPRO.</summary>
    public int EntityId { get; set; }

    /// <summary>Entity/plant display name.</summary>
    public string? EntityName { get; set; }

    /// <summary>Mandatory/optional work periods within this schedule.</summary>
    public List<SchedulePeriodDto> Periods { get; set; } = new();
}

/// <summary>
/// One time period within a schedule definition.
/// Maps from dbo.HorariosPeriodos.
/// </summary>
public class SchedulePeriodDto
{
    /// <summary>"Obrigatório" or "Opcional".</summary>
    public string Type { get; set; } = "";

    /// <summary>Period start time as "HH:mm".</summary>
    public string StartTime { get; set; } = "";

    /// <summary>Period end time as "HH:mm".</summary>
    public string EndTime { get; set; } = "";

    /// <summary>Entry tolerance in minutes.</summary>
    public int ToleranceEntryMinutes { get; set; }

    /// <summary>Exit tolerance in minutes.</summary>
    public int ToleranceExitMinutes { get; set; }

    /// <summary>Rounding interval in minutes.</summary>
    public int RoundingMinutes { get; set; }

    /// <summary>Work code description, e.g. "Trabalho Basico".</summary>
    public string? WorkCodeDescription { get; set; }
}

/// <summary>
/// Employee linked to a work plan — for the plan detail drill-down.
/// Respects Portal ACL scoping at the controller layer.
/// </summary>
public class PlanEmployeeDto
{
    public int InnuxEmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string? DepartmentName { get; set; }
    public string? PlantName { get; set; }
}
