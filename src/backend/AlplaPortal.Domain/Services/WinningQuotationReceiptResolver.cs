using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// v2.245.0 Receiving Finalization Fix — the SINGLE canonical rule for locating the winning
/// <see cref="QuotationItem"/> that carries a <see cref="RequestLineItem"/>'s receipt, and for deciding
/// whether a line item is physically received. Group completion, the receiving sync and the repair
/// preview all call this so they can never drift.
///
/// Root cause it addresses (REQ-01/07/2026-013 class): the legacy/winning-quotation flow registers the
/// receipt on the winning <c>QuotationItem</c> (status RECEIVED) while the <c>RequestLineItem</c> keeps
/// <c>SelectedQuotationItemId = NULL</c> and its own status un-advanced. The old bridge saw only the
/// explicit pointer, so a fully-received request was stuck IN_FOLLOWUP.
///
/// Resolution priority (never guesses):
///   1. explicit <c>SelectedQuotationItemId</c> (canonical — used even if the nav wasn't eager-loaded)
///   2. UNAMBIGUOUS <c>LineNumber</c> match within the WINNING quotation's items
///   3. unresolved (null) — including any ambiguous duplicate line number, which is refused
///
/// LineNumber is proven unique within a request's active line items and within a single quotation
/// (Phase-0 §4 data gate), so (2) is deterministic; the duplicate-guard is belt-and-suspenders.
/// </summary>
public static class WinningQuotationReceiptResolver
{
    public const string ReceivedStatusCode = "RECEIVED";

    public static QuotationItem? Resolve(RequestLineItem lineItem, IReadOnlyCollection<QuotationItem>? winningQuotationItems)
    {
        // 1. Explicit link is canonical and must never be overridden.
        if (lineItem.SelectedQuotationItemId.HasValue)
            return lineItem.SelectedQuotationItem;

        if (winningQuotationItems == null || winningQuotationItems.Count == 0)
            return null;

        // 2. Unambiguous line-number match within the winning quotation only.
        QuotationItem? match = null;
        foreach (var qi in winningQuotationItems)
        {
            if (qi.LineNumber != lineItem.LineNumber) continue;
            if (match != null) return null; // 3. ambiguous → refuse, never pick first
            match = qi;
        }
        return match;
    }

    /// <summary>True when the line item is received on EITHER its own status or its resolved winning
    /// quotation item. Both writers only stamp RECEIVED at full quantity, so this never weakens partial
    /// safety.</summary>
    public static bool IsLineItemReceived(RequestLineItem lineItem, IReadOnlyCollection<QuotationItem>? winningQuotationItems)
    {
        if (lineItem.LineItemStatus?.Code == ReceivedStatusCode)
            return true;
        var qi = Resolve(lineItem, winningQuotationItems);
        return qi?.LineItemStatus?.Code == ReceivedStatusCode;
    }
}
