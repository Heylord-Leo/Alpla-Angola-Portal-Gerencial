using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Receiving;

// v2.245.0 Receiving Finalization Fix — pure resolver + group-completion coverage (§24 A–H).
public class WinningQuotationReceiptResolverTests
{
    private static LineItemStatus Status(string code) => new() { Id = code.GetHashCode() & 0x7fffffff, Code = code, Name = code };
    private static QuotationItem Qi(int line, string statusCode, decimal qty = 2, decimal recv = 2) =>
        new() { Id = Guid.NewGuid(), LineNumber = line, Quantity = qty, ReceivedQuantity = recv, LineItemStatus = Status(statusCode) };
    private static RequestLineItem Li(int line, string? liStatus, Guid? selQiId = null, QuotationItem? selQi = null, decimal qty = 2, decimal recv = 0) =>
        new()
        {
            Id = Guid.NewGuid(), LineNumber = line, Quantity = qty, ReceivedQuantity = recv, IsDeleted = false,
            LineItemStatus = liStatus != null ? Status(liStatus) : null,
            SelectedQuotationItemId = selQiId, SelectedQuotationItem = selQi,
        };
    private static RequestPoGroup Group(params RequestLineItem[] items)
    {
        var g = new RequestPoGroup { Id = Guid.NewGuid(), Status = "IN_FOLLOWUP" };
        foreach (var i in items) { i.RequestPoGroupId = g.Id; g.LineItems.Add(i); }
        return g;
    }

    // ── A: the REQ-013 exact failure shape now resolves + completes ──
    [Fact]
    public void Req013Shape_LineUnlinkedButWinningItemReceived_IsComplete()
    {
        var win = Qi(1, "RECEIVED");
        var li = Li(1, "WAITING_QUOTATION", selQiId: null, selQi: null); // unlinked, not received on the line
        var group = Group(li);
        var winning = new List<QuotationItem> { win };

        // Old explicit-only behavior: NOT complete.
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(group));
        // New winning-items overload: complete via unambiguous line-number fallback.
        Assert.True(OperationalReceiptFacts.AreAllGroupItemsReceived(group, winning));
    }

    // ── B: explicit link path ──
    [Fact]
    public void ExplicitLink_Received_IsComplete()
    {
        var win = Qi(1, "RECEIVED");
        var li = Li(1, "PARTIALLY_RECEIVED", selQiId: win.Id, selQi: win);
        Assert.True(OperationalReceiptFacts.AreAllGroupItemsReceived(Group(li), new List<QuotationItem> { win }));
        Assert.Same(win, WinningQuotationReceiptResolver.Resolve(li, new List<QuotationItem> { win }));
    }

    // ── C: line-number fallback resolves ──
    [Fact]
    public void Fallback_ByLineNumber_Resolves()
    {
        var win = Qi(7, "RECEIVED");
        var li = Li(7, null, selQiId: null);
        Assert.Same(win, WinningQuotationReceiptResolver.Resolve(li, new List<QuotationItem> { Qi(3, "RECEIVED"), win }));
    }

    // ── D/E: partial vs exact ──
    [Fact]
    public void Partial_NotComplete_Exact_Complete()
    {
        var partial = Group(Li(1, "PARTIALLY_RECEIVED"));
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(partial, new List<QuotationItem> { Qi(1, "PARTIALLY_RECEIVED", recv: 1) }));
        var exact = Group(Li(1, "RECEIVED"));
        Assert.True(OperationalReceiptFacts.AreAllGroupItemsReceived(exact, new List<QuotationItem> { Qi(1, "RECEIVED") }));
    }

    // ── F: multi-line — all must be received ──
    [Fact]
    public void MultiLine_AllReceivedRequired()
    {
        var winning = new List<QuotationItem> { Qi(1, "RECEIVED"), Qi(2, "RECEIVED"), Qi(3, "PARTIALLY_RECEIVED") };
        var incomplete = Group(Li(1, null), Li(2, null), Li(3, null));
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(incomplete, winning));

        var winningAll = new List<QuotationItem> { Qi(1, "RECEIVED"), Qi(2, "RECEIVED"), Qi(3, "RECEIVED") };
        var complete = Group(Li(1, null), Li(2, null), Li(3, null));
        Assert.True(OperationalReceiptFacts.AreAllGroupItemsReceived(complete, winningAll));
    }

    // ── G: group-scoped — a group's completion depends only on its own items ──
    [Fact]
    public void GroupScoped_IndependentCompletion()
    {
        var winning = new List<QuotationItem> { Qi(1, "RECEIVED"), Qi(2, "PARTIALLY_RECEIVED") };
        var groupA = Group(Li(1, null));               // its winning item (line 1) is RECEIVED
        var groupB = Group(Li(2, null));               // its winning item (line 2) is partial
        Assert.True(OperationalReceiptFacts.AreAllGroupItemsReceived(groupA, winning));
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(groupB, winning));
    }

    // ── H: ambiguous duplicate line number is refused (never picks first) ──
    [Fact]
    public void AmbiguousLineNumber_Refused()
    {
        var li = Li(1, null, selQiId: null);
        var winning = new List<QuotationItem> { Qi(1, "RECEIVED"), Qi(1, "RECEIVED") }; // duplicate line 1
        Assert.Null(WinningQuotationReceiptResolver.Resolve(li, winning));
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(Group(li), winning));
    }

    // ── §23: explicit link is canonical and never overridden by a line-number match ──
    [Fact]
    public void ExplicitLink_TakesPrecedenceOverLineNumber()
    {
        var explicitQi = Qi(1, "RECEIVED");
        var otherSameLine = Qi(1, "PARTIALLY_RECEIVED");
        var li = Li(1, null, selQiId: explicitQi.Id, selQi: explicitQi);
        Assert.Same(explicitQi, WinningQuotationReceiptResolver.Resolve(li, new List<QuotationItem> { otherSameLine, explicitQi }));
    }

    // ── null winning list preserves pre-fix explicit-only behavior ──
    [Fact]
    public void NullWinning_PreservesExplicitOnlyBehavior()
    {
        var li = Li(1, "WAITING_QUOTATION", selQiId: null);
        Assert.False(OperationalReceiptFacts.AreAllGroupItemsReceived(Group(li), null));
        Assert.False(WinningQuotationReceiptResolver.IsLineItemReceived(li, null));
    }
}
