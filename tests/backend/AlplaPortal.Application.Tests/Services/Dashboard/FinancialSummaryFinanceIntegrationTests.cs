using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using AlplaPortal.Infrastructure.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// B7.1 — Em processamento financeiro (B3 reconciliation) and Pago / aguardando recebimento (actual-paid
/// evidence) against the REAL EF provider (LocalDB). The canonical B3 finance obligation projection cannot
/// materialize under EF in-memory (double PoGroups ThenInclude), so these are verified here. Skips
/// gracefully when the integration DB is unavailable.
/// </summary>
[Collection("IntegrationTests")]
public class FinancialSummaryFinanceIntegrationTests
{
    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    [Fact]
    public async Task Finance_processing_reconciles_with_B3_and_paid_uses_actual_evidence()
    {
        if (!CanConnect()) return;

        await using var ctx = new ApplicationDbContext(Options());
        var actor = await ctx.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == RequestConstants.Types.Quotation).Select(t => t.Id).FirstOrDefaultAsync();
        var statusId = await ctx.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.PoIssued).Select(s => s.Id).FirstOrDefaultAsync();
        if (actor == Guid.Empty || typeId == 0 || statusId == 0) return;

        var req = new Request
        {
            Id = Guid.NewGuid(), Title = "ZZTEST_FIN_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZF-" + Guid.NewGuid().ToString("N")[..10],
            StatusId = statusId, RequestTypeId = typeId, RequesterId = actor,
            DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1, CreatedAtUtc = DateTime.UtcNow,
        };
        // Processing: PO_ISSUED (NEEDS_SCHEDULING) + PAYMENT_SCHEDULED (NEEDS_PAYMENT).
        var gProc1 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, Status = RequestConstants.PoGroupStatuses.PoIssued, TotalAmount = 100m, CurrencyCode = "AOA" };
        var gProc2 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, Status = RequestConstants.PoGroupStatuses.PaymentScheduled, TotalAmount = 200m, CurrencyCode = "AOA" };
        // Paid handoff: PAYMENT_COMPLETED with completed ADVANCE (3M) + FINAL_BALANCE (7M) + a REFUND (2M)
        // + a SCHEDULED (99M) that must all be treated correctly.
        var gPaid = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, Status = RequestConstants.PoGroupStatuses.PaymentCompleted, TotalAmount = 10m, CurrencyCode = "AOA" };
        req.PoGroups.Add(gProc1); req.PoGroups.Add(gProc2); req.PoGroups.Add(gPaid);
        ctx.Requests.Add(req);

        int seq = 0;
        RequestPayment Pay(Guid gid, string type, string status, decimal amt, string cur) => new()
        {
            RequestId = req.Id, RequestPoGroupId = gid, PaymentType = type,
            PaymentStatus = status, PlannedAmount = amt, ActualPaidAmount = amt, CurrencyCode = cur,
            PaymentSequence = ++seq, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor,
        };
        ctx.RequestPayments.Add(Pay(gPaid.Id, RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Completed, 3_000_000m, "AOA"));
        ctx.RequestPayments.Add(Pay(gPaid.Id, RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Completed, 7_000_000m, "AOA"));
        ctx.RequestPayments.Add(Pay(gPaid.Id, RequestPayment.PaymentTypes.Refund, RequestPayment.PaymentStatuses.Completed, 2_000_000m, "AOA"));       // excluded
        ctx.RequestPayments.Add(Pay(gPaid.Id, RequestPayment.PaymentTypes.Regularization, RequestPayment.PaymentStatuses.Scheduled, 99_000_000m, "AOA")); // excluded (not completed)
        await ctx.SaveChangesAsync();

        try
        {
            var scoped = ctx.Requests.Where(r => r.Id == req.Id);
            var projection = new FinancialSummaryProjection(ctx, new FinancePaymentEligibilityService());
            var categories = await projection.BuildAsync(scoped, DateTime.UtcNow.Date);

            var proc = categories.Single(c => c.Code == FinancialCategories.EmProcessamentoFinanceiro);
            var paid = categories.Single(c => c.Code == FinancialCategories.PagoAguardandoRecebimento);

            // Reconcile with B3: same NEEDS_SCHEDULING + NEEDS_PAYMENT population.
            var b3 = await new FinanceObligationSummaryProjection(new FinancePaymentEligibilityService()).BuildAsync(scoped, null, null, null, DateTime.UtcNow.Date);
            var b3ProcGroups = b3.Obligations.Where(o => o.ActionClass == FinanceActionClasses.NeedsScheduling || o.ActionClass == FinanceActionClasses.NeedsPayment)
                .Select(o => o.RequestPoGroupId).Distinct().Count();
            Assert.Equal(b3ProcGroups, proc.EntityCount);
            Assert.Equal(2, proc.EntityCount); // the two processing groups
            Assert.Equal(300m, proc.Currencies.Single(c => c.CurrencyCode == "AOA").Amount); // 100 + 200 group amounts

            // Paid = actual completed owed-money evidence: 3M + 7M = 10M; refund + scheduled excluded.
            Assert.Equal(1, paid.EntityCount);          // one paid group
            Assert.Equal(1, paid.RequestCount);         // one request (not 2 payment rows)
            var aoaPaid = paid.Currencies.Single(c => c.CurrencyCode == "AOA");
            Assert.Equal(10_000_000m, aoaPaid.Amount);  // NOT 12M (refund), NOT 99M (scheduled), NOT group 10
        }
        finally
        {
            ctx.RequestPayments.RemoveRange(ctx.RequestPayments.Where(p => p.RequestId == req.Id));
            ctx.RequestPoGroups.RemoveRange(ctx.RequestPoGroups.Where(g => g.RequestId == req.Id));
            ctx.Requests.Remove(await ctx.Requests.FirstAsync(r => r.Id == req.Id));
            await ctx.SaveChangesAsync();
        }
    }
}
