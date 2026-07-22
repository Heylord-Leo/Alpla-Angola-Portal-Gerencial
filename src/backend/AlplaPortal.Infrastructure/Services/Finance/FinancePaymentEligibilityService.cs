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
    // Mirrors FinanceController.SchedulePayment's allowedScheduleStatuses.
    private static readonly string[] SchedulableGroupStatuses =
    {
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentRequestSent,
        RequestConstants.Statuses.AdvancePaymentRequired
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

    public bool CanSchedule(string? groupStatus)
    {
        return groupStatus != null && SchedulableGroupStatuses.Contains(groupStatus);
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

    public FinanceActionEligibilityResult Evaluate(FinanceEligibilityInput input)
    {
        var actions = new List<string>();
        var reasons = new Dictionary<string, string>();

        if (!input.IsPaid)
        {
            var canSchedule = input.PoGroups.Any(g => CanSchedule(g.GroupStatus));
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
        }
        else
        {
            reasons[FinancePaymentActionCodes.Schedule] = FinancePaymentUnavailableReasons.AlreadyPaid;
            reasons[FinancePaymentActionCodes.Pay] = FinancePaymentUnavailableReasons.AlreadyPaid;
            reasons[FinancePaymentActionCodes.Return] = FinancePaymentUnavailableReasons.AlreadyPaid;
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
