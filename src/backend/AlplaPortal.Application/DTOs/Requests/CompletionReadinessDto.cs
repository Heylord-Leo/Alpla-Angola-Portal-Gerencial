using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Read model of the Phase 4 completion readiness of one request (Release 4 Phase 4D).
/// A faithful projection of <c>GroupCompletionProjector</c> — the frontend renders these facts
/// and NEVER re-derives them. <c>CompletionCycleId</c> is deliberately not exposed: it is an
/// internal idempotency identity with no user-facing meaning.
/// </summary>
public class CompletionReadinessDto
{
    /// <summary>Whether the Phase 4 automatic completion lifecycle is active in this environment
    /// (Enabled &amp;&amp; CompletionEnabled). While false, the UI must present satisfied
    /// requirements without implying the automatic parent transition is running.</summary>
    public bool CompletionLifecycleEnabled { get; set; }

    public string? RequestStatusCode { get; set; }

    /// <summary>Every relevant group is Complete and no request-level blocker remains —
    /// authoritative, computed server-side, never derived from card counts in the UI.</summary>
    public bool IsCompletionReady { get; set; }

    /// <summary>The request itself is COMPLETED.</summary>
    public bool IsCompleted { get; set; }

    /// <summary>When the request completed (the REQUEST_COMPLETED event instant), if it has.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>An active reconciliation blocks completion at request level.</summary>
    public bool HasActiveReconciliation { get; set; }

    public int TotalGroupCount { get; set; }
    public int CompletedGroupCount { get; set; }
    public int BlockingGroupCount { get; set; }

    public List<CompletionReadinessGroupDto> Groups { get; set; } = new();
}

public class CompletionReadinessGroupDto
{
    public Guid GroupId { get; set; }
    public string? SupplierName { get; set; }
    public string? PlantName { get; set; }
    public string? CurrencyCode { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? GroupStatusCode { get; set; }

    // ── The projection booleans (GroupCompletionProjector — the single rulebook) ──
    public bool Classified { get; set; }
    public bool PoSatisfied { get; set; }
    public bool NoBlockingCorrection { get; set; }
    public bool PaymentSatisfied { get; set; }
    public bool ReceiptSatisfied { get; set; }
    public bool OperationInvoiceSatisfied { get; set; }
    public bool ClosedShort { get; set; }
    public bool FiscalReceiptRequired { get; set; }
    public bool FiscalReceiptSatisfied { get; set; }
    public bool Complete { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Missing obligations with their next-action owner, in lifecycle order.</summary>
    public List<CompletionBlockingReasonDto> BlockingReasons { get; set; } = new();

    /// <summary>The bound Recibo Fiscal, when one exists.</summary>
    public CompletionFiscalReceiptDto? FiscalReceipt { get; set; }
}

public class CompletionBlockingReasonDto
{
    /// <summary>GroupCompletionBlockingReasons code — the UI maps it to Portuguese.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>GroupCompletionOwnership code — who must act next.</summary>
    public string OwnerCode { get; set; } = string.Empty;
}

public class CompletionFiscalReceiptDto
{
    public Guid AttachmentId { get; set; }
    public string? FileName { get; set; }
    public DateTime? UploadedAtUtc { get; set; }
    public string? UploadedByName { get; set; }
}
