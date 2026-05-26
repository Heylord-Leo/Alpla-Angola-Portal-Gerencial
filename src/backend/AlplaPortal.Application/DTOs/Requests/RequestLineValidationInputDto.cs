using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests;

public class RequestLineValidationInputDto
{
    [Required]
    public int CompanyId { get; set; }

    [Required]
    public string ItemCatalogCode { get; set; } = string.Empty;

    public int? SupplierId { get; set; }
}
