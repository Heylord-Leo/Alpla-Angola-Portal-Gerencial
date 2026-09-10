namespace AlplaPortal.Application.DTOs.Admin;

/// <summary>
/// v2.242.0 — apply-body for the PAYMENT PO-responsibility backfill. Preview needs no body; apply
/// requires a non-empty <see cref="Reason"/> (audit) exactly like the monetary-scale repair.
/// </summary>
public class PoResponsibleBuyerBackfillRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>One PAYMENT PO group examined by the backfill, with the decision and its evidence.</summary>
public class PoResponsibleBuyerBackfillRow
{
    public string RequestNumber { get; set; } = string.Empty;
    public Guid PoGroupId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public string? PurchaseOrderNumber { get; set; }
    public string? SupplierName { get; set; }
    public Guid? CandidateBuyerId { get; set; }
    public string? CandidateBuyerName { get; set; }
    /// <summary>"UpdatedByUserId+HistoryCorroborated", "UpdatedByUserId", "None", … — how the candidate was derived.</summary>
    public string EvidenceSource { get; set; } = string.Empty;
    /// <summary>"WouldAssign" | "AlreadyAssigned" | "Unresolved" | "Conflicting".</summary>
    public string Decision { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>Aggregate + per-row backfill result (shared shape by preview and apply).</summary>
public class PoResponsibleBuyerBackfillResult
{
    public static class Statuses
    {
        public const string Preview = "PREVIEW";
        public const string Applied = "APPLIED";
        public const string Refused = "REFUSED";
    }

    public string Status { get; set; } = Statuses.Preview;
    public string? Message { get; set; }

    public int Scanned { get; set; }
    public int Eligible { get; set; }
    public int WouldAssign { get; set; }
    public int Assigned { get; set; }
    public int AlreadyAssigned { get; set; }
    public int Unresolved { get; set; }
    public int Conflicting { get; set; }

    public List<PoResponsibleBuyerBackfillRow> Rows { get; set; } = new();
}
