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
                    a.Justificacao
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

            // 1. Get the summary row
            var summary = await GetSingleSummaryAsync(connection, innuxEmployeeId, date);
            if (summary == null)
                return null;

            // 2. Get period breakdown (AlteracoesPeriodos)
            var periods = await GetPeriodsAsync(connection, innuxEmployeeId, date);

            // 3. Get raw punches (TerminaisMarcacoes)
            var punches = await GetPunchesAsync(connection, innuxEmployeeId, date);

            return new AttendanceDayDetailDto
            {
                Summary = summary,
                RawPunches = punches,
                Periods = periods
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

    private async Task<List<AttendancePunchDto>> GetPunchesAsync(
        SqlConnection connection, int innuxEmployeeId, DateTime date)
    {
        // TerminaisMarcacoes: Gerada (auto-generated), not Automatica.
        // Terminais: Nome (terminal name), not Descricao.
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
              AND tm.Data = @Date
            ORDER BY tm.Hora";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@EmployeeId", innuxEmployeeId);
        command.Parameters.AddWithValue("@Date", date.Date);
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
                DirectionLabel = direction.Equals("EN", StringComparison.OrdinalIgnoreCase) ? "Entry"
                               : direction.Equals("SA", StringComparison.OrdinalIgnoreCase) ? "Exit"
                               : direction,
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
            IsValidated = InnuxTimeHelper.SafeBool(reader["Validado"]),
            MissedMandatoryPeriods = missedPeriods,
            AnomalyDescription = string.IsNullOrWhiteSpace(anomaly) ? null : anomaly,
            Justification = reader["Justificacao"] as string,
            AttendanceStatus = ClassifyAttendance(
                absenceMinutes, justifiedMinutes, expectedMinutes,
                punchCount, anomaly, missedPeriods,
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
    ///   "Present"          — Employee was at work (has punches or entry/exit).
    ///   "Absent"           — Full unjustified absence.
    ///   "JustifiedAbsence" — Full justified absence (generic, e.g. sick leave).
    ///   "Vacation"         — Justified absence with "Gozo de Férias" justification.
    ///   "Holiday"          — Justified absence with "Feriado" justification.
    ///   "DayOff"           — No expected time (rest day / schedule off).
    ///   "Anomaly"          — Innux flagged a processing anomaly.
    ///   "Unknown"          — Cannot determine status.
    /// </summary>
    private static string ClassifyAttendance(
        int absenceMinutes, int justifiedMinutes, int expectedMinutes,
        int punchCount, string? anomaly, bool missedPeriods,
        string? firstEntry, string? firstExit, string? justification)
    {
        var justNorm = justification?.Trim() ?? "";

        // Day off: no expected time
        if (expectedMinutes == 0)
        {
            // Even on a day-off, check if justification indicates Holiday
            if (justNorm.Contains("Feriado", StringComparison.OrdinalIgnoreCase))
                return "Holiday";
            return "DayOff";
        }

        // Anomaly: if Innux flagged an issue
        if (!string.IsNullOrWhiteSpace(anomaly))
            return "Anomaly";

        // Full unjustified absence
        if (absenceMinutes > 0 && absenceMinutes >= expectedMinutes)
            return "Absent";

        // Full justified absence — sub-classify by justification text
        if (justifiedMinutes > 0 && justifiedMinutes >= expectedMinutes)
        {
            if (justNorm.Contains("Gozo de Férias", StringComparison.OrdinalIgnoreCase)
                || justNorm.Contains("Ferias", StringComparison.OrdinalIgnoreCase))
                return "Vacation";

            if (justNorm.Contains("Feriado", StringComparison.OrdinalIgnoreCase))
                return "Holiday";

            return "JustifiedAbsence";
        }

        // Has punches or valid entry/exit times: likely present (may have partial absence)
        if (punchCount > 0 || !string.IsNullOrWhiteSpace(firstEntry) || !string.IsNullOrWhiteSpace(firstExit))
            return "Present";

        // No punches, expected time, no absence recorded — anomalous
        return "Unknown";
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
