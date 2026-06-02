using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Operations;

/// <summary>
/// A single normalized event in an operations transfer timeline.
///
/// Every event — regardless of source table or pipeline model — uses this same
/// shape. The frontend renders events ordered by SortOrder then EventDate.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §6
/// </summary>
public class OperationsTimelineEventDto
{
    // ─── Ordering and classification ───

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }

    [JsonPropertyName("eventCode")]
    public string EventCode { get; set; } = string.Empty;

    [JsonPropertyName("eventLabelPT")]
    public string EventLabelPT { get; set; } = string.Empty;

    [JsonPropertyName("eventLabelEN")]
    public string EventLabelEN { get; set; } = string.Empty;

    [JsonPropertyName("sourceTable")]
    public string SourceTable { get; set; } = string.Empty;

    // ─── Temporal and user data ───

    [JsonPropertyName("eventDate")]
    public DateTime? EventDate { get; set; }

    [JsonPropertyName("eventUser")]
    public string? EventUser { get; set; }

    // ─── Status interpretation ───

    [JsonPropertyName("mainStatus")]
    public int? MainStatus { get; set; }

    [JsonPropertyName("secondaryStatus")]
    public int? SecondaryStatus { get; set; }

    [JsonPropertyName("statusMeaning")]
    public string? StatusMeaning { get; set; }

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "info";

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; set; }

    [JsonPropertyName("isTechnical")]
    public bool IsTechnical { get; set; }

    // ─── Entity references ───

    [JsonPropertyName("idBestellung")]
    public int? IdBestellung { get; set; }

    [JsonPropertyName("idBestellPosition")]
    public int? IdBestellPosition { get; set; }

    [JsonPropertyName("idJournal")]
    public int? IdJournal { get; set; }

    [JsonPropertyName("journalNummer")]
    public string? JournalNummer { get; set; }

    [JsonPropertyName("idAuftragsAbruf")]
    public int? IdAuftragsAbruf { get; set; }

    [JsonPropertyName("idAbrufe")]
    public int? IdAbrufe { get; set; }

    [JsonPropertyName("idLadePlanung")]
    public int? IdLadePlanung { get; set; }

    [JsonPropertyName("idLadeAuftrag")]
    public int? IdLadeAuftrag { get; set; }

    [JsonPropertyName("idWareneingang")]
    public int? IdWareneingang { get; set; }

    [JsonPropertyName("idInhouseLieferung")]
    public int? IdInhouseLieferung { get; set; }

    // ─── Business context ───

    [JsonPropertyName("referenceNumber")]
    public string? ReferenceNumber { get; set; }

    [JsonPropertyName("materialName")]
    public string? MaterialName { get; set; }

    [JsonPropertyName("articleAlias")]
    public string? ArticleAlias { get; set; }

    [JsonPropertyName("articleVariantType")]
    public string? ArticleVariantType { get; set; }

    [JsonPropertyName("packagingName")]
    public string? PackagingName { get; set; }

    [JsonPropertyName("quantity")]
    public double? Quantity { get; set; }

    [JsonPropertyName("quantityUnit")]
    public string? QuantityUnit { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}
