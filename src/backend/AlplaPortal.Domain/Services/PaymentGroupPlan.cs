using System;
using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Domain.Services;

/// <summary>One line item reduced to what grouping needs. Values come from its source document.</summary>
public sealed record PaymentGroupableItem
{
    public Guid LineItemId { get; init; }
    public Guid? PaymentSourceDocumentId { get; init; }
    public int? SupplierId { get; init; }
    public string? SupplierNameSnapshot { get; init; }
    public string? SupplierTaxIdSnapshot { get; init; }
    public string? CurrencyCode { get; init; }
    public int? PlantId { get; init; }
    public string? SourceDocumentType { get; init; }
    public decimal TotalAmount { get; init; }
}

/// <summary>A group that should exist, and the items that belong to it.</summary>
public sealed record PlannedPaymentGroup
{
    public PaymentGroupingKey Key { get; init; }
    public string? SupplierNameSnapshot { get; init; }
    public string? SupplierTaxIdSnapshot { get; init; }
    public decimal TotalAmount { get; init; }
    public IReadOnlyList<Guid> LineItemIds { get; init; } = Array.Empty<Guid>();

    /// <summary>The source documents feeding this group. Usually one; several when they agree.</summary>
    public IReadOnlyList<Guid> SourceDocumentIds { get; init; } = Array.Empty<Guid>();
}

/// <summary>
/// Turns a PAYMENT request's line items into the set of PO groups it needs.
///
/// <para>Replaces the previous behaviour, which created exactly one group per payment request from
/// the request header. With several source documents that is wrong in two directions: two invoices
/// for two plants must not share a group, and two documents that agree on every dimension must not
/// produce two.</para>
///
/// <para><b>One source document does not imply one group.</b> A single invoice whose items span two
/// plants produces two groups — the same consolidation phenomenon as an operation invoice covering
/// several groups, seen from the origin side.</para>
///
/// <para>Pure, so the grouping can be asserted directly rather than inferred from what got written.</para>
/// </summary>
public static class PaymentGroupPlan
{
    public static IReadOnlyList<PlannedPaymentGroup> Build(
        IEnumerable<PaymentGroupableItem> activeItems,
        string? requestPaymentConditionCode)
    {
        return activeItems
            .GroupBy(i => PaymentGroupingKey.From(
                i.SupplierId,
                i.CurrencyCode,
                requestPaymentConditionCode,
                i.PlantId,
                i.SourceDocumentType))
            .Select(g => new PlannedPaymentGroup
            {
                Key = g.Key,
                // Snapshots are taken from the first contributing item; every item in the group
                // shares the supplier by construction, so any of them would give the same answer.
                SupplierNameSnapshot = g.Select(i => i.SupplierNameSnapshot).FirstOrDefault(n => n != null),
                SupplierTaxIdSnapshot = g.Select(i => i.SupplierTaxIdSnapshot).FirstOrDefault(t => t != null),
                TotalAmount = g.Sum(i => i.TotalAmount),
                LineItemIds = g.Select(i => i.LineItemId).ToList(),
                SourceDocumentIds = g
                    .Where(i => i.PaymentSourceDocumentId.HasValue)
                    .Select(i => i.PaymentSourceDocumentId!.Value)
                    .Distinct()
                    .ToList()
            })
            // Deterministic order so two runs produce the same groups in the same sequence, and so
            // concurrent transactions lock groups in a stable order (deadlock avoidance).
            .OrderBy(g => g.Key.SupplierId ?? int.MaxValue)
            .ThenBy(g => g.Key.PlantId ?? int.MaxValue)
            .ThenBy(g => g.Key.SourceDocumentType ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(g => g.Key.CurrencyCode ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }
}
