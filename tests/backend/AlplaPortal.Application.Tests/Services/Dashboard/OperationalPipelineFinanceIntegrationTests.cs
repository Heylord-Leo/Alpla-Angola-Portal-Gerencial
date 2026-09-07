using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Api.Services.Dashboard;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// B6.1 — Finance pipeline positions + the PAYMENT_COMPLETED Finance→Receiving handoff, exercised against
/// the REAL EF provider (LocalDB). The canonical B3 finance projection cannot materialize under EF
/// in-memory (double PoGroups ThenInclude), so these counts are verified here. Skips gracefully when the
/// integration database is unavailable (same policy as the other integration tests).
/// </summary>
[Collection("IntegrationTests")]
public class OperationalPipelineFinanceIntegrationTests
{
    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    private static int Entity(DashboardV2PipelineDto d, string stage) => d.Stages.Single(s => s.Stage == stage).EntityCount;

    [Fact]
    public async Task Finance_positions_and_payment_completed_handoff()
    {
        if (!CanConnect()) return; // integration DB unavailable — skip

        await using var ctx = new ApplicationDbContext(Options());

        var actor = await ctx.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == RequestConstants.Types.Quotation).Select(t => t.Id).FirstOrDefaultAsync();
        var statusId = await ctx.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.PoIssued).Select(s => s.Id).FirstOrDefaultAsync();
        if (actor == Guid.Empty || typeId == 0 || statusId == 0) return; // DB not seeded — skip

        var req = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST_PIPE_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZP-" + Guid.NewGuid().ToString("N")[..10],
            StatusId = statusId, RequestTypeId = typeId, RequesterId = actor,
            DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1, CreatedAtUtc = DateTime.UtcNow,
        };
        req.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, Status = RequestConstants.PoGroupStatuses.PaymentCompleted, TotalAmount = 100m });
        req.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, Status = RequestConstants.PoGroupStatuses.PaymentScheduled, TotalAmount = 200m });
        req.PoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, Status = RequestConstants.PoGroupStatuses.PoIssued, TotalAmount = 300m });
        ctx.Requests.Add(req);
        await ctx.SaveChangesAsync();

        try
        {
            var projection = new OperationalPipelineProjection(ctx, new FinancePaymentEligibilityService());
            var d = await projection.BuildAsync(ctx.Requests.Where(r => r.Id == req.Id), DateTime.UtcNow.Date);

            Assert.Equal(1, Entity(d, PipelineStages.FinancePaid));          // PAYMENT_COMPLETED → PAID
            Assert.Equal(1, Entity(d, PipelineStages.FinanceScheduled));     // PAYMENT_SCHEDULED → SCHEDULED
            Assert.Equal(1, Entity(d, PipelineStages.FinanceNeedsScheduling));// PO_ISSUED → NEEDS_SCHEDULING
            // Handoff: the same PAYMENT_COMPLETED group is ALSO a Receiving entry (CanOverlap).
            Assert.Equal(1, Entity(d, PipelineStages.ReceivingReady));
        }
        finally
        {
            var groups = ctx.RequestPoGroups.Where(g => g.RequestId == req.Id);
            ctx.RequestPoGroups.RemoveRange(groups);
            ctx.Requests.Remove(await ctx.Requests.FirstAsync(r => r.Id == req.Id));
            await ctx.SaveChangesAsync();
        }
    }
}
