namespace AlplaPortal.Application.DTOs.Approvals;

/// <summary>
/// v2.244.0 Approval Center V2 — Phase 2. Read-only, derived-on-the-fly projection of one approval
/// decision from RequestStatusHistories. NEVER persisted. Multi-batch note (Option A): BatchNumber is
/// a DISPLAY hint parsed from the comment — it is not an ApprovalBatchId, and Amount/Supplier are
/// request-level best-effort (nullable) rather than a false per-batch claim.
/// </summary>
public sealed class ApprovalHistoryRowDto
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = "---";
    public string RequestTitle { get; set; } = "---";
    public string RequestTypeCode { get; set; } = "";

    /// <summary>"AREA" | "FINAL" | null (stage-less events such as resubmit).</summary>
    public string? ApprovalLevel { get; set; }
    /// <summary>"APPROVED" | "REJECTED" | "RETURNED" | "RESUBMITTED".</summary>
    public string? Decision { get; set; }
    public string ActionTaken { get; set; } = "";

    /// <summary>Parsed "Lote #N" — display only, not a batch identity.</summary>
    public int? BatchNumber { get; set; }

    public string RequesterName { get; set; } = "---";
    public string? DepartmentName { get; set; }
    public string? CompanyName { get; set; }
    public string? PlantName { get; set; }

    /// <summary>The real actor of the decision — ActorUserId, never Request.AreaApproverId/FinalApproverId.</summary>
    public Guid ApproverUserId { get; set; }
    public string ApproverName { get; set; } = "---";

    public DateTime DecisionAtUtc { get; set; }
    public string? Comment { get; set; }

    /// <summary>Request-level best-effort amount (approved ?? estimated). Not a per-batch figure.</summary>
    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }
    /// <summary>Request-level best-effort supplier (selected quotation snapshot); null when ambiguous.</summary>
    public string? SupplierName { get; set; }

    public string? PreviousStatusCode { get; set; }
    public string? NewStatusCode { get; set; }
}

/// <summary>Server-paged history result + cheap decision-mix summary over the CURRENT filtered set.</summary>
public sealed class ApprovalHistoryPageDto
{
    public IEnumerable<ApprovalHistoryRowDto> Items { get; set; } = new List<ApprovalHistoryRowDto>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }

    // Decision mix across the whole filtered result (not just the page).
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ReturnedCount { get; set; }
    public int ResubmittedCount { get; set; }
}

/// <summary>One chronological event in a request's approval-relevant audit timeline.</summary>
public sealed class ApprovalTimelineEventDto
{
    public Guid Id { get; set; }
    public string ActionTaken { get; set; } = "";
    /// <summary>Human PT label for the event (decisions + curated context events).</summary>
    public string ActionLabel { get; set; } = "";
    public string? ApprovalLevel { get; set; }
    public string? Decision { get; set; }
    public bool IsDecision { get; set; }

    public Guid ActorUserId { get; set; }
    public string ActorName { get; set; } = "---";
    public string? Comment { get; set; }

    public string? PreviousStatusCode { get; set; }
    public string? NewStatusCode { get; set; }
    public int? BatchNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>All history filters (each nullable = not applied). Shared by table + export so both match.</summary>
public sealed class ApprovalHistoryFilter
{
    public string? Search { get; set; }
    /// <summary>"APPROVED" | "REJECTED" | "RETURNED" | "RESUBMITTED".</summary>
    public string? Decision { get; set; }
    /// <summary>"AREA" | "FINAL".</summary>
    public string? Stage { get; set; }
    public Guid? ApproverId { get; set; }
    public string? RequestType { get; set; }
    public int? DepartmentId { get; set; }
    public int? CompanyId { get; set; }
    public int? PlantId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}
