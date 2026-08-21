namespace AlplaPortal.Application.DTOs.Requests;

public class RequestListItemDto
{
    public Guid Id { get; set; }
    public string? RequestNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    
    public int StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public int StatusDisplayOrder { get; set; }
    public string StatusBadgeColor { get; set; } = string.Empty;

    /// <summary>
    /// Computed workflow state for QUOTATION requests (not persisted).
    /// For PAYMENT requests, mirrors StatusCode.
    /// </summary>
    public string? DisplayWorkflowState { get; set; }

    /// <summary>
    /// Group-aware display override (RequestGroupDisplayStateCalculator), populated only by
    /// GetRequests. Null means "no override" — the caller must fall back to StatusName. Never used
    /// for permissions/eligibility/filtering — StatusId/StatusCode/StatusName remain the source of
    /// truth for that; these two fields exist purely for the friendly label shown to the user.
    /// </summary>
    public string? DisplayStatusCode { get; set; }
    public string? DisplayStatusName { get; set; }

    /// <summary>
    /// v2.230.0 multi-group summary (computed by GetRequests, never persisted). ActiveUnitCount
    /// counts active operational units (non-superseded in-approval batches + non-cancelled PO
    /// groups). UnitSummary is the compact PT line ("3 grupos · 1 P.O. emitida · …") — null when
    /// the request has at most one active unit so single-unit rows keep their current look.
    /// ResponsibleRoles lists the distinct roles with pending actions, priority-ordered.
    /// </summary>
    public int ActiveUnitCount { get; set; }
    public string? UnitSummary { get; set; }
    public List<string> ResponsibleRoles { get; set; } = new();

    public int RequestTypeId { get; set; }
    public string RequestTypeName { get; set; } = string.Empty;
    public string RequestTypeCode { get; set; } = string.Empty;

    
    public int? NeedLevelId { get; set; }
    public string? NeedLevelName { get; set; }
    
    public int? CurrencyId { get; set; }
    public int? CapexOpexClassificationId { get; set; }

    public Guid RequesterId { get; set; }
    public string RequesterName { get; set; } = string.Empty;

    public Guid? BuyerId { get; set; }
    public string? BuyerName { get; set; }

    public Guid? AreaApproverId { get; set; }
    public string? AreaApproverName { get; set; }

    public Guid? FinalApproverId { get; set; }
    public string? FinalApproverName { get; set; }

    public int DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    public int? PlantId { get; set; }
    public string? PlantName { get; set; }

    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierPortalCode { get; set; }

    public decimal EstimatedTotalAmount { get; set; }
    public string? CurrencyCode { get; set; }

    // ── Authoritative "actionable amount" for the Approval Center queue (ApprovalQueueAmountResolver) ──
    // The single monetary rule used by the card, the queue-total KPI and value sort/filter. Resolved
    // per request type + approval stage so partial QUOTATION lots show the current lot total, not the
    // (0) request estimate. Null = the amount could not be resolved — render "Valor ainda não definido",
    // NEVER 0. A genuine zero stays a concrete 0m.
    public decimal? ActionableAmount { get; set; }

    /// <summary>Which rule produced <see cref="ActionableAmount"/> — see ApprovalQueueAmountResolver.Sources.</summary>
    public string? ActionableAmountSource { get; set; }

    /// <summary>True when the batch approved snapshot disagrees with the sum of its included selected
    /// quotation items — the card must warn instead of showing a falsely normal amount.</summary>
    public bool HasAmountInconsistency { get; set; }

    /// <summary>The current actionable lot (batch) number, when the amount comes from a batch.</summary>
    public int? ActionableLotNumber { get; set; }

    public DateTime RequestedDateUtc { get; set; }
    public DateTime? NeedByDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    
    public bool IsCancelled { get; set; }
    public Guid? SelectedQuotationId { get; set; }

    // Context for Area Approval
    public string? CostCenterCode { get; set; }
    public string? CostCenterName { get; set; }
    
    public DateTime? CompletedAtUtc { get; set; }
    
    /// <summary>
    /// Date when payment was completed (transition to PAYMENT_COMPLETED status).
    /// Available for WAITING_RECEIPT and COMPLETED requests so the list can show both
    /// the original deadline and the actual payment date.
    /// </summary>
    public DateTime? PaymentCompletedAtUtc { get; set; }
}
