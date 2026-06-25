using System;
using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests;

public class SubmitReconciliationDto
{
    [Required]
    public decimal FinalInvoiceAmount { get; set; }
    
    [Required]
    public decimal FinalAcceptedAmount { get; set; }
    
    [Required]
    public decimal DeliveredAcceptedAmount { get; set; }
    
    [Required]
    public string ReconciliationDecision { get; set; } = string.Empty;
    
    public string? ReconciliationNotes { get; set; }

    public bool CreditNoteRequired { get; set; }
    public string? CreditNoteNumber { get; set; }
    public Guid? CreditNoteAttachmentId { get; set; }

    public bool DebitNoteRequired { get; set; }
    public string? DebitNoteNumber { get; set; }
    public Guid? DebitNoteAttachmentId { get; set; }

    public bool RefundRequired { get; set; }
    public decimal? RefundAmount { get; set; }

    public bool CompensationFuturePayment { get; set; }
    public string? CompensationNotes { get; set; }
}
