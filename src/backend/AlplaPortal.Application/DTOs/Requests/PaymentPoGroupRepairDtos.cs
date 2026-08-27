namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// One historical PAYMENT request assessed for the missing-PO-group repair (Phase 4B.2).
/// Produced by the read-only dry-run; carries every fact the operator needs to decide, plus the
/// planner's verdict. Purely diagnostic — the dry-run performs zero writes.
/// </summary>
public class PaymentPoRepairCandidateDto
{
    public Guid RequestId { get; set; }
    public string? RequestNumber { get; set; }
    public string RequestTypeCode { get; set; } = string.Empty;
    /// <summary>Persisted scalar (stays APPROVED after repair — never normalized to PO_REQUESTED).</summary>
    public string ScalarStatusCode { get; set; } = string.Empty;
    public DateTime? FinalApprovalAtUtc { get; set; }

    /// <summary>MULTI_DOCUMENT | LEGACY_HEADER | AMBIGUOUS.</summary>
    public string Model { get; set; } = string.Empty;

    public int ActiveLineItemCount { get; set; }
    public int SourceDocumentCount { get; set; }
    public int LineItemsLinkedToDocumentsCount { get; set; }
    public int ExistingGroupCount { get; set; }
    public int ExpectedGroupCount { get; set; }
    public string GroupingBasis { get; set; } = string.Empty;

    public bool HasPoEvidence { get; set; }
    public bool HasDownstreamEvidence { get; set; }

    /// <summary>SAFE_TO_REPAIR | MANUAL_REVIEW | SKIP.</summary>
    public string Classification { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Explicit, never-implicit list of requests to repair. No "repair all" exists.</summary>
public class PaymentPoRepairExecuteRequestDto
{
    public List<Guid> RequestIds { get; set; } = new();
}

/// <summary>Per-request outcome of an execution. One transaction produced each of these.</summary>
public class PaymentPoRepairResultDto
{
    public Guid RequestId { get; set; }
    public string? RequestNumber { get; set; }
    public string Classification { get; set; } = string.Empty;
    /// <summary>REPAIRED | SKIPPED | MANUAL_REVIEW | NOT_FOUND | ERROR.</summary>
    public string Outcome { get; set; } = string.Empty;
    public int GroupsCreated { get; set; }
    /// <summary>Echoed to prove the scalar was NOT changed by the repair.</summary>
    public string ScalarStatusCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
