using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Infrastructure.Services.Finance;

/// <summary>
/// Centralized finance-action eligibility. Every predicate here mirrors the exact status guard
/// found in FinanceController's mutation endpoints (SchedulePayment/MarkAsPaid/ReturnForAdjustment)
/// at the time this service was written - if one of those endpoints' rules changes, update the
/// matching predicate here, not a second copy in GetPayments.
///
/// MAINTENANCE TRIGGER: changes to per-group eligibility or the multi-group Finance projection MUST
/// be validated against the Finance DEV Regression Harness (ZZTEST-FIN-*) —
/// docs/FINANCE_DEV_REGRESSION_HARNESS.md.
/// </summary>
public class FinancePaymentEligibilityService : IFinancePaymentEligibilityService
{
    // Mirrors FinanceController.SchedulePayment's allowedScheduleStatuses. Authoritative whenever
    // the group's own status is meaningful (QUOTATION always; PAYMENT once the group has genuinely
    // progressed past the legacy default).
    private static readonly string[] SchedulableGroupStatuses =
    {
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentRequestSent,
        RequestConstants.Statuses.AdvancePaymentRequired
    };

    // Legacy-fallback set for PAYMENT-type requests only, used solely when the group status is
    // null/empty/PENDING (i.e. genuinely unknown — the group was never actively synced). Every
    // included status corresponds to a point in the PAYMENT workflow strictly before a payment has
    // ever been scheduled or completed:
    //  - PO_ISSUED: P.O. just issued, nothing scheduled yet -> scheduling is the very next step.
    //  - PAYMENT_REQUEST_SENT: payment request sent to Finance, still awaiting scheduling.
    // Deliberately excludes PAYMENT_SCHEDULED (no reschedule flow exists anywhere in this
    // codebase — confirmed by a full-repo search — so re-enabling scheduling here would let a
    // stale/never-synced parent silently reopen an already-scheduled payment),
    // PAYMENT_COMPLETED/PAID/WAITING_RECEIPT/COMPLETED (payment already happened or the request is
    // fully done), and REJECTED/CANCELLED (terminal, negative). This set only ever matters when the
    // group itself carries no information — it must never be broader than what's safe to assume
    // about a request whose group state is completely unknown.
    private static readonly string[] SchedulableParentStatusesForPayment =
    {
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentRequestSent
    };

    // Mirrors FinanceController.MarkAsPaid's QUOTATION branch (allowedGroupPayStatuses).
    private static readonly string[] PayableGroupStatusesForQuotation =
    {
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentRequestSent,
        RequestConstants.Statuses.PaymentScheduled,
        RequestConstants.Statuses.AdvancePaymentRequired,
        RequestConstants.Statuses.AdvancePaymentScheduled
    };

    // Mirrors FinanceController.MarkAsPaid's PAYMENT branch (allowedPayStatuses) - parent status, not group status.
    private static readonly string[] PayableParentStatusesForPayment =
    {
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentRequestSent,
        RequestConstants.Statuses.PaymentScheduled
    };

    // Mirrors FinanceController.ReturnForAdjustment's allowedReturnStatuses.
    private static readonly string[] ReturnableParentStatuses =
    {
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentScheduled
    };

    // Group-scoped return: the group's OWN status is authoritative. Same two states as the legacy
    // parent guard, but read from the group so one group can be returned without regressing siblings.
    private static readonly string[] ReturnableGroupStatuses =
    {
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentScheduled
    };

    // Mirrors FinanceController.CancelSchedule's guard. Group-status-only, no type-branching:
    // both statuses are always genuinely written values (never a legacy default like PENDING),
    // so the group's own status is always authoritative — a group that has already moved on to
    // PAYMENT_COMPLETED/ADVANCE_PAYMENT_COMPLETED (or anywhere else) must never be cancellable.
    private static readonly string[] CancellableScheduledGroupStatuses =
    {
        RequestConstants.Statuses.PaymentScheduled,
        RequestConstants.Statuses.AdvancePaymentScheduled
    };

    public bool CanSchedule(string requestTypeCode, string requestStatusCode, string? groupStatus)
    {
        if (requestTypeCode == RequestConstants.Types.Quotation)
        {
            // Unchanged — the group status remains the sole authority for QUOTATION.
            return groupStatus != null && SchedulableGroupStatuses.Contains(groupStatus);
        }

        // PAYMENT: the group's own status is authoritative once it holds a genuine, non-legacy
        // value — including when that value places the group OUTSIDE SchedulableGroupStatuses
        // (e.g. already PAYMENT_SCHEDULED/PAYMENT_COMPLETED), so a stale parent status can never
        // re-enable scheduling for a group that has already moved on. Only fall back to
        // requestStatusCode when the group carries no real information at all.
        var isMeaningfulGroupStatus = !string.IsNullOrEmpty(groupStatus) && groupStatus != RequestConstants.PoGroupStatuses.Pending;
        if (isMeaningfulGroupStatus)
        {
            return SchedulableGroupStatuses.Contains(groupStatus!);
        }

        return SchedulableParentStatusesForPayment.Contains(requestStatusCode);
    }

