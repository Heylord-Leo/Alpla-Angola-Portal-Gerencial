namespace AlplaPortal.Application.DTOs.Integration;

// ─────────────────────────────────────────────────────────────────────────────
// Portal Attendance Engine — Phase 1 & 2 DTOs
//
// These DTOs support the Portal-side attendance interpretation layer.
// They are designed to be comparison-ready: each DTO carries enough metadata
// to later compare Portal interpretation against Innux processed results.
//
// Strictly read-only — no writes to Innux or Primavera.
// ─────────────────────────────────────────────────────────────────────────────

#region Phase 1 — Schedule Day Resolver

/// <summary>
/// Output of the Schedule Day Resolver: the fully resolved schedule
/// that applies to an employee on a specific date.
///
/// Computed from Funcionarios → PlanosTrabalho → PlanosTrabalhoHorarios → Horarios → HorariosPeriodos.
/// </summary>
public class ResolvedScheduleDto
{
    // ─── Work Plan ───

    /// <summary>Innux IDPlanoTrabalho.</summary>
    public int WorkPlanId { get; set; }

    /// <summary>Work plan code (PlanosTrabalho.Codigo).</summary>
    public string WorkPlanCode { get; set; } = "";

    /// <summary>Work plan description.</summary>
    public string WorkPlanDescription { get; set; } = "";

    /// <summary>Plan type: "Padrão", "Escala", "Automático", "Calendar".</summary>
    public string WorkPlanType { get; set; } = "";

    /// <summary>Total days in the rotation cycle.</summary>
    public int CycleDays { get; set; }

    /// <summary>Cycle reference start date from PlanosTrabalho.DataInicio.</summary>
    public DateTime? CycleStartDate { get; set; }

    /// <summary>Computed 0-based day index within the cycle for the target date.</summary>
    public int ResolvedDayIndex { get; set; }

    // ─── Schedule (Horario) ───

    /// <summary>Innux IDHorario resolved for this day.</summary>
    public int ScheduleId { get; set; }

    /// <summary>Schedule code (Horarios.Codigo).</summary>
    public string ScheduleCode { get; set; } = "";

    /// <summary>Schedule description (Horarios.Descricao).</summary>
    public string ScheduleDescription { get; set; } = "";

    /// <summary>Short label (Horarios.Sigla), e.g. "TN", "TM", "DC".</summary>
    public string? ScheduleSigla { get; set; }

    /// <summary>Whether this schedule represents a rest day (DiaFolga).</summary>
    public bool IsRestDay { get; set; }

    /// <summary>
    /// Whether the schedule crosses midnight into the next calendar day.
    /// Derived from HorariosPeriodos.Fim having a date component > 1900-01-01.
    /// Only true for real worked overnight shifts — rest days are excluded.
    /// </summary>
    public bool IsOvernightShift { get; set; }

    // ─── Expected Times ───

    /// <summary>Expected shift start time as "HH:mm" (earliest mandatory period start).</summary>
    public string? ExpectedStartTime { get; set; }

    /// <summary>Expected shift end time as "HH:mm" (latest mandatory period end, may be next-day for overnight).</summary>
    public string? ExpectedEndTime { get; set; }

    /// <summary>Total expected working minutes from mandatory periods.</summary>
    public int ExpectedMinutes { get; set; }

    // ─── Resolution Metadata ───

    /// <summary>
    /// How the schedule was resolved:
    /// - "PlanosTrabalhoHorarios" — standard cycle-day mapping (primary path)
    /// - "Alteracoes.IDHorario" — daily assignment fallback for Escala-type plans
    /// </summary>
    public string ScheduleResolutionSource { get; set; } = "PlanosTrabalhoHorarios";

    // ─── Periods ───

    /// <summary>All schedule periods (mandatory + optional) resolved for this day.</summary>
    public List<ResolvedPeriodDto> Periods { get; set; } = new();
}

/// <summary>
/// One time window within a resolved schedule.
/// Maps from dbo.HorariosPeriodos for the resolved Horario.
/// </summary>
public class ResolvedPeriodDto
{
    /// <summary>"Obrigatório" (Mandatory) or "Opcional" (Optional).</summary>
    public string Type { get; set; } = "";

