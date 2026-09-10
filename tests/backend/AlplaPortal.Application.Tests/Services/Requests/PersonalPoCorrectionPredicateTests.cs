using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.242.0 — the canonical cross-type personal PO-correction rule. QUOTATION ownership = Request.BuyerId;
/// PAYMENT ownership = RequestPoGroup.PoResponsibleBuyerId; always group-based (never the request scalar).
/// </summary>
public class PersonalPoCorrectionPredicateTests
{
    private static readonly Guid Samuel = Guid.NewGuid();
    private static readonly Guid Celestina = Guid.NewGuid();

    private const string WPC = "WAITING_PO_CORRECTION";
    private const string PoIssued = "PO_ISSUED";

    // ── OwnsGroup (per-group card filter) ──
    [Fact]
    public void Quotation_group_owned_by_request_buyer()
    {
        Assert.True(PersonalPoCorrectionPredicate.OwnsGroup(RequestConstants.Types.Quotation, Samuel, WPC, null, Samuel));
        Assert.False(PersonalPoCorrectionPredicate.OwnsGroup(RequestConstants.Types.Quotation, Samuel, WPC, null, Celestina));
    }

    [Fact]
    public void Payment_group_owned_by_po_responsible_buyer_not_request_buyer()
    {
        // PAYMENT carries no BuyerId; ownership comes from the group's PoResponsibleBuyerId.
        Assert.True(PersonalPoCorrectionPredicate.OwnsGroup(RequestConstants.Types.Payment, null, WPC, Celestina, Celestina));
        Assert.False(PersonalPoCorrectionPredicate.OwnsGroup(RequestConstants.Types.Payment, null, WPC, Celestina, Samuel));
    }

    [Fact]
    public void Non_correction_group_status_never_owned()
    {
        // Stale scalar class: the group is PO_ISSUED, so it is not correction work for anyone.
        Assert.False(PersonalPoCorrectionPredicate.OwnsGroup(RequestConstants.Types.Payment, null, PoIssued, Celestina, Celestina));
        Assert.False(PersonalPoCorrectionPredicate.OwnsGroup(RequestConstants.Types.Quotation, Samuel, PoIssued, null, Samuel));
    }

    [Fact]
    public void Unresolved_payment_owner_matches_no_one()
    {
        Assert.False(PersonalPoCorrectionPredicate.OwnsGroup(RequestConstants.Types.Payment, null, WPC, null, Samuel));
        Assert.False(PersonalPoCorrectionPredicate.OwnsGroup(RequestConstants.Types.Payment, null, WPC, null, Celestina));
    }

    // ── OwnedBy (request-level EXISTS, compiled) ──
    private static Request Req(string type, Guid? buyerId, params RequestPoGroup[] groups)
        => new()
        {
            Id = Guid.NewGuid(),
            RequestType = new RequestType { Code = type },
            BuyerId = buyerId,
            PoGroups = groups.ToList()
        };

    private static RequestPoGroup Group(string status, Guid? poResponsible)
        => new() { Id = Guid.NewGuid(), Status = status, PoResponsibleBuyerId = poResponsible };

    [Fact]
    public void OwnedBy_matches_quotation_for_its_buyer_only()
    {
        var pred = PersonalPoCorrectionPredicate.OwnedBy(Samuel).Compile();
        var r = Req(RequestConstants.Types.Quotation, Samuel, Group(WPC, null));
        Assert.True(pred(r));
        Assert.False(PersonalPoCorrectionPredicate.OwnedBy(Celestina).Compile()(r));
    }

    [Fact]
    public void OwnedBy_matches_payment_for_po_responsible_only()
    {
        var r = Req(RequestConstants.Types.Payment, null, Group(WPC, Celestina));
        Assert.True(PersonalPoCorrectionPredicate.OwnedBy(Celestina).Compile()(r));
        Assert.False(PersonalPoCorrectionPredicate.OwnedBy(Samuel).Compile()(r));
    }

    [Fact]
    public void OwnedBy_excludes_stale_scalar_group()
    {
        var r = Req(RequestConstants.Types.Payment, null, Group(PoIssued, Celestina));
        Assert.False(PersonalPoCorrectionPredicate.OwnedBy(Celestina).Compile()(r));
    }

    [Fact]
    public void OwnedBy_multi_owner_matches_each_owner_of_their_group()
    {
        // One request, two correction groups owned by different Buyers → each owner matches.
        var r = Req(RequestConstants.Types.Payment, null, Group(WPC, Samuel), Group(WPC, Celestina));
        Assert.True(PersonalPoCorrectionPredicate.OwnedBy(Samuel).Compile()(r));
        Assert.True(PersonalPoCorrectionPredicate.OwnedBy(Celestina).Compile()(r));
    }
}
