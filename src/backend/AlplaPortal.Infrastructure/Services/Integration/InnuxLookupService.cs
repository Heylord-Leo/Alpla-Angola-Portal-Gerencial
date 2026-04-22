using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Innux lookup/reference data — read-only.
///
/// Provides absence codes (dbo.CodigosAusencia) and work codes (dbo.CodigosTrabalho).
/// These are low-volume reference tables (~30–50 rows each) that rarely change.
/// Results are cached with 1-hour TTL and loaded on first access.
///
/// Read-only: SELECT only, parameterized queries. No writes to Innux.
/// </summary>
public class InnuxLookupService : IInnuxLookupService
{
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InnuxLookupService> _logger;

    private const string AbsenceCodesCacheKey = "innux:lookup:absenceCodes";
    private const string WorkCodesCacheKey = "innux:lookup:workCodes";
    private static readonly TimeSpan LookupCacheDuration = TimeSpan.FromHours(1);

    public InnuxLookupService(
        InnuxConnectionFactory connectionFactory,
        IMemoryCache cache,
        ILogger<InnuxLookupService> logger)
    {
        _connectionFactory = connectionFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<InnuxAbsenceCodeDto>> GetAbsenceCodesAsync()
    {
        if (_cache.TryGetValue(AbsenceCodesCacheKey, out IEnumerable<InnuxAbsenceCodeDto>? cached) && cached != null)
            return cached;

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            var query = @"
                SELECT
                    IDCodigoAusencia,
                    Codigo,
                    Descricao
                FROM dbo.CodigosAusencia
                ORDER BY Codigo";

            await using var command = new SqlCommand(query, connection);
            command.CommandTimeout = 15;

            var results = new List<InnuxAbsenceCodeDto>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var code = reader["Codigo"]?.ToString()?.Trim() ?? "";
                results.Add(new InnuxAbsenceCodeDto
                {
                    Id = InnuxTimeHelper.SafeInt(reader["IDCodigoAusencia"]),
                    Code = code,
                    Description = reader["Descricao"]?.ToString()?.Trim() ?? "",
                    Classification = ClassifyAbsenceCode(code)
                });
            }

            _cache.Set(AbsenceCodesCacheKey, (IEnumerable<InnuxAbsenceCodeDto>)results, LookupCacheDuration);
            _logger.LogDebug("InnuxLookupService: loaded {Count} absence codes", results.Count);

            return results;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Innux absence codes");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<InnuxWorkCodeDto>> GetWorkCodesAsync()
    {
        if (_cache.TryGetValue(WorkCodesCacheKey, out IEnumerable<InnuxWorkCodeDto>? cached) && cached != null)
            return cached;

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            var query = @"
                SELECT
                    IDCodigoTrabalho,
                    Codigo,
                    Descricao
                FROM dbo.CodigosTrabalho
                ORDER BY Codigo";

            await using var command = new SqlCommand(query, connection);
            command.CommandTimeout = 15;

            var results = new List<InnuxWorkCodeDto>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var code = reader["Codigo"]?.ToString()?.Trim() ?? "";
                var desc = reader["Descricao"]?.ToString()?.Trim() ?? "";
                results.Add(new InnuxWorkCodeDto
                {
                    Id = InnuxTimeHelper.SafeInt(reader["IDCodigoTrabalho"]),
                    Code = code,
                    Description = desc,
                    IsOvertime = IsOvertimeCode(code, desc)
                });
            }

            _cache.Set(WorkCodesCacheKey, (IEnumerable<InnuxWorkCodeDto>)results, LookupCacheDuration);
            _logger.LogDebug("InnuxLookupService: loaded {Count} work codes", results.Count);

            return results;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Innux work codes");
            throw;
        }
    }

    // ─── Classification helpers ───

    /// <summary>
    /// Best-effort classification of absence codes.
    /// This is a heuristic based on common Innux code patterns.
    /// May require refinement as we encounter more code values.
    /// </summary>
    private static string ClassifyAbsenceCode(string code)
    {
        var upper = code.ToUpperInvariant();

        // Vacation-related codes
        if (upper.StartsWith("FE") || upper.Contains("FERIAS"))
            return "Vacation";

        // Sick leave
        if (upper.StartsWith("DM") || upper.Contains("DOENCA") || upper.Contains("MEDIC"))
            return "SickLeave";

        // Justified absence
        if (upper.StartsWith("FJ") || upper.Contains("JUSTIF"))
            return "Justified";

        // Unjustified absence
        if (upper.StartsWith("FI") || upper.Contains("INJUSTIF"))
            return "Unjustified";

        return "Other";
    }

    /// <summary>
    /// Best-effort overtime detection from work code.
    /// May require refinement based on actual Innux code semantics.
    /// </summary>
    private static bool IsOvertimeCode(string code, string description)
    {
        var upper = code.ToUpperInvariant();
        var descUpper = description.ToUpperInvariant();

        return upper.Contains("HE") ||
               upper.Contains("OT") ||
               descUpper.Contains("EXTRA") ||
               descUpper.Contains("OVERTIME");
    }
}
