using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Domain.Entities;

public class Request
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? RequestNumber { get; set; } // E.g., PRQ-2023-0001
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int RequestTypeId { get; set; }
    public RequestType RequestType { get; set; } = null!;

    public int StatusId { get; set; }
    public RequestStatus Status { get; set; } = null!;

    public Guid RequesterId { get; set; }
    public User Requester { get; set; } = null!;

    // Request Participants (Involved Parties)
    public Guid? BuyerId { get; set; }
    public User? Buyer { get; set; }

    public Guid? AreaApproverId { get; set; }
    public User? AreaApprover { get; set; }

    public Guid? FinalApproverId { get; set; }
    public User? FinalApprover { get; set; }


    public int? NeedLevelId { get; set; }
    public NeedLevel? NeedLevel { get; set; }

    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public DateTime RequestedDateUtc { get; set; }
    public DateTime? NeedByDateUtc { get; set; }
    public DateTime? ScheduledDateUtc { get; set; }

    // Financial
    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal EstimatedTotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// OCR-extracted grand total from the supplier document (proforma/invoice).
    /// Persisted during OCR extraction to serve as the financial integrity baseline.
    /// Null for manually-created requests (integrity gate is skipped).
    /// </summary>
    public decimal? OcrOriginalGrandTotal { get; set; }

    // ── DEC-110: Financial Snapshot (populated at Final Approval — immutable) ─────
    /// <summary>
    /// Immutable snapshot of the total amount at the time of final approval.
    /// For QUOTATION: captured from SelectedQuotation.TotalAmount.
    /// For PAYMENT: captured from EstimatedTotalAmount.
    /// Null for requests approved before this feature was deployed.
    /// </summary>
    public decimal? ApprovedTotalAmount { get; set; }

    /// <summary>
    /// Currency code snapshot at approval time (e.g., "AOA", "USD", "EUR").
    /// </summary>
    public string? ApprovedCurrencyCode { get; set; }

    /// <summary>
    /// UTC timestamp when the financial snapshot was captured (final approval moment).
    /// </summary>
    public DateTime? ApprovedAtUtc { get; set; }

    // ── DEC-110: Actual Payment (populated at MarkAsPaid — mandatory) ─────────────
    /// <summary>
    /// The actual amount paid by Finance, as entered at payment confirmation.
    /// Mandatory when confirming payment. Null until payment is executed.
    /// </summary>
    public decimal? ActualPaidAmount { get; set; }

    /// <summary>
    /// UTC timestamp when the actual payment was recorded.
    /// </summary>
    public DateTime? ActualPaidAtUtc { get; set; }

    public int? CapexOpexClassificationId { get; set; }
    public CapexOpexClassification? CapexOpexClassification { get; set; }

    // Workflow Tracking
    public string? CurrentResponsibleRole { get; set; } // Minimal V1 tracking
    public Guid? CurrentResponsibleUserId { get; set; }

    // Audit fields
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public bool IsCancelled { get; set; }
    public Guid? SelectedQuotationId { get; set; }

    // Contract linkage (DEC-111 — unidirectional FK design)
    public Guid? ContractId { get; set; }
    public Contract? Contract { get; set; }

    public Guid? ContractPaymentObligationId { get; set; }
    public ContractPaymentObligation? ContractPaymentObligation { get; set; }

    // ── Buy-to-Pay: Payment Condition (set by Buyer at PO registration) ──
    /// <summary>
    /// Payment condition code: POST_PAID, ADVANCE_FULL, ADVANCE_PARTIAL.
    /// NULL = POST_PAID (backward compatible for existing requests).
    /// </summary>
    public string? PaymentConditionCode { get; set; }

    /// <summary>
    /// Advance payment percentage (1–100). Only set when PaymentConditionCode = ADVANCE_PARTIAL or ADVANCE_FULL (100).
    /// </summary>
    public decimal? AdvancePaymentPercent { get; set; }

    /// <summary>
    /// Source of the payment condition selection: OCR_DETECTED, USER_SELECTED.
    /// Null for legacy requests created before this tracking was introduced.
    /// </summary>
    public string? PaymentConditionSource { get; set; }

    // ── Post-Payment Completion Workflow (Release 1 foundation — inert while disabled) ──
    // ── Document identity (what the supplier actually issued) ────────────────
    /// <summary>
    /// The document that originated this request — see RequestConstants.SourceDocumentTypes.
    /// A statement of IDENTITY only. What remains owed is derived by DocumentObligationResolver
    /// and never stored here. No default: the Requester must choose explicitly.
    /// </summary>
    public string? SourceDocumentType { get; set; }

    /// <summary>How the classification was reached: USER_SELECTED, OCR_CONFIRMED, FINANCE_REVIEW.</summary>
    public string? SourceDocumentTypeSource { get; set; }

    /// <summary>What document extraction proposed, if anything. Never auto-applied.</summary>
    public string? SourceDocumentTypeOcrSuggestion { get; set; }

    /// <summary>Extraction confidence for the suggestion (0.0–1.0).</summary>
    public decimal? SourceDocumentTypeOcrConfidence { get; set; }

    /// <summary>Serialized evidence: title found, supporting/conflicting evidence, fiscal markers.</summary>
    public string? SourceDocumentTypeEvidenceJson { get; set; }

    /// <summary>
    /// Release 3: this PAYMENT request was created under the multi-source-document model.
    ///
    /// <para>The explicit discriminator between a HISTORICAL request that has no source documents
    /// because the feature did not exist, and a NEW draft that has none yet because its first
    /// document has not been persisted (or failed to persist). Both look identical from a row count,
    /// and only the second may show the collection.</para>
    ///
    /// <para>Persisted rather than inferred: deducing it from CreatedAtUtc versus the effective date
    /// would make the answer depend on configuration that can change afterwards, and would silently
    /// reclassify existing drafts if the date were ever moved.</para>
    /// </summary>
    public bool UsesMultiSourceDocuments { get; set; }

    /// <summary>The user was shown a conflict with the extracted evidence and proceeded anyway.</summary>
    public bool ClassificationConflictAcknowledged { get; set; }

    /// <summary>Mandatory written reason when a high-risk conflict was overridden.</summary>
    public string? ClassificationJustification { get; set; }

    /// <summary>
    /// Stable completion identity. Assigned exactly once, by the winning atomic transition of
    /// this Request to COMPLETED, in the same SaveChanges as StatusId. Never regenerated by a
    /// retry: a loser of the RowVersion race reloads and reuses the persisted value. Builds the
    /// REQUEST_COMPLETED history key (RC:{RequestId}:{CompletionCycleId}) and the
    /// RequestFinalized notification CorrelationId. Null while the request is not completed.
    /// </summary>
    public Guid? CompletionCycleId { get; set; }

    /// <summary>
    /// SQL Server rowversion. Selects a single winner for the parent-completion transition when
    /// several PO groups of the same request complete concurrently.
    /// Written by the database only — never assigned by application code.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // Integration Placeholders
    public string? PrimaveraReference { get; set; }
    public string? AlplaProdReference { get; set; }

    public ICollection<Quotation> Quotations { get; set; } = new List<Quotation>();
    public ICollection<RequestLineItem> LineItems { get; set; } = new List<RequestLineItem>();
    public ICollection<RequestStatusHistory> StatusHistories { get; set; } = new List<RequestStatusHistory>();
    public ICollection<RequestAttachment> Attachments { get; set; } = new List<RequestAttachment>();

    // ── Buy-to-Pay: Child Tables ──
    public ICollection<RequestPayment> Payments { get; set; } = new List<RequestPayment>();
    public ICollection<RequestReconciliation> Reconciliations { get; set; } = new List<RequestReconciliation>();
    public ICollection<RequestPoGroup> PoGroups { get; set; } = new List<RequestPoGroup>();

    // ── Partial Quotation Approval ──
    public ICollection<ApprovalBatch> ApprovalBatches { get; set; } = new List<ApprovalBatch>();
}
