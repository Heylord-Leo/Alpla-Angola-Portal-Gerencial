namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Immutable snapshot of an Innux Alteracoes row captured at sync time.
/// Preserves the exact source data used by the detection engine for audit traceability.
///
/// Source: Innux dbo.Alteracoes (processed daily attendance summary).
/// This table is append-only per processing run — never modified after creation.
///
/// Note: TerminaisMarcacoes (raw punches) are NOT persisted in V1.
/// Punch-level drill-down uses live Innux query via IInnuxAttendanceService.
/// </summary>
public class MCAttendanceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ─── Parent ───

    public Guid ProcessingRunId { get; set; }
    public MCProcessingRun? ProcessingRun { get; set; }

    /// <summary>Innux IDEntidade — denormalized from ProcessingRun for direct entity-scoped queries.</summary>
    public int EntityId { get; set; }

    // ─── Innux Source Identity ───

    /// <summary>Original Innux Alteracoes PK (IDAlteracao or composite key).</summary>
    public int InnuxAlteracaoId { get; set; }

    /// <summary>Innux IDFuncionario — internal technical PK.</summary>
    public int InnuxEmployeeId { get; set; }

    /// <summary>Employee code (Innux Numero) — maps to Primavera Codigo.</summary>
    public string EmployeeCode { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    // ─── Attendance Data ───

    public DateTime Date { get; set; }

    /// <summary>Innux schedule code assigned for this day.</summary>
    public string? ScheduleCode { get; set; }

    /// <summary>Innux schedule abbreviation (Sigla).</summary>
    public string? ScheduleSigla { get; set; }

    public bool IsRestDay { get; set; }

    /// <summary>First entry time as HH:mm string, null if no entry.</summary>
    public string? FirstEntry { get; set; }

    /// <summary>First exit time as HH:mm string, null if no exit.</summary>
    public string? FirstExit { get; set; }

    /// <summary>Schedule start time as HH:mm string — earliest mandatory period start. Used by detection engine for lateness calculation.</summary>
    public string? ScheduleStartTime { get; set; }

    /// <summary>Expected work minutes from the schedule definition.</summary>
    public int ExpectedMinutes { get; set; }

    /// <summary>Total absence minutes (Innux Falta field, converted).</summary>
    public int AbsenceMinutes { get; set; }

    /// <summary>Justified absence minutes (Innux Ausencia field, converted).</summary>
    public int JustifiedAbsenceMinutes { get; set; }

    /// <summary>Balance vs. expected (positive = overtime, negative = deficit).</summary>
    public int BalanceMinutes { get; set; }

    /// <summary>Total number of punch events for this day.</summary>
    public int PunchCount { get; set; }

    /// <summary>Whether Innux has validated/processed this day's record.</summary>
    public bool IsValidated { get; set; }

    /// <summary>Innux anomaly description if present.</summary>
    public string? AnomalyDescription { get; set; }

    /// <summary>Innux justification text if present.</summary>
    public string? Justification { get; set; }

    // ─── Sync Metadata ───

    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}
