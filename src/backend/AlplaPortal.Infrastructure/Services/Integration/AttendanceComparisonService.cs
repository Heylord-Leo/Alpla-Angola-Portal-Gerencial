using System.Diagnostics;
using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Attendance Comparison Engine — Phase 3 (diagnostic, read-only).
///
/// Orchestrates existing services to compare Innux processed attendance
/// against Portal raw-punch interpretation. No new SQL queries — reuses:
///   - IInnuxAttendanceService.GetDailyAttendanceAsync()  → Innux side
///   - IPortalPunchInterpreter.InterpretPunchesAsync()    → Portal side
///   - IPortalScheduleResolver.ResolveScheduleForDateAsync() → schedule context
///
/// Discrepancy rules, severity assignment, and Portuguese recommendation
/// messages are computed deterministically from both outputs.
///
/// Read-only: no writes to Innux, Primavera, or Portal DB.
/// Schedule fallback (Alteracoes.IDHorario) is context only, NOT proof of attendance.
/// </summary>
public class AttendanceComparisonService : IAttendanceComparisonService
{
    private readonly IInnuxAttendanceService _innuxAttendanceService;
    private readonly IPortalPunchInterpreter _punchInterpreter;
    private readonly IPortalScheduleResolver _scheduleResolver;
    private readonly ILogger<AttendanceComparisonService> _logger;

    public AttendanceComparisonService(
        IInnuxAttendanceService innuxAttendanceService,
        IPortalPunchInterpreter punchInterpreter,
        IPortalScheduleResolver scheduleResolver,
        ILogger<AttendanceComparisonService> logger)
    {
        _innuxAttendanceService = innuxAttendanceService;
        _punchInterpreter = punchInterpreter;
        _scheduleResolver = scheduleResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AttendanceComparisonResultDto> CompareEmployeeDayAsync(
        int innuxEmployeeId, DateTime date)
    {
        var result = new AttendanceComparisonResultDto
        {
            InnuxEmployeeId = innuxEmployeeId,
            Date = date.Date
        };

        // ── 1. Innux side: get processed attendance ──
        AttendanceDaySummaryDto? innuxSummary = null;
        try
        {
            var innuxResults = await _innuxAttendanceService.GetDailyAttendanceAsync(
                new[] { innuxEmployeeId }, date.Date, date.Date);
            innuxSummary = innuxResults.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ComparisonEngine: Failed to retrieve Innux attendance for Employee {Id} on {Date:yyyy-MM-dd}",
                innuxEmployeeId, date);
        }

        if (innuxSummary != null)
        {
            result.InnuxStatus = innuxSummary.AttendanceStatus;
            result.InnuxFirstEntry = innuxSummary.FirstEntry;
            result.InnuxLastExit = innuxSummary.FirstExit; // Innux DTO uses FirstExit for last exit
            result.InnuxWorkedMinutes = innuxSummary.TotalWorkedMinutes;
            result.InnuxExpectedMinutes = innuxSummary.ExpectedMinutes;
        }

        // ── 1b. Innux worked-minutes enrichment ──
        // GetDailyAttendanceAsync (calendar grid) does not merge AlteracoesPeriodos,
        // so TotalWorkedMinutes is always 0. When Innux indicates presence, enrich
        // from GetWorkedHoursAsync to avoid false Medium discrepancies.
        var innuxPresenceStatuses = new HashSet<string>
        {
            "Present", "PortalInterpreted", "Anomaly"
        };
        if (innuxSummary != null &&
            result.InnuxWorkedMinutes == 0 &&
            innuxPresenceStatuses.Contains(result.InnuxStatus))
        {
            try
            {
                var workedHours = await _innuxAttendanceService.GetWorkedHoursAsync(
                    new[] { innuxEmployeeId }, date.Date, date.Date);

                if (workedHours.TryGetValue((innuxEmployeeId, date.Date), out var wh))
                {
                    result.InnuxWorkedMinutes = wh.TotalMinutes;
                    result.InnuxWorkedMinutesSource = "DayDetail";
                    result.InnuxWorkedMinutesEnriched = true;
                    _logger.LogDebug(
                        "ComparisonEngine: Enriched InnuxWorkedMinutes for Employee {Id} on {Date:yyyy-MM-dd}: {Minutes}min (Basic={Basic}, Overtime={OT})",
                        innuxEmployeeId, date, wh.TotalMinutes, wh.BasicMinutes, wh.OvertimeMinutes);
                }
                else
                {
                    result.InnuxWorkedMinutesSource = "NotAvailable";
                    _logger.LogDebug(
                        "ComparisonEngine: No AlteracoesPeriodos detail found for Employee {Id} on {Date:yyyy-MM-dd}",
                        innuxEmployeeId, date);
                }
            }
            catch (Exception ex)
            {
                result.InnuxWorkedMinutesSource = "NotAvailable";
                _logger.LogWarning(ex,
                    "ComparisonEngine: Failed to enrich worked minutes for Employee {Id} on {Date:yyyy-MM-dd}",
                    innuxEmployeeId, date);
            }
        }

        // ── 2. Portal side: interpret raw punches ──
        PunchInterpretationResultDto? portalResult = null;
        try
        {
            portalResult = await _punchInterpreter.InterpretPunchesAsync(innuxEmployeeId, date.Date);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ComparisonEngine: Failed to interpret Portal punches for Employee {Id} on {Date:yyyy-MM-dd}",
                innuxEmployeeId, date);
        }

