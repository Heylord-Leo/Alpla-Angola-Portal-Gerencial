using System;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// The identity of a PO group built from a PAYMENT request's source documents.
///
/// <para>Two line items belong to the same group exactly when all five components match. The first
/// three mirror the quotation path (<c>GroupBuilderService</c>); <b>Plant</b> and
/// <b>SourceDocumentType</b> are added in Release 3.</para>
///
/// <para><b>Why the document type is part of the identity.</b> One supplier, one plant, two
/// documents — a Pró-forma and a Factura. Without the type in the key they would land in one group,
/// which would then owe an operation invoice for part of its value and not for the rest. That is an
/// obligation the model cannot express, and the alternatives are worse: a "MIXED" type the resolver
/// cannot resolve, or a silent compromise on one half. A PO group is the unit of post-payment
/// obligation, so two lines with different obligations must not share one. Groups are cheap; an
/// inexpressible obligation is not.</para>
/// </summary>
public readonly record struct PaymentGroupingKey(
    int? SupplierId,
    string? CurrencyCode,
    string? PaymentConditionCode,
    int? PlantId,
    string? SourceDocumentType)
{
    /// <summary>
    /// Builds the key from a source document plus the request-level payment condition.
    ///
    /// <para>Currency and type are normalized so that casing or a superseded alias can never split
    /// what is really one group — <c>FINAL_INVOICE</c> and <c>INVOICE</c> are the same document.</para>
    /// </summary>
    public static PaymentGroupingKey From(
        int? supplierId,
        string? currencyCode,
        string? paymentConditionCode,
        int? plantId,
        string? sourceDocumentType) =>
        new(
            supplierId,
            Normalize(currencyCode),
            Normalize(paymentConditionCode),
            plantId,
            RequestConstants.SourceDocumentTypes.Normalize(sourceDocumentType));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    /// <summary>A short, stable label for logs and history entries.</summary>
    public override string ToString() =>
        $"supplier={SupplierId?.ToString() ?? "-"}, currency={CurrencyCode ?? "-"}, " +
        $"condition={PaymentConditionCode ?? "-"}, plant={PlantId?.ToString() ?? "-"}, " +
        $"document={SourceDocumentType ?? "-"}";
}
