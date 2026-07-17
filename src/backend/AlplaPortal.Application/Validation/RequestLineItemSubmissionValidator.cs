using AlplaPortal.Application.Interfaces;

namespace AlplaPortal.Application.Validation;

/// <inheritdoc cref="IRequestLineItemSubmissionValidator"/>
public sealed class RequestLineItemSubmissionValidator : IRequestLineItemSubmissionValidator
{
    private const string QuotationNoItems =
        "O pedido de Cotação deve conter pelo menos um item válido (descrição, quantidade maior que zero e unidade).";
    private const string QuotationInvalidItem =
        "Cada item da Cotação deve ter descrição, quantidade maior que zero e unidade.";
    private const string PaymentNoItems =
        "Para submeter, o pedido deve conter pelo menos um item.";
    private const string PaymentInvalidLine =
        "Cada item do pagamento deve ter descrição, quantidade maior que zero, unidade e valor de linha maior que zero.";

    public LineItemValidationResult ValidateQuotation(IReadOnlyList<LineItemCandidate>? items, ISet<int> validUnitIds)
        => ValidateAll(items, validUnitIds, requireLineTotal: false, noItemsMessage: QuotationNoItems, invalidItemMessage: QuotationInvalidItem);

    public LineItemValidationResult ValidatePaymentSubmit(IReadOnlyList<LineItemCandidate>? items, ISet<int> validUnitIds)
        => ValidateAll(items, validUnitIds, requireLineTotal: true, noItemsMessage: PaymentNoItems, invalidItemMessage: PaymentInvalidLine);

    /// <summary>
    /// Shared rule: there must be at least one active item AND every active item must be valid.
    /// A single invalid line rejects the whole set — an invalid/zero line is never masked by a valid one.
    /// </summary>
    private static LineItemValidationResult ValidateAll(
        IReadOnlyList<LineItemCandidate>? items,
        ISet<int> validUnitIds,
        bool requireLineTotal,
        string noItemsMessage,
        string invalidItemMessage)
    {
        var result = new LineItemValidationResult();

        var active = (items ?? Array.Empty<LineItemCandidate>()).Where(i => !i.IsDeleted).ToList();
        if (active.Count == 0)
        {
            result.Add(noItemsMessage, field: "lineItems");
            return result;
        }

        foreach (var item in active)
        {
            var coreValid = IsItemCoreValid(item, validUnitIds);
            var financialValid = !requireLineTotal || item.LineTotal > 0;
            if (!coreValid || !financialValid)
            {
                // Structured, index-addressable error; Summary de-duplicates the text into one message.
                result.Errors.Add(new LineItemValidationError
                {
                    ItemIndex = item.Index,
                    Field = "lineItems",
                    Message = invalidItemMessage
                });
            }
        }

        return result;
    }

    private static bool IsItemCoreValid(LineItemCandidate item, ISet<int> validUnitIds)
        => !string.IsNullOrWhiteSpace(item.Description)
           && item.Quantity > 0
           && item.UnitId.HasValue
           && validUnitIds.Contains(item.UnitId.Value);
}
