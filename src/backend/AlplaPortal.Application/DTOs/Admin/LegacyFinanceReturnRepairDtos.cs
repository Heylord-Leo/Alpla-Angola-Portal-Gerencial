using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Admin;

/// <summary>Body for APPLY / ROLLBACK of the legacy Finance-return repair (confirm=true). PREVIEW needs no body.</summary>
public class LegacyFinanceReturnRepairRequest
{
    /// <summary>Mandatory human reason recorded in the audit history row.</summary>
    public string? Reason { get; set; }
    /// <summary>The group id reported by the PREVIEW; the APPLY fails closed when the live qualifying group differs.</summary>
    public Guid? ExpectedPoGroupId { get; set; }
}

public class LegacyFinanceReturnRepairResultDto
{
    public string Mode { get; set; } = "PREVIEW";                  // PREVIEW | APPLY | ROLLBACK
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTypeCode { get; set; } = string.Empty;
    /// <summary>QUALIFIES | REPAIRED | ALREADY_REPAIRED | REFUSED | ROLLED_BACK | ERROR</summary>
    public string Decision { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? ScalarStatusCode { get; set; }
    public Guid? GroupId { get; set; }
    public string? GroupStatusBefore { get; set; }
    public string? GroupStatusAfter { get; set; }
    public DateTime? LatestFinanceReturnUtc { get; set; }
    public DateTime? LatestPoRegistrationUtc { get; set; }
    public int LiveGroupCount { get; set; }
    public int LivePaymentCount { get; set; }
    public List<string> Preserved { get; set; } = new();
    public string? AuditIdempotencyKey { get; set; }
}
