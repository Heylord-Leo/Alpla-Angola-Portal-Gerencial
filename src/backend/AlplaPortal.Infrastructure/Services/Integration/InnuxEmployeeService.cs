using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Innux employee lookup — read-only, single database.
///
/// Uses InnuxConnectionFactory to resolve the SQL connection.
/// Queries dbo.Funcionarios with LEFT JOIN on dbo.Departamentos for
/// department enrichment.
///
/// This service handles employee-identity queries only.
/// Connection resolution, credential validation, and config checks
/// are delegated to the shared InnuxConnectionFactory.
///
/// Scope constraints:
/// - Read-only: SELECT only, parameterized queries
/// - No attendance events, punch history, or terminal data
/// - No writes, sync, or cross-system merge logic
/// - HasPhoto is a boolean presence indicator — never fetches blob data
/// </summary>
public class InnuxEmployeeService : IInnuxEmployeeService
{
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly ILogger<InnuxEmployeeService> _logger;

    public InnuxEmployeeService(
        InnuxConnectionFactory connectionFactory,
        ILogger<InnuxEmployeeService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<InnuxEmployeeDto?> GetEmployeeByNumberAsync(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
            return null;

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            var query = @"
                SELECT TOP 1 
                    f.IDFuncionario, f.Numero, f.Nome, f.NomeAbreviado,
                    f.Email, f.Telefone, f.Telemovel,
                    f.IDDepartamento, f.Cartao, f.Activo, f.LoginAD,
                    f.DataAdmissao, f.DataDemissao, f.Categoria, f.CentroCusto,
                    CASE WHEN f.Fotografia IS NOT NULL THEN 1 ELSE 0 END AS HasPhoto,
                    d.Descricao AS DepartmentName
                FROM dbo.Funcionarios f
                LEFT JOIN dbo.Departamentos d ON f.IDDepartamento = d.IDDepartamento
                WHERE f.Numero = @Number";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Number", number.Trim());

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapToDto(reader);
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
            _logger.LogError(ex, "Failed to fetch Innux employee by number {Number}", number);
            throw;
        }
    }

    public async Task<IEnumerable<InnuxEmployeeDto>> SearchEmployeesByNameAsync(
        string nameQuery, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(nameQuery) || nameQuery.Trim().Length < 2)
            throw new ArgumentException("Search query must be at least 2 characters long.");

        if (limit <= 0 || limit > 50)
            limit = 50;

        var employees = new List<InnuxEmployeeDto>();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            var query = $@"
                SELECT TOP {limit} 
                    f.IDFuncionario, f.Numero, f.Nome, f.NomeAbreviado,
                    f.Email, f.Telefone, f.Telemovel,
                    f.IDDepartamento, f.Cartao, f.Activo, f.LoginAD,
                    f.DataAdmissao, f.DataDemissao, f.Categoria, f.CentroCusto,
                    CASE WHEN f.Fotografia IS NOT NULL THEN 1 ELSE 0 END AS HasPhoto,
                    d.Descricao AS DepartmentName
                FROM dbo.Funcionarios f
                LEFT JOIN dbo.Departamentos d ON f.IDDepartamento = d.IDDepartamento
                WHERE f.Nome LIKE @Query
                ORDER BY f.Nome ASC";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Query", $"%{nameQuery.Trim()}%");

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                employees.Add(MapToDto(reader));
            }

            return employees;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Innux employees with query '{Query}'", nameQuery);
            throw;
        }
    }

    /// <summary>
    /// Maps a SqlDataReader row to InnuxEmployeeDto.
    /// Reads Innux-specific columns from dbo.Funcionarios + department enrichment.
    /// </summary>
    private static InnuxEmployeeDto MapToDto(SqlDataReader reader)
    {
        return new InnuxEmployeeDto
        {
            EmployeeNumber = reader["Numero"]?.ToString() ?? "",
            InnuxEmployeeId = reader["IDFuncionario"] is int id ? id : 0,
            Name = reader["Nome"]?.ToString() ?? "",
            ShortName = reader["NomeAbreviado"] as string,
            Email = reader["Email"] as string,
            Phone = reader["Telefone"] as string,
            Mobile = reader["Telemovel"] as string,
            DepartmentId = reader["IDDepartamento"] as int?,
            DepartmentName = reader["DepartmentName"] as string,
            CardNumber = reader["Cartao"] as string,
            IsActiveOperational = reader["Activo"] as bool?,
            LoginAd = reader["LoginAD"] as string,
            HireDate = reader["DataAdmissao"] as DateTime?,
            TerminationDate = reader["DataDemissao"] as DateTime?,
            Category = reader["Categoria"] as string,
            CostCenter = reader["CentroCusto"] as string,
            HasPhoto = reader["HasPhoto"] is int hp && hp == 1,
            Source = "INNUX"
        };
    }
}
