using System;
using System.Linq;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Infrastructure.Services.Finance;

/// <summary>
/// Builds the supplier-credit facts of ONE group for <see cref="IFinancePaymentEligibilityService"/> from the group's
/// own loaded payment rows and its latest reservation — the same facts the settlement endpoints enforce, so the
/// Finance queue can never advertise APPLY_CREDIT / REVERSE_CREDIT where the endpoint would refuse.
/// </summary>
public static class SupplierCreditFactsBuilder
{
    public static FinanceGroupCreditFacts? Build(RequestPoGroup group, SupplierCreditReservation? latestReservation)
    {
        if (group.SupplierCreditReservationId == null && latestReservation == null) return null;
        var payments = group.Payments ?? Array.Empty<RequestPayment>();
        string[] bankTypes = { RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentTypes.Regularization };
        return new FinanceGroupCreditFacts
        {
            IsCreditTouched = true,
            CreditCoverageAmount = group.CreditCoverageAmount,
            HasPlannedCreditRow = payments.Any(p => p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned),
            HasPendingBankRow = payments.Any(p => bankTypes.Contains(p.PaymentType)
                && (p.PaymentStatus == RequestPayment.PaymentStatuses.Planned || p.PaymentStatus == RequestPayment.PaymentStatuses.Scheduled)),
            ReservationStatus = latestReservation?.Status,
            HasCompletedCashRow = payments.Any(p => bankTypes.Contains(p.PaymentType) && p.PaymentStatus == RequestPayment.PaymentStatuses.Completed && (p.ActualPaidAmount ?? 0m) > 0m),
            HasPostSettlementEvidence = group.OperationalReceiptCompletedAtUtc != null
                || (group.FiscalReceiptAttachmentId != null && group.FiscalReceiptAttachmentId != Guid.Empty)
                || group.Status is RequestConstants.Statuses.WaitingReceipt or RequestConstants.Statuses.InFollowup
                    or RequestConstants.Statuses.WaitingReconciliation or RequestConstants.Statuses.WaitingFiscalReceipt or RequestConstants.Statuses.Completed,
        };
    }
}
