using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Purchasing;

public enum PoCorrectionClaimOutcome
{
    Claimed,
    /// <summary>An owned group re-assigned by a coordinator (same authority as request-level reassignment).</summary>
    Transferred,
    AlreadyOwnedByClaimant,
    OwnedByAnother,
    Ambiguous,
    NoCorrectionGroup,
    GroupNotFound,
    GroupNotEligible,
    ConcurrencyConflict
}

public sealed record PoCorrectionClaimResult(
    PoCorrectionClaimOutcome Outcome,
    Guid? GroupId,
    Guid? OwnerId,
    IReadOnlyList<Guid> CandidateGroupIds,
    Guid? HistoryId,
    string Message);

/// <summary>
/// docs §G.20 D2 — claim of a P.O. correction returned by Finance on a PAYMENT request. A PAYMENT request carries no
/// <c>Request.BuyerId</c>; its correction ownership is the returned group's <c>PoResponsibleBuyerId</c>
/// (<see cref="Domain.Services.PersonalPoCorrectionPredicate"/>, the rule shared by "Para Minha Ação", the dashboard
/// count and the Buyer queue). This service persists exactly that:
/// <list type="bullet">
///   <item>only a <c>WAITING_PO_CORRECTION</c> group of THIS request is claimable; a group id of another request is
///   refused; a sibling group is never chosen implicitly — several correction groups without an explicit id are
///   <see cref="PoCorrectionClaimOutcome.Ambiguous"/>;</item>
///   <item>a Buyer never overwrites an existing owner (<see cref="PoCorrectionClaimOutcome.OwnedByAnother"/>); a
///   coordinator (<paramref name="allowTransfer"/>, the same SysAdmin/LocalManager authority that reassigns a QUOTATION's
///   Buyer) may transfer an owned group (<see cref="PoCorrectionClaimOutcome.Transferred"/>, previous owner recorded in
///   the history); claiming a group one already owns is idempotent;</item>
///   <item><c>Request.BuyerId</c> is never touched;</item>
///   <item>the group's <c>rowversion</c> arbitrates concurrent claims: the second writer gets
///   <see cref="PoCorrectionClaimOutcome.ConcurrencyConflict"/> and nothing of its attempt is persisted.</item>
/// </list>
/// Authorization and access scope are the caller's responsibility (the controller checks role and scope first).
/// </summary>
public sealed class PoCorrectionClaimService
{
    /// <summary>Same history action as the request-level Buyer assignment, so timelines read consistently.</summary>
    public const string ClaimAction = "COMPRADOR_ATRIBUIDO";

    private readonly ApplicationDbContext _context;

    /// <summary>Test-only synchronization point between staging the claim and SaveChanges (deterministic races). Never set by production code.</summary>
    public Func<Task>? BeforeSaveHookForTests { get; set; }

    public PoCorrectionClaimService(ApplicationDbContext context) => _context = context;

