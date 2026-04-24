using AlplaPortal.Application.Interfaces.MonthlyChanges;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.MonthlyChanges;

/// <summary>
/// Syncs Innux processed attendance data (dbo.Alteracoes) into MCAttendanceSnapshot.
///
/// Data flow:
///   Innux Alteracoes + Funcionarios + Horarios → MCAttendanceSnapshot (immutable)
///
/// Key design decisions:
///   - One sync per ProcessingRun (entity+month)
///   - Employee entity resolution via Funcionarios.IDEntidade
///   - LEFT JOIN to Horarios for schedule context (no FK in Innux)
///   - TerminaisMarcacoes NOT persisted (Amendment §3)
///   - EntityId denormalized on each snapshot row for direct filtering
///   - Any existing snapshots for the run are deleted before sync (idempotent retry)
/// </summary>
public class MonthlyChangesSyncService : IMonthlyChangesSyncService
{
    private readonly ApplicationDbContext _context;
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly ILogger<MonthlyChangesSyncService> _logger;

    public MonthlyChangesSyncService(
        ApplicationDbContext context,
        InnuxConnectionFactory connectionFactory,
        ILogger<MonthlyChangesSyncService> logger)
    {
        _context = context;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SyncAttendanceDataAsync(
        Guid processingRunId, int entityId, int year, int month, CancellationToken ct = default)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        _logger.LogInformation(
            "MC Sync: Starting for RunId={RunId}, Entity={EntityId}, Period={Year}-{Month:D2}",
            processingRunId, entityId, year, month);

        // ─── Idempotent: clear any previous snapshots for this run ───
        var existingCount = await _context.MCAttendanceSnapshots
            .Where(s => s.ProcessingRunId == processingRunId)
            .ExecuteDeleteAsync(ct);

        if (existingCount > 0)
            _logger.LogWarning("MC Sync: Cleared {Count} existing snapshots for run {RunId} (retry scenario)", existingCount, processingRunId);

        // ─── Query Innux ───
        var snapshots = await QueryInnuxAlteracoesAsync(entityId, startDate, endDate, processingRunId, ct);

        if (snapshots.Count == 0)
        {
            _logger.LogWarning("MC Sync: No Alteracoes rows found for Entity={EntityId}, Period={Start:yyyy-MM-dd} to {End:yyyy-MM-dd}",
                entityId, startDate, endDate);
            return 0;
        }

        // ─── Persist in batches ───
        const int batchSize = 500;
        for (int i = 0; i < snapshots.Count; i += batchSize)
        {
            var batch = snapshots.Skip(i).Take(batchSize);
            _context.MCAttendanceSnapshots.AddRange(batch);
            await _context.SaveChangesAsync(ct);
        }

        _logger.LogInformation("MC Sync: Completed — {Count} snapshot rows persisted for run {RunId}", snapshots.Count, processingRunId);
        return snapshots.Count;
    }

    // ──────────────────────────────────────────────────────────────────────

