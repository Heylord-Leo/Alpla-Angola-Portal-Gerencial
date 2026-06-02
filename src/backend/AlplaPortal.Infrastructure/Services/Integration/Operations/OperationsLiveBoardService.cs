using System.Diagnostics;
using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Application.Interfaces.Operations;
using AlplaPortal.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Orchestrates Live Board queries against AlplaPROD databases.
///
/// Flow:
///   1. Detect pipeline model via <see cref="IOperationsPipelineDetector"/>
///   2. Open read-only connection via <see cref="AlplaProdConnectionFactory"/>
///   3. Execute optimized single-pass Live Board query
///   4. Map rows to <see cref="OperationsLiveBoardTransferDto"/> with stage classification
///   5. Split into inbound/outbound based on plant direction rules
///   6. Apply attention detection and priority sorting
///   7. Return <see cref="OperationsLiveBoardResponseDto"/>
///
/// All queries are read-only SELECT. No writes are ever performed.
///
/// Design reference: docs/OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md §9–§14
/// </summary>
public class OperationsLiveBoardService : IOperationsLiveBoardService
{
    private readonly AlplaProdConnectionFactory _factory;
    private readonly IOperationsPipelineDetector _pipelineDetector;
    private readonly ILogger<OperationsLiveBoardService> _logger;

    // ─── Attention thresholds (minutes) ───
    private const int AttentionReceivingMinutes = 240;    // 4 hours
    private const int AttentionPartialMinutes = 480;      // 8 hours
    private const int AttentionOrderedMinutes = 1440;     // 24 hours
    private const int AttentionCriticalMinutes = 2880;    // 48 hours

    // ─── Plant display names ───
    private static readonly Dictionary<AlplaProdPlant, string> PlantNames = new()
    {
        { AlplaProdPlant.VIANA1, "Viana 1" },
        { AlplaProdPlant.VIANA2, "Viana 2" },
        { AlplaProdPlant.VIANA3, "Viana 3" },
    };

    // ─── Stage definitions ───
    private static readonly List<(string Code, string Label)> StageDefinitions = new()
    {
        ("ORDERED",   "Pedido"),
        ("SENT",      "Enviado"),
        ("RECEIVING", "Recebimento"),
        ("PARTIAL",   "Parcial"),
        ("COMPLETED", "Concluído"),
    };

    // ─── Stage labels (full form for currentStageLabel) ───
    private static readonly Dictionary<string, string> StageLabels = new()
    {
        { "ORDERED",   "Pedido criado" },
        { "SENT",      "Enviado" },
        { "RECEIVING", "Aguardando recebimento" },
        { "PARTIAL",   "Parcialmente recebido" },
        { "COMPLETED", "Concluído" },
        { "ERROR",     "Atenção" },
    };

    // ─── Stage colors ───
    private static readonly Dictionary<string, string> StageColors = new()
    {
        { "ORDERED",   "info" },
        { "SENT",      "info" },
        { "RECEIVING", "warning" },
        { "PARTIAL",   "warning" },
        { "COMPLETED", "success" },
        { "ERROR",     "error" },
    };

    // ─── Stage priority for sorting (lower = higher priority) ───
    private static readonly Dictionary<string, int> StagePriority = new()
    {
        { "ERROR",     0 },
        { "RECEIVING", 1 },
        { "PARTIAL",   2 },
        { "SENT",      3 },
        { "ORDERED",   4 },
        { "COMPLETED", 5 },
    };

