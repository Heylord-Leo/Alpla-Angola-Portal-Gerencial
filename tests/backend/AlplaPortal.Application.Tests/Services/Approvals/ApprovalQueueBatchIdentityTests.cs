using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Api.Projections;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Projections;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Regression guard for the Approval Center queue-identity defect (REQ-21/07/2026-132): a request
/// with TWO simultaneous WAITING_AREA_APPROVAL batches was collapsed into a single request-level
/// card (OrderBy(BatchNumber).FirstOrDefault()), so one lot's amount was shown while the drawer
/// opened a different lot.
///
/// The queue unit is now the actionable ApprovalBatch (batchId + stage), so
/// <see cref="ApprovalQueueProjection"/> emits ONE ROW PER ACTIONABLE BATCH. These tests exercise
/// the real EF projection against LocalDB (skipped when unavailable), seeding a REQ-132-shaped
/// fixture and asserting the collapse cannot recur.
/// </summary>
[Collection("IntegrationTests")]
public class ApprovalQueueBatchIdentityTests
{
    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    private const string Batch2Supplier = "ZZTEST Mistoquimica - Industria Quimica, Lda";
    private const string Batch1Supplier = "ZZTEST AFRICANA DISCOUNT, LDA.";
    private static readonly decimal[] Batch1LineTotals = { 30043.41m, 30043.41m, 30043.41m, 30043.41m, 30043.43m }; // = 150,217.07
    private static readonly decimal[] Batch2LineTotals = { 123746.42m, 123746.42m, 123746.42m, 123746.42m, 123746.42m }; // = 618,732.10
    private const decimal Batch1Total = 150217.07m;
    private const decimal Batch2Total = 618732.10m;

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

    /// <summary>
    /// Seeds a QUOTATION request (kept at WAITING_QUOTATION, as REQ-132 was) with two
    /// WAITING_AREA_APPROVAL batches of 5 items each (distinct suppliers/amounts), plus an optional
    /// non-actionable historical batch and an optional Final batch, so the projection rules can be
    /// asserted in isolation.
    /// </summary>
    private static async Task<Guid> SeedAsync(bool withApprovedHistoricalBatch, bool withFinalBatch)
    {
        await using var ctx = new ApplicationDbContext(Options());
        var actor = await ctx.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
        if (actor == Guid.Empty) return Guid.Empty;

        var waitingQuotation = await ctx.RequestStatuses.Where(s => s.Code == "WAITING_QUOTATION").Select(s => s.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "QUOTATION").Select(t => t.Id).FirstOrDefaultAsync();
        if (waitingQuotation == 0 || typeId == 0) return Guid.Empty;

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST_QUEUE_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZT-" + Guid.NewGuid().ToString("N")[..10],
            StatusId = waitingQuotation,
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
        // Batch 1 + Batch 2 (both WAITING_AREA_APPROVAL)
        SeedBatch(ctx, request, actor, batchNumber: 1, status: "WAITING_AREA_APPROVAL", supplier: Batch1Supplier, lineTotals: Batch1LineTotals, ref line);
        SeedBatch(ctx, request, actor, batchNumber: 2, status: "WAITING_AREA_APPROVAL", supplier: Batch2Supplier, lineTotals: Batch2LineTotals, ref line);

        if (withApprovedHistoricalBatch)
            SeedBatch(ctx, request, actor, batchNumber: 3, status: "APPROVED", supplier: "ZZTEST HISTORICAL", lineTotals: new[] { 999m, 999m }, ref line);

        if (withFinalBatch)
            SeedBatch(ctx, request, actor, batchNumber: 4, status: "WAITING_FINAL_APPROVAL", supplier: "ZZTEST FINAL SUP", lineTotals: new[] { 500m, 500m, 500m }, ref line);

        await ctx.SaveChangesAsync();
        return request.Id;
    }

    private static void SeedBatch(ApplicationDbContext ctx, Request request, Guid actor, int batchNumber, string status,
        string supplier, decimal[] lineTotals, ref int line)
    {
        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = supplier,
            Currency = "AOA",
            SourceType = "MANUAL",
            TotalAmount = lineTotals.Sum(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actor
        };
        ctx.Quotations.Add(quotation);

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            BatchNumber = batchNumber,
            Status = status,
            ApprovedTotalAmount = null, // as REQ-132: amount comes from the item sum, not a snapshot
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actor
        };
        ctx.ApprovalBatches.Add(batch);

        for (int i = 0; i < lineTotals.Length; i++)
        {
            var li = new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                LineNumber = line++,
                Description = $"ZZTEST line {supplier} {i + 1}",
                Quantity = 1,
                UnitPrice = lineTotals[i],
                TotalAmount = lineTotals[i],
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
                UnitPrice = lineTotals[i],
                LineTotal = lineTotals[i],
                LineNumber = i + 1
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
    }

