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

    public bool CanCancelSchedule(string? groupStatus)
    {
        return groupStatus != null && CancellableScheduledGroupStatuses.Contains(groupStatus);
    }

    public FinanceActionEligibilityResult Evaluate(FinanceEligibilityInput input)
    {
        var actions = new List<string>();
        var reasons = new Dictionary<string, string>();

        if (!input.IsPaid)
        {
            var canSchedule = input.PoGroups.Any(g => CanSchedule(input.RequestTypeCode, input.RequestStatusCode, g.GroupStatus));
            if (canSchedule)
            {
                actions.Add(FinancePaymentActionCodes.Schedule);
            }
            else
            {
                reasons[FinancePaymentActionCodes.Schedule] = input.PoGroups.Count == 0
                    ? FinancePaymentUnavailableReasons.NoPoGroup
                    : FinancePaymentUnavailableReasons.NoGroupInSchedulableState;
            }

            var canPay = input.RequestTypeCode == RequestConstants.Types.Quotation
                ? input.PoGroups.Any(g => CanPay(input.RequestTypeCode, input.RequestStatusCode, g.GroupStatus))
                : CanPay(input.RequestTypeCode, input.RequestStatusCode, null);
            if (canPay)
            {
                actions.Add(FinancePaymentActionCodes.Pay);
            }
            else
            {
                reasons[FinancePaymentActionCodes.Pay] = input.RequestTypeCode == RequestConstants.Types.Quotation && input.PoGroups.Count == 0
                    ? FinancePaymentUnavailableReasons.NoPoGroup
                    : FinancePaymentUnavailableReasons.ParentOrGroupStatusNotEligibleForPay;
            }

            if (CanReturn(input.RequestStatusCode))
            {
                actions.Add(FinancePaymentActionCodes.Return);
            }
            else
            {
                reasons[FinancePaymentActionCodes.Return] = FinancePaymentUnavailableReasons.ParentStatusNotEligibleForReturn;
            }

            var canCancelSchedule = input.PoGroups.Any(g => CanCancelSchedule(g.GroupStatus));
            if (canCancelSchedule)
            {
                actions.Add(FinancePaymentActionCodes.CancelSchedule);
            }
            else
            {
                reasons[FinancePaymentActionCodes.CancelSchedule] = input.PoGroups.Count == 0
                    ? FinancePaymentUnavailableReasons.NoPoGroup
                    : FinancePaymentUnavailableReasons.NoGroupCurrentlyScheduled;
            }
        }
        else
        {
            reasons[FinancePaymentActionCodes.Schedule] = FinancePaymentUnavailableReasons.AlreadyPaid;
            reasons[FinancePaymentActionCodes.Pay] = FinancePaymentUnavailableReasons.AlreadyPaid;
            reasons[FinancePaymentActionCodes.Return] = FinancePaymentUnavailableReasons.AlreadyPaid;
            reasons[FinancePaymentActionCodes.CancelSchedule] = FinancePaymentUnavailableReasons.AlreadyPaid;
        }

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
