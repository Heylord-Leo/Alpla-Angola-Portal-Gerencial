using System.Globalization;
using System.Text.RegularExpressions;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Result of resolving a Finance history event's display amount. Amount, GroupId, and LoteNumber
/// are independently nullable — a structured comment can yield one without another (e.g. a
/// malformed segment next to a perfectly parseable one). GroupId is retained only for backward
/// compatibility with legacy comments and is never a user-facing identifier — LoteNumber (the
/// portal's existing "Lote #n" terminology) is the one meant for display.
/// </summary>
public sealed record FinanceHistoryAmountResolution(
    decimal? Amount,
    Guid? GroupId,
    string? SupplierName,
    string? CurrencyCode,
    int? LoteNumber = null)
{
    public static readonly FinanceHistoryAmountResolution Empty = new(null, null, null, null, null);
}

/// <summary>
/// Pure, static, side-effect-free resolver for the amount shown next to a Finance history/audit
/// event (RequestStatusHistory row). RequestStatusHistory has no RequestPoGroupId column, so
/// group attribution is only possible when the event's own Comment text embeds it — this
/// resolver never guesses a group from amount matching or any other heuristic.
///
/// Resolution order:
///  1. New structured "Lote" comment (written by SchedulePayment/MarkAsPaid/
///     ConfirmAdvancePayment going forward): "[Lote #{n} | {supplier} | Moeda: {currency} |
///     Total|Montante: {amount}]" — the "Lote #{n} | " segment is itself optional (omitted by the
///     writer for legacy/batchless groups with no ApprovalBatch), never "Lote #0". Parsed
///     defensively; any malformed segment yields a null field for that segment only.
///  2. Legacy structured group-scoped comment (written by the pre-Lote SchedulePayment): "[Grupo
///     P.O.: {supplier} | Moeda: {currency} | Total: {amount} | GroupId: {id}]" — parsed exactly
///     as before, unchanged, so every already-stored row keeps resolving identically. The GUID is
///     parsed for backward compatibility only and is never rendered as a user-facing identifier.
///  3. Amount-only comment (written by the pre-Lote MarkAsPaid): "Montante: {amount}" — amount
///     parsed, GroupId/LoteNumber left null. Do not infer the group from amount matching: two
///     groups can share an amount, and even when they don't, uniqueness is never proven here.
///  4. Neither pattern present, but the event's ActionTaken itself denotes a genuine payment
///     state transition (not a side note/attachment/adjustment echoing the request's current,
///     unrelated status) — fall back to the request-level snapshot (ApprovedTotalAmount when
///     positive, else the selected quotation's total, else EstimatedTotalAmount).
///  5. Otherwise (DOCUMENTO ADICIONADO, NOTA_FINANCEIRA, FINANCE_RETURN_ADJUSTMENT,
///     PAYMENT_DIVERGENCE_DETECTED, or any other non-transition event) — no amount. Displaying
///     0,00 Kz for a document upload or a comment is more misleading than showing nothing.
/// </summary>
public static class FinanceHistoryAmountResolver
{
    // Tried first: the current writer format. The optional "Lote #n | " lead-in and the
    // Total|Montante label choice cover SchedulePayment/MarkAsPaid/ConfirmAdvancePayment alike.
    // Never matches the legacy "[Grupo P.O.: ... | GroupId: ...]" shape — that format's trailing
    // "| GroupId: ..." segment after the amount prevents the closing "]" from lining up here.
    private static readonly Regex LoteStructuredPrefix = new(
        @"\[(?:Lote #(?<lote>\d+)\s*\|\s*)?(?<supplier>[^|]*)\|\s*Moeda:\s*(?<currency>[^|]*)\|\s*(?:Total|Montante):\s*(?<amount>[^|\]]*)\]",
        RegexOptions.Compiled);

