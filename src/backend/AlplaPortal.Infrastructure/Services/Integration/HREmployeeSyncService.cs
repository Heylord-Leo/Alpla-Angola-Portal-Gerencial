using System.Diagnostics;
using System.Text.Json;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Syncs Innux employees into the local HREmployee projection table.
///
/// Strategy:
/// 1. Query all active employees from Innux dbo.Funcionarios
/// 2. Validate in-memory (skip duplicates or invalid codes)
/// 3. Upsert into HREmployees matching on InnuxEmployeeId
/// 4. Mark employees not found in Innux as IsActive = false
/// 5. Save changes in a batch, with full error logging if it fails
///
/// Note on Concurrency: SemaphoreSlim is process-level. If this API 
/// scales to multiple instances, use a distributed lock (e.g. Redis/DB).
/// </summary>
public class HREmployeeSyncService : IHREmployeeSyncService
{
    private readonly ApplicationDbContext _context;
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly AdminLogWriter _adminLogWriter;
    private readonly ILogger<HREmployeeSyncService> _logger;

    private static readonly SemaphoreSlim _syncLock = new(1, 1);

    public HREmployeeSyncService(
        ApplicationDbContext context,
        InnuxConnectionFactory connectionFactory,
        AdminLogWriter adminLogWriter,
        ILogger<HREmployeeSyncService> logger)
    {
        _context = context;
        _connectionFactory = connectionFactory;
        _adminLogWriter = adminLogWriter;
        _logger = logger;
    }

    public async Task<HRSyncLog> SyncFromInnuxAsync(Guid? triggeredByUserId = null, string? correlationId = null)
    {
        if (!_syncLock.Wait(0))
        {
            throw new InvalidOperationException("Uma sincronização já está em curso. Por favor, aguarde.");
        }

        var sw = Stopwatch.StartNew();
        var syncLog = new HRSyncLog
        {
            TriggeredByUserId = triggeredByUserId,
            Status = "RUNNING"
        };
        _context.HRSyncLogs.Add(syncLog);
        await _context.SaveChangesAsync();

        await _adminLogWriter.WriteAsync("Information", nameof(HREmployeeSyncService), "HR_SYNC_STARTED",
            $"Sincronização de funcionários iniciada pelo utilizador {triggeredByUserId}.",
            payload: JsonSerializer.Serialize(new { CorrelationId = correlationId }));

        var skippedDetails = new List<object>();

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
            var skipped = 0;
            var processedInnuxIds = new HashSet<int>();
            var seenEmployeeCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var innux in innuxEmployees)
            {
                try
                {
                    // Validation
                    if (string.IsNullOrWhiteSpace(innux.EmployeeCode))
                    {
                        skipped++;
                        skippedDetails.Add(new { EmployeeCode = innux.EmployeeCode ?? "(vazio)", Reason = "EmployeeCode vazio" });
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(innux.FullName))
                    {
                        skipped++;
                        skippedDetails.Add(new { innux.EmployeeCode, Reason = "Nome completo vazio" });
                        continue;
                    }

                    if (seenEmployeeCodes.Contains(innux.EmployeeCode))
                    {
                        skipped++;
                        skippedDetails.Add(new { innux.EmployeeCode, Reason = "EmployeeCode duplicado no Innux" });
                        continue;
                    }

                    seenEmployeeCodes.Add(innux.EmployeeCode);
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
                        if (!string.IsNullOrWhiteSpace(innux.Email))
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
                    _logger.LogWarning(ex, "Error processing Innux employee {Code}", innux.EmployeeCode);
                    errors++;
                    skippedDetails.Add(new { innux.EmployeeCode, Reason = $"Exceção durante o mapeamento: {ex.Message}" });
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

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException dbEx)
            {
                // Unroll InnerExceptions to capture actual constraint failures
                var actualError = dbEx.InnerException?.Message ?? dbEx.Message;
                throw new Exception($"Database save failed: {actualError}", dbEx);
            }

            sw.Stop();

            // 4. Update sync log
            syncLog.EmployeesCreated = created;
            syncLog.EmployeesUpdated = updated;
            syncLog.EmployeesDeactivated = deactivated;
            syncLog.TotalProcessed = innuxEmployees.Count;
            syncLog.Errors = errors;
            syncLog.CompletedAtUtc = DateTime.UtcNow;

            // Determine status: PARTIAL if any records were skipped or had errors
            var isPartial = skipped > 0 || errors > 0;
            syncLog.Status = isPartial ? "PARTIAL" : "COMPLETED";
            
            await _context.SaveChangesAsync();

            var eventCode = isPartial ? "HR_SYNC_PARTIAL" : "HR_SYNC_SUCCESS";
            var logLevel = isPartial ? "Warning" : "Information";
            var statusMsg = isPartial
                ? $"Sincronização concluída parcialmente: {created} criados, {updated} atualizados, {deactivated} desativados, {skipped} ignorado(s)."
                : $"Sincronização concluída: {created} criados, {updated} atualizados, {deactivated} desativados.";

            await _adminLogWriter.WriteAsync(logLevel, nameof(HREmployeeSyncService), eventCode,
                statusMsg,
                payload: JsonSerializer.Serialize(new { CorrelationId = correlationId, Created = created, Updated = updated, Deactivated = deactivated, Skipped = skipped, DurationMs = sw.ElapsedMilliseconds, SkippedDetails = skippedDetails }));

            _logger.LogInformation(
                "HR Employee sync completed: {Created} created, {Updated} updated, {Deactivated} deactivated, {Skipped} skipped, {Errors} errors",
                created, updated, deactivated, skipped, errors);

            return syncLog;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "HR Employee sync failed");

            // Build detailed exception tracking
            var fullError = ex.Message;
            var currentEx = ex.InnerException;
            while (currentEx != null)
            {
                fullError += $" | Inner: {currentEx.Message}";
                currentEx = currentEx.InnerException;
            }

            syncLog.Status = "FAILED";
            syncLog.ErrorDetails = fullError;
            syncLog.CompletedAtUtc = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();

            await _adminLogWriter.WriteAsync("Error", nameof(HREmployeeSyncService), "HR_SYNC_FAILED",
                $"Falha na sincronização: {ex.Message}",
                exceptionDetail: fullError + "\n" + ex.StackTrace,
                payload: JsonSerializer.Serialize(new { CorrelationId = correlationId, DurationMs = sw.ElapsedMilliseconds, Skipped = skippedDetails.Count }));

            throw;
        }
        finally
        {
            _syncLock.Release();
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