    private static async Task CleanupAsync(Guid requestId)
    {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE abi FROM ApprovalBatchItems abi INNER JOIN ApprovalBatches b ON b.Id = abi.ApprovalBatchId WHERE b.RequestId = {0};" +
            "DELETE FROM ApprovalBatches WHERE RequestId = {0};" +
            "DELETE qi FROM QuotationItems qi INNER JOIN Quotations q ON q.Id = qi.QuotationId WHERE q.RequestId = {0};" +
            "DELETE FROM Quotations WHERE RequestId = {0};" +
            "DELETE FROM RequestLineItems WHERE RequestId = {0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId = {0};" +
            "DELETE FROM Requests WHERE Id = {0};", requestId);
    }

    // ── Assertions 1-8, 13, 14: two simultaneous Area batches produce two independent rows ──
    [Fact]
    public async Task TwoSimultaneousAreaBatches_ProduceTwoIndependentRows()
    {
        if (!CanConnect()) return;
        var requestId = await SeedAsync(withApprovedHistoricalBatch: true, withFinalBatch: false);
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var statusMap = await StatusMapAsync(ctx);
            var rows = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageArea, statusMap);

            // (1) Endpoint returns two queue rows (the APPROVED historical batch does NOT add one → (14)).
            Assert.Equal(2, rows.Count);

            var lot1 = rows.Single(r => r.LotNumber == 1);
            var lot2 = rows.Single(r => r.LotNumber == 2);

            // (2) Both rows share the same requestId/requestNumber.
            Assert.Equal(requestId, lot1.RequestId);
            Assert.Equal(requestId, lot2.RequestId);
            Assert.Equal(lot1.RequestNumber, lot2.RequestNumber);

            // (3) Rows have different approvalBatchId (and distinct queue keys).
            Assert.NotNull(lot1.ApprovalBatchId);
            Assert.NotNull(lot2.ApprovalBatchId);
            Assert.NotEqual(lot1.ApprovalBatchId, lot2.ApprovalBatchId);
            Assert.NotEqual(lot1.QueueKey, lot2.QueueKey);

            // (4) Lote #1 and Lote #2 respectively, each with 5 items.
            Assert.Equal(5, lot1.ItemCount);
            Assert.Equal(5, lot2.ItemCount);

            // (5) Each amount comes only from its own batch.
            Assert.Equal(Batch1Total, lot1.ActionableAmount);
            Assert.Equal(Batch2Total, lot2.ActionableAmount);
            Assert.Equal(ApprovalQueueAmountResolver.Sources.BatchItemSum, lot1.ActionableAmountSource);
            Assert.Equal(ApprovalQueueAmountResolver.Sources.BatchItemSum, lot2.ActionableAmountSource);

            // (6) Each supplier comes only from its own batch.
            Assert.Equal(Batch1Supplier, lot1.SupplierDisplay);
            Assert.Equal(Batch2Supplier, lot2.SupplierDisplay);

            // (7) Area section count increases by two (both rows are in the Area stage).
            Assert.All(rows, r => Assert.Equal(ApprovalQueueProjection.StageArea, r.ApprovalStage));

            // (8) Queue total includes both amounts exactly once.
            Assert.Equal(Batch1Total + Batch2Total,
                ApprovalQueueAmountResolver.SumActionable(rows.Select(r => r.ActionableAmount)));

            // (13) Request status may remain WAITING_QUOTATION without affecting the actionable batches.
            Assert.Equal("WAITING_QUOTATION", lot1.RequestStatusCode);
            Assert.Equal("WAITING_AREA_APPROVAL", lot1.BatchStatus);
            Assert.Equal("WAITING_AREA_APPROVAL", lot2.BatchStatus);
        }
        finally { await CleanupAsync(requestId); }
    }

    // ── Assertion 15 + Area/Final independence: one Area batch and one Final batch land in the
    //    correct section independently, one row each. ──
    [Fact]
    public async Task AreaAndFinalBatches_AppearInTheirOwnSectionOnly()
    {
        if (!CanConnect()) return;
        var requestId = await SeedAsync(withApprovedHistoricalBatch: false, withFinalBatch: true);
        if (requestId == Guid.Empty) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var statusMap = await StatusMapAsync(ctx);

            var area = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageArea, statusMap);
            var final = await ProjectAsync(ctx, requestId, ApprovalQueueProjection.StageFinal, statusMap);

            // Two Area batches surface in Area; the single Final batch surfaces only in Final.
            Assert.Equal(2, area.Count);
            Assert.All(area, r => Assert.Equal("WAITING_AREA_APPROVAL", r.BatchStatus));

            var finalRow = Assert.Single(final);
            Assert.Equal("WAITING_FINAL_APPROVAL", finalRow.BatchStatus);
            Assert.Equal(ApprovalQueueProjection.StageFinal, finalRow.ApprovalStage);
            Assert.Equal(1500m, finalRow.ActionableAmount); // 3 items × 500
            Assert.Equal(3, finalRow.ItemCount);

            // The Area rows never leak into the Final section and vice-versa.
            Assert.DoesNotContain(final, r => r.ApprovalBatchId == area[0].ApprovalBatchId);
            Assert.DoesNotContain(area, r => r.ApprovalBatchId == finalRow.ApprovalBatchId);
        }
        finally { await CleanupAsync(requestId); }
    }
}
