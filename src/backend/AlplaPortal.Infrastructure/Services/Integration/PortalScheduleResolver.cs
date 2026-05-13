using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Portal-side Schedule Day Resolver — read-only.
///
/// Resolves which Innux schedule (Horario) applies to an employee on a specific date
/// by computing the work plan cycle day index and loading schedule periods.
///
/// Data path (primary):
///   Funcionarios.IDPlanoTrabalho → PlanosTrabalho (cycle metadata)
///   → PlanosTrabalhoHorarios (day→schedule mapping) → Horarios (schedule definition)
///   → HorariosPeriodos (time windows with tolerances)
///
/// Data path (fallback for Escala plans with CycleDays == 0):
///   Funcionarios.IDFuncionario + Date → Alteracoes.IDHorario → Horarios
///   → HorariosPeriodos (time windows with tolerances)
///   This covers ~64% of employees whose rotation is assigned daily in Alteracoes
///   rather than via PlanosTrabalhoHorarios cycle mappings.
///
/// Read-only: SELECT only, parameterized queries. No writes to Innux.
/// </summary>
public class PortalScheduleResolver : IPortalScheduleResolver
{
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly ILogger<PortalScheduleResolver> _logger;

    public PortalScheduleResolver(
        InnuxConnectionFactory connectionFactory,
        ILogger<PortalScheduleResolver> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ResolvedScheduleDto?> ResolveScheduleForDateAsync(int innuxEmployeeId, DateTime date)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            // ── Step 1: Load employee's work plan + resolve cycle day ──
            var planInfo = await LoadEmployeePlanAsync(connection, innuxEmployeeId);
            if (planInfo == null)
            {
                _logger.LogDebug(
                    "PortalScheduleResolver: Employee {EmployeeId} has no work plan assigned",
                    innuxEmployeeId);
                return null;
            }

            // Compute cycle day index
            int resolvedDayIndex;
            if (planInfo.CycleDays > 0 && planInfo.CycleStartDate.HasValue)
            {
                var daysDiff = (date.Date - planInfo.CycleStartDate.Value.Date).Days;
                resolvedDayIndex = ((daysDiff % planInfo.CycleDays) + planInfo.CycleDays) % planInfo.CycleDays;
            }
            else
            {
                // Fallback for plans without cycle start or cycle days = 0
                resolvedDayIndex = (int)date.DayOfWeek;
                // Innux uses Monday=0, DayOfWeek uses Sunday=0 — adjust
                resolvedDayIndex = resolvedDayIndex == 0 ? 6 : resolvedDayIndex - 1;
            }

            _logger.LogDebug(
                "PortalScheduleResolver: Employee {EmployeeId}, Date {Date:yyyy-MM-dd}, " +
                "Plan {PlanCode} ({PlanType}), CycleDays={CycleDays}, ResolvedDayIndex={DayIndex}",
                innuxEmployeeId, date, planInfo.PlanCode, planInfo.PlanType,
                planInfo.CycleDays, resolvedDayIndex);

            // ── Step 2: Resolve which schedule applies on this day ──
            var scheduleInfo = await LoadScheduleForDayAsync(
                connection, planInfo.PlanId, resolvedDayIndex);

            // ── Step 2b: Fallback for Escala-type plans ──
            // Escala plans typically have CycleDays == 0 and no PlanosTrabalhoHorarios
            // mappings. In these cases, the schedule is assigned daily via
            // Alteracoes.IDHorario by the Innux processing engine.
            string scheduleResolutionSource = "PlanosTrabalhoHorarios";
            if (scheduleInfo == null && planInfo.CycleDays == 0)
            {
                _logger.LogDebug(
                    "PortalScheduleResolver: Primary lookup returned null for Plan {PlanId} (CycleDays=0). " +
                    "Attempting Alteracoes.IDHorario fallback for Employee {EmployeeId}, Date {Date:yyyy-MM-dd}",
                    planInfo.PlanId, innuxEmployeeId, date);

                scheduleInfo = await LoadScheduleFromAlteracoesAsync(
                    connection, innuxEmployeeId, date);

                if (scheduleInfo != null)
                {
                    scheduleResolutionSource = "Alteracoes.IDHorario";
                    _logger.LogDebug(
                        "PortalScheduleResolver: Fallback succeeded → Schedule {Code} ({Desc}) from Alteracoes",
                        scheduleInfo.ScheduleCode, scheduleInfo.ScheduleDescription);
                }
            }

            if (scheduleInfo == null)
            {
                _logger.LogWarning(
                    "PortalScheduleResolver: No schedule found for Plan {PlanId} Day {DayIndex} " +
                    "(Employee {EmployeeId}, Date {Date:yyyy-MM-dd})",
                    planInfo.PlanId, resolvedDayIndex, innuxEmployeeId, date);
                return null;
            }

            // ── Step 3: Load schedule periods ──
            var periods = await LoadPeriodsAsync(connection, scheduleInfo.ScheduleId);

            // ── Step 4: Compute derived fields ──
            var mandatoryPeriods = periods.Where(p =>
                p.Type.StartsWith("Obrigat", StringComparison.OrdinalIgnoreCase)).ToList();

            var expectedMinutes = mandatoryPeriods.Sum(p => p.DurationMinutes);

            var isOvernight = !scheduleInfo.IsRestDay && periods.Any(p =>
                p.Type.StartsWith("Obrigat", StringComparison.OrdinalIgnoreCase) &&
                p.IsOvernightPeriod);

            string? expectedStart = mandatoryPeriods
                .OrderBy(p => p.RawStartMinutes)
                .Select(p => p.StartTime)
                .FirstOrDefault();

            string? expectedEnd = mandatoryPeriods
                .OrderByDescending(p => p.RawEndMinutes)
                .Select(p => p.EndTime)
                .FirstOrDefault();

            var result = new ResolvedScheduleDto
            {
                WorkPlanId = planInfo.PlanId,
                WorkPlanCode = planInfo.PlanCode,
                WorkPlanDescription = planInfo.PlanDescription,
                WorkPlanType = planInfo.PlanType,
                CycleDays = planInfo.CycleDays,
                CycleStartDate = planInfo.CycleStartDate,
                ResolvedDayIndex = resolvedDayIndex,
                ScheduleId = scheduleInfo.ScheduleId,
                ScheduleCode = scheduleInfo.ScheduleCode,
                ScheduleDescription = scheduleInfo.ScheduleDescription,
                ScheduleSigla = scheduleInfo.ScheduleSigla,
                IsRestDay = scheduleInfo.IsRestDay,
                IsOvernightShift = isOvernight,
                ExpectedStartTime = expectedStart,
                ExpectedEndTime = expectedEnd,
                ExpectedMinutes = expectedMinutes,
                ScheduleResolutionSource = scheduleResolutionSource,
                Periods = periods.Select(p => new ResolvedPeriodDto
                {
                    Type = p.Type,
                    StartTime = p.StartTime,
                    EndTime = p.EndTime,
                    DurationMinutes = p.DurationMinutes,
                    ToleranceEntryMinutes = p.ToleranceEntryMinutes,
                    ToleranceExitMinutes = p.ToleranceExitMinutes,
                    WorkCodeDescription = p.WorkCodeDescription
                }).ToList()
            };

            _logger.LogDebug(
                "PortalScheduleResolver: Resolved → Schedule {ScheduleCode} ({ScheduleDesc}), " +
                "RestDay={IsRestDay}, Overnight={IsOvernight}, ExpectedMinutes={ExpMin}, " +
                "ExpectedStart={Start}, ExpectedEnd={End}, Periods={PeriodCount}",
                result.ScheduleCode, result.ScheduleDescription,
                result.IsRestDay, result.IsOvernightShift, result.ExpectedMinutes,
                result.ExpectedStartTime, result.ExpectedEndTime, result.Periods.Count);

            return result;
        }
        catch (InvalidOperationException)
        {
            throw; // Connection configuration errors — rethrow
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PortalScheduleResolver: Failed to resolve schedule for Employee {EmployeeId}, Date {Date:yyyy-MM-dd}",
                innuxEmployeeId, date);
            throw;
        }
    }

    // ─── Private Data Access ───

    private async Task<PlanInfoInternal?> LoadEmployeePlanAsync(SqlConnection connection, int employeeId)
    {
        var query = @"
            SELECT
                f.IDPlanoTrabalho,
                pt.Codigo,
                pt.Descricao,
                pt.Tipo,
                pt.NumeroDias,
                pt.DataInicio
            FROM dbo.Funcionarios f
            INNER JOIN dbo.PlanosTrabalho pt ON f.IDPlanoTrabalho = pt.IDPlanoTrabalho
            WHERE f.IDFuncionario = @EmployeeId
              AND f.Activo = 1";

        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@EmployeeId", employeeId);
        cmd.CommandTimeout = 15;

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new PlanInfoInternal
        {
            PlanId = SafeInt(reader["IDPlanoTrabalho"]),
            PlanCode = reader["Codigo"]?.ToString()?.Trim() ?? "",
            PlanDescription = reader["Descricao"]?.ToString()?.Trim() ?? "",
            PlanType = reader["Tipo"]?.ToString()?.Trim() ?? "Desconhecido",
            CycleDays = SafeInt(reader["NumeroDias"]),
            CycleStartDate = reader["DataInicio"] is DateTime dt ? dt : null
        };
    }

    private async Task<ScheduleInfoInternal?> LoadScheduleForDayAsync(
        SqlConnection connection, int planId, int dayIndex)
    {
        var query = @"
            SELECT
                h.IDHorario,
                h.Codigo,
                h.Descricao,
                h.Sigla,
                ISNULL(h.DiaFolga, 0) AS DiaFolga
            FROM dbo.PlanosTrabalhoHorarios pth
            INNER JOIN dbo.Horarios h ON pth.IDHorario = h.IDHorario
            WHERE pth.IDPlanoTrabalho = @PlanId
              AND pth.Dia = @DayIndex";

        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@PlanId", planId);
        cmd.Parameters.AddWithValue("@DayIndex", dayIndex);
        cmd.CommandTimeout = 15;

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new ScheduleInfoInternal
        {
            ScheduleId = SafeInt(reader["IDHorario"]),
            ScheduleCode = reader["Codigo"]?.ToString()?.Trim() ?? "",
            ScheduleDescription = reader["Descricao"]?.ToString()?.Trim() ?? "",
            ScheduleSigla = reader["Sigla"]?.ToString()?.Trim(),
            IsRestDay = Convert.ToBoolean(reader["DiaFolga"])
        };
    }

    /// <summary>
    /// Fallback schedule resolution for Escala-type plans.
    /// Reads the schedule assignment from Alteracoes.IDHorario for a specific employee/date.
    /// This covers employees whose daily schedule is assigned by Innux processing
    /// rather than through the PlanosTrabalhoHorarios cycle table.
    /// Read-only: SELECT only.
    /// </summary>
    private async Task<ScheduleInfoInternal?> LoadScheduleFromAlteracoesAsync(
        SqlConnection connection, int employeeId, DateTime date)
    {
        var query = @"
            SELECT TOP 1
                h.IDHorario,
                h.Codigo,
                h.Descricao,
                h.Sigla,
                ISNULL(h.DiaFolga, 0) AS DiaFolga
            FROM dbo.Alteracoes a
            INNER JOIN dbo.Horarios h ON a.IDHorario = h.IDHorario
            WHERE a.IDFuncionario = @EmployeeId
              AND CAST(a.Data AS DATE) = @Date
              AND a.IDHorario IS NOT NULL
              AND a.IDHorario > 0";

        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@EmployeeId", employeeId);
        cmd.Parameters.AddWithValue("@Date", date.Date);
        cmd.CommandTimeout = 15;

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new ScheduleInfoInternal
        {
            ScheduleId = SafeInt(reader["IDHorario"]),
            ScheduleCode = reader["Codigo"]?.ToString()?.Trim() ?? "",
            ScheduleDescription = reader["Descricao"]?.ToString()?.Trim() ?? "",
            ScheduleSigla = reader["Sigla"]?.ToString()?.Trim(),
            IsRestDay = Convert.ToBoolean(reader["DiaFolga"])
        };
    }

    private async Task<List<PeriodInfoInternal>> LoadPeriodsAsync(SqlConnection connection, int scheduleId)
    {
        var query = @"
            SELECT
                hp.Tipo,
                hp.Inicio,
                hp.Fim,
                hp.ToleranciaEntrada,
                hp.ToleranciaSaida,
                ct.Descricao AS WorkCodeDescription
            FROM dbo.HorariosPeriodos hp
            LEFT JOIN dbo.CodigosTrabalho ct ON hp.IDCodigoTrabalho = ct.IDCodigoTrabalho
            WHERE hp.IDHorario = @ScheduleId
            ORDER BY hp.Inicio";

        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@ScheduleId", scheduleId);
        cmd.CommandTimeout = 15;

        var results = new List<PeriodInfoInternal>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var startDt = reader["Inicio"] as DateTime?;
            var endDt = reader["Fim"] as DateTime?;
            var tolEntryDt = reader["ToleranciaEntrada"] as DateTime?;
            var tolExitDt = reader["ToleranciaSaida"] as DateTime?;

            var startMinutes = startDt.HasValue ? startDt.Value.Hour * 60 + startDt.Value.Minute : 0;
            var endMinutes = endDt.HasValue ? endDt.Value.Hour * 60 + endDt.Value.Minute : 0;

            // Detect overnight: Innux stores next-day times with date component > 1900-01-01
            var isOvernight = endDt.HasValue && endDt.Value.Date > new DateTime(1900, 1, 1);
            if (isOvernight)
            {
                // Next-day end time: add 24 hours worth of minutes
                endMinutes += 24 * 60;
            }

            var durationMinutes = endMinutes - startMinutes;
            if (durationMinutes < 0) durationMinutes += 24 * 60; // safety wrap

            results.Add(new PeriodInfoInternal
            {
                Type = reader["Tipo"]?.ToString()?.Trim() ?? "Desconhecido",
                StartTime = startDt?.ToString("HH:mm") ?? "--:--",
                EndTime = endDt?.ToString("HH:mm") ?? "--:--",
                RawStartMinutes = startMinutes,
                RawEndMinutes = endMinutes,
                DurationMinutes = durationMinutes,
                IsOvernightPeriod = isOvernight,
                ToleranceEntryMinutes = ToMinutes(tolEntryDt),
                ToleranceExitMinutes = ToMinutes(tolExitDt),
                WorkCodeDescription = reader["WorkCodeDescription"]?.ToString()?.Trim()
            });
        }

        return results;
    }

    // ─── Helpers ───

    private static int SafeInt(object? value)
    {
        if (value == null || value == DBNull.Value) return 0;
        return Convert.ToInt32(value);
    }

    private static int ToMinutes(DateTime? dt)
    {
        if (!dt.HasValue) return 0;
        return dt.Value.Hour * 60 + dt.Value.Minute;
    }

    // ─── Internal Models (not exposed) ───

    private sealed class PlanInfoInternal
    {
        public int PlanId { get; init; }
        public string PlanCode { get; init; } = "";
        public string PlanDescription { get; init; } = "";
        public string PlanType { get; init; } = "";
        public int CycleDays { get; init; }
        public DateTime? CycleStartDate { get; init; }
    }

    private sealed class ScheduleInfoInternal
    {
        public int ScheduleId { get; init; }
        public string ScheduleCode { get; init; } = "";
        public string ScheduleDescription { get; init; } = "";
        public string? ScheduleSigla { get; init; }
        public bool IsRestDay { get; init; }
    }

    private sealed class PeriodInfoInternal
    {
        public string Type { get; init; } = "";
        public string StartTime { get; init; } = "";
        public string EndTime { get; init; } = "";
        public int RawStartMinutes { get; init; }
        public int RawEndMinutes { get; init; }
        public int DurationMinutes { get; init; }
        public bool IsOvernightPeriod { get; init; }
        public int ToleranceEntryMinutes { get; init; }
        public int ToleranceExitMinutes { get; init; }
        public string? WorkCodeDescription { get; init; }
    }
}
