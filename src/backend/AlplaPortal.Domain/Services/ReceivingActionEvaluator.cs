using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// The single canonical rule for which Receiving actions a <c>RequestPoGroup</c> exposes, keyed on the
/// group's own status. Extracted (behavior-preserving) from the inline guards in
/// RequestsController.MoveToReceipt / ConfirmReceiving so those endpoints, the group-level Receiving
/// queue and the Dashboard all agree and can never drift:
///   RequestsController (operational) + ReceivingQueueProjection + DashboardV2  →  this evaluator.
///
/// Pure: no DB, no authorization/role resolution (the endpoints keep their own Receiving-role checks),
/// no request-scalar-status shortcuts. Actionability is judged on the GROUP status only.
/// </summary>
public static class ReceivingActionEvaluator
{
    public const string MoveToReceipt = "MOVE_TO_RECEIPT";
    public const string ConfirmReceiving = "CONFIRM_RECEIVING";

    // Verbatim mirror of the endpoint guards:
    //   MoveToReceipt   accepts { PAYMENT_COMPLETED }
    //   ConfirmReceiving accepts { WAITING_RECEIPT, IN_FOLLOWUP, PAYMENT_COMPLETED, WAITING_SUPPLIER_DELIVERY }
    private static readonly string[] MovableStatuses =
    {
        RequestConstants.Statuses.PaymentCompleted,
    };
    private static readonly string[] ConfirmableStatuses =
    {
        RequestConstants.Statuses.WaitingReceipt,
        RequestConstants.Statuses.InFollowup,
        RequestConstants.Statuses.PaymentCompleted,
        RequestConstants.Statuses.WaitingSupplierDelivery,
    };

    /// <summary>The union of all Receiving-actionable group statuses — the single source a SQL prefilter
    /// must use so it can never drift from <see cref="IsReceivingActionable"/>.</summary>
    public static readonly IReadOnlyList<string> ActionableStatuses =
        MovableStatuses.Union(ConfirmableStatuses).ToArray();

    // ── v2.245.0 duplicate-confirm fix ──────────────────────────────────────────
    // CONFIRM_RECEIVING is the operator's explicit, one-time attestation. It is available ONLY from a
    // PRE-confirmation receiving status. WAITING_RECEIPT is the POST-confirmation state (all items
    // received AND confirmed, awaiting the supplier fiscal receipt / finalization) — the group reaches it
    // ONLY via a confirm-receiving, never via item-receipt registration (that syncs the REQUEST scalar,
    // not the group). Allowing confirm again from WAITING_RECEIPT is exactly the duplicate-confirm defect
    // (REQ-06/07/2026-023): a second CONFIRM_RECEIVING history row for a no-op transition.
    //
    // IN_FOLLOWUP stays confirmable: a partially-received group can be confirmed again once the remaining
    // items arrive, advancing it to WAITING_RECEIPT.
    private static readonly string[] ConfirmActionStatuses =
    {
        RequestConstants.Statuses.PaymentCompleted,
        RequestConstants.Statuses.InFollowup,
        RequestConstants.Statuses.WaitingSupplierDelivery,
    };

    // Post-confirmation group states: the operator's receiving confirmation has already happened; a
    // further confirm must be refused (no duplicate audit).
    private static readonly string[] PostConfirmStatuses =
    {
        RequestConstants.Statuses.WaitingReceipt,
        RequestConstants.Statuses.WaitingFiscalReceipt,
        RequestConstants.Statuses.Completed,
    };

    public static bool CanMoveToReceipt(string? groupStatus)
        => groupStatus != null && MovableStatuses.Contains(groupStatus);

    /// <summary>
    /// Whether a group status is Receiving-actionable at all (queue/dashboard membership). Broad by
    /// design — includes the post-confirmation WAITING_RECEIPT (which still needs the fiscal-receipt
    /// follow-up surfaced in the workspace). NOT the guard for the confirm-receiving action — use
    /// <see cref="CanConfirmReceivingAction"/> for that.
    /// </summary>
    public static bool CanConfirmReceiving(string? groupStatus)
        => groupStatus != null && ConfirmableStatuses.Contains(groupStatus);

    /// <summary>
    /// The canonical guard for the explicit CONFIRM_RECEIVING action: true only from a PRE-confirmation
    /// receiving status. Excludes WAITING_RECEIPT (post-confirmation) so a second confirmation is refused.
    /// </summary>
    public static bool CanConfirmReceivingAction(string? groupStatus)
        => groupStatus != null && ConfirmActionStatuses.Contains(groupStatus);

    /// <summary>
    /// v2.245.5 — item-quantity registration AND correction are allowed ONLY while the group is in a
    /// PRE-confirmation receiving status (the same set as the confirm action). Once the operator has
    /// confirmed receiving (WAITING_RECEIPT and beyond) quantities are frozen: a correction first requires
    /// the explicit, audited REABRIR RECEBIMENTO, which returns the group to IN_FOLLOWUP.
    /// </summary>
    public static bool CanRegisterItemReceipt(string? groupStatus)
        => groupStatus != null && ConfirmActionStatuses.Contains(groupStatus);

    /// <summary>True when the group has already had its receiving confirmed (post-confirmation state).</summary>
    public static bool IsReceivingConfirmed(string? groupStatus)
        => groupStatus != null && PostConfirmStatuses.Contains(groupStatus);

    /// <summary>A group is Receiving-actionable when it exposes at least one Receiving action.</summary>
    public static bool IsReceivingActionable(string? groupStatus)
        => CanMoveToReceipt(groupStatus) || CanConfirmReceiving(groupStatus);

    /// <summary>The available Receiving action codes for a group status (order: move, then confirm).</summary>
    public static IReadOnlyList<string> Evaluate(string? groupStatus)
    {
        var actions = new List<string>(2);
        if (CanMoveToReceipt(groupStatus)) actions.Add(MoveToReceipt);
        if (CanConfirmReceiving(groupStatus)) actions.Add(ConfirmReceiving);
        return actions;
    }

    // ── Dashboard/queue bucketing: each actionable group maps to exactly ONE current-state bucket. ──
    public static class Buckets
    {
        public const string ReadyForReceipt = "READY_FOR_RECEIPT";     // PAYMENT_COMPLETED (ready to move to receipt)
        public const string WaitingReceipt = "WAITING_RECEIPT";
        public const string FollowUp = "IN_FOLLOWUP";                   // partial receipt in progress
        public const string WaitingSupplierDelivery = "WAITING_SUPPLIER_DELIVERY";
    }

    /// <summary>The single actionable bucket for a group status, or null when not Receiving-actionable.</summary>
    public static string? ActionableBucket(string? groupStatus) => groupStatus switch
    {
        RequestConstants.Statuses.PaymentCompleted => Buckets.ReadyForReceipt,
        RequestConstants.Statuses.WaitingReceipt => Buckets.WaitingReceipt,
        RequestConstants.Statuses.InFollowup => Buckets.FollowUp,
        RequestConstants.Statuses.WaitingSupplierDelivery => Buckets.WaitingSupplierDelivery,
        _ => null,
    };
}
