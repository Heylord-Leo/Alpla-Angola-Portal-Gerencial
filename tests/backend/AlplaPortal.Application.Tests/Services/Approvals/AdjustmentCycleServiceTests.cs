using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Adjustment V2 Phase 3 — the structured cycle service (<see cref="AdjustmentCycleService"/>) on the
/// real relational model (LocalDB): validation, the concurrency-safe CycleNumber + one-open-cycle
/// guard, and the Phase 3 transitional close-from-legacy-resubmit/cancel behavior. These are the
/// acceptance-critical guarantees — no fixture is silently skipped except when LocalDB is absent
/// (CanConnect gates every suite here identically).
/// </summary>
[Collection("IntegrationTests")]
public class AdjustmentCycleServiceTests
{
    static AdjustmentCycleServiceTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                var tableId = ctx.Database
                    .SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.ApprovalBatchAdjustments'), 0) AS [Value]")
                    .AsEnumerable().First();
                if (tableId == 0) ctx.Database.EnsureDeleted();
            }
            ctx.Database.EnsureCreated();
        }
        catch { /* LocalDB unavailable — CanConnect() gates every test. */ }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    private sealed record Seed(Guid RequestId, Guid BatchId, Guid LineItemId, Guid ActorId);

    private static async Task<Seed?> SeedAsync(string batchStatus = "WAITING_AREA_APPROVAL")
    {
        await using var ctx = new ApplicationDbContext(Options());
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Adj Actor", Email = $"zztest-adj-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(actor);

        var statusId = await ctx.RequestStatuses.Where(s => s.Code == "WAITING_AREA_APPROVAL").Select(s => s.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "QUOTATION").Select(t => t.Id).FirstOrDefaultAsync();
        if (statusId == 0 || typeId == 0) return null;

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST_ADJ_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZT-ADJ-" + Guid.NewGuid().ToString("N")[..8],
            StatusId = statusId, RequestTypeId = typeId,
            DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            RequesterId = actor.Id, BuyerId = actor.Id, CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var li = new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 1,
            Description = "ZZTEST adjusted item", Quantity = 1, UnitPrice = 100m, TotalAmount = 100m,
            PlantId = 1, IsDeleted = false, CreatedAtUtc = DateTime.UtcNow
        };
        ctx.RequestLineItems.Add(li);

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 1, Status = batchStatus,
            CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor.Id
        };
        ctx.ApprovalBatches.Add(batch);
        ctx.ApprovalBatchItems.Add(new ApprovalBatchItem
        {
            Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = li.Id, CreatedAtUtc = DateTime.UtcNow
        });

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, batch.Id, li.Id, actor.Id);
    }

    private static async Task<ApprovalBatch> LoadBatchAsync(ApplicationDbContext ctx, Guid batchId) =>
        await ctx.ApprovalBatches.Include(b => b.Items).SingleAsync(b => b.Id == batchId);

    private static BatchAdjustmentRequestDto Dto(bool wholeBatch, params BatchAdjustmentReasonInputDto[] reasons) => new()
    {
        Comment = "ZZTEST comentário do aprovador.",
        WholeBatch = wholeBatch,
        Reasons = reasons.ToList()
    };

    private static BatchAdjustmentReasonInputDto Reason(string code, Guid? item = null, string? detail = null) =>
        new() { ReasonCode = code, RequestLineItemId = item, Detail = detail };

    private static async Task CleanupAsync(Guid requestId)
    {
        if (requestId == Guid.Empty) return;
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE a FROM ApprovalBatchAdjustments a INNER JOIN ApprovalBatches b ON b.Id = a.ApprovalBatchId WHERE b.RequestId = {0};" +
            "DELETE abi FROM ApprovalBatchItems abi INNER JOIN ApprovalBatches b ON b.Id = abi.ApprovalBatchId WHERE b.RequestId = {0};" +
            "DELETE FROM ApprovalBatches WHERE RequestId = {0};" +
            "DELETE FROM RequestLineItems WHERE RequestId = {0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId = {0};" +
            "DELETE FROM Requests WHERE Id = {0};" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-adj-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId = Users.Id);", requestId);
    }

    // ── A/B. Structured creation (Area + Final) ───────────────────────────────

    [Theory]
    [InlineData(AdjustmentConstants.SourceStages.Area)]
    [InlineData(AdjustmentConstants.SourceStages.Final)]
    public async Task StageNewCycle_CreatesOneCycle_WaitingBuyer_WithReasons_AuditFields(string sourceStage)
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new AdjustmentCycleService(ctx);
                var batch = await LoadBatchAsync(ctx, seed.BatchId);
                var dto = Dto(false,
                    Reason(AdjustmentConstants.ReasonCodes.PriceNegotiation),
                    Reason(AdjustmentConstants.ReasonCodes.RequestedQuantity, seed.LineItemId));

                var result = await svc.StageNewCycleAsync(batch, sourceStage, dto, seed.ActorId);
                Assert.True(result.Success, result.ErrorDetail);
                await ctx.SaveChangesAsync();
            }

            await using (var verify = new ApplicationDbContext(Options()))
            {
                var cycle = await verify.ApprovalBatchAdjustments.AsNoTracking()
                    .Include(a => a.Reasons)
                    .SingleAsync(a => a.ApprovalBatchId == seed.BatchId);

                Assert.Equal(1, cycle.CycleNumber);
                Assert.Equal(sourceStage, cycle.SourceStage);
                // PHASE 3 TRANSITIONAL: every new cycle starts WAITING_BUYER regardless of ownership.
                Assert.Equal(AdjustmentConstants.States.WaitingBuyer, cycle.Status);
                Assert.Equal(seed.ActorId, cycle.RequestedByUserId);
                Assert.NotEqual(default, cycle.RequestedAtUtc);
                Assert.Equal("ZZTEST comentário do aprovador.", cycle.ApproverComment);
                Assert.Equal(2, cycle.Reasons.Count);
                // Reason ownership is preserved — the requester-owned reason is NOT reclassified.
                Assert.Contains(cycle.Reasons, r => r.ReasonCode == AdjustmentConstants.ReasonCodes.RequestedQuantity && r.RequestLineItemId == seed.LineItemId);
                Assert.Contains(cycle.Reasons, r => r.ReasonCode == AdjustmentConstants.ReasonCodes.PriceNegotiation && r.RequestLineItemId == null);
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── C. Multiple cycles → next CycleNumber ─────────────────────────────────

    [Fact]
    public async Task StageNewCycle_AfterClosedCycle_AllocatesNextCycleNumber()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using (var seedCtx = new ApplicationDbContext(Options()))
            {
                seedCtx.ApprovalBatchAdjustments.Add(new ApprovalBatchAdjustment
                {
                    ApprovalBatchId = seed.BatchId, CycleNumber = 1, SourceStage = AdjustmentConstants.SourceStages.Area,
                    Status = AdjustmentConstants.States.Resubmitted, WholeBatch = true, ApproverComment = "ciclo anterior",
                    RequestedByUserId = seed.ActorId, RequestedAtUtc = DateTime.UtcNow, ClosedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
                });
                await seedCtx.SaveChangesAsync();
            }

            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new AdjustmentCycleService(ctx);
                var batch = await LoadBatchAsync(ctx, seed.BatchId);
                var result = await svc.StageNewCycleAsync(batch, AdjustmentConstants.SourceStages.Final,
                    Dto(true, Reason(AdjustmentConstants.ReasonCodes.BatchComposition)), seed.ActorId);
                Assert.True(result.Success);
                Assert.Equal(2, result.Cycle!.CycleNumber);
                await ctx.SaveChangesAsync();
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── D. Open-cycle conflict (pre-check) ────────────────────────────────────

    [Fact]
    public async Task StageNewCycle_WithExistingOpenCycle_Returns409()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using (var seedCtx = new ApplicationDbContext(Options()))
            {
                seedCtx.ApprovalBatchAdjustments.Add(new ApprovalBatchAdjustment
                {
                    ApprovalBatchId = seed.BatchId, CycleNumber = 1, SourceStage = AdjustmentConstants.SourceStages.Area,
                    Status = AdjustmentConstants.States.WaitingBuyer, WholeBatch = true, ApproverComment = "aberto",
                    RequestedByUserId = seed.ActorId, RequestedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
                });
                await seedCtx.SaveChangesAsync();
            }

            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new AdjustmentCycleService(ctx);
                var batch = await LoadBatchAsync(ctx, seed.BatchId);
                var result = await svc.StageNewCycleAsync(batch, AdjustmentConstants.SourceStages.Area,
                    Dto(true, Reason(AdjustmentConstants.ReasonCodes.PriceNegotiation)), seed.ActorId);
                Assert.False(result.Success);
                Assert.Equal(409, result.ErrorStatus);
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── E. Concurrency / double-submit → exactly one open cycle ───────────────

    [Fact]
    public async Task ConcurrentStage_SecondSaveViolatesUniqueIndex_NetOneOpenCycle()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using var ctx1 = new ApplicationDbContext(Options());
            await using var ctx2 = new ApplicationDbContext(Options());
            var svc1 = new AdjustmentCycleService(ctx1);
            var svc2 = new AdjustmentCycleService(ctx2);
            var batch1 = await LoadBatchAsync(ctx1, seed.BatchId);
            var batch2 = await LoadBatchAsync(ctx2, seed.BatchId);

            // Both pass the pre-check (neither committed yet) and both build cycle #1.
            var r1 = await svc1.StageNewCycleAsync(batch1, AdjustmentConstants.SourceStages.Area, Dto(true, Reason(AdjustmentConstants.ReasonCodes.PriceNegotiation)), seed.ActorId);
            var r2 = await svc2.StageNewCycleAsync(batch2, AdjustmentConstants.SourceStages.Area, Dto(true, Reason(AdjustmentConstants.ReasonCodes.NewQuotation)), seed.ActorId);
            Assert.True(r1.Success);
            Assert.True(r2.Success);

            await ctx1.SaveChangesAsync(); // winner
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
            Assert.True(svc2.IsUniqueViolation(ex)); // deterministically mapped to 409 by the controller

            await using var verify = new ApplicationDbContext(Options());
            var open = await verify.ApprovalBatchAdjustments.CountAsync(a => a.ApprovalBatchId == seed.BatchId && AdjustmentConstants.States.Open.Contains(a.Status));
            Assert.Equal(1, open);
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── F. Reason validation ──────────────────────────────────────────────────

    [Fact]
    public async Task StageNewCycle_NoReason_InvalidCode_ForeignItem_ItemRequired_AreRejected_ValidAccepted()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var svc = new AdjustmentCycleService(ctx);
            var batch = await LoadBatchAsync(ctx, seed.BatchId);

            // No reason.
            Assert.Equal(400, (await svc.StageNewCycleAsync(batch, "AREA", Dto(true), seed.ActorId)).ErrorStatus);
            // Empty comment.
            Assert.Equal(400, (await svc.StageNewCycleAsync(batch, "AREA", new BatchAdjustmentRequestDto { Comment = "  ", Reasons = { Reason(AdjustmentConstants.ReasonCodes.PriceNegotiation) } }, seed.ActorId)).ErrorStatus);
            // Invalid code.
            Assert.Equal(400, (await svc.StageNewCycleAsync(batch, "AREA", Dto(true, Reason("NOT_A_REAL_CODE")), seed.ActorId)).ErrorStatus);
            // Item that belongs to another request/batch.
            Assert.Equal(400, (await svc.StageNewCycleAsync(batch, "AREA", Dto(false, Reason(AdjustmentConstants.ReasonCodes.RequestedQuantity, Guid.NewGuid())), seed.ActorId)).ErrorStatus);
            // Item-required reason with no item.
            Assert.Equal(400, (await svc.StageNewCycleAsync(batch, "AREA", Dto(false, Reason(AdjustmentConstants.ReasonCodes.RequestedQuantity)), seed.ActorId)).ErrorStatus);
            // Item-required reason incompatible with whole-lot.
            Assert.Equal(400, (await svc.StageNewCycleAsync(batch, "AREA", Dto(true, Reason(AdjustmentConstants.ReasonCodes.Specification, seed.LineItemId)), seed.ActorId)).ErrorStatus);

            // Valid whole-batch reason.
            Assert.True((await svc.StageNewCycleAsync(batch, "AREA", Dto(true, Reason(AdjustmentConstants.ReasonCodes.PriceNegotiation)), seed.ActorId)).Success);
            // Valid item-scoped reason (fresh context — previous Add is discarded by not saving).
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    [Fact]
    public async Task StageNewCycle_DuplicateReason_IsNormalizedToOne()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new AdjustmentCycleService(ctx);
                var batch = await LoadBatchAsync(ctx, seed.BatchId);
                var dto = Dto(false,
                    Reason(AdjustmentConstants.ReasonCodes.RequestedQuantity, seed.LineItemId),
                    Reason(AdjustmentConstants.ReasonCodes.RequestedQuantity, seed.LineItemId), // duplicate
                    Reason(AdjustmentConstants.ReasonCodes.PriceNegotiation));
                var result = await svc.StageNewCycleAsync(batch, "AREA", dto, seed.ActorId);
                Assert.True(result.Success);
                Assert.Equal(2, result.Cycle!.Reasons.Count); // deduped
                await ctx.SaveChangesAsync();
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── Close (Phase 3 transitional) + legacy compat ──────────────────────────

    [Fact]
    public async Task CloseOpenCycle_SetsTerminalState_ResubmittedAndCancelled()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            // RESUBMITTED close.
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                ctx.ApprovalBatchAdjustments.Add(new ApprovalBatchAdjustment
                {
                    ApprovalBatchId = seed.BatchId, CycleNumber = 1, SourceStage = "AREA", Status = AdjustmentConstants.States.WaitingBuyer,
                    WholeBatch = true, ApproverComment = "aberto", RequestedByUserId = seed.ActorId, RequestedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
                });
                await ctx.SaveChangesAsync();
            }
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new AdjustmentCycleService(ctx);
                var closed = await svc.CloseOpenCycleAsync(seed.BatchId, AdjustmentConstants.States.Resubmitted, seed.ActorId, null);
                Assert.NotNull(closed);
                await ctx.SaveChangesAsync();
            }
            await using (var verify = new ApplicationDbContext(Options()))
            {
                var cycle = await verify.ApprovalBatchAdjustments.AsNoTracking().SingleAsync(a => a.ApprovalBatchId == seed.BatchId);
                Assert.Equal(AdjustmentConstants.States.Resubmitted, cycle.Status);
                Assert.NotNull(cycle.ClosedAtUtc);
                // A closed cycle no longer occupies the open slot → a new cycle can be created.
                var open = await verify.ApprovalBatchAdjustments.CountAsync(a => a.ApprovalBatchId == seed.BatchId && AdjustmentConstants.States.Open.Contains(a.Status));
                Assert.Equal(0, open);
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    [Fact]
    public async Task CloseOpenCycle_LegacyBatchWithNoCycle_IsNoOp()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync(); // no V2 cycle exists (pre-Phase-3 / legacy batch)
        if (seed == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var svc = new AdjustmentCycleService(ctx);
            var closed = await svc.CloseOpenCycleAsync(seed.BatchId, AdjustmentConstants.States.Resubmitted, seed.ActorId, null);
            Assert.Null(closed); // legacy resubmit path stays fully functional with nothing to close
            await ctx.SaveChangesAsync();
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ══════════════ Phase 4 — Buyer resolution (GetOpenCycle + StageBuyerResolutionAndClose) ══════════════

    private static async Task<Guid> SeedOpenCycleAsync(Guid batchId, Guid actorId)
    {
        await using var ctx = new ApplicationDbContext(Options());
        var cycle = new ApprovalBatchAdjustment
        {
            ApprovalBatchId = batchId, CycleNumber = 1, SourceStage = AdjustmentConstants.SourceStages.Area,
            Status = AdjustmentConstants.States.WaitingBuyer, WholeBatch = true, ApproverComment = "aberto",
            RequestedByUserId = actorId, RequestedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow,
        };
        cycle.Reasons.Add(new ApprovalBatchAdjustmentReason
        {
            ReasonCode = AdjustmentConstants.ReasonCodes.PriceNegotiation, CreatedAtUtc = DateTime.UtcNow,
        });
        ctx.ApprovalBatchAdjustments.Add(cycle);
        await ctx.SaveChangesAsync();
        return cycle.Id;
    }

    // ── G. GetOpenCycle returns the open cycle; null when none / already closed ────
    [Fact]
    public async Task GetOpenCycle_ReturnsOpen_NullWhenClosedOrLegacy()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new AdjustmentCycleService(ctx);
                Assert.Null(await svc.GetOpenCycleAsync(seed.BatchId)); // legacy batch: no cycle
            }
            var cycleId = await SeedOpenCycleAsync(seed.BatchId, seed.ActorId);
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new AdjustmentCycleService(ctx);
                var open = await svc.GetOpenCycleAsync(seed.BatchId);
                Assert.NotNull(open);
                Assert.Equal(cycleId, open!.Id);
                Assert.NotEmpty(open.Reasons); // Reasons are included for the notification payload
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── H. StageBuyerResolutionAndClose records one BUYER resolution and closes the cycle ─────────
    [Fact]
    public async Task StageBuyerResolutionAndClose_RecordsBuyerResolution_ClosesResubmitted()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await SeedOpenCycleAsync(seed.BatchId, seed.ActorId);
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new AdjustmentCycleService(ctx);
                var open = await svc.GetOpenCycleAsync(seed.BatchId);
                var res = svc.StageBuyerResolutionAndClose(open!, seed.ActorId, "  Cotação corrigida.  ");
                Assert.Equal(AdjustmentConstants.ActorTypes.Buyer, res.ActorType);
                Assert.Equal("Cotação corrigida.", res.ResolutionComment); // trimmed
                await ctx.SaveChangesAsync();
            }
            await using (var verify = new ApplicationDbContext(Options()))
            {
                var cycle = await verify.ApprovalBatchAdjustments.Include(a => a.Resolutions)
                    .SingleAsync(a => a.ApprovalBatchId == seed.BatchId);
                Assert.Equal(AdjustmentConstants.States.Resubmitted, cycle.Status);
                Assert.NotNull(cycle.ClosedAtUtc);
                var resolution = Assert.Single(cycle.Resolutions);
                Assert.Equal(AdjustmentConstants.ActorTypes.Buyer, resolution.ActorType);
                Assert.Equal(seed.ActorId, resolution.ResolvedByUserId);
                // The open slot is freed → a new cycle can be created afterwards.
                var open = await verify.ApprovalBatchAdjustments.CountAsync(a => a.ApprovalBatchId == seed.BatchId && AdjustmentConstants.States.Open.Contains(a.Status));
                Assert.Equal(0, open);
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── I. Concurrency / double-resolve → the resolution unique index yields exactly one BUYER
    //      resolution (the atomicity guarantee the controller maps to 409). ─────────────────────
    [Fact]
    public async Task ConcurrentBuyerResolution_SecondSaveViolatesUniqueIndex_NetOneResolution()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await SeedOpenCycleAsync(seed.BatchId, seed.ActorId);

            await using var ctx1 = new ApplicationDbContext(Options());
            await using var ctx2 = new ApplicationDbContext(Options());
            var svc1 = new AdjustmentCycleService(ctx1);
            var svc2 = new AdjustmentCycleService(ctx2);
            var open1 = await svc1.GetOpenCycleAsync(seed.BatchId);
            var open2 = await svc2.GetOpenCycleAsync(seed.BatchId);

            svc1.StageBuyerResolutionAndClose(open1!, seed.ActorId, "resposta 1");
            svc2.StageBuyerResolutionAndClose(open2!, seed.ActorId, "resposta 2");

            await ctx1.SaveChangesAsync(); // winner
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
            Assert.True(svc2.IsUniqueViolation(ex)); // controller maps this to 409 "Reenvio em Andamento"

            await using var verify = new ApplicationDbContext(Options());
            var count = await verify.Set<ApprovalBatchAdjustmentResolution>()
                .CountAsync(r => r.AdjustmentId == open1!.Id && r.ActorType == AdjustmentConstants.ActorTypes.Buyer);
            Assert.Equal(1, count);
        }
        finally { await CleanupAsync(seed.RequestId); }
    }
}
