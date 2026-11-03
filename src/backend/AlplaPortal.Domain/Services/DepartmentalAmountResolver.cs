using System;
using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Facts about ONE request needed to resolve its request-level amount and currency for the departmental payment
/// summary. Group facts cover NON-CANCELLED groups only, except <see cref="CancelledGroupCount"/>.
/// </summary>
public sealed record RequestAmountFacts(
    Guid RequestId,
    decimal EstimatedTotalAmount,
    string? RequestCurrency,
    decimal? ApprovedTotalAmount,
    string? ApprovedCurrencyCode,
    IReadOnlyList<string?> LiveGroupCurrencies,
    decimal LiveGroupSum,
    int PendingGroupCount,
    int CancelledGroupCount,
    int BatchCount,
    int ApprovedBatchCount,
    decimal ApprovedBatchSum,
    int BatchFinalApprovalHistoryCount,
    // Phase 1 P.O. cancellation: items released from APPROVED batches (their value is no longer live).
    int ReleasedBatchItemCount = 0);

/// <summary>Where the resolved request-level amount comes from.</summary>
public enum RequestAmountSource
{
    /// <summary>Positive approved total with a recorded, consistent single currency.</summary>
    RecordedApproved,
    /// <summary>Positive approved total whose currency was NOT recorded (legacy batch approvals); currency derived from
    /// current consistent data under the evidence rules of <see cref="DepartmentalAmountResolver"/>.</summary>
    DerivedApproved,
    /// <summary>Phase 1 P.O. cancellation (D5): a group was cancelled after approval (or an approved batch had items
    /// released), so the recorded total no longer describes what is alive; the amount is the live group sum in the
    /// recorded currency.</summary>
    ActiveApproved,
    /// <summary>No usable positive approved total: the requester's estimate under the estimate comparability rules.</summary>
    EstimateFallback,
    /// <summary>No amount can be stated in a single currency with sufficient evidence; excluded from any ratio.</summary>
    Unresolved
}

public sealed record RequestAmountResolution(RequestAmountSource Source, decimal Amount, string? Currency, string? Reason)
{
    public bool IsResolved => Source != RequestAmountSource.Unresolved;
}

/// <summary>
/// Pure, static request-level amount/currency resolver shared by the departmental payment summary for the current
/// request (numerator) and every denominator candidate — the same rules, applied once per request.
/// Decision table (docs/APPROVAL_NOTIFICATIONS_PHASES.md §G.18):
///  A. ApprovedTotalAmount > 0 and ApprovedCurrencyCode recorded as a single code: every non-cancelled group in that
///     code (vacuous without groups) and the request currency equal or unregistered → RecordedApproved.
///     "MULTIPLE", a group in another/unknown currency or a differing request currency → Unresolved.
///  B. ApprovedTotalAmount > 0 and ApprovedCurrencyCode missing (legacy batch approvals): DerivedApproved ONLY when the
///     request currency is registered; at least one non-cancelled group exists and all carry that same currency; no
///     cancelled and no pending-approval group exists; every approval batch is APPROVED and at least one exists; the
///     BATCH_FINAL_APPROVED history supports every approved batch; the approved batch sum equals the request approved
///     total (±0.01) and the non-cancelled group total equals it (±0.01 per group). Anything else → Unresolved.
///  C. No positive approved total: EstimateFallback when the request currency is registered and every non-cancelled
///     group carries it; otherwise Unresolved.
/// Rounding rule (from source): group TotalAmount = Σ QuotationItem.LineTotal (Round2 per line), batch ApprovedTotalAmount =
/// Σ the same LineTotals, request ApprovedTotalAmount = Σ batch amounts, all persisted as decimal(18,2) — equality is
/// expected; the tolerances only absorb two-decimal storage rounding.
/// </summary>
public static class DepartmentalAmountResolver
{
    public const string MultipleCurrencies = "MULTIPLE";
    public const decimal BatchSumTolerance = 0.01m;
    public const decimal GroupSumTolerancePerGroup = 0.01m;

