using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

public class PrimaveraDepartmentSyncService : IPrimaveraDepartmentSyncService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly PrimaveraConnectionFactory _connectionFactory;
    private readonly ILogger<PrimaveraDepartmentSyncService> _logger;

    public PrimaveraDepartmentSyncService(
        ApplicationDbContext dbContext,
        PrimaveraConnectionFactory connectionFactory,
        ILogger<PrimaveraDepartmentSyncService> logger)
    {
        _dbContext = dbContext;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<DepartmentSyncResult> SyncDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        var companies = _connectionFactory.GetConfiguredCompanies();
        var result = new DepartmentSyncResult();

        foreach (var company in companies)
        {
            var dbName = _connectionFactory.GetDatabaseName(company);
            if (string.IsNullOrWhiteSpace(dbName)) continue;

            string companyLabel = company.ToString() == "ALPLAPLASTICO" ? "AlplaPLASTICOS" :
                                  company.ToString() == "ALPLASOPRO" ? "AlplaSOPRO" :
                                  company.ToString();

            _logger.LogInformation("Syncing departments from Primavera database: {DbName}", dbName);

            try
            {
                await using var connection = await _connectionFactory.CreateConnectionAsync(company, cancellationToken);
                var query = @"
                    SELECT 
                        Departamento AS DepartmentCode, 
                        Descricao AS DepartmentName
                    FROM dbo.Departamentos";

                await using var command = new SqlCommand(query, connection);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Processed++;
                    var code = reader.GetString(reader.GetOrdinal("DepartmentCode"));
                    var name = reader.IsDBNull(reader.GetOrdinal("DepartmentName")) ? string.Empty : reader.GetString(reader.GetOrdinal("DepartmentName"));
                    
                    var existing = await _dbContext.DepartmentMasters
                        .FirstOrDefaultAsync(d => d.SourceSystem == "PRIMAVERA" 
                                               && d.SourceDatabase == dbName 
                                               && d.DepartmentCode == code, cancellationToken);

                    if (existing == null)
                    {
                        existing = new DepartmentMaster
                        {
                            SourceSystem = "PRIMAVERA",
                            SourceDatabase = dbName,
                            CompanyCode = companyLabel,
                            DepartmentCode = code,
                            DepartmentName = name,
                            IsActive = true, // Force active since inactive column is not present
                            LastSyncedAtUtc = DateTime.UtcNow
                        };
                        _dbContext.DepartmentMasters.Add(existing);
                        result.Created++;
                    }
                    else
                    {
                        existing.DepartmentName = name;
                        existing.IsActive = true;
                        existing.CompanyCode = companyLabel;
                        existing.LastSyncedAtUtc = DateTime.UtcNow;
                        result.Updated++;
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync departments from Primavera database {DbName}", dbName);
                result.Errors.Add($"[{dbName}] {ex.Message}");
            }
        }

        return result;
    }
}