    /// <summary>Period start time as "HH:mm".</summary>
    public string StartTime { get; set; } = "";

    /// <summary>Period end time as "HH:mm" (may be next-day for overnight shifts).</summary>
    public string EndTime { get; set; } = "";

    /// <summary>Duration of this period in minutes.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Entry tolerance in minutes.</summary>
    public int ToleranceEntryMinutes { get; set; }

    /// <summary>Exit tolerance in minutes.</summary>
    public int ToleranceExitMinutes { get; set; }

    /// <summary>Work code description, e.g. "Trabalho Basico".</summary>
    public string? WorkCodeDescription { get; set; }
}

#endregion

#region Phase 2 — Raw Punch Interpreter

/// <summary>
/// One raw terminal punch with Portal-side interpretation metadata.
///
/// Preserves the original raw data for audit while adding Portal interpretation.
/// Duplicate punches are kept in the list but flagged — never removed.
/// </summary>
public class InterpretedPunchDto
{
    /// <summary>Raw punch time from TerminaisMarcacoes.Hora.</summary>
    public DateTime PunchTime { get; set; }

    /// <summary>Formatted punch time as "HH:mm:ss".</summary>
    public string PunchTimeFormatted { get; set; } = "";

    /// <summary>Raw direction code from TipoProcessado (e.g. "EN", "SA", "17", "18", "", null).</summary>
    public string? RawDirection { get; set; }

    /// <summary>Portal-interpreted direction: "Entry", "Exit", or "Unknown".</summary>
    public string InterpretedDirection { get; set; } = "Unknown";

    /// <summary>Human-readable direction label in Portuguese: "Entrada", "Saída", "Sem direção", etc.</summary>
    public string DirectionLabel { get; set; } = "";

    /// <summary>Terminal/device name, null if not available.</summary>
    public string? TerminalName { get; set; }

    /// <summary>Terminal ID for duplicate detection.</summary>
    public int? TerminalId { get; set; }

    /// <summary>Whether the punch was auto-generated by the Innux engine.</summary>
    public bool IsAutoGenerated { get; set; }

    // ─── Interpretation Metadata ───

    /// <summary>
    /// Short rule identifier that was applied.
    /// Examples: "StandardEN", "StandardSA", "Code17Entry", "Code18Exit",
    ///           "InferredFirstEntry", "InferredLastExit", "UnknownCode".
    /// </summary>
    public string InterpretationRule { get; set; } = "";

    /// <summary>
    /// Human-readable explanation of why this interpretation was applied.
    /// Example: "Code 17 interpreted as Entry based on Portal alternate terminal code rule."
    /// </summary>
    public string InterpretationReason { get; set; } = "";

    /// <summary>Per-punch confidence: "High", "Medium", "Low".</summary>
    public string Confidence { get; set; } = "High";

    // ─── Duplicate Handling ───

    /// <summary>Whether this punch is considered a duplicate for calculation purposes.</summary>
    public bool IsDuplicateCandidate { get; set; }

    /// <summary>Whether this punch was excluded from worked-time calculations.</summary>
    public bool IgnoredForCalculation { get; set; }

    /// <summary>
    /// Explanation of why this punch was flagged as duplicate, null if not a duplicate.
    /// Example: "Punch within 2 minutes of previous punch on same terminal."
    /// </summary>
    public string? DuplicateReason { get; set; }
}

/// <summary>
/// A matched Entry/Exit punch pair for worked-time calculation.
/// Incomplete pairs (missing entry or exit) are still represented with null members.
/// </summary>
public class PunchPairDto
{
    /// <summary>The entry punch, null if missing entry.</summary>
    public InterpretedPunchDto? Entry { get; set; }

    /// <summary>The exit punch, null if missing exit.</summary>
    public InterpretedPunchDto? Exit { get; set; }

    /// <summary>Worked minutes for this pair. 0 if incomplete pair.</summary>
    public int WorkedMinutes { get; set; }

    /// <summary>Pair completeness: "Complete", "MissingEntry", "MissingExit".</summary>
    public string PairType { get; set; } = "Complete";
}

