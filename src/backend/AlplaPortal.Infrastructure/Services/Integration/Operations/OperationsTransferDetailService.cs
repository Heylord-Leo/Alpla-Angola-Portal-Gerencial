using System.Diagnostics;
using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Application.Interfaces.Operations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Orchestrates transfer detail queries against AlplaPROD databases.
///
/// Flow:
///   1. Detect pipeline model via <see cref="IOperationsPipelineDetector"/>
///   2. Open read-only connection via <see cref="AlplaProdConnectionFactory"/>
///   3. Verify PO exists (return null if not found)
///   4. Execute correct detail query (Standard or Inhouse)
///   5. Map single row to <see cref="OperationsTransferDetailDto"/>
///   6. Apply <see cref="OperationsStatusMapper"/> for status enrichment
///
/// All queries are read-only SELECT. No writes are ever performed.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §9 (Phase 6)
/// </summary>
public class OperationsTransferDetailService : IOperationsTransferDetailService
{
    private readonly AlplaProdConnectionFactory _factory;
    private readonly IOperationsPipelineDetector _pipelineDetector;
    private readonly ILogger<OperationsTransferDetailService> _logger;

    public OperationsTransferDetailService(
        AlplaProdConnectionFactory factory,
        IOperationsPipelineDetector pipelineDetector,
        ILogger<OperationsTransferDetailService> logger)
    {
        _factory = factory;
        _pipelineDetector = pipelineDetector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationsTransferDetailDto?> GetTransferDetailAsync(
        AlplaProdPlant plant, int idBestellung, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var pipelineModel = await _pipelineDetector.DetectPipelineModelAsync(plant, ct);

        _logger.LogInformation(
            "[Operations] Transfer detail requested: Plant={Plant}, IdBestellung={Id}, Pipeline={Pipeline}",
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
                "[Operations] PO {Id} not found in plant {Plant} (detail request)",
                idBestellung, plant);
            return null;
        }

        // ─── Step 2: Select and execute detail query ───

        var sql = pipelineModel switch
        {
            AlplaProdPipelineModel.INHOUSE => OperationsTransferDetailQueryBuilder.BuildInhouseDetailQuery(),
            _ => OperationsTransferDetailQueryBuilder.BuildStandardDetailQuery(),
        };

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@IdBestellung", idBestellung);
        cmd.CommandTimeout = 30;

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var plantServer = await _factory.GetPlantServerAsync(plant, ct);
        var plantDatabase = await _factory.GetPlantDatabaseNameAsync(plant, ct);

        OperationsTransferDetailDto? result = null;

        if (await reader.ReadAsync(ct))
        {
            result = pipelineModel switch
            {
                AlplaProdPipelineModel.INHOUSE => MapInhouseRow(reader, plant, plantServer, plantDatabase, pipelineModel),
                _ => MapStandardRow(reader, plant, plantServer, plantDatabase, pipelineModel),
            };
        }

        sw.Stop();

        if (result != null)
        {
            result.QueryTimestamp = DateTime.UtcNow;
            result.QueryDurationMs = sw.ElapsedMilliseconds;
        }

        _logger.LogInformation(
            "[Operations] Transfer detail returned: Plant={Plant}, IdBestellung={Id}, Found={Found}, Duration={Ms}ms",
            plant, idBestellung, result != null, sw.ElapsedMilliseconds);

        return result;
    }

    // ─── Private row mappers ───

