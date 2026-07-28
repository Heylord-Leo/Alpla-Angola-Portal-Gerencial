using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Validation;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Approvals;

/// <summary>
/// Implements the Buyer's batch-composition decision for genuine EXTRA_ITEM quotation lines
/// (§IBatchExtraItemDecisionService). Single source of truth used by both CreateBatch and
/// UpdateBatch — the Area Approver never calls into this service; by the time a batch reaches
/// Area Approval every relevant EXTRA_ITEM line already has a decision, or the batch shows an
/// explicit, blocking "unresolved legacy line" state (§H) instead of silently guessing.
/// </summary>
public class BatchExtraItemDecisionService : IBatchExtraItemDecisionService
{
    private readonly ApplicationDbContext _context;

    public BatchExtraItemDecisionService(ApplicationDbContext context)
    {
        _context = context;
    }

    private sealed record ExtraLine(QuotationItem Item, string? DocumentNumber, string? SupplierName);

    /// <summary>
    /// Backward compatibility for pre-existing ApprovalBatchExtraItemDecision rows that may still
    /// carry the old BatchAreaApprove-era values (APPROVE/REJECT) instead of the current
    /// INCLUDE/EXCLUDE vocabulary. Comparison-only — never mutates the stored value; an explicit,
    /// audited one-time data conversion (if PROD is found to have any such rows) is a separate,
    /// deliberate migration step, not a silent side effect of reading a batch.
    /// </summary>
    private static bool IsIncludeDecision(string decision) => decision == ExtraItemDecisionValues.Include || decision == "APPROVE";
    private static bool IsExcludeDecision(string decision) => decision == ExtraItemDecisionValues.Exclude || decision == "REJECT";

