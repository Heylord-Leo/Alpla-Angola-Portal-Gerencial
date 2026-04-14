using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

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
/// </summary>
public class PrimaveraEmployeeService : IPrimaveraEmployeeService
{
    private readonly PrimaveraConnectionFactory _connectionFactory;
    private readonly ILogger<PrimaveraEmployeeService> _logger;

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

            var query = @"
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
                    d.Descricao as DepartmentName 
                FROM Funcionarios f 
                LEFT JOIN Departamentos d ON f.CodDepartamento = d.Departamento 
                WHERE f.Codigo = @Code";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Code", code);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapToDto(reader, company);
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
                    d.Descricao as DepartmentName 
                FROM Funcionarios f 
                LEFT JOIN Departamentos d ON f.CodDepartamento = d.Departamento 
                WHERE f.Nome LIKE @Query 
                ORDER BY f.Nome ASC";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Query", $"%{nameQuery.Trim()}%");

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                employees.Add(MapToDto(reader, company));
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

    private static PrimaveraEmployeeDto MapToDto(SqlDataReader reader, PrimaveraCompany company)
    {
        return new PrimaveraEmployeeDto
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
    }
}
