using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Common;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using C = AlplaPortal.Domain.Constants.PoGroupCancellationConstants;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

/// <summary>
/// Phase 1 P.O. group cancellation / return to quotation after final approval (docs/PO_GROUP_CANCELLATION.md).
///
/// Invariants enforced here and nowhere else:
///  • the original quotation, the APPROVED batch, its winner stamps and the P.O. document are never deleted — the
///    batch items are RELEASED, the P.O. attachment is VOIDED (DEC-150 semantics), the group becomes CANCELLED;
///  • only lines linked to the cancelled group are touched; sibling groups, their lines and their payments are not read
///    for writing;
///  • the Buyer's ECF declaration and Finance's confirmation are separate statements by separate actors (D12);
///  • every write is one transaction, with the notification emitted INSIDE it (in-app persistence required).
/// </summary>
public sealed class PoGroupCancellationService : IPoGroupCancellationService
{
    private readonly ApplicationDbContext _context;
    private readonly IRequestStatusSyncService _statusSync;
    private readonly IWorkflowNotificationOrchestrator _orchestrator;
    private readonly AdminLogWriter _adminLog;
    private readonly ILogger<PoGroupCancellationService> _logger;

    public PoGroupCancellationService(
        ApplicationDbContext context,
        IRequestStatusSyncService statusSync,
        IWorkflowNotificationOrchestrator orchestrator,
        AdminLogWriter adminLog,
        ILogger<PoGroupCancellationService> logger)
    {
        _context = context;
        _statusSync = statusSync;
        _orchestrator = orchestrator;
        _adminLog = adminLog;
        _logger = logger;
    }

    private static readonly string[] RegisterPoActions = { "REGISTER_PO", "REREGISTER_PO" };

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  Cancel (RESTART_QUOTATION | CANCEL_ITEMS)
    // ═══════════════════════════════════════════════════════════════════════════════════════

