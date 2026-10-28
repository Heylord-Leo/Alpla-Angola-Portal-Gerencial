using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

/// <summary>
/// v2.249.1 — loads the facts <see cref="QuotationWindowPolicy"/> needs straight from the database (lines,
/// batch holds) so write endpoints can apply the item-level window without widening their own Includes.
/// Read-only; three small queries; the policy itself stays pure in the Domain.
/// </summary>
public static class QuotationWindowEvaluator
{
    public static async Task<QuotationWindowPolicy.Evaluation> EvaluateAsync(
        ApplicationDbContext context,
        Guid requestId,
        string? requestTypeCode,
        string? requestStatusCode,
        bool isCancelled,
        CancellationToken ct = default)
    {
        if (requestTypeCode == null || requestStatusCode == null)
        {
            var head = await context.Requests.AsNoTracking()
                .Where(r => r.Id == requestId)
                .Select(r => new { Type = r.RequestType.Code, Status = r.Status.Code, r.IsCancelled })
                .FirstOrDefaultAsync(ct);
            if (head == null) return QuotationWindowPolicy.Evaluation.Closed(QuotationWindowPolicy.ClosedNotQuotationType);
            requestTypeCode ??= head.Type;
            requestStatusCode ??= head.Status;
            isCancelled = isCancelled || head.IsCancelled;
        }

        var lines = await context.RequestLineItems.AsNoTracking()
            .Where(li => li.RequestId == requestId)
            .Select(li => new { li.Id, li.IsDeleted, li.QuotationLifecycleStatus })
            .ToListAsync(ct);

        var hasBatches = await context.ApprovalBatches.AsNoTracking().AnyAsync(b => b.RequestId == requestId, ct);

        var held = new HashSet<Guid>();
        if (hasBatches)
        {
            var holding = QuotationWindowPolicy.HoldingBatchStatuses;
            var heldIds = await context.ApprovalBatchItems.AsNoTracking()
                .Where(bi => bi.ApprovalBatch.RequestId == requestId
                             && holding.Contains(bi.ApprovalBatch.Status)
                             && bi.ReleasedAtUtc == null)
                .Select(bi => bi.RequestLineItemId)
                .ToListAsync(ct);
            held = heldIds.ToHashSet();
        }

        return QuotationWindowPolicy.Evaluate(
            requestTypeCode, requestStatusCode, isCancelled, hasBatches,
            lines.Select(l => new QuotationWindowPolicy.LineFacts(l.Id, l.IsDeleted, l.QuotationLifecycleStatus, held.Contains(l.Id))));
    }

    /// <summary>Uses whatever the caller already loaded (RequestType/Status navigation may be null — then they are queried).</summary>
    public static Task<QuotationWindowPolicy.Evaluation> EvaluateAsync(ApplicationDbContext context, Request request, CancellationToken ct = default) =>
        EvaluateAsync(context, request.Id, request.RequestType?.Code, request.Status?.Code, request.IsCancelled, ct);
}
