namespace AlplaPortal.Domain.Services;

/// <summary>
/// Pure, static formatter for the structured group-attribution prefix written at the start of
/// Finance history comments (SchedulePayment/MarkAsPaid/ConfirmAdvancePayment). Replaces the
/// legacy "[Grupo P.O.: {supplier} | ... | GroupId: {guid}]" shape — the GUID was never a
/// user-facing identifier and confused non-technical readers — with the portal's existing
/// "Lote #{n}" terminology (RequestPoGroup.ApprovalBatch.BatchNumber), already used throughout
/// Approvals/Buyer screens. When a group has no ApprovalBatch (legacy pre-batch groups, or the
/// single auto-created group on a PAYMENT-type request), the "Lote #n" segment is omitted
/// entirely — never rendered as "Lote #0" or an empty placeholder.
///
/// Only used for NEWLY WRITTEN comments. Existing stored rows are never rewritten — see
/// FinanceHistoryAmountResolver for how both the old and new shapes are read back.
/// </summary>
public static class FinanceHistoryCommentFormatter
{
    public static string FormatGroupPrefix(int? batchNumber, string? supplierName, string? currencyCode, string amountFieldLabel, decimal amount)
    {
        var lotePart = batchNumber.HasValue ? $"Lote #{batchNumber.Value} | " : string.Empty;
        var supplierPart = string.IsNullOrWhiteSpace(supplierName) ? "---" : supplierName;
        var currencyPart = string.IsNullOrWhiteSpace(currencyCode) ? "---" : currencyCode;
        return $"[{lotePart}{supplierPart} | Moeda: {currencyPart} | {amountFieldLabel}: {amount:N2}]";
    }
}
