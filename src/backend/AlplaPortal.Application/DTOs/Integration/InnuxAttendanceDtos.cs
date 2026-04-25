namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// Daily attendance summary for one employee on one day — the calendar cell.
///
/// Maps from Innux dbo.Alteracoes with schedule enrichment via dbo.Horarios.
/// All time values are pre-converted from Innux datetime-as-duration encoding
/// to practical display/service forms (minutes or HH:mm strings).
///
/// AttendanceStatus is a Portal-computed classification derived from
/// the combination of Innux fields, not a raw Innux column.
/// </summary>
public class AttendanceDaySummaryDto
{
    /// <summary>Innux IDFuncionario — join key, never displayed.</summary>
    public int InnuxEmployeeId { get; set; }

    /// <summary>Calendar date.</summary>
    public DateTime Date { get; set; }

    /// <summary>Schedule code (Horarios.Codigo), null if no schedule assigned.</summary>
    public string? ScheduleCode { get; set; }

    /// <summary>Schedule description (Horarios.Descricao).</summary>
    public string? ScheduleDescription { get; set; }

    /// <summary>Schedule short label (Horarios.Sigla), e.g. "TN", "TM", "DC".</summary>
    public string? ScheduleSigla { get; set; }

    /// <summary>Whether the applied schedule is a rest day (Horarios.DiaFolga).</summary>
    public bool IsRestDay { get; set; }

    /// <summary>
    /// Whether the applied schedule crosses midnight into the next day.
    /// Only true for real worked overnight shifts — rest days, vacation, and
    /// optional-only periods are excluded even if they technically span midnight.
    /// Derived from HorariosPeriodos.Fim date component crossing 1900-01-02.
    /// </summary>
    public bool IsOvernightShift { get; set; }

    /// <summary>Schedule period start time as "HH:mm" (earliest mandatory period).</summary>
    public string? ScheduleStartTime { get; set; }

    /// <summary>Schedule period end time as "HH:mm" (latest period end, may be next-day).</summary>
    public string? ScheduleEndTime { get; set; }

    /// <summary>First entry time as "HH:mm", null if no entry recorded.</summary>
    public string? FirstEntry { get; set; }

    /// <summary>First exit time as "HH:mm", null if no exit recorded.</summary>
    public string? FirstExit { get; set; }

    /// <summary>Unjustified absence duration in minutes (Falta → DATEDIFF).</summary>
    public int AbsenceMinutes { get; set; }

    /// <summary>Justified absence duration in minutes (Ausencia → DATEDIFF).</summary>
    public int JustifiedAbsenceMinutes { get; set; }

    /// <summary>Expected working duration in minutes (Objectivo → DATEDIFF).</summary>
    public int ExpectedMinutes { get; set; }

    /// <summary>
    /// Balance in minutes (Saldo → DATEDIFF).
    /// Known limitation: negative balance encoding in Innux is unconfirmed.
    /// Values may appear as 0 when the true balance is negative.
    /// This field should be consumed defensively until validated.
    /// </summary>
    public int BalanceMinutes { get; set; }

    /// <summary>Number of clock punches recorded — official processed Innux value (Alteracoes.Marcacao).</summary>
    public int PunchCount { get; set; }

    /// <summary>
    /// Live count of raw terminal punches from TerminaisMarcacoes.
    /// May differ from PunchCount (processed Alteracoes.Marcacao) if punches were
    /// purged after processing, manually validated, or imported from external systems.
    /// Only populated in detail/drill-down queries, defaults to 0 in calendar grid.
    /// </summary>
    public int RawPunchCount { get; set; }

    /// <summary>Whether the day has been validated/approved in Innux.</summary>
    public bool IsValidated { get; set; }

    /// <summary>Whether mandatory schedule periods were missed.</summary>
    public bool MissedMandatoryPeriods { get; set; }

    /// <summary>Anomaly description from Innux, null if clean.</summary>
    public string? AnomalyDescription { get; set; }

    /// <summary>Justification text, null if none provided.</summary>
    public string? Justification { get; set; }

    /// <summary>
    /// Basic (normal) worked minutes for this day, aggregated from AlteracoesPeriodos
    /// where CodigosTrabalho.Tipo = 'Normal'. 0 if no periods or all dispensed.
    /// </summary>
    public int BasicWorkedMinutes { get; set; }

    /// <summary>
    /// Overtime worked minutes for this day, aggregated from AlteracoesPeriodos
    /// where CodigosTrabalho.Tipo = 'Extra' (includes Extra, Extra 2, etc.). 0 if none.
    /// </summary>
    public int OvertimeMinutes { get; set; }