    public static RequestAmountResolution Resolve(RequestAmountFacts f)
    {
        var approved = f.ApprovedTotalAmount ?? 0m;
        var requestCurrency = Normalize(f.RequestCurrency);
        var groupCurrencies = f.LiveGroupCurrencies;
        var anyBlankGroup = groupCurrencies.Any(c => Normalize(c) == null);
        var distinct = groupCurrencies.Select(Normalize).Where(c => c != null).Distinct(StringComparer.Ordinal).ToList();

        if (approved > 0m)
        {
            var recorded = Normalize(f.ApprovedCurrencyCode);
            if (recorded != null)
            {
                if (string.Equals(recorded, MultipleCurrencies, StringComparison.Ordinal))
                    return Unresolved(approved, "valor aprovado registado em várias moedas (MULTIPLE)");
                if (anyBlankGroup)
                    return Unresolved(approved, "valor aprovado registado, mas um grupo P.O. ativo não tem moeda registada");
                if (distinct.Count > 1 || (distinct.Count == 1 && !string.Equals(distinct[0], recorded, StringComparison.Ordinal)))
                    return Unresolved(approved, "valor aprovado registado, mas os grupos P.O. ativos estão noutra moeda");
                if (requestCurrency != null && !string.Equals(requestCurrency, recorded, StringComparison.Ordinal))
                    return Unresolved(approved, "valor aprovado registado numa moeda diferente da moeda do pedido");
                // D5: a cancelled group or a released approved item means the recorded total counts value that is no longer
                // alive (and, after a restart, would count the item twice). Report what is live, in the recorded currency.
                if (f.CancelledGroupCount > 0 || f.ReleasedBatchItemCount > 0)
                {
                    if (groupCurrencies.Count == 0)
                        return Unresolved(approved, "grupos P.O. cancelados e nenhum grupo ativo — valor aprovado histórico apenas");
                    return new RequestAmountResolution(RequestAmountSource.ActiveApproved, f.LiveGroupSum, recorded, null);
                }
                return new RequestAmountResolution(RequestAmountSource.RecordedApproved, approved, recorded, null);
            }

            const string prefix = "valor aprovado sem moeda registada";
            if (requestCurrency == null) return Unresolved(approved, $"{prefix} e moeda do pedido não registada");
            if (groupCurrencies.Count == 0) return Unresolved(approved, $"{prefix} e sem grupos P.O. ativos");
            if (anyBlankGroup || distinct.Count != 1 || !string.Equals(distinct[0], requestCurrency, StringComparison.Ordinal))
                return Unresolved(approved, $"{prefix}; grupos P.O. em moeda diferente, mista ou não registada");
            if (f.CancelledGroupCount > 0) return Unresolved(approved, $"{prefix}; existem grupos P.O. cancelados");
            if (f.PendingGroupCount > 0) return Unresolved(approved, $"{prefix}; existem grupos P.O. pendentes de aprovação");
            if (f.ApprovedBatchCount == 0 || f.BatchCount != f.ApprovedBatchCount)
                return Unresolved(approved, $"{prefix}; lotes de aprovação em falta ou não aprovados");
            if (f.BatchFinalApprovalHistoryCount < f.ApprovedBatchCount)
                return Unresolved(approved, $"{prefix}; histórico de aprovação final incompleto");
            if (Math.Abs(f.ApprovedBatchSum - approved) > BatchSumTolerance)
                return Unresolved(approved, $"{prefix}; soma dos lotes aprovados difere do total aprovado");
            if (Math.Abs(f.LiveGroupSum - approved) > GroupSumTolerancePerGroup * groupCurrencies.Count)
                return Unresolved(approved, $"{prefix}; total dos grupos P.O. difere do total aprovado");
            return new RequestAmountResolution(RequestAmountSource.DerivedApproved, approved, requestCurrency, null);
        }

        if (requestCurrency == null) return Unresolved(f.EstimatedTotalAmount, "estimativa: moeda do pedido não registada");
        if (anyBlankGroup) return Unresolved(f.EstimatedTotalAmount, "estimativa: um grupo P.O. ativo não tem moeda registada");
        if (distinct.Any(c => !string.Equals(c, requestCurrency, StringComparison.Ordinal)))
            return Unresolved(f.EstimatedTotalAmount, "estimativa: grupos P.O. ativos noutra moeda");
        return new RequestAmountResolution(RequestAmountSource.EstimateFallback, f.EstimatedTotalAmount, requestCurrency, null);
    }

