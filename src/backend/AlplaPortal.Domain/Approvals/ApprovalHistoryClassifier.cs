using System.Text.RegularExpressions;

namespace AlplaPortal.Domain.Approvals;

/// <summary>
/// v2.244.0 Approval Center V2 — Phase 2. Single, centralized classifier that turns a raw
/// RequestStatusHistory row into an approval (Level, Decision) pair. This is the ONLY place approval
/// classification is defined — controllers, the history service, the timeline and the tests all call
/// it, so there is no duplicated status/action string mapping (Phase-2 §20 requirement).
///
/// Proven against source + the DEV clone (Phase-2 first gate):
///  • Batch codes are SELF-DESCRIBING — the request-level status on a batch event is unreliable
///    (a batch can be WAITING_FINAL while its parent request is still WAITING_QUOTATION), so batch
///    rows classify by ActionTaken alone.
///  • Scalar codes (PAYMENT and legacy pre-batch QUOTATION) write ActionTaken="APPROVE" for BOTH the
///    area and final steps, so they MUST be disambiguated by the status transition:
///       APPROVE → NewStatus WAITING_FINAL_APPROVAL  = AREA / APPROVED   (DEV: 181 PAYMENT, 13 QUOT)
///       APPROVE → NewStatus APPROVED                = FINAL / APPROVED  (DEV: 147 PAYMENT, 12 QUOT)
///       REJECT  ← PrevStatus WAITING_FINAL_APPROVAL = FINAL / REJECTED
///       REJECT  ← PrevStatus WAITING_AREA_APPROVAL  = AREA / REJECTED
///       REQUEST_ADJUSTMENT → NewStatus AREA_ADJUSTMENT  = AREA / RETURNED
///       REQUEST_ADJUSTMENT → NewStatus FINAL_ADJUSTMENT = FINAL / RETURNED
/// </summary>
public static class ApprovalHistoryClassifier
{
    // Status codes that drive scalar disambiguation.
    public const string WaitingAreaApproval = "WAITING_AREA_APPROVAL";
    public const string WaitingCostCenter = "WAITING_COST_CENTER";
    public const string WaitingFinalApproval = "WAITING_FINAL_APPROVAL";
    public const string Approved = "APPROVED";
    public const string AreaAdjustment = "AREA_ADJUSTMENT";
    public const string FinalAdjustment = "FINAL_ADJUSTMENT";

    // ActionTaken codes that can represent an approval DECISION (the history-table candidate set).
    // Context events (CREATED, SUBMIT, BATCH_CREATED, FINANCE_RETURN_ADJUSTMENT, …) are excluded here
    // and only appear in the per-request timeline.
    public static readonly IReadOnlyList<string> DecisionActionCodes = new[]
    {
        "APPROVE", "REJECT", "REQUEST_ADJUSTMENT",
        "BATCH_AREA_APPROVED", "BATCH_FINAL_APPROVED",
        "BATCH_AREA_REJECTED", "BATCH_FINAL_REJECTED",
        "BATCH_AREA_ADJUSTMENT", "BATCH_FINAL_ADJUSTMENT",
        "BATCH_RESUBMITTED",
    };

    // Broader approval-RELEVANT set for the per-request audit timeline: decisions + the context events
    // needed to read the sequence, deliberately excluding unrelated technical noise (OCR, PO register,
    // document/item edits, status sync, payments — those live in their own histories).
    public static readonly IReadOnlyList<string> TimelineActionCodes = new[]
    {
        "CREATED", "SUBMIT",
        "BATCH_CREATED", "BATCH_CANDIDATES_SUBMITTED", "BATCH_EDITED",
        "APPROVE", "REJECT", "REQUEST_ADJUSTMENT",
        "BATCH_AREA_APPROVED", "BATCH_FINAL_APPROVED",
        "BATCH_AREA_REJECTED", "BATCH_FINAL_REJECTED",
        "BATCH_AREA_ADJUSTMENT", "BATCH_FINAL_ADJUSTMENT",
        "BATCH_RESUBMITTED",
        "FINANCE_RETURN_ADJUSTMENT",
    };

