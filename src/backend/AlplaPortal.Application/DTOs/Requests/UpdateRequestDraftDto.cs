using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests;

public class UpdateRequestDraftDto
{
    [Required(ErrorMessage = "O título é obrigatório.")]
    [MaxLength(200, ErrorMessage = "O título não pode exceder 200 caracteres.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "A descrição é obrigatória.")]
    [MaxLength(2000, ErrorMessage = "A descrição não pode exceder 2000 caracteres.")]
    public string Description { get; set; } = string.Empty;

    [Required]
    public int RequestTypeId { get; set; }


    [Required(ErrorMessage = "O grau de necessidade é obrigatório.")]
    public int NeedLevelId { get; set; }

    public int? CurrencyId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "O valor total estimado deve ser maior ou igual a zero.")]
    public decimal EstimatedTotalAmount { get; set; }

    [Required(ErrorMessage = "O departamento é obrigatório.")]
    public int DepartmentId { get; set; }
    
    [Required(ErrorMessage = "A empresa é obrigatória.")]
    public int CompanyId { get; set; }
    
    [Required(ErrorMessage = "A planta é obrigatória.")]
    public int? PlantId { get; set; }
    
    public int? CapexOpexClassificationId { get; set; }
    
    [Required(ErrorMessage = "A data Necessário Até é obrigatória.")]
    public DateTime? NeedByDateUtc { get; set; }

    public int? SupplierId { get; set; }

    // Workflow Participants
    public Guid? BuyerId { get; set; }
    public Guid? AreaApproverId { get; set; }
    public Guid? FinalApproverId { get; set; }
}
