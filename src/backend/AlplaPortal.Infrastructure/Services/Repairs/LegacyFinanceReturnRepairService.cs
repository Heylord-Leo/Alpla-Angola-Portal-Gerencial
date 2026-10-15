using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AlplaPortal.Infrastructure.Services.Repairs;

/// <summary>
/// Scoped repair for the legacy request-level Finance return (docs §G.19): moves the ONE qualifying group from
/// PO_ISSUED to WAITING_PO_CORRECTION so the correction becomes actionable through the normal flow. Exactly one
/// request per call, PREVIEW by default. APPLY re-evaluates the predicate on a fresh tracked load inside a
/// transaction (relational providers), fails closed when the live qualifying group differs from the PREVIEW fact, is
/// idempotent (audit row keyed by group), writes ONLY the group status plus one audit history row (scalar status,
/// amounts, approval metadata, documents, prior history and notification logs untouched), emits no workflow event and
/// no e-mail. ROLLBACK reverts the group to PO_ISSUED only while nothing legitimate happened after the repair (no
/// later history row of any kind, no live payment, group still WAITING_PO_CORRECTION).
/// </summary>
public sealed class LegacyFinanceReturnRepairService
{
    public const string RepairAction = "LEGACY_RETURN_REPAIR";
    public const string RollbackAction = "LEGACY_RETURN_REPAIR_ROLLBACK";
    public static string IdempotencyKey(Guid groupId) => $"LEGACY_RETURN_REPAIR:{groupId:D}";

