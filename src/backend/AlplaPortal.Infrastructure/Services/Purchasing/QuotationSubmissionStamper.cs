using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

/// <summary>
/// Durable "ever submitted for approval" marker (docs/BUYER_WORKSPACE_QUOTATION_ACTIONS.md §7).
///
/// Runs INSIDE <see cref="ApplicationDbContext.SaveChangesAsync(bool, CancellationToken)"/> (and the sync
/// twin), before the write is sent, so the marker is persisted in the same transaction as the first
/// submission evidence — every submission path is covered structurally (batch creation, rework
/// recomposition, extra-item include, approver winner stamp, legacy line-level award), not by remembering
/// to call a helper.
///
/// Submission evidence detected in the change tracker (ONE snapshot of the tracked entries per save — one
/// DetectChanges — and no query at all when nothing relevant is pending):
///  - Added <see cref="ApprovalBatchItemCandidate"/> (candidate model: CreateBatch, UpdateBatch rework, extra-item include);
///  - Added/modified <see cref="ApprovalBatchItem"/> with a non-null <c>SelectedQuotationItemId</c> (legacy buyer-selected winner, approver winner stamp);
///  - Added/modified <see cref="RequestLineItem"/> with a non-null <c>SelectedQuotationItemId</c> (legacy request-level award).
///
/// Rules: set only when still null (first submission wins, repeated submissions never move it); never
/// overwritten or cleared — a tracked quotation whose marker/pointer/source was nulled or changed in memory has
/// the persisted values restored before save (raw SQL is the only way around this, and no product code writes
/// Quotations that way). Quotations are updated through their tracked instance when one exists (CreateBatch and
/// UpdateBatch load them with the quotation items), so the UPDATE runs under the quotation's RowVersion: a Buyer
/// edit committed between the batch's read and its save makes the submission fail with
/// DbUpdateConcurrencyException instead of snapshotting stale values. Never calls SaveChanges itself.
/// </summary>
public static class QuotationSubmissionStamper
{
    private sealed class Pending
    {
        public readonly Dictionary<Guid, Guid?> QuotationBatch = new();      // quotationId → batch id (first seen)
        public readonly Dictionary<Guid, Guid?> ItemBatch = new();           // quotationItemId → batch id (first seen)
        public bool Any => QuotationBatch.Count > 0 || ItemBatch.Count > 0;
    }

    public static void Stamp(ApplicationDbContext ctx, DateTime nowUtc)
    {
        var entries = ctx.ChangeTracker.Entries().ToList();   // single DetectChanges
        RestoreClearedMarkers(entries);
        var pending = Detect(entries);
        if (!pending.Any) return;
        var unresolvedItems = pending.ItemBatch.Keys.Where(id => !TryResolveTracked(ctx, id, out _)).ToList();
        var lookup = unresolvedItems.Count == 0
            ? new Dictionary<Guid, Guid>()
            : ctx.QuotationItems.AsNoTracking().Where(qi => unresolvedItems.Contains(qi.Id)).Select(qi => new { qi.Id, qi.QuotationId }).ToList().ToDictionary(x => x.Id, x => x.QuotationId);
        var targets = MergeTargets(ctx, pending, lookup);
        var missing = targets.Keys.Where(id => ctx.Quotations.Local.All(q => q.Id != id)).ToList();
        if (missing.Count > 0) ctx.Quotations.AsTracking().Where(q => missing.Contains(q.Id)).ToList();   // tracked load, keyed lookup
        Apply(ctx, targets, nowUtc);
    }

    public static async Task StampAsync(ApplicationDbContext ctx, DateTime nowUtc, CancellationToken ct)
    {
        var entries = ctx.ChangeTracker.Entries().ToList();   // single DetectChanges
        RestoreClearedMarkers(entries);
        var pending = Detect(entries);
        if (!pending.Any) return;
        var unresolvedItems = pending.ItemBatch.Keys.Where(id => !TryResolveTracked(ctx, id, out _)).ToList();
        var lookup = unresolvedItems.Count == 0
            ? new Dictionary<Guid, Guid>()
            : (await ctx.QuotationItems.AsNoTracking().Where(qi => unresolvedItems.Contains(qi.Id)).Select(qi => new { qi.Id, qi.QuotationId }).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.QuotationId);
        var targets = MergeTargets(ctx, pending, lookup);
        var missing = targets.Keys.Where(id => ctx.Quotations.Local.All(q => q.Id != id)).ToList();
        if (missing.Count > 0) await ctx.Quotations.AsTracking().Where(q => missing.Contains(q.Id)).ToListAsync(ct);   // tracked load, keyed lookup
        Apply(ctx, targets, nowUtc);
    }

