using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>One allocation reduced to what the aggregate actually needs to know about it.</summary>
public readonly record struct AllocationCoverage(string InvoiceStatus, decimal AllocatedGrossAmount);

/// <summary>The four numbers and the state, for one PO group.</summary>
public sealed record OperationInvoiceCoverage
{
    public string Status { get; init; } = RequestConstants.OperationInvoiceStatuses.Unclassified;
    public decimal ExpectedTotal { get; init; }
    public decimal ValidatedTotal { get; init; }
    public decimal PendingValidationTotal { get; init; }
    public decimal RemainingAmount { get; init; }
    public decimal AppliedTolerance { get; init; }

    /// <summary>True when the obligation was closed below its expected total by Finance.</summary>
    public bool ClosedShort { get; init; }
}

/// <summary>
/// Turns a group's allocations into its operation-invoice state.
///
/// <para>Pure by design: the same allocation set always yields the same answer, with no database
/// access. That is what makes it safe to recompute after a concurrency conflict — reload, recompute,
/// save — and what lets the rules be tested directly instead of inferred from what got written.</para>
///
/// <para>The result is cached on <c>RequestPoGroup.OperationInvoiceStatus</c>, written by this
/// function only and always inside the same transaction as the change that caused it. The four
/// totals are never stored: they are one <c>SUM</c> away, and storing them would create a second
/// source of truth for a number that already has one.</para>
/// </summary>
public static class OperationInvoiceAggregateDeriver
{
    /// <summary>
    /// Precedence, in order:
    /// <list type="number">
    /// <item>unclassified — nothing can be decided about a document nobody has identified;</item>
    /// <item>not required — the source document already documents the operation;</item>
    /// <item>divergence — the only state needing an explicit human decision before anything moves;</item>
    /// <item>pending validation — work sitting with Finance, who can act without a third party;</item>
    /// <item>short-closed — Finance accepted less than the expected total, with a reason;</item>
    /// <item>covered — cumulative validated coverage reached the expected total within tolerance;</item>
    /// <item>partially invoiced — real coverage, legitimately short of the total;</item>
    /// <item>pending upload — nothing counted yet, including after a rejection.</item>
    /// </list>
    /// <para>Unresolved rejections collapse into the last two states on purpose: the aggregate says
    /// WHERE THE WORK IS, and the per-allocation list — always visible in the UI — says why. No
    /// information is lost by the collapse.</para>
    /// </summary>
    public static OperationInvoiceCoverage Derive(
        bool isClassified,
        bool requiresOperationInvoice,
        decimal? expectedTotal,
        IEnumerable<AllocationCoverage> allocations,
        bool hasApprovedShortClose = false)
    {
        var expected = expectedTotal ?? 0m;
        var tolerance = OperationInvoiceTolerance.For(expected);

        var list = allocations as IReadOnlyCollection<AllocationCoverage> ?? allocations.ToList();

        var validated = list
            .Where(a => RequestConstants.OperationInvoiceDocumentStatuses.CountsTowardCoverage(a.InvoiceStatus))
            .Sum(a => a.AllocatedGrossAmount);

        var pending = list
            .Where(a => RequestConstants.OperationInvoiceDocumentStatuses.IsAwaitingDecision(a.InvoiceStatus))
            .Sum(a => a.AllocatedGrossAmount);

        var baseResult = new OperationInvoiceCoverage
        {
            ExpectedTotal = expected,
            ValidatedTotal = validated,
            PendingValidationTotal = pending,
            RemainingAmount = Math.Max(0m, expected - validated),
            AppliedTolerance = tolerance,
            ClosedShort = hasApprovedShortClose
        };

        if (!isClassified)
            return baseResult with { Status = RequestConstants.OperationInvoiceStatuses.Unclassified };

        if (!requiresOperationInvoice)
            return baseResult with { Status = RequestConstants.OperationInvoiceStatuses.NotRequired };

        if (list.Any(a => string.Equals(a.InvoiceStatus,
                RequestConstants.OperationInvoiceDocumentStatuses.DivergenceDetected,
                StringComparison.OrdinalIgnoreCase)))
        {
            return baseResult with { Status = RequestConstants.OperationInvoiceStatuses.DivergenceDetected };
        }

        if (list.Any(a => RequestConstants.OperationInvoiceDocumentStatuses.IsAwaitingDecision(a.InvoiceStatus)))
            return baseResult with { Status = RequestConstants.OperationInvoiceStatuses.PendingValidation };

        if (hasApprovedShortClose)
            return baseResult with { Status = RequestConstants.OperationInvoiceStatuses.Satisfied };

        // Coverage is judged against the expected total less tolerance, so rounding residue from a
        // consolidated split never leaves a group one cêntimo short of satisfied.
        if (expected > 0m && validated >= expected - tolerance)
            return baseResult with { Status = RequestConstants.OperationInvoiceStatuses.Satisfied };

        if (validated > 0m)
            return baseResult with { Status = RequestConstants.OperationInvoiceStatuses.PartiallyInvoiced };

        return baseResult with { Status = RequestConstants.OperationInvoiceStatuses.PendingUpload };
    }
}

/// <summary>
/// The single call site for operation-invoice tolerance.
///
/// <para>It delegates to <see cref="RequestConstants.FinancialIntegrity.CalculateTolerance"/> — the
/// mechanism the Portal already uses for quotation reconciliation — and adds nothing. The point of
/// the indirection is that a Finance-specific tolerance can later be introduced by changing this
/// function alone, with no change to the state model and no hunt for hard-coded numbers.</para>
/// </summary>
public static class OperationInvoiceTolerance
{
    public static decimal For(decimal amount) =>
        RequestConstants.FinancialIntegrity.CalculateTolerance(amount);
}
