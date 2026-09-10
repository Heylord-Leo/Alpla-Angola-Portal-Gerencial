using System;
using System.Linq.Expressions;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.242.0 — the single canonical rule for "is this returned P.O. correction personally owned by this
/// Buyer", spanning both request types. A P.O. group returned by Finance
/// (<c>WAITING_PO_CORRECTION</c>) is personal work for the Buyer responsible for that P.O.:
/// <list type="bullet">
///   <item>QUOTATION → <c>Request.BuyerId</c> (the assigned buyer).</item>
///   <item>PAYMENT → <c>RequestPoGroup.PoResponsibleBuyerId</c> (the last registrant; PAYMENT requests
///   carry no <c>BuyerId</c>).</item>
/// </list>
/// It is deliberately GROUP-based, never keyed on the request scalar status: REQ-275 aggregates to
/// <c>PO_PARTIALLY_UPLOADED</c> yet has a real correction group, and a request whose scalar drifted to
/// <c>WAITING_PO_CORRECTION</c> while its group is already <c>PO_ISSUED</c> must NOT match. Callers MUST
/// still AND this with the request access scope (plant/department) — ownership never overrides scope.
/// Both the "Para Minha Ação" task filter and the personal correction count consume this so they can
/// never drift apart. A null <c>PoResponsibleBuyerId</c> (unresolved PAYMENT owner) matches no one —
/// such a group is UNASSIGNED, never broadcast to every Buyer.
/// </summary>
public static class PersonalPoCorrectionPredicate
{
    /// <summary>Requests that have at least one WAITING_PO_CORRECTION group personally owned by
    /// <paramref name="currentUserId"/> under the cross-type rule.</summary>
    public static Expression<Func<Request, bool>> OwnedBy(Guid currentUserId) =>
        r => r.PoGroups.Any(g =>
            g.Status == RequestConstants.PoGroupStatuses.WaitingPoCorrection
            && ((r.RequestType!.Code == RequestConstants.Types.Quotation && r.BuyerId == currentUserId)
                || (r.RequestType!.Code == RequestConstants.Types.Payment && g.PoResponsibleBuyerId == currentUserId)));

    /// <summary>True when a single group is a correction personally owned by the user, given its owning
    /// request's type and buyer. Used to filter the per-owner correction groups shown on a card so one
    /// Buyer never sees a sibling group owned by another Buyer.</summary>
    public static bool OwnsGroup(string requestTypeCode, Guid? requestBuyerId, string groupStatus, Guid? groupPoResponsibleBuyerId, Guid currentUserId) =>
        groupStatus == RequestConstants.PoGroupStatuses.WaitingPoCorrection
        && ((requestTypeCode == RequestConstants.Types.Quotation && requestBuyerId == currentUserId)
            || (requestTypeCode == RequestConstants.Types.Payment && groupPoResponsibleBuyerId == currentUserId));
}