    private static Pending Detect(List<EntityEntry> entries)
    {
        var p = new Pending();
        var batchItems = entries.Where(e => e.Entity is ApprovalBatchItem).ToList();
        foreach (var e in entries)
        {
            switch (e.Entity)
            {
                case ApprovalBatchItemCandidate c when e.State == EntityState.Added:
                    var batchId = c.ApprovalBatchItem?.ApprovalBatchId
                                  ?? (batchItems.FirstOrDefault(x => ((ApprovalBatchItem)x.Entity).Id == c.ApprovalBatchItemId)?.Entity as ApprovalBatchItem)?.ApprovalBatchId;
                    if (!p.QuotationBatch.ContainsKey(c.QuotationId)) p.QuotationBatch[c.QuotationId] = batchId;   // untracked parent: batch unknown, marker still set
                    break;
                case ApprovalBatchItem bi when bi.SelectedQuotationItemId is Guid qi:
                    if (IsNewValue(e, nameof(ApprovalBatchItem.SelectedQuotationItemId)) && !p.ItemBatch.ContainsKey(qi)) p.ItemBatch[qi] = bi.ApprovalBatchId;
                    break;
                case RequestLineItem li when li.SelectedQuotationItemId is Guid qi:
                    if (IsNewValue(e, nameof(RequestLineItem.SelectedQuotationItemId)) && !p.ItemBatch.ContainsKey(qi)) p.ItemBatch[qi] = null;   // legacy award: no batch identity
                    break;
            }
        }
        return p;
    }

    private static bool IsNewValue(EntityEntry e, string property)
        => e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(property).IsModified);

    private static bool TryResolveTracked(ApplicationDbContext ctx, Guid quotationItemId, out Guid quotationId)
    {
        var tracked = ctx.QuotationItems.Local.FirstOrDefault(qi => qi.Id == quotationItemId);
        quotationId = tracked?.QuotationId ?? Guid.Empty;
        return tracked != null;
    }

    private static Dictionary<Guid, Guid?> MergeTargets(ApplicationDbContext ctx, Pending p, Dictionary<Guid, Guid> lookup)
    {
        var targets = new Dictionary<Guid, Guid?>(p.QuotationBatch);
        foreach (var (itemId, batchId) in p.ItemBatch)
        {
            Guid quotationId;
            if (!TryResolveTracked(ctx, itemId, out quotationId) && !lookup.TryGetValue(itemId, out quotationId)) continue;
            if (!targets.TryGetValue(quotationId, out var existing) || existing == null) targets[quotationId] = batchId ?? existing;
        }
        return targets;
    }

    private static void Apply(ApplicationDbContext ctx, Dictionary<Guid, Guid?> targets, DateTime nowUtc)
    {
        foreach (var q in ctx.Quotations.Local)
        {
            if (!targets.TryGetValue(q.Id, out var batchId)) continue;
            var entry = ctx.Entry(q);
            if (entry.State == EntityState.Deleted || entry.State == EntityState.Detached) continue;
            if (q.FirstSubmittedForApprovalAtUtc.HasValue) continue;   // first submission wins — never moved
            q.FirstSubmittedForApprovalAtUtc = nowUtc;
            q.FirstSubmittedBatchId = batchId;
            q.FirstSubmittedSource = QuotationSubmissionSources.Submission;
        }
    }

    /// <summary>Never cleared or replaced: a tracked quotation whose persisted marker, pointer or source was nulled/changed in memory gets the persisted values back.</summary>
    private static void RestoreClearedMarkers(List<EntityEntry> entries)
    {
        foreach (var e in entries)
        {
            if (e.Entity is not Quotation q || e.State != EntityState.Modified) continue;
            var originalAt = (DateTime?)e.Property(nameof(Quotation.FirstSubmittedForApprovalAtUtc)).OriginalValue;
            if (originalAt == null) continue;   // never marked before this save: nothing to protect (the stamper is the only legitimate first writer)
            if (q.FirstSubmittedForApprovalAtUtc != originalAt) q.FirstSubmittedForApprovalAtUtc = originalAt;
            var originalBatch = (Guid?)e.Property(nameof(Quotation.FirstSubmittedBatchId)).OriginalValue;
            if (q.FirstSubmittedBatchId != originalBatch) q.FirstSubmittedBatchId = originalBatch;
            var originalSource = (string?)e.Property(nameof(Quotation.FirstSubmittedSource)).OriginalValue;
            if (q.FirstSubmittedSource != originalSource) q.FirstSubmittedSource = originalSource;
        }
    }
}
