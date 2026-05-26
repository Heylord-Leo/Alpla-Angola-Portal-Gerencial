using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Read-only Primavera plant suggestion resolver.
///
/// For each unmapped HREmployee (no PlantId), looks up the employee's code
/// across all configured Primavera company databases to determine which
/// company the employee belongs to.
///
/// Inference rules (approved):
/// - ALPLASOPRO match → Confidence: High → Viana 3 (direct 1:1)
/// - ALPLAPLASTICO match → Confidence: Ambiguous → Viana 1 or Viana 2 (user must choose)
/// - Multiple company matches → Confidence: Ambiguous
/// - No match → Confidence: NotFound
///
/// This service NEVER:
/// - Writes to Primavera databases
/// - Modifies confirmed Portal mapping fields (PlantId, DepartmentMasterId, ManagerUserId)
/// - Uses Cost Center fields (deferred to future improvement)
/// - Auto-assigns PlantId based on suggestions
///
/// It ONLY updates the advisory suggestion fields:
/// SuggestedPlantSource, SuggestedPlantReason, SuggestedPlantConfidence, SuggestedPlantResolvedAtUtc
/// </summary>
public class PrimaveraPlantSuggestionService : IPrimaveraPlantSuggestionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly PrimaveraConnectionFactory _connectionFactory;
    private readonly ILogger<PrimaveraPlantSuggestionService> _logger;

    public PrimaveraPlantSuggestionService(
        ApplicationDbContext dbContext,
        PrimaveraConnectionFactory connectionFactory,
        ILogger<PrimaveraPlantSuggestionService> logger)
    {
        _dbContext = dbContext;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<PlantSuggestionResult> ResolveSuggestionsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new PlantSuggestionResult();

        // 1. Get all employees that need suggestion resolution:
        //    - No confirmed PlantId (unmapped for plant)
        //    - OR suggestion never resolved (SuggestedPlantConfidence is null)
        var employees = await _dbContext.HREmployees
            .Where(e => e.IsActive &&
                        (e.PlantId == null || e.SuggestedPlantConfidence == null))
            .ToListAsync(cancellationToken);

        if (!employees.Any())
        {
            _logger.LogInformation("PrimaveraPlantSuggestion: no employees require suggestion resolution.");
            return result;
        }

        _logger.LogInformation(
            "PrimaveraPlantSuggestion: resolving suggestions for {Count} employees.",
            employees.Count);

        // 2. Build a lookup of employee codes across all configured Primavera companies
        var companies = _connectionFactory.GetConfiguredCompanies();
        var companyLookups = new Dictionary<PrimaveraCompany, HashSet<string>>();

        foreach (var company in companies)
        {
            try
            {
                var codes = await FetchEmployeeCodesAsync(company, cancellationToken);
                companyLookups[company] = codes;
                _logger.LogInformation(
                    "PrimaveraPlantSuggestion: loaded {Count} employee codes from {Company}.",
                    codes.Count, company);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "PrimaveraPlantSuggestion: failed to load codes from {Company}. Skipping this company.",
                    company);
                result.Errors++;
                result.ErrorMessages.Add($"Failed to connect to {company}: {ex.Message}");
            }
        }

        if (!companyLookups.Any())
        {
            _logger.LogWarning("PrimaveraPlantSuggestion: no Primavera companies available. Aborting.");
            return result;
        }

        // 3. Resolve each employee
        foreach (var employee in employees)
        {
            try
            {
                result.TotalProcessed++;

                // Skip employees that already have a confirmed plant
                if (employee.PlantId.HasValue)
                {
                    // Still resolve suggestion if not yet done, for informational purposes
                    if (employee.SuggestedPlantConfidence != null)
                    {
                        result.AlreadyMapped++;
                        continue;
                    }
                }

                var code = employee.EmployeeCode?.Trim();
                if (string.IsNullOrWhiteSpace(code))
                {
                    SetSuggestion(employee, "PRIMAVERA:NO_CODE", "NotFound",
                        "Funcionário sem código — não é possível consultar o Primavera.");
                    result.NotFound++;
                    continue;
                }

                // Find which companies contain this employee code
                var matchedCompanies = companyLookups
                    .Where(kv => kv.Value.Contains(code))
                    .Select(kv => kv.Key)
                    .ToList();

                if (matchedCompanies.Count == 0)
                {
                    // Not found in any Primavera database
                    SetSuggestion(employee, "PRIMAVERA:NOT_FOUND", "NotFound",
                        "Código do funcionário não encontrado em nenhuma base Primavera.");
                    result.NotFound++;
                }
                else if (matchedCompanies.Count == 1)
                {
                    var company = matchedCompanies[0];

                    if (company == PrimaveraCompany.ALPLASOPRO)
                    {
                        // High confidence: ALPLASOPRO → Viana 3
                        SetSuggestion(employee, "PRIMAVERA:ALPLASOPRO", "High",
                            "Funcionário encontrado na base ALPLASOPRO — corresponde a Viana 3.");
                        result.HighConfidence++;
                    }
                    else if (company == PrimaveraCompany.ALPLAPLASTICO)
                    {
                        // Ambiguous: ALPLAPLASTICO → Viana 1 or Viana 2
                        SetSuggestion(employee, "PRIMAVERA:ALPLAPLASTICO", "Ambiguous",
                            "Funcionário encontrado na base ALPLAPLASTICO — pode pertencer a Viana 1 ou Viana 2. Seleção manual necessária.");
                        result.Ambiguous++;
                    }
                    else
                    {
                        // Unknown company — treat as ambiguous
                        SetSuggestion(employee, $"PRIMAVERA:{company}", "Ambiguous",
                            $"Funcionário encontrado na base {company}. Planta não determinada automaticamente.");
                        result.Ambiguous++;
                    }
                }
                else
                {
                    // Found in multiple companies
                    var companyNames = string.Join(", ", matchedCompanies.Select(c => c.ToString()));
                    SetSuggestion(employee, "PRIMAVERA:MULTI", "Ambiguous",
                        $"Funcionário encontrado em múltiplas bases Primavera ({companyNames}). Seleção manual necessária.");
                    result.Ambiguous++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "PrimaveraPlantSuggestion: error resolving employee {Id} ({Code}).",
                    employee.Id, employee.EmployeeCode);
                result.Errors++;
            }
        }

        // 4. Save all changes
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "PrimaveraPlantSuggestion: completed. Processed: {Total}, High: {High}, Ambiguous: {Ambiguous}, NotFound: {NotFound}, AlreadyMapped: {Mapped}, Errors: {Errors}",
                result.TotalProcessed, result.HighConfidence, result.Ambiguous,
                result.NotFound, result.AlreadyMapped, result.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PrimaveraPlantSuggestion: failed to save suggestion results.");
            result.Errors++;
            result.ErrorMessages.Add($"Failed to save: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Fetches all active employee codes (Funcionarios.Codigo) from a Primavera company database.
    /// Read-only, parameterless query — just retrieves the code set for membership checks.
    /// </summary>
    private async Task<HashSet<string>> FetchEmployeeCodesAsync(
        PrimaveraCompany company, CancellationToken ct)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var connection = await _connectionFactory.CreateConnectionAsync(company, ct);

        var query = @"
            SELECT Codigo 
            FROM Funcionarios 
            WHERE Codigo IS NOT NULL AND Codigo <> ''";

        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var code = reader["Codigo"]?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(code))
                codes.Add(code);
        }

        return codes;
    }

    /// <summary>
    /// Sets the advisory suggestion fields on an HREmployee.
    /// Never touches PlantId or other confirmed mapping fields.
    /// </summary>
    private static void SetSuggestion(
        Domain.Entities.HREmployee employee,
        string source, string confidence, string reason)
    {
        employee.SuggestedPlantSource = source;
        employee.SuggestedPlantConfidence = confidence;
        employee.SuggestedPlantReason = reason;
        employee.SuggestedPlantResolvedAtUtc = DateTime.UtcNow;
    }
}
