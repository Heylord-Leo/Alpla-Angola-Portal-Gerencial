using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

/// <summary>Server-derived eligibility of a quotation for ORDINARY edit/delete (docs/BUYER_WORKSPACE_QUOTATION_ACTIONS.md §7).</summary>
/// <param name="Evidence">One of <see cref="QuotationSubmissionLock.EvidenceBatchReference"/>, <see cref="QuotationSubmissionLock.EvidenceLineAward"/>, <see cref="QuotationSubmissionLock.EvidenceDurableMarker"/>, or null when not submitted.</param>
public sealed record QuotationSubmissionState(bool IsSubmitted, int? BatchNumber, string? BatchStatus, string? Reason, string? Evidence = null)
{
    public static readonly QuotationSubmissionState NotSubmitted = new(false, null, null, null);
}

/// <summary>
/// "Ever submitted for approval" = the quotation's durable marker (<see cref="Quotation.FirstSubmittedForApprovalAtUtc"/>,
/// written atomically with the first persisted submission by <see cref="QuotationSubmissionStamper"/>) OR any still
/// existing reference: a line of the quotation referenced as a selected item or candidate of ANY approval batch
/// (whatever its status — rejected, cancelled, adjustment, approved) or as the line-level award pointer of a request
/// item (legacy request-level approval). The decision is per quotation: a never-submitted sibling stays editable.
/// A locked quotation is never edited in place or deleted; changes go through the explicit revision/rework flow
/// (SaveQuotation with reworkBatchId / revisesQuotationId), which creates a NEW quotation.
///
/// Reason wording prefers the live batch (earliest BatchNumber/status). When the references were removed (batch
/// recomposition, extra-item reversal, batch deletion) the marker alone keeps the lock and the reason says so.
/// </summary>
public static class QuotationSubmissionLock
{
    public const string ReasonCode = "QUOTATION_SUBMITTED";
    public const string ConcurrencyReasonCode = "QUOTATION_CONCURRENT_CHANGE";
    public const string EvidenceBatchReference = "BATCH_REFERENCE";
    public const string EvidenceLineAward = "LINE_AWARD";
    public const string EvidenceDurableMarker = "DURABLE_MARKER";

    public const string ConcurrencyDetail =
        "A cotação foi alterada ou submetida para aprovação por outra ação enquanto esta operação decorria. Nada foi gravado; atualize o pedido e tente novamente.";

    private const string Tail = " a edição e a exclusão diretas estão bloqueadas, mesmo após rejeição, cancelamento ou reajuste do lote. Para alterar valores, use a revisão/reajuste da cotação.";

    public static string BuildReason(int batchNumber, string batchStatus) =>
        $"Cotação já submetida para aprovação (Lote #{batchNumber}, {batchStatus}):" + Tail;

    public static string BuildLineAwardReason() =>
        "Cotação já adjudicada em aprovação (item do pedido selecionado nesta cotação):" + Tail;

