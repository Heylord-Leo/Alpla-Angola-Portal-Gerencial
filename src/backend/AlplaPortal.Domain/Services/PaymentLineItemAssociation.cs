using System;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>The document an item is being attached to, reduced to what the rule needs.</summary>
public sealed record PaymentSourceDocumentBinding(
    Guid DocumentId,
    Guid RequestId,
    bool IsVoided,
    int? SupplierId,
    int? PlantId,
    string? Currency,
    int SequenceNumber);

/// <summary>The item's own values, as submitted.</summary>
public sealed record PaymentLineItemBinding(
    int? SupplierId,
    int? PlantId,
    string? CurrencyCode);

/// <summary>
/// Whether an item may belong to a source document.
///
/// <para>The four checks all guard the same thing: <b>a group's totals are derived from its items</b>,
/// so an item whose supplier, plant or currency disagrees with its document would land in a group
/// the document never intended to fund. The mismatch would be invisible until the operation-invoice
/// obligation was computed against the wrong baseline.</para>
///
/// <para>Pure, so the rule is asserted directly rather than inferred from a rejected request.</para>
/// </summary>
public static class PaymentLineItemAssociation
{
    /// <summary>The reason the item may not be attached, or null when it may.</summary>
    public static string? Validate(
        Guid requestId,
        PaymentSourceDocumentBinding? document,
        PaymentLineItemBinding item)
    {
        if (document == null)
            return "O documento de origem indicado não existe.";

        if (document.RequestId != requestId)
            return "O documento de origem indicado pertence a outro pedido.";

        if (document.IsVoided)
            return $"O Documento {document.SequenceNumber} foi anulado e não aceita novos itens.";

        // Null on the item means "inherit the document's value" — only a stated, different value
        // is a conflict. Requiring the client to echo them would make every item edit a chance to
        // introduce a mismatch.
        if (item.SupplierId != null && document.SupplierId != null &&
            item.SupplierId != document.SupplierId)
        {
            return $"O fornecedor do item difere do fornecedor do Documento {document.SequenceNumber}.";
        }

        if (item.PlantId != null && document.PlantId != null &&
            item.PlantId != document.PlantId)
        {
            return $"A planta do item difere da planta do Documento {document.SequenceNumber}.";
        }

        if (!string.IsNullOrWhiteSpace(item.CurrencyCode) &&
            !string.IsNullOrWhiteSpace(document.Currency) &&
            !string.Equals(item.CurrencyCode.Trim(), document.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return $"A moeda do item difere da moeda do Documento {document.SequenceNumber}.";
        }

        return null;
    }

    /// <summary>
    /// The values an item should carry once attached. Anything the item left unset is inherited
    /// from its document, which is why the checks above only fire on a stated disagreement.
    /// </summary>
    public static PaymentLineItemBinding Inherit(
        PaymentSourceDocumentBinding document, PaymentLineItemBinding item) =>
        new(
            item.SupplierId ?? document.SupplierId,
            item.PlantId ?? document.PlantId,
            string.IsNullOrWhiteSpace(item.CurrencyCode) ? document.Currency : item.CurrencyCode);

    /// <summary>
    /// Whether an item on this request must name a source document.
    ///
    /// <para>Only for PAYMENT, only while the workflow is mandatory, and only once the request
    /// actually has documents — a legacy request has none and must keep working untouched.</para>
    /// </summary>
    public static bool IsDocumentRequired(string? requestTypeCode, bool workflowMandatory, int activeDocumentCount) =>
        string.Equals(requestTypeCode, RequestConstants.Types.Payment, StringComparison.OrdinalIgnoreCase)
        && workflowMandatory
        && activeDocumentCount > 0;
}
