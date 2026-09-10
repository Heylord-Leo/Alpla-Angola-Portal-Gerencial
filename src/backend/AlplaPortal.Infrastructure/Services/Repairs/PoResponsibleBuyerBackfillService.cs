using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Repairs;

/// <summary>
/// v2.242.0 — conservative, PAYMENT-only historical backfill of
/// <c>RequestPoGroup.PoResponsibleBuyerId</c>. PAYMENT requests carry no <c>Request.BuyerId</c>, so a
/// returned PAYMENT P.O. correction had no durable personal owner before this field existed. This
/// service assigns one ONLY when the evidence is trustworthy — never a guess.
///
/// <para>Scope is strictly: <c>RequestType == PAYMENT</c> AND the group has a registered P.O.
/// (<c>PurchaseOrderNumber != null</c>) AND <c>PoResponsibleBuyerId IS NULL</c>. QUOTATION groups are
/// deliberately never backfilled — their correction ownership already comes from
/// <c>Request.BuyerId</c>. The candidate is the STRUCTURED <c>UpdatedByUserId</c> (the field RegisterPo
/// stamps, proven correct for the live case) — history comments (truncated, ~85% coverage) are used
/// only as secondary corroboration, never as the primary source. A candidate is accepted only if it
/// resolves to an active user WITH the Buyer role; otherwise the row is left NULL (unresolved). Apply
/// is idempotent (never overwrites a non-null value) and transactional.</para>
/// </summary>
public sealed class PoResponsibleBuyerBackfillService
{
    private readonly ApplicationDbContext _context;
    public PoResponsibleBuyerBackfillService(ApplicationDbContext context) => _context = context;

    public Task<PoResponsibleBuyerBackfillResult> PreviewAsync(CancellationToken ct = default)
        => RunAsync(apply: false, actorId: null, reason: null, ct);

    public Task<PoResponsibleBuyerBackfillResult> ApplyAsync(Guid actorId, string reason, CancellationToken ct = default)
        => RunAsync(apply: true, actorId, reason, ct);

    private sealed record Candidate(
        string RequestNumber, Guid PoGroupId, string? PurchaseOrderNumber, string? SupplierName,
        Guid? UpdatedByUserId, Guid GroupId);

