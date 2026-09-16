using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Repairs;

/// <summary>
/// v2.245.0 — controlled, tightly-scoped repair for the PAYMENT RECEIVING STATUS DRIFT defect
/// (READ-ONLY investigation cohort, incident REQ-06/07/2026-023 class). A historical backfill created
/// operational <see cref="RequestPoGroup"/> rows at <c>PENDING</c> for PAYMENT requests that were already
/// paid/completed at the request level (before the group model), so the group never inherited payment
/// completion. Backend receiving actions key on GROUP status and reject <c>PENDING</c>, while the scalar
/// workspace admits the request by its (advanced) scalar status — the group is therefore visible but not
/// receivable.
///
/// <para>CANONICAL correction (group status ONLY): promote the single operational group from
/// <c>PENDING</c> → <c>PAYMENT_COMPLETED</c> — exactly the state the current Finance pay flow
/// (<c>FinanceController.pay</c>) would have produced. It does NOT create a CONFIRM_RECEIVING row, does
/// NOT fabricate receipt or RequestPayment rows, does NOT modify payment amounts, PO, approval or
/// divergence data, and does NOT touch Request.Status (the scalar is already historically advanced).</para>
///
/// <para>Conservative &amp; idempotent: repairs ONLY when there is authoritative PAYMENT_COMPLETED history
/// evidence, exactly one operational (non-cancelled) group, and no contradicting payment ledger; refuses
/// otherwise. Divergence never excludes a candidate. Preview writes nothing.</para>
/// </summary>
public sealed class PaymentReceivingStatusDriftRepairService
{
    private readonly ApplicationDbContext _context;

    /// <summary>Technical repair audit action — deliberately NOT PAYMENT_COMPLETED and NOT CONFIRM_RECEIVING.</summary>
    public const string RepairActionCode = "RECEIVING_PAYMENT_GROUP_SYNC_REPAIR";
    private const string PaymentCompletedHistoryAction = "PAYMENT_COMPLETED";
    private const string DivergenceHistoryAction = "PAYMENT_DIVERGENCE_DETECTED";

    public PaymentReceivingStatusDriftRepairService(ApplicationDbContext context) => _context = context;