    private static RequestAmountResolution Unresolved(decimal amount, string reason) =>
        new(RequestAmountSource.Unresolved, amount, null, reason);

    /// <summary>Null for null/whitespace; otherwise the value unchanged (no trimming: comparisons are ordinal).</summary>
    public static string? Normalize(string? code) => string.IsNullOrWhiteSpace(code) ? null : code;
}

/// <summary>
/// Pure helper for the batch final-approval path: the currency of the amounts actually summed into a batch and the
/// request-level cumulative currency across batches. Never infers the currency of batches approved before the
/// currency was recorded, never lets an unknown become known, and never lets a proven "MULTIPLE" collapse.
/// </summary>
public static class ApprovalCurrencyResolver
{
    public const string MultipleCurrencies = DepartmentalAmountResolver.MultipleCurrencies;

    /// <summary>
    /// Currency of a batch from its summed contributions (code, amount). Zero amounts carry no evidence and are ignored.
    /// Any NON-ZERO contribution without a known code makes the batch currency unknown (null): a sum that includes an
    /// amount of unknown currency is never labelled with the known code of the other amounts. Otherwise a single code,
    /// or "MULTIPLE" when the codes differ; null when there is no non-zero contribution.
    /// </summary>
    public static string? ResolveBatchCurrency(IEnumerable<(string? Code, decimal Amount)> summedContributions)
    {
        var contributing = summedContributions.Where(c => c.Amount != 0m).ToList();
        if (contributing.Count == 0) return null;
        if (contributing.Any(c => DepartmentalAmountResolver.Normalize(c.Code) == null)) return null;
        var distinct = contributing.Select(c => DepartmentalAmountResolver.Normalize(c.Code)!).Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count == 1 ? distinct[0] : MultipleCurrencies;
    }

    /// <summary>
    /// Request-level currency after this batch.
    ///  - No prior evidence (no previously approved batch and no recorded request currency): the batch currency as is
    ///    (known code, "MULTIPLE", or null = unknown).
    ///  - Prior approved batches but no recorded request currency (legacy approvals, or an earlier batch of unknown
    ///    currency): null. The prior currency is never inferred and a later known batch never makes the cumulative known.
    ///  - Recorded "MULTIPLE": "MULTIPLE" whatever follows (several currencies are already proven).
    ///  - Recorded single code: same code unchanged; different known code or "MULTIPLE" gives "MULTIPLE"; unknown batch
    ///    currency gives null (the cumulative is no longer provably single, and not provably multiple either).
    /// </summary>
    public static string? CombineCumulative(string? recordedRequestCurrency, bool hadPreviouslyApprovedBatches, string? batchCurrency)
    {
        var recorded = DepartmentalAmountResolver.Normalize(recordedRequestCurrency);
        var batch = DepartmentalAmountResolver.Normalize(batchCurrency);
        if (!hadPreviouslyApprovedBatches && recorded == null) return batch;
        if (recorded == null) return null;
        if (string.Equals(recorded, MultipleCurrencies, StringComparison.Ordinal)) return MultipleCurrencies;
        if (batch == null) return null;
        if (string.Equals(batch, MultipleCurrencies, StringComparison.Ordinal)) return MultipleCurrencies;
        return string.Equals(recorded, batch, StringComparison.Ordinal) ? recorded : MultipleCurrencies;
    }
}
