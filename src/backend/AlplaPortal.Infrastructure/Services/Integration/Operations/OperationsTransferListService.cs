using System.Diagnostics;
using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Application.Interfaces.Operations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Orchestrates paginated transfer list queries against AlplaPROD databases.
///
/// Flow:
///   1. Detect pipeline model via <see cref="IOperationsPipelineDetector"/>
///   2. Resolve status filter to T_Bestellungen.Status integer values
///   3. Open read-only connection via <see cref="AlplaProdConnectionFactory"/>
///   4. Execute count query (total distinct POs matching filters)
///   5. Execute paginated data query with OFFSET/FETCH
///   6. Map rows using <see cref="OperationsStatusMapper"/> for status enrichment
///   7. Return <see cref="OperationsTransferListResponseDto"/>
///
/// All queries are read-only SELECT. No writes are ever performed.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §8
/// </summary>
public class OperationsTransferListService : IOperationsTransferListService
{
    private readonly AlplaProdConnectionFactory _factory;
    private readonly IOperationsPipelineDetector _pipelineDetector;
    private readonly ILogger<OperationsTransferListService> _logger;

    public OperationsTransferListService(
        AlplaProdConnectionFactory factory,
        IOperationsPipelineDetector pipelineDetector,
        ILogger<OperationsTransferListService> logger)
    {
        _factory = factory;
        _pipelineDetector = pipelineDetector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationsTransferListResponseDto> GetTransferListAsync(
        AlplaProdPlant plant,
        DateTime dateFrom,
        DateTime dateTo,
        string? status,
        string? pipelineModelFilter,
        string? articleSearch,
        string? poSearch,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var pipelineModel = _pipelineDetector.DetectPipelineModel(plant);
        var expectedEventCount = _pipelineDetector.GetExpectedEventCount(pipelineModel);

        _logger.LogInformation(
            "[Operations] Transfer list requested: Plant={Plant}, DateFrom={DateFrom:yyyy-MM-dd}, DateTo={DateTo:yyyy-MM-dd}, Status={Status}, Page={Page}, PageSize={PageSize}",
            plant, dateFrom, dateTo, status ?? "(all)", page, pageSize);

        // ─── Resolve status filter ───

        var statusValues = OperationsTransferListQueryBuilder.GetStatusValues(status);

        // ─── Normalize search inputs ───

        articleSearch = NormalizeSearch(articleSearch);
        poSearch = NormalizeSearch(poSearch);

        // dateTo is inclusive — add 1 day for exclusive end boundary
        var dateToExclusive = dateTo.Date.AddDays(1);
        var dateFromNormalized = dateFrom.Date;

        // ─── Open connection (throws InvalidOperationException if disabled) ───

        await using var connection = await _factory.CreateConnectionAsync(plant, ct);

        // ─── Execute count query ───

        var countSql = OperationsTransferListQueryBuilder.BuildListCountQuery(statusValues);
        await using var countCmd = new SqlCommand(countSql, connection);
        AddCommonParameters(countCmd, dateFromNormalized, dateToExclusive, statusValues, articleSearch, poSearch);
        countCmd.CommandTimeout = 30;

        var totalCount = (int)(await countCmd.ExecuteScalarAsync(ct))!;

        // ─── Calculate pagination ───

        var totalPages = totalCount > 0
            ? (int)Math.Ceiling((double)totalCount / pageSize)
            : 0;

        var offset = (page - 1) * pageSize;

        // ─── Short-circuit if no results or page beyond range ───

        if (totalCount == 0 || offset >= totalCount)
        {
            sw.Stop();
            return BuildResponse(plant, pipelineModel, dateFromNormalized, dateTo, page, pageSize,
                totalCount, totalPages, new List<OperationsTransferListItemDto>(), sw.ElapsedMilliseconds);
        }

        // ─── Execute data query ───

        var dataSql = pipelineModel == AlplaProdPipelineModel.INHOUSE
            ? OperationsTransferListQueryBuilder.BuildInhouseListDataQuery(statusValues)
            : OperationsTransferListQueryBuilder.BuildStandardListDataQuery(statusValues);

        await using var dataCmd = new SqlCommand(dataSql, connection);
        AddCommonParameters(dataCmd, dateFromNormalized, dateToExclusive, statusValues, articleSearch, poSearch);
        dataCmd.Parameters.AddWithValue("@Offset", offset);
        dataCmd.Parameters.AddWithValue("@PageSize", pageSize);
        dataCmd.CommandTimeout = 30;

        var items = new List<OperationsTransferListItemDto>();

        await using var reader = await dataCmd.ExecuteReaderAsync(ct);

        var plantServer = _factory.GetPlantServer(plant);
        var plantDatabase = _factory.GetPlantDatabaseName(plant);

        while (await reader.ReadAsync(ct))
        {
            var item = MapRow(reader, plant, plantServer, plantDatabase, pipelineModel, expectedEventCount);
            items.Add(item);
        }

        sw.Stop();

        _logger.LogInformation(
            "[Operations] Transfer list returned: Plant={Plant}, Items={Count}/{Total}, Page={Page}/{Pages}, Duration={Ms}ms",
            plant, items.Count, totalCount, page, totalPages, sw.ElapsedMilliseconds);

        return BuildResponse(plant, pipelineModel, dateFromNormalized, dateTo, page, pageSize,
            totalCount, totalPages, items, sw.ElapsedMilliseconds);
    }

    // ─── Private helpers ───

    /// <summary>
    /// Adds shared parameters to a SQL command: date range, status, and search filters.
    /// </summary>
    private static void AddCommonParameters(
        SqlCommand cmd,
        DateTime dateFrom,
        DateTime dateToExclusive,
        IReadOnlyList<int>? statusValues,
        string? articleSearch,
        string? poSearch)
    {
        cmd.Parameters.AddWithValue("@DateFrom", dateFrom);
        cmd.Parameters.AddWithValue("@DateTo", dateToExclusive);

        // Status parameters — only if filter is active
        if (statusValues != null)
        {
            for (int i = 0; i < statusValues.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@S{i}", statusValues[i]);
            }
        }

        // Search parameters — null if not provided
        cmd.Parameters.AddWithValue("@ArticleSearch",
            articleSearch != null ? (object)$"%{articleSearch}%" : DBNull.Value);
        cmd.Parameters.AddWithValue("@PoSearch",
            poSearch != null ? (object)$"%{poSearch}%" : DBNull.Value);
    }

    /// <summary>
    /// Normalizes a search string: trims whitespace, returns null if empty.
    /// </summary>
    private static string? NormalizeSearch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        return input.Trim();
    }

