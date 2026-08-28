using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Api.Projections;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Regression guard for the Approval Center GHOST-CARD defect (REQ-20/08/2026-274): a batch-model
/// QUOTATION request whose only batch sits in AREA/FINAL_ADJUSTMENT keeps its INTENTIONAL aggregate
/// scalar (WAITING_AREA/FINAL_APPROVAL, per RequestStatusCalculator), so the stage query's scalar
/// arm matched while no batch matched — and the projection's request-level fallback emitted a
/// genuinely actionable request-level approval row for a request the Buyer owns.
///
/// The rule under test: request-level rows exist ONLY for PAYMENT and for true legacy zero-batch
/// QUOTATION requests. A QUOTATION with ANY ApprovalBatch gets rows exclusively from actionable
/// batches. Exercised against the real EF projection on LocalDB (skipped when unavailable),
/// following the ApprovalQueueBatchIdentityTests pattern.
/// </summary>
[Collection("IntegrationTests")]
public class ApprovalQueueAdjustmentGhostTests
{
    /// <summary>
    /// Self-bootstrap BEFORE any CanConnect() gate: the sandbox is model-created (EnsureCreated +
    /// the model's HasData master data). The committed migration chain cannot build a database
    /// from scratch (duplicate 'SourceCompany' column between the consolidated baseline and a
    /// later migration), so a model-based sandbox is the only reproducible local shape. No-op when
    /// the database already exists; swallowed when LocalDB itself is unavailable (each test then
    /// skips via CanConnect(), unchanged).
    /// </summary>
    static ApprovalQueueAdjustmentGhostTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            ctx.Database.EnsureCreated();
        }
        catch
        {
            // LocalDB unavailable — CanConnect() gates every test exactly as before.
        }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    private static async Task<Dictionary<string, (string Name, string Color)>> StatusMapAsync(ApplicationDbContext ctx)
    {
        var rows = await ctx.RequestStatuses.AsNoTracking()
            .Select(s => new { s.Code, s.Name, s.BadgeColor }).ToListAsync();
        return rows.Where(s => !string.IsNullOrEmpty(s.Code))
            .GroupBy(s => s.Code!)
            .ToDictionary(g => g.Key, g => (g.First().Name ?? string.Empty, g.First().BadgeColor ?? string.Empty));
    }

    private static Task<List<ApprovalQueueItemDto>> ProjectAsync(ApplicationDbContext ctx, Guid requestId, string stage,
        IReadOnlyDictionary<string, (string, string)> statusMap)
    {
        var today = DateTime.UtcNow.Date;
        return ApprovalQueueProjection.ProjectAsync(
            ctx.Requests.Where(r => r.Id == requestId),
            stage, statusMap, today, today.AddDays(1), today.AddDays(4));
    }

    /// <summary>Seeds one request of <paramref name="typeCode"/> at scalar
    /// <paramref name="scalarStatusCode"/> with the given batches (one item + one quotation line
    /// each). Self-sufficient: creates its own ZZTEST actor user (removed by CleanupAsync), so the
    /// suite exercises the real projection even on a fresh integration DB with zero users.
    /// Returns Guid.Empty when required master data is absent.</summary>
    private static async Task<Guid> SeedAsync(string typeCode, string scalarStatusCode, params (int Number, string Status)[] batches)
    {
        await using var ctx = new ApplicationDbContext(Options());
        var actorUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "ZZTEST Ghost Actor",
            Email = $"zztest-ghost-{Guid.NewGuid():N}@test.local"
        };
        ctx.Users.Add(actorUser);
        var actor = actorUser.Id;

        var statusId = await ctx.RequestStatuses.Where(s => s.Code == scalarStatusCode).Select(s => s.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == typeCode).Select(t => t.Id).FirstOrDefaultAsync();
        if (statusId == 0 || typeId == 0) return Guid.Empty;

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST_GHOST_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZT-" + Guid.NewGuid().ToString("N")[..10],
            StatusId = statusId,
            RequestTypeId = typeId,
            DepartmentId = 4,
            CompanyId = 1,
            PlantId = 1,
            CurrencyId = 1,
            RequesterId = actor,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        int line = 1;
        foreach (var (number, status) in batches)
        {
            var quotation = new Quotation
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                SupplierNameSnapshot = $"ZZTEST GHOST SUP {number}",
                Currency = "AOA",
                SourceType = "MANUAL",
                TotalAmount = 100m,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actor
            };
            ctx.Quotations.Add(quotation);

            var batch = new ApprovalBatch
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                BatchNumber = number,
                Status = status,
                ApprovedTotalAmount = null,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actor
            };
            ctx.ApprovalBatches.Add(batch);

            var li = new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                LineNumber = line++,
                Description = $"ZZTEST ghost line {number}",
                Quantity = 1,
                UnitPrice = 100m,
                TotalAmount = 100m,
                PlantId = 1,
                IsDeleted = false,
                CreatedAtUtc = DateTime.UtcNow
            };
            ctx.RequestLineItems.Add(li);

            var qi = new QuotationItem
            {
                Id = Guid.NewGuid(),
                QuotationId = quotation.Id,
                Description = li.Description,
                Quantity = 1,
                UnitPrice = 100m,
                LineTotal = 100m,
                LineNumber = 1
            };
            ctx.QuotationItems.Add(qi);

            ctx.ApprovalBatchItems.Add(new ApprovalBatchItem
            {
                Id = Guid.NewGuid(),
                ApprovalBatchId = batch.Id,
                RequestLineItemId = li.Id,
                SelectedQuotationItemId = qi.Id,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await ctx.SaveChangesAsync();
        return request.Id;
    }

    private static async Task CleanupAsync(Guid requestId)
    {
        if (requestId == Guid.Empty) return;
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE abi FROM ApprovalBatchItems abi INNER JOIN ApprovalBatches b ON b.Id = abi.ApprovalBatchId WHERE b.RequestId = {0};" +
            "DELETE FROM ApprovalBatches WHERE RequestId = {0};" +
            "DELETE qi FROM QuotationItems qi INNER JOIN Quotations q ON q.Id = qi.QuotationId WHERE q.RequestId = {0};" +
            "DELETE FROM Quotations WHERE RequestId = {0};" +
            "DELETE FROM RequestLineItems WHERE RequestId = {0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId = {0};" +
            "DELETE FROM Requests WHERE Id = {0};" +
            // Our own ZZTEST actor, removable only once no request references it (FK order).
            "DELETE FROM Users WHERE Email LIKE 'zztest-ghost-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId = Users.Id);", requestId);
    }

    // (1) The exact REQ-274 shape: FINAL_ADJUSTMENT-only batch, aggregate scalar kept — no rows.
    [Fact]
    public async Task FinalAdjustmentOnlyBatch_EmitsZeroFinalRows()
    {
        if (!CanConnect()) return;
        var requestId = await SeedAsync("QUOTATION", "WAITING_FINAL_APPROVAL", (1, "FINAL_ADJUSTMENT"));
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var rows = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageFinal, await StatusMapAsync(ctx));
            Assert.Empty(rows);
        }
        finally { await CleanupAsync(requestId); }
    }

    // (2) Symmetric Area case: AREA_ADJUSTMENT-only batch, aggregate scalar kept — no rows.
    [Fact]
    public async Task AreaAdjustmentOnlyBatch_EmitsZeroAreaRows()
    {
        if (!CanConnect()) return;
        var requestId = await SeedAsync("QUOTATION", "WAITING_AREA_APPROVAL", (1, "AREA_ADJUSTMENT"));
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var rows = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageArea, await StatusMapAsync(ctx));
            Assert.Empty(rows);
        }
        finally { await CleanupAsync(requestId); }
    }

    // (3) Multi-batch: the FINAL_ADJUSTMENT lot must not suppress nor duplicate the genuinely
    //     waiting lot — exactly ONE row, carrying the REAL waiting batch's identity.
    [Fact]
    public async Task MixedBatches_EmitExactlyTheWaitingBatchRow()
    {
        if (!CanConnect()) return;
        var requestId = await SeedAsync("QUOTATION", "WAITING_FINAL_APPROVAL",
            (1, "FINAL_ADJUSTMENT"), (2, "WAITING_FINAL_APPROVAL"));
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var rows = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageFinal, await StatusMapAsync(ctx));

            var row = Assert.Single(rows);
            Assert.Equal(2, row.LotNumber);
            Assert.Equal("WAITING_FINAL_APPROVAL", row.BatchStatus);

            var waitingBatchId = await ctx.ApprovalBatches
                .Where(b => b.RequestId == requestId && b.BatchNumber == 2)
                .Select(b => b.Id).SingleAsync();
            Assert.Equal(waitingBatchId, row.ApprovalBatchId);
        }
        finally { await CleanupAsync(requestId); }
    }

    // (4) PAYMENT request-level approvals are untouched by the gate.
    [Fact]
    public async Task PaymentRequestLevelRow_StillEmitted()
    {
        if (!CanConnect()) return;
        var requestId = await SeedAsync("PAYMENT", "WAITING_FINAL_APPROVAL");
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var rows = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageFinal, await StatusMapAsync(ctx));

            var row = Assert.Single(rows);
            Assert.Null(row.ApprovalBatchId);
        }
        finally { await CleanupAsync(requestId); }
    }

    // (5) True legacy zero-batch QUOTATION request-level approvals are untouched by the gate.
    [Fact]
    public async Task LegacyZeroBatchQuotationRow_StillEmitted()
    {
        if (!CanConnect()) return;
        var requestId = await SeedAsync("QUOTATION", "WAITING_FINAL_APPROVAL");
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var rows = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageFinal, await StatusMapAsync(ctx));

            var row = Assert.Single(rows);
            Assert.Null(row.ApprovalBatchId);
        }
        finally { await CleanupAsync(requestId); }
    }

    // (6) A normal WAITING_FINAL_APPROVAL batch keeps its single batch row — and the gate must not
    //     add a request-level duplicate beside it.
    [Fact]
    public async Task NormalWaitingFinalBatch_SingleBatchRow_NoRequestLevelDuplicate()
    {
        if (!CanConnect()) return;
        var requestId = await SeedAsync("QUOTATION", "WAITING_FINAL_APPROVAL", (1, "WAITING_FINAL_APPROVAL"));
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var rows = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageFinal, await StatusMapAsync(ctx));

            var row = Assert.Single(rows);
            Assert.NotNull(row.ApprovalBatchId);
            Assert.Equal("WAITING_FINAL_APPROVAL", row.BatchStatus);
        }
        finally { await CleanupAsync(requestId); }
    }

    // (7) Post-resubmit shape: the reworked batch returns to WAITING_AREA_APPROVAL and must appear
    //     as a REAL Area batch row (and contribute nothing to the Final queue).
    [Fact]
    public async Task PostResubmitBatch_AppearsAsAreaBatchRow()
    {
        if (!CanConnect()) return;
        var requestId = await SeedAsync("QUOTATION", "WAITING_AREA_APPROVAL", (1, "WAITING_AREA_APPROVAL"));
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var statusMap = await StatusMapAsync(ctx);

            var areaRows = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageArea, statusMap);
            var areaRow = Assert.Single(areaRows);
            Assert.NotNull(areaRow.ApprovalBatchId);
            Assert.Equal("WAITING_AREA_APPROVAL", areaRow.BatchStatus);

            var finalRows = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageFinal, statusMap);
            Assert.Empty(finalRows);
        }
        finally { await CleanupAsync(requestId); }
    }
}