    public async Task<ExtraItemDecisionApplyResult> ApplyAsync(
        Request request,
        ApprovalBatch batch,
        IEnumerable<Guid> winningQuotationItemIds,
        Dictionary<Guid, ExtraItemDecisionDto>? decisions,
        Guid actorId)
    {
        var winningIdsList = winningQuotationItemIds.Distinct().ToList();
        var extraLines = await LoadExtraLinesAsync(winningIdsList);

        if (extraLines.Count == 0)
            return ExtraItemDecisionApplyResult.Ok();

        var existingDecisions = await _context.ApprovalBatchExtraItemDecisions
            .Where(d => d.ApprovalBatchId == batch.Id)
            .ToListAsync();
        var existingByItemId = existingDecisions.ToDictionary(d => d.QuotationItemId);

        // ── Pass 1: completeness check — every genuine EXTRA_ITEM line must already have a
        // decision on file OR be present in the incoming dictionary. No partial application:
        // if any EXTRA_ITEM line is missing, fail fast before mutating anything. ──
        var pending = new List<PendingExtraItem>();
        foreach (var line in extraLines)
        {
            var hasIncoming = decisions != null && decisions.ContainsKey(line.Item.Id);
            var hasExisting = existingByItemId.ContainsKey(line.Item.Id);
            if (!hasIncoming && !hasExisting)
            {
                pending.Add(new PendingExtraItem
                {
                    QuotationItemId = line.Item.Id,
                    Description = line.Item.Description,
                    QuotationDocumentNumber = line.DocumentNumber,
                    SupplierName = line.SupplierName
                });
            }
        }
        if (pending.Count > 0)
            return ExtraItemDecisionApplyResult.PendingDecisions(pending);

        // ── Pass 2: validate every incoming decision before applying any of them. ──
        if (decisions != null)
        {
            foreach (var (quotationItemId, decision) in decisions)
            {
                var normalized = (decision.Decision ?? string.Empty).Trim().ToUpperInvariant();
                var line = extraLines.FirstOrDefault(l => l.Item.Id == quotationItemId);

                if (line != null)
                {
                    // A genuine EXTRA_ITEM line only ever takes INCLUDE/EXCLUDE.
                    if (normalized != ExtraItemDecisionValues.Include && normalized != ExtraItemDecisionValues.Exclude)
                    {
                        return ExtraItemDecisionApplyResult.Invalid(
                            $"Decisão inválida para o item '{line.Item.Description}'. Utilize INCLUDE ou EXCLUDE.");
                    }

                    if (normalized == ExtraItemDecisionValues.Exclude)
                    {
                        if (!ReconciliationJustificationValidator.IsValid(decision.Comment, out var commentError))
                        {
                            return ExtraItemDecisionApplyResult.Invalid(
                                $"Comentário obrigatório para excluir o item '{line.Item.Description}': {commentError}");
                        }
                    }

                    // Reversal check: INCLUDE (already generated a RequestLineItem) → EXCLUDE.
                    // IsIncludeDecision also recognizes the legacy APPROVE value (backward compatibility).
                    if (normalized == ExtraItemDecisionValues.Exclude &&
                        existingByItemId.TryGetValue(quotationItemId, out var existingDecision) &&
                        IsIncludeDecision(existingDecision.Decision) &&
                        existingDecision.GeneratedRequestLineItemId.HasValue)
                    {
                        var lockResult = await CheckReversalSafeAsync(request, batch, existingDecision);
                        if (lockResult != null)
                            return lockResult;
                    }
                }
                // else: not one of this batch's contributing EXTRA_ITEM lines — ignore silently.
            }
        }

        // ── Apply. Everything up to here validated cleanly; now mutate the tracked context. ──
        if (decisions != null)
        {
            foreach (var (quotationItemId, decision) in decisions)
            {
                var normalized = decision.Decision.Trim().ToUpperInvariant();
                var line = extraLines.FirstOrDefault(l => l.Item.Id == quotationItemId);
                existingByItemId.TryGetValue(quotationItemId, out var existingDecision);

                if (line != null)
                {
                    if (normalized == ExtraItemDecisionValues.Include)
                    {
                        if (existingDecision != null && IsIncludeDecision(existingDecision.Decision)
                            && existingDecision.GeneratedRequestLineItemId.HasValue)
                        {
                            // Already included and unchanged — idempotent no-op. IsIncludeDecision also
                            // recognizes a legacy APPROVE row, so re-submitting INCLUDE for one never
                            // creates a duplicate RequestLineItem/ApprovalBatchItem.
                            continue;
                        }

                        await IncludeExtraItemAsync(request, batch, line.Item, decision.Comment, actorId, existingDecision);
                    }
                    else // EXCLUDE
                    {
                        if (existingDecision != null && IsIncludeDecision(existingDecision.Decision)
                            && existingDecision.GeneratedRequestLineItemId.HasValue)
                        {
                            // Safe reversal already confirmed above — deactivate the generated line.
                            ReverseInclusion(existingDecision);
                        }

                        ExcludeExtraItem(request, batch, line.Item, decision.Comment!, actorId, existingDecision);
                    }
                }
            }
        }

        return ExtraItemDecisionApplyResult.Ok();
    }