    public OperationsLiveBoardService(
        AlplaProdConnectionFactory factory,
        IOperationsPipelineDetector pipelineDetector,
        ILogger<OperationsLiveBoardService> logger)
    {
        _factory = factory;
        _pipelineDetector = pipelineDetector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationsLiveBoardResponseDto> GetLiveBoardAsync(
        AlplaProdPlant plant,
        int refreshSeconds = 60,
        int maxInbound = 6,
        int maxOutbound = 6,
        bool includeRecentlyCompleted = true,
        int completedWindowHours = 4,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var pipelineModel = await _pipelineDetector.DetectPipelineModelAsync(plant, ct);
        var plantName = PlantNames.GetValueOrDefault(plant, plant.ToString());

        _logger.LogInformation(
            "[Operations] Live Board requested: Plant={Plant}, Pipeline={Pipeline}, MaxIn={MaxIn}, MaxOut={MaxOut}, CompletedHrs={Hrs}",
            plant, pipelineModel, maxInbound, maxOutbound, completedWindowHours);

        // ─── Query ───

        await using var connection = await _factory.CreateConnectionAsync(plant, ct);

        var sql = pipelineModel switch
        {
            AlplaProdPipelineModel.INHOUSE => OperationsLiveBoardQueryBuilder.BuildInhouseLiveBoardQuery(),
            _ => OperationsLiveBoardQueryBuilder.BuildStandardLiveBoardQuery(),
        };

        var completedCutoff = includeRecentlyCompleted
            ? DateTime.UtcNow.AddHours(-completedWindowHours)
            : DateTime.UtcNow; // effectively excludes all completed

        var maxRows = maxInbound + maxOutbound + 10; // extra buffer for direction split

        await using var cmd = new SqlCommand(sql, connection);
        cmd.CommandTimeout = 30;
        cmd.Parameters.AddWithValue("@CompletedCutoff", completedCutoff);
        cmd.Parameters.AddWithValue("@MaxRows", maxRows);

        var allTransfers = new List<OperationsLiveBoardTransferDto>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var transfer = MapRow(reader, plant);
            allTransfers.Add(transfer);
        }

        sw.Stop();

        _logger.LogInformation(
            "[Operations] Live Board query completed: Plant={Plant}, Rows={Count}, Duration={Ms}ms",
            plant, allTransfers.Count, sw.ElapsedMilliseconds);

        // ─── Classify direction ───
        // MVP: For VIANA1, all transfers in the local DB are conceptually relevant.
        // Direction determination is approximate — see design doc §8.4.
        // For now, classify by PO status and stage heuristics.
        ClassifyDirection(allTransfers, plant);

        // ─── Apply attention flags ───
        foreach (var t in allTransfers)
        {
            ApplyAttention(t);
        }

        // ─── Sort by priority ───
        allTransfers.Sort((a, b) =>
        {
            // Attention first
            if (a.IsAttention != b.IsAttention)
                return a.IsAttention ? -1 : 1;

            // Then by stage priority
            var pa = StagePriority.GetValueOrDefault(a.CurrentStage, 5);
            var pb = StagePriority.GetValueOrDefault(b.CurrentStage, 5);
            if (pa != pb) return pa.CompareTo(pb);

            // Then by most recent event
            return (b.LastEventAt ?? DateTime.MinValue).CompareTo(a.LastEventAt ?? DateTime.MinValue);
        });

        // ─── Split into inbound/outbound ───
        var inbound = allTransfers
            .Where(t => t.Direction == "INBOUND")
            .Take(maxInbound)
            .ToList();

        var outbound = allTransfers
            .Where(t => t.Direction == "OUTBOUND")
            .Take(maxOutbound)
            .ToList();

        var allInbound = allTransfers.Where(t => t.Direction == "INBOUND").ToList();
        var allOutbound = allTransfers.Where(t => t.Direction == "OUTBOUND").ToList();

        // ─── Build summary ───
        var summary = new OperationsLiveBoardSummaryDto
        {
            InboundTotal = allInbound.Count,
            InboundActive = allInbound.Count(t => t.CurrentStage != "COMPLETED"),
            OutboundTotal = allOutbound.Count,
            OutboundActive = allOutbound.Count(t => t.CurrentStage != "COMPLETED"),
            AttentionCount = allTransfers.Count(t => t.IsAttention),
            CompletedRecentCount = allTransfers.Count(t => t.CurrentStage == "COMPLETED"),
        };

        return new OperationsLiveBoardResponseDto
        {
            Plant = plant.ToString(),
            PlantName = plantName,
            LastUpdated = DateTime.UtcNow,
            RefreshSeconds = refreshSeconds,
            Summary = summary,
            Inbound = inbound,
            Outbound = outbound,
            QueryDurationMs = sw.ElapsedMilliseconds,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Row Mapping
    // ═══════════════════════════════════════════════════════════════════════

    private OperationsLiveBoardTransferDto MapRow(SqlDataReader reader, AlplaProdPlant boardPlant)
    {
        var idBestellung = reader.GetInt32(reader.GetOrdinal("IdBestellung"));
        var mainStatus = reader.IsDBNull(reader.GetOrdinal("MainStatus"))
            ? (int?)null : reader.GetInt32(reader.GetOrdinal("MainStatus"));
        var orderedQty = reader.IsDBNull(reader.GetOrdinal("OrderedQuantity"))
            ? (double?)null : Convert.ToDouble(reader["OrderedQuantity"]);
        var receivedQty = reader.IsDBNull(reader.GetOrdinal("ReceivedQuantity"))
            ? (double?)null : Convert.ToDouble(reader["ReceivedQuantity"]);
        var journalNummer = reader.IsDBNull(reader.GetOrdinal("JournalNummer"))
            ? null : reader.GetString(reader.GetOrdinal("JournalNummer"));
        var materialName = reader.IsDBNull(reader.GetOrdinal("MaterialName"))
            ? null : reader.GetString(reader.GetOrdinal("MaterialName"));
        var lastEventAt = reader.IsDBNull(reader.GetOrdinal("LastEventAt"))
            ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("LastEventAt"));
        var hasEdiSync = reader.GetInt32(reader.GetOrdinal("HasEdiSync")) == 1;
        var hasLoadingOrder = reader.GetInt32(reader.GetOrdinal("HasLoadingOrder")) == 1;
        var hasGoodsReceipt = reader.GetInt32(reader.GetOrdinal("HasGoodsReceipt")) == 1;
        var grMaxStatus = reader.IsDBNull(reader.GetOrdinal("GrMaxStatus"))
            ? (int?)null : reader.GetInt32(reader.GetOrdinal("GrMaxStatus"));

        // ─── Compute quantities ───
        double? openQty = null;
        if (orderedQty.HasValue && receivedQty.HasValue)
        {
            openQty = Math.Max(0, orderedQty.Value - receivedQty.Value);
        }
        else if (orderedQty.HasValue && !receivedQty.HasValue)
        {
            openQty = orderedQty.Value;
        }

        // ─── Derive stage ───
        var stage = DeriveStage(mainStatus, hasEdiSync, hasLoadingOrder, hasGoodsReceipt,
            grMaxStatus, orderedQty, receivedQty, openQty);

        // ─── Build steps ───
        var steps = BuildSteps(stage);

        // ─── Age ───
        int? ageMinutes = null;
        if (lastEventAt.HasValue)
        {
            ageMinutes = (int)(DateTime.UtcNow - lastEventAt.Value).TotalMinutes;
            if (ageMinutes < 0) ageMinutes = 0;
        }

        return new OperationsLiveBoardTransferDto
        {
            IdBestellung = idBestellung,
            JournalNummer = journalNummer,
            MaterialName = materialName,
            OrderedQuantity = orderedQty,
            ReceivedQuantity = receivedQty,
            OpenQuantity = openQty,
            QuantityUnit = null, // Not available in schema
            CurrentStage = stage,
            CurrentStageLabel = StageLabels.GetValueOrDefault(stage, stage),
            StatusColor = StageColors.GetValueOrDefault(stage, "info"),
            LastEventAt = lastEventAt,
            AgeMinutes = ageMinutes,
            Steps = steps,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Stage Derivation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Derives the simplified Live Board stage from raw query indicators.
    ///
    /// Priority rules (from design doc §6.2 + §8.7):
    ///   1. PO cancelled/error → ERROR
    ///   2. PO completed (status 7/8) AND (received >= ordered OR open = 0) → COMPLETED
    ///   3. PO partially delivered (status 5) OR (received > 0 AND received < ordered) → PARTIAL
    ///   4. Has goods receipt (pending) → RECEIVING
    ///   5. Has EDI sync or loading/inhouse delivery → SENT
    ///   6. Fallback → ORDERED
    /// </summary>
    private static string DeriveStage(
        int? mainStatus,
        bool hasEdiSync, bool hasLoadingOrder, bool hasGoodsReceipt,
        int? grMaxStatus,
        double? orderedQty, double? receivedQty, double? openQty)
    {
        // Rule 1: Cancelled / error
        if (mainStatus == 3) return "ERROR";

        // Rule 2: Completed
        if (mainStatus is 7 or 8)
        {
            // Verify with quantity data if available
            if (receivedQty.HasValue && orderedQty.HasValue && receivedQty.Value > 0 && receivedQty.Value < orderedQty.Value)
                return "PARTIAL"; // PO marked complete but quantities don't match
            return "COMPLETED";
        }

        // Rule 3: Partial delivery
        if (mainStatus == 5)
            return "PARTIAL";

        if (receivedQty.HasValue && orderedQty.HasValue && receivedQty.Value > 0 && receivedQty.Value < orderedQty.Value)
            return "PARTIAL";

        // Rule 4: Goods receipt exists (but not completed)
        if (hasGoodsReceipt)
        {
            if (grMaxStatus == 21)
            {
                // GR completed but PO not marked complete — likely partial
                if (receivedQty.HasValue && orderedQty.HasValue && receivedQty.Value < orderedQty.Value)
                    return "PARTIAL";
                // GR completed and quantities match
                return "COMPLETED";
            }
            return "RECEIVING";
        }

        // Rule 5: Sent (EDI sync or loading/inhouse delivery)
        if (hasLoadingOrder || hasEdiSync)
            return "SENT";

        // Rule 6: Fallback
        return "ORDERED";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Step Generation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a consistent 5-step mini-timeline for a transfer card.
    ///
    /// Steps: ORDERED → SENT → RECEIVING → PARTIAL → COMPLETED
    /// Each step is: done (past), active (current), or pending (future).
    /// </summary>
    private static List<OperationsLiveBoardStepDto> BuildSteps(string currentStage)
    {
        var stageIndex = StageDefinitions.FindIndex(s => s.Code == currentStage);
        if (stageIndex < 0)
        {
            // ERROR or unknown: mark all as pending except first
            return StageDefinitions.Select((s, i) => new OperationsLiveBoardStepDto
            {
                Code = s.Code,
                Label = s.Label,
                State = i == 0 ? "done" : "pending",
            }).ToList();
        }

        return StageDefinitions.Select((s, i) => new OperationsLiveBoardStepDto
        {
            Code = s.Code,
            Label = s.Label,
            State = i < stageIndex ? "done" : (i == stageIndex ? "active" : "pending"),
        }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Direction Classification
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Classifies transfers as INBOUND or OUTBOUND relative to the board plant.
    ///
    /// MVP approximation (v2.177.0):
    ///   The AlplaPROD schema does not have explicit origin/destination fields per PO.
    ///   Each plant database contains POs that are relevant to that plant.
    ///
    ///   For MVP, we use a simple heuristic:
    ///   - All transfers queried from the board plant's database are classified
    ///     as INBOUND (the plant is the receiver/destination).
    ///   - The origin plant is set to a partner plant based on known routes.
    ///
    ///   This is a known approximation. See design doc §8.4 for the target behavior.
    ///
    /// Known routes:
    ///   VIANA1 DB → POs are purchases arriving at VIANA1 (from VIANA2)
    ///   VIANA2 DB → POs are purchases arriving at VIANA2 (from VIANA1)
    ///   VIANA3 DB → POs are inhouse deliveries arriving at VIANA3 (from VIANA1)
    /// </summary>
    private void ClassifyDirection(List<OperationsLiveBoardTransferDto> transfers, AlplaProdPlant boardPlant)
    {
        var boardPlantCode = boardPlant.ToString();
        var boardPlantName = PlantNames.GetValueOrDefault(boardPlant, boardPlantCode);

        // MVP: determine partner plant based on known routes
        var (partnerCode, partnerName) = boardPlant switch
        {
            AlplaProdPlant.VIANA1 => ("VIANA2", "Viana 2"),
            AlplaProdPlant.VIANA2 => ("VIANA1", "Viana 1"),
            AlplaProdPlant.VIANA3 => ("VIANA1", "Viana 1"),
            _ => ("UNKNOWN", "Desconhecido"),
        };

        foreach (var t in transfers)
        {
            // MVP: POs in a plant's DB are purchases arriving at that plant
            t.Direction = "INBOUND";
            t.OriginPlant = partnerCode;
            t.OriginPlantName = partnerName;
            t.DestinationPlant = boardPlantCode;
            t.DestinationPlantName = boardPlantName;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Attention Detection
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies attention flags based on age and current stage.
    /// See design doc §14 for threshold rationale.
    /// </summary>
    private static void ApplyAttention(OperationsLiveBoardTransferDto transfer)
    {
        if (transfer.CurrentStage == "COMPLETED")
        {
            transfer.IsAttention = false;
            transfer.AttentionReason = null;
            return;
        }

        var age = transfer.AgeMinutes ?? 0;

        // Critical: any non-completed stage > 48 hours
        if (age > AttentionCriticalMinutes)
        {
            transfer.IsAttention = true;
            transfer.AttentionReason = $"Transferência sem progresso há mais de {age / 60} horas";
            transfer.StatusColor = "error";
            return;
        }

        // Stage-specific thresholds
        switch (transfer.CurrentStage)
        {
            case "RECEIVING" when age > AttentionReceivingMinutes:
                transfer.IsAttention = true;
                transfer.AttentionReason = $"Aguardando recebimento há {age / 60}h";
                break;

            case "PARTIAL" when age > AttentionPartialMinutes:
                transfer.IsAttention = true;
                transfer.AttentionReason = $"Parcialmente recebido há {age / 60}h — saldo em aberto";
                break;

            case "ORDERED" when age > AttentionOrderedMinutes:
                transfer.IsAttention = true;
                transfer.AttentionReason = $"Pedido criado há {age / 60}h sem progresso";
                break;
        }
    }
}
