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

    public int RequestTypeId { get; set; }


    public int? NeedLevelId { get; set; }

    public int? CurrencyId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "O valor total estimado deve ser maior ou igual a zero.")]
    public decimal EstimatedTotalAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    [Required(ErrorMessage = "O departamento é obrigatório.")]
    public int DepartmentId { get; set; }
    
    [Required(ErrorMessage = "A empresa é obrigatória.")]
    public int CompanyId { get; set; }
    
    public int? PlantId { get; set; }
    
    public int? CapexOpexClassificationId { get; set; }
    
    public DateTime? NeedByDateUtc { get; set; }

    public int? SupplierId { get; set; }

    /// <summary>
    /// Post-Payment Completion (Release 2 corrected): IDENTITY of the document attached to this
    /// PAYMENT request. Editable while the request is still a draft; locked once submitted.
    /// </summary>
    public string? SourceDocumentType { get; set; }

    // ── Classification evidence (how the identity was decided) ──
    /// <summary>USER_SELECTED, OCR_CONFIRMED or FINANCE_REVIEW.</summary>
    public string? SourceDocumentTypeSource { get; set; }
    /// <summary>What document extraction proposed. Never auto-applied to the selection.</summary>
    public string? SourceDocumentTypeOcrSuggestion { get; set; }
    /// <summary>Extraction confidence for the suggestion (0.0–1.0).</summary>
    public decimal? SourceDocumentTypeOcrConfidence { get; set; }
    /// <summary>Serialized evidence behind the suggestion.</summary>
    public string? SourceDocumentTypeEvidenceJson { get; set; }
    /// <summary>The user was warned that the selection conflicts with the evidence and proceeded.</summary>
    public bool? ClassificationConflictAcknowledged { get; set; }
    /// <summary>Mandatory written reason when a high-risk conflict was overridden.</summary>
    public string? ClassificationJustification { get; set; }

    // Workflow Participants.
    // (Phase B: AreaApproverId removed — area routing comes from DepartmentManagers;
    // Request.AreaApproverId records who decided, never a nomination.)
    public Guid? BuyerId { get; set; }
    public Guid? FinalApproverId { get; set; }
}
