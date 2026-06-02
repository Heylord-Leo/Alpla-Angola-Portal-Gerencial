using System.Diagnostics;
using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Application.Interfaces.Operations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Orchestrates transfer timeline queries against AlplaPROD databases.
///
/// Flow:
///   1. Validate plant is enabled via <see cref="AlplaProdConnectionFactory"/>
///   2. Detect pipeline model via <see cref="IOperationsPipelineDetector"/>
///   3. Open read-only connection
///   4. Verify PO exists (return null if not found)
///   5. Execute correct UNION ALL query (Standard or Inhouse)
///   6. Map rows to <see cref="OperationsTimelineEventDto"/> list
///   7. Apply <see cref="OperationsStatusMapper"/> for enrichment
///   8. Return <see cref="OperationsTimelineResponseDto"/>
///
/// All queries are read-only SELECT. No writes are ever performed.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §6
/// </summary>
public class OperationsTimelineService : IOperationsTimelineService
{
    private readonly AlplaProdConnectionFactory _factory;
    private readonly IOperationsPipelineDetector _pipelineDetector;
    private readonly ILogger<OperationsTimelineService> _logger;

    public OperationsTimelineService(
        AlplaProdConnectionFactory factory,
        IOperationsPipelineDetector pipelineDetector,
        ILogger<OperationsTimelineService> logger)
    {
        _factory = factory;
        _pipelineDetector = pipelineDetector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationsTimelineResponseDto> GetTimelineAsync(
        AlplaProdPlant plant, int idBestellung, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var pipelineModel = _pipelineDetector.DetectPipelineModel(plant);
        var expectedCount = _pipelineDetector.GetExpectedEventCount(pipelineModel);

        _logger.LogInformation(
            "[Operations] Timeline requested: Plant={Plant}, IdBestellung={Id}, Pipeline={Pipeline}",
            plant, idBestellung, pipelineModel);

        // CreateConnectionAsync throws InvalidOperationException if disabled/unconfigured
        await using var connection = await _factory.CreateConnectionAsync(plant, ct);

        // ─── Step 1: Verify PO exists ───

        var existsSql = OperationsTimelineQueryBuilder.BuildExistenceCheckQuery();
        await using var existsCmd = new SqlCommand(existsSql, connection);
        existsCmd.Parameters.AddWithValue("@IdBestellung", idBestellung);
        existsCmd.CommandTimeout = 15;

        var poExists = (int)(await existsCmd.ExecuteScalarAsync(ct))! > 0;

        if (!poExists)
        {
            sw.Stop();
            _logger.LogWarning(
                "[Operations] PO {Id} not found in plant {Plant}",
                idBestellung, plant);

            // Return null to signal 404 to the controller
            return null!;
        }

        // ─── Step 2: Select and execute timeline query ───

        var sql = pipelineModel switch
        {
            AlplaProdPipelineModel.INHOUSE => OperationsTimelineQueryBuilder.BuildInhouseTimelineQuery(),
            _ => OperationsTimelineQueryBuilder.BuildStandardTimelineQuery(),
        };

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@IdBestellung", idBestellung);
        cmd.CommandTimeout = 30;

        var events = new List<OperationsTimelineEventDto>();
        string? journalNummer = null;

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var dto = MapRow(reader);

            // Capture JournalNummer from the first EDI event
            if (journalNummer == null && dto.JournalNummer != null)
                journalNummer = dto.JournalNummer;

            events.Add(dto);
        }

        sw.Stop();

        var completedCount = events.Count(e => e.IsCompleted);

        _logger.LogInformation(
            "[Operations] Timeline returned: Plant={Plant}, IdBestellung={Id}, Events={Count}, Completed={Completed}, Duration={Ms}ms",
            plant, idBestellung, events.Count, completedCount, sw.ElapsedMilliseconds);

        return new OperationsTimelineResponseDto
        {
            Plant = plant.ToString(),
            PlantServer = _factory.GetPlantServer(plant),
            PlantDatabase = _factory.GetPlantDatabaseName(plant),
            IdBestellung = idBestellung,
            JournalNummer = journalNummer,
            PipelineModel = pipelineModel.ToString(),
            ExpectedEventCount = expectedCount,
            CompletedEventCount = completedCount,
            Events = events,
            QueryTimestamp = DateTime.UtcNow,
            QueryDurationMs = sw.ElapsedMilliseconds,
        };
    }