    public bool CanPay(string requestTypeCode, string requestStatusCode, string? groupStatus)
    {
        if (requestTypeCode == RequestConstants.Types.Quotation)
        {
            return groupStatus != null && PayableGroupStatusesForQuotation.Contains(groupStatus);
        }

        return PayableParentStatusesForPayment.Contains(requestStatusCode);
    }

    public bool CanReturn(string? requestStatusCode)
    {
        return requestStatusCode != null && ReturnableParentStatuses.Contains(requestStatusCode);
    }

    public bool CanReturnGroup(string? groupStatus)
    {
        return groupStatus != null && ReturnableGroupStatuses.Contains(groupStatus);
    }

    public bool CanCancelSchedule(string? groupStatus)
    {
        return groupStatus != null && CancellableScheduledGroupStatuses.Contains(groupStatus);
    }

    public IReadOnlyList<string> EvaluateGroupActions(string requestTypeCode, string requestStatusCode, string? groupStatus)
    {
        var actions = new List<string>();
        if (CanSchedule(requestTypeCode, requestStatusCode, groupStatus)) actions.Add(FinancePaymentActionCodes.Schedule);
        if (CanPay(requestTypeCode, requestStatusCode, groupStatus)) actions.Add(FinancePaymentActionCodes.Pay);
        if (CanCancelSchedule(groupStatus)) actions.Add(FinancePaymentActionCodes.CancelSchedule);
        if (CanReturnGroup(groupStatus)) actions.Add(FinancePaymentActionCodes.Return);
        return actions;
    }

    public FinanceActionEligibilityResult Evaluate(FinanceEligibilityInput input)
    {
        var actions = new List<string>();
        var reasons = new Dictionary<string, string>();

        // v2.230.0 correctness fix: financial-mutation eligibility is the UNION of each group's OWN
        // per-group actions. A request-level "IsPaid" flag no longer suppresses actions — a paid
        // sibling group can never hide a still-actionable sibling's SCHEDULE/PAY/CANCEL/RETURN.
        // (IsPaid still drives the request-level ADD_PROOF affordance below; it is a document action,
        // not a financial mutation.)
        var union = new HashSet<string>();
        foreach (var g in input.PoGroups)
        {
            foreach (var a in EvaluateGroupActions(input.RequestTypeCode, input.RequestStatusCode, g.GroupStatus))
                union.Add(a);
        }

        // Legacy fallback: a request with no reconstructible group at all (e.g. an old PAYMENT-type
        // row) is still judged from the parent status — exactly as before. SCHEDULE/CANCEL_SCHEDULE
        // deliberately require a real group (never offered with zero groups, matching the historical
        // NoPoGroup behaviour); PAY and RETURN remain parent-status-driven for PAYMENT.
        if (input.PoGroups.Count == 0)
        {
            if (CanPay(input.RequestTypeCode, input.RequestStatusCode, null)) union.Add(FinancePaymentActionCodes.Pay);
            if (CanReturn(input.RequestStatusCode)) union.Add(FinancePaymentActionCodes.Return);
        }

        void Emit(string code, string absentReason)
        {
            if (union.Contains(code)) actions.Add(code);
            else reasons[code] = absentReason;
        }

        Emit(FinancePaymentActionCodes.Schedule, input.PoGroups.Count == 0
            ? FinancePaymentUnavailableReasons.NoPoGroup : FinancePaymentUnavailableReasons.NoGroupInSchedulableState);
        Emit(FinancePaymentActionCodes.Pay, input.PoGroups.Count == 0
            ? FinancePaymentUnavailableReasons.NoPoGroup : FinancePaymentUnavailableReasons.ParentOrGroupStatusNotEligibleForPay);
        Emit(FinancePaymentActionCodes.Return, FinancePaymentUnavailableReasons.ParentStatusNotEligibleForReturn);
        Emit(FinancePaymentActionCodes.CancelSchedule, input.PoGroups.Count == 0
            ? FinancePaymentUnavailableReasons.NoPoGroup : FinancePaymentUnavailableReasons.NoGroupCurrentlyScheduled);

        actions.Add(FinancePaymentActionCodes.AddNote);

        if (!input.HasProof && (actions.Contains(FinancePaymentActionCodes.Pay) || input.IsPaid))
        {
            actions.Add(FinancePaymentActionCodes.AddProof);
        }

        return new FinanceActionEligibilityResult
        {
            Actions = actions,
            UnavailableReasons = reasons
        };
    }
}
