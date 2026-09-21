using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Admin;

/// <summary>
/// v2.245.4 — request body for APPLY of the payment-group-item-linkage repair. A non-empty reason is
/// mandatory to apply (preview needs no body).
/// </summary>
public sealed class PaymentGroupItemLinkageRepairRequest
{
    public string? Reason { get; set; }
}

/// <summary>Per-request/group decision in the payment-group-item-linkage repair scan.</summary>
public sealed class PaymentGroupItemLinkageRowDto
{
    public string RequestNumber { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string? PoGroupId { get; set; }
    public string? RequestStatus { get; set; }
    public string? GroupStatus { get; set; }
    /// <summary>The group status the repair would leave (equal to GroupStatus when only linkage is needed).</summary>
    public string? TargetGroupStatus { get; set; }
    public int ActiveItems { get; set; }
    public int UnlinkedItems { get; set; }
    public int ReceivedItems { get; set; }
    /// <summary>NONE | CORRELATED | UNCORRELATED — see the correlation rules in the service.</summary>
    public string ConfirmEvidence { get; set; } = "NONE";
    public bool MoveEvidence { get; set; }
    public bool PaymentEvidence { get; set; }
    public bool AdvanceEvidence { get; set; }
    public bool ReceiptPresent { get; set; }
    /// <summary>
    /// REPAIR_LINK | REPAIR_LINK_AND_DEMOTE | REPAIRED_LINK | REPAIRED_LINK_AND_DEMOTE | ALREADY_HEALTHY |
    /// AMBIGUOUS | CONFLICTING | REFUSED | ERROR.
    /// </summary>
    public string Decision { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>Structured result of a payment-group-item-linkage preview or apply run. Never persisted.</summary>
public sealed class PaymentGroupItemLinkageRepairResultDto
{
    /// <summary>"PREVIEW" or "APPLIED".</summary>
    public string Status { get; set; } = "PREVIEW";
    public string Message { get; set; } = "";
    public int ScannedRequests { get; set; }
    public int ScannedGroups { get; set; }
    public int Eligible { get; set; }
    public int WouldRepair { get; set; }
    public int Repaired { get; set; }
    public int AlreadyHealthy { get; set; }
    public int Ambiguous { get; set; }
    public int Conflicting { get; set; }
    public int Refused { get; set; }
    public int Errors { get; set; }
    public List<PaymentGroupItemLinkageRowDto> Rows { get; set; } = new();
}
