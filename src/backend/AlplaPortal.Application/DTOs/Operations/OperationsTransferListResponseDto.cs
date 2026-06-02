using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Operations;

/// <summary>
/// Paginated response wrapper for the transfer list endpoint.
///
/// Contains filter metadata, pagination info, and the list of transfer items.
/// Items is never null — it defaults to an empty list.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §8
/// </summary>
public class OperationsTransferListResponseDto
{
    [JsonPropertyName("plant")]
    public string Plant { get; set; } = string.Empty;

    [JsonPropertyName("pipelineModel")]
    public string PipelineModel { get; set; } = string.Empty;

    [JsonPropertyName("dateFrom")]
    public DateTime DateFrom { get; set; }

    [JsonPropertyName("dateTo")]
    public DateTime DateTo { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("items")]
    public List<OperationsTransferListItemDto> Items { get; set; } = new();

    [JsonPropertyName("queryTimestamp")]
    public DateTime QueryTimestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("queryDurationMs")]
    public long QueryDurationMs { get; set; }
}
