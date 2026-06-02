using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Operations;

/// <summary>
/// Represents a single transfer/purchase order in the paginated list response.
///
/// Each item represents one distinct IdBestellung from T_Bestellungen,
/// enriched with the first available line item data (T_Bestellpositionen TOP 1).
///
/// Note — MVP limitations:
///   • CompletedEventCount is always null — computing per-row requires the full
///     timeline UNION ALL query, which is too expensive for list queries.
///     Use the timeline detail endpoint for accurate event completion.
///   • PackagingName is always null — T_VpkVorschrift join path is unreliable
///     across plants. Deferred until validated.
///   • QuantityUnit is always null — T_Bestellpositionen does not have a unit column.
///   • MaterialName/ArticleAlias/ArticleVariantType come from the first position only.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §8
/// </summary>
public class OperationsTransferListItemDto
{
    [JsonPropertyName("plant")]
    public string Plant { get; set; } = string.Empty;

    [JsonPropertyName("plantServer")]
    public string? PlantServer { get; set; }

    [JsonPropertyName("plantDatabase")]
    public string? PlantDatabase { get; set; }

    [JsonPropertyName("idBestellung")]
    public int IdBestellung { get; set; }

    [JsonPropertyName("idJournal")]
    public int? IdJournal { get; set; }

    [JsonPropertyName("journalNummer")]
    public string? JournalNummer { get; set; }

    [JsonPropertyName("pipelineModel")]
    public string PipelineModel { get; set; } = string.Empty;

    [JsonPropertyName("createdDate")]
    public DateTime? CreatedDate { get; set; }

    [JsonPropertyName("updatedDate")]
    public DateTime? UpdatedDate { get; set; }

    [JsonPropertyName("mainStatus")]
    public int? MainStatus { get; set; }

    [JsonPropertyName("statusMeaning")]
    public string? StatusMeaning { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("materialName")]
    public string? MaterialName { get; set; }

    [JsonPropertyName("articleAlias")]
    public string? ArticleAlias { get; set; }

    [JsonPropertyName("articleVariantType")]
    public string? ArticleVariantType { get; set; }

    /// <summary>Always null in this MVP. T_VpkVorschrift join deferred.</summary>
    [JsonPropertyName("packagingName")]
    public string? PackagingName { get; set; }

    [JsonPropertyName("quantity")]
    public double? Quantity { get; set; }

    /// <summary>Always null in this MVP. T_Bestellpositionen has no unit column.</summary>
    [JsonPropertyName("quantityUnit")]
    public string? QuantityUnit { get; set; }

    [JsonPropertyName("expectedEventCount")]
    public int ExpectedEventCount { get; set; }

    /// <summary>Always null in list results. Use the timeline endpoint for accurate completion data.</summary>
    [JsonPropertyName("completedEventCount")]
    public int? CompletedEventCount { get; set; }

    [JsonPropertyName("referenceNumber")]
    public string? ReferenceNumber { get; set; }
}
