using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Payload for the buyer-reconciliation workaround: create a *requested* line item from a
/// proforma/OCR line to cover an omitted item (or an old item-less request).
/// Distinct from EXTRA_ITEM: this materializes a real RequestLineItem immediately
/// (QUOTATION_PENDING) without depending on approver acceptance.
/// </summary>
public class CreateLineItemFromProformaDto
{
    [Required(ErrorMessage = "A descrição é obrigatória.")]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "A quantidade é obrigatória.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "A quantidade deve ser maior que zero.")]
    public decimal? Quantity { get; set; }

    [Required(ErrorMessage = "A unidade é obrigatória.")]
    public int? UnitId { get; set; }

    /// <summary>Optional catalog reference carried over from the proforma line.</summary>
    public int? ItemCatalogId { get; set; }

    /// <summary>The proforma attachment the line was derived from (provenance + duplicate detection).</summary>
    public Guid? SourceProformaAttachmentId { get; set; }

    /// <summary>
    /// Client-generated UUID identifying THIS single create operation. Reused verbatim on retries
    /// so double-click / retry / network-failure re-send return the same line instead of duplicating.
    /// </summary>
    [Required(ErrorMessage = "A chave de idempotência é obrigatória.")]
    [MaxLength(100)]
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// When true, the buyer explicitly confirmed creating despite a probable (non-unambiguous)
    /// duplicate detected across sessions. Ignored for unambiguous duplicates (always deduped).
    /// </summary>
    public bool ConfirmCreateDespiteDuplicate { get; set; }
}