    public static ApprovalClassification Classify(string? actionTaken, string? newStatusCode, string? previousStatusCode)
    {
        switch (actionTaken)
        {
            // ── Batch codes: self-describing ───────────────────────────────────────────────
            case "BATCH_AREA_APPROVED": return new(ApprovalLevel.Area, ApprovalDecision.Approved, true);
            case "BATCH_FINAL_APPROVED": return new(ApprovalLevel.Final, ApprovalDecision.Approved, true);
            case "BATCH_AREA_REJECTED": return new(ApprovalLevel.Area, ApprovalDecision.Rejected, true);
            case "BATCH_FINAL_REJECTED": return new(ApprovalLevel.Final, ApprovalDecision.Rejected, true);
            case "BATCH_AREA_ADJUSTMENT": return new(ApprovalLevel.Area, ApprovalDecision.Returned, true);
            case "BATCH_FINAL_ADJUSTMENT": return new(ApprovalLevel.Final, ApprovalDecision.Returned, true);
            case "BATCH_RESUBMITTED": return new(null, ApprovalDecision.Resubmitted, true);

            // ── Scalar codes (PAYMENT + legacy QUOTATION): disambiguate by the status transition ──
            case "APPROVE":
                if (newStatusCode == WaitingFinalApproval) return new(ApprovalLevel.Area, ApprovalDecision.Approved, true);
                if (newStatusCode == Approved) return new(ApprovalLevel.Final, ApprovalDecision.Approved, true);
                return new(null, ApprovalDecision.Approved, true); // defensive: still a genuine approval

            case "REJECT":
                if (previousStatusCode == WaitingFinalApproval) return new(ApprovalLevel.Final, ApprovalDecision.Rejected, true);
                if (previousStatusCode is WaitingAreaApproval or WaitingCostCenter) return new(ApprovalLevel.Area, ApprovalDecision.Rejected, true);
                return new(null, ApprovalDecision.Rejected, true);

            case "REQUEST_ADJUSTMENT":
                if (newStatusCode == FinalAdjustment) return new(ApprovalLevel.Final, ApprovalDecision.Returned, true);
                if (newStatusCode == AreaAdjustment) return new(ApprovalLevel.Area, ApprovalDecision.Returned, true);
                return new(null, ApprovalDecision.Returned, true);

            // ── Everything else: approval-relevant CONTEXT (timeline only), not a decision ──
            default:
                return new(null, null, false);
        }
    }

    // "Lote #N" DISPLAY parse (Phase-2 Option A). This is a display hint only and is NEVER treated as
    // an ApprovalBatchId — there is no structured batch link on RequestStatusHistories.
    private static readonly Regex LotePattern =
        new(@"Lote\s*#\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static int? ParseLoteNumber(string? comment)
    {
        if (string.IsNullOrEmpty(comment)) return null;
        var m = LotePattern.Match(comment);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : (int?)null;
    }
}

public enum ApprovalLevel { Area, Final }

public enum ApprovalDecision { Approved, Rejected, Returned, Resubmitted }

/// <summary>Result of classifying one history row. Level is null for stage-less events (e.g. resubmit).</summary>
public readonly record struct ApprovalClassification(ApprovalLevel? Level, ApprovalDecision? Decision, bool IsApprovalDecision)
{
    public string? LevelCode => Level switch { ApprovalLevel.Area => "AREA", ApprovalLevel.Final => "FINAL", _ => null };
    public string? DecisionCode => Decision switch
    {
        ApprovalDecision.Approved => "APPROVED",
        ApprovalDecision.Rejected => "REJECTED",
        ApprovalDecision.Returned => "RETURNED",
        ApprovalDecision.Resubmitted => "RESUBMITTED",
        _ => null,
    };
}
