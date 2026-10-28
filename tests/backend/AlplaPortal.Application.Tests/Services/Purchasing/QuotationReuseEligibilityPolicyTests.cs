using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;
using P = AlplaPortal.Domain.Services.QuotationReuseEligibilityPolicy;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// v2.249.2 — the ONE reuse rule (DEC-147 Option C / P.O. cancellation D3), pure: candidates and winners of a CANCELLED
/// batch item or of a RELEASED item of an APPROVED batch need an explicit, active, unconsumed authorization for that
/// exact (item, source batch) pair. Shared by the batch endpoint (QuotationItemEligibilityService) and the Buyer
/// queue/Workspace coverage projection, so "Pronto para lote" can never disagree with CreateBatch again.
/// </summary>
public class QuotationReuseEligibilityPolicyTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);

    private static P.AuthorizationFacts Auth(Guid itemId, Guid sourceBatchId, bool active = true, DateTime? consumed = null, DateTime? revoked = null, Guid? consumedBy = null, DateTime? at = null)
        => new(Guid.NewGuid(), itemId, sourceBatchId, at ?? T0, active, consumed, revoked, consumedBy);

    [Fact]
    public void No_finished_use_is_eligible_normal()
    {
        var v = P.Resolve(Guid.NewGuid(), RequestConstants.ReconciliationStatuses.Mapped, Array.Empty<P.FinishedUse>(), Array.Empty<P.AuthorizationFacts>());
        Assert.True(v.IsEligible); Assert.False(v.IsReuseBlocked); Assert.Equal(P.Reasons.EligibleNormal, v.ReasonCode);
    }

    [Theory]
    [InlineData("NOT_QUOTED")]
    [InlineData("IGNORED")]
    [InlineData(null)]
    public void Unreconciled_item_is_never_a_candidate(string? recon)
    {
        var v = P.Resolve(Guid.NewGuid(), recon, Array.Empty<P.FinishedUse>(), Array.Empty<P.AuthorizationFacts>());
        Assert.False(v.IsEligible); Assert.False(v.IsReuseBlocked); Assert.Equal(P.Reasons.ItemNotReconciled, v.ReasonCode);
    }

    [Fact]
    public void Released_item_use_without_authorization_requires_authorization()
    {
        var item = Guid.NewGuid(); var batch = Guid.NewGuid();
        var v = P.Resolve(item, "MAPPED", new[] { new P.FinishedUse(item, batch, 1, T0) }, Array.Empty<P.AuthorizationFacts>());
        Assert.False(v.IsEligible); Assert.True(v.IsReuseBlocked); Assert.False(v.IsReuseAuthorized);
        Assert.Equal(P.Reasons.ReuseAuthorizationRequired, v.ReasonCode);
        Assert.Equal(batch, v.SourceBatchId); Assert.Equal(1, v.SourceBatchNumber);
    }

    [Fact]
    public void Active_authorization_for_the_same_source_batch_makes_it_eligible_again()
    {
        var item = Guid.NewGuid(); var batch = Guid.NewGuid();
        var auth = Auth(item, batch);
        var v = P.Resolve(item, "SUBSTITUTE", new[] { new P.FinishedUse(item, batch, 1, T0) }, new[] { auth });
        Assert.True(v.IsEligible); Assert.True(v.IsReuseAuthorized); Assert.False(v.IsReuseBlocked);
        Assert.Equal(P.Reasons.ReuseAuthorized, v.ReasonCode); Assert.Equal(auth.Id, v.AuthorizationId);
    }

    [Fact]
    public void Authorization_for_another_source_batch_does_not_count()
    {
        var item = Guid.NewGuid(); var batch = Guid.NewGuid();
        var v = P.Resolve(item, "MAPPED", new[] { new P.FinishedUse(item, batch, 1, T0) }, new[] { Auth(item, Guid.NewGuid()) });
        Assert.True(v.IsReuseBlocked); Assert.Equal(P.Reasons.ReuseAuthorizationRequired, v.ReasonCode);
    }

    [Fact]
    public void Consumed_and_revoked_authorizations_block_with_their_own_reasons()
    {
        var item = Guid.NewGuid(); var batch = Guid.NewGuid();
        var consumed = P.Resolve(item, "MAPPED", new[] { new P.FinishedUse(item, batch, 1, T0) },
            new[] { Auth(item, batch, active: false, consumed: T0.AddHours(1), consumedBy: Guid.NewGuid()) });
        Assert.True(consumed.IsReuseBlocked); Assert.Equal(P.Reasons.AuthorizationConsumed, consumed.ReasonCode);
        var revoked = P.Resolve(item, "MAPPED", new[] { new P.FinishedUse(item, batch, 1, T0) },
            new[] { Auth(item, batch, active: false, revoked: T0.AddHours(1)) });
        Assert.True(revoked.IsReuseBlocked); Assert.Equal(P.Reasons.AuthorizationRevoked, revoked.ReasonCode);
    }

    [Fact]
    public void Revalidation_of_the_consuming_batch_is_eligible_without_a_second_consumption()
    {
        var item = Guid.NewGuid(); var batch = Guid.NewGuid(); var consumer = Guid.NewGuid();
        var v = P.Resolve(item, "MAPPED", new[] { new P.FinishedUse(item, batch, 1, T0) },
            new[] { Auth(item, batch, active: false, consumed: T0.AddHours(1), consumedBy: consumer) }, consumingBatchId: consumer);
        Assert.True(v.IsEligible); Assert.False(v.IsReuseAuthorized); Assert.Equal(consumer, v.ConsumedByBatchId);
    }

    [Fact]
    public void Most_recent_finished_use_governs_the_cycle()
    {
        var item = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var uses = new[] { new P.FinishedUse(item, first, 1, T0), new P.FinishedUse(item, second, 2, T0.AddDays(1)) };
        // Authorized only for the OLD cycle → the new cycle still requires a new authorization.
        var v = P.Resolve(item, "MAPPED", uses, new[] { Auth(item, first) });
        Assert.True(v.IsReuseBlocked); Assert.Equal(second, v.SourceBatchId); Assert.Equal(2, v.SourceBatchNumber);
    }

    // ── loaded-graph derivation ──

    private static (ApprovalBatch batch, Guid winner, Guid candidate) BatchWith(string status, bool released, DateTime created)
    {
        var winner = Guid.NewGuid(); var candidate = Guid.NewGuid();
        var bi = new ApprovalBatchItem
        {
            Id = Guid.NewGuid(), RequestLineItemId = Guid.NewGuid(), SelectedQuotationItemId = winner,
            ReleasedAtUtc = released ? created.AddDays(3) : null, ReleaseKind = released ? "RESTART_QUOTATION" : null,
            Candidates = new List<ApprovalBatchItemCandidate> { new() { Id = Guid.NewGuid(), QuotationItemId = candidate }, new() { Id = Guid.NewGuid(), QuotationItemId = winner } },
        };
        var b = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 1, Status = status, CreatedAtUtc = created, Items = new List<ApprovalBatchItem> { bi } };
        return (b, winner, candidate);
    }

    [Fact]
    public void FinishedUsesFromLoaded_released_approved_item_dated_by_release_cancelled_batch_dated_by_creation()
    {
        var (approvedReleased, w1, c1) = BatchWith(RequestConstants.ApprovalBatchStatuses.Approved, released: true, T0);
        var (cancelled, w2, c2) = BatchWith(RequestConstants.ApprovalBatchStatuses.Cancelled, released: false, T0.AddDays(-5));
        var (approvedHeld, w3, c3) = BatchWith(RequestConstants.ApprovalBatchStatuses.Approved, released: false, T0);
        var (rejected, w4, c4) = BatchWith(RequestConstants.ApprovalBatchStatuses.Rejected, released: false, T0);
        var uses = P.FinishedUsesFromLoaded(new[] { approvedReleased, cancelled, approvedHeld, rejected });
        var ids = uses.Select(u => u.QuotationItemId).ToHashSet();
        Assert.Contains(w1, ids); Assert.Contains(c1, ids);
        Assert.Contains(w2, ids); Assert.Contains(c2, ids);
        Assert.DoesNotContain(w3, ids); Assert.DoesNotContain(c3, ids); // still held → not a finished use
        Assert.DoesNotContain(w4, ids); Assert.DoesNotContain(c4, ids); // REJECTED releases to the pool without the reuse rule
        Assert.Equal(T0.AddDays(3), uses.First(u => u.QuotationItemId == w1).AtUtc);
        Assert.Equal(T0.AddDays(-5), uses.First(u => u.QuotationItemId == w2).AtUtc);
    }

    [Fact]
    public void ReuseBlockedQuotationItemIds_on_a_loaded_request_honours_active_authorizations()
    {
        var (b, winner, candidate) = BatchWith(RequestConstants.ApprovalBatchStatuses.Approved, released: true, T0);
        var r = new Request
        {
            ApprovalBatches = new List<ApprovalBatch> { b },
            QuotationReuseAuthorizations = new List<QuotationReuseAuthorization>
            {
                new() { Id = Guid.NewGuid(), QuotationItemId = winner, SourceApprovalBatchId = b.Id, AuthorizedAtUtc = T0.AddDays(4), IsActive = true },
            },
        };
        var blocked = P.ReuseBlockedQuotationItemIds(r);
        Assert.DoesNotContain(winner, blocked);   // authorized → selectable again
        Assert.Contains(candidate, blocked);      // the other option still needs its own authorization
    }
}