    public async Task<BatchInformationalLinesDto> GetInformationalLinesAsync(Guid batchId, IEnumerable<Guid> winningQuotationItemIds)
    {
        var result = new BatchInformationalLinesDto();
        var extraLines = await LoadExtraLinesAsync(winningQuotationItemIds);

        var ids = winningQuotationItemIds.Distinct().ToList();
        var quotationIds = await _context.QuotationItems.AsNoTracking()
            .Where(qi => ids.Contains(qi.Id))
            .Select(qi => qi.QuotationId)
            .Distinct()
            .ToListAsync();

        var ignoredLines = await _context.QuotationItems.AsNoTracking()
            .Include(qi => qi.Quotation)
            .Where(qi => quotationIds.Contains(qi.QuotationId) && qi.ReconciliationStatus == RequestConstants.ReconciliationStatuses.Ignored)
            .ToListAsync();

        foreach (var qi in ignoredLines)
        {
            result.IgnoredLines.Add(ToInformationalItem(qi, qi.Quotation?.DocumentNumber, qi.Quotation?.SupplierNameSnapshot, comment: null));
        }

        if (extraLines.Count == 0)
            return result;

        var decisions = await _context.ApprovalBatchExtraItemDecisions.AsNoTracking()
            .Where(d => d.ApprovalBatchId == batchId)
            .ToListAsync();
        var decisionsByItemId = decisions.ToDictionary(d => d.QuotationItemId);

        foreach (var line in extraLines)
        {
            if (!decisionsByItemId.TryGetValue(line.Item.Id, out var decision))
            {
                result.UnresolvedLegacyLines.Add(ToInformationalItem(line.Item, line.DocumentNumber, line.SupplierName, comment: null));
            }
            else if (IsExcludeDecision(decision.Decision))
            {
                // IsExcludeDecision also recognizes the legacy REJECT value — those rows never had a
                // RequestLineItem created (old BatchAreaApprove only generated one for APPROVE), so
                // without this they would be silently invisible: not unresolved, not excluded, not
                // in the batch either.
                result.ExcludedExtraItems.Add(ToInformationalItem(line.Item, line.DocumentNumber, line.SupplierName, decision.Comment));
            }
            // INCLUDE (or legacy APPROVE) decisions are not informational — they're full ApprovalBatchItems, shown in the main list.
        }

        return result;
    }

    // ── Private helpers ──

    private async Task<List<ExtraLine>> LoadExtraLinesAsync(IEnumerable<Guid> winningQuotationItemIds)
    {
        var ids = winningQuotationItemIds.Distinct().ToList();
        if (ids.Count == 0) return new List<ExtraLine>();

        var quotationIds = await _context.QuotationItems.AsNoTracking()
            .Where(qi => ids.Contains(qi.Id))
            .Select(qi => qi.QuotationId)
            .Distinct()
            .ToListAsync();

        if (quotationIds.Count == 0) return new List<ExtraLine>();

        var items = await _context.QuotationItems
            .Include(qi => qi.Quotation)
            .Where(qi => quotationIds.Contains(qi.QuotationId) && qi.ReconciliationStatus == RequestConstants.ReconciliationStatuses.ExtraItem)
            .ToListAsync();

        return items
            .Select(qi => new ExtraLine(qi, qi.Quotation?.DocumentNumber, qi.Quotation?.SupplierNameSnapshot))
            .ToList();
    }

    private static BatchInformationalItemDto ToInformationalItem(QuotationItem qi, string? documentNumber, string? supplierName, string? comment) => new()
    {
        QuotationItemId = qi.Id,
        Description = qi.Description,
        Quantity = qi.Quantity,
        UnitPrice = qi.UnitPrice,
        LineTotal = qi.LineTotal,
        SupplierName = supplierName,
        QuotationDocumentNumber = documentNumber,
        ReconciliationJustification = qi.ReconciliationJustification,
        Comment = comment
    };

    private Task IncludeExtraItemAsync(
        Request request,
        ApprovalBatch batch,
        QuotationItem quotationItem,
        string? comment,
        Guid actorId,
        ApprovalBatchExtraItemDecision? existingDecision) =>
        CreateIncludedLineAsync(
            request, batch, quotationItem, comment, actorId, existingDecision,
            historyAction: LineItemHistoryActions.ItemAddedFromExtraQuotationLine,
            historyComment: $"[Lote #{batch.BatchNumber}] Item adicional incluído pelo comprador: '{quotationItem.Description}' (Cotação item {quotationItem.Id}).");