    /// <summary>
    /// Maps a Standard pipeline detail row.
    /// </summary>
    private static OperationsTransferDetailDto MapStandardRow(
        SqlDataReader reader,
        AlplaProdPlant plant,
        string? plantServer,
        string? plantDatabase,
        AlplaProdPipelineModel pipelineModel)
    {
        // Header
        var mainStatus = ReadNullableInt(reader, "MainStatus");
        var (statusMeaning, severity) = OperationsStatusMapper.MapBestellungStatus(mainStatus);

        var header = new OperationsTransferHeaderDto
        {
            IdBestellung = reader.GetInt32(reader.GetOrdinal("IdBestellung")),
            IdJournal = ReadNullableInt(reader, "IdJournal"),
            JournalNummer = ReadNullableString(reader, "JournalNummer"),
            Status = mainStatus,
            StatusMeaning = statusMeaning,
            Severity = severity,
            CreatedDate = ReadNullableDateTime(reader, "CreatedDate"),
            UpdatedDate = ReadNullableDateTime(reader, "UpdatedDate"),
            CreatedBy = ReadNullableString(reader, "CreatedBy"),
            UpdatedBy = ReadNullableString(reader, "UpdatedBy"),
            Notes = ReadNullableString(reader, "Notes"),
        };

        // Material
        var material = new OperationsTransferMaterialDto
        {
            MaterialName = ReadNullableString(reader, "MaterialName"),
            ArticleAlias = ReadNullableString(reader, "ArticleAlias"),
            Color = ReadNullableString(reader, "Color"),
            IdArtikelVarianten = ReadNullableInt(reader, "IdArtikelVarianten"),
        };

        // Quantity
        var orderedQty = ReadNullableDouble(reader, "OrderedQuantity");
        var receivedQty = ReadNullableDouble(reader, "ReceivedQuantity");
        var openQty = (orderedQty.HasValue && receivedQty.HasValue)
            ? orderedQty.Value - receivedQty.Value
            : (double?)null;

        var quantity = new OperationsTransferQuantityDto
        {
            OrderedQuantity = orderedQty,
            ReceivedQuantity = receivedQty,
            OpenQuantity = openQty,
            PalletQuantity = ReadNullableDouble(reader, "PalletQuantity"),
        };

        // Loading (Standard)
        var loadingStatus = ReadNullableInt(reader, "LoadingStatus");
        var (loadingMeaning, _) = OperationsStatusMapper.MapLadeAuftragStatus(loadingStatus);

        var loading = new OperationsTransferLoadingDto
        {
            IdLadeAuftrag = ReadNullableInt(reader, "IdLadeAuftrag"),
            IdLadePlanung = ReadNullableInt(reader, "IdLadePlanung"),
            LadeDatum = ReadNullableDateTime(reader, "LadeDatum"),
            LoadingStatus = loadingStatus,
            LoadingStatusMeaning = loadingStatus.HasValue ? loadingMeaning : null,
            TruckNumber = ReadNullableString(reader, "TruckNumber"),
            TruckDescription = ReadNullableString(reader, "TruckDescription"),
            DeliveryNumber = ReadNullableString(reader, "DeliveryNumber"),
            DeliveryDate = ReadNullableDateTime(reader, "DeliveryDate"),
        };

        // Goods Receipt
        var receiptStatus = ReadNullableInt(reader, "ReceiptStatus");
        var (receiptMeaning, _) = OperationsStatusMapper.MapWareneingangStatus(receiptStatus);
        var receiptCount = ReadNullableInt(reader, "ReceiptCount") ?? 0;
        var receiptIsCompleted = ReadNullableInt(reader, "ReceiptIsCompleted") == 1;

        var goodsReceipt = new OperationsTransferGoodsReceiptDto
        {
            IdWareneingang = ReadNullableInt(reader, "IdWareneingang"),
            ReceiptDate = ReadNullableDateTime(reader, "ReceiptDate"),
            ReceiptStatus = receiptStatus,
            ReceiptStatusMeaning = receiptStatus.HasValue ? receiptMeaning : null,
            ReceivedQuantity = receivedQty,
            ReceiptCount = receiptCount,
            LastReceiptDate = ReadNullableDateTime(reader, "LastReceiptDate"),
            IsCompleted = receiptIsCompleted,
        };

        // Tech refs
        var techRefs = new OperationsTransferTechRefsDto
        {
            IdBestellung = header.IdBestellung,
            IdBestellPosition = ReadNullableInt(reader, "IdBestellPosition"),
            IdJournal = header.IdJournal,
            JournalNummer = header.JournalNummer,
            IdAuftragsAbruf = ReadNullableInt(reader, "IdAuftragsAbruf"),
            IdAbrufe = ReadNullableInt(reader, "IdAbrufe"),
            IdLadePlanung = loading.IdLadePlanung,
            IdLadeAuftrag = loading.IdLadeAuftrag,
            IdWareneingang = goodsReceipt.IdWareneingang,
            ReferenceNumber = ReadNullableString(reader, "ReferenceNumber"),
        };

        return new OperationsTransferDetailDto
        {
            Plant = plant.ToString(),
            PlantServer = plantServer,
            PlantDatabase = plantDatabase,
            PipelineModel = pipelineModel.ToString(),
            Header = header,
            Material = material,
            Quantity = quantity,
            Loading = loading,
            GoodsReceipt = goodsReceipt,
            TechnicalReferences = techRefs,
        };
    }

