using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Structured blocking-reason codes of the group completion projection. Codes, not sentences:
/// presentation (pt-PT labels, ownership captions) belongs to the UI layer, which maps each code
/// to the actor who must act — Buyer for the P.O. family, Finance for payment/invoice/fiscal
/// receipt, Receiving for the operational receipt, Finance (Release 5 tool) for classification.
/// </summary>
public static class GroupCompletionBlockingReasons
{
    /// <summary>Document identity unknown — nothing else about the group is decidable.</summary>
    public const string ClassificationPending = "CLASSIFICATION_PENDING";

    /// <summary>No P.O. registered yet (group still PENDING / WAITING_PO).</summary>
    public const string PoMissing = "PO_MISSING";

    /// <summary>P.O. returned for correction (WAITING_PO_CORRECTION) — hard blocker.</summary>
    public const string PoCorrectionPending = "PO_CORRECTION_PENDING";

    /// <summary>Owed money still PLANNED/SCHEDULED, or the group has not reached the paid stage.</summary>
    public const string PaymentPending = "PAYMENT_PENDING";

    /// <summary>An advance reconciliation is open, or a required one has not completed.</summary>
    public const string ReconciliationPending = "RECONCILIATION_PENDING";

    /// <summary>Operational receipt not confirmed (items not all received).</summary>
    public const string ReceiptPending = "RECEIPT_PENDING";

    /// <summary>The operation-invoice obligation is not satisfied.</summary>
    public const string OperationInvoicePending = "OPERATION_INVOICE_PENDING";

    /// <summary>A separate Fiscal Receipt is owed and not yet uploaded.</summary>
    public const string FiscalReceiptPending = "FISCAL_RECEIPT_PENDING";
}

/// <summary>
/// Ownership of the next action for each blocking reason (approved Phase 4D mapping). Codes,
/// not sentences — presentation labels live in the UI layer; the ASSIGNMENT itself is business
/// truth and therefore lives here, beside the reasons, so no frontend ever re-derives it.
/// </summary>
public static class GroupCompletionOwnership
{
    public const string Buyer = "BUYER";
    public const string Finance = "FINANCE";
    public const string FinanceAdmin = "FINANCE_ADMIN";
    public const string Receiving = "RECEIVING";

    public static string OwnerOf(string blockingReason) => blockingReason switch
    {
        GroupCompletionBlockingReasons.ClassificationPending => FinanceAdmin,
        GroupCompletionBlockingReasons.PoMissing => Buyer,
        GroupCompletionBlockingReasons.PoCorrectionPending => Buyer,
        GroupCompletionBlockingReasons.PaymentPending => Finance,
        GroupCompletionBlockingReasons.ReconciliationPending => Finance,
        GroupCompletionBlockingReasons.ReceiptPending => Receiving,
        GroupCompletionBlockingReasons.OperationInvoicePending => Finance,
        GroupCompletionBlockingReasons.FiscalReceiptPending => Finance,
        _ => Finance
    };
}

/// <summary>
/// The deterministic completion reading of ONE RequestPoGroup — the single rulebook shared by
/// <c>EvaluateGroupCompletionAsync</c> (Phase 1 transitions), the future readiness UI and the
/// future parent completion. Never persisted: every boolean is derived from facts that already
/// live on the group, its payments, its reconciliations and its items.
/// </summary>
public sealed record GroupCompletionProjection
{
    public Guid GroupId { get; init; }

    /// <summary>Document identity is known. False fails closed: nothing below may complete.</summary>
    public bool Classified { get; init; }

    /// <summary>A P.O. was registered — the group has left PENDING / WAITING_PO.</summary>
    public bool PoSatisfied { get; init; }

    /// <summary>The group is not parked in WAITING_PO_CORRECTION (independent hard blocker).</summary>
    public bool NoBlockingCorrection { get; init; }

    /// <summary>Money actually moved and nothing owed remains open (see projector remarks).</summary>
    public bool PaymentSatisfied { get; init; }

    /// <summary>Operational receipt proven — stamp present, or every item already received.</summary>
    public bool ReceiptSatisfied { get; init; }

    /// <summary>The operation-invoice obligation reads satisfied (NOT_REQUIRED or SATISFIED).</summary>
    public bool OperationInvoiceSatisfied { get; init; }

    /// <summary>An APPROVED short-close exists. Informational — already folded into SATISFIED.</summary>
    public bool ClosedShort { get; init; }

    /// <summary>A separate Fiscal Receipt is owed (persisted classification result — never inferred).</summary>
    public bool FiscalReceiptRequired { get; init; }

    /// <summary>The fiscal-receipt dimension is discharged (uploaded, or not owed at all).</summary>
    public bool FiscalReceiptSatisfied { get; init; }

    /// <summary>Every applicable obligation satisfied — the group may complete.</summary>
    public bool Complete { get; init; }