    public async Task<PoCorrectionClaimResult> ClaimAsync(Guid requestId, Guid? poGroupId, Guid newOwnerId, Guid actorId, string? newOwnerName, bool allowTransfer = false, CancellationToken ct = default)
    {
        var groups = await _context.RequestPoGroups
            .Where(g => g.RequestId == requestId && g.Status != RequestConstants.PoGroupStatuses.Cancelled)
            .ToListAsync(ct);
        var corrections = groups.Where(g => g.Status == RequestConstants.PoGroupStatuses.WaitingPoCorrection).ToList();

        RequestPoGroup target;
        if (poGroupId.HasValue)
        {
            var named = groups.FirstOrDefault(g => g.Id == poGroupId.Value);
            if (named == null)
                return Fail(PoCorrectionClaimOutcome.GroupNotFound, null, "Grupo P.O não encontrado neste pedido.");
            if (named.Status != RequestConstants.PoGroupStatuses.WaitingPoCorrection)
                return Fail(PoCorrectionClaimOutcome.GroupNotEligible, named.Id, "O grupo indicado não está devolvido para correção de P.O.");
            target = named;
        }
        else
        {
            if (corrections.Count == 0)
                return Fail(PoCorrectionClaimOutcome.NoCorrectionGroup, null, "Este pedido não tem nenhuma P.O devolvida para correção.");
            if (corrections.Count > 1)
                return new PoCorrectionClaimResult(PoCorrectionClaimOutcome.Ambiguous, null, null, corrections.Select(g => g.Id).ToList(), null,
                    "Este pedido possui várias correções de P.O pendentes. Indique o grupo (poGroupId) a assumir.");
            target = corrections[0];
        }

        if (target.PoResponsibleBuyerId == newOwnerId)
            return new PoCorrectionClaimResult(PoCorrectionClaimOutcome.AlreadyOwnedByClaimant, target.Id, newOwnerId, Array.Empty<Guid>(), null,
                "O comprador já é responsável por esta correção de P.O.");
        var previousOwnerId = target.PoResponsibleBuyerId;
        if (previousOwnerId.HasValue && !allowTransfer)
            return new PoCorrectionClaimResult(PoCorrectionClaimOutcome.OwnedByAnother, target.Id, previousOwnerId, Array.Empty<Guid>(), null,
                "Esta correção de P.O já tem um comprador responsável. O reencaminhamento só é permitido por coordenadores.");

        var request = await _context.Requests.AsNoTracking().Where(r => r.Id == requestId).Select(r => new { r.StatusId }).FirstAsync(ct);
        string? previousOwnerName = null;
        if (previousOwnerId.HasValue)
            previousOwnerName = await _context.Users.AsNoTracking().Where(u => u.Id == previousOwnerId.Value).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        var now = DateTime.UtcNow;
        target.PoResponsibleBuyerId = newOwnerId;
        target.UpdatedAtUtc = now;
        target.UpdatedByUserId = actorId;

        var prefix = $"[Grupo P.O.: {target.SupplierNameSnapshot} | GroupId: {target.Id.ToString().Substring(0, 8)}] ";
        var comment = previousOwnerId.HasValue
            ? $"A correção da P.O devolvida por Finanças foi reatribuída ao comprador {newOwnerName} (anterior: {previousOwnerName ?? previousOwnerId.Value.ToString()})."
            : newOwnerId == actorId
                ? "O comprador assumiu a responsabilidade pela correção da P.O devolvida por Finanças."
                : $"A correção da P.O devolvida por Finanças foi atribuída ao comprador {newOwnerName}.";
        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = ClaimAction,
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = prefix + comment,
            CreatedAtUtc = now
        };
        _context.RequestStatusHistories.Add(history);

        if (BeforeSaveHookForTests != null) await BeforeSaveHookForTests();

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another claim won the group's rowversion race. Nothing of this attempt is persisted; the caller
            // reports the conflict and the first owner stands.
            _context.Entry(history).State = EntityState.Detached;
            _context.Entry(target).State = EntityState.Detached;
            return Fail(PoCorrectionClaimOutcome.ConcurrencyConflict, target.Id, "Esta correção de P.O acabou de ser assumida por outro comprador. Atualize a lista.");
        }

        return new PoCorrectionClaimResult(previousOwnerId.HasValue ? PoCorrectionClaimOutcome.Transferred : PoCorrectionClaimOutcome.Claimed, target.Id, newOwnerId, Array.Empty<Guid>(), history.Id,
            previousOwnerId.HasValue ? "Correção de P.O reatribuída com sucesso." : newOwnerId == actorId ? "Correção de P.O assumida com sucesso." : "Correção de P.O atribuída com sucesso.");
    }

    private static PoCorrectionClaimResult Fail(PoCorrectionClaimOutcome outcome, Guid? groupId, string message)
        => new(outcome, groupId, null, Array.Empty<Guid>(), null, message);
}
