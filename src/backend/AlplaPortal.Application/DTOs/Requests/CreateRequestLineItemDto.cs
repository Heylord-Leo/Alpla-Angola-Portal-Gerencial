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

    [Required(ErrorMessage = "O preço unitário é obrigatório.")]
    [Range(0, double.MaxValue, ErrorMessage = "O preço unitário não pode ser negativo.")]
    public decimal? UnitPrice { get; set; }

    public int? CurrencyId { get; set; }

    public int? PlantId { get; set; }

    [Required(ErrorMessage = "O centro de custo é obrigatório.")]
    public int? CostCenterId { get; set; }

    [Required(ErrorMessage = "A taxa de IVA é obrigatória.")]
    public int? IvaRateId { get; set; }

    [MaxLength(200)]
    public string? SupplierName { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime? DueDate { get; set; }
}
