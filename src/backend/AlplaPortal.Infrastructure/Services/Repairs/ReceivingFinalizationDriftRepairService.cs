using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Repairs;

/// <summary>
/// v2.245.0 — controlled, tightly-scoped repair for the RECEIVING FINALIZATION DRIFT defect
/// (incident REQ-01/07/2026-013 class). A QUOTATION request whose receipt was registered on the winning
/// <see cref="QuotationItem"/> (status RECEIVED) can leave the matching <see cref="RequestLineItem"/>
/// unlinked (<c>SelectedQuotationItemId = NULL</c>) and un-advanced, so the group is stuck IN_FOLLOWUP.
///
/// <para>MINIMAL canonical correction (linkage + receiving-fact sync ONLY): populate the missing
/// <c>SelectedQuotationItemId</c> and mirror the winning item's ReceivedQuantity + RECEIVED status onto
/// the line item. It DOES NOT advance the group/request status and DOES NOT write a CONFIRM_RECEIVING
/// row — the operator still performs the explicit "Confirmar Recebimento". It never touches payment, PO
/// or approval data. Idempotent: only null links are filled; a non-null link is never overridden;
/// ambiguous line numbers are refused. Preview writes nothing.</para>
/// </summary>
public sealed class ReceivingFinalizationDriftRepairService
{
    private readonly ApplicationDbContext _context;
    private const string Received = "RECEIVED";
    public const string RepairActionCode = "RECEIVING_LINKAGE_REPAIR";

    public ReceivingFinalizationDriftRepairService(ApplicationDbContext context) => _context = context;

    public async Task<ReceivingFinalizationRepairResultDto> RunAsync(
        bool apply, Guid actorId, string? reason, CancellationToken ct = default)
    {
        var result = new ReceivingFinalizationRepairResultDto { Status = apply ? "APPLIED" : "PREVIEW" };

        var groups = await _context.RequestPoGroups
            .Where(g => g.Status == RequestConstants.PoGroupStatuses.InFollowup)
            .Include(g => g.Request!).ThenInclude(r => r.Quotations).ThenInclude(q => q.Items).ThenInclude(qi => qi.LineItemStatus)
            .Include(g => g.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(g => g.LineItems).ThenInclude(li => li.SelectedQuotationItem).ThenInclude(qi => qi!.LineItemStatus)
            .ToListAsync(ct);

        foreach (var group in groups)
        {
            var request = group.Request;
            if (request?.SelectedQuotationId == null) continue; // only the winning-quotation model is in scope
            result.Scanned++;

            var winningItems = request.Quotations
                .FirstOrDefault(q => q.Id == request.SelectedQuotationId.Value)?.Items?.ToList()
                ?? new List<QuotationItem>();

            var activeLines = group.LineItems.Where(li => !li.IsDeleted).ToList();
            bool groupHasRepair = false;

            foreach (var li in activeLines)
            {
                var row = new ReceivingRepairRowDto
                {
                    RequestNumber = request.RequestNumber ?? "",
                    PoGroupId = group.Id.ToString(),
                    LineNumber = li.LineNumber,
                    RequestLineItemId = li.Id.ToString(),
                    GroupStatus = group.Status,
                    LineItemStatus = li.LineItemStatus?.Code,
                };

                // Already linked or already received on the line → nothing to repair.
                if (li.SelectedQuotationItemId.HasValue)
                {
                    row.Decision = "ALREADY_HEALTHY"; row.Reason = "Já vinculado a um item de cotação."; row.QuotationItemId = li.SelectedQuotationItemId.ToString();
                    result.AlreadyHealthy++; result.Rows.Add(row); continue;
                }
                if (li.LineItemStatus?.Code == Received)
                {
                    row.Decision = "ALREADY_HEALTHY"; row.Reason = "Item já marcado como RECEBIDO na linha do pedido.";
                    result.AlreadyHealthy++; result.Rows.Add(row); continue;
                }

                var matches = winningItems.Where(qi => qi.LineNumber == li.LineNumber).ToList();
                if (matches.Count > 1)
                {
                    row.Decision = "AMBIGUOUS"; row.Reason = "Múltiplos itens da cotação vencedora com o mesmo número de linha.";
                    result.Ambiguous++; result.Rows.Add(row); continue;
                }
                var match = matches.Count == 1 ? matches[0] : null;
                if (match == null || match.LineItemStatus?.Code != Received)
                {
                    row.Decision = "REFUSED"; row.Reason = match == null
                        ? "Nenhum item da cotação vencedora corresponde a esta linha."
                        : "O item da cotação vencedora ainda não está RECEBIDO.";
                    row.QuotationItemId = match?.Id.ToString(); row.QuotationItemStatus = match?.LineItemStatus?.Code;
                    result.Refused++; result.Rows.Add(row); continue;
                }

                // Repairable: unambiguous, received winning item, unlinked line.
                row.QuotationItemId = match.Id.ToString();
                row.QuotationItemStatus = match.LineItemStatus?.Code;
                groupHasRepair = true;

                if (apply)
                {
                    li.SelectedQuotationItemId = match.Id;
                    li.ReceivedQuantity = match.ReceivedQuantity;
                    li.LineItemStatusId = match.LineItemStatusId;
                    li.UpdatedAtUtc = DateTime.UtcNow;
                    row.Decision = "REPAIRED"; row.Reason = "Vínculo restaurado e fatos de recebimento sincronizados a partir do item da cotação vencedora.";
                    result.Repaired++;
                }
                else
                {
                    row.Decision = "REPAIR"; row.Reason = "Vínculo ausente; item da cotação vencedora RECEBIDO — elegível para reparo.";
                    result.WouldRepair++;
                }
                result.Rows.Add(row);
            }

            if (groupHasRepair)
            {
                result.Eligible++;
                if (apply)
                {
                    // Technical audit ONLY — NOT a CONFIRM_RECEIVING (the user did not confirm). No status change.
                    var key = $"RECV_LINK_REPAIR:{group.Id}";
                    var exists = await _context.RequestStatusHistories.AnyAsync(h => h.IdempotencyKey == key, ct);
                    if (!exists)
                    {
                        _context.RequestStatusHistories.Add(new RequestStatusHistory
                        {
                            Id = Guid.NewGuid(),
                            RequestId = request.Id,
                            ActorUserId = actorId,
                            ActionTaken = RepairActionCode,
                            PreviousStatusId = request.StatusId,
                            NewStatusId = request.StatusId, // no status change
                            Comment = $"[Reparo de vínculo de recebimento | Grupo {group.SupplierNameSnapshot ?? group.Id.ToString().Substring(0, 8)}] " +
                                      $"Vínculo do item da cotação vencedora restaurado. Motivo: {reason}",
                            IdempotencyKey = key,
                            CreatedAtUtc = DateTime.UtcNow,
                        });
                    }
                }
            }
        }

        if (apply) await _context.SaveChangesAsync(ct);

        result.Message = apply
            ? $"Reparo aplicado: {result.Repaired} linha(s) em {result.Eligible} grupo(s). Confirmação de recebimento continua a cargo do operador."
            : $"Pré-visualização: {result.WouldRepair} linha(s) reparável(is) em {result.Eligible} grupo(s) de {result.Scanned} grupo(s) IN_FOLLOWUP examinado(s).";
        return result;
    }
}