    /// <summary>
    /// Total worked minutes = BasicWorkedMinutes + OvertimeMinutes.
    /// </summary>
    public int TotalWorkedMinutes { get; set; }

    /// <summary>
    /// Portal-computed attendance classification.
    /// Values: "Present", "Absent", "JustifiedAbsence", "Vacation", "Holiday",
    ///         "DayOff", "Anomaly", "Unknown".
    ///
    /// "Vacation" — Justified absence where Justificacao contains "Gozo de Férias".
    /// "Holiday"  — Day with Justificacao containing "Feriado" (may or may not have expected time).
    ///
    /// Derived from AbsenceMinutes, PunchCount, ExpectedMinutes, AnomalyDescription,
    /// and Justification text.
    /// </summary>
    public string AttendanceStatus { get; set; } = "Unknown";
}

/// <summary>
/// Drill-down detail for one employee on one day.
/// Combines the processed summary (Alteracoes) with raw punches and period breakdown.
/// </summary>
public class AttendanceDayDetailDto
{
    public AttendanceDaySummaryDto Summary { get; set; } = null!;
    public List<AttendancePunchDto> RawPunches { get; set; } = new();
    public List<AttendancePeriodDto> Periods { get; set; } = new();
    public string? WorkPlanCode { get; set; }
    public string? WorkPlanDescription { get; set; }

    // ─── Debug metadata for HR/IT transparency ───

    /// <summary>Official processed punch count from Alteracoes.Marcacao.</summary>
    public int DebugProcessedPunchCount { get; set; }

    /// <summary>Live count of rows in TerminaisMarcacoes for this employee/date.</summary>
    public int DebugRawTerminalPunchCount { get; set; }

    /// <summary>Whether the day was validated/approved in Innux (Alteracoes.Validado).</summary>
    public bool DebugIsValidated { get; set; }

    /// <summary>Schedule code from Horarios.Codigo, used by Innux for this day.</summary>
    public string? DebugScheduleCode { get; set; }

    /// <summary>Schedule description from Horarios.Descricao.</summary>
    public string? DebugScheduleDescription { get; set; }

    /// <summary>Human-readable description of the data source used for status classification.</summary>
    public string? DebugStatusSource { get; set; }
}

/// <summary>
/// One raw clock punch from TerminaisMarcacoes.
/// Used for audit/drill-down only, not for calendar rendering.
/// </summary>
public class AttendancePunchDto
{
    public int InnuxEmployeeId { get; set; }
    public DateTime Date { get; set; }

    /// <summary>Punch time as "HH:mm:ss".</summary>
    public string Time { get; set; } = "";

    /// <summary>Raw Innux direction: "EN" (entry) or "SA" (exit).</summary>
    public string Direction { get; set; } = "";

    /// <summary>Human-readable direction: "Entry" or "Exit".</summary>
    public string DirectionLabel { get; set; } = "";

    /// <summary>Terminal/device name, null if not available.</summary>
    public string? TerminalName { get; set; }

    /// <summary>Whether the punch was auto-generated by the Innux engine.</summary>
    public bool IsAutoGenerated { get; set; }
}

/// <summary>
/// One work period within a day from AlteracoesPeriodos.
/// Shows what happened in each scheduled time slot.
/// </summary>
public class AttendancePeriodDto
{
    /// <summary>Period start time as "HH:mm".</summary>
    public string? StartTime { get; set; }

    /// <summary>Period end time as "HH:mm".</summary>
    public string? EndTime { get; set; }

    /// <summary>Work code if the employee was working.</summary>
    public string? WorkCode { get; set; }

    /// <summary>Work code description.</summary>
    public string? WorkDescription { get; set; }

    /// <summary>Absence code if the employee was absent.</summary>
    public string? AbsenceCode { get; set; }

    /// <summary>Absence code description.</summary>
    public string? AbsenceDescription { get; set; }

    /// <summary>Whether the period was dispensed/excused.</summary>
    public bool IsDispensed { get; set; }
}

/// <summary>
/// Aggregated worked hours for one employee on one day.
/// Computed from AlteracoesPeriodos grouped by CodigosTrabalho type.
/// Used to merge into AttendanceDaySummaryDto during calendar assembly.
/// </summary>
public class WorkedHoursDto
{
    /// <summary>Normal/basic worked minutes (CodigosTrabalho.Tipo = 'Normal').</summary>
    public int BasicMinutes { get; set; }

    /// <summary>Overtime worked minutes (CodigosTrabalho.Tipo contains 'Extra').</summary>
    public int OvertimeMinutes { get; set; }

    /// <summary>Total = BasicMinutes + OvertimeMinutes.</summary>
    public int TotalMinutes => BasicMinutes + OvertimeMinutes;
}
