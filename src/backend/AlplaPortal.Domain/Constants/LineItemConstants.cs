namespace AlplaPortal.Domain.Constants;

/// <summary>
/// Origins for <see cref="Entities.RequestLineItem.CreationOrigin"/>.
/// Null is treated as the standard requester/create flow.
/// </summary>
public static class LineItemCreationOrigins
{
    /// <summary>Standard creation (requester form, copy flow, add-item). Stored as null in practice.</summary>
    public const string Standard = "STANDARD";

    /// <summary>
    /// Created by the Buyer from a proforma line during quotation reconciliation,
    /// to cover a requested item omitted by the requester (or an old item-less request).
    /// </summary>
    public const string BuyerReconciliation = "BUYER_RECONCILIATION";
}

/// <summary>
/// <see cref="Entities.RequestStatusHistory.ActionTaken"/> codes related to line items.
/// </summary>
public static class LineItemHistoryActions
{
    /// <summary>An item was added through the standard add-item endpoint.</summary>
    public const string ItemAdded = "ITEM_ADDED";

    /// <summary>
    /// An item was created by the Buyer from a proforma line during reconciliation
    /// (omitted requested item workaround).
    /// </summary>
    public const string ItemAddedFromProforma = "ITEM_ADDED_FROM_PROFORMA";
}
