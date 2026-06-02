using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Operations;

/// <summary>
/// Response wrapper for a transfer timeline query.
///
/// Contains metadata about the plant/pipeline, query performance metrics,
/// and the ordered list of timeline events.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §6
/// </summary>
public class OperationsTimelineResponseDto
{
    [JsonPropertyName("plant")]
    public string Plant { get; set; } = string.Empty;

    [JsonPropertyName("plantServer")]
    public string? PlantServer { get; set; }

    [JsonPropertyName("plantDatabase")]
    public string? PlantDatabase { get; set; }

    [JsonPropertyName("idBestellung")]
    public int IdBestellung { get; set; }

    [JsonPropertyName("journalNummer")]
    public string? JournalNummer { get; set; }

    [JsonPropertyName("pipelineModel")]
    public string PipelineModel { get; set; } = string.Empty;

    [JsonPropertyName("expectedEventCount")]
    public int ExpectedEventCount { get; set; }

    [JsonPropertyName("completedEventCount")]
    public int CompletedEventCount { get; set; }

    [JsonPropertyName("events")]
    public List<OperationsTimelineEventDto> Events { get; set; } = new();

    [JsonPropertyName("queryTimestamp")]
    public DateTime QueryTimestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("queryDurationMs")]
    public long QueryDurationMs { get; set; }
}