    /// <summary>
    /// Maps a single SQL result row to an <see cref="OperationsTimelineEventDto"/>.
    /// Applies status mapping and enrichment from <see cref="OperationsStatusMapper"/>.
    /// </summary>
    private static OperationsTimelineEventDto MapRow(SqlDataReader reader)
    {
        var eventCode = reader.GetString(reader.GetOrdinal("EventCode"));
        var mainStatus = reader.IsDBNull(reader.GetOrdinal("MainStatus"))
            ? (int?)null
            : reader.GetInt32(reader.GetOrdinal("MainStatus"));

        var (meaning, severity, isCompleted) = OperationsStatusMapper.MapEvent(eventCode, mainStatus);
        var isTechnical = OperationsStatusMapper.IsTechnicalEvent(eventCode);

        return new OperationsTimelineEventDto
        {
            SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
            EventCode = eventCode,
            EventLabelPT = reader.GetString(reader.GetOrdinal("EventLabelPT")),
            EventLabelEN = string.Empty, // EN labels deferred to Phase 4
            SourceTable = reader.GetString(reader.GetOrdinal("SourceTable")),
            EventDate = reader.IsDBNull(reader.GetOrdinal("EventDate"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("EventDate")),
            EventUser = reader.IsDBNull(reader.GetOrdinal("EventUser"))
                ? null
                : reader.GetString(reader.GetOrdinal("EventUser")),
            MainStatus = mainStatus,
            SecondaryStatus = reader.IsDBNull(reader.GetOrdinal("SecondaryStatus"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("SecondaryStatus")),
            StatusMeaning = meaning,
            Severity = severity,
            IsCompleted = isCompleted,
            IsTechnical = isTechnical,
            IdBestellung = reader.IsDBNull(reader.GetOrdinal("IdBestellung"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("IdBestellung")),
            IdBestellPosition = reader.IsDBNull(reader.GetOrdinal("IdBestellPosition"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("IdBestellPosition")),
            IdJournal = reader.IsDBNull(reader.GetOrdinal("IdJournal"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("IdJournal")),
            JournalNummer = reader.IsDBNull(reader.GetOrdinal("JournalNummer"))
                ? null
                : reader.GetString(reader.GetOrdinal("JournalNummer")),
            IdAuftragsAbruf = reader.IsDBNull(reader.GetOrdinal("IdAuftragsAbruf"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("IdAuftragsAbruf")),
            IdAbrufe = reader.IsDBNull(reader.GetOrdinal("IdAbrufe"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("IdAbrufe")),
            IdLadePlanung = reader.IsDBNull(reader.GetOrdinal("IdLadePlanung"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("IdLadePlanung")),
            IdLadeAuftrag = reader.IsDBNull(reader.GetOrdinal("IdLadeAuftrag"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("IdLadeAuftrag")),
            IdWareneingang = reader.IsDBNull(reader.GetOrdinal("IdWareneingang"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("IdWareneingang")),
            IdInhouseLieferung = reader.IsDBNull(reader.GetOrdinal("IdInhouseLieferung"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("IdInhouseLieferung")),
            ReferenceNumber = reader.IsDBNull(reader.GetOrdinal("ReferenceNumber"))
                ? null
                : reader.GetString(reader.GetOrdinal("ReferenceNumber")),
            Quantity = reader.IsDBNull(reader.GetOrdinal("Quantity"))
                ? null
                : reader.GetDouble(reader.GetOrdinal("Quantity")),
            Notes = reader.IsDBNull(reader.GetOrdinal("Notes"))
                ? null
                : reader.GetString(reader.GetOrdinal("Notes")),
        };
    }
}
