namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Tracks individual payment events within the Buy-to-Pay lifecycle.
/// A single request may have multiple payments (advance, final balance, regularization, refund).
/// Each payment has its own lifecycle: PLANNED → SCHEDULED → COMPLETED | CANCELLED.
/// </summary>
public class RequestPayment
{
    public int Id { get; set; }

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    // ── Payment Classification ──
    /// <summary>
    /// Type of payment: ADVANCE, FINAL_BALANCE, REGULARIZATION, REFUND, OTHER.
    /// </summary>
    public string PaymentType { get; set; } = string.Empty;

    /// <summary>
    /// Ordering within the request: 1 = first advance, 2 = balance, etc.
    /// </summary>
    public int PaymentSequence { get; set; } = 1;

    // ── Planned Amounts ──
    /// <summary>
    /// Percentage of the approved total (e.g., 50.00 for 50%).
    /// </summary>
    public decimal? PlannedPercent { get; set; }

    /// <summary>
    /// Calculated or manually entered planned amount.
    /// </summary>
    public decimal PlannedAmount { get; set; }

    /// <summary>
    /// Currency code snapshot at creation time.
    /// </summary>
    public string CurrencyCode { get; set; } = string.Empty;

    // ── Scheduling ──
    public DateTime? ScheduledDateUtc { get; set; }
    public Guid? ScheduledByUserId { get; set; }
    public User? ScheduledByUser { get; set; }

    // ── Actual Payment ──
    public decimal? ActualPaidAmount { get; set; }
    public DateTime? PaidDateUtc { get; set; }
    public Guid? PaidByUserId { get; set; }
    public User? PaidByUser { get; set; }

    // ── Status ──
    /// <summary>
    /// Payment lifecycle: PLANNED, SCHEDULED, COMPLETED, CANCELLED.
    /// </summary>
    public string PaymentStatus { get; set; } = PaymentStatuses.Planned;

    // ── Attachment Link ──
    /// <summary>
    /// FK to the payment proof attachment (optional).
    /// </summary>
    public Guid? PaymentProofAttachmentId { get; set; }
    public RequestAttachment? PaymentProofAttachment { get; set; }

    // ── Divergence Detection (DEC-110 pattern) ──
    public bool HasDivergence { get; set; }
    public decimal? DivergenceAmount { get; set; }
    public string? DivergenceNotes { get; set; }

    // ── Notes ──
    public string? Notes { get; set; }

    // ── Audit ──
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // ── Constants ──
    public static class PaymentTypes
    {
        public const string Advance = "ADVANCE";
        public const string FinalBalance = "FINAL_BALANCE";
        public const string Regularization = "REGULARIZATION";
        public const string Refund = "REFUND";
        public const string Other = "OTHER";
    }

    public static class PaymentStatuses
    {
        public const string Planned = "PLANNED";
        public const string Scheduled = "SCHEDULED";
        public const string Completed = "COMPLETED";
        public const string Cancelled = "CANCELLED";
    }
}
