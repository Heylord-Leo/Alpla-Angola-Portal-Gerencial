using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// One PO group reduced to the facts the obligation projection needs. A snapshot, never an EF
/// entity: the projector must stay pure, and an entity would drag change tracking, lazy loading
/// and mutability into logic whose whole value is that it cannot touch anything.
/// </summary>
public sealed record OperationInvoiceObligationGroupSnapshot
{
    public Guid GroupId { get; init; }

    public int? SupplierId { get; init; }
    public string? SupplierNameSnapshot { get; init; }
    public string? CurrencyCode { get; init; }
    public string? PaymentConditionCode { get; init; }
    public int? PlantId { get; init; }

    /// <summary>Document IDENTITY as persisted. Obligations are recomputed from it, never read.</summary>
    public string? SourceDocumentType { get; init; }

    /// <summary>Null when never captured — legacy/pre-flag groups. Never invented.</summary>
    public decimal? ExpectedTotal { get; init; }
    public string? ExpectedCurrency { get; init; }

    /// <summary>The cached column on the group, reported beside the recomputed status.</summary>
    public string PersistedStatus { get; init; } = RequestConstants.OperationInvoiceStatuses.Unclassified;

    public string? PurchaseOrderNumber { get; init; }

    /// <summary>Live (non-voided) allocations pointing at this group.</summary>
    public IReadOnlyList<AllocationCoverage> Allocations { get; init; } = Array.Empty<AllocationCoverage>();

    /// <summary>Status of the single active short-close slot (PROPOSED/APPROVED), null when none.</summary>
    public string? ActiveShortCloseStatus { get; init; }

    /// <summary>The payment source documents that fed this group. Traceability, never arithmetic.</summary>
    public IReadOnlyList<Guid> SourceDocumentIds { get; init; } = Array.Empty<Guid>();

    /// <summary>The line items that landed in this group.</summary>
    public IReadOnlyList<Guid> LineItemIds { get; init; } = Array.Empty<Guid>();
}

/// <summary>
/// Machine-readable explanation codes for an obligation's state. One code per obligation, chosen
/// by the projector alone — a UI or API consumer branches on these, never on the pt-PT text.
/// </summary>
public static class OperationInvoiceObligationReasons
{
    public const string ClassificationPending = "CLASSIFICATION_PENDING";
    public const string SourceAlreadyDocumentsOperation = "SOURCE_ALREADY_DOCUMENTS_OPERATION";
    public const string DivergenceRequiresFinanceDecision = "DIVERGENCE_REQUIRES_FINANCE_DECISION";
    public const string AwaitingFinanceValidation = "AWAITING_FINANCE_VALIDATION";
    public const string SatisfiedByShortClose = "SATISFIED_BY_SHORT_CLOSE";
    public const string SatisfiedByCoverage = "SATISFIED_BY_COVERAGE";
    public const string PartiallyCovered = "PARTIALLY_COVERED";
    public const string AwaitingOperationInvoice = "AWAITING_OPERATION_INVOICE";

    /// <summary>
    /// The obligation exists but its finish line was never recorded — legacy/pre-flag groups.
    /// Reported instead of inventing a total (rule: no historical fact invention).
    /// </summary>
    public const string ExpectedTotalUnknown = "EXPECTED_TOTAL_UNKNOWN";
}

/// <summary>
/// The operation-invoice obligation of one PO group, fully explained: what created it, what it
/// requires, what has arrived, what remains, and whether the cached status agrees with the
/// recomputed one.
/// </summary>
public sealed record OperationInvoiceObligation
{
    public Guid GroupId { get; init; }

    /// <summary>Canonical document identity (legacy FINAL_INVOICE already mapped to INVOICE).</summary>
    public string? SourceDocumentType { get; init; }

    /// <summary>Recomputed from the identity via <see cref="DocumentObligationResolver"/>.</summary>
    public bool RequiresOperationInvoice { get; init; }