    /// <summary>RequestLineItem/ApprovalBatchItem/decision-row creation for an INCLUDE decision.</summary>
    private async Task CreateIncludedLineAsync(
        Request request,
        ApprovalBatch batch,
        QuotationItem quotationItem,
        string? comment,
        Guid actorId,
        ApprovalBatchExtraItemDecision? existingDecision,
        string historyAction,
        string historyComment)
    {
        var activeItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
        var nextLineNumber = activeItems.Count > 0 ? activeItems.Max(i => i.LineNumber) + 1 : 1;

        var newLineItem = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            LineNumber = nextLineNumber,
            Description = "[Item Adicional] " + quotationItem.Description,
            Quantity = quotationItem.Quantity,
            UnitId = quotationItem.UnitId,
            UnitPrice = quotationItem.UnitPrice,
            TotalAmount = quotationItem.LineTotal,
            QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.BatchAssigned,
            CreationOrigin = LineItemCreationOrigins.BuyerExtraItemIncluded,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorId
        };
        // Deliberately not also calling request.LineItems.Add(newLineItem) here — request is
        // already tracked (loaded by the caller), so EF Core's relationship fixup adds the new
        // entity to the navigation collection automatically on _context.RequestLineItems.Add();
        // doing both would duplicate the in-memory entry (caught by a unit test during Phase 2).
        _context.RequestLineItems.Add(newLineItem);

        var newBatchItem = new ApprovalBatchItem
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = batch.Id,
            RequestLineItemId = newLineItem.Id,
            SelectedQuotationItemId = quotationItem.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        // Same reasoning as above — batch is already tracked, so fixup handles batch.Items; no manual Add.
        _context.ApprovalBatchItems.Add(newBatchItem);

