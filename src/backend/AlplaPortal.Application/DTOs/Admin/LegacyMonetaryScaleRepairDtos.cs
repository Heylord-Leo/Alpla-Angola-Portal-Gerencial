using System;
using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Application.DTOs.Admin;

/// <summary>
/// Strong-intent body for the legacy monetary-scale repair. The caller states ONLY what it
/// believes to be true (the current wrong total and the intended correct total) plus a reason;
/// it may NOT name arbitrary fields or supply per-entity amounts. The service derives every
/// dependent value canonically. See <c>LegacyMonetaryScaleRepairService</c>.
/// </summary>
public sealed class LegacyMonetaryScaleRepairRequest
{
    /// <summary>The wrong total the caller expects to find persisted (e.g. 18400000000).</summary>
    public decimal ExpectedCurrentTotal { get; set; }

    /// <summary>The corrected total the caller intends (e.g. 18400000).</summary>
    public decimal ExpectedCorrectTotal { get; set; }

    /// <summary>Mandatory human justification, recorded verbatim in the corrective audit.</summary>
    public string Reason { get; set; } = string.Empty;

    // ── Optional concurrency tokens captured from the preview. When supplied, apply refuses with
    //    CONFLICT if the persisted state moved since the preview — the cross-call guard the approved
    //    design requires. Request/PoGroup use the rowversion; Line/Payment (no rowversion) use a
    //    value+timestamp fingerprint. Omitting them still leaves the value-gate protection intact. ──
    public string? ExpectedRequestRowVersion { get; set; }
    public string? ExpectedPoGroupRowVersion { get; set; }
    public string? ExpectedLineFingerprint { get; set; }
    public string? ExpectedPaymentFingerprint { get; set; }
}

/// <summary>One named safety gate and whether the current state satisfies it.</summary>
public sealed class RepairSafetyCheck
{
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string? Detail { get; set; }
}

/// <summary>
/// Read-only projection of the repair: every persisted amount that would change, current vs
/// corrected, plus the safety gates and opaque concurrency fingerprints. Preview writes nothing.
/// </summary>
public sealed class LegacyMonetaryScaleRepairPreview
{
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestStatus { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;

    // ── Line ──
    public decimal LineQuantity { get; set; }
    public decimal LineUnitPrice { get; set; }
    public decimal LineCurrentDiscount { get; set; }
    public decimal LineCurrentTotal { get; set; }
    public decimal LineCorrectedDiscount { get; set; }
    public decimal LineCorrectedTotal { get; set; }

    // ── Request ──
    public decimal RequestCurrentEstimated { get; set; }
    public decimal RequestCorrectedEstimated { get; set; }
    public decimal? RequestCurrentApproved { get; set; }
    public decimal? RequestCorrectedApproved { get; set; }

    // ── PO group ──
    public decimal PoCurrentTotal { get; set; }
    public decimal PoCorrectedTotal { get; set; }
    public string? PurchaseOrderNumber { get; set; }

    // ── Payment ──
    public decimal PaymentCurrentPlannedAmount { get; set; }
    public decimal PaymentCorrectedPlannedAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal? ActualPaidAmount { get; set; }

    // ── Verdict ──
    public List<RepairSafetyCheck> SafetyChecks { get; set; } = new();
    public bool AllSafetyChecksPassed => SafetyChecks.Count > 0 && SafetyChecks.All(c => c.Passed);
    public bool AlreadyCorrect { get; set; }
    public bool WillWrite { get; set; }
    public int AffectedRows { get; set; }

    // ── Concurrency snapshot metadata (opaque; no secrets). Request/PoGroup carry a real
    // rowversion; Line/Payment have none, so a value+timestamp fingerprint stands in. ──
    public string RequestRowVersion { get; set; } = string.Empty;
    public string PoGroupRowVersion { get; set; } = string.Empty;
    public string LineFingerprint { get; set; } = string.Empty;
    public string PaymentFingerprint { get; set; } = string.Empty;
}

/// <summary>One field actually changed by an apply, old → new (as invariant-culture strings).</summary>
public sealed class RepairChange
{
    public string Entity { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

/// <summary>Outcome of an apply (or an attempted apply that refused / found nothing to do).</summary>
public sealed class LegacyMonetaryScaleRepairResult
{
    /// <summary>APPLIED | ALREADY_CORRECT | REFUSED | CONFLICT.</summary>
    public string Status { get; set; } = string.Empty;
    public Guid RepairId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;

    /// <summary>Financial rows corrected (line + request + PO group + payment) — matches the preview's
    /// AffectedRows. This is the number an operator reasons about.</summary>
    public int FinancialRowsChanged { get; set; }
    /// <summary>Append-only corrective audit rows inserted (1 status-history + N field-change).</summary>
    public int AuditRowsInserted { get; set; }
    /// <summary>Total DB rows the single SaveChanges affected (= FinancialRowsChanged + AuditRowsInserted).</summary>
    public int TotalDbRowsAffected { get; set; }

    /// <summary>Deprecated alias of <see cref="TotalDbRowsAffected"/> (financial + audit). Prefer the
    /// explicit fields above; kept for back-compat.</summary>
    public int RowsChanged { get; set; }
    public List<RepairChange> Changes { get; set; } = new();
    public List<Guid> AuditEntryIds { get; set; } = new();
    public DateTime TimestampUtc { get; set; }
    public string Operator { get; set; } = string.Empty;
    public List<RepairSafetyCheck> SafetyChecks { get; set; } = new();
    public string? Message { get; set; }

    public static class Statuses
    {
        public const string Applied = "APPLIED";
        public const string AlreadyCorrect = "ALREADY_CORRECT";
        public const string Refused = "REFUSED";
        public const string Conflict = "CONFLICT";
    }
}
