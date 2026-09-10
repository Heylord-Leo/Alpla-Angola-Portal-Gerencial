using System;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.242.0 Phase 1 — the canonical ActionType vocabulary and the PURE, reusable membership rules for
/// the personal-action projection. Domain-only: entity/primitive inputs, no DTOs, no UI labels, no
/// route construction (those belong to the Application projection service). It complements
/// <see cref="PersonalPoCorrectionPredicate"/> (which it reuses for the cross-type correction rule).
/// </summary>
public static class PersonalActionPredicates
{
    /// <summary>Canonical action-type codes surfaced by "Para Minha Ação".</summary>
    public static class ActionTypes
    {
        public const string PoCorrection = "PO_CORRECTION";
        public const string QuotationRequired = "QUOTATION_REQUIRED";
        public const string PoRegistration = "PO_REGISTRATION";
        public const string AreaApproval = "AREA_APPROVAL";
        public const string FinalApproval = "FINAL_APPROVAL";
        public const string RequestAdjustment = "REQUEST_ADJUSTMENT";
        public const string NotQuotedDecision = "NOT_QUOTED_DECISION";
        public const string Receiving = "RECEIVING";
        public const string FinanceAction = "FINANCE_ACTION";
    }

    /// <summary>True when the group is a WAITING_PO_CORRECTION group personally owned by the user under
    /// the cross-type rule (QUOTATION → request buyer; PAYMENT → group PoResponsibleBuyerId). Thin,
    /// entity-shaped wrapper over <see cref="PersonalPoCorrectionPredicate.OwnsGroup"/>.</summary>
    public static bool OwnsCorrectionGroup(Request request, RequestPoGroup group, Guid userId)
        => PersonalPoCorrectionPredicate.OwnsGroup(
            request.RequestType!.Code, request.BuyerId, group.Status, group.PoResponsibleBuyerId, userId);

    /// <summary>Terminal request statuses that never carry personal Buyer work (except a live P.O.
    /// correction, which is evaluated at the group level and is unaffected by this).</summary>
    public static bool IsTerminal(string statusCode) =>
        statusCode is RequestConstants.Statuses.Cancelled
                   or RequestConstants.Statuses.Rejected
                   or RequestConstants.Statuses.Completed
                   or RequestConstants.Statuses.Paid
                   or RequestConstants.Statuses.PaymentCompleted;
}