        if (existingDecision != null)
        {
            existingDecision.Decision = ExtraItemDecisionValues.Include;
            existingDecision.Comment = comment;
            existingDecision.GeneratedRequestLineItemId = newLineItem.Id;
        }
        else
        {
            _context.ApprovalBatchExtraItemDecisions.Add(new ApprovalBatchExtraItemDecision
            {
                Id = Guid.NewGuid(),
                ApprovalBatchId = batch.Id,
                QuotationItemId = quotationItem.Id,
                Decision = ExtraItemDecisionValues.Include,
                Comment = comment,
                GeneratedRequestLineItemId = newLineItem.Id,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actorId
            });
        }

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actorId,
            ActionTaken = historyAction,
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = historyComment,
            CreatedAtUtc = DateTime.UtcNow
        });

        await Task.CompletedTask;
    }

    private void ExcludeExtraItem(Request request, ApprovalBatch batch, QuotationItem quotationItem, string comment, Guid actorId, ApprovalBatchExtraItemDecision? existingDecision)
    {
        if (existingDecision != null)
        {
            existingDecision.Decision = ExtraItemDecisionValues.Exclude;
            existingDecision.Comment = comment;
        }
        else
        {
            _context.ApprovalBatchExtraItemDecisions.Add(new ApprovalBatchExtraItemDecision
            {
                Id = Guid.NewGuid(),
                ApprovalBatchId = batch.Id,
                QuotationItemId = quotationItem.Id,
                Decision = ExtraItemDecisionValues.Exclude,
                Comment = comment,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actorId
            });
        }

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = batch.RequestId,
            ActorUserId = actorId,
            ActionTaken = "EXTRA_ITEM_EXCLUDED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"[Lote #{batch.BatchNumber}] Item adicional não incluído pelo comprador: '{quotationItem.Description}'. Motivo: {comment}",
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>Deactivates the previously-generated RequestLineItem/ApprovalBatchItem for a safe reversal.
    /// Preserves the RequestLineItem row (soft-deleted) and the decision's GeneratedRequestLineItemId for audit.</summary>
    private void ReverseInclusion(ApprovalBatchExtraItemDecision decision)
    {
        if (!decision.GeneratedRequestLineItemId.HasValue) return;

        var lineItem = _context.RequestLineItems.Local.FirstOrDefault(li => li.Id == decision.GeneratedRequestLineItemId.Value)
            ?? _context.RequestLineItems.Find(decision.GeneratedRequestLineItemId.Value);
        if (lineItem != null)
        {
            lineItem.IsDeleted = true;
        }

        var batchItem = _context.ApprovalBatchItems.Local
            .FirstOrDefault(bi => bi.RequestLineItemId == decision.GeneratedRequestLineItemId.Value && bi.ApprovalBatchId == decision.ApprovalBatchId);
        if (batchItem != null)
        {
            _context.ApprovalBatchItems.Remove(batchItem);
        }
    }

    /// <summary>Enforces the four-condition safe-reversal check (§D2). Returns a Locked result if
    /// any condition fails, or null if the reversal is safe to apply.</summary>
    private async Task<ExtraItemDecisionApplyResult?> CheckReversalSafeAsync(Request request, ApprovalBatch batch, ApprovalBatchExtraItemDecision decision)
    {
        var lineItemId = decision.GeneratedRequestLineItemId!.Value;
        var lineItem = request.LineItems.FirstOrDefault(li => li.Id == lineItemId)
            ?? await _context.RequestLineItems.AsNoTracking().FirstOrDefaultAsync(li => li.Id == lineItemId);

        if (lineItem == null)
            return ExtraItemDecisionApplyResult.Locked("Item gerado não encontrado.", decision.QuotationItemId);

        // Condition 1: must actually be a buyer-included extra.
        if (lineItem.CreationOrigin != LineItemCreationOrigins.BuyerExtraItemIncluded)
            return ExtraItemDecisionApplyResult.Locked("O item não tem origem de inclusão de item adicional.", decision.QuotationItemId);

        // Condition 2: no other batch (past or present) references this line.
        var referencedElsewhere = await _context.ApprovalBatchItems.AsNoTracking()
            .AnyAsync(bi => bi.RequestLineItemId == lineItemId && bi.ApprovalBatchId != batch.Id);
        if (referencedElsewhere)
            return ExtraItemDecisionApplyResult.Locked("O item já está referenciado por outro lote de aprovação.", decision.QuotationItemId);

        // Condition 3: the batch must not have been resubmitted/approved since this inclusion.
        var lastSubmissionEvent = await _context.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == request.Id
                     && (h.ActionTaken == "BATCH_RESUBMITTED" || h.ActionTaken == "BATCH_AREA_APPROVED")
                     && h.Comment != null && h.Comment.Contains($"Lote #{batch.BatchNumber}"))
            .OrderByDescending(h => h.CreatedAtUtc)
            .Select(h => (DateTime?)h.CreatedAtUtc)
            .FirstOrDefaultAsync();
        if (lastSubmissionEvent.HasValue && lastSubmissionEvent.Value > decision.CreatedAtUtc)
            return ExtraItemDecisionApplyResult.Locked("O lote já foi reenviado ou aprovado desde que este item foi incluído.", decision.QuotationItemId);

        // Condition 4: no downstream references — allocations, receiving, PO group.
        var hasAllocations = await _context.RequestLineItemAllocations.AsNoTracking().AnyAsync(a => a.RequestLineItemId == lineItemId);
        if (hasAllocations)
            return ExtraItemDecisionApplyResult.Locked("O item já possui alocação financeira registrada.", decision.QuotationItemId);

        if (lineItem.ReceivedQuantity != 0 || !string.IsNullOrWhiteSpace(lineItem.DivergenceNotes))
            return ExtraItemDecisionApplyResult.Locked("O item já possui recebimento registrado.", decision.QuotationItemId);

        if (lineItem.RequestPoGroupId.HasValue)
            return ExtraItemDecisionApplyResult.Locked("O item já está associado a um grupo de PO.", decision.QuotationItemId);

        // ItemCatalogId is a reference association, not an operational commitment — does not block (resolved, §D2).
        return null;
    }
}