/// <summary>
/// Full output of the Portal Raw Punch Interpreter for one employee on one day.
/// Contains all raw punches (including duplicates), matched pairs, worked time,
/// confidence scoring, warnings, and applied interpretation rules.
/// </summary>
public class PunchInterpretationResultDto
{
    /// <summary>Innux IDFuncionario.</summary>
    public int InnuxEmployeeId { get; set; }

    /// <summary>Target date.</summary>
    public DateTime Date { get; set; }

    /// <summary>Resolved schedule for this employee/date, null if no plan assigned.</summary>
    public ResolvedScheduleDto? ResolvedSchedule { get; set; }

    // ─── Raw Punches (all preserved, including duplicates) ───

    /// <summary>
    /// All raw terminal punches for this employee/date, including flagged duplicates.
    /// Duplicates are NEVER removed — they are flagged with IsDuplicateCandidate and DuplicateReason.
    /// </summary>
    public List<InterpretedPunchDto> RawPunches { get; set; } = new();

    // ─── Interpretation Results ───

    /// <summary>Matched punch pairs used for worked-time calculation (excludes duplicates).</summary>
    public List<PunchPairDto> PunchPairs { get; set; } = new();

    /// <summary>Interpreted first entry time as "HH:mm", null if no entry found.</summary>
    public string? InterpretedFirstEntry { get; set; }

    /// <summary>Interpreted last exit time as "HH:mm", null if no exit found.</summary>
    public string? InterpretedLastExit { get; set; }

    /// <summary>Total worked minutes from all complete pairs.</summary>
    public int TotalWorkedMinutes { get; set; }

    // ─── Quality Metadata ───

    /// <summary>
    /// Overall confidence level for this interpretation.
    /// "High" — explicit EN/SA directions, all pairs complete.
    /// "Medium" — alternate codes (17/18) or inferred directions, coherent result.
    /// "Low" — missing pairs, unknown codes, or fallback overnight logic used.
    /// "None" — no punches found.
    /// </summary>
    public string ConfidenceLevel { get; set; } = "None";

    /// <summary>Human-readable warnings about the interpretation.</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>List of interpretation rule identifiers that were applied.</summary>
    public List<string> AppliedRules { get; set; } = new();
}

#endregion

#region Phase 3 — Comparison Engine DTOs

/// <summary>
/// Comparison result for one employee on one day.
/// Carries both the Innux processed result and the Portal raw-punch interpretation
/// side by side, with discrepancy analysis, severity, and a recommended review action.
///
/// Consumed by diagnostic endpoints only — not exposed to the production calendar UI.
/// </summary>
public class AttendanceComparisonResultDto
{
    // ─── Identity ───

    /// <summary>Innux IDFuncionario.</summary>
    public int InnuxEmployeeId { get; set; }

    /// <summary>Employee name, if resolvable.</summary>
    public string? EmployeeName { get; set; }

    /// <summary>Target date.</summary>
    public DateTime Date { get; set; }

    // ─── Innux Side ───

    /// <summary>
    /// Innux attendance status from AttendanceDaySummaryDto.AttendanceStatus.
    /// Values: "Present", "Absent", "JustifiedAbsence", "Vacation", "Holiday",
    ///         "DayOff", "Anomaly", "Unknown".
    /// </summary>
    public string InnuxStatus { get; set; } = "Unknown";

    /// <summary>First entry time as HH:mm from Innux, null if none.</summary>
    public string? InnuxFirstEntry { get; set; }

    /// <summary>Last exit time as HH:mm from Innux, null if none.</summary>
    public string? InnuxLastExit { get; set; }

    /// <summary>Total worked minutes from Innux (Basic + Overtime).</summary>
    public int InnuxWorkedMinutes { get; set; }

    /// <summary>
    /// Source of InnuxWorkedMinutes value:
    /// - "CalendarSummary" — from GetDailyAttendanceAsync (may be 0 if not merged)
    /// - "DayDetail" — enriched from GetWorkedHoursAsync (AlteracoesPeriodos)
    /// - "NotAvailable" — enrichment attempted but no detail found
    /// </summary>
    public string InnuxWorkedMinutesSource { get; set; } = "CalendarSummary";

