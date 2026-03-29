using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests;

public class UpdateRequestLineItemDto
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

    [Required(ErrorMessage = "A planta de destino é obrigatória.")]
    public int? PlantId { get; set; }

    [MaxLength(200)]
    public string? SupplierName { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}
