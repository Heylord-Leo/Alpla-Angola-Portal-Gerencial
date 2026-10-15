using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Finance;

/// <summary>
/// Loads the <see cref="LegacyFinanceReturnFacts"/> of ONE request with set-based, read-only queries and evaluates
/// them through <see cref="LegacyFinanceReturnEvaluator"/>. Shared by the Finance guards, RegisterPo, the payments
/// queue eligibility and the scoped repair, so every path judges the shape by the same rule.
/// </summary>
public static class LegacyFinanceReturnShape
{
    public static async Task<LegacyFinanceReturnVerdict> EvaluateAsync(ApplicationDbContext context, Guid requestId, CancellationToken ct = default)
        => LegacyFinanceReturnEvaluator.Evaluate(await LoadAsync(context, requestId, ct));

    /// <summary>Financial blocking decision (schedule / pay / advance) — see <see cref="LegacyFinanceReturnEvaluator.EvaluateFinancialBlock"/>.</summary>
    public static async Task<LegacyFinanceReturnBlock> EvaluateFinancialBlockAsync(ApplicationDbContext context, Guid requestId, CancellationToken ct = default)
        => LegacyFinanceReturnEvaluator.EvaluateFinancialBlock(await LoadAsync(context, requestId, ct));

    public static async Task<LegacyFinanceReturnFacts> LoadAsync(ApplicationDbContext context, Guid requestId, CancellationToken ct = default)
    {
        var scalar = await context.Requests.AsNoTracking()
            .Where(r => r.Id == requestId)
            .Select(r => r.Status != null ? r.Status.Code : null)
            .FirstOrDefaultAsync(ct);
        var liveGroups = await context.RequestPoGroups.AsNoTracking()
            .Where(g => g.RequestId == requestId && g.Status != RequestConstants.PoGroupStatuses.Cancelled)
            .Select(g => new LegacyFinanceReturnGroupFact(g.Id, g.Status))
            .ToListAsync(ct);
        var latestReturn = await context.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == requestId && h.ActionTaken == LegacyFinanceReturnEvaluator.FinanceReturnAction)
            .MaxAsync(h => (DateTime?)h.CreatedAtUtc, ct);
        var latestRegistration = await context.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == requestId && LegacyFinanceReturnEvaluator.PoRegistrationActions.Contains(h.ActionTaken))
            .MaxAsync(h => (DateTime?)h.CreatedAtUtc, ct);
        var livePayments = await context.RequestPayments.AsNoTracking()
            .CountAsync(p => p.RequestId == requestId && p.PaymentStatus != Domain.Entities.RequestPayment.PaymentStatuses.Cancelled, ct);
        return new LegacyFinanceReturnFacts(requestId, scalar, liveGroups, latestReturn, latestRegistration, livePayments);
    }
}
