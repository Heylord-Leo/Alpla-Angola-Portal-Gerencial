using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Primavera employee lookup — read-only, company-aware.
///
/// Uses PrimaveraConnectionFactory to resolve connections for the requested
/// company/database target. Every call requires an explicit PrimaveraCompany.
///
/// This service handles employee-specific queries only.
/// Connection resolution, credential validation, and database targeting
/// are delegated to the shared PrimaveraConnectionFactory.
///
/// Schema-aware: Before querying identity document columns (NumBI, Nacionalidade,
/// NumPassaporte), the service checks INFORMATION_SCHEMA.COLUMNS to confirm
/// they exist in the target database. Results are cached per database for the
/// lifetime of the application.
/// </summary>
public class PrimaveraEmployeeService : IPrimaveraEmployeeService
{
    private readonly PrimaveraConnectionFactory _connectionFactory;
    private readonly ILogger<PrimaveraEmployeeService> _logger;

    /// <summary>
    /// Cached per-database column availability for the Funcionarios table.
    /// Key = database name (connection.Database), Value = set of column names present.
    /// </summary>
    private static readonly ConcurrentDictionary<string, HashSet<string>> _columnCache = new();

    /// <summary>HR identity document columns that may or may not exist in a given deployment.</summary>
    private static readonly string[] OptionalHrColumns = ["NumBI", "Nacionalidade", "NumPassaporte"];

    public PrimaveraEmployeeService(
        PrimaveraConnectionFactory connectionFactory,
        ILogger<PrimaveraEmployeeService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<PrimaveraEmployeeDto?> GetEmployeeByCodeAsync(PrimaveraCompany company, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(company);
            var availableCols = await GetAvailableColumnsAsync(connection);

            var extraSelect = BuildOptionalSelectClause(availableCols);

            var query = $@"
                SELECT TOP 1 
                    f.Codigo, 
                    f.Nome, 
                    f.NomeAbreviado, 
                    f.PrimeiroNome, 
                    f.PrimeiroApelido, 
                    f.SegundoApelido, 
                    f.Email, 
                    f.Telefone, 
                    f.Telemovel, 
                    f.CodDepartamento, 
                    f.DataAdmissao, 
                    f.DataDemissao, 
                    f.InactivoTemp,
                    d.Descricao as DepartmentName{extraSelect}
                FROM Funcionarios f 
                LEFT JOIN Departamentos d ON f.CodDepartamento = d.Departamento 
                WHERE f.Codigo = @Code";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Code", code);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapToDto(reader, company, availableCols);
            }

            return null;
        }
        catch (InvalidOperationException)
        {
            // Configuration errors — rethrow as-is for the controller to handle
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Primavera employee {Code} from {Company}", code, company);
            throw;
        }
    }

    public async Task<IEnumerable<PrimaveraEmployeeDto>> SearchEmployeesByNameAsync(
        PrimaveraCompany company, string nameQuery, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(nameQuery) || nameQuery.Trim().Length < 2)
            throw new ArgumentException("Search query must be at least 2 characters long.");

        if (limit <= 0 || limit > 100)
            limit = 50;

        var employees = new List<PrimaveraEmployeeDto>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(company);
            var availableCols = await GetAvailableColumnsAsync(connection);

            var extraSelect = BuildOptionalSelectClause(availableCols);

            var query = $@"
                SELECT TOP {limit} 
                    f.Codigo, 
                    f.Nome, 
                    f.NomeAbreviado, 
                    f.PrimeiroNome, 
                    f.PrimeiroApelido, 
                    f.SegundoApelido, 
                    f.Email, 
                    f.Telefone, 
                    f.Telemovel, 
                    f.CodDepartamento, 
                    f.DataAdmissao, 
                    f.DataDemissao, 
                    f.InactivoTemp,
                    d.Descricao as DepartmentName{extraSelect}
                FROM Funcionarios f 
                LEFT JOIN Departamentos d ON f.CodDepartamento = d.Departamento 
                WHERE f.Nome LIKE @Query 
                ORDER BY f.Nome ASC";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Query", $"%{nameQuery.Trim()}%");

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                employees.Add(MapToDto(reader, company, availableCols));
            }

            return employees;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Primavera employees with query '{Query}' on {Company}", nameQuery, company);
            throw;
        }
    }

    // ─── Schema-Aware Column Detection ───

    /// <summary>
    /// Returns the set of optional HR columns that exist in the Funcionarios table
    /// for the current database. Results are cached per database name.
    /// </summary>
    private async Task<HashSet<string>> GetAvailableColumnsAsync(SqlConnection connection)
    {
        var dbName = connection.Database;
        if (_columnCache.TryGetValue(dbName, out var cached))
            return cached;

        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var query = @"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'Funcionarios' 
                  AND COLUMN_NAME IN ('NumBI', 'Nacionalidade', 'NumPassaporte')";

            await using var cmd = new SqlCommand(query, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                available.Add(reader.GetString(0));
            }

            _logger.LogInformation(
                "Primavera schema detection for {Database}: Funcionarios optional columns found: [{Columns}]",
                dbName,
                available.Count > 0 ? string.Join(", ", available) : "none");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to detect optional HR columns in {Database}. Proceeding without identity document fields.",
                dbName);
        }

        _columnCache[dbName] = available;
        return available;
    }

    /// <summary>
    /// Builds the extra SELECT clause fragment for optional columns,
    /// only including columns that actually exist in the schema.
    /// Returns empty string if no optional columns are available.
    /// </summary>
    private static string BuildOptionalSelectClause(HashSet<string> availableCols)
    {
        var parts = new List<string>();

        foreach (var col in OptionalHrColumns)
        {
            if (availableCols.Contains(col))
            {
                parts.Add($"f.{col}");
            }
        }

        return parts.Count > 0 ? ", " + string.Join(", ", parts) : "";
    }

    // ─── DTO Mapping ───

    private static PrimaveraEmployeeDto MapToDto(
        SqlDataReader reader, PrimaveraCompany company, HashSet<string> availableCols)
    {
        var dto = new PrimaveraEmployeeDto
        {
            Code = reader["Codigo"]?.ToString() ?? "",
            Name = reader["Nome"]?.ToString() ?? "",
            ShortName = reader["NomeAbreviado"] as string,
            FirstName = reader["PrimeiroNome"] as string,
            FirstLastName = reader["PrimeiroApelido"] as string,
            SecondLastName = reader["SegundoApelido"] as string,
            Email = reader["Email"] as string,
            Phone = reader["Telefone"] as string,
            Mobile = reader["Telemovel"] as string,
            DepartmentCode = reader["CodDepartamento"] as string,
            DepartmentName = reader["DepartmentName"] as string,
            HireDate = reader["DataAdmissao"] as DateTime?,
            TerminationDate = reader["DataDemissao"] as DateTime?,
            IsTemporarilyInactive = reader["InactivoTemp"] as bool?,
            SourceCompany = company.ToString()
        };

        // Map optional identity document columns — only read if present in schema
        if (availableCols.Contains("NumBI"))
            dto.IdentityDocNumber = reader["NumBI"] as string;

        if (availableCols.Contains("Nacionalidade"))
            dto.Nationality = reader["Nacionalidade"] as string;

        if (availableCols.Contains("NumPassaporte"))
            dto.PassportNumber = reader["NumPassaporte"] as string;

        return dto;
    }
}
