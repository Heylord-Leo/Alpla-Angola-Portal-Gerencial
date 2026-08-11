namespace AlplaPortal.Domain.Entities;

public class RequestAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public Guid? RequestPoGroupId { get; set; }
    public RequestPoGroup? RequestPoGroup { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public decimal FileSizeMBytes { get; set; }
    public string? FileHash { get; set; }
    
    public string AttachmentTypeCode { get; set; } = string.Empty; // e.g., PROFORMA, PO, PAYMENT_PROOF, PAYMENT_SCHEDULE
    
    // Constants for Type Codes
    public const string TYPE_PROFORMA = "PROFORMA";
    public const string TYPE_PO = "PO";
    public const string TYPE_PAYMENT_PROOF = "PAYMENT_PROOF";
    public const string TYPE_PAYMENT_SCHEDULE = "PAYMENT_SCHEDULE";
    /// <summary>
    /// Legacy supplier receipt. Semantically ambiguous (fiscal receipt vs. delivery note) and
    /// therefore NEVER renamed or reclassified automatically — historical rows keep this code
    /// exactly as recorded (rule R18). New fiscal receipts use <see cref="TYPE_FISCAL_RECEIPT"/>.
    /// </summary>
    public const string TYPE_RECEIPT = "RECEIPT";

    // ── Post-Payment Completion Workflow ──
    /// <summary>
    /// Operation Invoice ("Factura da operação") uploaded post-approval by Buyer or Finance
    /// (Release 4 Phase 2 approved rule: Requester and Receiving are read-only). Renamed from
    /// FINAL_INVOICE in Release 3: "final" wrongly implied terminality, when a Factura is simply
    /// the operation document and other obligations may still be open. The old code was never
    /// written to any row, so nothing carries it.
    /// </summary>
    public const string TYPE_OPERATION_INVOICE = "OPERATION_INVOICE";

    /// <summary>A document that ORIGINATES a PAYMENT request. Never an operation invoice.</summary>
    public const string TYPE_PAYMENT_SOURCE_DOCUMENT = "PAYMENT_SOURCE_DOCUMENT";
    /// <summary>Fiscal receipt ("Recibo Fiscal") — terminal closing document, uploaded by Finance only.</summary>
    public const string TYPE_FISCAL_RECEIPT = "FISCAL_RECEIPT";

    // Buy-to-Pay
    public const string TYPE_ADVANCE_PAYMENT_PROOF = "ADVANCE_PAYMENT_PROOF";
    public const string TYPE_CREDIT_NOTE = "CREDIT_NOTE";
    public const string TYPE_DEBIT_NOTE = "DEBIT_NOTE";

    public string StorageReference { get; set; } = string.Empty; // Path pointer

    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public DateTime UploadedAtUtc { get; set; }
    public bool IsDeleted { get; set; }

    // ── Void (correction workflows, e.g. Finance cancel-schedule) ──
    // Distinct from IsDeleted: a voided attachment stays returned by every normal query and
    // visible in the UI ("Sem efeito"/"Cancelado" badge) — it is not hidden like a deletion.
    // IsVoided is intentionally not a stored column; derive it from VoidedAtUtc != null.
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}