    private async Task<PoResponsibleBuyerBackfillResult> RunAsync(bool apply, Guid? actorId, string? reason, CancellationToken ct)
    {
        var result = new PoResponsibleBuyerBackfillResult
        {
            Status = apply ? PoResponsibleBuyerBackfillResult.Statuses.Applied : PoResponsibleBuyerBackfillResult.Statuses.Preview
        };

        if (apply && string.IsNullOrWhiteSpace(reason))
        {
            result.Status = PoResponsibleBuyerBackfillResult.Statuses.Refused;
            result.Message = "A justificativa (reason) é obrigatória para aplicar o backfill.";
            return result;
        }

        // Strict candidate scope: PAYMENT, has a P.O., not yet owned.
        var groups = await _context.RequestPoGroups
            .Where(g => g.Request.RequestType!.Code == RequestConstants.Types.Payment
                        && g.PurchaseOrderNumber != null
                        && g.PoResponsibleBuyerId == null)
            .Select(g => new
            {
                g.Id,
                g.RequestId,
                Number = g.Request.RequestNumber,
                g.PurchaseOrderNumber,
                g.SupplierNameSnapshot,
                g.UpdatedByUserId
            })
            .ToListAsync(ct);

        result.Scanned = groups.Count;
        if (groups.Count == 0)
        {
            result.Message = "Nenhum grupo P.O. de PAGAMENTO elegível (com P.O. e sem responsável).";
            return result;
        }

        // Resolve the distinct candidate users once: active + Buyer role.
        var candidateUserIds = groups.Where(g => g.UpdatedByUserId != null).Select(g => g.UpdatedByUserId!.Value).Distinct().ToList();
        var buyerUsers = await _context.Users
            .Where(u => candidateUserIds.Contains(u.Id) && u.IsActive
                        && u.UserRoleAssignments.Any(a => a.Role.RoleName == RoleConstants.Buyer))
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync(ct);
        var buyerById = buyerUsers.ToDictionary(u => u.Id, u => u.FullName);

        // History corroboration: latest REGISTER_PO/REREGISTER_PO actor per request whose comment names
        // the group (truncated 8-char id). Secondary evidence only — never the primary source.
        var requestNumbers = groups.Select(g => g.Number).Distinct().ToList();

        foreach (var g in groups)
        {
            var row = new PoResponsibleBuyerBackfillRow
            {
                RequestNumber = g.Number ?? string.Empty,
                PoGroupId = g.Id,
                RequestType = RequestConstants.Types.Payment,
                PurchaseOrderNumber = g.PurchaseOrderNumber,
                SupplierName = g.SupplierNameSnapshot
            };

            if (g.UpdatedByUserId == null || !buyerById.TryGetValue(g.UpdatedByUserId.Value, out var name))
            {
                row.Decision = "Unresolved";
                row.EvidenceSource = "None";
                row.Reason = g.UpdatedByUserId == null
                    ? "Sem UpdatedByUserId estruturado."
                    : "UpdatedByUserId não resolve para um utilizador ativo com o papel Comprador.";
                result.Unresolved++;
                result.Rows.Add(row);
                continue;
            }

            // Secondary corroboration: does a REGISTER_PO/REREGISTER_PO history row for this request,
            // naming this group's short id, share the same actor? (Absence does not veto — UpdatedBy is
            // authoritative — but a CONFLICTING actor downgrades to unresolved to stay conservative.)
            var shortId = g.Id.ToString().Substring(0, 8);
            var histActor = await _context.RequestStatusHistories
                .Where(h => h.RequestId == g.RequestId
                            && (h.ActionTaken == "REGISTER_PO" || h.ActionTaken == "REREGISTER_PO")
                            && h.Comment != null && h.Comment.Contains(shortId))
                .OrderByDescending(h => h.CreatedAtUtc)
                .Select(h => (Guid?)h.ActorUserId)
                .FirstOrDefaultAsync(ct);

            if (histActor.HasValue && histActor.Value != g.UpdatedByUserId.Value)
            {
                row.Decision = "Conflicting";
                row.EvidenceSource = "UpdatedByUserId≠HistoryActor";
                row.CandidateBuyerId = g.UpdatedByUserId;
                row.CandidateBuyerName = name;
                row.Reason = "O último registrante no histórico difere de UpdatedByUserId — mantido NULL por segurança.";
                result.Conflicting++;
                result.Rows.Add(row);
                continue;
            }

            row.CandidateBuyerId = g.UpdatedByUserId;
            row.CandidateBuyerName = name;
            row.EvidenceSource = histActor.HasValue ? "UpdatedByUserId+HistoryCorroborated" : "UpdatedByUserId";
            row.Decision = apply ? "AlreadyAssigned" : "WouldAssign"; // overwritten just below for apply
            result.Eligible++;

            if (apply)
            {
                var entity = await _context.RequestPoGroups.FirstOrDefaultAsync(x => x.Id == g.Id, ct);
                if (entity != null && entity.PoResponsibleBuyerId == null) // idempotent: never overwrite
                {
                    entity.PoResponsibleBuyerId = g.UpdatedByUserId;
                    row.Decision = "WouldAssign";
                    result.Assigned++;
                }
                else
                {
                    row.Decision = "AlreadyAssigned";
                    result.AlreadyAssigned++;
                }
            }
            else
            {
                result.WouldAssign++;
            }

            result.Rows.Add(row);
        }

        if (apply && result.Assigned > 0)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        result.Message = apply
            ? $"Backfill aplicado: {result.Assigned} grupo(s) atribuído(s), {result.Unresolved} não resolvido(s), {result.Conflicting} conflituoso(s)."
            : $"Pré-visualização: {result.WouldAssign} atribuiria(m), {result.Unresolved} não resolvido(s), {result.Conflicting} conflituoso(s).";
        return result;
    }
}
