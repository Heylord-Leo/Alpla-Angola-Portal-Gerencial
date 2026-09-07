using System;
using AlplaPortal.Application.DTOs.Dashboard;

namespace AlplaPortal.Infrastructure.Services.Dashboard;

// ── Dashboard V2 B9.3 — HONEST backfill evidence rules (pure). ──
// Given a resolved current aging stage and the ONLY reliable, server-authored, entity-grain timestamps that
// exist today, return a defensible StageEnteredAtUtc — or NULL when no such evidence exists. Backfill NEVER
// fabricates age: an unknown entry time stays null (the read side renders "Idade não disponível"). These
// rules are deliberately conservative; a stage returns a timestamp ONLY where the source provably marks
// entry INTO that exact stage at the correct grain.
//
// Explicitly FORBIDDEN as stage-entry sources (never referenced here): Request.CreatedAtUtc/UpdatedAtUtc,
// ApprovalBatch.UpdatedAtUtc, RequestPoGroup.UpdatedAtUtc, NeedByDateUtc, ScheduledDateUtc (a deadline),
// PaidDateUtc (a Finance-entered business date, not a system transition), any deployment/current time.
public static class OperationalStageBackfillEvidence
{
    /// <summary>
    /// APPROVAL_BATCH: a batch is created directly at WAITING_AREA_APPROVAL, so its CreatedAtUtc is the
    /// reliable entry into AREA_APPROVAL. For FINAL_APPROVAL / ADJUSTMENT the same CreatedAtUtc is the AREA
    /// entry (not the current stage's), and no batch-linked transition stamp exists (RequestStatusHistory is
    /// request-grained, with no ApprovalBatchId) — so those are UNKNOWN → null.
    /// </summary>
    public static DateTime? ForApprovalBatch(string stageCode, DateTime batchCreatedAtUtc)
        => stageCode == PipelineStages.AreaApproval ? batchCreatedAtUtc : null;

    /// <summary>
    /// PO_GROUP:
    ///  • FIN_SCHEDULED — the group's SCHEDULED RequestPayment row was created (server-authored, group-linked)
    ///    when Finance scheduled the payment: that IS entry into FIN_SCHEDULED. Use the LATEST such creation
    ///    (handles re-scheduling / multiple payments). This is NOT ScheduledDateUtc (a future deadline).
    ///  • DOCUMENTATION — OperationalReceiptCompletedAtUtc marks receiving completion = entry into the fiscal
    ///    documentation stage (server-authored, group-grain). Null when the stamp was never written.
    ///  • Every other PO stage (PO_WAITING — the group is created at PENDING, not WAITING_PO; PO_CORRECTION;
    ///    FIN_NEEDS_SCHEDULING; REC_READY — no server payment-completion stamp; REC_WAITING/FOLLOWUP/SUPPLIER)
    ///    has no reliable group-grain server entry timestamp → UNKNOWN → null.
    /// </summary>
    public static DateTime? ForPoGroup(
        string stageCode,
        DateTime? latestScheduledPaymentCreatedAtUtc,
        DateTime? operationalReceiptCompletedAtUtc)
        => stageCode switch
        {
            PipelineStages.FinanceScheduled => latestScheduledPaymentCreatedAtUtc,
            PipelineStages.Documentation => operationalReceiptCompletedAtUtc,
            _ => null,
        };
}