    public async Task<PaymentReceivingDriftRepairResultDto> RunAsync(
        bool apply, Guid actorId, string? reason, CancellationToken ct = default)
    {
        var result = new PaymentReceivingDriftRepairResultDto { Status = apply ? "APPLIED" : "PREVIEW" };

        // Population: PAYMENT requests that either still carry a PENDING operational group (the drift), or
        // already carry this repair's audit (so a repaired request re-scans and classifies ALREADY_HEALTHY —
        // idempotency §11). Nothing outside this population is touched.
        var requests = await _context.Requests
            .Where(r => r.RequestType.Code == RequestConstants.Types.Payment
                        && (r.PoGroups.Any(g => g.Status == RequestConstants.PoGroupStatuses.Pending)
                            || r.StatusHistories.Any(h => h.ActionTaken == RepairActionCode)))
            .Include(r => r.Status)
            .Include(r => r.PoGroups)
            .Include(r => r.Payments)
            .Include(r => r.StatusHistories)
            .ToListAsync(ct);

        foreach (var request in requests)
        {
            result.ScannedRequests++;

            var requestStatus = request.Status?.Code;
            var paymentHistoryFound = request.StatusHistories.Any(h => h.ActionTaken == PaymentCompletedHistoryAction);
            var divergencePresent = request.StatusHistories.Any(h => h.ActionTaken == DivergenceHistoryAction);
            var paymentRows = request.Payments.Count;
            // The ledger contradicts completion only when rows EXIST and NONE is COMPLETED. The legacy cohort
            // has 0 rows (predates the ledger) — absence is NOT a contradiction (§5).
            var contradictoryLedger = paymentRows > 0
                && !request.Payments.Any(p => p.PaymentStatus == RequestPayment.PaymentStatuses.Completed);

            // Non-cancelled groups are the "operational" set. PAYMENT is single-group by design (§7).
            var operationalGroups = request.PoGroups
                .Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled)
                .ToList();

            PaymentReceivingDriftRowDto NewRow(RequestPoGroup g) => new()
            {
                RequestNumber = request.RequestNumber ?? "",
                RequestId = request.Id.ToString(),
                PoGroupId = g.Id.ToString(),
                RequestStatus = requestStatus,
                GroupStatus = g.Status,
                PaymentHistoryFound = paymentHistoryFound,
                PaymentDivergencePresent = divergencePresent,
                RequestPaymentRows = paymentRows,
            };

            // Idempotent re-scan: single operational group already PAYMENT_COMPLETED with this repair's audit.
            var repairAuditExists = request.StatusHistories.Any(h => h.ActionTaken == RepairActionCode);
            if (operationalGroups.Count == 1
                && operationalGroups[0].Status == RequestConstants.PoGroupStatuses.PaymentCompleted
                && repairAuditExists)
            {
                var healthy = NewRow(operationalGroups[0]);
                healthy.Decision = "ALREADY_HEALTHY";
                healthy.Reason = "Grupo já em PAYMENT_COMPLETED (reparo anterior aplicado).";
                result.ScannedGroups++;
                result.AlreadyHealthy++;
                result.Rows.Add(healthy);
                continue;
            }

            // The groups we actually decide on: the PENDING operational group(s).
            var pendingGroups = operationalGroups
                .Where(g => g.Status == RequestConstants.PoGroupStatuses.Pending)
                .ToList();
            if (pendingGroups.Count == 0) continue; // nothing drifted here

            // Request cancelled → never promote.
            if (requestStatus == RequestConstants.Statuses.Cancelled)
            {
                foreach (var g in pendingGroups)
                {
                    var row = NewRow(g);
                    row.Decision = "REFUSED";
                    row.Reason = "Pedido cancelado.";
                    result.ScannedGroups++; result.Refused++; result.Rows.Add(row);
                }
                continue;
            }

            // Single-group invariant: more than one operational group is ambiguous (§7).
            if (operationalGroups.Count != 1)
            {
                foreach (var g in pendingGroups)
                {
                    var row = NewRow(g);
                    row.Decision = "AMBIGUOUS";
                    row.Reason = "Múltiplos grupos operacionais no pedido — PAYMENT é grupo único por design; identidade do grupo ambígua.";
                    result.ScannedGroups++; result.Ambiguous++; result.Rows.Add(row);
                }
                continue;
            }

            var group = pendingGroups[0];
            var single = NewRow(group);
            result.ScannedGroups++;

            // Authoritative PAYMENT_COMPLETED history evidence is mandatory — never promote on scalar alone (§4).
            if (!paymentHistoryFound)
            {
                single.Decision = "REFUSED";
                single.Reason = "Sem evidência histórica de PAYMENT_COMPLETED — não promovido apenas pelo status escalar.";
                result.Refused++; result.Rows.Add(single);
                continue;
            }

            // Payment ledger must not contradict completion (§5).
            if (contradictoryLedger)
            {
                single.Decision = "CONFLICTING";
                single.Reason = "Existem lançamentos de pagamento (RequestPayment) e nenhum está COMPLETED — contradiz a conclusão do pagamento.";
                result.Conflicting++; result.Rows.Add(single);
                continue;
            }

            // Eligible: single PENDING operational group with real PAYMENT_COMPLETED evidence, no contradiction.
            result.Eligible++;
            if (apply)
            {
                var previous = group.Status;
                group.Status = RequestConstants.PoGroupStatuses.PaymentCompleted;
                group.UpdatedAtUtc = DateTime.UtcNow;
                group.UpdatedByUserId = actorId;

                // Technical audit ONLY — NOT a CONFIRM_RECEIVING and NOT a PAYMENT_COMPLETED transition.
                // No Request.Status change. Idempotent via a stable per-group key.
                var key = $"RECV_PAY_GROUP_SYNC:{group.Id}";
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
                        NewStatusId = request.StatusId, // NO request status change
                        Comment = $"[Sincronização de status do grupo de recebimento (PAYMENT) | Grupo {group.SupplierNameSnapshot ?? group.Id.ToString().Substring(0, 8)}] " +
                                  $"Status do grupo corrigido de {previous} para {RequestConstants.PoGroupStatuses.PaymentCompleted}. " +
                                  $"Nenhuma ação de negócio (recebimento/pagamento) fabricada. Motivo: {reason}",
                        IdempotencyKey = key,
                        CreatedAtUtc = DateTime.UtcNow,
                    });
                }

                single.GroupStatus = group.Status;
                single.Decision = "REPAIRED";
                single.Reason = $"Status do grupo promovido de {previous} para PAYMENT_COMPLETED (estado que o fluxo de pagamento atual produziria).";
                result.Repaired++;
            }
            else
            {
                result.WouldRepair++;
                single.Decision = "REPAIR";
                single.Reason = "Grupo único PENDING com evidência de PAYMENT_COMPLETED e sem contradição — elegível para reparo.";
            }
            result.Rows.Add(single);
        }

        if (apply) await _context.SaveChangesAsync(ct);

        result.Message = apply
            ? $"Reparo aplicado: {result.Repaired} grupo(s) promovido(s) a PAYMENT_COMPLETED de {result.ScannedRequests} pedido(s) PAGAMENTO examinado(s). O recebimento passa a ser feito pelo operador normalmente."
            : $"Pré-visualização: {result.WouldRepair} grupo(s) reparável(is) de {result.ScannedRequests} pedido(s) PAGAMENTO examinado(s) ({result.Ambiguous} ambíguo(s), {result.Conflicting} conflitante(s), {result.Refused} recusado(s), {result.AlreadyHealthy} já saudável(is)).";
        return result;
    }
}
