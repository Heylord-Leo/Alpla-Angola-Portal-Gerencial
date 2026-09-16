using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Admin;

/// <summary>
/// v2.245.0 — request body for APPLY of the receiving-finalization-drift repair. A non-empty reason is
/// mandatory to apply (preview needs no body).
/// </summary>
public sealed class ReceivingFinalizationRepairRequest
{
    public string? Reason { get; set; }
}

/// <summary>Per-line decision in the receiving-finalization-drift repair scan.</summary>
public sealed class ReceivingRepairRowDto
{
    public string RequestNumber { get; set; } = "";
    public string PoGroupId { get; set; } = "";
    public int LineNumber { get; set; }
    public string? RequestLineItemId { get; set; }
    public string? QuotationItemId { get; set; }
    public string? GroupStatus { get; set; }
    public string? LineItemStatus { get; set; }
    public string? QuotationItemStatus { get; set; }
    /// <summary>REPAIR | ALREADY_HEALTHY | AMBIGUOUS | REFUSED | REPAIRED.</summary>
    public string Decision { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>Structured result of a receiving-finalization-drift preview or apply run. Never persisted.</summary>
public sealed class ReceivingFinalizationRepairResultDto
{
    /// <summary>"PREVIEW" or "APPLIED".</summary>
    public string Status { get; set; } = "PREVIEW";
    public string Message { get; set; } = "";
    public int Scanned { get; set; }        // IN_FOLLOWUP groups (with winning quotation) examined
    public int Eligible { get; set; }       // groups with ≥1 repairable line
    public int WouldRepair { get; set; }    // repairable lines (preview)
    public int Repaired { get; set; }       // lines actually repaired (apply)
    public int AlreadyHealthy { get; set; }
    public int Ambiguous { get; set; }
    public int Refused { get; set; }
    public List<ReceivingRepairRowDto> Rows { get; set; } = new();
}