    private readonly ApplicationDbContext _context;
    public LegacyFinanceReturnRepairService(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// TEST-ONLY synchronization point: awaited after the qualifying group has been loaded (tracked, with its RowVersion)
    /// and staged, immediately before SaveChanges of the APPLY. Relational race tests use it to interleave a concurrent
    /// writer deterministically. Never set in production code paths.
    /// </summary>
    public Func<Task>? BeforeApplySaveHookForTests { get; set; }

    public async Task<LegacyFinanceReturnRepairResultDto?> PreviewAsync(Guid requestId, CancellationToken ct = default)
    {
        var head = await HeadAsync(requestId, ct);
        if (head == null) return null;
        var facts = await LegacyFinanceReturnShape.LoadAsync(_context, requestId, ct);
        var verdict = LegacyFinanceReturnEvaluator.Evaluate(facts);
        var dto = Describe("PREVIEW", head, facts, verdict);
        if (verdict.Qualifies)
        {
            dto.Decision = "QUALIFIES";
            dto.Reason = "Forma legada confirmada: devolução por Finanças ao nível do pedido com grupo ainda PO_ISSUED. APPLY moverá apenas o grupo para WAITING_PO_CORRECTION.";
            dto.GroupStatusAfter = RequestConstants.PoGroupStatuses.WaitingPoCorrection;
        }
        else if (await AlreadyRepairedAsync(facts, ct))
        {
            dto.Decision = "ALREADY_REPAIRED";
            dto.Reason = "Reparação já aplicada a este grupo (audit LEGACY_RETURN_REPAIR presente).";
        }
        else
        {
            dto.Decision = "REFUSED";
            dto.Reason = verdict.Reason;
        }
        return dto;
    }

    public async Task<LegacyFinanceReturnRepairResultDto?> ApplyAsync(Guid requestId, Guid actorId, string reason, Guid expectedGroupId, CancellationToken ct = default)
    {
        var head = await HeadAsync(requestId, ct);
        if (head == null) return null;
        IDbContextTransaction? tx = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var facts = await LegacyFinanceReturnShape.LoadAsync(_context, requestId, ct);
            var verdict = LegacyFinanceReturnEvaluator.Evaluate(facts);
            var dto = Describe("APPLY", head, facts, verdict);
            if (!verdict.Qualifies)
            {
                if (tx != null) await tx.RollbackAsync(ct);
                var already = await AlreadyRepairedAsync(facts, ct);
                dto.Decision = already ? "ALREADY_REPAIRED" : "REFUSED";
                dto.Reason = already ? "Reparação já aplicada a este grupo; nada escrito." : $"Nada escrito: {verdict.Reason}";
                return dto;
            }
            if (verdict.GroupId != expectedGroupId)
            {
                if (tx != null) await tx.RollbackAsync(ct);
                dto.Decision = "REFUSED";
                dto.Reason = $"Nada escrito: o grupo qualificado ({verdict.GroupId}) difere do facto da pré-visualização ({expectedGroupId}).";
                return dto;
            }
            var key = IdempotencyKey(verdict.GroupId!.Value);
            if (await _context.RequestStatusHistories.AnyAsync(h => h.IdempotencyKey == key, ct))
            {
                if (tx != null) await tx.RollbackAsync(ct);
                dto.Decision = "ALREADY_REPAIRED"; dto.Reason = "Audit LEGACY_RETURN_REPAIR já existe; nada escrito."; dto.AuditIdempotencyKey = key;
                return dto;
            }

            // Tracked load of exactly the two rows that change: the group (status) and nothing else.
            var group = await _context.RequestPoGroups.FirstAsync(g => g.Id == verdict.GroupId.Value, ct);
            var request = await _context.Requests.AsNoTracking().FirstAsync(r => r.Id == requestId, ct);
            var before = group.Status;
            group.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection;
            group.UpdatedAtUtc = DateTime.UtcNow;
            group.UpdatedByUserId = actorId;
            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = requestId,
                ActorUserId = actorId,
                ActionTaken = RepairAction,
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,                       // scalar deliberately unchanged
                IdempotencyKey = key,
                Comment = $"[REPARAÇÃO §G.19] Grupo {group.SupplierNameSnapshot ?? group.Id.ToString()}: {before} → WAITING_PO_CORRECTION (devolução legada por Finanças ao nível do pedido). " +
                          $"Devolução {facts.LatestFinanceReturnUtc:yyyy-MM-dd HH:mm:ss}Z, último registo de P.O. {facts.LatestPoRegistrationUtc:yyyy-MM-dd HH:mm:ss}Z. Motivo: {reason}",
                CreatedAtUtc = DateTime.UtcNow
            });
            if (BeforeApplySaveHookForTests != null) await BeforeApplySaveHookForTests();
            await _context.SaveChangesAsync(ct);               // RowVersion on the group → DbUpdateConcurrencyException on a concurrent change
            if (tx != null) await tx.CommitAsync(ct);
            dto.Decision = "REPAIRED"; dto.Reason = "Grupo movido para WAITING_PO_CORRECTION; audit escrito; nenhum evento/e-mail emitido.";
            dto.GroupStatusBefore = before; dto.GroupStatusAfter = group.Status; dto.AuditIdempotencyKey = key;
            return dto;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            var facts = await LegacyFinanceReturnShape.LoadAsync(_context, requestId, ct);
            var dto = Describe("APPLY", head, facts, LegacyFinanceReturnEvaluator.Evaluate(facts));
            dto.Decision = "REFUSED"; dto.Reason = "Nada escrito: o grupo foi alterado concorrentemente (RowVersion). Repita a pré-visualização.";
            return dto;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<LegacyFinanceReturnRepairResultDto?> RollbackAsync(Guid requestId, Guid actorId, string reason, Guid expectedGroupId, CancellationToken ct = default)
    {
        var head = await HeadAsync(requestId, ct);
        if (head == null) return null;
        IDbContextTransaction? tx = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var facts = await LegacyFinanceReturnShape.LoadAsync(_context, requestId, ct);
            var dto = Describe("ROLLBACK", head, facts, LegacyFinanceReturnEvaluator.Evaluate(facts));
            var key = IdempotencyKey(expectedGroupId);
            var audit = await _context.RequestStatusHistories.AsNoTracking().FirstOrDefaultAsync(h => h.IdempotencyKey == key && h.ActionTaken == RepairAction, ct);
            string? refusal = null;
            if (audit == null) refusal = "sem audit LEGACY_RETURN_REPAIR para este grupo";
            else if (await _context.RequestStatusHistories.AnyAsync(h => h.RequestId == requestId && h.CreatedAtUtc > audit.CreatedAtUtc, ct))
                refusal = "existe histórico posterior à reparação (ação legítima subsequente); reversão não permitida";
            else if (facts.LivePaymentCount > 0) refusal = "existem pagamentos ativos";
            var group = refusal == null ? await _context.RequestPoGroups.FirstOrDefaultAsync(g => g.Id == expectedGroupId && g.RequestId == requestId, ct) : null;
            if (refusal == null && (group == null || group.Status != RequestConstants.PoGroupStatuses.WaitingPoCorrection))
                refusal = "grupo não está em WAITING_PO_CORRECTION";
            if (refusal != null)
            {
                if (tx != null) await tx.RollbackAsync(ct);
                dto.Decision = "REFUSED"; dto.Reason = $"Nada escrito: {refusal}.";
                return dto;
            }
            var request = await _context.Requests.AsNoTracking().FirstAsync(r => r.Id == requestId, ct);
            group!.Status = RequestConstants.PoGroupStatuses.PoIssued;
            group.UpdatedAtUtc = DateTime.UtcNow;
            group.UpdatedByUserId = actorId;
            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(), RequestId = requestId, ActorUserId = actorId, ActionTaken = RollbackAction,
                PreviousStatusId = request.StatusId, NewStatusId = request.StatusId,
                IdempotencyKey = $"{key}:ROLLBACK",
                Comment = $"[REVERSÃO §G.19] Grupo {group.SupplierNameSnapshot ?? group.Id.ToString()}: WAITING_PO_CORRECTION → PO_ISSUED (reversão da reparação legada). Motivo: {reason}",
                CreatedAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            dto.Decision = "ROLLED_BACK"; dto.Reason = "Grupo revertido para PO_ISSUED; audit de reversão escrito.";
            dto.GroupStatusBefore = RequestConstants.PoGroupStatuses.WaitingPoCorrection; dto.GroupStatusAfter = group.Status; dto.AuditIdempotencyKey = key;
            return dto;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            throw;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    private sealed record Head(string RequestNumber, string RequestTypeCode);

    private async Task<Head?> HeadAsync(Guid requestId, CancellationToken ct) =>
        await _context.Requests.AsNoTracking().Where(r => r.Id == requestId)
            .Select(r => new Head(r.RequestNumber ?? "", r.RequestType != null ? r.RequestType.Code : ""))
            .FirstOrDefaultAsync(ct);

    private async Task<bool> AlreadyRepairedAsync(LegacyFinanceReturnFacts facts, CancellationToken ct)
    {
        if (facts.LiveGroups.Count != 1) return false;
        var key = IdempotencyKey(facts.LiveGroups[0].GroupId);
        return await _context.RequestStatusHistories.AnyAsync(h => h.IdempotencyKey == key && h.ActionTaken == RepairAction, ct);
    }

    private static LegacyFinanceReturnRepairResultDto Describe(string mode, Head head, LegacyFinanceReturnFacts facts, LegacyFinanceReturnVerdict verdict) => new()
    {
        Mode = mode, RequestId = facts.RequestId, RequestNumber = head.RequestNumber, RequestTypeCode = head.RequestTypeCode,
        ScalarStatusCode = facts.ScalarStatusCode,
        GroupId = facts.LiveGroups.Count == 1 ? facts.LiveGroups[0].GroupId : null,
        GroupStatusBefore = facts.LiveGroups.Count == 1 ? facts.LiveGroups[0].Status : null,
        GroupStatusAfter = facts.LiveGroups.Count == 1 ? facts.LiveGroups[0].Status : null,
        LatestFinanceReturnUtc = facts.LatestFinanceReturnUtc, LatestPoRegistrationUtc = facts.LatestPoRegistrationUtc,
        LiveGroupCount = facts.LiveGroups.Count, LivePaymentCount = facts.LivePaymentCount,
        Preserved = { "Request.StatusId (scalar)", "ApprovedTotalAmount/ApprovedCurrencyCode/ApprovedAtUtc", "amounts", "attachments", "existing history", "notification logs", "no workflow event, no e-mail" }
    };
}
