using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Operations;

/// <summary>
/// Root response for the transfer detail endpoint.
///
/// GET /api/operations/transfers/{plant}/{idBestellung}/details
///
/// Returns a single-row view of a purchase order with enriched header,
/// material, quantity, loading, goods receipt, and technical reference data.
///
/// All queries are read-only SELECT. No writes are ever performed.
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §9 (Phase 6)
/// </summary>
public class OperationsTransferDetailDto
{
    [JsonPropertyName("plant")]
    public string Plant { get; set; } = string.Empty;

    [JsonPropertyName("plantServer")]
    public string? PlantServer { get; set; }

    [JsonPropertyName("plantDatabase")]
    public string? PlantDatabase { get; set; }

    [JsonPropertyName("pipelineModel")]
    public string PipelineModel { get; set; } = string.Empty;

    [JsonPropertyName("header")]
    public OperationsTransferHeaderDto Header { get; set; } = new();

    [JsonPropertyName("material")]
    public OperationsTransferMaterialDto Material { get; set; } = new();

    [JsonPropertyName("quantity")]
    public OperationsTransferQuantityDto Quantity { get; set; } = new();

    [JsonPropertyName("loading")]
    public OperationsTransferLoadingDto Loading { get; set; } = new();

    [JsonPropertyName("goodsReceipt")]
    public OperationsTransferGoodsReceiptDto GoodsReceipt { get; set; } = new();

    [JsonPropertyName("technicalReferences")]
    public OperationsTransferTechRefsDto TechnicalReferences { get; set; } = new();

    [JsonPropertyName("queryTimestamp")]
    public DateTime QueryTimestamp { get; set; }

    [JsonPropertyName("queryDurationMs")]
    public long QueryDurationMs { get; set; }
}

/// <summary>
/// PO header information from T_Bestellungen + T_EAIJournal.
/// </summary>
public class OperationsTransferHeaderDto
{
    [JsonPropertyName("idBestellung")]
    public int IdBestellung { get; set; }

    [JsonPropertyName("idJournal")]
    public int? IdJournal { get; set; }

    [JsonPropertyName("journalNummer")]
    public string? JournalNummer { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("statusMeaning")]
    public string? StatusMeaning { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("createdDate")]
    public DateTime? CreatedDate { get; set; }

