using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Innux processed attendance data — read-only.
///
/// Queries dbo.Alteracoes (daily attendance summary) and dbo.AlteracoesPeriodos
/// (period breakdown) with schedule enrichment from dbo.Horarios.
///
/// This service is scope-agnostic: it accepts pre-scoped Innux employee IDs
/// and does NOT apply Portal ACL. Scoping is resolved at the controller layer.
///
/// Query strategy:
/// - All transactional queries require date-range filtering
/// - All reference joins use LEFT JOIN (no FK constraints in Innux)
/// - Time values are converted from Innux datetime-as-duration via InnuxTimeHelper
/// - Results are cached with 5-minute TTL for calendar queries
///
/// Raw punches (TerminaisMarcacoes) for drill-down are also handled here
/// to keep the day-detail query cohesive.
///
/// Read-only: SELECT only, parameterized queries. No writes to Innux.
/// </summary>
public class InnuxAttendanceService : IInnuxAttendanceService
{
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InnuxAttendanceService> _logger;

    private static readonly TimeSpan CalendarCacheDuration = TimeSpan.FromMinutes(5);

    public InnuxAttendanceService(
        InnuxConnectionFactory connectionFactory,
        IMemoryCache cache,
        ILogger<InnuxAttendanceService> logger)
    {
        _connectionFactory = connectionFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AttendanceDaySummaryDto>> GetDailyAttendanceAsync(
        IEnumerable<int> innuxEmployeeIds,
        DateTime startDate,
        DateTime endDate)
    {
        var idList = innuxEmployeeIds.ToList();
        if (idList.Count == 0)
            return Enumerable.Empty<AttendanceDaySummaryDto>();

        // Enforce reasonable date range (max 90 days) to prevent full-table scans
        if ((endDate - startDate).TotalDays > 90)
            throw new ArgumentException("Date range cannot exceed 90 days.");

        // Cache key based on sorted IDs + date range
        var idHash = string.Join(",", idList.OrderBy(x => x));
        var cacheKey = $"innux:attendance:{idHash}:{startDate:yyyyMMdd}:{endDate:yyyyMMdd}";

        if (_cache.TryGetValue(cacheKey, out IEnumerable<AttendanceDaySummaryDto>? cached) && cached != null)
        {
            _logger.LogDebug("InnuxAttendanceService: cache hit for {CacheKey}", cacheKey);
            return cached;
        }

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            // Build parameterised IN clause
            var (inClause, parameters) = BuildInClause(idList, "empId");
            parameters.Add(new SqlParameter("@StartDate", startDate.Date));
            parameters.Add(new SqlParameter("@EndDate", endDate.Date));

            var query = $@"
                SELECT
                    a.IDFuncionario,
                    a.Data,
                    h.Codigo       AS ScheduleCode,
                    h.Descricao    AS ScheduleDescription,
                    h.Sigla        AS ScheduleSigla,
                    ISNULL(h.DiaFolga, 0) AS IsRestDay,
                    -- Overnight: mandatory period crosses midnight, schedule is NOT a rest day
                    CASE WHEN ISNULL(h.DiaFolga, 0) = 0 AND EXISTS (
                        SELECT 1 FROM dbo.HorariosPeriodos hp2
                        WHERE hp2.IDHorario = h.IDHorario
                          AND hp2.Tipo = 'Obrigatório'
                          AND hp2.Fim IS NOT NULL
                          AND CAST(hp2.Fim AS DATE) > '1900-01-01'
                    ) THEN 1 ELSE 0 END AS IsOvernightShift,
                    -- Earliest mandatory period start
                    (SELECT TOP 1 hp3.Inicio FROM dbo.HorariosPeriodos hp3
                     WHERE hp3.IDHorario = h.IDHorario AND hp3.Tipo = 'Obrigatório'
                     ORDER BY hp3.Inicio) AS ScheduleStart,
                    -- Latest period end
                    (SELECT TOP 1 hp4.Fim FROM dbo.HorariosPeriodos hp4
                     WHERE hp4.IDHorario = h.IDHorario AND hp4.Tipo = 'Obrigatório'
                     ORDER BY hp4.Fim DESC) AS ScheduleEnd,
                    a.Entrada1     AS Entrada,
                    a.Saida1       AS Saida,
                    a.Falta,
                    a.Ausencia,
                    a.Objectivo,
                    a.Saldo,
                    a.Marcacao,
                    a.Validado,
                    a.FaltouPeriodosObrigatorios,
                    a.TipoAnomalia,
                    a.Justificacao,
                    -- Live count of raw terminal punches (for anomaly detection).
                    -- For overnight shifts, also count early-morning punches from the next
                    -- calendar date (the exit punch is stored under Date+1 in Innux).
                    (SELECT COUNT(*) FROM dbo.TerminaisMarcacoes tm
                     WHERE tm.IDFuncionario = a.IDFuncionario
                       AND (
                           tm.Data = a.Data
                           OR (
                               -- Include next-day early-morning punches for overnight shifts
                               ISNULL(h.DiaFolga, 0) = 0
                               AND EXISTS (
                                   SELECT 1 FROM dbo.HorariosPeriodos hp5
                                   WHERE hp5.IDHorario = h.IDHorario
                                     AND hp5.Tipo = 'Obrigatório'
                                     AND hp5.Fim IS NOT NULL
                                     AND CAST(hp5.Fim AS DATE) > '1900-01-01'
                               )
                               AND tm.Data = DATEADD(DAY, 1, a.Data)
                               AND tm.Hora < '1900-01-01 12:00:00'
                           )
                       )
                    ) AS RawTerminalCount
                FROM dbo.Alteracoes a
                LEFT JOIN dbo.Horarios h ON a.IDHorario = h.IDHorario
                WHERE a.IDFuncionario IN ({inClause})
                  AND a.Data >= @StartDate
                  AND a.Data <= @EndDate
                ORDER BY a.IDFuncionario, a.Data";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddRange(parameters.ToArray());
            command.CommandTimeout = 30;

            var results = new List<AttendanceDaySummaryDto>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(MapToSummaryDto(reader));
            }

            _cache.Set(cacheKey, (IEnumerable<AttendanceDaySummaryDto>)results, CalendarCacheDuration);
            _logger.LogDebug(
                "InnuxAttendanceService: loaded {Count} attendance rows for {EmployeeCount} employees, {Start} to {End}",
                results.Count, idList.Count, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

            return results;
        }
        catch (InvalidOperationException)
        {
            throw; // Configuration errors — rethrow for controller
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch attendance for {Count} employees between {Start} and {End}",
                idList.Count, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AttendanceDayDetailDto?> GetDayDetailAsync(int innuxEmployeeId, DateTime date)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            // 1. Get the summary row (uses Alteracoes.Marcacao as official processed punch count)
            var summary = await GetSingleSummaryAsync(connection, innuxEmployeeId, date);
            if (summary == null)
                return null;

            // 2. Get period breakdown (AlteracoesPeriodos)
            var periods = await GetPeriodsAsync(connection, innuxEmployeeId, date);

            // 3. Get raw punches (TerminaisMarcacoes) — supporting/debug evidence
            //    For overnight shifts, also fetch next-day early-morning punches.
            var punches = await GetPunchesAsync(connection, innuxEmployeeId, date, summary.IsOvernightShift);

            // 4. Set raw punch count on summary for transparency
            //    This is the live terminal count, may differ from processed Alteracoes.Marcacao
            summary.RawPunchCount = punches.Count;

            // 5. Re-classify attendance status with the ACTUAL raw punch count.
            //    MapToSummaryDto classified with rawPunchCount=-1 because GetSingleSummaryAsync
            //    does not include the RawTerminalCount subquery. Now that we know the true
            //    punch count from GetPunchesAsync, re-run classification for accuracy.
            summary.AttendanceStatus = ClassifyAttendance(
                summary.AbsenceMinutes, summary.JustifiedAbsenceMinutes, summary.ExpectedMinutes,
                summary.PunchCount, punches.Count, summary.AnomalyDescription,
                summary.MissedMandatoryPeriods, summary.IsValidated,
                summary.FirstEntry, summary.FirstExit, summary.Justification);

            // 6. Build debug metadata for HR/IT transparency
            var debugSource = summary.PunchCount > 0 && punches.Count == 0
                ? "Processado Innux (sem marcações brutas de terminal)"
                : summary.PunchCount == punches.Count
                    ? "Processado Innux (consistente com marcações brutas)"
                    : $"Processado Innux (Marcação={summary.PunchCount}, Terminal={punches.Count})";

            return new AttendanceDayDetailDto
            {
                Summary = summary,
                RawPunches = punches,
                Periods = periods,
                DebugProcessedPunchCount = summary.PunchCount,
                DebugRawTerminalPunchCount = punches.Count,
                DebugIsValidated = summary.IsValidated,
                DebugScheduleCode = summary.ScheduleCode,
                DebugScheduleDescription = summary.ScheduleDescription,
                DebugStatusSource = debugSource
            };
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch attendance detail for employee {EmployeeId} on {Date}",
                innuxEmployeeId, date.ToString("yyyy-MM-dd"));
            throw;
        }
    }

    // ─── Private query methods ───

    /// <inheritdoc />
    public async Task<Dictionary<(int EmployeeId, DateTime Date), WorkedHoursDto>> GetWorkedHoursAsync(
        IEnumerable<int> innuxEmployeeIds,
        DateTime startDate,
        DateTime endDate)
    {
        var idList = innuxEmployeeIds.ToList();
        if (idList.Count == 0)
            return new Dictionary<(int, DateTime), WorkedHoursDto>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            var (inClause, parameters) = BuildInClause(idList, "empId");
            parameters.Add(new SqlParameter("@StartDate", startDate.Date));
            parameters.Add(new SqlParameter("@EndDate", endDate.Date));

            // Aggregate non-dispensed periods by employee, date, and work type.
            // CodigosTrabalho.Tipo values observed: 'Normal', 'Extra', 'Extra 2', etc.
            // Duration = DATEDIFF(MINUTE, Inicio, Fim) using Innux's 1900-01-01 base encoding.
            var query = $@"
                SELECT
                    a.IDFuncionario,
                    a.Data,
                    ct.Tipo AS WorkType,
                    SUM(DATEDIFF(MINUTE, ap.Inicio, ap.Fim)) AS TotalMinutes
                FROM dbo.AlteracoesPeriodos ap
                INNER JOIN dbo.Alteracoes a ON ap.IDAlteracao = a.IDAlteracao
                LEFT JOIN dbo.CodigosTrabalho ct ON ap.IDCodigoTrabalho = ct.IDCodigoTrabalho
                WHERE a.IDFuncionario IN ({inClause})
                  AND a.Data >= @StartDate
                  AND a.Data <= @EndDate
                  AND ISNULL(ap.Dispensa, 0) = 0
                GROUP BY a.IDFuncionario, a.Data, ct.Tipo";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddRange(parameters.ToArray());
            command.CommandTimeout = 30;

            var result = new Dictionary<(int, DateTime), WorkedHoursDto>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var empId = InnuxTimeHelper.SafeInt(reader["IDFuncionario"]);
                var date = reader["Data"] is DateTime d ? d.Date : DateTime.MinValue;
                var workType = (reader["WorkType"] as string)?.Trim() ?? "";
                var minutes = InnuxTimeHelper.SafeInt(reader["TotalMinutes"]);

                if (minutes <= 0) continue;

                var key = (empId, date);
                if (!result.TryGetValue(key, out var dto))
                {
                    dto = new WorkedHoursDto();
                    result[key] = dto;
                }

                // Classify: "Normal" → Basic, anything containing "Extra" → Overtime
                if (workType.Contains("Extra", StringComparison.OrdinalIgnoreCase))
                    dto.OvertimeMinutes += minutes;
                else
                    dto.BasicMinutes += minutes;
            }

            _logger.LogDebug(
                "InnuxAttendanceService: computed worked hours for {Count} employee-day pairs, {Start} to {End}",
                result.Count, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

            return result;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to compute worked hours for {Count} employees between {Start} and {End}",
                idList.Count, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
            throw;
        }
    }

    private async Task<AttendanceDaySummaryDto?> GetSingleSummaryAsync(
        SqlConnection connection, int innuxEmployeeId, DateTime date)
    {
        var query = @"
            SELECT TOP 1
                a.IDFuncionario,
                a.Data,
                h.Codigo       AS ScheduleCode,
                h.Descricao    AS ScheduleDescription,
                h.Sigla        AS ScheduleSigla,
                ISNULL(h.DiaFolga, 0) AS IsRestDay,
                CASE WHEN ISNULL(h.DiaFolga, 0) = 0 AND EXISTS (
                    SELECT 1 FROM dbo.HorariosPeriodos hp2
                    WHERE hp2.IDHorario = h.IDHorario
                      AND hp2.Tipo = 'Obrigatório'
                      AND hp2.Fim IS NOT NULL
                      AND CAST(hp2.Fim AS DATE) > '1900-01-01'
                ) THEN 1 ELSE 0 END AS IsOvernightShift,
                (SELECT TOP 1 hp3.Inicio FROM dbo.HorariosPeriodos hp3
                 WHERE hp3.IDHorario = h.IDHorario AND hp3.Tipo = 'Obrigatório'
                 ORDER BY hp3.Inicio) AS ScheduleStart,
                (SELECT TOP 1 hp4.Fim FROM dbo.HorariosPeriodos hp4
                 WHERE hp4.IDHorario = h.IDHorario AND hp4.Tipo = 'Obrigatório'
                 ORDER BY hp4.Fim DESC) AS ScheduleEnd,
                a.Entrada1     AS Entrada,
                a.Saida1       AS Saida,
                a.Falta,
                a.Ausencia,
                a.Objectivo,
                a.Saldo,
                a.Marcacao,
                a.Validado,
                a.FaltouPeriodosObrigatorios,
                a.TipoAnomalia,
                a.Justificacao
            FROM dbo.Alteracoes a
            LEFT JOIN dbo.Horarios h ON a.IDHorario = h.IDHorario
            WHERE a.IDFuncionario = @EmployeeId
              AND a.Data = @Date";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@EmployeeId", innuxEmployeeId);
        command.Parameters.AddWithValue("@Date", date.Date);
        command.CommandTimeout = 15;

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return MapToSummaryDto(reader);

        return null;
    }

    private async Task<List<AttendancePeriodDto>> GetPeriodsAsync(
        SqlConnection connection, int innuxEmployeeId, DateTime date)
    {
        // AlteracoesPeriodos links to Alteracoes via IDAlteracao (not IDFuncionario+Data).
        // Columns: Inicio (start), Fim (end), Dispensa (dispensed).
        var query = @"
            SELECT
                ap.Inicio,
                ap.Fim,
                ct.Codigo      AS WorkCode,
                ct.Descricao   AS WorkDescription,
                ca.Codigo      AS AbsenceCode,
                ca.Descricao   AS AbsenceDescription,
                ap.Dispensa
            FROM dbo.AlteracoesPeriodos ap
            INNER JOIN dbo.Alteracoes a
                ON ap.IDAlteracao = a.IDAlteracao
            LEFT JOIN dbo.CodigosTrabalho ct ON ap.IDCodigoTrabalho = ct.IDCodigoTrabalho
            LEFT JOIN dbo.CodigosAusencia ca ON ap.IDCodigoAusencia = ca.IDCodigoAusencia
            WHERE a.IDFuncionario = @EmployeeId
              AND a.Data = @Date
            ORDER BY ap.Inicio";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@EmployeeId", innuxEmployeeId);
        command.Parameters.AddWithValue("@Date", date.Date);
        command.CommandTimeout = 15;

        var periods = new List<AttendancePeriodDto>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            periods.Add(new AttendancePeriodDto
            {
                StartTime = InnuxTimeHelper.ToTimeString(reader["Inicio"]),
                EndTime = InnuxTimeHelper.ToTimeString(reader["Fim"]),
                WorkCode = reader["WorkCode"] as string,
                WorkDescription = reader["WorkDescription"] as string,
                AbsenceCode = reader["AbsenceCode"] as string,
                AbsenceDescription = reader["AbsenceDescription"] as string,
                IsDispensed = InnuxTimeHelper.SafeBool(reader["Dispensa"])
            });
        }

        return periods;
    }

    /// <summary>
    /// Fetches raw terminal punches for an employee on a given date.
    /// For overnight shifts, also fetches early-morning punches from the next calendar
    /// date (before midday) — because Innux stores the cross-midnight exit punch under
    /// the next date in TerminaisMarcacoes.
    /// </summary>
    private async Task<List<AttendancePunchDto>> GetPunchesAsync(
        SqlConnection connection, int innuxEmployeeId, DateTime date,
        bool isOvernightShift = false)
    {
        // TerminaisMarcacoes: Gerada (auto-generated), not Automatica.
        // Terminais: Nome (terminal name), not Descricao.
        //
        // Cross-midnight logic:
        //   For overnight shifts (e.g. 20:00-08:00), the exit punch is stored
        //   under Date+1 in Innux. We include next-day punches before 12:00
        //   so the drawer shows the complete shift picture.
        var query = @"
            SELECT
                tm.IDFuncionario,
                tm.Data,
                tm.Hora,
                tm.TipoProcessado,
                tm.Gerada,
                t.Nome AS TerminalName
            FROM dbo.TerminaisMarcacoes tm
            LEFT JOIN dbo.Terminais t ON tm.IDTerminal = t.IDTerminal
            WHERE tm.IDFuncionario = @EmployeeId
              AND (
                  tm.Data = @Date
                  OR (
                      @IsOvernightShift = 1
                      AND tm.Data = DATEADD(DAY, 1, @Date)
                      AND tm.Hora < '1900-01-01 12:00:00'
                  )
              )
            ORDER BY tm.Data, tm.Hora";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@EmployeeId", innuxEmployeeId);
        command.Parameters.AddWithValue("@Date", date.Date);
        command.Parameters.AddWithValue("@IsOvernightShift", isOvernightShift ? 1 : 0);
        command.CommandTimeout = 15;

        var punches = new List<AttendancePunchDto>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var direction = reader["TipoProcessado"]?.ToString()?.Trim() ?? "";
            punches.Add(new AttendancePunchDto
            {
                InnuxEmployeeId = InnuxTimeHelper.SafeInt(reader["IDFuncionario"]),
                Date = reader["Data"] is DateTime d ? d : date,
                Time = InnuxTimeHelper.ToTimeStringFull(reader["Hora"]),
                Direction = direction,
                DirectionLabel = MapDirectionLabel(direction),
                TerminalName = reader["TerminalName"] as string,
                IsAutoGenerated = InnuxTimeHelper.SafeBool(reader["Gerada"])
            });
        }

        return punches;
    }

    // ─── Mapping ───

    private static AttendanceDaySummaryDto MapToSummaryDto(SqlDataReader reader)
    {
        var absenceMinutes = InnuxTimeHelper.ToMinutes(reader["Falta"]);
        var justifiedMinutes = InnuxTimeHelper.ToMinutes(reader["Ausencia"]);
        var expectedMinutes = InnuxTimeHelper.ToMinutes(reader["Objectivo"]);
        var punchCount = InnuxTimeHelper.SafeInt(reader["Marcacao"]);
        var anomaly = reader["TipoAnomalia"] as string;
        var missedPeriods = InnuxTimeHelper.SafeBool(reader["FaltouPeriodosObrigatorios"]);

        // Raw terminal count — populated from subquery in calendar query,
        // or defaults to -1 (unavailable) for queries without the column.
        int rawPunchCount = 0;
        try { rawPunchCount = InnuxTimeHelper.SafeInt(reader["RawTerminalCount"]); }
        catch (IndexOutOfRangeException) { rawPunchCount = -1; }

        return new AttendanceDaySummaryDto
        {
            InnuxEmployeeId = InnuxTimeHelper.SafeInt(reader["IDFuncionario"]),
            Date = reader["Data"] is DateTime d ? d : DateTime.MinValue,
            ScheduleCode = reader["ScheduleCode"] as string,
            ScheduleDescription = reader["ScheduleDescription"] as string,
            ScheduleSigla = reader["ScheduleSigla"] as string,
            IsRestDay = InnuxTimeHelper.SafeBool(reader["IsRestDay"]),
            IsOvernightShift = InnuxTimeHelper.SafeBool(reader["IsOvernightShift"]),
            ScheduleStartTime = InnuxTimeHelper.ToTimeString(reader["ScheduleStart"]),
            ScheduleEndTime = InnuxTimeHelper.ToTimeString(reader["ScheduleEnd"]),
            FirstEntry = InnuxTimeHelper.ToTimeString(reader["Entrada"]),
            FirstExit = InnuxTimeHelper.ToTimeString(reader["Saida"]),
            AbsenceMinutes = absenceMinutes,
            JustifiedAbsenceMinutes = justifiedMinutes,
            ExpectedMinutes = expectedMinutes,
            BalanceMinutes = InnuxTimeHelper.ToMinutes(reader["Saldo"]),
            PunchCount = punchCount,
            RawPunchCount = rawPunchCount >= 0 ? rawPunchCount : 0,
            IsValidated = InnuxTimeHelper.SafeBool(reader["Validado"]),
            MissedMandatoryPeriods = missedPeriods,
            AnomalyDescription = string.IsNullOrWhiteSpace(anomaly) ? null : anomaly,
            Justification = reader["Justificacao"] as string,
            AttendanceStatus = ClassifyAttendance(
                absenceMinutes, justifiedMinutes, expectedMinutes,
                punchCount, rawPunchCount, anomaly, missedPeriods,
                InnuxTimeHelper.SafeBool(reader["Validado"]),
                InnuxTimeHelper.ToTimeString(reader["Entrada"]),
                InnuxTimeHelper.ToTimeString(reader["Saida"]),
                reader["Justificacao"] as string)
        };
    }

    /// <summary>
    /// Derives a Portal-friendly attendance classification from Innux fields.
    /// This is a best-effort classification — edge cases may require refinement
    /// as we learn more about Innux processing conventions.
    ///
    /// Status values:
    ///   "Present"          — Employee was at work (confirmed by raw terminal punches
    ///                        or by validated entry/exit on Escala-Intercalada days).
    ///   "Absent"           — Full unjustified absence (no raw terminal records).
    ///   "JustifiedAbsence" — Full justified absence (generic, e.g. sick leave).
    ///   "Vacation"         — Justified absence with "Gozo de Férias" justification.
    ///   "Holiday"          — Justified absence with "Feriado" justification.
    ///   "DayOff"           — No expected time (rest day / schedule off).
    ///   "Anomaly"          — Innux flagged a processing anomaly, OR processed punch
    ///                        exists without raw terminal records (e.g. Escala auto-processing),
    ///                        OR absence was declared but raw terminal events exist that were
    ///                        not recognised as valid Entry/Exit punches.
    ///   "Unknown"          — Cannot determine status.
    ///
    /// Key Innux conventions:
    ///   - Marcacao (punchCount) is a PENDING counter: Innux resets it to 0 after
    ///     full validation for Escala-Intercalada patterns. Marcacao=0 + Validado=True
    ///     does NOT mean "absent" — it means "fully processed".
    ///   - For overnight shifts, raw terminal punches may be split across two
    ///     calendar dates (entry on Day N, exit on Day N+1).
    /// </summary>
    private static string ClassifyAttendance(
        int absenceMinutes, int justifiedMinutes, int expectedMinutes,
        int punchCount, int rawPunchCount, string? anomaly, bool missedPeriods,
        bool isValidated,
        string? firstEntry, string? firstExit, string? justification)
    {
        var justNorm = justification?.Trim() ?? "";
        var hasEntry = !string.IsNullOrWhiteSpace(firstEntry);
        var hasExit = !string.IsNullOrWhiteSpace(firstExit);

        // 1. Explicitly check justification text first.
        // Innux often marks vacations, holidays, and days off by writing text in the justification
        // without necessarily updating expected or absence minutes correctly.
        if (justNorm.Contains("Gozo de Férias", StringComparison.OrdinalIgnoreCase) || 
            justNorm.Contains("Ferias", StringComparison.OrdinalIgnoreCase))
        {
            return "Vacation";
        }

        if (justNorm.Contains("Feriado", StringComparison.OrdinalIgnoreCase))
        {
            return "Holiday";
        }

        if (justNorm.Contains("Folga", StringComparison.OrdinalIgnoreCase))
        {
            return "DayOff";
        }

        // 2. Day off based on expected time
        if (expectedMinutes == 0)
        {
            return "DayOff";
        }

        // 3. Anomaly flagged by Innux
        if (!string.IsNullOrWhiteSpace(anomaly))
            return "Anomaly";

        // 4. Full unjustified absence
        if (absenceMinutes > 0 && absenceMinutes >= expectedMinutes)
        {
            // 4b. Absence-with-raw-terminal-events contradiction:
            // Innux declared the day as absence (F03 / Falta Injustificada),
            // but raw terminal punches exist that were NOT recognised as valid
            // Entry/Exit. This typically happens when the terminal code/direction
            // (e.g. code 17) is unknown to Innux processing. The employee was
            // physically at a terminal — HR must review.
            if (rawPunchCount > 0 && !hasEntry && !hasExit && punchCount == 0)
                return "Anomaly";

            return "Absent";
        }

        // 5. Full justified absence
        if (justifiedMinutes > 0 && justifiedMinutes >= expectedMinutes)
            return "JustifiedAbsence";

        // 6. Present — confirmed by raw terminal punches
        if (punchCount > 0 && rawPunchCount > 0)
            return "Present";

        // 6b. Present confirmed by entry/exit times with raw punches
        if (rawPunchCount > 0 && (hasEntry || hasExit))
            return "Present";

        // 6c. Validated day with entry/exit = Present.
        //     Innux resets Marcacao to 0 after full validation for Escala-Intercalada
        //     patterns. A validated day with confirmed entry/exit times is Present
        //     even when Marcacao=0 — it means "fully processed", not "absent".
        if (isValidated && (hasEntry || hasExit) && rawPunchCount > 0)
            return "Present";

        // 7. Anomaly: processed punch exists but NO raw terminal records.
        // This typically occurs with Escala (rotation) auto-processing or manual HR
        // validation in Innux that sets Marcacao without creating terminal records.
        // Surfaced as Anomaly so HR can review and confirm.
        if (punchCount > 0 && rawPunchCount == 0)
            return "Anomaly";

        // 8. If entry/exit exists but no raw punches, still surface for review.
        //    This covers validated Escala days where Innux set entry/exit from rotation
        //    logic but no physical terminal record exists.
        if ((hasEntry || hasExit) && rawPunchCount == 0)
            return "Anomaly";

        // 9. If expected > 0 and no absence/justification, but no punches,
        // it means either it was validated manually or hasn't been processed.
        return "Unknown";
    }

    // ─── Direction label mapping ───

    /// <summary>
    /// Maps Innux TerminaisMarcacoes.TipoProcessado values to human-readable labels.
    /// Known values: "EN" = Entry, "SA" = Exit.
    /// Numeric codes (e.g. "17") are terminal event types not recognised as valid
    /// attendance punches — displayed as "Código XX" to avoid implying Entry/Exit.
    /// </summary>
    private static string MapDirectionLabel(string direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
            return "Sem direção";

        if (direction.Equals("EN", StringComparison.OrdinalIgnoreCase))
            return "Entrada";
        if (direction.Equals("SA", StringComparison.OrdinalIgnoreCase))
            return "Saída";

        // Numeric codes are terminal event types not mapped to Entry/Exit.
        // Show them clearly as raw codes so HR understands they are not attendance punches.
        if (int.TryParse(direction, out _))
            return $"Código {direction}";

        // Unknown text — show as-is
        return direction;
    }

    // ─── SQL IN clause builder ───

    /// <summary>
    /// Builds a parameterised SQL IN clause to prevent SQL injection.
    /// Returns the clause string and the list of SqlParameter objects.
    /// </summary>
    private static (string clause, List<SqlParameter> parameters) BuildInClause(
        List<int> ids, string prefix)
    {
        var parameters = new List<SqlParameter>();
        var names = new List<string>();

        for (var i = 0; i < ids.Count; i++)
        {
            var name = $"@{prefix}{i}";
            names.Add(name);
            parameters.Add(new SqlParameter(name, ids[i]));
        }

        return (string.Join(", ", names), parameters);
    }
}
