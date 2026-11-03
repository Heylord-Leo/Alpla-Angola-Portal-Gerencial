using System;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using Proj = AlplaPortal.Domain.Services.BuyerQueueProjectionBuilder;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// The single adapter from a fully-hydrated <see cref="Request"/> aggregate to the pure
/// <see cref="Proj.RequestInput"/> consumed by <see cref="BuyerQueueProjectionBuilder"/>. Extracted to
/// Domain so every surface feeds the projection the IDENTICAL input and can never diverge on
/// coverage/state:
///   Controllers (Buyer queue, Buyer Workspace) + DashboardV2QueryService  →  this factory  →  builder.
/// Previously this mapping lived on <c>BuyerQueueController.BuildRequestInput</c>; the Dashboard service
/// reusing it created a service→controller dependency, so it now lives here (correct layering).
///
/// Requires the same includes as the queue hydration: LineItems(.LineItemStatus),
/// ApprovalBatches.Items.Candidates, PoGroups, Quotations.Items, Attachments and (v2.249.2) QuotationReuseAuthorizations.
/// </summary>
public static class BuyerQueueProjectionInputFactory
{
    public static Proj.RequestInput FromRequest(Request r)
    {
        var nonDeleted = r.LineItems.Where(li => !li.IsDeleted).ToList();
        var poGroups = r.PoGroups.ToList();
        var supersededIds = r.ApprovalBatches
            .Where(b => SupersededBatchPolicy.IsSuperseded(b, nonDeleted, poGroups))
            .Select(b => b.Id)
            .ToHashSet();

        var items = r.LineItems.Select(li => new Proj.ItemInput(
            li.Id, li.IsDeleted, li.QuotationLifecycleStatus, li.LineItemStatus?.Code,
            li.SupplierId.HasValue || !string.IsNullOrEmpty(li.SupplierName))).ToList();

        var batches = r.ApprovalBatches.Select(b => new Proj.BatchInput(
            b.Id, b.BatchNumber, b.Status,
            b.Items.Select(bi => new Proj.BatchItemInput(
                bi.RequestLineItemId, bi.SelectedQuotationItemId,
                bi.Candidates.Select(c => c.QuotationItemId).ToList(),
                bi.ReleasedAtUtc.HasValue)).ToList())).ToList();

        // v2.249.2 — the SAME reuse rule the batch endpoint enforces (QuotationReuseEligibilityPolicy): candidates/winners of
        // a released or cancelled batch item without an active authorization are not selectable → their line stays pending.
        var reuseBlocked = QuotationReuseEligibilityPolicy.ReuseBlockedQuotationItemIds(r);
        var quotationItems = r.Quotations
            .SelectMany(qq => qq.Items)
            .Select(qi => new Proj.QuotationItemInput(qi.Id, qi.MappedRequestLineItemId, qi.ReconciliationStatus, reuseBlocked.Contains(qi.Id)))
            .ToList();

        var hasProformaOrQuotation = r.Attachments.Any(a =>
            (a.AttachmentTypeCode == AttachmentConstants.Types.Proforma
             || a.AttachmentTypeCode == AttachmentConstants.Types.Quotation) && !a.IsDeleted);

        // v2.242.0 — any PO group returned by Finance for correction.
        var correctionGroups = poGroups.Where(g => g.Status == RequestConstants.PoGroupStatuses.WaitingPoCorrection).ToList();
        var hasPoCorrection = correctionGroups.Count > 0;
        // v2.249.2 — stored return kinds (null = legacy) and the first-registration pending flag.
        var poCorrectionReturnKinds = correctionGroups.Select(g => g.LastFinanceReturnKind).ToList();
        var hasPoRegistrationPending = poGroups.Any(g => g.Status == RequestConstants.PoGroupStatuses.WaitingPo);

        return new Proj.RequestInput(
            r.Id, r.RequestNumber ?? string.Empty, r.Title,
            r.RequestType.Code, r.Status.Code, r.IsCancelled,
            r.BuyerId, r.NeedLevel?.Code, r.NeedByDateUtc, r.CreatedAtUtc,
            r.SupplierId.HasValue, hasProformaOrQuotation,
            items, batches, quotationItems, supersededIds,
            hasPoCorrection, poCorrectionReturnKinds, hasPoRegistrationPending);
    }
}