    /// <summary>Null when the expected total was never captured. Never coerced to zero.</summary>
    public decimal? ExpectedAmount { get; init; }
    public string? ExpectedCurrency { get; init; }

    public decimal ValidatedCoveredAmount { get; init; }
    public decimal PendingCoveredAmount { get; init; }

    /// <summary>Null when <see cref="ExpectedAmount"/> is unknown — an unknowable remainder is not zero.</summary>
    public decimal? RemainingAmount { get; init; }

    public decimal AppliedTolerance { get; init; }

    /// <summary>Recomputed by <see cref="OperationInvoiceAggregateDeriver"/> from authoritative inputs.</summary>
    public string DerivedStatus { get; init; } = RequestConstants.OperationInvoiceStatuses.Unclassified;

    /// <summary>The cached column as it stands in the database.</summary>
    public string PersistedStatus { get; init; } = RequestConstants.OperationInvoiceStatuses.Unclassified;

    /// <summary>The cached status disagrees with the recomputed one. Diagnostic only — never repaired here.</summary>
    public bool StatusDrift { get; init; }

    public bool ClosedShort { get; init; }

    public string? PurchaseOrderNumber { get; init; }

    public IReadOnlyList<Guid> SourceDocumentIds { get; init; } = Array.Empty<Guid>();
    public int LineItemCount { get; init; }

    /// <summary>One of <see cref="OperationInvoiceObligationReasons"/>.</summary>
    public string ReasonCode { get; init; } = OperationInvoiceObligationReasons.ClassificationPending;

    /// <summary>User-facing explanation, pt-PT — same convention as <see cref="PostPaymentPendingReason"/>.</summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>Per-currency sums for the rollup. Amounts are never summed across currencies.</summary>
public sealed record OperationInvoiceObligationCurrencyTotals
{
    public string CurrencyCode { get; init; } = string.Empty;
    public decimal ExpectedTotal { get; init; }
    public decimal ValidatedTotal { get; init; }
    public decimal PendingValidationTotal { get; init; }
    public decimal RemainingTotal { get; init; }

    /// <summary>Groups whose expected total is unrecorded — their amounts are absent from the sums above.</summary>
    public int GroupsWithUnknownExpectedTotal { get; init; }
}

/// <summary>Request-level view: counts by state family plus honest per-currency totals.</summary>
public sealed record OperationInvoiceObligationRollup
{
    public int TotalGroups { get; init; }
    public int RequiringOperationInvoiceCount { get; init; }

    /// <summary>Groups whose derived status blocks completion. NOT_REQUIRED and SATISFIED never count.</summary>
    public int PendingActionCount { get; init; }

    public int SatisfiedCount { get; init; }
    public int NotRequiredCount { get; init; }
    public int UnclassifiedCount { get; init; }

    public int DriftCount { get; init; }
    public bool HasStatusDrift => DriftCount > 0;

    public IReadOnlyList<OperationInvoiceObligationCurrencyTotals> CurrencyTotals { get; init; } =
        Array.Empty<OperationInvoiceObligationCurrencyTotals>();
}

/// <summary>The full answer for one request: one obligation per group, plus the rollup.</summary>
public sealed record OperationInvoiceObligationProjection
{
    public IReadOnlyList<OperationInvoiceObligation> Obligations { get; init; } =
        Array.Empty<OperationInvoiceObligation>();

    public OperationInvoiceObligationRollup Rollup { get; init; } = new();
}

/// <summary>
/// Composes the existing pure pieces — <see cref="DocumentObligationResolver"/> for "is an
/// operation invoice owed?" and <see cref="OperationInvoiceAggregateDeriver"/> for "how covered is
/// it?" — into one explained, request-level answer. It introduces no rule of its own beyond the
/// request rollup and the drift comparison.
///
/// <para>Pure and read-only by construction: no database, no clock, no mutation of its inputs, and
/// no repair of what it finds. When the cached <c>OperationInvoiceStatus</c> disagrees with the
/// recomputed value the disagreement is REPORTED as <see cref="OperationInvoiceObligation.StatusDrift"/> —
/// fixing the column is a write-path responsibility, in the same transaction as whatever caused
/// the change, never a side effect of reading.</para>
/// </summary>
public static class OperationInvoiceObligationProjector
{
    /// <summary>Currency bucket for groups that predate expected-total capture and carry no code.</summary>
    private const string UnknownCurrency = "UNKNOWN";

