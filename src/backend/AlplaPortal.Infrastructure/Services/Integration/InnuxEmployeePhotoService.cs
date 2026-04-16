using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Innux employee photo retrieval — read-only, single database.
///
/// Uses InnuxConnectionFactory to resolve the SQL connection.
/// Queries dbo.Funcionarios.Fotografia (varbinary blob) by employee number.
///
/// Design decisions:
/// - Separated from InnuxEmployeeService to avoid accidentally loading
///   large blobs during identity lookups
/// - Returns raw bytes — caller decides content-type and caching
/// - Returns null on not-found or no-photo (caller produces 404)
/// </summary>
public class InnuxEmployeePhotoService : IInnuxEmployeePhotoService
{
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly ILogger<InnuxEmployeePhotoService> _logger;

    public InnuxEmployeePhotoService(
        InnuxConnectionFactory connectionFactory,
        ILogger<InnuxEmployeePhotoService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<byte[]?> GetEmployeePhotoAsync(string employeeNumber)
    {
        if (string.IsNullOrWhiteSpace(employeeNumber))
            return null;

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync();

            var query = @"
                SELECT TOP 1 Fotografia
                FROM dbo.Funcionarios
                WHERE Numero = @Number
                  AND Fotografia IS NOT NULL";

            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Number", employeeNumber.Trim());

            var result = await command.ExecuteScalarAsync();

            if (result is byte[] photoBytes && photoBytes.Length > 0)
            {
                _logger.LogInformation(
                    "Innux photo retrieved for employee {Number}. Size: {Size} bytes.",
                    employeeNumber, photoBytes.Length);
                return photoBytes;
            }

            _logger.LogInformation(
                "No photo found in Innux for employee {Number}.", employeeNumber);
            return null;
        }
        catch (InvalidOperationException)
        {
            // Configuration errors — rethrow as-is for the controller to handle
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to retrieve Innux photo for employee {Number}.", employeeNumber);
            throw;
        }
    }
}
