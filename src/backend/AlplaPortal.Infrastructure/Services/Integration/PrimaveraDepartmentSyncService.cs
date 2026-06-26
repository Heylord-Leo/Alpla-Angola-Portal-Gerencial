using System.Text.Json;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

public class PrimaveraDepartmentSyncService : IPrimaveraDepartmentSyncService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly PrimaveraConnectionFactory _connectionFactory;
    private readonly AdminLogWriter _adminLogWriter;
    private readonly ILogger<PrimaveraDepartmentSyncService> _logger;

    public PrimaveraDepartmentSyncService(
        ApplicationDbContext dbContext,
        PrimaveraConnectionFactory connectionFactory,
        AdminLogWriter adminLogWriter,
        ILogger<PrimaveraDepartmentSyncService> logger)
    {
        _dbContext = dbContext;
        _connectionFactory = connectionFactory;
        _adminLogWriter = adminLogWriter;
        _logger = logger;
    }

    public async Task<DepartmentSyncResult> SyncDepartmentsAsync(string? correlationId = null, CancellationToken cancellationToken = default)
    {
        var companies = _connectionFactory.GetConfiguredCompanies();
        var result = new DepartmentSyncResult();

        _ = _adminLogWriter.WriteAsync("Information", nameof(PrimaveraDepartmentSyncService), "DEPT_SYNC_STARTED",
            "Sincronização de departamentos (Primavera) iniciada.",
            payload: JsonSerializer.Serialize(new { CorrelationId = correlationId }));

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
                
                // Add system log for department sync failure
                _ = _adminLogWriter.WriteAsync("Error", nameof(PrimaveraDepartmentSyncService), "DEPT_SYNC_FAILED",
                    $"Falha ao sincronizar departamentos da base {dbName}: {ex.Message}",
                    exceptionDetail: ex.StackTrace,
                    payload: JsonSerializer.Serialize(new { CorrelationId = correlationId, Database = dbName }));
            }
        }

        // Add system log for completion
        if (result.Processed > 0 || result.Errors.Any())
        {
            var status = result.Errors.Any() ? "Warning" : "Information";
            var eventCode = result.Errors.Any() ? "DEPT_SYNC_PARTIAL" : "DEPT_SYNC_SUCCESS";
            _ = _adminLogWriter.WriteAsync(status, nameof(PrimaveraDepartmentSyncService), eventCode,
                $"Sincronização de departamentos (Primavera): {result.Created} criados, {result.Updated} atualizados, {result.Errors.Count} erros.",
                payload: JsonSerializer.Serialize(new { CorrelationId = correlationId, Created = result.Created, Updated = result.Updated, ErrorCount = result.Errors.Count }));
        }

        return result;
    }
}