    public static OperationInvoiceObligationProjection Project(
        IEnumerable<OperationInvoiceObligationGroupSnapshot> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        // Input order is preserved: callers (PaymentGroupPlan, group queries) already order
        // deterministically, and re-sorting here would hide their contract.
        var obligations = groups.Select(ProjectGroup).ToList();

        return new OperationInvoiceObligationProjection
        {
            Obligations = obligations,
            Rollup = BuildRollup(obligations)
        };
    }

    private static OperationInvoiceObligation ProjectGroup(OperationInvoiceObligationGroupSnapshot group)
    {
        var normalizedType = RequestConstants.SourceDocumentTypes.Normalize(group.SourceDocumentType);
        var isClassified = RequestConstants.SourceDocumentTypes.IsValid(normalizedType);

        // RequiresOperationInvoice is invariant across usage contexts (the context only affects
        // blocking and review flags), so the origin context does not need to travel in the
        // snapshot. PaymentRequest is passed as the canonical origin.
        var resolved = DocumentObligationResolver.Resolve(
            normalizedType, DocumentUsageContext.PaymentRequest);

        var hasApprovedShortClose = string.Equals(
            group.ActiveShortCloseStatus,
            RequestConstants.ShortCloseStatuses.Approved,
            StringComparison.OrdinalIgnoreCase);

        var coverage = OperationInvoiceAggregateDeriver.Derive(
            isClassified,
            resolved.RequiresOperationInvoice,
            group.ExpectedTotal,
            group.Allocations,
            hasApprovedShortClose);

        var (reasonCode, explanation) = Explain(coverage, group.ExpectedTotal, normalizedType);

        return new OperationInvoiceObligation
        {
            GroupId = group.GroupId,
            SourceDocumentType = isClassified ? normalizedType : null,
            RequiresOperationInvoice = resolved.RequiresOperationInvoice,
            ExpectedAmount = group.ExpectedTotal,
            ExpectedCurrency = group.ExpectedCurrency ?? group.CurrencyCode,
            ValidatedCoveredAmount = coverage.ValidatedTotal,
            PendingCoveredAmount = coverage.PendingValidationTotal,
            RemainingAmount = group.ExpectedTotal.HasValue ? coverage.RemainingAmount : null,
            AppliedTolerance = coverage.AppliedTolerance,
            DerivedStatus = coverage.Status,
            PersistedStatus = group.PersistedStatus,
            StatusDrift = !string.Equals(
                group.PersistedStatus, coverage.Status, StringComparison.OrdinalIgnoreCase),
            ClosedShort = coverage.ClosedShort,
            PurchaseOrderNumber = group.PurchaseOrderNumber,
            SourceDocumentIds = group.SourceDocumentIds,
            LineItemCount = group.LineItemIds.Count,
            ReasonCode = reasonCode,
            Explanation = explanation
        };
    }

