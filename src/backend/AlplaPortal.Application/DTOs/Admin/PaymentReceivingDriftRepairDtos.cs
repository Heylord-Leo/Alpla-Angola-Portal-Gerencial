using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Admin;

/// <summary>
/// v2.245.0 — request body for APPLY of the payment-receiving-status-drift repair. A non-empty reason is
/// mandatory to apply (preview needs no body).
/// </summary>
public sealed class PaymentReceivingDriftRepairRequest
{
    public string? Reason { get; set; }
}

/// <summary>Per-request/group decision in the payment-receiving-status-drift repair scan.</summary>
public sealed class PaymentReceivingDriftRowDto
{
    public string RequestNumber { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string PoGroupId { get; set; } = "";
    /// <summary>The request scalar status code (already historically advanced for the legacy cohort).</summary>
    public string? RequestStatus { get; set; }
    /// <summary>The operational group status code (PENDING for the drifted cohort).</summary>
    public string? GroupStatus { get; set; }
    /// <summary>True when a real PAYMENT_COMPLETED history event exists for the request.</summary>
    public bool PaymentHistoryFound { get; set; }
    /// <summary>True when a PAYMENT_DIVERGENCE_DETECTED history event exists (informational — never a blocker).</summary>
    public bool PaymentDivergencePresent { get; set; }
    /// <summary>Count of RequestPayment ledger rows for the request (the legacy cohort has 0).</summary>
    public int RequestPaymentRows { get; set; }
    /// <summary>REPAIR | REPAIRED | ALREADY_HEALTHY | AMBIGUOUS | CONFLICTING | REFUSED.</summary>
    public string Decision { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>Structured result of a payment-receiving-status-drift preview or apply run. Never persisted.</summary>
public sealed class PaymentReceivingDriftRepairResultDto
{
    /// <summary>"PREVIEW" or "APPLIED".</summary>
    public string Status { get; set; } = "PREVIEW";
    public string Message { get; set; } = "";
    public int ScannedRequests { get; set; }   // PAYMENT requests in the drift population examined
    public int ScannedGroups { get; set; }     // operational groups evaluated
    public int Eligible { get; set; }          // groups eligible for repair
    public int WouldRepair { get; set; }       // eligible groups (preview)
    public int Repaired { get; set; }          // groups actually repaired (apply)
    public int AlreadyHealthy { get; set; }    // group already PAYMENT_COMPLETED (idempotent re-scan)
    public int Ambiguous { get; set; }         // >1 operational group — PAYMENT is single-group by design
    public int Conflicting { get; set; }       // payment ledger contradicts completion
    public int Refused { get; set; }           // no PAYMENT_COMPLETED evidence, cancelled, etc.
    public List<PaymentReceivingDriftRowDto> Rows { get; set; } = new();
}
