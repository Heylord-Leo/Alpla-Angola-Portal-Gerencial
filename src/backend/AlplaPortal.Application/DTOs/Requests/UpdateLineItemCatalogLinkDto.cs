namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// The catalogue linkage of a single line item, and nothing else.
/// </summary>
///
/// <remarks>
/// One nullable field on purpose. A wider DTO here would invite callers to "just also send" the
/// quantity or the total while they are at it, which is precisely what reconciliation must never
/// touch. Null clears the link and returns the line to free text.
/// </remarks>
public class UpdateLineItemCatalogLinkDto
{
    public int? ItemCatalogId { get; set; }
}