        // ── 3. Schedule context ──
        ResolvedScheduleDto? schedule = null;
        try
        {
            schedule = await _scheduleResolver.ResolveScheduleForDateAsync(innuxEmployeeId, date.Date);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ComparisonEngine: Failed to resolve schedule for Employee {Id} on {Date:yyyy-MM-dd}",
                innuxEmployeeId, date);
        }

        if (schedule != null)
        {
            result.PortalExpectedMinutes = schedule.ExpectedMinutes;
            result.ScheduleResolutionSource = schedule.ScheduleResolutionSource;
        }

        // ── 4. Derive Portal status ──
        result.PortalStatus = DerivePortalStatus(portalResult, schedule);
        if (portalResult != null)
        {
            result.PortalWorkedMinutes = portalResult.TotalWorkedMinutes;
            result.ConfidenceLevel = portalResult.ConfidenceLevel;
            result.PortalWarnings = portalResult.Warnings;

            // Extract first entry and last exit from punch pairs
            var entryPunch = portalResult.PunchPairs
                .Where(p => p.Entry != null)
                .Select(p => p.Entry)
                .FirstOrDefault();
            var exitPunch = portalResult.PunchPairs
                .Where(p => p.Exit != null)
                .Select(p => p.Exit)
                .LastOrDefault();

            result.PortalFirstEntry = entryPunch?.PunchTimeFormatted;
            result.PortalLastExit = exitPunch?.PunchTimeFormatted;
        }

        // ── 5. Run discrepancy rules ──
        EvaluateDiscrepancies(result, portalResult);

        // ── 6. Diagnostic logging ──
        _logger.LogDebug(
            "ComparisonEngine: Employee {Id}, Date {Date:yyyy-MM-dd} — " +
            "Innux={InnuxStatus}, Portal={PortalStatus}, Severity={Severity}, " +
            "DiscrepancyCount={Count}, Schedule={Source}, Confidence={Confidence}",
            innuxEmployeeId, date,
            result.InnuxStatus, result.PortalStatus, result.DiscrepancySeverity,
            result.DiscrepancyTypes.Count, result.ScheduleResolutionSource,
            result.ConfidenceLevel);

        return result;
    }

    /// <inheritdoc />
    public async Task<DateRangeComparisonResultDto> CompareDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        int? innuxEmployeeId = null,
        int? departmentId = null,
        bool onlyDiscrepancies = false)
    {
        var sw = Stopwatch.StartNew();

        if ((endDate - startDate).TotalDays > 31)
            throw new ArgumentException("O intervalo de datas não pode exceder 31 dias.");

        // Employee list resolution is handled by the controller layer.
        // If innuxEmployeeId is specified, we process only that employee.
        // If departmentId is specified, the controller should resolve employee IDs before calling.
        // This method processes a single employee across the range when innuxEmployeeId is provided.
        var results = new List<AttendanceComparisonResultDto>();
        int totalDays = 0;

        if (innuxEmployeeId.HasValue)
        {
            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                totalDays++;
                var comparison = await CompareEmployeeDayAsync(innuxEmployeeId.Value, day);

                if (!onlyDiscrepancies || comparison.HasDiscrepancy)
                    results.Add(comparison);
            }
        }

        sw.Stop();

        var output = new DateRangeComparisonResultDto
        {
            StartDate = startDate.Date,
            EndDate = endDate.Date,
            TotalEmployeeDays = totalDays,
            DiscrepancyCount = results.Count(r => r.HasDiscrepancy),
            HighSeverityCount = results.Count(r => r.DiscrepancySeverity == "High"),
            MediumSeverityCount = results.Count(r => r.DiscrepancySeverity == "Medium"),
            LowSeverityCount = results.Count(r => r.DiscrepancySeverity == "Low"),
            ExecutionTimeMs = sw.ElapsedMilliseconds,
            Results = results
        };

        _logger.LogInformation(
            "ComparisonEngine: Range {Start:yyyy-MM-dd}→{End:yyyy-MM-dd}, " +
            "EmployeeDays={Total}, Discrepancies={Disc} (H={H},M={M},L={L}), " +
            "ExecutionTime={Ms}ms",
            startDate, endDate, output.TotalEmployeeDays, output.DiscrepancyCount,
            output.HighSeverityCount, output.MediumSeverityCount, output.LowSeverityCount,
            output.ExecutionTimeMs);

        return output;
    }

    /// <summary>
    /// Processes a batch of innux employee IDs across a date range.
    /// Called by the controller when departmentId filtering resolves multiple employees.
    /// </summary>
    public async Task<DateRangeComparisonResultDto> CompareDateRangeBatchAsync(
        IEnumerable<int> innuxEmployeeIds,
        DateTime startDate,
        DateTime endDate,
        bool onlyDiscrepancies = false)
    {
        var sw = Stopwatch.StartNew();
        var idList = innuxEmployeeIds.ToList();
        var results = new List<AttendanceComparisonResultDto>();
        int totalDays = 0;

        foreach (var empId in idList)
        {
            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                totalDays++;
                var comparison = await CompareEmployeeDayAsync(empId, day);

                if (!onlyDiscrepancies || comparison.HasDiscrepancy)
                    results.Add(comparison);
            }
        }

        sw.Stop();

        var output = new DateRangeComparisonResultDto
        {
            StartDate = startDate.Date,
            EndDate = endDate.Date,
            TotalEmployeeDays = totalDays,
            DiscrepancyCount = results.Count(r => r.HasDiscrepancy),
            HighSeverityCount = results.Count(r => r.DiscrepancySeverity == "High"),
            MediumSeverityCount = results.Count(r => r.DiscrepancySeverity == "Medium"),
            LowSeverityCount = results.Count(r => r.DiscrepancySeverity == "Low"),
            ExecutionTimeMs = sw.ElapsedMilliseconds,
            Results = results
        };

        _logger.LogInformation(
            "ComparisonEngine: BatchRange {Start:yyyy-MM-dd}→{End:yyyy-MM-dd}, " +
            "Employees={EmpCount}, EmployeeDays={Total}, Discrepancies={Disc} (H={H},M={M},L={L}), " +
            "ExecutionTime={Ms}ms",
            startDate, endDate, idList.Count, output.TotalEmployeeDays, output.DiscrepancyCount,
            output.HighSeverityCount, output.MediumSeverityCount, output.LowSeverityCount,
            output.ExecutionTimeMs);

        return output;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Portal Status Derivation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Derives the Portal attendance status from the punch interpretation result
    /// and the resolved schedule.
    ///
    /// Status values:
    ///   - "NoPunches"         — no raw punches found
    ///   - "Present"           — at least one complete pair with worked > 0
    ///   - "Incomplete"        — punches exist but no complete pair
    ///   - "DayOff"            — schedule is rest day and no punches
    ///   - "PresentOnRestDay"  — schedule is rest day but punches exist
    ///   - "Unknown"           — fallback
    /// </summary>
    private static string DerivePortalStatus(
        PunchInterpretationResultDto? portalResult,
        ResolvedScheduleDto? schedule)
    {
        bool isRestDay = schedule?.IsRestDay ?? false;
        bool hasPunches = portalResult?.RawPunches?.Any() ?? false;
        bool hasCompletePairs = portalResult?.PunchPairs?.Any(p =>
            p.PairType == "Complete" && p.WorkedMinutes > 0) ?? false;

        if (!hasPunches)
        {
            return isRestDay ? "DayOff" : "NoPunches";
        }

        if (isRestDay)
        {
            return "PresentOnRestDay";
        }

        if (hasCompletePairs)
        {
            return "Present";
        }

        return "Incomplete";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Discrepancy Evaluation Rules
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies explicit discrepancy rules comparing Innux vs Portal results.
    /// Rules are evaluated from highest to lowest severity.
    /// Multiple rules can fire simultaneously; the overall severity is the max.
    /// </summary>
    private static void EvaluateDiscrepancies(
        AttendanceComparisonResultDto result,
        PunchInterpretationResultDto? portalResult)
    {
        var types = new List<string>();
        var messages = new List<string>();
        int maxSeverity = 0; // 0=None, 1=Low, 2=Medium, 3=High

        // ─── HIGH severity rules ───

        // H1: Innux says non-present, Portal says Present
        var innuxNonPresent = new HashSet<string>
        {
            "Absent", "DayOff", "Vacation", "Holiday", "JustifiedAbsence"
        };

        if (innuxNonPresent.Contains(result.InnuxStatus) && result.PortalStatus == "Present")
        {
            types.Add($"StatusConflict_{result.InnuxStatus}VsPresent");
            messages.Add($"Revisar: o Innux indica \"{TranslateStatus(result.InnuxStatus)}\", " +
                         "mas o Portal encontrou picagens válidas com entrada e saída.");
            maxSeverity = 3;
        }

        // H2: Innux has no punches but Portal found complete pairs
        if (innuxNonPresent.Contains(result.InnuxStatus) &&
            result.PortalWorkedMinutes > 0 &&
            result.PortalStatus == "Present")
        {
            // Already covered by H1, but add specific type if not duplicate
            if (!types.Contains($"StatusConflict_{result.InnuxStatus}VsPresent"))
            {
                types.Add("RawPunchesFoundWhileAbsent");
                messages.Add("Revisar: o Innux não reconhece picagens, " +
                             "mas o Portal encontrou pares completos no terminal.");
                maxSeverity = 3;
            }
        }

        // H3: Innux says Present but Portal found no punches
        if (result.InnuxStatus == "Present" && result.PortalStatus == "NoPunches")
        {
            types.Add("StatusConflict_PresentVsNoPunches");
            messages.Add("Revisar: o Innux indica presença, " +
                         "mas o Portal não encontrou picagens brutas no terminal.");
            maxSeverity = 3;
        }

        // ─── MEDIUM severity rules ───

        // M1: Innux Present, Portal Incomplete (has punches but no complete pair)
        if (result.InnuxStatus == "Present" && result.PortalStatus == "Incomplete")
        {
            types.Add("IncompletePairs");
            messages.Add("Revisar manualmente: o Innux indica presença, " +
                         "mas o Portal encontrou picagem incompleta (sem par entrada/saída).");
            maxSeverity = Math.Max(maxSeverity, 2);
        }

        // M2/L1: Both present (or PortalInterpreted), worked minutes differ
        var innuxPresentFamily = new HashSet<string> { "Present", "PortalInterpreted" };
        if (innuxPresentFamily.Contains(result.InnuxStatus) && result.PortalStatus == "Present")
        {
            // If Innux worked minutes are still unavailable after enrichment,
            // do NOT flag a false Medium — add an informational message instead.
            if (result.InnuxWorkedMinutesSource == "NotAvailable" && result.InnuxWorkedMinutes == 0)
            {
                types.Add("InnuxWorkedMinutesUnavailable");
                messages.Add("Atenção: minutos trabalhados do Innux não estavam disponíveis " +
                             "no resumo diário; comparação baseada em dados incompletos.");
                maxSeverity = Math.Max(maxSeverity, 1);
            }
            else
            {
                int workedDiff = Math.Abs(result.InnuxWorkedMinutes - result.PortalWorkedMinutes);

                if (workedDiff > 30)
                {
                    types.Add("WorkedMinutesDrift_High");
                    messages.Add($"Revisar: diferença superior a 30 minutos entre Innux " +
                                 $"({result.InnuxWorkedMinutes}min) e Portal ({result.PortalWorkedMinutes}min).");
                    maxSeverity = Math.Max(maxSeverity, 2);
                }
                else if (workedDiff >= 1)
                {
                    types.Add("WorkedMinutesDrift_Low");
                    messages.Add($"Apenas informativo: diferença de {workedDiff} minutos entre " +
                                 $"Innux ({result.InnuxWorkedMinutes}min) e Portal ({result.PortalWorkedMinutes}min).");
                    maxSeverity = Math.Max(maxSeverity, 1);
                }
            }

            // M3: Entry time drift > 30 min
            if (result.InnuxFirstEntry != null && result.PortalFirstEntry != null)
            {
                int entryDiff = ComputeTimeDiffMinutes(result.InnuxFirstEntry, result.PortalFirstEntry);
                if (entryDiff > 30)
                {
                    types.Add("EntryTimeDrift");
                    messages.Add($"Revisar: hora de entrada difere mais de 30 minutos — " +
                                 $"Innux: {result.InnuxFirstEntry}, Portal: {result.PortalFirstEntry}.");
                    maxSeverity = Math.Max(maxSeverity, 2);
                }
            }

            // M4: Exit time drift > 30 min
            if (result.InnuxLastExit != null && result.PortalLastExit != null)
            {
                int exitDiff = ComputeTimeDiffMinutes(result.InnuxLastExit, result.PortalLastExit);
                if (exitDiff > 30)
                {
                    types.Add("ExitTimeDrift");
                    messages.Add($"Revisar: hora de saída difere mais de 30 minutos — " +
                                 $"Innux: {result.InnuxLastExit}, Portal: {result.PortalLastExit}.");
                    maxSeverity = Math.Max(maxSeverity, 2);
                }
            }
        }

        // M5: Duplicates detected
        bool hasDuplicates = portalResult?.RawPunches?.Any(p => p.IsDuplicateCandidate) ?? false;
        if (hasDuplicates)
        {
            types.Add("DuplicatesDetected");
            int dupCount = portalResult!.RawPunches.Count(p => p.IsDuplicateCandidate);
            messages.Add($"Apenas informativo: {dupCount} picagem(ns) duplicada(s) detectada(s) pelo Portal.");
            maxSeverity = Math.Max(maxSeverity, 2);
        }

        // ─── LOW severity rules ───

        // L2: Schedule resolved via Alteracoes fallback but otherwise coherent
        if (result.ScheduleResolutionSource == "Alteracoes.IDHorario" &&
            maxSeverity < 2) // Only flag if no higher issue
        {
            types.Add("ScheduleFallback");
            messages.Add("Apenas informativo: turno resolvido via fallback Alteracoes.IDHorario.");
            maxSeverity = Math.Max(maxSeverity, 1);
        }

        // L3: Portal confidence is Low but no strong contradiction
        if (result.ConfidenceLevel == "Low" && maxSeverity < 2)
        {
            types.Add("LowConfidence");
            messages.Add("Apenas informativo: confiança do Portal é baixa para esta interpretação.");
            maxSeverity = Math.Max(maxSeverity, 1);
        }

        // ─── Assign results ───
        result.DiscrepancyTypes = types;
        result.DiscrepancyMessages = messages;
        result.HasDiscrepancy = maxSeverity > 0;
        result.DiscrepancySeverity = maxSeverity switch
        {
            3 => "High",
            2 => "Medium",
            1 => "Low",
            _ => "None"
        };

        // ─── Recommended review action ───
        result.RecommendedReviewAction = maxSeverity switch
        {
            3 => messages.First(), // Use the first HIGH message
            2 => messages.First(m => m.StartsWith("Revisar")),
            1 => "Sem divergência relevante. " + string.Join(" ", messages),
            _ => "Sem divergência relevante."
        };

        // Safety: if recommendation lookup fails, use default
        if (string.IsNullOrEmpty(result.RecommendedReviewAction))
            result.RecommendedReviewAction = "Sem divergência relevante.";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes absolute time difference in minutes between two HH:mm or HH:mm:ss strings.
    /// Returns 0 if either value is invalid.
    /// </summary>
    private static int ComputeTimeDiffMinutes(string time1, string time2)
    {
        if (TimeSpan.TryParse(time1, out var ts1) && TimeSpan.TryParse(time2, out var ts2))
        {
            return (int)Math.Abs((ts1 - ts2).TotalMinutes);
        }
        return 0;
    }

    /// <summary>
    /// Translates Innux AttendanceStatus codes to Portuguese for HR messages.
    /// </summary>
    private static string TranslateStatus(string status) => status switch
    {
        "Absent" => "Ausência",
        "DayOff" => "Folga",
        "Vacation" => "Férias",
        "Holiday" => "Feriado",
        "JustifiedAbsence" => "Ausência Justificada",
        "Present" => "Presente",
        "Anomaly" => "Anomalia",
        _ => status
    };
}