    /// <summary>
    /// Maps a single SQL result row to a <see cref="OperationsTransferListItemDto"/>.
    /// Uses <see cref="OperationsStatusMapper.MapBestellungStatus"/> for status enrichment.
    /// </summary>
    private static OperationsTransferListItemDto MapRow(
        SqlDataReader reader,
        AlplaProdPlant plant,
        string? plantServer,
        string? plantDatabase,
        AlplaProdPipelineModel pipelineModel,
        int expectedEventCount)
    {
        var mainStatus = reader.IsDBNull(reader.GetOrdinal("MainStatus"))
            ? (int?)null
            : reader.GetInt32(reader.GetOrdinal("MainStatus"));

        var (meaning, severity) = OperationsStatusMapper.MapBestellungStatus(mainStatus);

        return new OperationsTransferListItemDto
        {
            Plant = plant.ToString(),
            PlantServer = plantServer,
            PlantDatabase = plantDatabase,
            IdBestellung = reader.GetInt32(reader.GetOrdinal("IdBestellung")),
            IdJournal = reader.IsDBNull(reader.GetOrdinal("IdJournal"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("IdJournal")),
            JournalNummer = reader.IsDBNull(reader.GetOrdinal("JournalNummer"))
                ? null
                : reader.GetString(reader.GetOrdinal("JournalNummer")),
            PipelineModel = pipelineModel.ToString(),
            CreatedDate = reader.IsDBNull(reader.GetOrdinal("CreatedDate"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("CreatedDate")),
            UpdatedDate = reader.IsDBNull(reader.GetOrdinal("UpdatedDate"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("UpdatedDate")),
            MainStatus = mainStatus,
            StatusMeaning = meaning,
            Severity = severity,
            MaterialName = reader.IsDBNull(reader.GetOrdinal("MaterialName"))
                ? null
                : reader.GetString(reader.GetOrdinal("MaterialName")),
            ArticleAlias = reader.IsDBNull(reader.GetOrdinal("ArticleAlias"))
                ? null
                : reader.GetString(reader.GetOrdinal("ArticleAlias")),
            ArticleVariantType = null, // MVP — T_ArtikelvariantenTyp join deferred (IdArtikelvariantenTyp not in schema)
            PackagingName = null, // MVP — T_VpkVorschrift join deferred
            Quantity = reader.IsDBNull(reader.GetOrdinal("Quantity"))
                ? null
                : reader.GetDouble(reader.GetOrdinal("Quantity")),
            QuantityUnit = null, // MVP — T_Bestellpositionen has no unit column
            ExpectedEventCount = expectedEventCount,
            CompletedEventCount = null, // MVP — too expensive for list queries
            ReferenceNumber = reader.IsDBNull(reader.GetOrdinal("ReferenceNumber"))
                ? null
                : reader.GetString(reader.GetOrdinal("ReferenceNumber")),
        };
    }

    /// <summary>
    /// Builds the final response DTO with metadata.
    /// </summary>
    private static OperationsTransferListResponseDto BuildResponse(
        AlplaProdPlant plant,
        AlplaProdPipelineModel pipelineModel,
        DateTime dateFrom,
        DateTime dateTo,
        int page,
        int pageSize,
        int totalCount,
        int totalPages,
        List<OperationsTransferListItemDto> items,
        long queryDurationMs)
    {
        return new OperationsTransferListResponseDto
        {
            Plant = plant.ToString(),
            PipelineModel = pipelineModel.ToString(),
            DateFrom = dateFrom,
            DateTo = dateTo,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = items,
            QueryTimestamp = DateTime.UtcNow,
            QueryDurationMs = queryDurationMs,
        };
    }
}
