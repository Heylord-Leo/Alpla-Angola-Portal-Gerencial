using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Approvals;

/// <summary>
/// Adjustment V2 — Phase 3 structured cycle lifecycle (see <see cref="IAdjustmentCycleService"/>).
///
/// <para><b>PHASE 3 TRANSITIONAL COMPATIBILITY BEHAVIOR.</b> The approved final routing is
/// Approver → (Requester →) Buyer → Area, with the Buyer's structured "Resposta ao reajuste"
/// captured at a V2 resubmit. Until Phase 4/5 own the Buyer/Requester surfaces, this service:
/// (1) routes EVERY new cycle to WAITING_BUYER — requester-owned/mixed adjustments remain
/// actionable through the existing Buyer rework path (their reasons stay classified as
/// requester-owned with affected-item metadata; the WAITING_REQUESTER hop and requester
/// notifications activate in Phase 5); and (2) closes the open cycle from the legacy resubmit
/// (RESUBMITTED) and batch-cancel (CANCELLED) paths WITHOUT a structured resolution row. Both are
/// temporary and must be replaced when Phase 4 activates structured Buyer resolution.</para>
/// </summary>
public class AdjustmentCycleService : IAdjustmentCycleService
{
    private readonly ApplicationDbContext _context;

    public AdjustmentCycleService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<AdjustmentStageResult> StageNewCycleAsync(
        ApprovalBatch batch, string sourceStage, BatchAdjustmentRequestDto dto, Guid actorId,
        CancellationToken ct = default)
    {
        // ── Comment (mandatory, as today) ──
        var comment = dto.Comment?.Trim();
        if (string.IsNullOrWhiteSpace(comment))
            return AdjustmentStageResult.Fail(400, "Comentário Obrigatório",
                "É necessário informar o motivo do reajuste.");

        // ── At least one reason ──
        if (dto.Reasons == null || dto.Reasons.Count == 0)
            return AdjustmentStageResult.Fail(400, "Motivo Obrigatório",
                "Selecione ao menos um motivo para o reajuste.");

        // ── Normalize + dedupe (ReasonCode, RequestLineItemId); keep first Detail seen ──
        var normalized = new List<BatchAdjustmentReasonInputDto>();
        var seen = new HashSet<(string, Guid?)>();
        var batchItemIds = batch.Items.Select(i => i.RequestLineItemId).ToHashSet();

        foreach (var r in dto.Reasons)
        {
            var code = r.ReasonCode?.Trim().ToUpperInvariant() ?? string.Empty;

            if (!AdjustmentConstants.ReasonCodes.All.Contains(code))
                return AdjustmentStageResult.Fail(400, "Motivo Inválido",
                    $"Código de motivo desconhecido: '{r.ReasonCode}'.");

            // Item-scoped references must belong to this batch (no foreign / cross-request item injection).
            if (r.RequestLineItemId.HasValue && !batchItemIds.Contains(r.RequestLineItemId.Value))
                return AdjustmentStageResult.Fail(400, "Item Inválido",
                    "Um item referenciado no reajuste não pertence a este lote.");

            // Item-required reasons are meaningless without a target item.
            var itemRequired = code is AdjustmentConstants.ReasonCodes.RequestedQuantity
                                    or AdjustmentConstants.ReasonCodes.Specification
                                    or AdjustmentConstants.ReasonCodes.RequestedUnit
                                    or AdjustmentConstants.ReasonCodes.RemoveRequestItem;
            if (itemRequired && !r.RequestLineItemId.HasValue)
                return AdjustmentStageResult.Fail(400, "Item Obrigatório",
                    $"O motivo '{AdjustmentEventLabels.ReasonLabel(code)}' exige a seleção de pelo menos um item específico.");
            if (itemRequired && dto.WholeBatch)
                return AdjustmentStageResult.Fail(400, "Seleção Inconsistente",
                    $"O motivo '{AdjustmentEventLabels.ReasonLabel(code)}' exige itens específicos e não pode ser aplicado ao lote inteiro.");

            var key = (code, r.RequestLineItemId);
            if (seen.Add(key))
                normalized.Add(new BatchAdjustmentReasonInputDto
                {
                    ReasonCode = code,
                    RequestLineItemId = r.RequestLineItemId,
                    Detail = string.IsNullOrWhiteSpace(r.Detail) ? null : r.Detail.Trim()
                });
        }

        // ── One-open-cycle guard (pre-check; the DB unique index is the authority under races) ──
        var hasOpenCycle = await _context.ApprovalBatchAdjustments
            .AnyAsync(a => a.ApprovalBatchId == batch.Id
                        && AdjustmentConstants.States.Open.Contains(a.Status), ct);
        if (hasOpenCycle)
            return AdjustmentStageResult.Fail(409, "Reajuste em Andamento",
                "Já existe um ciclo de reajuste aberto para este lote. Conclua-o antes de solicitar outro.");

        // ── Next CycleNumber (transactional read; unique index catches the race) ──
        var maxCycle = await _context.ApprovalBatchAdjustments
            .Where(a => a.ApprovalBatchId == batch.Id)
            .MaxAsync(a => (int?)a.CycleNumber, ct) ?? 0;
        var now = DateTime.UtcNow;

        var cycle = new ApprovalBatchAdjustment
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = batch.Id,
            CycleNumber = maxCycle + 1,
            SourceStage = sourceStage,
            // PHASE 3 TRANSITIONAL: all cycles start WAITING_BUYER; reason ownership is unchanged
            // and drives the final requester-first routing only once Phase 5 is activated.
            Status = AdjustmentConstants.States.WaitingBuyer,
            WholeBatch = dto.WholeBatch,
            ApproverComment = comment,
            RequestedByUserId = actorId,
            RequestedAtUtc = now,
            CreatedAtUtc = now,
        };

        foreach (var r in normalized)
        {
            cycle.Reasons.Add(new ApprovalBatchAdjustmentReason
            {
                Id = Guid.NewGuid(),
                ReasonCode = r.ReasonCode,
                RequestLineItemId = r.RequestLineItemId,
                Detail = r.Detail,
                CreatedAtUtc = now,
            });
        }

        _context.ApprovalBatchAdjustments.Add(cycle);
        return AdjustmentStageResult.Ok(cycle);
    }

    /// <inheritdoc />
    public async Task<ApprovalBatchAdjustment?> CloseOpenCycleAsync(
        Guid batchId, string terminalStatus, Guid actorId, string? cancelReason,
        CancellationToken ct = default)
    {
        var cycle = await _context.ApprovalBatchAdjustments
            .FirstOrDefaultAsync(a => a.ApprovalBatchId == batchId
                                   && AdjustmentConstants.States.Open.Contains(a.Status), ct);
        if (cycle == null) return null;

        var now = DateTime.UtcNow;
        cycle.Status = terminalStatus;
        cycle.ClosedAtUtc = now;
        cycle.UpdatedAtUtc = now;
        cycle.UpdatedByUserId = actorId;

        if (terminalStatus == AdjustmentConstants.States.Cancelled)
        {
            cycle.CancelledByUserId = actorId;
            cycle.CancelReason = string.IsNullOrWhiteSpace(cancelReason) ? null : cancelReason.Trim();
        }

        return cycle;
    }

    /// <inheritdoc />
    public bool IsUniqueViolation(Exception ex) =>
        ex is DbUpdateException && ex.InnerException is SqlException { Number: 2601 or 2627 };
}