    [JsonPropertyName("updatedDate")]
    public DateTime? UpdatedDate { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>
/// Material/article information from T_Bestellpositionen + T_Artikelvarianten.
///
/// NOTE: Represents the FIRST position only (OUTER APPLY TOP 1).
/// POs with multiple positions may have different materials in other line items.
/// </summary>
public class OperationsTransferMaterialDto
{
    [JsonPropertyName("materialName")]
    public string? MaterialName { get; set; }

    [JsonPropertyName("articleAlias")]
    public string? ArticleAlias { get; set; }

    /// <summary>Always null in this MVP. IdArtikelvariantenTyp not confirmed in schema.</summary>
    [JsonPropertyName("articleVariantType")]
    public string? ArticleVariantType { get; set; }

    /// <summary>Always null in this MVP. T_Artikeltyp join deferred.</summary>
    [JsonPropertyName("articleTypeName")]
    public string? ArticleTypeName { get; set; }

    /// <summary>Always null in this MVP. T_ArtikelKlassifikationen join deferred.</summary>
    [JsonPropertyName("classification")]
    public string? Classification { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("idArtikelVarianten")]
    public int? IdArtikelVarianten { get; set; }
}

/// <summary>
/// Quantity information from T_Bestellpositionen and T_Wareneingaenge aggregates.
///
/// NOTE: OrderedQuantity comes from the first/representative position.
/// ReceivedQuantity is an aggregate SUM from T_Wareneingaenge where available.
/// If not reliable, values are null rather than misleading 0.
/// </summary>
public class OperationsTransferQuantityDto
{
    [JsonPropertyName("orderedQuantity")]
    public double? OrderedQuantity { get; set; }

    [JsonPropertyName("deliveredQuantity")]
    public double? DeliveredQuantity { get; set; }

    [JsonPropertyName("receivedQuantity")]
    public double? ReceivedQuantity { get; set; }

    /// <summary>Computed as OrderedQuantity - ReceivedQuantity when both are available.</summary>
    [JsonPropertyName("openQuantity")]
    public double? OpenQuantity { get; set; }

    /// <summary>Always null in this MVP. T_Bestellpositionen has no unit column.</summary>
    [JsonPropertyName("quantityUnit")]
    public string? QuantityUnit { get; set; }

    [JsonPropertyName("palletQuantity")]
    public double? PalletQuantity { get; set; }

    /// <summary>Always null in this MVP. T_VpkVorschrift join deferred.</summary>
    [JsonPropertyName("packagingName")]
    public string? PackagingName { get; set; }
}

/// <summary>
/// Loading/delivery information.
///
/// For STANDARD pipeline (VIANA1/VIANA2): from T_LadeAuftraege / T_LadePlanungen
/// via the T_EAIJournalPosition → T_Abrufe bridge.
///
/// For INHOUSE pipeline (VIANA3): from T_InhouseLieferungen via IdJournal.
///
/// All fields null if the PO has not reached loading/delivery stage.
/// </summary>
public class OperationsTransferLoadingDto
{
    // ─── Standard pipeline fields ───

    [JsonPropertyName("idLadeAuftrag")]
    public int? IdLadeAuftrag { get; set; }

    [JsonPropertyName("idLadePlanung")]
    public int? IdLadePlanung { get; set; }

    [JsonPropertyName("ladeDatum")]
    public DateTime? LadeDatum { get; set; }

    [JsonPropertyName("loadingStatus")]
    public int? LoadingStatus { get; set; }

    [JsonPropertyName("loadingStatusMeaning")]
    public string? LoadingStatusMeaning { get; set; }

    [JsonPropertyName("truckNumber")]
    public string? TruckNumber { get; set; }

    [JsonPropertyName("truckDescription")]
    public string? TruckDescription { get; set; }

    [JsonPropertyName("deliveryNumber")]
    public string? DeliveryNumber { get; set; }

    [JsonPropertyName("deliveryDate")]
    public DateTime? DeliveryDate { get; set; }

    // ─── Inhouse pipeline fields ───

    [JsonPropertyName("idInhouseLieferung")]
    public int? IdInhouseLieferung { get; set; }

    [JsonPropertyName("lieferscheinDatum")]
    public DateTime? LieferscheinDatum { get; set; }

    [JsonPropertyName("prodTag")]
    public DateTime? ProdTag { get; set; }

    [JsonPropertyName("inhouseIdJournal")]
    public int? InhouseIdJournal { get; set; }

    [JsonPropertyName("inhouseJournalNummer")]
    public string? InhouseJournalNummer { get; set; }
}

/// <summary>
/// Goods receipt summary from T_Wareneingaenge.
///
/// Aggregated across all receipt records for the PO's positions.
/// </summary>
public class OperationsTransferGoodsReceiptDto
{
    [JsonPropertyName("idWareneingang")]
    public int? IdWareneingang { get; set; }

    [JsonPropertyName("receiptDate")]
    public DateTime? ReceiptDate { get; set; }

    [JsonPropertyName("receiptStatus")]
    public int? ReceiptStatus { get; set; }

    [JsonPropertyName("receiptStatusMeaning")]
    public string? ReceiptStatusMeaning { get; set; }

    [JsonPropertyName("receivedQuantity")]
    public double? ReceivedQuantity { get; set; }

    [JsonPropertyName("receiptCount")]
    public int ReceiptCount { get; set; }

    [JsonPropertyName("lastReceiptDate")]
    public DateTime? LastReceiptDate { get; set; }

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; set; }
}

/// <summary>
/// Technical reference IDs for debugging and cross-referencing.
/// Collapsed by default in the frontend UI.
/// </summary>
public class OperationsTransferTechRefsDto
{
    [JsonPropertyName("idBestellung")]
    public int IdBestellung { get; set; }

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

    [JsonPropertyName("referenceNumber")]
    public string? ReferenceNumber { get; set; }
}
