using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Innux schedule/shift/work plan reference data — read-only.
///
/// Queries dbo.PlanosTrabalho, dbo.PlanosTrabalhoHorarios, dbo.Horarios,
/// dbo.HorariosPeriodos, dbo.CodigosTrabalho, and dbo.Funcionarios.
/// Results are cached with 1-hour TTL.
///
/// Read-only: SELECT only, parameterized queries. No writes to Innux.
/// </summary>
public class InnuxScheduleService : IInnuxScheduleService
{
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InnuxScheduleService> _logger;

    private const string WorkPlansCacheKey = "innux:schedules:workPlans";
    private const string SchedulesCacheKey = "innux:schedules:schedules";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public InnuxScheduleService(
        InnuxConnectionFactory connectionFactory,
        IMemoryCache cache,
        ILogger<InnuxScheduleService> logger)
    {
        _connectionFactory = connectionFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WorkPlanListItemDto>> GetWorkPlansAsync()
    {
        if (_cache.TryGetValue(WorkPlansCacheKey, out IEnumerable<WorkPlanListItemDto>? cached) && cached != null)
            return cached;

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            // Step 1: Load work plans with employee counts and rest-day schedule info
            var planQuery = @"
                SELECT
                    pt.IDPlanoTrabalho,
                    pt.Codigo,
                    pt.Descricao,
                    pt.Tipo,
                    pt.NumeroDias,
                    hf.Descricao AS RestScheduleDescricao,
                    hf.Sigla AS RestScheduleSigla,
                    pt.IDEntidade,
                    (SELECT COUNT(*) FROM dbo.Funcionarios f WHERE f.IDPlanoTrabalho = pt.IDPlanoTrabalho AND f.Activo = 1) AS EmployeeCount
                FROM dbo.PlanosTrabalho pt
                LEFT JOIN dbo.Horarios hf ON pt.IDFolgas = hf.IDHorario
                ORDER BY pt.Codigo";

            await using var planCmd = new SqlCommand(planQuery, connection);
            planCmd.CommandTimeout = 30;

            var plans = new List<WorkPlanListItemDto>();
            await using var planReader = await planCmd.ExecuteReaderAsync();

            while (await planReader.ReadAsync())
            {
                plans.Add(new WorkPlanListItemDto
                {
                    Id = SafeInt(planReader["IDPlanoTrabalho"]),
                    Code = planReader["Codigo"]?.ToString()?.Trim() ?? "",
                    Description = planReader["Descricao"]?.ToString()?.Trim() ?? "",
                    Type = planReader["Tipo"]?.ToString()?.Trim() ?? "Desconhecido",
                    CycleDays = SafeInt(planReader["NumeroDias"]),
                    RestScheduleDescription = planReader["RestScheduleDescricao"]?.ToString()?.Trim(),
                    RestScheduleSigla = planReader["RestScheduleSigla"]?.ToString()?.Trim(),
                    EntityId = SafeInt(planReader["IDEntidade"]),
                    EntityName = MapEntityName(SafeInt(planReader["IDEntidade"])),
                    EmployeeCount = SafeInt(planReader["EmployeeCount"])
                });
            }

            await planReader.CloseAsync();

            // Step 2: Load cycle composition for all plans at once
            var cycleQuery = @"
                SELECT
                    pth.IDPlanoTrabalho,
                    pth.Dia,
                    pth.IDHorario,
                    h.Codigo,
                    h.Descricao,
                    h.Sigla,
                    h.DiaFolga,
                    h.HorasNormais
                FROM dbo.PlanosTrabalhoHorarios pth
                LEFT JOIN dbo.Horarios h ON pth.IDHorario = h.IDHorario
                ORDER BY pth.IDPlanoTrabalho, pth.Dia";

            await using var cycleCmd = new SqlCommand(cycleQuery, connection);
            cycleCmd.CommandTimeout = 30;

            var cycleDays = new Dictionary<int, List<PlanCycleDayDto>>();
            await using var cycleReader = await cycleCmd.ExecuteReaderAsync();

            while (await cycleReader.ReadAsync())
            {
                var planId = SafeInt(cycleReader["IDPlanoTrabalho"]);
                if (!cycleDays.ContainsKey(planId))
                    cycleDays[planId] = new List<PlanCycleDayDto>();

                cycleDays[planId].Add(new PlanCycleDayDto
                {
                    DayIndex = SafeInt(cycleReader["Dia"]),
                    ScheduleId = SafeInt(cycleReader["IDHorario"]),
                    ScheduleCode = cycleReader["Codigo"]?.ToString()?.Trim() ?? "",
                    ScheduleDescription = cycleReader["Descricao"]?.ToString()?.Trim() ?? "",
                    ScheduleSigla = cycleReader["Sigla"]?.ToString()?.Trim(),
                    IsRestDay = SafeBool(cycleReader["DiaFolga"]),
                    NormalHours = FormatInnuxDuration(cycleReader["HorasNormais"])
                });
            }

            // Attach cycle composition to plans
            foreach (var plan in plans)
            {
                plan.CycleDays_Detail = cycleDays.TryGetValue(plan.Id, out var days)
                    ? days
                    : new List<PlanCycleDayDto>();
            }

            _cache.Set(WorkPlansCacheKey, (IEnumerable<WorkPlanListItemDto>)plans, CacheDuration);
            _logger.LogDebug("InnuxScheduleService: loaded {Count} work plans", plans.Count);

            return plans;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Innux work plans");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ScheduleListItemDto>> GetSchedulesAsync()
    {
        if (_cache.TryGetValue(SchedulesCacheKey, out IEnumerable<ScheduleListItemDto>? cached) && cached != null)
            return cached;

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            // Step 1: Load schedule headers
            var scheduleQuery = @"
                SELECT
                    h.IDHorario,
                    h.Codigo,
                    h.Descricao,
                    h.Sigla,
                    h.HorasNormais,
                    h.DiaFolga,
                    h.HorarioComoFolga,
                    h.NumeroMarcacoes,
                    h.NumeroMinimoMarcacoes,
                    h.ToleranciaMaximaDiaria,
                    h.IDEntidade
                FROM dbo.Horarios h
                ORDER BY h.Codigo";

            await using var schedCmd = new SqlCommand(scheduleQuery, connection);
            schedCmd.CommandTimeout = 30;

            var schedules = new List<ScheduleListItemDto>();
            await using var schedReader = await schedCmd.ExecuteReaderAsync();

            while (await schedReader.ReadAsync())
            {
                schedules.Add(new ScheduleListItemDto
                {
                    Id = SafeInt(schedReader["IDHorario"]),
                    Code = schedReader["Codigo"]?.ToString()?.Trim() ?? "",
                    Description = schedReader["Descricao"]?.ToString()?.Trim() ?? "",
                    Sigla = schedReader["Sigla"]?.ToString()?.Trim(),
                    NormalHours = FormatInnuxDuration(schedReader["HorasNormais"]),
                    IsRestDay = SafeBool(schedReader["DiaFolga"]),
                    IsScheduleAsRestDay = SafeBool(schedReader["HorarioComoFolga"]),
                    ExpectedPunches = SafeInt(schedReader["NumeroMarcacoes"]),
                    MinPunches = SafeInt(schedReader["NumeroMinimoMarcacoes"]),
                    MaxDailyToleranceMinutes = FormatInnuxToleranceMinutes(schedReader["ToleranciaMaximaDiaria"]),
                    EntityId = SafeInt(schedReader["IDEntidade"]),
                    EntityName = MapEntityName(SafeInt(schedReader["IDEntidade"]))
                });
            }

            await schedReader.CloseAsync();

            // Step 2: Load all periods for all schedules
            var periodQuery = @"
                SELECT
                    hp.IDHorario,
                    hp.Tipo,
                    hp.Inicio,
                    hp.Fim,
                    hp.ToleranciaEntrada,
                    hp.ToleranciaSaida,
                    hp.Arredondar,
                    ct.Descricao AS WorkCodeDescription
                FROM dbo.HorariosPeriodos hp
                LEFT JOIN dbo.CodigosTrabalho ct ON hp.IDCodigoTrabalho = ct.IDCodigoTrabalho
                ORDER BY hp.IDHorario, hp.Inicio";

            await using var periodCmd = new SqlCommand(periodQuery, connection);
            periodCmd.CommandTimeout = 30;

            var periods = new Dictionary<int, List<SchedulePeriodDto>>();
            await using var periodReader = await periodCmd.ExecuteReaderAsync();

            while (await periodReader.ReadAsync())
            {
                var schedId = SafeInt(periodReader["IDHorario"]);
                if (!periods.ContainsKey(schedId))
                    periods[schedId] = new List<SchedulePeriodDto>();

                periods[schedId].Add(new SchedulePeriodDto
                {
                    Type = periodReader["Tipo"]?.ToString()?.Trim() ?? "Desconhecido",
                    StartTime = FormatInnuxTime(periodReader["Inicio"]),
                    EndTime = FormatInnuxTime(periodReader["Fim"]),
                    ToleranceEntryMinutes = FormatInnuxToleranceMinutes(periodReader["ToleranciaEntrada"]) ?? 0,
                    ToleranceExitMinutes = FormatInnuxToleranceMinutes(periodReader["ToleranciaSaida"]) ?? 0,
                    RoundingMinutes = FormatInnuxToleranceMinutes(periodReader["Arredondar"]) ?? 0,
                    WorkCodeDescription = periodReader["WorkCodeDescription"]?.ToString()?.Trim()
                });
            }

            // Attach periods to schedules
            foreach (var schedule in schedules)
            {
                schedule.Periods = periods.TryGetValue(schedule.Id, out var p)
                    ? p
                    : new List<SchedulePeriodDto>();
            }

            _cache.Set(SchedulesCacheKey, (IEnumerable<ScheduleListItemDto>)schedules, CacheDuration);
            _logger.LogDebug("InnuxScheduleService: loaded {Count} schedules", schedules.Count);

            return schedules;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Innux schedules");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<PlanEmployeeDto>> GetPlanEmployeesAsync(int planId)
    {
        // NOT cached — employee lists can change and are filtered by caller.
        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            var query = @"
                SELECT
                    f.IDFuncionario,
                    f.Nome,
                    d.Descricao AS DepartmentName
                FROM dbo.Funcionarios f
                LEFT JOIN dbo.Departamentos d ON f.IDDepartamento = d.IDDepartamento
                WHERE f.IDPlanoTrabalho = @PlanId
                  AND f.Activo = 1
                ORDER BY f.Nome";

            await using var cmd = new SqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@PlanId", planId);
            cmd.CommandTimeout = 30;

            var results = new List<PlanEmployeeDto>();
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(new PlanEmployeeDto
                {
                    InnuxEmployeeId = SafeInt(reader["IDFuncionario"]),
                    FullName = reader["Nome"]?.ToString()?.Trim() ?? "",
                    DepartmentName = reader["DepartmentName"]?.ToString()?.Trim()
                    // PlantName is enriched at the controller layer from Portal HREmployee data
                });
            }

            _logger.LogDebug("InnuxScheduleService: loaded {Count} employees for plan {PlanId}", results.Count, planId);
            return results;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load employees for plan {PlanId}", planId);
            throw;
        }
    }

    // ─── Helpers ───

    // Tipo and Periodo type are nvarchar in Innux — read as strings directly.
    // No mapping needed — values are already "Padrão", "Escala", "Automático",
    // "Calendar", "Obrigatório", "Opcional", etc.

    /// <summary>
    /// Converts Innux datetime-as-duration (base 1900-01-01) to "HH:mm" time string.
    /// </summary>
    private static string FormatInnuxTime(object? value)
    {
        if (value == null || value == DBNull.Value)
            return "--:--";

        if (value is DateTime dt)
        {
            return dt.ToString("HH:mm");
        }

        return "--:--";
    }

    /// <summary>
    /// Converts Innux datetime-as-duration to formatted hours string like "08:00".
    /// </summary>
    private static string? FormatInnuxDuration(object? value)
    {
        if (value == null || value == DBNull.Value)
            return null;

        if (value is DateTime dt)
        {
            var totalMinutes = dt.Hour * 60 + dt.Minute;
            if (totalMinutes == 0) return null;
            return $"{totalMinutes / 60:D2}:{totalMinutes % 60:D2}";
        }

        return null;
    }

    /// <summary>
    /// Converts Innux datetime-as-duration to integer minutes.
    /// </summary>
    private static int? FormatInnuxToleranceMinutes(object? value)
    {
        if (value == null || value == DBNull.Value)
            return null;

        if (value is DateTime dt)
        {
            var totalMinutes = dt.Hour * 60 + dt.Minute;
            return totalMinutes > 0 ? totalMinutes : null;
        }

        return null;
    }

    private static int SafeInt(object? value)
    {
        if (value == null || value == DBNull.Value) return 0;
        return Convert.ToInt32(value);
    }

    private static bool SafeBool(object? value)
    {
        if (value == null || value == DBNull.Value) return false;
        return Convert.ToBoolean(value);
    }

    /// <summary>
    /// Maps Innux IDEntidade to human-readable plant name.
    /// Known entities: 1 = AlplaPLASTICO, 6 = AlplaSOPRO.
    /// </summary>
    private static string MapEntityName(int entityId) => entityId switch
    {
        1 => "AlplaPLASTICO",
        6 => "AlplaSOPRO",
        _ => $"Entidade {entityId}"
    };
}