    /// <summary>
    /// Everything satisfied EXCEPT a REQUIRED fiscal receipt: the group belongs in
    /// WAITING_FISCAL_RECEIPT, where only Finance's upload remains.
    /// </summary>
    public bool ReadyForFiscalReceipt { get; init; }

    /// <summary>
    /// Structured missing-obligation codes (<see cref="GroupCompletionBlockingReasons"/>), in
    /// lifecycle order, classification first. Empty exactly when <see cref="Complete"/> is true.
    /// </summary>
    public IReadOnlyList<string> BlockingReasons { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Pure, side-effect-free projector of <see cref="GroupCompletionProjection"/> — the ONE
/// authoritative group completion predicate (approved Phase 4 decisions 1–8).
///
/// <para><b>Inputs are the caller's entity graph.</b> The group must be loaded with
/// <c>LineItems</c> (and their <c>LineItemStatus</c> / <c>SelectedQuotationItem.LineItemStatus</c>)
/// for the receipt fact; payments and reconciliations are passed as the REQUEST's rows and
/// filtered here, so callers can never disagree about which rows count. The projector never
/// queries, never writes, never stamps — the lazy operational-receipt stamp is a write-path
/// concern of the evaluation service, not of this projection.</para>
/// </summary>
public static class GroupCompletionProjector
{
    /// <summary>Owed-money payment types: a pending row of these blocks completion. REFUND and
    /// OTHER represent money coming back or annotations, never an outstanding obligation.</summary>
    private static readonly string[] OwedMoneyPaymentTypes =
    {
        RequestPayment.PaymentTypes.Advance,
        RequestPayment.PaymentTypes.FinalBalance,
        RequestPayment.PaymentTypes.Regularization
    };

    /// <summary>Payment lifecycle states that mean money is still owed. SCHEDULED is never paid.</summary>
    private static readonly string[] PendingPaymentStatuses =
    {
        RequestPayment.PaymentStatuses.Planned,
        RequestPayment.PaymentStatuses.Scheduled
    };

    /// <summary>
    /// Group statuses at or beyond the actually-paid stage. PAID and PAYMENT_COMPLETED are
    /// treated as equivalent because MarkAsPaid resolves whichever lookup row it finds first and
    /// stamps its code onto the group. WAITING_RECONCILIATION is deliberately ABSENT: an advance
    /// under reconciliation may still owe a final balance.
    /// </summary>
    private static readonly string[] PaidStageStatuses =
    {
        RequestConstants.PoGroupStatuses.PaymentCompleted,
        RequestConstants.Statuses.Paid,
        RequestConstants.PoGroupStatuses.WaitingReceipt,
        RequestConstants.PoGroupStatuses.InFollowup,
        RequestConstants.PoGroupStatuses.WaitingFiscalReceipt,
        RequestConstants.PoGroupStatuses.Completed
    };

    private static readonly string[] PoNotRegisteredStatuses =
    {
        RequestConstants.PoGroupStatuses.Pending,
        RequestConstants.PoGroupStatuses.WaitingPo
    };

    /// <summary>
    /// Projects the completion reading of <paramref name="group"/>.
    /// </summary>
    /// <param name="group">The group, loaded with its line items (and their statuses).</param>
    /// <param name="requestPayments">ALL payment rows of the owning request. Group-linked rows are
    /// matched by id; rows with a null group id are request-level and block EVERY group — a
    /// reconciliation-born FINAL_BALANCE carries no group id, and attributing it to nobody would
    /// silently complete a request that still owes money.</param>
    /// <param name="requestReconciliations">ALL reconciliation rows of the owning request; the
    /// same null-group rule applies.</param>
    /// <param name="hasApprovedShortClose">An APPROVED short-close exists for this group
    /// (derived from OperationInvoiceShortCloses by the caller). Informational only.</param>
    public static GroupCompletionProjection Project(
        RequestPoGroup group,
        IEnumerable<RequestPayment> requestPayments,
        IEnumerable<RequestReconciliation> requestReconciliations,
        bool hasApprovedShortClose = false)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(requestPayments);
        ArgumentNullException.ThrowIfNull(requestReconciliations);

        var payments = requestPayments
            .Where(p => p.RequestPoGroupId == null || p.RequestPoGroupId == group.Id)
            .ToList();
        var reconciliations = requestReconciliations
            .Where(r => r.RequestPoGroupId == null || r.RequestPoGroupId == group.Id)
            .ToList();

        // ── Classified (approved decision: fail closed, never infer) ──
        var classified =
            !string.Equals(group.OperationInvoiceStatus,
                RequestConstants.OperationInvoiceStatuses.Unclassified,
                StringComparison.OrdinalIgnoreCase)
            && group.SourceDocumentType != null;

        // ── P.O. ──
        var poSatisfied = !PoNotRegisteredStatuses.Any(s => StatusIs(group.Status, s));
        var noBlockingCorrection =
            !StatusIs(group.Status, RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        // ── Payment ──
        var hasPendingOwedPayment = payments.Any(p =>
            OwedMoneyPaymentTypes.Any(t => StatusIs(p.PaymentType, t)) &&
            PendingPaymentStatuses.Any(s => StatusIs(p.PaymentStatus, s)));

        var hasActiveReconciliation = reconciliations.Any(r =>
            StatusIs(r.ReconciliationStatus, RequestReconciliation.ReconciliationStatuses.Draft) ||
            StatusIs(r.ReconciliationStatus, RequestReconciliation.ReconciliationStatuses.InProgress));

        var regularizationDischarged = !group.RequiresAdvanceRegularization ||
            reconciliations.Any(r =>
                StatusIs(r.ReconciliationStatus, RequestReconciliation.ReconciliationStatuses.Completed));

        var reachedPaidStage = PaidStageStatuses.Any(s => StatusIs(group.Status, s));

        var paymentSatisfied = !hasPendingOwedPayment
            && !hasActiveReconciliation
            && regularizationDischarged
            && reachedPaidStage;

        // ── Operational receipt: the stamp, or the item records that already prove it ──
        var receiptSatisfied = group.OperationalReceiptCompletedAtUtc != null
            || OperationalReceiptFacts.AreAllGroupItemsReceived(group);

        // ── Operation invoice: the single existing financial rulebook, never recomputed here ──
        var operationInvoiceSatisfied =
            RequestConstants.OperationInvoiceStatuses.IsSatisfied(group.OperationInvoiceStatus);

        // ── Fiscal receipt: the persisted classification result is authoritative ──
        var fiscalReceiptRequired = group.RequiresSeparateFiscalReceipt;
        var fiscalReceiptSatisfied = !fiscalReceiptRequired
            || (group.FiscalReceiptAttachmentId != null
                && group.FiscalReceiptAttachmentId != Guid.Empty
                && group.FiscalReceiptUploadedAtUtc != null);

        var allButFiscalReceipt = classified && poSatisfied && noBlockingCorrection
            && paymentSatisfied && receiptSatisfied && operationInvoiceSatisfied;

        var complete = allButFiscalReceipt && fiscalReceiptSatisfied;
        var readyForFiscalReceipt = allButFiscalReceipt && fiscalReceiptRequired && !fiscalReceiptSatisfied;

        var reasons = new List<string>();
        if (!classified) reasons.Add(GroupCompletionBlockingReasons.ClassificationPending);
        if (!poSatisfied) reasons.Add(GroupCompletionBlockingReasons.PoMissing);
        if (!noBlockingCorrection) reasons.Add(GroupCompletionBlockingReasons.PoCorrectionPending);
        if (hasPendingOwedPayment || !reachedPaidStage)
            reasons.Add(GroupCompletionBlockingReasons.PaymentPending);
        if (hasActiveReconciliation || !regularizationDischarged)
            reasons.Add(GroupCompletionBlockingReasons.ReconciliationPending);
        if (!receiptSatisfied) reasons.Add(GroupCompletionBlockingReasons.ReceiptPending);
        if (!operationInvoiceSatisfied)
            reasons.Add(GroupCompletionBlockingReasons.OperationInvoicePending);
        if (fiscalReceiptRequired && !fiscalReceiptSatisfied)
            reasons.Add(GroupCompletionBlockingReasons.FiscalReceiptPending);

        return new GroupCompletionProjection
        {
            GroupId = group.Id,
            Classified = classified,
            PoSatisfied = poSatisfied,
            NoBlockingCorrection = noBlockingCorrection,
            PaymentSatisfied = paymentSatisfied,
            ReceiptSatisfied = receiptSatisfied,
            OperationInvoiceSatisfied = operationInvoiceSatisfied,
            ClosedShort = hasApprovedShortClose,
            FiscalReceiptRequired = fiscalReceiptRequired,
            FiscalReceiptSatisfied = fiscalReceiptSatisfied,
            Complete = complete,
            ReadyForFiscalReceipt = readyForFiscalReceipt,
            BlockingReasons = reasons
        };
    }

    private static bool StatusIs(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The single rulebook for "every item of this group was physically received". Moved verbatim
/// from the Api-layer <c>RequestWorkflowHelper.AreAllGroupItemsReceived</c> so the completion
/// projection and the receiving endpoints can never drift apart; the Api helper now delegates
/// here. Quantities and statuses make no material/service distinction — a service line is
/// "received" when its confirmation sets the same RECEIVED status.
/// </summary>
public static class OperationalReceiptFacts
{
    public static bool AreAllGroupItemsReceived(RequestPoGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (group.LineItems == null || !group.LineItems.Any())
            return false;

        return group.LineItems.Where(li => !li.IsDeleted).All(li =>
            li.SelectedQuotationItemId.HasValue && li.SelectedQuotationItem != null
                ? li.SelectedQuotationItem.LineItemStatus?.Code == "RECEIVED"
                : li.LineItemStatus?.Code == "RECEIVED");
    }
}