    private static (string Code, string Explanation) Explain(
        OperationInvoiceCoverage coverage, decimal? expectedTotal, string? normalizedType)
    {
        if (Is(coverage.Status, RequestConstants.OperationInvoiceStatuses.Unclassified))
        {
            return (OperationInvoiceObligationReasons.ClassificationPending,
                "Documento por classificar: identifique o tipo de documento de origem para derivar a obrigação.");
        }

        if (Is(coverage.Status, RequestConstants.OperationInvoiceStatuses.NotRequired))
        {
            return (OperationInvoiceObligationReasons.SourceAlreadyDocumentsOperation,
                $"{RequestConstants.SourceDocumentTypes.DisplayName(normalizedType)} já documenta a operação — não é exigida fatura final adicional.");
        }

        if (Is(coverage.Status, RequestConstants.OperationInvoiceStatuses.DivergenceDetected))
        {
            return (OperationInvoiceObligationReasons.DivergenceRequiresFinanceDecision,
                "Divergência detetada: requer decisão explícita do Financeiro antes de prosseguir.");
        }

        if (Is(coverage.Status, RequestConstants.OperationInvoiceStatuses.PendingValidation))
        {
            return (OperationInvoiceObligationReasons.AwaitingFinanceValidation,
                "Fatura final recebida: aguarda validação do Financeiro.");
        }

        if (Is(coverage.Status, RequestConstants.OperationInvoiceStatuses.Satisfied))
        {
            return coverage.ClosedShort
                ? (OperationInvoiceObligationReasons.SatisfiedByShortClose,
                    "Obrigação encerrada abaixo do total esperado por decisão aprovada do Financeiro.")
                : (OperationInvoiceObligationReasons.SatisfiedByCoverage,
                    "Cobertura validada atingiu o total esperado, dentro da tolerância.");
        }

        // PENDING_UPLOAD and PARTIALLY_INVOICED share the honesty rule: an obligation whose
        // finish line was never recorded is reported as unknown, never as "zero remaining".
        if (expectedTotal == null)
        {
            return (OperationInvoiceObligationReasons.ExpectedTotalUnknown,
                "Fatura final exigida, mas o total esperado não foi registado neste grupo (anterior à ativação da funcionalidade).");
        }

        if (Is(coverage.Status, RequestConstants.OperationInvoiceStatuses.PartiallyInvoiced))
        {
            return (OperationInvoiceObligationReasons.PartiallyCovered,
                "Cobertura parcial: o total validado ainda não atinge o total esperado.");
        }

        return (OperationInvoiceObligationReasons.AwaitingOperationInvoice,
            "Fatura final em falta: nenhuma cobertura validada até ao momento.");
    }

    private static OperationInvoiceObligationRollup BuildRollup(
        IReadOnlyList<OperationInvoiceObligation> obligations)
    {
        var currencyTotals = obligations
            .Where(o => o.RequiresOperationInvoice)
            .GroupBy(o => string.IsNullOrWhiteSpace(o.ExpectedCurrency)
                ? UnknownCurrency
                : o.ExpectedCurrency!.Trim().ToUpperInvariant())
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new OperationInvoiceObligationCurrencyTotals
            {
                CurrencyCode = g.Key,
                ExpectedTotal = g.Sum(o => o.ExpectedAmount ?? 0m),
                ValidatedTotal = g.Sum(o => o.ValidatedCoveredAmount),
                PendingValidationTotal = g.Sum(o => o.PendingCoveredAmount),
                RemainingTotal = g.Sum(o => o.RemainingAmount ?? 0m),
                GroupsWithUnknownExpectedTotal = g.Count(o => o.ExpectedAmount == null)
            })
            .ToList();

        return new OperationInvoiceObligationRollup
        {
            TotalGroups = obligations.Count,
            RequiringOperationInvoiceCount = obligations.Count(o => o.RequiresOperationInvoice),
            PendingActionCount = obligations.Count(
                o => RequestConstants.OperationInvoiceStatuses.IsBlocking(o.DerivedStatus)),
            SatisfiedCount = obligations.Count(
                o => Is(o.DerivedStatus, RequestConstants.OperationInvoiceStatuses.Satisfied)),
            NotRequiredCount = obligations.Count(
                o => Is(o.DerivedStatus, RequestConstants.OperationInvoiceStatuses.NotRequired)),
            UnclassifiedCount = obligations.Count(
                o => Is(o.DerivedStatus, RequestConstants.OperationInvoiceStatuses.Unclassified)),
            DriftCount = obligations.Count(o => o.StatusDrift),
            CurrencyTotals = currencyTotals
        };
    }

    private static bool Is(string? status, string expected) =>
        string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);
}
