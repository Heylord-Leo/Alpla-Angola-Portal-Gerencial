using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Operations;

/// <summary>
/// Root response for the Live Transfer Board endpoint.
///
/// GET /api/operations/live-board?plant=VIANA1
///
/// Returns a TV-ready payload with pre-classified inbound/outbound
/// transfer cards, each containing a simplified mini-timeline.
///
/// All queries are read-only SELECT. No writes are ever performed.
///
/// Design reference: docs/OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md §9
/// </summary>
public class OperationsLiveBoardResponseDto
{
    [JsonPropertyName("plant")]
    public string Plant { get; set; } = string.Empty;

    [JsonPropertyName("plantName")]
    public string PlantName { get; set; } = string.Empty;

    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; }

    [JsonPropertyName("refreshSeconds")]
    public int RefreshSeconds { get; set; }

    [JsonPropertyName("summary")]
    public OperationsLiveBoardSummaryDto Summary { get; set; } = new();

    [JsonPropertyName("inbound")]
    public List<OperationsLiveBoardTransferDto> Inbound { get; set; } = new();

    [JsonPropertyName("outbound")]
    public List<OperationsLiveBoardTransferDto> Outbound { get; set; } = new();

    [JsonPropertyName("queryDurationMs")]
    public long QueryDurationMs { get; set; }
}

/// <summary>
/// Summary counters for the Live Board header/footer bar.
/// </summary>
public class OperationsLiveBoardSummaryDto
{
    [JsonPropertyName("inboundTotal")]
    public int InboundTotal { get; set; }

    [JsonPropertyName("inboundActive")]
    public int InboundActive { get; set; }

    [JsonPropertyName("outboundTotal")]
    public int OutboundTotal { get; set; }

    [JsonPropertyName("outboundActive")]
    public int OutboundActive { get; set; }

    [JsonPropertyName("attentionCount")]
    public int AttentionCount { get; set; }

    [JsonPropertyName("completedRecentCount")]
    public int CompletedRecentCount { get; set; }
}

/// <summary>
/// A single transfer card for the Live Board.
///
/// Contains pre-simplified stage, direction, quantity, and mini-timeline
/// steps — ready for direct TV rendering without further business logic.
///
/// Attention flags and reasons are pre-computed server-side.
/// </summary>
public class OperationsLiveBoardTransferDto
{
    [JsonPropertyName("idBestellung")]
    public int IdBestellung { get; set; }

    [JsonPropertyName("journalNummer")]
    public string? JournalNummer { get; set; }

    // ─── Direction ───

    [JsonPropertyName("originPlant")]
    public string OriginPlant { get; set; } = string.Empty;

    [JsonPropertyName("originPlantName")]
    public string OriginPlantName { get; set; } = string.Empty;

    [JsonPropertyName("destinationPlant")]
    public string DestinationPlant { get; set; } = string.Empty;

    [JsonPropertyName("destinationPlantName")]
    public string DestinationPlantName { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = string.Empty; // INBOUND / OUTBOUND

    // ─── Material ───

    [JsonPropertyName("materialName")]
    public string? MaterialName { get; set; }

    // ─── Quantity ───

    [JsonPropertyName("orderedQuantity")]
    public double? OrderedQuantity { get; set; }

    [JsonPropertyName("receivedQuantity")]
    public double? ReceivedQuantity { get; set; }

    [JsonPropertyName("openQuantity")]
    public double? OpenQuantity { get; set; }

    [JsonPropertyName("quantityUnit")]
    public string? QuantityUnit { get; set; }

    // ─── Stage ───

    [JsonPropertyName("currentStage")]
    public string CurrentStage { get; set; } = string.Empty; // ORDERED, SENT, RECEIVING, PARTIAL, COMPLETED, ERROR

    [JsonPropertyName("currentStageLabel")]
    public string CurrentStageLabel { get; set; } = string.Empty;

    [JsonPropertyName("statusColor")]
    public string StatusColor { get; set; } = string.Empty; // info, warning, success, error

    // ─── Attention ───

    [JsonPropertyName("isAttention")]
    public bool IsAttention { get; set; }

    [JsonPropertyName("attentionReason")]
    public string? AttentionReason { get; set; }

    // ─── Timing ───

    [JsonPropertyName("lastEventAt")]
    public DateTime? LastEventAt { get; set; }

    [JsonPropertyName("ageMinutes")]
    public int? AgeMinutes { get; set; }

    // ─── Mini Timeline Steps ───

    [JsonPropertyName("steps")]
    public List<OperationsLiveBoardStepDto> Steps { get; set; } = new();
}

/// <summary>
/// A single step in the mini-timeline of a Live Board transfer card.
///
/// State transitions: pending → active → done
/// </summary>
public class OperationsLiveBoardStepDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty; // ORDERED, SENT, RECEIVING, PARTIAL, COMPLETED

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty; // Pedido, Enviado, Recebimento, Parcial, Concluído

    [JsonPropertyName("state")]
    public string State { get; set; } = "pending"; // done, active, pending
}
