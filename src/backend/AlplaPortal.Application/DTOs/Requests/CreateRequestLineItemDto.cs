using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests;

public class CreateRequestLineItemDto
{
    [Required(ErrorMessage = "A descrição é obrigatória.")]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Business priority: HIGH, MEDIUM, or LOW. Defaults to MEDIUM.</summary>
    public string ItemPriority { get; set; } = "MEDIUM";

    [Required(ErrorMessage = "A quantidade é obrigatória.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "A quantidade deve ser maior que zero.")]
    public decimal? Quantity { get; set; }

    public int? UnitId { get; set; }

    public decimal? UnitPrice { get; set; }

    public int? CurrencyId { get; set; }

    public int? PlantId { get; set; }
    
    public int? CostCenterId { get; set; }

    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }

    public int? IvaRateId { get; set; }

    /// <summary>Optional reference to catalog item. Null = manual/free-text item.</summary>
    public int? ItemCatalogId { get; set; }

    [MaxLength(200)]
    public string? SupplierName { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime? DueDate { get; set; }
}