    /// <summary>
    /// Maps an Inhouse pipeline detail row.
    /// </summary>
    private static OperationsTransferDetailDto MapInhouseRow(
        SqlDataReader reader,
        AlplaProdPlant plant,
        string? plantServer,
        string? plantDatabase,
        AlplaProdPipelineModel pipelineModel)
    {
        // Header — same as Standard
        var mainStatus = ReadNullableInt(reader, "MainStatus");
        var (statusMeaning, severity) = OperationsStatusMapper.MapBestellungStatus(mainStatus);

        var header = new OperationsTransferHeaderDto
        {
            IdBestellung = reader.GetInt32(reader.GetOrdinal("IdBestellung")),
            IdJournal = ReadNullableInt(reader, "IdJournal"),
            JournalNummer = ReadNullableString(reader, "JournalNummer"),
            Status = mainStatus,
            StatusMeaning = statusMeaning,
            Severity = severity,
            CreatedDate = ReadNullableDateTime(reader, "CreatedDate"),
            UpdatedDate = ReadNullableDateTime(reader, "UpdatedDate"),
            CreatedBy = ReadNullableString(reader, "CreatedBy"),
            UpdatedBy = ReadNullableString(reader, "UpdatedBy"),
            Notes = ReadNullableString(reader, "Notes"),
        };

        // Material — same as Standard
        var material = new OperationsTransferMaterialDto
        {
            MaterialName = ReadNullableString(reader, "MaterialName"),
            ArticleAlias = ReadNullableString(reader, "ArticleAlias"),
            Color = ReadNullableString(reader, "Color"),
            IdArtikelVarianten = ReadNullableInt(reader, "IdArtikelVarianten"),
        };

        // Quantity — same as Standard
        var orderedQty = ReadNullableDouble(reader, "OrderedQuantity");
        var receivedQty = ReadNullableDouble(reader, "ReceivedQuantity");
        var openQty = (orderedQty.HasValue && receivedQty.HasValue)
            ? orderedQty.Value - receivedQty.Value
            : (double?)null;

        var quantity = new OperationsTransferQuantityDto
        {
            OrderedQuantity = orderedQty,
            ReceivedQuantity = receivedQty,
            OpenQuantity = openQty,
            PalletQuantity = ReadNullableDouble(reader, "PalletQuantity"),
        };

        // Loading — Inhouse delivery fields
        var loading = new OperationsTransferLoadingDto
        {
            IdInhouseLieferung = ReadNullableInt(reader, "IdInhouseLieferung"),
            LieferscheinDatum = ReadNullableDateTime(reader, "LieferscheinDatum"),
            ProdTag = ReadNullableDateTime(reader, "ProdTag"),
            InhouseIdJournal = ReadNullableInt(reader, "InhouseIdJournal"),
            InhouseJournalNummer = ReadNullableString(reader, "InhouseJournalNummer"),
        };

        // Goods Receipt — same as Standard
        var receiptStatus = ReadNullableInt(reader, "ReceiptStatus");
        var (receiptMeaning, _) = OperationsStatusMapper.MapWareneingangStatus(receiptStatus);
        var receiptCount = ReadNullableInt(reader, "ReceiptCount") ?? 0;
        var receiptIsCompleted = ReadNullableInt(reader, "ReceiptIsCompleted") == 1;

        var goodsReceipt = new OperationsTransferGoodsReceiptDto
        {
            IdWareneingang = ReadNullableInt(reader, "IdWareneingang"),
            ReceiptDate = ReadNullableDateTime(reader, "ReceiptDate"),
            ReceiptStatus = receiptStatus,
            ReceiptStatusMeaning = receiptStatus.HasValue ? receiptMeaning : null,
            ReceivedQuantity = receivedQty,
            ReceiptCount = receiptCount,
            LastReceiptDate = ReadNullableDateTime(reader, "LastReceiptDate"),
            IsCompleted = receiptIsCompleted,
        };

        // Tech refs
        var techRefs = new OperationsTransferTechRefsDto
        {
            IdBestellung = header.IdBestellung,
            IdBestellPosition = ReadNullableInt(reader, "IdBestellPosition"),
            IdJournal = header.IdJournal,
            JournalNummer = header.JournalNummer,
            IdInhouseLieferung = loading.IdInhouseLieferung,
            IdWareneingang = goodsReceipt.IdWareneingang,
            ReferenceNumber = ReadNullableString(reader, "ReferenceNumber"),
        };

        return new OperationsTransferDetailDto
        {
            Plant = plant.ToString(),
            PlantServer = plantServer,
            PlantDatabase = plantDatabase,
            PipelineModel = pipelineModel.ToString(),
            Header = header,
            Material = material,
            Quantity = quantity,
            Loading = loading,
            GoodsReceipt = goodsReceipt,
            TechnicalReferences = techRefs,
        };
    }

    // ─── Null-safe reader helpers ───

    private static int? ReadNullableInt(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static double? ReadNullableDouble(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return null;

        // T_Bestellpositionen.BestellMenge is FLOAT in SQL Server → maps to GetDouble
        // T_Wareneingaenge.Menge may also be FLOAT or DECIMAL
        var fieldType = reader.GetFieldType(ordinal);
        if (fieldType == typeof(decimal))
            return (double)reader.GetDecimal(ordinal);
        return reader.GetDouble(ordinal);
    }

    private static string? ReadNullableString(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime? ReadNullableDateTime(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
