using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// When a PAYMENT request's origin documents may be changed, and what the request header should say
/// about them.
///
/// <para>Pure, so both rules can be asserted directly. Both are the kind of thing that is easy to
/// get subtly wrong in a controller and impossible to notice afterwards.</para>
/// </summary>
public static class PaymentSourceDocumentPolicy
{
    /// <summary>
    /// The only statuses in which origin documents may be created, edited, replaced or removed.
    ///
    /// <para>After submission the documents are frozen: they are what the approvers approved. To
    /// change one, the request must first be formally returned for adjustment — which is a decision
    /// somebody makes and the timeline records, not a side effect of opening a screen.</para>
    ///
    /// <para>The two adjustment statuses are included because both mean "sent back to the
    /// requester": AREA_ADJUSTMENT from the area manager, FINAL_ADJUSTMENT from the final approver.
    /// </para>
    /// </summary>
    public static readonly string[] EditableStatuses =
    {
        RequestConstants.Statuses.Draft,
        RequestConstants.Statuses.AreaAdjustment,
        RequestConstants.Statuses.FinalAdjustment
    };

    public static bool IsEditable(string? statusCode) =>
        EditableStatuses.Any(s => string.Equals(s, statusCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Why editing is refused, in the language the user reads. Never a bare 403 — the requester
    /// needs to know that the route forward is a formal return, not a permission change.
    /// </summary>
    public static string EditBlockedReason(string? statusCode) =>
        string.Equals(statusCode, RequestConstants.Statuses.Draft, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : "Os documentos de origem só podem ser alterados enquanto o pedido está em rascunho ou " +
              "foi devolvido para ajuste. Para alterar um documento, o pedido tem de ser devolvido " +
              "formalmente para ajuste.";

    /// <summary>
    /// A document removed before the request was ever submitted leaves no trace worth keeping —
    /// nothing downstream saw it. One removed after submission must be VOIDED instead, because its
    /// classification decision and any justification were already audited, and an audit must
    /// survive the object it describes.
    /// </summary>
    public static bool MayHardDelete(bool requestWasEverSubmitted) => !requestWasEverSubmitted;

    /// <summary>
    /// What the request header should show for supplier, plant and document type.
    ///
    /// <para><b>The rule:</b> a header field is populated only when every active document agrees on
    /// it. With several documents disagreeing it is set to <c>null</c>, and the UI says "vários".
    /// Nothing is invented — in particular there is no <c>MIXED</c> document type, because the
    /// obligation resolver would have to resolve it and no honest answer exists.</para>
    ///
    /// <para>These fields never decide an obligation, a group or a total. They exist so list
    /// screens, filters and the pre-multi-document detail view keep working.</para>
    ///
    /// <para><b>Plant is the exception</b> and is returned separately: it stays the request's own
    /// routing and authorization plant regardless of what the documents say, because approval
    /// routing and plant-scoped access are request-level concerns this release does not reopen.</para>
    /// </summary>
    public static PaymentHeaderCompatibility DeriveHeader(
        IEnumerable<PaymentSourceDocumentHeaderInput> activeDocuments)
    {
        var docs = activeDocuments as IReadOnlyList<PaymentSourceDocumentHeaderInput>
                   ?? activeDocuments.ToList();

        if (docs.Count == 0)
            return new PaymentHeaderCompatibility();

        return new PaymentHeaderCompatibility
        {
            SupplierId = SingleOrNull(docs.Select(d => d.SupplierId)),
            SourceDocumentType = SingleOrNull(docs
                .Select(d => RequestConstants.SourceDocumentTypes.Normalize(d.SourceDocumentType))),
            DocumentPlantId = SingleOrNull(docs.Select(d => d.PlantId)),
            HasSeveralDocuments = docs.Count > 1
        };
    }

    /// <summary>The value when every document agrees, otherwise null. Never a made-up composite.</summary>
    private static T? SingleOrNull<T>(IEnumerable<T?> values) where T : struct
    {
        var distinct = values.Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    private static string? SingleOrNull(IEnumerable<string?> values)
    {
        var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }
}

public sealed record PaymentSourceDocumentHeaderInput(
    int? SupplierId,
    int? PlantId,
    string? SourceDocumentType);

/// <summary>What the request header may safely display. Null means "several — do not claim one".</summary>
public sealed record PaymentHeaderCompatibility
{
    public int? SupplierId { get; init; }
    public string? SourceDocumentType { get; init; }

    /// <summary>
    /// The plant every document agrees on, or null. Offered for display only — the request's own
    /// PlantId is NOT overwritten with it, because routing and authorization depend on it.
    /// </summary>
    public int? DocumentPlantId { get; init; }

    public bool HasSeveralDocuments { get; init; }
}