    private static readonly Regex LegacyStructuredGroupPrefix = new(
        @"\[Grupo P\.O\.:\s*(?<supplier>[^|]*)\|\s*Moeda:\s*(?<currency>[^|]*)\|\s*Total:\s*(?<total>[^|]*)\|\s*GroupId:\s*(?<groupId>[^\]]*)\]",
        RegexOptions.Compiled);

    // Alternating digit/separator groups so a sentence-ending period right after the number
    // (e.g. "Montante: 70341.42. ", MarkAsPaid's exact format) is never swallowed into the match.
    private static readonly Regex MontantePattern = new(
        @"Montante:\s*(?<amount>[0-9]+(?:[.,][0-9]+)*)",
        RegexOptions.Compiled);

    // ActionTaken values that represent a genuine payment state transition — the only cases where
    // falling back to the request-level snapshot (tier 4) is justified when the comment itself
    // doesn't parse. Deliberately excludes DOCUMENTO ADICIONADO/NOTA_FINANCEIRA/
    // FINANCE_RETURN_ADJUSTMENT/PAYMENT_DIVERGENCE_DETECTED, whose NewStatus often just echoes the
    // request's current (unrelated) status and must never be mistaken for a real transition.
    private static readonly string[] TransitionActionCodes =
    {
        "PAYMENT_SCHEDULED",
        "ADVANCE_PAYMENT_SCHEDULED",
        "PAYMENT_COMPLETED",
        "ADVANCE_PAYMENT_COMPLETED"
    };

    public static FinanceHistoryAmountResolution Resolve(
        string? comment,
        string? actionTaken,
        decimal? approvedTotalAmount,
        decimal? selectedQuotationTotalAmount,
        decimal estimatedTotalAmount)
    {
        if (!string.IsNullOrEmpty(comment))
        {
            var loteMatch = LoteStructuredPrefix.Match(comment);
            if (loteMatch.Success)
            {
                var supplier = loteMatch.Groups["supplier"].Value.Trim();
                var currency = loteMatch.Groups["currency"].Value.Trim();
                var loteGroup = loteMatch.Groups["lote"];
                return new FinanceHistoryAmountResolution(
                    TryParseAmount(loteMatch.Groups["amount"].Value),
                    null,
                    string.IsNullOrEmpty(supplier) ? null : supplier,
                    string.IsNullOrEmpty(currency) ? null : currency,
                    loteGroup.Success ? TryParseInt(loteGroup.Value) : null);
            }

            var structuredMatch = LegacyStructuredGroupPrefix.Match(comment);
            if (structuredMatch.Success)
            {
                var supplier = structuredMatch.Groups["supplier"].Value.Trim();
                var currency = structuredMatch.Groups["currency"].Value.Trim();
                return new FinanceHistoryAmountResolution(
                    TryParseAmount(structuredMatch.Groups["total"].Value),
                    TryParseGuid(structuredMatch.Groups["groupId"].Value),
                    string.IsNullOrEmpty(supplier) ? null : supplier,
                    string.IsNullOrEmpty(currency) ? null : currency);
            }

            var montanteMatch = MontantePattern.Match(comment);
            if (montanteMatch.Success)
            {
                return new FinanceHistoryAmountResolution(
                    TryParseAmount(montanteMatch.Groups["amount"].Value),
                    null,
                    null,
                    null);
            }
        }

        if (actionTaken != null && TransitionActionCodes.Contains(actionTaken))
        {
            var fallback = (approvedTotalAmount.HasValue && approvedTotalAmount.Value > 0)
                ? approvedTotalAmount.Value
                : selectedQuotationTotalAmount ?? estimatedTotalAmount;
            return new FinanceHistoryAmountResolution(fallback, null, null, null);
        }

        return FinanceHistoryAmountResolution.Empty;
    }

    private static decimal? TryParseAmount(string raw) =>
        decimal.TryParse(
            raw?.Trim(),
            NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    private static Guid? TryParseGuid(string raw) =>
        Guid.TryParse(raw?.Trim(), out var value) ? value : null;

    private static int? TryParseInt(string raw) =>
        int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
