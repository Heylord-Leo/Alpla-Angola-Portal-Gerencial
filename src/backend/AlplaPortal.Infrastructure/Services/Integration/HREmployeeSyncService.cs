using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Syncs Innux employees into the local HREmployee projection table.
///
/// Strategy:
/// 1. Query all active employees from Innux dbo.Funcionarios
/// 2. Upsert into HREmployees matching on InnuxEmployeeId
/// 3. Mark employees not found in Innux as IsActive = false
/// 4. Log sync operation in HRSyncLogs
///
/// Preserves Portal mapping fields (PortalDepartmentId, PlantId, ManagerUserId)
/// — these are never overwritten by sync; they are managed by HR/Admin.
/// </summary>
public class HREmployeeSyncService : IHREmployeeSyncService
{
    private readonly ApplicationDbContext _context;
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly ILogger<HREmployeeSyncService> _logger;

    public HREmployeeSyncService(
        ApplicationDbContext context,
        InnuxConnectionFactory connectionFactory,
        ILogger<HREmployeeSyncService> logger)
    {
        _context = context;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<HRSyncLog> SyncFromInnuxAsync(Guid? triggeredByUserId = null)
    {
        var syncLog = new HRSyncLog
        {
            TriggeredByUserId = triggeredByUserId,
            Status = "RUNNING"
        };
        _context.HRSyncLogs.Add(syncLog);
        await _context.SaveChangesAsync();

        try
        {
            // 1. Fetch all active employees from Innux
            var innuxEmployees = await FetchInnuxEmployeesAsync();

            // 2. Get all existing local employees
            var existingEmployees = await _context.HREmployees.ToListAsync();
            var existingByInnuxId = existingEmployees.ToDictionary(e => e.InnuxEmployeeId);

            var created = 0;
            var updated = 0;
            var deactivated = 0;
            var errors = 0;
            var processedInnuxIds = new HashSet<int>();

            foreach (var innux in innuxEmployees)
            {
                try
                {
                    processedInnuxIds.Add(innux.InnuxEmployeeId);

                    if (existingByInnuxId.TryGetValue(innux.InnuxEmployeeId, out var existing))
                    {
                        // Update Innux source fields only (preserve Portal mapping fields)
                        existing.EmployeeCode = innux.EmployeeCode;
                        existing.FullName = innux.FullName;
                        existing.InnuxDepartmentName = innux.InnuxDepartmentName;
                        existing.InnuxDepartmentId = innux.InnuxDepartmentId;
                        existing.JobTitle = innux.JobTitle;
                        existing.CardNumber = innux.CardNumber;
                        existing.Email = innux.Email;
                        existing.HireDate = innux.HireDate;
                        existing.TerminationDate = innux.TerminationDate;
                        existing.IsActive = true;
                        existing.LastSyncedAtUtc = DateTime.UtcNow;
                        updated++;
                    }
                    else
                    {
                        // Create new HREmployee
                        var newEmployee = new HREmployee
                        {
                            InnuxEmployeeId = innux.InnuxEmployeeId,
                            EmployeeCode = innux.EmployeeCode,
                            FullName = innux.FullName,
                            InnuxDepartmentName = innux.InnuxDepartmentName,
                            InnuxDepartmentId = innux.InnuxDepartmentId,
                            JobTitle = innux.JobTitle,
                            CardNumber = innux.CardNumber,
                            Email = innux.Email,
                            HireDate = innux.HireDate,
                            TerminationDate = innux.TerminationDate,
                            IsActive = true,
                            IsMapped = false,
                            LastSyncedAtUtc = DateTime.UtcNow
                        };
                        _context.HREmployees.Add(newEmployee);
                        created++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing Innux employee {Id}", innux.InnuxEmployeeId);
                    errors++;
                }
            }

            // 3. Deactivate employees no longer in Innux
            foreach (var existing in existingEmployees)
            {
                if (existing.IsActive && !processedInnuxIds.Contains(existing.InnuxEmployeeId))
                {
                    existing.IsActive = false;
                    existing.LastSyncedAtUtc = DateTime.UtcNow;
                    deactivated++;
                }
            }

            await _context.SaveChangesAsync();

            // 4. Update sync log
            syncLog.EmployeesCreated = created;
            syncLog.EmployeesUpdated = updated;
            syncLog.EmployeesDeactivated = deactivated;
            syncLog.TotalProcessed = innuxEmployees.Count;
            syncLog.Errors = errors;
            syncLog.Status = "COMPLETED";
            syncLog.CompletedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "HR Employee sync completed: {Created} created, {Updated} updated, {Deactivated} deactivated, {Errors} errors",
                created, updated, deactivated, errors);

            return syncLog;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HR Employee sync failed");
            syncLog.Status = "FAILED";
            syncLog.ErrorDetails = ex.Message;
            syncLog.CompletedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            throw;
        }
    }

    public async Task<HRSyncLog?> GetLastSyncAsync()
    {
        return await _context.HRSyncLogs
            .OrderByDescending(s => s.StartedAtUtc)
            .FirstOrDefaultAsync();
    }

    /// <summary>Fetches all active employees from Innux dbo.Funcionarios.</summary>
    private async Task<List<InnuxEmployeeRecord>> FetchInnuxEmployeesAsync()
    {
        var employees = new List<InnuxEmployeeRecord>();

        await using var connection = await _connectionFactory.CreateConnectionAsync();

        var query = @"
            SELECT 
                f.IDFuncionario, f.Numero, f.Nome, f.NomeAbreviado,
                f.Email, f.IDDepartamento, f.Cartao, f.Categoria,
                f.DataAdmissao, f.DataDemissao,
                d.Descricao AS DepartmentName
            FROM dbo.Funcionarios f
            LEFT JOIN dbo.Departamentos d ON f.IDDepartamento = d.IDDepartamento
            WHERE f.Activo = 1
            ORDER BY f.Nome ASC";

        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            employees.Add(new InnuxEmployeeRecord
            {
                InnuxEmployeeId = reader["IDFuncionario"] != DBNull.Value ? Convert.ToInt32(reader["IDFuncionario"]) : 0,
                EmployeeCode = reader["Numero"] != DBNull.Value ? reader["Numero"].ToString() ?? "" : "",
                FullName = reader["Nome"] != DBNull.Value ? reader["Nome"].ToString() ?? "" : "",
                InnuxDepartmentName = reader["DepartmentName"] as string,
                InnuxDepartmentId = reader["IDDepartamento"] != DBNull.Value ? Convert.ToInt32(reader["IDDepartamento"]) : null,
                JobTitle = reader["Categoria"] as string,
                CardNumber = reader["Cartao"] as string,
                Email = reader["Email"] as string,
                HireDate = reader["DataAdmissao"] != DBNull.Value ? Convert.ToDateTime(reader["DataAdmissao"]) : null,
                TerminationDate = reader["DataDemissao"] != DBNull.Value ? Convert.ToDateTime(reader["DataDemissao"]) : null
            });
        }

        return employees;
    }

    /// <summary>Internal record for Innux query results.</summary>
    private class InnuxEmployeeRecord
    {
        public int InnuxEmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? InnuxDepartmentName { get; set; }
        public int? InnuxDepartmentId { get; set; }
        public string? JobTitle { get; set; }
        public string? CardNumber { get; set; }
        public string? Email { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
    }
}