    public static string BuildMarkerReason(DateTime firstSubmittedAtUtc, int? batchNumber, string? batchStatus, string? source = null)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (source == QuotationSubmissionSources.BackfillLineAwardEstimated)
        {
            // Proven submission (legacy line-level award recorded by the migration backfill) with an APPROXIMATE date:
            // never presented as the exact submission instant.
            var approx = firstSubmittedAtUtc.ToString("dd/MM/yyyy", culture);
            return $"Cotação adjudicada em aprovação (registo legado; data de referência aproximada {approx}; lote original não registado):" + Tail;
        }
        if (source == QuotationSubmissionSources.BackfillBatch)
        {
            // Persisted batch evidence recorded by the migration backfill: the date is the batch's creation date, which
            // is the submission instant only for quotations present at batch creation — worded as a batch record.
            var created = firstSubmittedAtUtc.ToString("dd/MM/yyyy", culture);
            var record = batchNumber.HasValue
                ? $"Lote #{batchNumber}, {batchStatus ?? "estado desconhecido"}, criado em {created}; referências do lote entretanto removidas ou recompostas"
                : $"lote de aprovação criado em {created}, já removido ou recomposto";
            return $"Cotação com registo de submissão para aprovação ({record}):" + Tail;
        }
        var when = firstSubmittedAtUtc.ToString("dd/MM/yyyy HH:mm", culture) + " UTC";
        var batch = batchNumber.HasValue
            ? $"Lote #{batchNumber}, {batchStatus ?? "estado desconhecido"}; referências do lote entretanto removidas ou recompostas"
            : "lote original já removido ou recomposto";
        return $"Cotação submetida para aprovação em {when} ({batch}):" + Tail;
    }

    /// <summary>A still-existing batch reference (selected item or candidate) to one of the quotation's lines.</summary>
    public readonly record struct BatchReference(int BatchNumber, string BatchStatus);

    /// <summary>The durable marker plus the batch it points to, when that batch still exists. <paramref name="Source"/> is
    /// the persisted <see cref="Quotation.FirstSubmittedSource"/> (null/SUBMISSION = exact instant; BACKFILL_BATCH =
    /// batch evidence dated by batch creation; BACKFILL_LINE_AWARD_ESTIMATED = approximate date).</summary>
    public readonly record struct Marker(DateTime FirstSubmittedAtUtc, int? BatchNumber, string? BatchStatus, string? Source = null);

    /// <summary>Pure decision: live references first (earliest batch), then the line-level award, then the durable marker.</summary>
    public static QuotationSubmissionState Evaluate(IEnumerable<BatchReference> references, bool hasLineAward, Marker? marker)
    {
        var first = references.OrderBy(r => r.BatchNumber).Select(r => (BatchReference?)r).FirstOrDefault();
        if (first.HasValue)
            return new QuotationSubmissionState(true, first.Value.BatchNumber, first.Value.BatchStatus, BuildReason(first.Value.BatchNumber, first.Value.BatchStatus), EvidenceBatchReference);
        if (hasLineAward)
            return new QuotationSubmissionState(true, marker?.BatchNumber, marker?.BatchStatus, BuildLineAwardReason(), EvidenceLineAward);
        if (marker.HasValue)
            return new QuotationSubmissionState(true, marker.Value.BatchNumber, marker.Value.BatchStatus, BuildMarkerReason(marker.Value.FirstSubmittedAtUtc, marker.Value.BatchNumber, marker.Value.BatchStatus, marker.Value.Source), EvidenceDurableMarker);
        return QuotationSubmissionState.NotSubmitted;
    }

    /// <summary>In-memory decision for a request loaded with ApprovalBatches → Items → Candidates, LineItems and Quotations → Items.</summary>
    public static IReadOnlyDictionary<Guid, QuotationSubmissionState> EvaluateLoaded(Request request)
    {
        var refsByItem = new Dictionary<Guid, List<BatchReference>>();
        void Add(Guid quotationItemId, ApprovalBatch b)
        {
            if (!refsByItem.TryGetValue(quotationItemId, out var l)) refsByItem[quotationItemId] = l = new List<BatchReference>();
            l.Add(new BatchReference(b.BatchNumber, b.Status));
        }
        foreach (var b in request.ApprovalBatches)
        foreach (var bi in b.Items)
        {
            if (bi.SelectedQuotationItemId.HasValue) Add(bi.SelectedQuotationItemId.Value, b);
            foreach (var c in bi.Candidates) Add(c.QuotationItemId, b);
        }
        var awarded = request.LineItems.Where(li => li.SelectedQuotationItemId.HasValue).Select(li => li.SelectedQuotationItemId!.Value).ToHashSet();
        var batchesById = request.ApprovalBatches.ToDictionary(b => b.Id);

        var result = new Dictionary<Guid, QuotationSubmissionState>();
        foreach (var q in request.Quotations)
        {
            var refs = q.Items.Where(i => refsByItem.ContainsKey(i.Id)).SelectMany(i => refsByItem[i.Id]);
            var lineAward = q.Items.Any(i => awarded.Contains(i.Id));
            result[q.Id] = Evaluate(refs, lineAward, ToMarker(q, id => batchesById.TryGetValue(id, out var b) ? b : null));
        }
        return result;
    }

    /// <summary>Database decision for ONE quotation (Items loaded) — used by the mutation guards before any change.</summary>
    public static async Task<QuotationSubmissionState> EvaluateAsync(ApplicationDbContext context, Quotation quotation, CancellationToken ct = default)
    {
        var ids = quotation.Items.Select(i => i.Id).ToList();
        var refs = new List<BatchReference>();
        var lineAward = false;
        if (ids.Count > 0)
        {
            refs.AddRange((await context.Set<ApprovalBatchItem>().AsNoTracking()
                .Where(bi => bi.SelectedQuotationItemId != null && ids.Contains(bi.SelectedQuotationItemId.Value))
                .Select(bi => new { bi.ApprovalBatch.BatchNumber, bi.ApprovalBatch.Status }).ToListAsync(ct))
                .Select(x => new BatchReference(x.BatchNumber, x.Status)));
            refs.AddRange((await context.ApprovalBatchItemCandidates.AsNoTracking()
                .Where(c => ids.Contains(c.QuotationItemId))
                .Select(c => new { c.ApprovalBatchItem.ApprovalBatch.BatchNumber, c.ApprovalBatchItem.ApprovalBatch.Status }).ToListAsync(ct))
                .Select(x => new BatchReference(x.BatchNumber, x.Status)));
            lineAward = await context.RequestLineItems.AsNoTracking().AnyAsync(li => li.SelectedQuotationItemId != null && ids.Contains(li.SelectedQuotationItemId.Value), ct);
        }
        ApprovalBatch? markerBatch = quotation.FirstSubmittedBatchId.HasValue && refs.Count == 0
            ? await context.ApprovalBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == quotation.FirstSubmittedBatchId.Value, ct)
            : null;
        return Evaluate(refs, lineAward, ToMarker(quotation, _ => markerBatch));
    }

    /// <summary>Database decision for every quotation of a request (detail projection flags).</summary>
    public static async Task<IReadOnlyDictionary<Guid, QuotationSubmissionState>> EvaluateForRequestAsync(ApplicationDbContext context, Guid requestId, CancellationToken ct = default)
    {
        var quotations = await context.Quotations.AsNoTracking()
            .Where(q => q.RequestId == requestId)
            .Select(q => new { q.Id, q.FirstSubmittedForApprovalAtUtc, q.FirstSubmittedBatchId, q.FirstSubmittedSource, ItemIds = q.Items.Select(i => i.Id).ToList() })
            .ToListAsync(ct);
        var byItem = quotations.SelectMany(q => q.ItemIds.Select(i => (i, q.Id))).ToDictionary(x => x.i, x => x.Id);
        var ids = byItem.Keys.ToList();
        var refs = new Dictionary<Guid, List<BatchReference>>();
        var awardedQuotations = new HashSet<Guid>();
        void Add(Guid itemId, int n, string s)
        {
            var qid = byItem[itemId];
            if (!refs.TryGetValue(qid, out var l)) refs[qid] = l = new List<BatchReference>();
            l.Add(new BatchReference(n, s));
        }
        if (ids.Count > 0)
        {
            foreach (var x in await context.Set<ApprovalBatchItem>().AsNoTracking()
                .Where(bi => bi.SelectedQuotationItemId != null && ids.Contains(bi.SelectedQuotationItemId.Value))
                .Select(bi => new { ItemId = bi.SelectedQuotationItemId!.Value, bi.ApprovalBatch.BatchNumber, bi.ApprovalBatch.Status }).ToListAsync(ct))
                Add(x.ItemId, x.BatchNumber, x.Status);
            foreach (var x in await context.ApprovalBatchItemCandidates.AsNoTracking()
                .Where(c => ids.Contains(c.QuotationItemId))
                .Select(c => new { ItemId = c.QuotationItemId, c.ApprovalBatchItem.ApprovalBatch.BatchNumber, c.ApprovalBatchItem.ApprovalBatch.Status }).ToListAsync(ct))
                Add(x.ItemId, x.BatchNumber, x.Status);
            foreach (var itemId in await context.RequestLineItems.AsNoTracking()
                .Where(li => li.SelectedQuotationItemId != null && ids.Contains(li.SelectedQuotationItemId.Value))
                .Select(li => li.SelectedQuotationItemId!.Value).ToListAsync(ct))
                awardedQuotations.Add(byItem[itemId]);
        }
        var markerBatchIds = quotations.Where(q => q.FirstSubmittedBatchId.HasValue).Select(q => q.FirstSubmittedBatchId!.Value).Distinct().ToList();
        var markerBatches = markerBatchIds.Count == 0
            ? new Dictionary<Guid, ApprovalBatch>()
            : await context.ApprovalBatches.AsNoTracking().Where(b => markerBatchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);

        var result = new Dictionary<Guid, QuotationSubmissionState>();
        foreach (var q in quotations)
        {
            Marker? marker = q.FirstSubmittedForApprovalAtUtc.HasValue
                ? new Marker(q.FirstSubmittedForApprovalAtUtc.Value,
                    q.FirstSubmittedBatchId.HasValue && markerBatches.TryGetValue(q.FirstSubmittedBatchId.Value, out var mb) ? mb.BatchNumber : null,
                    q.FirstSubmittedBatchId.HasValue && markerBatches.TryGetValue(q.FirstSubmittedBatchId.Value, out var mb2) ? mb2.Status : null,
                    q.FirstSubmittedSource)
                : null;
            result[q.Id] = Evaluate(refs.TryGetValue(q.Id, out var l) ? l : Enumerable.Empty<BatchReference>(), awardedQuotations.Contains(q.Id), marker);
        }
        return result;
    }

    private static Marker? ToMarker(Quotation q, Func<Guid, ApprovalBatch?> resolveBatch)
    {
        if (!q.FirstSubmittedForApprovalAtUtc.HasValue) return null;
        var b = q.FirstSubmittedBatchId.HasValue ? resolveBatch(q.FirstSubmittedBatchId.Value) : null;
        return new Marker(q.FirstSubmittedForApprovalAtUtc.Value, b?.BatchNumber, b?.Status, q.FirstSubmittedSource);
    }
}
