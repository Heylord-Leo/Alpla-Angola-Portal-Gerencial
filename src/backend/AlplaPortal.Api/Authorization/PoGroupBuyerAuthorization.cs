using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SC = AlplaPortal.Domain.Constants.SupplierCreditConstants;

namespace AlplaPortal.Api.Authorization;

/// <summary>
/// Server-side Buyer authorization for the SUPPLIER CREDIT paths of a P.O. group (credit-eligibility, registration
/// with credit, keep / change / remove during a correction, release of an unapplied hold). Reuses the verified rules
/// of the cancellation service: System Administrator passes; otherwise the Buyer role, the canonical request scope
/// (<see cref="RequestAccessScope"/>) and the responsible Buyer (Request.BuyerId or group.PoResponsibleBuyerId).
/// Deliberately applied ONLY where credit is involved — the standard registration keeps today's role-only check
/// (separate work, see the plan §4.4).
/// </summary>
public static class PoGroupBuyerAuthorization
{
    public sealed record Verdict(bool Allowed, int Status, string? Code, string? Title, string? Detail)
    {
        public static Verdict Ok() => new(true, 200, null, null, null);
        public static Verdict Deny(int status, string code, string title, string detail) => new(false, status, code, title, detail);

        public ObjectResult ToResult()
        {
            var pd = new ProblemDetails { Title = Title, Detail = Detail, Status = Status };
            pd.Extensions["code"] = Code;
            return new ObjectResult(pd) { StatusCode = Status };
        }
    }

    /// <summary>Rules 1–4 of the plan: role → scope → group belongs to request → responsible Buyer.</summary>
    public static async Task<Verdict> EvaluateAsync(ApplicationDbContext context, Request request, RequestPoGroup group, Guid userId, IReadOnlyCollection<string> roles, bool allowCoordinators = false)
    {
        if (roles.Contains(RoleConstants.SystemAdministrator)) return Verdict.Ok();
        var coordinator = allowCoordinators && roles.Contains(RoleConstants.LocalManager);
        if (!roles.Contains(RoleConstants.Buyer) && !coordinator)
            return Verdict.Deny(403, SC.ErrorCodes.NotResponsibleBuyer, "Acesso negado", "Apenas o comprador responsável pelo pedido (ou um administrador) pode gerir o crédito em conta corrente de um grupo P.O.");

        var scoped = await RequestAccessScope.ScopedRequestsAsync(context, userId, roles.ToList());
        if (!await scoped.AnyAsync(r => r.Id == request.Id))
            return Verdict.Deny(403, SC.ErrorCodes.RequestOutOfScope, "Pedido fora do seu âmbito", "O pedido pertence a uma planta ou departamento fora do seu âmbito de acesso.");

        if (group.RequestId != request.Id)
            return Verdict.Deny(404, "GROUP_NOT_FOUND", "Grupo P.O não encontrado", "O grupo P.O não pertence a este pedido.");

        if (coordinator) return Verdict.Ok();
        if (request.BuyerId == userId || group.PoResponsibleBuyerId == userId) return Verdict.Ok();
        return Verdict.Deny(403, SC.ErrorCodes.NotResponsibleBuyer, "Não é o comprador responsável",
            "Este pedido está atribuído a outro comprador. Utilize \"Assumir pedido de outro comprador\" para assumir a responsabilidade antes de gerir o crédito em conta corrente.");
    }
}