    public async Task<PoGroupCancellationOperationResult> CancelAsync(CancelPoGroupCommand cmd)
    {
        // ── Input validation (nothing read yet) ──
        var kind = cmd.Kind?.Trim();
        if (kind == null || !C.Kinds.All.Contains(kind))
            return Fail(400, C.ErrorCodes.KindInvalid, "Tipo de cancelamento inválido", "Indique RESTART_QUOTATION (voltar a cotar) ou CANCEL_ITEMS (encerrar itens).");
        var reasonCode = cmd.ReasonCode?.Trim();
        if (!C.ReasonCodes.IsValid(reasonCode))
            return Fail(400, C.ErrorCodes.ReasonCodeInvalid, "Motivo inválido", "Selecione um motivo de cancelamento válido.");
        var reason = (cmd.Reason ?? string.Empty).Trim();
        if (reason.Length < C.MinJustificationLength || reason.Length > C.MaxJustificationLength)
            return Fail(400, C.ErrorCodes.JustificationRequired, "Explicação obrigatória",
                $"A explicação é obrigatória e deve ter entre {C.MinJustificationLength} e {C.MaxJustificationLength} caracteres.");

        // ── Scope + load ──
        if (!await InScopeAsync(cmd.RequestId, cmd.Actor)) return Fail(404, "NOT_FOUND", "Pedido não encontrado", "Pedido não encontrado ou fora do seu âmbito.");

        var r = await LoadRequestForWriteAsync(cmd.RequestId);
        if (r == null) return Fail(404, "NOT_FOUND", "Pedido não encontrado", "Pedido não encontrado.");
        var group = r.PoGroups.FirstOrDefault(g => g.Id == cmd.GroupId);
        if (group == null) return Fail(404, "GROUP_NOT_FOUND", "Grupo P.O não encontrado", "O grupo P.O não pertence a este pedido.");

        // ── Authorization (D1): responsible Buyer or SystemAdministrator ──
        var auth = AuthorizeBuyer(r, group, cmd.Actor);
        if (auth != null) return auth;

        // ── Concurrency token the client displayed ──
        if (cmd.ExpectedGroupRowVersion != null && !cmd.ExpectedGroupRowVersion.SequenceEqual(group.RowVersion))
            return Fail(409, C.ErrorCodes.CancellationConcurrentChange, "Dados desatualizados",
                "O grupo P.O foi alterado por outra operação. Atualize a página e verifique o estado atual antes de repetir.");

        // ── Eligibility (pure rules) ──
        var facts = await BuildFactsAsync(r, group);
        var verdict = PoGroupCancellationEvaluator.Evaluate(facts);
        if (!verdict.CanCancel)
            return Fail(409, verdict.Code!, "Cancelamento não permitido", verdict.Message!);

        // ── ECF declaration (D4 §D.1): required only when the group ever had a P.O. ──
        var requiresFinance = await RequiresFinanceConfirmationAsync(r, group);
        string ecfStatus = C.EcfStatuses.NotApplicable; string? ecfReference = null; string? ecfNote = null;
        if (requiresFinance)
        {
            var declared = cmd.EcfStatus?.Trim();
            if (declared == null || !C.EcfStatuses.Declarable.Contains(declared))
                return Fail(400, C.ErrorCodes.EcfDeclarationRequired, "Declaração sobre a ECF obrigatória",
                    "Este grupo teve uma P.O registada. Declare se a ECF/P.O ainda não foi anulada, já foi anulada fora do Portal, ou se desconhece a situação.");
            ecfReference = cmd.EcfReference?.Trim();
            if (declared == C.EcfStatuses.DeclaredCancelled && string.IsNullOrWhiteSpace(ecfReference))
                return Fail(400, C.ErrorCodes.EcfReferenceRequired, "Referência da ECF obrigatória",
                    "Ao declarar que a ECF já foi anulada, indique a referência do documento anulado (ex.: ECF10 2026/364).");
            if (ecfReference != null && ecfReference.Length > C.MaxEcfReferenceLength)
                return Fail(400, C.ErrorCodes.EcfReferenceRequired, "Referência demasiado longa", $"A referência da ECF tem no máximo {C.MaxEcfReferenceLength} caracteres.");
            ecfStatus = declared;
            ecfNote = Trunc(cmd.EcfDeclarationNote, C.MaxJustificationLength);
        }

        var actor = await _context.Users.AsNoTracking().Where(u => u.Id == cmd.Actor.UserId).Select(u => new { u.FullName }).FirstOrDefaultAsync();
        var actorName = actor?.FullName ?? "Sistema";
        var now = DateTime.UtcNow;
        var roleSnapshot = RoleSnapshot(cmd.Actor);

        var waitingQuotationStatus = await _context.LineItemStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Code == "WAITING_QUOTATION");
        var cancelledLineStatus = await _context.LineItemStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Code == "CANCELLED");
        var cancelledRequestStatus = await _context.RequestStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.Cancelled);

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var groupLines = r.LineItems.Where(li => !li.IsDeleted && li.RequestPoGroupId == group.Id).OrderBy(li => li.LineNumber).ToList();
            var groupLineIds = groupLines.Select(l => l.Id).ToHashSet();
            var batch = group.ApprovalBatchId.HasValue ? r.ApprovalBatches.FirstOrDefault(b => b.Id == group.ApprovalBatchId.Value) : null;
            var batchItems = batch?.Items.Where(bi => groupLineIds.Contains(bi.RequestLineItemId)).ToList() ?? new List<ApprovalBatchItem>();

            var originReturn = await _context.RequestStatusHistories.AsNoTracking()
                .Where(h => h.RequestId == r.Id && h.ActionTaken == "FINANCE_RETURN_ADJUSTMENT" && h.Comment != null && h.Comment.Contains(group.Id.ToString()))
                .OrderByDescending(h => h.CreatedAtUtc).Select(h => (Guid?)h.Id).FirstOrDefaultAsync();

            // 1. The durable record
            var cancellation = new RequestPoGroupCancellation
            {
                RequestPoGroupId = group.Id, RequestId = r.Id, Kind = kind, ReasonCode = reasonCode!, Reason = reason,
                PreviousGroupStatus = group.Status, PurchaseOrderNumberSnapshot = group.PurchaseOrderNumber,
                TotalAmountSnapshot = group.TotalAmount, CurrencyCodeSnapshot = group.CurrencyCode ?? group.Currency?.Code,
                ApprovalBatchNumberSnapshot = batch?.BatchNumber, OriginFinanceReturnHistoryId = originReturn,
                CancelledByUserId = cmd.Actor.UserId, CancelledAtUtc = now,
                FinanceConfirmationRequired = requiresFinance, EcfStatus = ecfStatus, EcfReference = ecfReference, EcfDeclarationNote = ecfNote,
                EcfEvidenceAttachmentId = requiresFinance ? cmd.EcfEvidenceAttachmentId : null,
                EcfDeclaredByUserId = requiresFinance ? cmd.Actor.UserId : null, EcfDeclaredAtUtc = requiresFinance ? now : null,
                FinanceConfirmationStatus = requiresFinance ? C.FinanceConfirmationStatuses.Pending : C.FinanceConfirmationStatuses.NotRequired,
            };
            _context.RequestPoGroupCancellations.Add(cancellation);
            if (requiresFinance)
            {
                cancellation.EcfEvents.Add(new RequestPoGroupCancellationEcfEvent
                {
                    EventType = C.EcfEventTypes.BuyerDeclared, EcfStatusAfter = ecfStatus, FinanceStatusAfter = cancellation.FinanceConfirmationStatus,
                    Reference = ecfReference, Note = ecfNote, EvidenceAttachmentId = cancellation.EcfEvidenceAttachmentId,
                    ActorUserId = cmd.Actor.UserId, ActorRoleSnapshot = roleSnapshot, AtUtc = now,
                });
            }

            // 2. Group → CANCELLED (the only status write; the row is never mutated again)
            group.Status = RequestConstants.PoGroupStatuses.Cancelled;
            group.UpdatedAtUtc = now; group.UpdatedByUserId = cmd.Actor.UserId;

            // 3. Payments: PLANNED rows are cancelled (audit kept); SCHEDULED/COMPLETED were refused by the evaluator.
            var cancelledPayments = 0;
            foreach (var p in group.Payments.Where(p => p.PaymentStatus == RequestPayment.PaymentStatuses.Planned))
            {
                p.PaymentStatus = RequestPayment.PaymentStatuses.Cancelled;
                p.UpdatedByUserId = cmd.Actor.UserId; p.UpdatedAtUtc = now;
                p.Notes = Trunc($"{p.Notes} Cancelado em {now:dd/MM/yyyy HH:mm} UTC: grupo P.O cancelado ({C.ReasonCodes.Label(reasonCode)}).".Trim(), 2000);
                cancelledPayments++;
            }

            // 4. P.O. documents: VOIDED, never deleted — they stay downloadable and visibly "Sem efeito" (DEC-150).
            var voided = 0;
            var voidReason = Trunc($"P.O cancelada ({(kind == C.Kinds.RestartQuotation ? "itens voltam a cotação" : "itens encerrados")}) por {actorName} em {AppTimeZone.FormatWithUtc(now)}. " +
                                   $"Motivo: {C.ReasonCodes.Label(reasonCode)} — {reason}. ECF: {EcfLabel(ecfStatus, ecfReference)}", 1000);
            foreach (var a in r.Attachments.Where(a => a.RequestPoGroupId == group.Id && a.AttachmentTypeCode == RequestAttachment.TYPE_PO && !a.IsDeleted && a.VoidedAtUtc == null))
            {
                a.VoidedAtUtc = now; a.VoidedByUserId = cmd.Actor.UserId; a.VoidReason = voidReason; voided++;
            }

            // 5. Batch items: RELEASED (batch stays APPROVED; winner stamps untouched)
            foreach (var bi in batchItems)
            {
                bi.ReleasedAtUtc = now; bi.ReleasedByUserId = cmd.Actor.UserId; bi.ReleaseKind = kind; bi.PoGroupCancellationId = cancellation.Id;
            }

            // 6. Lines of THIS group only
            var released = 0; var closed = 0;
            foreach (var li in groupLines)
            {
                var previousStatusCode = li.LineItemStatus?.Code;
                var item = new RequestPoGroupCancellationItem
                {
                    RequestLineItemId = li.Id, PreviousPoGroupId = group.Id,
                    ApprovalBatchItemId = batchItems.FirstOrDefault(bi => bi.RequestLineItemId == li.Id)?.Id,
                    PreviousLineItemStatusCode = previousStatusCode, PreviousQuotationLifecycleStatus = li.QuotationLifecycleStatus,
                    PreviousSelectedQuotationItemId = li.SelectedQuotationItemId,
                };
                if (kind == C.Kinds.RestartQuotation)
                {
                    item.Outcome = "RELEASED";
                    li.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationPending;
                    li.SelectedQuotationItemId = null;
                    li.RequestPoGroupId = null;
                    if (waitingQuotationStatus != null && PoGroupCancellationEvaluator.IsRestorableLineStatus(previousStatusCode) && li.LineItemStatusId != waitingQuotationStatus.Id)
                    {
                        AddFieldChange(r, li, cmd.Actor.UserId, now, "LineItemStatus", "Estado do item", previousStatusCode, "WAITING_QUOTATION");
                        li.LineItemStatusId = waitingQuotationStatus.Id;
                    }
                    AddFieldChange(r, li, cmd.Actor.UserId, now, "QuotationLifecycleStatus", "Ciclo de cotação", item.PreviousQuotationLifecycleStatus, RequestConstants.QuotationLifecycleStatuses.QuotationPending);
                    released++;
                }
                else
                {
                    item.Outcome = "CLOSED";
                    li.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval;
                    if (cancelledLineStatus != null && li.LineItemStatusId != cancelledLineStatus.Id)
                    {
                        AddFieldChange(r, li, cmd.Actor.UserId, now, "LineItemStatus", "Estado do item", previousStatusCode, "CANCELLED");
                        li.LineItemStatusId = cancelledLineStatus.Id;
                    }
                    AddFieldChange(r, li, cmd.Actor.UserId, now, "QuotationLifecycleStatus", "Ciclo de cotação", item.PreviousQuotationLifecycleStatus, RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval);
                    closed++;
                }
                li.UpdatedAtUtc = now; li.UpdatedByUserId = cmd.Actor.UserId;
                cancellation.Items.Add(item);
            }

            // 7. History (the PO_GROUP_CANCELLED row is the notification correlation)
            var prefix = FinanceHistoryCommentFormatter.FormatGroupPrefix(batch?.BatchNumber, group.Supplier?.Name ?? group.SupplierNameSnapshot, group.CurrencyCode, "Total", group.TotalAmount);
            var lineRefs = string.Join(", ", groupLines.Select(l => $"#{l.LineNumber}"));
            var mainHistory = new RequestStatusHistory
            {
                RequestId = r.Id, ActorUserId = cmd.Actor.UserId, ActionTaken = C.HistoryActions.GroupCancelled,
                PreviousStatusId = r.StatusId, NewStatusId = r.StatusId, CreatedAtUtc = now,
                Comment = Trunc($"{prefix} Grupo P.O cancelado ({(kind == C.Kinds.RestartQuotation ? "itens voltam a cotação" : "itens encerrados")}) por {actorName}. " +
                                $"Motivo: {C.ReasonCodes.Label(reasonCode)} — {reason}. P.O anterior: {group.PurchaseOrderNumber ?? "nenhuma registada"}. " +
                                $"ECF (declaração do comprador): {EcfLabel(ecfStatus, ecfReference)}. Confirmação de Finanças: {(requiresFinance ? "pendente" : "não necessária")}. " +
                                $"(GroupId: {group.Id}; CancellationId: {cancellation.Id})", 4000),
            };
            _context.RequestStatusHistories.Add(mainHistory);
            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                RequestId = r.Id, ActorUserId = cmd.Actor.UserId,
                ActionTaken = kind == C.Kinds.RestartQuotation ? C.HistoryActions.ItemsReleased : C.HistoryActions.ItemsClosed,
                PreviousStatusId = r.StatusId, NewStatusId = r.StatusId, CreatedAtUtc = now.AddMilliseconds(1),
                Comment = kind == C.Kinds.RestartQuotation
                    ? $"Itens {lineRefs} libertados do lote #{batch?.BatchNumber.ToString() ?? "—"} e devolvidos à cotação; novo ciclo de aprovação necessário. Alocações e cotações anteriores preservadas."
                    : $"Itens {lineRefs} encerrados após aprovação (CANCELLED_AFTER_APPROVAL). Cotação, lote #{batch?.BatchNumber.ToString() ?? "—"} e P.O preservados como histórico.",
            });

            await _context.SaveChangesAsync();

            // 8. Request scalar — derived; D8 explicit CANCELLED when nothing active remains
            var requestCancelled = false;
            if (kind == C.Kinds.CancelItems && cancelledRequestStatus != null && NothingActiveRemains(r))
            {
                r.IsCancelled = true;
                var prev = r.StatusId; r.StatusId = cancelledRequestStatus.Id;
                _context.RequestStatusHistories.Add(new RequestStatusHistory
                {
                    RequestId = r.Id, ActorUserId = cmd.Actor.UserId, ActionTaken = "CANCELLED", PreviousStatusId = prev, NewStatusId = cancelledRequestStatus.Id,
                    CreatedAtUtc = now.AddMilliseconds(2),
                    Comment = $"Pedido cancelado: todos os itens foram encerrados após o cancelamento do(s) grupo(s) P.O. Motivo: {C.ReasonCodes.Label(reasonCode)} — {reason}",
                });
                requestCancelled = true;
                await _context.SaveChangesAsync();
            }
            else
            {
                await _statusSync.SyncStatusAsync(r.Id, cmd.Actor.UserId);
                await _context.SaveChangesAsync();
            }
            r.UpdatedAtUtc = now; r.UpdatedByUserId = cmd.Actor.UserId;

            // 9. Notification INSIDE the transaction (in-app persistence required; outbox rows commit with the action)
            var requestStatusCode = await _context.RequestStatuses.AsNoTracking().Where(s => s.Id == r.StatusId).Select(s => s.Code).FirstAsync();
            await EmitCancellationEventAsync(r, group, cancellation, batch, mainHistory.Id, cmd.Actor.UserId, actorName, "PO_GROUP_CANCELLED", requestStatusCode, now);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            var warning = requiresFinance ? await NotificationWarningAsync(cancellation, r) : null;
            if (warning != null)
                await _adminLog.WriteAsync("Warning", nameof(PoGroupCancellationService), "PO_CANCELLATION_NO_FINANCE_RECIPIENT",
                    $"Cancelamento do grupo P.O {group.Id} do pedido {r.RequestNumber}: nenhum utilizador Finance com acesso à planta foi encontrado; a confirmação da ECF continua disponível na fila de Finanças.",
                    payload: $"CancellationId: {cancellation.Id}. PlantId: {group.PlantId ?? r.PlantId}. CorrelationId: {mainHistory.Id}.");

            return Ok(new PoGroupCancellationResultDto
            {
                CancellationId = cancellation.Id, Kind = kind, RequestStatusCode = requestStatusCode, RequestCancelled = requestCancelled,
                FinanceConfirmationRequired = requiresFinance, FinanceRecipientsResolvedCount = cancellation.FinanceRecipientsResolvedCount,
                ReleasedLineCount = released, ClosedLineCount = closed, VoidedPoAttachments = voided, CancelledPlannedPayments = cancelledPayments,
                NotificationWarning = warning,
                Message = kind == C.Kinds.RestartQuotation
                    ? $"Grupo P.O cancelado. {released} item(ns) voltaram à cotação; será necessário um novo lote e novas aprovações de área e final." + (requiresFinance ? " O registo da nova P.O fica bloqueado até Finanças confirmar a anulação da ECF anterior." : "")
                    : $"Grupo P.O cancelado. {closed} item(ns) encerrados." + (requestCancelled ? " O pedido foi cancelado por não restarem itens ativos." : "") + (requiresFinance ? " Finanças deve confirmar a anulação da ECF." : ""),
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await tx.RollbackAsync();
            _logger.LogWarning(ex, "P.O group cancellation lost a concurrency race on group {GroupId}", cmd.GroupId);
            return Fail(409, C.ErrorCodes.CancellationConcurrentChange, "Conflito de concorrência",
                "Outra operação alterou este grupo P.O ao mesmo tempo. Nada foi gravado; atualize e verifique o estado atual.");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  Buyer: update the ECF declaration
    // ═══════════════════════════════════════════════════════════════════════════════════════

    public async Task<PoGroupCancellationOperationResult> UpdateEcfDeclarationAsync(UpdateEcfDeclarationCommand cmd)
    {
        var c = await LoadCancellationForWriteAsync(cmd.CancellationId, lockRow: false);
        if (c == null || !await InScopeAsync(c.RequestId, cmd.Actor)) return Fail(404, "NOT_FOUND", "Cancelamento não encontrado", "Cancelamento não encontrado ou fora do seu âmbito.");
        var auth = AuthorizeBuyer(c.Request, c.RequestPoGroup, cmd.Actor);
        if (auth != null) return auth;
        if (!c.FinanceConfirmationRequired)
            return Fail(409, C.ErrorCodes.FinanceConfirmationNotRequired, "Sem ECF a declarar", "Este grupo nunca teve P.O registada; não há declaração de ECF a atualizar.");
        if (c.FinanceConfirmationStatus == C.FinanceConfirmationStatuses.Confirmed)
            return Fail(409, C.ErrorCodes.DeclarationLocked, "Declaração fechada", "Finanças já confirmou a anulação da ECF; a declaração não pode ser alterada.");
        if (cmd.ExpectedRowVersion != null && !cmd.ExpectedRowVersion.SequenceEqual(c.RowVersion))
            return Fail(409, C.ErrorCodes.CancellationConcurrentChange, "Dados desatualizados", "O cancelamento foi alterado entretanto. Atualize antes de repetir.");

        var declared = cmd.EcfStatus?.Trim();
        if (declared == null || !C.EcfStatuses.Declarable.Contains(declared))
            return Fail(400, C.ErrorCodes.EcfDeclarationRequired, "Declaração inválida", "Indique PENDING_EXTERNAL, DECLARED_CANCELLED ou UNKNOWN.");
        var reference = cmd.EcfReference?.Trim();
        if (declared == C.EcfStatuses.DeclaredCancelled && string.IsNullOrWhiteSpace(reference))
            return Fail(400, C.ErrorCodes.EcfReferenceRequired, "Referência da ECF obrigatória", "Ao declarar que a ECF já foi anulada, indique a referência do documento anulado.");

        var now = DateTime.UtcNow;
        var actorName = await NameOfAsync(cmd.Actor.UserId);
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            c.EcfStatus = declared; c.EcfReference = reference; c.EcfDeclarationNote = Trunc(cmd.Note, C.MaxJustificationLength);
            if (cmd.EvidenceAttachmentId.HasValue) c.EcfEvidenceAttachmentId = cmd.EvidenceAttachmentId;
            c.EcfDeclaredByUserId = cmd.Actor.UserId; c.EcfDeclaredAtUtc = now;
            // A rejected declaration goes back to PENDING for a fresh Finance review; a pending one stays pending.
            c.FinanceConfirmationStatus = C.FinanceConfirmationStatuses.Pending;
            AddEcfEvent(c, new RequestPoGroupCancellationEcfEvent
            {
                EventType = C.EcfEventTypes.BuyerUpdated, EcfStatusAfter = declared, FinanceStatusAfter = c.FinanceConfirmationStatus,
                Reference = reference, Note = c.EcfDeclarationNote, EvidenceAttachmentId = cmd.EvidenceAttachmentId,
                ActorUserId = cmd.Actor.UserId, ActorRoleSnapshot = RoleSnapshot(cmd.Actor), AtUtc = now,
            });
            var history = new RequestStatusHistory
            {
                RequestId = c.RequestId, ActorUserId = cmd.Actor.UserId, ActionTaken = C.HistoryActions.EcfDeclared,
                PreviousStatusId = c.Request.StatusId, NewStatusId = c.Request.StatusId, CreatedAtUtc = now,
                Comment = $"Declaração do comprador sobre a ECF atualizada por {actorName}: {EcfLabel(declared, reference)}. {c.EcfDeclarationNote} (CancellationId: {c.Id})".Trim(),
            };
            _context.RequestStatusHistories.Add(history);
            await _context.SaveChangesAsync();

            var batch = c.RequestPoGroup.ApprovalBatch;
            var requestStatusCode = await _context.RequestStatuses.AsNoTracking().Where(s => s.Id == c.Request.StatusId).Select(s => s.Code).FirstAsync();
            await EmitCancellationEventAsync(c.Request, c.RequestPoGroup, c, batch, history.Id, cmd.Actor.UserId, actorName, "ECF_DECLARATION_UPDATED", requestStatusCode, now);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(await BuildDtoAsync(c, cmd.Actor));
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            return Fail(409, C.ErrorCodes.CancellationConcurrentChange, "Conflito de concorrência", "O cancelamento foi alterado ao mesmo tempo. Nada foi gravado.");
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  Buyer/SysAdmin: resend the cancellation notification (D13) — never repeats the cancellation
    // ═══════════════════════════════════════════════════════════════════════════════════════

    public async Task<PoGroupCancellationOperationResult> ResendNotificationAsync(Guid cancellationId, PoGroupCancellationActor actor)
    {
        var c = await LoadCancellationForWriteAsync(cancellationId, lockRow: false);
        if (c == null || !await InScopeAsync(c.RequestId, actor)) return Fail(404, "NOT_FOUND", "Cancelamento não encontrado", "Cancelamento não encontrado ou fora do seu âmbito.");
        var auth = AuthorizeBuyer(c.Request, c.RequestPoGroup, actor);
        if (auth != null) return auth;
        if (c.LastNotifiedAtUtc.HasValue && (DateTime.UtcNow - c.LastNotifiedAtUtc.Value).TotalMinutes < C.ResendMinimumIntervalMinutes)
            return Fail(409, C.ErrorCodes.ResendTooSoon, "Reenvio demasiado próximo",
                $"A notificação foi enviada há menos de {C.ResendMinimumIntervalMinutes} minutos. Aguarde antes de reenviar (evita envios duplicados).");

        var now = DateTime.UtcNow;
        var actorName = await NameOfAsync(actor.UserId);
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // A NEW history row = a NEW correlation id: the outbox/in-app dedup (which is per correlation + recipient)
            // does not suppress the resend, while a re-emission of the SAME action is still deduplicated.
            var history = new RequestStatusHistory
            {
                RequestId = c.RequestId, ActorUserId = actor.UserId, ActionTaken = C.HistoryActions.NotificationResent,
                PreviousStatusId = c.Request.StatusId, NewStatusId = c.Request.StatusId, CreatedAtUtc = now,
                Comment = $"Notificação do cancelamento do grupo P.O reenviada por {actorName} (reenvio nº {c.NotificationResendCount + 1}). (CancellationId: {c.Id})",
            };
            _context.RequestStatusHistories.Add(history);
            c.NotificationResendCount++;
            AddEcfEvent(c, new RequestPoGroupCancellationEcfEvent
            {
                EventType = C.EcfEventTypes.NotificationResent, EcfStatusAfter = c.EcfStatus, FinanceStatusAfter = c.FinanceConfirmationStatus,
                ActorUserId = actor.UserId, ActorRoleSnapshot = RoleSnapshot(actor), AtUtc = now,
            });
            await _context.SaveChangesAsync();
            var requestStatusCode = await _context.RequestStatuses.AsNoTracking().Where(s => s.Id == c.Request.StatusId).Select(s => s.Code).FirstAsync();
            await EmitCancellationEventAsync(c.Request, c.RequestPoGroup, c, c.RequestPoGroup.ApprovalBatch, history.Id, actor.UserId, actorName, "NOTIFICATION_RESENT", requestStatusCode, now);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            var dto = await BuildDtoAsync(c, actor);
            if (c.FinanceConfirmationRequired && c.FinanceEmailPolicyOutcome != PoCancellationRecipientOutcome.PolicyQueued)
                await _adminLog.WriteAsync("Warning", nameof(PoGroupCancellationService), "PO_CANCELLATION_NO_FINANCE_RECIPIENT",
                    $"Reenvio da notificação do cancelamento {c.Id}: resultado da política de e-mail para Finanças = {c.FinanceEmailPolicyOutcome}.", payload: $"CorrelationId: {history.Id}.");
            return Ok(dto);
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  Finance: confirm / reject (D12)
    // ═══════════════════════════════════════════════════════════════════════════════════════

    public async Task<PoGroupCancellationOperationResult> FinanceReviewAsync(FinanceReviewCommand cmd)
    {
        var note = Trunc(cmd.Note, C.MaxJustificationLength);
        if (!cmd.Confirm && (note == null || note.Length < C.MinJustificationLength))
            return Fail(400, C.ErrorCodes.JustificationRequired, "Nota obrigatória", $"Para rejeitar a declaração indique o motivo (mínimo {C.MinJustificationLength} caracteres).");

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // Row lock: serializes against RegisterPo's guard (UPDLOCK/HOLDLOCK on the same rows).
            var c = await LoadCancellationForWriteAsync(cmd.CancellationId, lockRow: true);
            if (c == null || !await InScopeAsync(c.RequestId, cmd.Actor)) { await tx.RollbackAsync(); return Fail(404, "NOT_FOUND", "Cancelamento não encontrado", "Cancelamento não encontrado ou fora do seu âmbito."); }

            var authBlock = await FinanceReviewBlockReasonAsync(c, cmd.Actor);
            if (authBlock != null) { await tx.RollbackAsync(); return authBlock; }
            if (cmd.ExpectedRowVersion != null && !cmd.ExpectedRowVersion.SequenceEqual(c.RowVersion))
            { await tx.RollbackAsync(); return Fail(409, C.ErrorCodes.CancellationConcurrentChange, "Dados desatualizados", "A declaração foi alterada entretanto. Atualize antes de decidir."); }

            var now = DateTime.UtcNow;
            var actorName = await NameOfAsync(cmd.Actor.UserId);
            c.FinanceConfirmationStatus = cmd.Confirm ? C.FinanceConfirmationStatuses.Confirmed : C.FinanceConfirmationStatuses.Rejected;
            c.FinanceReviewedByUserId = cmd.Actor.UserId; c.FinanceReviewedAtUtc = now; c.FinanceConfirmationNote = note;
            if (cmd.Confirm) { c.FinanceConfirmedByUserId = cmd.Actor.UserId; c.FinanceConfirmedAtUtc = now; }
            AddEcfEvent(c, new RequestPoGroupCancellationEcfEvent
            {
                EventType = cmd.Confirm ? C.EcfEventTypes.FinanceConfirmed : C.EcfEventTypes.FinanceRejected,
                EcfStatusAfter = c.EcfStatus, FinanceStatusAfter = c.FinanceConfirmationStatus, Note = note, Reference = c.EcfReference,
                ActorUserId = cmd.Actor.UserId, ActorRoleSnapshot = "FINANCE", AtUtc = now,
            });
            var history = new RequestStatusHistory
            {
                RequestId = c.RequestId, ActorUserId = cmd.Actor.UserId,
                ActionTaken = cmd.Confirm ? C.HistoryActions.FinanceConfirmed : C.HistoryActions.FinanceRejected,
                PreviousStatusId = c.Request.StatusId, NewStatusId = c.Request.StatusId, CreatedAtUtc = now,
                Comment = cmd.Confirm
                    ? $"Finanças ({actorName}) confirmou a anulação da ECF {c.EcfReference ?? "(sem referência)"} da P.O {c.PurchaseOrderNumberSnapshot ?? "—"} cancelada. {note} O registo de P.O de substituição para os itens afetados está desbloqueado. (CancellationId: {c.Id})".Trim()
                    : $"Finanças ({actorName}) NÃO confirmou a anulação da ECF {c.EcfReference ?? "(sem referência)"} da P.O {c.PurchaseOrderNumberSnapshot ?? "—"}: {note} A P.O anterior continua considerada ativa fora do Portal; o registo de P.O de substituição permanece bloqueado. (CancellationId: {c.Id})",
            };
            _context.RequestStatusHistories.Add(history);
            await _context.SaveChangesAsync();

            var requestStatusCode = await _context.RequestStatuses.AsNoTracking().Where(s => s.Id == c.Request.StatusId).Select(s => s.Code).FirstAsync();
            await _orchestrator.EmitAsync(new WorkflowEvent
            {
                EventCode = cmd.Confirm ? WorkflowEventCodes.PoCancellationConfirmed : WorkflowEventCodes.PoCancellationDeclarationRejected,
                RequestId = c.RequestId, RequestNumber = c.Request.RequestNumber ?? "S/N", RequestTitle = c.Request.Title ?? string.Empty,
                TargetStatusCode = requestStatusCode, ActionTaken = history.ActionTaken, ActorUserId = cmd.Actor.UserId, ActorName = actorName,
                Comment = note, CorrelationId = history.Id, OccurredAtUtc = now,
                RequesterId = c.Request.RequesterId, BuyerId = c.Request.BuyerId, DepartmentId = c.Request.DepartmentId, PlantId = c.RequestPoGroup.PlantId ?? c.Request.PlantId, CompanyId = c.Request.CompanyId,
                PoGroupId = c.RequestPoGroupId, PoCancellationId = c.Id, PoCancellationKind = c.Kind, PoCancellationReasonCode = c.ReasonCode,
                EcfStatus = c.EcfStatus, EcfReference = c.EcfReference, FinanceConfirmationRequired = true, PreviousPoNumber = c.PurchaseOrderNumberSnapshot,
                PreviousBuyerId = c.CancelledByUserId,
            });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(await BuildDtoAsync(c, cmd.Actor));
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            return Fail(409, C.ErrorCodes.CancellationConcurrentChange, "Conflito de concorrência", "A declaração foi alterada ao mesmo tempo. Nada foi gravado.");
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  Reads
    // ═══════════════════════════════════════════════════════════════════════════════════════

    public async Task<PoGroupCancellationDto?> GetAsync(Guid groupId, PoGroupCancellationActor actor)
    {
        var c = await _context.RequestPoGroupCancellations.AsNoTracking()
            .Include(x => x.Items).Include(x => x.EcfEvents)
            .Include(x => x.Request).Include(x => x.RequestPoGroup)
            .FirstOrDefaultAsync(x => x.RequestPoGroupId == groupId);
        return c == null ? null : await BuildDtoAsync(c, actor);
    }

    public async Task<PoGroupCancellationEligibilityDto> EvaluateEligibilityAsync(Guid groupId, PoGroupCancellationActor actor)
    {
        var group = await _context.RequestPoGroups.AsNoTracking()
            .Include(g => g.Payments).Include(g => g.Cancellation)
            .Include(g => g.Request).ThenInclude(r => r.RequestType)
            .Include(g => g.Request).ThenInclude(r => r.Status)
            .FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null) return new PoGroupCancellationEligibilityDto { CanCancel = false, BlockCode = "GROUP_NOT_FOUND", BlockMessage = "Grupo não encontrado." };
        var r = group.Request;
        var lines = await _context.RequestLineItems.AsNoTracking().Include(li => li.LineItemStatus)
            .Where(li => li.RequestPoGroupId == groupId).ToListAsync();
        var facts = await BuildFactsAsync(r, group, lines);
        var verdict = PoGroupCancellationEvaluator.Evaluate(facts);
        var auth = AuthorizeBuyer(r, group, actor);
        return new PoGroupCancellationEligibilityDto
        {
            CanCancel = verdict.CanCancel && auth == null,
            BlockCode = verdict.CanCancel ? auth?.Code : verdict.Code,
            BlockMessage = verdict.CanCancel ? auth?.Detail : verdict.Message,
            FinanceConfirmationWouldBeRequired = await RequiresFinanceConfirmationAsync(r, group),
            ActorAuthorizationBlockCode = auth?.Code,
        };
    }

    public async Task<FinancePoCancellationListDto> ListForFinanceAsync(PoGroupCancellationActor actor, string? status, int? plantId, string? search)
    {
        var scoped = await RequestAccessScope.ScopedRequestsAsync(_context, actor.UserId, actor.Roles.ToList());
        var scopedIds = scoped.Select(r => r.Id);
        var q = _context.RequestPoGroupCancellations.AsNoTracking()
            .Include(c => c.Request).ThenInclude(r => r.Company)
            .Include(c => c.Request).ThenInclude(r => r.Plant)
            .Include(c => c.RequestPoGroup).ThenInclude(g => g.Supplier)
            .Include(c => c.RequestPoGroup).ThenInclude(g => g.Plant)
            .Where(c => c.FinanceConfirmationRequired && scopedIds.Contains(c.RequestId));

        var all = await q.ToListAsync();
        var dto = new FinancePoCancellationListDto
        {
            PendingCount = all.Count(c => c.FinanceConfirmationStatus == C.FinanceConfirmationStatuses.Pending),
            RejectedCount = all.Count(c => c.FinanceConfirmationStatus == C.FinanceConfirmationStatuses.Rejected),
            ConfirmedCount = all.Count(c => c.FinanceConfirmationStatus == C.FinanceConfirmationStatuses.Confirmed),
        };
        IEnumerable<RequestPoGroupCancellation> items = all;
        status = string.IsNullOrWhiteSpace(status) ? "OPEN" : status.Trim().ToUpperInvariant();
        items = status switch
        {
            "OPEN" => items.Where(c => c.FinanceConfirmationStatus != C.FinanceConfirmationStatuses.Confirmed),
            "ALL" => items,
            _ => items.Where(c => c.FinanceConfirmationStatus == status),
        };
        if (plantId.HasValue) items = items.Where(c => (c.RequestPoGroup.PlantId ?? c.Request.PlantId) == plantId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            items = items.Where(c => (c.Request.RequestNumber ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                                     || (c.PurchaseOrderNumberSnapshot ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                                     || (c.RequestPoGroup.Supplier?.Name ?? c.RequestPoGroup.SupplierNameSnapshot ?? "").Contains(s, StringComparison.OrdinalIgnoreCase)
                                     || (c.EcfReference ?? "").Contains(s, StringComparison.OrdinalIgnoreCase));
        }
        var list = items.OrderBy(c => c.FinanceConfirmationStatus == C.FinanceConfirmationStatuses.Confirmed ? 1 : 0).ThenBy(c => c.CancelledAtUtc).ToList();
        var userIds = list.SelectMany(c => new[] { c.CancelledByUserId, c.EcfDeclaredByUserId ?? Guid.Empty, c.FinanceReviewedByUserId ?? Guid.Empty }).Where(id => id != Guid.Empty).Distinct().ToList();
        var names = await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        foreach (var c in list)
        {
            var block = await FinanceReviewBlockReasonAsync(c, actor);
            dto.Items.Add(new FinancePoCancellationListItemDto
            {
                CancellationId = c.Id, RequestId = c.RequestId, RequestNumber = c.Request.RequestNumber ?? "S/N", RequestTitle = c.Request.Title,
                RequestPoGroupId = c.RequestPoGroupId, SupplierName = c.RequestPoGroup.Supplier?.Name ?? c.RequestPoGroup.SupplierNameSnapshot,
                CompanyName = c.Request.Company?.Name, PlantName = c.RequestPoGroup.Plant?.Name ?? c.Request.Plant?.Name, PlantId = c.RequestPoGroup.PlantId ?? c.Request.PlantId,
                TotalAmountSnapshot = c.TotalAmountSnapshot, CurrencyCodeSnapshot = c.CurrencyCodeSnapshot, PurchaseOrderNumberSnapshot = c.PurchaseOrderNumberSnapshot,
                Kind = c.Kind, ReasonCode = c.ReasonCode, ReasonLabel = C.ReasonCodes.Label(c.ReasonCode), Reason = c.Reason,
                EcfStatus = c.EcfStatus, EcfReference = c.EcfReference,
                EcfDeclaredByName = c.EcfDeclaredByUserId.HasValue && names.TryGetValue(c.EcfDeclaredByUserId.Value, out var dn) ? dn : null, EcfDeclaredAtUtc = c.EcfDeclaredAtUtc,
                FinanceConfirmationStatus = c.FinanceConfirmationStatus, FinanceConfirmationNote = c.FinanceConfirmationNote,
                FinanceReviewAppliesToCurrentDeclaration = ResolveReviewChronology(c, c.FinanceReviewedByUserId.HasValue && names.TryGetValue(c.FinanceReviewedByUserId.Value, out var rn) ? rn : null).IsCurrent,
                PreviousFinanceReview = ResolveReviewChronology(c, c.FinanceReviewedByUserId.HasValue && names.TryGetValue(c.FinanceReviewedByUserId.Value, out var rn2) ? rn2 : null).Previous,
                CancelledAtUtc = c.CancelledAtUtc, CancelledByName = names.TryGetValue(c.CancelledByUserId, out var cn) ? cn : null,
                FinanceRecipientsResolvedCount = c.FinanceRecipientsResolvedCount, FinanceEmailPolicyOutcome = c.FinanceEmailPolicyOutcome, RowVersion = Convert.ToBase64String(c.RowVersion ?? Array.Empty<byte>()),
                CanReview = block == null && c.FinanceConfirmationStatus != C.FinanceConfirmationStatuses.Confirmed, ReviewBlockReason = block?.Detail,
            });
        }
        return dto;
    }

    public async Task<LastFinanceReturnDto?> GetLastFinanceReturnAsync(Guid groupId)
    {
        var g = await _context.RequestPoGroups.AsNoTracking().Where(x => x.Id == groupId)
            .Select(x => new { x.RequestId, x.LastFinanceReturnKind, x.LastFinanceReturnReasonCode, x.LastFinanceReturnedAtUtc, x.LastFinanceReturnedByUserId })
            .FirstOrDefaultAsync();
        if (g == null) return null;
        var history = await _context.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == g.RequestId && h.ActionTaken == "FINANCE_RETURN_ADJUSTMENT" && h.Comment != null && h.Comment.Contains(groupId.ToString()))
            .OrderByDescending(h => h.CreatedAtUtc)
            .Select(h => new { h.Comment, h.CreatedAtUtc, h.ActorUserId })
            .FirstOrDefaultAsync();
        if (history == null && g.LastFinanceReturnedAtUtc == null) return null;
        var byId = g.LastFinanceReturnedByUserId ?? history?.ActorUserId;
        var name = byId.HasValue ? await _context.Users.AsNoTracking().Where(u => u.Id == byId.Value).Select(u => u.FullName).FirstOrDefaultAsync() : null;
        return new LastFinanceReturnDto
        {
            Kind = g.LastFinanceReturnKind,
            KindLabel = C.FinanceReturnKinds.Label(g.LastFinanceReturnKind),
            ReasonCode = g.LastFinanceReturnReasonCode,
            Notes = ExtractMotivo(history?.Comment),
            ReturnedAtUtc = g.LastFinanceReturnedAtUtc ?? history?.CreatedAtUtc,
            ReturnedByName = name,
            Source = g.LastFinanceReturnKind != null ? "COLUMNS" : "HISTORY",
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════════════════════════

    private static PoGroupCancellationOperationResult Ok(object payload) => PoGroupCancellationOperationResult.Ok(payload);
    private static PoGroupCancellationOperationResult Fail(int status, string code, string title, string detail) => PoGroupCancellationOperationResult.Fail(status, code, title, detail);

    private async Task<bool> InScopeAsync(Guid requestId, PoGroupCancellationActor actor)
    {
        var scoped = await RequestAccessScope.ScopedRequestsAsync(_context, actor.UserId, actor.Roles.ToList());
        return await scoped.AnyAsync(r => r.Id == requestId);
    }

    private static PoGroupCancellationOperationResult? AuthorizeBuyer(Request r, RequestPoGroup group, PoGroupCancellationActor actor)
    {
        if (actor.Roles.Contains(RoleConstants.SystemAdministrator)) return null;
        if (!actor.Roles.Contains(RoleConstants.Buyer))
            return Fail(403, C.ErrorCodes.NotResponsibleBuyer, "Acesso negado", "Apenas o comprador responsável pelo pedido (ou um administrador) pode cancelar um grupo P.O.");
        if (r.BuyerId == actor.UserId || group.PoResponsibleBuyerId == actor.UserId) return null;
        return Fail(403, C.ErrorCodes.NotResponsibleBuyer, "Não é o comprador responsável",
            "Este pedido está atribuído a outro comprador. Utilize \"Assumir pedido de outro comprador\" para assumir a responsabilidade antes de cancelar o grupo P.O.");
    }

    /// <summary>D12: Finance role + plant scope + active, never SysAdmin alone, never the declarer.</summary>
    private async Task<PoGroupCancellationOperationResult?> FinanceReviewBlockReasonAsync(RequestPoGroupCancellation c, PoGroupCancellationActor actor)
    {
        if (!c.FinanceConfirmationRequired)
            return Fail(409, C.ErrorCodes.FinanceConfirmationNotRequired, "Confirmação não aplicável", "Este cancelamento não requer confirmação de Finanças (nunca houve P.O registada).");
        if (c.FinanceConfirmationStatus == C.FinanceConfirmationStatuses.Confirmed)
            return Fail(409, C.ErrorCodes.FinanceConfirmationAlreadyDone, "Já confirmado", "A anulação da ECF já foi confirmada por Finanças.");
        if (!actor.Roles.Contains(RoleConstants.Finance))
            return Fail(403, C.ErrorCodes.FinanceScopeRequired, "Perfil Finanças necessário",
                "Apenas utilizadores com o perfil Finanças e acesso à planta do grupo podem confirmar ou rejeitar a anulação da ECF. O perfil de administrador, por si só, não concede esta permissão.");
        var user = await _context.Users.AsNoTracking().Where(u => u.Id == actor.UserId).Select(u => new { u.IsActive }).FirstOrDefaultAsync();
        if (user == null || !user.IsActive)
            return Fail(403, C.ErrorCodes.FinanceScopeRequired, "Utilizador inativo", "Apenas utilizadores ativos com o perfil Finanças podem rever o cancelamento.");
        var hasFinanceRole = await _context.UserRoleAssignments.AsNoTracking().AnyAsync(ura => ura.UserId == actor.UserId && ura.Role.RoleName == RoleConstants.Finance);
        if (!hasFinanceRole)
            return Fail(403, C.ErrorCodes.FinanceScopeRequired, "Perfil Finanças necessário", "O perfil Finanças não está atribuído a este utilizador na base de dados.");
        var plantId = c.RequestPoGroup?.PlantId ?? c.Request?.PlantId;
        if (plantId.HasValue)
        {
            var inPlant = await _context.UserPlantScopes.AsNoTracking().AnyAsync(s => s.UserId == actor.UserId && s.PlantId == plantId.Value);
            if (!inPlant)
                return Fail(403, C.ErrorCodes.FinanceScopeRequired, "Planta fora do seu âmbito", "O grupo P.O pertence a uma planta fora do seu âmbito de acesso; apenas Finanças dessa planta pode confirmar.");
        }
        if (c.EcfDeclaredByUserId == actor.UserId || c.CancelledByUserId == actor.UserId)
            return Fail(403, C.ErrorCodes.SelfConfirmationForbidden, "Confirmação pela mesma pessoa",
                "A confirmação de Finanças tem de ser feita por uma pessoa diferente de quem declarou a anulação da ECF.");
        return null;
    }

    private async Task<Request?> LoadRequestForWriteAsync(Guid requestId) =>
        await _context.Requests
            .Include(x => x.RequestType).Include(x => x.Status)
            .Include(x => x.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(x => x.PoGroups).ThenInclude(g => g.Payments)
            .Include(x => x.PoGroups).ThenInclude(g => g.Supplier)
            .Include(x => x.PoGroups).ThenInclude(g => g.Currency)
            .Include(x => x.PoGroups).ThenInclude(g => g.Cancellation)
            .Include(x => x.ApprovalBatches).ThenInclude(b => b.Items)
            .Include(x => x.Attachments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == requestId);

    private async Task<RequestPoGroupCancellation?> LoadCancellationForWriteAsync(Guid id, bool lockRow)
    {
        if (lockRow && _context.Database.IsSqlServer())
        {
            var locked = await _context.RequestPoGroupCancellations
                .FromSqlRaw("SELECT * FROM [RequestPoGroupCancellations] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}", id)
                .Include(c => c.Items).Include(c => c.EcfEvents)
                .Include(c => c.Request).Include(c => c.RequestPoGroup).ThenInclude(g => g.ApprovalBatch)
                .FirstOrDefaultAsync();
            return locked;
        }
        return await _context.RequestPoGroupCancellations
            .Include(c => c.Items).Include(c => c.EcfEvents)
            .Include(c => c.Request).Include(c => c.RequestPoGroup).ThenInclude(g => g.ApprovalBatch)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    private async Task<PoGroupCancellationEvaluator.Input> BuildFactsAsync(Request r, RequestPoGroup group, List<RequestLineItem>? linesOverride = null)
    {
        var lines = (linesOverride ?? r.LineItems.Where(li => li.RequestPoGroupId == group.Id).ToList())
            .Select(li => new PoGroupCancellationEvaluator.LineFacts(li.Id, li.LineNumber, li.LineItemStatus?.Code, li.ReceivedQuantity, li.IsDeleted)).ToList();
        var payments = group.Payments.Select(p => new PoGroupCancellationEvaluator.PaymentFacts(p.PaymentType, p.PaymentStatus)).ToList();
        var hasActiveAllocation = await _context.OperationInvoiceAllocations.AsNoTracking()
            .AnyAsync(a => a.RequestPoGroupId == group.Id && a.OperationInvoice != null && a.OperationInvoice.VoidedAtUtc == null);
        // Linkage: a group that is not PENDING and has NO live line linked to it cannot be cancelled safely.
        var linkageInconsistent = group.Status != RequestConstants.PoGroupStatuses.Pending && !lines.Any(l => !l.IsDeleted);
        return new PoGroupCancellationEvaluator.Input(
            r.RequestType?.Code ?? string.Empty, r.Status?.Code ?? string.Empty, r.IsCancelled,
            group.Status, group.Cancellation != null, lines, payments,
            hasActiveAllocation, group.FiscalReceiptAttachmentId.HasValue, group.OperationalReceiptCompletedAtUtc.HasValue, linkageInconsistent);
    }

    private async Task<bool> RequiresFinanceConfirmationAsync(Request r, RequestPoGroup group)
    {
        var hasPoAttachment = await _context.RequestAttachments.AsNoTracking()
            .AnyAsync(a => a.RequestPoGroupId == group.Id && a.AttachmentTypeCode == RequestAttachment.TYPE_PO && !a.IsDeleted);
        var shortId = group.Id.ToString().Substring(0, 8);
        var hasRegisterHistory = await _context.RequestStatusHistories.AsNoTracking()
            .AnyAsync(h => h.RequestId == r.Id && RegisterPoActions.Contains(h.ActionTaken) && h.Comment != null && h.Comment.Contains(shortId));
        return PoGroupCancellationEvaluator.RequiresFinanceConfirmation(group.PurchaseOrderNumber, hasPoAttachment, hasRegisterHistory);
    }

    private static bool NothingActiveRemains(Request r)
    {
        var inFlight = new[]
        {
            RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
            RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, RequestConstants.ApprovalBatchStatuses.FinalAdjustment,
        };
        var allLinesClosed = r.LineItems.Where(li => !li.IsDeleted).All(li =>
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.CancelledAfterApproval
            || li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted
            || li.LineItemStatus?.Code == "CANCELLED");
        var noActiveBatch = !r.ApprovalBatches.Any(b => inFlight.Contains(b.Status));
        var noLiveGroup = !r.PoGroups.Any(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled);
        return allLinesClosed && noActiveBatch && noLiveGroup;
    }

    /// <summary>
    /// Emits PO_GROUP_CANCELLED INSIDE the caller's transaction: the in-app rows, the per-user outbox rows AND the Accounts Payable
    /// outbox row are database writes on the same scoped context and commit or roll back with the business change. No SMTP
    /// happens here; the outbox worker delivers after the commit, with its own retry budget.
    /// </summary>
    private async Task<WorkflowEvent> EmitCancellationEventAsync(Request r, RequestPoGroup group, RequestPoGroupCancellation c, ApprovalBatch? batch,
        Guid correlationId, Guid actorId, string actorName, string actionTaken, string requestStatusCode, DateTime now)
    {
        c.LastNotificationCorrelationId = correlationId; c.LastNotifiedAtUtc = now;
        c.FinanceRecipientsResolvedCount = 0; c.FinanceRecipientsWithEmailCount = 0; c.FinanceEmailsQueuedCount = 0; c.FinanceEmailPolicyOutcome = null;
        c.BuyerRecipientResolved = false; c.BuyerEmailQueued = false;
        var evt = new WorkflowEvent
        {
            EventCode = WorkflowEventCodes.PoGroupCancelled,
            RequestId = r.Id, RequestNumber = r.RequestNumber ?? "S/N", RequestTitle = r.Title ?? string.Empty,
            TargetStatusCode = requestStatusCode, ActionTaken = actionTaken, ActorUserId = actorId, ActorName = actorName,
            Comment = $"{C.ReasonCodes.Label(c.ReasonCode)} — {c.Reason}", CorrelationId = correlationId, OccurredAtUtc = now,
            RequesterId = r.RequesterId, BuyerId = r.BuyerId, AreaApproverId = r.AreaApproverId, FinalApproverId = r.FinalApproverId,
            DepartmentId = r.DepartmentId, PlantId = group.PlantId ?? r.PlantId, CompanyId = r.CompanyId,
            PoGroupId = group.Id, BatchNumber = batch?.BatchNumber,
            PoCancellationId = c.Id, PoCancellationKind = c.Kind, PoCancellationReasonCode = c.ReasonCode,
            EcfStatus = c.EcfStatus, EcfReference = c.EcfReference, FinanceConfirmationRequired = c.FinanceConfirmationRequired,
            PreviousPoNumber = c.PurchaseOrderNumberSnapshot, PreviousBuyerId = group.PoResponsibleBuyerId,
            Justification = c.Reason,
            OnRecipientsResolved = o =>
            {
                c.FinanceRecipientsResolvedCount = o.FinanceEligible; c.FinanceRecipientsWithEmailCount = o.FinanceWithEmail; c.FinanceEmailsQueuedCount = o.FinanceEmailQueued;
                c.FinanceEmailPolicyOutcome = o.FinanceEmailPolicy; c.BuyerRecipientResolved = o.BuyerResolved; c.BuyerEmailQueued = o.BuyerEmailQueued;
            },
        };
        await _orchestrator.EmitAsync(evt);
        return evt;
    }


    /// <summary>Human explanation of the LAST emission's Finance e-mail outcome and of failed deliveries, with the recovery path.</summary>
    private async Task<string?> NotificationWarningAsync(RequestPoGroupCancellation c, Request r)
    {
        var parts = new List<string>();
        switch (c.FinanceEmailPolicyOutcome)
        {
            case PoCancellationRecipientOutcome.PolicyNoEligibleUsers: parts.Add(NoFinanceRecipientWarning(r)); break;
            case PoCancellationRecipientOutcome.PolicyNoPlantInAppOnly: parts.Add("O grupo/pedido não tem planta definida: Finanças recebeu apenas o aviso interno (sem e-mail). Defina a planta do pedido ou peça a confirmação diretamente em Obrigações & Pagamentos → Cancelamentos a confirmar."); break;
            case PoCancellationRecipientOutcome.PolicyUsersWithoutEmail: parts.Add($"Os {c.FinanceRecipientsResolvedCount} utilizador(es) Finanças da planta não têm endereço de e-mail registado: receberam apenas o aviso interno. Corrija os endereços e use \"Reenviar notificação\"."); break;
        }
        if (c.LastNotificationCorrelationId.HasValue)
        {
            var failed = await _context.EmailOutbox.AsNoTracking()
                .Where(e => e.CorrelationId == c.LastNotificationCorrelationId.Value && (e.Status == "DEAD_LETTER" || e.Status == "FAILED"))
                .Select(e => new { e.RecipientEmail, e.Status }).ToListAsync();
            if (failed.Count > 0)
                parts.Add($"Entrega de e-mail com falha para: {string.Join(", ", failed.Select(f => $"{f.RecipientEmail} ({f.Status})"))}. Verifique Administração → Logs do Sistema (EMAIL_OUTBOX_*), corrija a causa e use \"Reenviar notificação\" (nova correlação; o cancelamento não é repetido).");
        }
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string NoFinanceRecipientWarning(Request r) =>
        $"Finanças não foi notificada por e-mail/aviso: nenhum utilizador ativo com o perfil Finanças tem acesso à planta deste grupo ({r.Plant?.Name ?? r.PlantId?.ToString() ?? "sem planta"}). " +
        "A confirmação da ECF continua disponível a qualquer utilizador Finanças da planta em Obrigações & Pagamentos → Cancelamentos a confirmar. Corrija o âmbito de planta dos utilizadores Finanças e use \"Reenviar notificação\".";

    /// <summary>
    /// v2.249.4 — the Finance review columns hold the LAST review (confirmation or rejection). After a rejection the Buyer
    /// updates the declaration, which returns the status to PENDING without clearing those columns (they are evidence).
    /// A review made BEFORE the current declaration (<c>FinanceReviewedAtUtc &lt; EcfDeclaredAtUtc</c>) therefore describes
    /// the PREVIOUS declaration: it is reported as history, never as the decision on the declaration now awaiting review.
    /// Pure over the row; the event table is not needed for the chronology.
    /// </summary>
    internal static (bool IsCurrent, PoGroupCancellationPreviousReviewDto? Previous) ResolveReviewChronology(RequestPoGroupCancellation c, string? reviewerName)
    {
        if (!c.FinanceReviewedAtUtc.HasValue) return (false, null);
        var isCurrent = !c.EcfDeclaredAtUtc.HasValue || c.FinanceReviewedAtUtc.Value >= c.EcfDeclaredAtUtc.Value;
        if (isCurrent) return (true, null);
        var previousStatus = c.FinanceConfirmedAtUtc.HasValue && c.FinanceConfirmedAtUtc.Value == c.FinanceReviewedAtUtc.Value
            ? C.FinanceConfirmationStatuses.Confirmed
            : C.FinanceConfirmationStatuses.Rejected;
        return (false, new PoGroupCancellationPreviousReviewDto
        {
            Status = previousStatus, ByName = reviewerName, AtUtc = c.FinanceReviewedAtUtc, Note = c.FinanceConfirmationNote,
        });
    }

    private async Task<PoGroupCancellationDto> BuildDtoAsync(RequestPoGroupCancellation c, PoGroupCancellationActor actor)
    {
        var ids = c.EcfEvents.Select(e => e.ActorUserId).Concat(new[] { c.CancelledByUserId, c.EcfDeclaredByUserId ?? Guid.Empty, c.FinanceConfirmedByUserId ?? Guid.Empty, c.FinanceReviewedByUserId ?? Guid.Empty })
            .Where(id => id != Guid.Empty).Distinct().ToList();
        var names = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        string? N(Guid? id) => id.HasValue && names.TryGetValue(id.Value, out var n) ? n : null;
        var lineIds = c.Items.Select(i => i.RequestLineItemId).ToList();
        var lines = await _context.RequestLineItems.AsNoTracking().Where(li => lineIds.Contains(li.Id)).Select(li => new { li.Id, li.LineNumber, li.Description }).ToDictionaryAsync(x => x.Id);
        var request = c.Request ?? await _context.Requests.AsNoTracking().FirstAsync(r => r.Id == c.RequestId);
        var group = c.RequestPoGroup ?? await _context.RequestPoGroups.AsNoTracking().FirstAsync(g => g.Id == c.RequestPoGroupId);
        var buyerAuth = AuthorizeBuyer(request, group, actor) == null;
        var financeBlock = await FinanceReviewBlockReasonAsync(c, actor);
        var open = c.FinanceConfirmationStatus is C.FinanceConfirmationStatuses.Pending or C.FinanceConfirmationStatuses.Rejected;
        var (reviewIsCurrent, previousReview) = ResolveReviewChronology(c, N(c.FinanceReviewedByUserId));
        return new PoGroupCancellationDto
        {
            Id = c.Id, RequestPoGroupId = c.RequestPoGroupId, RequestId = c.RequestId, Kind = c.Kind, ReasonCode = c.ReasonCode, ReasonLabel = C.ReasonCodes.Label(c.ReasonCode), Reason = c.Reason,
            PreviousGroupStatus = c.PreviousGroupStatus, PurchaseOrderNumberSnapshot = c.PurchaseOrderNumberSnapshot, TotalAmountSnapshot = c.TotalAmountSnapshot,
            CurrencyCodeSnapshot = c.CurrencyCodeSnapshot, ApprovalBatchNumberSnapshot = c.ApprovalBatchNumberSnapshot, OriginFinanceReturnHistoryId = c.OriginFinanceReturnHistoryId,
            CancelledByUserId = c.CancelledByUserId, CancelledByName = N(c.CancelledByUserId), CancelledAtUtc = c.CancelledAtUtc,
            FinanceConfirmationRequired = c.FinanceConfirmationRequired, EcfStatus = c.EcfStatus, EcfReference = c.EcfReference, EcfDeclarationNote = c.EcfDeclarationNote,
            EcfEvidenceAttachmentId = c.EcfEvidenceAttachmentId, EcfDeclaredByUserId = c.EcfDeclaredByUserId, EcfDeclaredByName = N(c.EcfDeclaredByUserId), EcfDeclaredAtUtc = c.EcfDeclaredAtUtc,
            FinanceConfirmationStatus = c.FinanceConfirmationStatus, FinanceConfirmedByUserId = c.FinanceConfirmedByUserId, FinanceConfirmedByName = N(c.FinanceConfirmedByUserId),
            FinanceConfirmedAtUtc = c.FinanceConfirmedAtUtc, FinanceConfirmationNote = c.FinanceConfirmationNote, FinanceReviewedByName = N(c.FinanceReviewedByUserId), FinanceReviewedAtUtc = c.FinanceReviewedAtUtc,
            FinanceReviewAppliesToCurrentDeclaration = reviewIsCurrent, PreviousFinanceReview = previousReview,
            FinanceRecipientsResolvedCount = c.FinanceRecipientsResolvedCount, FinanceRecipientsWithEmailCount = c.FinanceRecipientsWithEmailCount, FinanceEmailsQueuedCount = c.FinanceEmailsQueuedCount,
            FinanceEmailPolicyOutcome = c.FinanceEmailPolicyOutcome, BuyerRecipientResolved = c.BuyerRecipientResolved, BuyerEmailQueued = c.BuyerEmailQueued,
            LastNotificationCorrelationId = c.LastNotificationCorrelationId, LastNotifiedAtUtc = c.LastNotifiedAtUtc, NotificationResendCount = c.NotificationResendCount,
            NotificationWarning = c.FinanceConfirmationRequired ? await NotificationWarningAsync(c, request) : null,
            Delivery = await DeliveryAsync(c.LastNotificationCorrelationId),
            RowVersion = Convert.ToBase64String(c.RowVersion ?? Array.Empty<byte>()),
            Items = c.Items.Select(i => new PoGroupCancellationItemDto
            {
                RequestLineItemId = i.RequestLineItemId, Outcome = i.Outcome, PreviousLineItemStatusCode = i.PreviousLineItemStatusCode,
                LineNumber = lines.TryGetValue(i.RequestLineItemId, out var ln) ? ln.LineNumber : 0, Description = lines.TryGetValue(i.RequestLineItemId, out var ld) ? ld.Description : null,
            }).OrderBy(i => i.LineNumber).ToList(),
            Events = c.EcfEvents.OrderBy(e => e.AtUtc).Select(e => new PoGroupCancellationEcfEventDto
            {
                EventType = e.EventType, EcfStatusAfter = e.EcfStatusAfter, FinanceStatusAfter = e.FinanceStatusAfter, Reference = e.Reference, Note = e.Note,
                EvidenceAttachmentId = e.EvidenceAttachmentId, ActorUserId = e.ActorUserId, ActorName = N(e.ActorUserId), ActorRoleSnapshot = e.ActorRoleSnapshot, AtUtc = e.AtUtc,
            }).ToList(),
            Actions = new PoGroupCancellationActionsDto
            {
                CanUpdateDeclaration = buyerAuth && c.FinanceConfirmationRequired && open,
                CanResendNotification = buyerAuth,
                CanFinanceReview = financeBlock == null && open,
                FinanceReviewBlockReason = financeBlock?.Detail,
            },
        };
    }

    /// <summary>Outbox state of the LAST emission, per recipient (PENDING | PROCESSING | SENT | FAILED | DEAD_LETTER | EXPIRED).</summary>
    private async Task<List<NotificationDeliveryDto>> DeliveryAsync(Guid? correlationId)
    {
        if (!correlationId.HasValue) return new();
        return await _context.EmailOutbox.AsNoTracking().Where(e => e.CorrelationId == correlationId.Value)
            .OrderBy(e => e.RecipientEmail)
            .Select(e => new NotificationDeliveryDto { RecipientEmail = e.RecipientEmail, RecipientName = e.RecipientName, Status = e.Status, RetryCount = e.RetryCount, LastError = e.LastError, ProcessedAtUtc = e.ProcessedAtUtc, NextRetryAtUtc = e.NextRetryAtUtc })
            .ToListAsync();
    }

    private void AddFieldChange(Request r, RequestLineItem li, Guid actor, DateTime now, string field, string display, string? prev, string? next)
    {
        _context.RequestFieldChangeHistories.Add(new RequestFieldChangeHistory
        {
            RequestId = r.Id, ActorUserId = actor, FieldName = field, FieldDisplayName = $"{display} (linha #{li.LineNumber})",
            PreviousValue = prev, NewValue = next, StatusCodeAtChange = r.Status?.Code ?? string.Empty, LineItemId = li.Id, CreatedAtUtc = now,
        });
    }

    /// <summary>Children of an already-tracked parent carry a non-default Guid key, so EF would treat them as Modified;
    /// add them explicitly as new rows.</summary>
    private void AddEcfEvent(RequestPoGroupCancellation c, RequestPoGroupCancellationEcfEvent e)
    {
        e.RequestPoGroupCancellationId = c.Id;
        _context.RequestPoGroupCancellationEcfEvents.Add(e);
        c.EcfEvents.Add(e);
    }

    private async Task<string> NameOfAsync(Guid userId) =>
        await _context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync() ?? "Sistema";

    private static string RoleSnapshot(PoGroupCancellationActor actor) =>
        actor.Roles.Contains(RoleConstants.Buyer) ? "BUYER" : actor.Roles.Contains(RoleConstants.SystemAdministrator) ? "SYSTEM_ADMINISTRATOR" : "OTHER";

    private static string? Trunc(string? s, int max) { var t = s?.Trim(); if (string.IsNullOrEmpty(t)) return null; return t.Length <= max ? t : t.Substring(0, max); }

    /// <summary>Human label of a declaration; always phrased as the Buyer's statement, never as a Portal verification.</summary>
    public static string EcfLabel(string status, string? reference) => status switch
    {
        C.EcfStatuses.NotApplicable => "não aplicável (nenhuma P.O/ECF registada)",
        C.EcfStatuses.PendingExternal => $"comprador declara que a ECF {reference ?? ""} ainda NÃO foi anulada".Replace("  ", " "),
        C.EcfStatuses.DeclaredCancelled => $"comprador declara que a ECF {reference} JÁ foi anulada fora do Portal",
        C.EcfStatuses.Unknown => "comprador desconhece a situação da ECF",
        _ => status,
    };

    private static string? ExtractMotivo(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;
        var idx = comment.LastIndexOf("Motivo:", StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? comment.Trim() : comment.Substring(idx + "Motivo:".Length).Trim();
    }
}
