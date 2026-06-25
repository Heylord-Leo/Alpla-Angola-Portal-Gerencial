namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// DTO for confirming an advance payment (Buy-to-Pay lifecycle).
/// Used by Finance to record the actual paid amount and link the payment proof.
/// </summary>
public class ConfirmAdvancePaymentDto
{
    /// <summary>
    /// The actual amount paid by Finance.
    /// </summary>
    public decimal ActualPaidAmount { get; set; }

    /// <summary>
    /// Optional comment / justification.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Optional FK to a previously uploaded payment proof attachment.
    /// </summary>
    public Guid? PaymentProofAttachmentId { get; set; }
}