    /// <summary>
    /// True if InnuxWorkedMinutes was enriched from AlteracoesPeriodos detail
    /// because the calendar summary returned 0 for a Present employee.
    /// </summary>
    public bool InnuxWorkedMinutesEnriched { get; set; }

    /// <summary>Expected working minutes from Innux (Alteracoes.Objectivo).</summary>
    public int InnuxExpectedMinutes { get; set; }

    // ─── Portal Side ───

    /// <summary>
    /// Portal-derived attendance status based on raw punch interpretation.
    /// Values: "Present", "NoPunches", "Incomplete", "DayOff",
    ///         "PresentOnRestDay", "Unknown".
    /// </summary>
    public string PortalStatus { get; set; } = "Unknown";

    /// <summary>First entry time as HH:mm from Portal interpretation, null if none.</summary>
    public string? PortalFirstEntry { get; set; }

    /// <summary>Last exit time as HH:mm from Portal interpretation, null if none.</summary>
    public string? PortalLastExit { get; set; }

    /// <summary>Total worked minutes from Portal punch pair calculation.</summary>
    public int PortalWorkedMinutes { get; set; }

    /// <summary>Expected working minutes from Portal schedule resolver.</summary>
    public int PortalExpectedMinutes { get; set; }

    // ─── Discrepancy Analysis ───

    /// <summary>Quick flag: true if any discrepancy exists.</summary>
    public bool HasDiscrepancy { get; set; }

    /// <summary>Discrepancy severity: "None", "Low", "Medium", "High".</summary>
    public string DiscrepancySeverity { get; set; } = "None";

    /// <summary>
    /// Machine-readable discrepancy type codes.
    /// Examples: "StatusConflict_AbsentVsPresent", "WorkedMinutesDrift_High",
    ///           "EntryTimeDrift", "IncompletePairs", "DuplicatesDetected".
    /// </summary>
    public List<string> DiscrepancyTypes { get; set; } = new();

    /// <summary>Human-readable discrepancy messages in Portuguese for HR users.</summary>
    public List<string> DiscrepancyMessages { get; set; } = new();

    /// <summary>
    /// Recommended review action in Portuguese.
    /// Examples:
    /// - "Sem divergência relevante."
    /// - "Revisar: o Innux indica ausência, mas o Portal encontrou picagens válidas."
    /// </summary>
    public string RecommendedReviewAction { get; set; } = "Sem divergência relevante.";

    // ─── Metadata ───

    /// <summary>Portal interpretation confidence level: "High", "Medium", "Low", "None".</summary>
    public string ConfidenceLevel { get; set; } = "None";

    /// <summary>Warnings from the Portal punch interpreter.</summary>
    public List<string> PortalWarnings { get; set; } = new();

    /// <summary>
    /// How the schedule was resolved:
    /// - "PlanosTrabalhoHorarios" — standard cycle-day mapping
    /// - "Alteracoes.IDHorario" — fallback for Escala-type plans
    /// - "Unavailable" — schedule could not be resolved
    /// </summary>
    public string ScheduleResolutionSource { get; set; } = "Unavailable";
}

/// <summary>
/// Batch comparison result for a date range, with summary statistics.
/// Used by the compare-range diagnostic endpoint.
/// </summary>
public class DateRangeComparisonResultDto
{
    /// <summary>Inclusive start date of the range.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Inclusive end date of the range.</summary>
    public DateTime EndDate { get; set; }

    /// <summary>Total employee-days evaluated.</summary>
    public int TotalEmployeeDays { get; set; }

    /// <summary>Number of employee-days with at least one discrepancy.</summary>
    public int DiscrepancyCount { get; set; }

    /// <summary>Number of HIGH severity discrepancies.</summary>
    public int HighSeverityCount { get; set; }

    /// <summary>Number of MEDIUM severity discrepancies.</summary>
    public int MediumSeverityCount { get; set; }

    /// <summary>Number of LOW severity discrepancies.</summary>
    public int LowSeverityCount { get; set; }

    /// <summary>Execution time in milliseconds.</summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>Individual comparison results.</summary>
    public List<AttendanceComparisonResultDto> Results { get; set; } = new();
}

#endregion