    private async Task<List<MCAttendanceSnapshot>> QueryInnuxAlteracoesAsync(
        int entityId, DateTime startDate, DateTime endDate, Guid processingRunId, CancellationToken ct)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);

        // ─── SQL: Alteracoes + Funcionarios (entity+name) + Horarios (schedule) ───
        const string query = @"
            SELECT
                a.IDAlteracao,
                a.IDFuncionario,
                f.Numero        AS EmployeeCode,
                f.Nome          AS EmployeeName,
                f.IDEntidade,
                a.Data,
                h.Codigo        AS ScheduleCode,
                h.Sigla         AS ScheduleSigla,
                ISNULL(h.DiaFolga, 0) AS IsRestDay,
                -- Earliest mandatory period start
                (SELECT TOP 1 hp.Inicio FROM dbo.HorariosPeriodos hp
                 WHERE hp.IDHorario = h.IDHorario AND hp.Tipo = 'Obrigatório'
                 ORDER BY hp.Inicio) AS ScheduleStart,
                a.Entrada1      AS Entrada,
                a.Saida1        AS Saida,
                a.Falta,
                a.Ausencia,
                a.Objectivo,
                a.Saldo,
                a.Marcacao,
                a.Validado,
                a.TipoAnomalia,
                a.Justificacao
            FROM dbo.Alteracoes a
            INNER JOIN dbo.Funcionarios f ON a.IDFuncionario = f.IDFuncionario
            LEFT JOIN dbo.Horarios h ON a.IDHorario = h.IDHorario
            WHERE f.IDEntidade = @EntityId
              AND a.Data >= @StartDate
              AND a.Data <= @EndDate
            ORDER BY f.Numero, a.Data";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@EntityId", entityId);
        command.Parameters.AddWithValue("@StartDate", startDate);
        command.Parameters.AddWithValue("@EndDate", endDate);
        command.CommandTimeout = 120; // Larger dataset for full month

        var results = new List<MCAttendanceSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            results.Add(MapToSnapshot(reader, processingRunId, entityId));
        }

        return results;
    }

    private static MCAttendanceSnapshot MapToSnapshot(SqlDataReader reader, Guid processingRunId, int entityId)
    {
        var snapshot = new MCAttendanceSnapshot
        {
            Id = Guid.NewGuid(),
            ProcessingRunId = processingRunId,
            EntityId = entityId,
            InnuxAlteracaoId = SafeInt(reader["IDAlteracao"]),
            InnuxEmployeeId = SafeInt(reader["IDFuncionario"]),
            EmployeeCode = SafeString(reader["EmployeeCode"]),
            EmployeeName = SafeString(reader["EmployeeName"]),
            Date = reader.GetDateTime(reader.GetOrdinal("Data")),
            ScheduleCode = SafeNullableString(reader["ScheduleCode"]),
            ScheduleSigla = SafeNullableString(reader["ScheduleSigla"]),
            IsRestDay = SafeBool(reader["IsRestDay"]),
            ExpectedMinutes = ConvertInnuxDuration(reader["Objectivo"]),
            AbsenceMinutes = ConvertInnuxDuration(reader["Falta"]),
            JustifiedAbsenceMinutes = ConvertInnuxDuration(reader["Ausencia"]),
            BalanceMinutes = ConvertInnuxDuration(reader["Saldo"]),
            PunchCount = SafeInt(reader["Marcacao"]),
            IsValidated = SafeBool(reader["Validado"]),
            AnomalyDescription = SafeNullableString(reader["TipoAnomalia"]),
            Justification = SafeNullableString(reader["Justificacao"]),
            SyncedAtUtc = DateTime.UtcNow
        };

        // ─── FirstEntry / ScheduleStartTime from datetime-as-duration ───
        snapshot.FirstEntry = ConvertInnuxTime(reader["Entrada"]);
        snapshot.ScheduleStartTime = ConvertInnuxTime(reader["ScheduleStart"]);

        return snapshot;
    }

    // ─── Innux helpers (match existing InnuxAttendanceService patterns) ──

    private static int ConvertInnuxDuration(object raw)
    {
        if (raw == DBNull.Value || raw == null) return 0;
        if (raw is DateTime dt)
        {
            // Innux encodes duration as 1900-01-01 + hours:minutes
            return (int)dt.TimeOfDay.TotalMinutes;
        }
        return 0;
    }

    private static string? ConvertInnuxTime(object raw)
    {
        if (raw == DBNull.Value || raw == null) return null;
        if (raw is DateTime dt)
        {
            return dt.ToString("HH:mm");
        }
        return null;
    }

    private static int SafeInt(object raw) => raw == DBNull.Value ? 0 : Convert.ToInt32(raw);
    private static long SafeLong(object raw) => raw == DBNull.Value ? 0L : Convert.ToInt64(raw);
    private static bool SafeBool(object raw) => raw != DBNull.Value && Convert.ToBoolean(raw);
    private static string SafeString(object raw) => raw == DBNull.Value ? string.Empty : raw.ToString() ?? string.Empty;
    private static string? SafeNullableString(object raw) => raw == DBNull.Value ? null : raw.ToString();
}
