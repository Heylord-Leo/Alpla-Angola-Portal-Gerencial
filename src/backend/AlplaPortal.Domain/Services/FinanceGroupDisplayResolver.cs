namespace AlplaPortal.Domain.Services;

/// <summary>
/// Pure, static resolver for the supplier/currency display values shown for a single
/// RequestPoGroup in Finance (schedule/cancel-schedule history comments, the Finance payments
/// list, and the cancel-schedule confirmation modal DTO). Legacy PAYMENT-type auto-created groups
/// can carry a null RequestPoGroup.SupplierNameSnapshot/CurrencyCode (never actively synced — same
/// root-cause class as the DEC-149 group-status issue) even though the parent Request always knows
/// its own supplier/currency. This never writes back to the database — it only resolves what to
/// display, falling through: group snapshot -> selected quotation's supplier/currency (QUOTATION
/// type) -> request-level supplier/currency -> "---".
/// </summary>
public static class FinanceGroupDisplayResolver
{
    private const string Fallback = "---";

    public static string ResolveSupplierName(
        string? groupSupplierNameSnapshot,
        bool hasSelectedQuotation,
        string? selectedQuotationSupplierName,
        string? requestSupplierName)
    {
        if (!string.IsNullOrWhiteSpace(groupSupplierNameSnapshot)) return groupSupplierNameSnapshot!;
        if (hasSelectedQuotation && !string.IsNullOrWhiteSpace(selectedQuotationSupplierName)) return selectedQuotationSupplierName!;
        if (!string.IsNullOrWhiteSpace(requestSupplierName)) return requestSupplierName!;
        return Fallback;
    }

    public static string ResolveCurrencyCode(
        string? groupCurrencyCode,
        bool hasSelectedQuotation,
        string? selectedQuotationCurrencyCode,
        string? requestCurrencyCode)
    {
        if (!string.IsNullOrWhiteSpace(groupCurrencyCode)) return groupCurrencyCode!;
        if (hasSelectedQuotation && !string.IsNullOrWhiteSpace(selectedQuotationCurrencyCode)) return selectedQuotationCurrencyCode!;
        if (!string.IsNullOrWhiteSpace(requestCurrencyCode)) return requestCurrencyCode!;
        return Fallback;
    }
}
