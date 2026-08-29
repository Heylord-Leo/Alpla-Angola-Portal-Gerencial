using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Adjustment V2 Phase 2 — the DORMANT structured adjustment domain. These tests exercise the
/// real relational model on LocalDB (constraints, cascades, computed columns — none of which the
/// InMemory provider enforces): cycle uniqueness per batch, the one-open-cycle filtered unique
/// index, the unfiltered whole-lot reason uniqueness, resolution actor uniqueness, candidate
/// review identity, and the delete-behavior contract (adjustments cascade WITH their batch
/// aggregate; line-item references block hard deletes; review identities carry no FK on purpose).
/// </summary>
[Collection("IntegrationTests")]
public class ApprovalBatchAdjustmentDomainTests
{
    /// <summary>
    /// Model-drift-aware bootstrap: the sandbox is model-created (EnsureCreated + HasData; the
    /// committed migration chain cannot build a database from scratch). When the existing sandbox
    /// predates the Phase 2 tables, it is recreated from the CURRENT model — otherwise every test
    /// here would fail on missing tables while claiming to test the domain.
    /// </summary>
    static ApprovalBatchAdjustmentDomainTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                var tableId = ctx.Database
                    .SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.ApprovalBatchAdjustments'), 0) AS [Value]")
                    .AsEnumerable()
                    .First();
                if (tableId == 0)
                {
                    ctx.Database.EnsureDeleted();
                }
            }
            ctx.Database.EnsureCreated();
        }
        catch
        {
            // LocalDB unavailable — CanConnect() gates every test exactly as in the other suites.
        }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    private sealed record Seed(Guid RequestId, Guid BatchId, Guid LineItemId, Guid ActorId);

    /// <summary>One QUOTATION request with one batch and one line item — the minimal aggregate an
    /// adjustment cycle attaches to. Self-sufficient ZZTEST actor (removed by CleanupAsync).</summary>
    private static async Task<Seed?> SeedAsync(string batchStatus = "FINAL_ADJUSTMENT")
    {
        await using var ctx = new ApplicationDbContext(Options());
        var actor = new User
        {
            Id = Guid.NewGuid(),
            FullName = "ZZTEST Adjustment Actor",
            Email = $"zztest-adj-{Guid.NewGuid():N}@test.local"
        };
        ctx.Users.Add(actor);

        var statusId = await ctx.RequestStatuses.Where(s => s.Code == "WAITING_FINAL_APPROVAL").Select(s => s.Id).FirstOrDefaultAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "QUOTATION").Select(t => t.Id).FirstOrDefaultAsync();
        if (statusId == 0 || typeId == 0) return null;

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST_ADJ_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZT-ADJ-" + Guid.NewGuid().ToString("N")[..8],
            StatusId = statusId,
            RequestTypeId = typeId,
            DepartmentId = 4,
            CompanyId = 1,
            PlantId = 1,
            CurrencyId = 1,
            RequesterId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var li = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            LineNumber = 1,
            Description = "ZZTEST adjusted item",
            Quantity = 1,
            UnitPrice = 100m,
            TotalAmount = 100m,
            PlantId = 1,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.RequestLineItems.Add(li);

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            BatchNumber = 1,
            Status = batchStatus,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actor.Id
        };
        ctx.ApprovalBatches.Add(batch);
        ctx.ApprovalBatchItems.Add(new ApprovalBatchItem
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = batch.Id,
            RequestLineItemId = li.Id,
            CreatedAtUtc = DateTime.UtcNow
        });

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, batch.Id, li.Id, actor.Id);
    }

    private static ApprovalBatchAdjustment NewCycle(Seed seed, int cycleNumber, string status, string sourceStage = "FINAL") => new()
    {
        ApprovalBatchId = seed.BatchId,
        CycleNumber = cycleNumber,
        SourceStage = sourceStage,
        Status = status,
        WholeBatch = true,
        ApproverComment = "ZZTEST motivo do reajuste estruturado.",
        RequestedByUserId = seed.ActorId,
        RequestedAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static async Task CleanupAsync(Guid requestId)
    {
        if (requestId == Guid.Empty) return;
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.ExecuteSqlRawAsync(
            // Adjustment children cascade in the DATABASE from their cycle root.
            "DELETE a FROM ApprovalBatchAdjustments a INNER JOIN ApprovalBatches b ON b.Id = a.ApprovalBatchId WHERE b.RequestId = {0};" +
            "DELETE abi FROM ApprovalBatchItems abi INNER JOIN ApprovalBatches b ON b.Id = abi.ApprovalBatchId WHERE b.RequestId = {0};" +
            "DELETE FROM ApprovalBatches WHERE RequestId = {0};" +
            "DELETE FROM RequestLineItems WHERE RequestId = {0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId = {0};" +
            "DELETE FROM Requests WHERE Id = {0};" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-adj-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId = Users.Id);", requestId);
    }

    // ── A. Model / configuration ──────────────────────────────────────────────

    [Fact]
    public async Task Cycle_PersistsWithChildren_AndComputedIsOpen()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var cycle = NewCycle(seed, 1, AdjustmentConstants.States.WaitingBuyer);
                cycle.Reasons.Add(new ApprovalBatchAdjustmentReason { ReasonCode = AdjustmentConstants.ReasonCodes.PriceNegotiation, CreatedAtUtc = DateTime.UtcNow });
                cycle.Reasons.Add(new ApprovalBatchAdjustmentReason { ReasonCode = AdjustmentConstants.ReasonCodes.RequestedQuantity, RequestLineItemId = seed.LineItemId, CreatedAtUtc = DateTime.UtcNow });
                ctx.ApprovalBatchAdjustments.Add(cycle);
                await ctx.SaveChangesAsync();
            }

            await using (var verify = new ApplicationDbContext(Options()))
            {
                var loaded = await verify.ApprovalBatchAdjustments.AsNoTracking()
                    .Include(a => a.Reasons)
                    .SingleAsync(a => a.ApprovalBatchId == seed.BatchId);

                Assert.Equal(1, loaded.CycleNumber);
                Assert.Equal(AdjustmentConstants.SourceStages.Final, loaded.SourceStage);
                Assert.Equal(AdjustmentConstants.States.WaitingBuyer, loaded.Status);
                Assert.Contains(loaded.Status, AdjustmentConstants.States.Open); // an open-state cycle
                Assert.Equal(2, loaded.Reasons.Count);
                Assert.Contains(loaded.Reasons, r => r.RequestLineItemId == null);          // whole-lot reason
                Assert.Contains(loaded.Reasons, r => r.RequestLineItemId == seed.LineItemId); // item-scoped reason
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    [Fact]
    public async Task DuplicateCycleNumber_SameBatch_IsRejected_ButDifferentBatchesMayShare()
    {
        if (!CanConnect()) return;
        var seedA = await SeedAsync();
        var seedB = await SeedAsync();
        if (seedA == null || seedB == null) { await CleanupAsync(seedA?.RequestId ?? Guid.Empty); await CleanupAsync(seedB?.RequestId ?? Guid.Empty); return; }
        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                ctx.ApprovalBatchAdjustments.Add(NewCycle(seedA, 1, AdjustmentConstants.States.Resubmitted));
                // Same cycle number on a DIFFERENT batch is legal.
                ctx.ApprovalBatchAdjustments.Add(NewCycle(seedB, 1, AdjustmentConstants.States.Resubmitted));
                await ctx.SaveChangesAsync();
            }

            await using (var dup = new ApplicationDbContext(Options()))
            {
                dup.ApprovalBatchAdjustments.Add(NewCycle(seedA, 1, AdjustmentConstants.States.Cancelled));
                await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
            }
        }
        finally { await CleanupAsync(seedA.RequestId); await CleanupAsync(seedB.RequestId); }
    }

    [Fact]
    public async Task SecondOpenCycle_SameBatch_IsRejected_ClosedPlusOpenIsAllowed()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                // Cycle 1 closed, cycle 2 open — the multiple-historical-cycles shape.
                var closed = NewCycle(seed, 1, AdjustmentConstants.States.Resubmitted);
                closed.ClosedAtUtc = DateTime.UtcNow;
                ctx.ApprovalBatchAdjustments.Add(closed);
                ctx.ApprovalBatchAdjustments.Add(NewCycle(seed, 2, AdjustmentConstants.States.WaitingRequester));
                await ctx.SaveChangesAsync(); // closed + open on the same batch is legal
            }

            await using (var dup = new ApplicationDbContext(Options()))
            {
                // A THIRD cycle in an open state violates the filtered unique index.
                dup.ApprovalBatchAdjustments.Add(NewCycle(seed, 3, AdjustmentConstants.States.WaitingBuyer));
                await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── B. Reasons ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApprovedReasonCatalog_PersistsInFull()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var cycle = NewCycle(seed, 1, AdjustmentConstants.States.WaitingBuyer);
                foreach (var code in AdjustmentConstants.ReasonCodes.All)
                    cycle.Reasons.Add(new ApprovalBatchAdjustmentReason { ReasonCode = code, CreatedAtUtc = DateTime.UtcNow });
                cycle.Reasons.Single(r => r.ReasonCode == AdjustmentConstants.ReasonCodes.Other).Detail = "Contexto do motivo OTHER.";
                ctx.ApprovalBatchAdjustments.Add(cycle);
                await ctx.SaveChangesAsync();
            }

            await using (var verify = new ApplicationDbContext(Options()))
            {
                var codes = await verify.ApprovalBatchAdjustmentReasons.AsNoTracking()
                    .Where(r => r.Adjustment.ApprovalBatchId == seed.BatchId)
                    .Select(r => r.ReasonCode)
                    .ToListAsync();
                Assert.Equal(AdjustmentConstants.ReasonCodes.All.OrderBy(c => c), codes.OrderBy(c => c));
                // SUPPLIER and SUPPLIER_DELIVERY_TIME are distinct catalog entries (decision OD5).
                Assert.Contains(AdjustmentConstants.ReasonCodes.Supplier, codes);
                Assert.Contains(AdjustmentConstants.ReasonCodes.SupplierDeliveryTime, codes);
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    [Fact]
    public async Task DuplicateWholeLotReason_IsRejected_ByUnfilteredUniqueIndex()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            Guid adjustmentId;
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var cycle = NewCycle(seed, 1, AdjustmentConstants.States.WaitingBuyer);
                cycle.Reasons.Add(new ApprovalBatchAdjustmentReason { ReasonCode = AdjustmentConstants.ReasonCodes.Supplier, CreatedAtUtc = DateTime.UtcNow });
                ctx.ApprovalBatchAdjustments.Add(cycle);
                await ctx.SaveChangesAsync();
                adjustmentId = cycle.Id;
            }

            await using (var dup = new ApplicationDbContext(Options()))
            {
                // Regression pin for HasFilter(null): the NULL (whole-lot) scope participates in
                // the unique index — EF's default filtered index would have allowed this duplicate.
                dup.ApprovalBatchAdjustmentReasons.Add(new ApprovalBatchAdjustmentReason
                {
                    AdjustmentId = adjustmentId,
                    ReasonCode = AdjustmentConstants.ReasonCodes.Supplier,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── C. Resolutions ────────────────────────────────────────────────────────

    [Fact]
    public async Task RequesterAndBuyerResolutions_Persist_DuplicateActorRejected()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            Guid adjustmentId;
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var cycle = NewCycle(seed, 1, AdjustmentConstants.States.Resubmitted);
                cycle.Resolutions.Add(new ApprovalBatchAdjustmentResolution
                {
                    ActorType = AdjustmentConstants.ActorTypes.Requester,
                    ResolvedByUserId = seed.ActorId,
                    ResolutionComment = "Quantidade alterada de 20 para 15 unidades conforme solicitado.",
                    ResolvedAtUtc = DateTime.UtcNow
                });
                cycle.Resolutions.Add(new ApprovalBatchAdjustmentResolution
                {
                    ActorType = AdjustmentConstants.ActorTypes.Buyer,
                    ResolvedByUserId = seed.ActorId,
                    ResolutionComment = "Nova cotação incluída e valor negociado.",
                    ResolvedAtUtc = DateTime.UtcNow
                });
                ctx.ApprovalBatchAdjustments.Add(cycle);
                await ctx.SaveChangesAsync();
                adjustmentId = cycle.Id;
            }

            await using (var dup = new ApplicationDbContext(Options()))
            {
                dup.ApprovalBatchAdjustmentResolutions.Add(new ApprovalBatchAdjustmentResolution
                {
                    AdjustmentId = adjustmentId,
                    ActorType = AdjustmentConstants.ActorTypes.Buyer,
                    ResolvedByUserId = seed.ActorId,
                    ResolutionComment = "Segunda resposta do comprador não é permitida no mesmo ciclo.",
                    ResolvedAtUtc = DateTime.UtcNow
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── D. Field changes ──────────────────────────────────────────────────────

    [Fact]
    public async Task FieldChange_PersistsControlledCatalog_WithLineItemRelation()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var cycle = NewCycle(seed, 1, AdjustmentConstants.States.WaitingBuyer);
                cycle.FieldChanges.Add(new ApprovalBatchAdjustmentFieldChange
                {
                    RequestLineItemId = seed.LineItemId,
                    FieldCode = AdjustmentConstants.FieldCodes.RequestedQuantity,
                    OldValue = "20",
                    NewValue = "15",
                    ChangedByUserId = seed.ActorId,
                    ChangedAtUtc = DateTime.UtcNow
                });
                ctx.ApprovalBatchAdjustments.Add(cycle);
                await ctx.SaveChangesAsync();
            }

            await using (var verify = new ApplicationDbContext(Options()))
            {
                var fc = await verify.ApprovalBatchAdjustmentFieldChanges.AsNoTracking()
                    .Include(x => x.RequestLineItem)
                    .SingleAsync(x => x.Adjustment.ApprovalBatchId == seed.BatchId);
                Assert.Equal(AdjustmentConstants.FieldCodes.RequestedQuantity, fc.FieldCode);
                Assert.Equal("20", fc.OldValue);
                Assert.Equal("15", fc.NewValue);
                Assert.Equal(seed.LineItemId, fc.RequestLineItem.Id);
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── E. Candidate review ───────────────────────────────────────────────────

    [Fact]
    public async Task CandidateReview_PersistsActions_NoFkOnIdentities_DuplicateRejected()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            Guid adjustmentId;
            // Identities that exist in NO table — proving the deliberate absence of FK constraints
            // (the review audit must survive candidate/item deletion on REPLACE/REMOVE).
            var ghostItemId = Guid.NewGuid();
            var ghostQuotationItemId = Guid.NewGuid();

            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var cycle = NewCycle(seed, 1, AdjustmentConstants.States.WaitingBuyer);
                foreach (var (state, index) in AdjustmentConstants.CandidateReviewStates.All.Select((s, i) => (s, i)))
                {
                    cycle.CandidateReviews.Add(new ApprovalBatchCandidateReview
                    {
                        ApprovalBatchItemId = ghostItemId,
                        QuotationItemId = index == 0 ? ghostQuotationItemId : Guid.NewGuid(),
                        TriggerReason = AdjustmentConstants.CandidateReviewTriggers.QuantityChanged,
                        Status = state,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
                ctx.ApprovalBatchAdjustments.Add(cycle);
                await ctx.SaveChangesAsync();
                adjustmentId = cycle.Id;
            }

            await using (var verify = new ApplicationDbContext(Options()))
            {
                var states = await verify.ApprovalBatchCandidateReviews.AsNoTracking()
                    .Where(cr => cr.AdjustmentId == adjustmentId)
                    .Select(cr => cr.Status)
                    .ToListAsync();
                Assert.Equal(AdjustmentConstants.CandidateReviewStates.All.OrderBy(s => s), states.OrderBy(s => s));
            }

            await using (var dup = new ApplicationDbContext(Options()))
            {
                // Same (cycle, item, quotation line) twice → unique index violation.
                dup.ApprovalBatchCandidateReviews.Add(new ApprovalBatchCandidateReview
                {
                    AdjustmentId = adjustmentId,
                    ApprovalBatchItemId = ghostItemId,
                    QuotationItemId = ghostQuotationItemId,
                    TriggerReason = AdjustmentConstants.CandidateReviewTriggers.UnitChanged,
                    Status = AdjustmentConstants.CandidateReviewStates.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── F. Delete / FK safety ─────────────────────────────────────────────────

    [Fact]
    public async Task DeletingBatch_CascadesAdjustmentAggregate()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            Guid adjustmentId;
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var cycle = NewCycle(seed, 1, AdjustmentConstants.States.Resubmitted);
                cycle.Reasons.Add(new ApprovalBatchAdjustmentReason { ReasonCode = AdjustmentConstants.ReasonCodes.BatchComposition, CreatedAtUtc = DateTime.UtcNow });
                cycle.Resolutions.Add(new ApprovalBatchAdjustmentResolution { ActorType = AdjustmentConstants.ActorTypes.Buyer, ResolvedByUserId = seed.ActorId, ResolutionComment = "Composição revisada e lote reenviado.", ResolvedAtUtc = DateTime.UtcNow });
                cycle.CandidateReviews.Add(new ApprovalBatchCandidateReview { ApprovalBatchItemId = Guid.NewGuid(), QuotationItemId = Guid.NewGuid(), TriggerReason = AdjustmentConstants.CandidateReviewTriggers.SpecificationChanged, Status = AdjustmentConstants.CandidateReviewStates.Confirmed, CreatedAtUtc = DateTime.UtcNow });
                ctx.ApprovalBatchAdjustments.Add(cycle);
                await ctx.SaveChangesAsync();
                adjustmentId = cycle.Id;
            }

            await using (var del = new ApplicationDbContext(Options()))
            {
                // Deleting the batch removes the whole adjustment aggregate WITH it (ownership,
                // same convention as Items/ExtraItemDecisions) — batch items first (their own FK).
                await del.Database.ExecuteSqlRawAsync(
                    "DELETE FROM ApprovalBatchItems WHERE ApprovalBatchId = {0}; DELETE FROM ApprovalBatches WHERE Id = {0};", seed.BatchId);

                Assert.Equal(0, await del.ApprovalBatchAdjustments.CountAsync(a => a.Id == adjustmentId));
                Assert.Equal(0, await del.ApprovalBatchAdjustmentReasons.CountAsync(r => r.AdjustmentId == adjustmentId));
                Assert.Equal(0, await del.ApprovalBatchAdjustmentResolutions.CountAsync(r => r.AdjustmentId == adjustmentId));
                Assert.Equal(0, await del.ApprovalBatchCandidateReviews.CountAsync(r => r.AdjustmentId == adjustmentId));
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    [Fact]
    public async Task DeletingReferencedLineItem_IsBlocked_UntilAdjustmentRowsAreRemoved()
    {
        if (!CanConnect()) return;
        var seed = await SeedAsync();
        if (seed == null) return;
        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var cycle = NewCycle(seed, 1, AdjustmentConstants.States.WaitingBuyer);
                cycle.Reasons.Add(new ApprovalBatchAdjustmentReason { ReasonCode = AdjustmentConstants.ReasonCodes.RequestedQuantity, RequestLineItemId = seed.LineItemId, CreatedAtUtc = DateTime.UtcNow });
                ctx.ApprovalBatchAdjustments.Add(cycle);
                await ctx.SaveChangesAsync();
            }

            await using (var del = new ApplicationDbContext(Options()))
            {
                // NoAction FK: hard-deleting a line item still referenced by the adjustment audit
                // must FAIL (batch item removed first so only the new FK is under test).
                await del.Database.ExecuteSqlRawAsync("DELETE FROM ApprovalBatchItems WHERE RequestLineItemId = {0};", seed.LineItemId);
                await Assert.ThrowsAnyAsync<Exception>(() =>
                    del.Database.ExecuteSqlRawAsync("DELETE FROM RequestLineItems WHERE Id = {0};", seed.LineItemId));

                // After removing the adjustment rows, the same delete succeeds.
                await del.Database.ExecuteSqlRawAsync(
                    "DELETE a FROM ApprovalBatchAdjustments a WHERE a.ApprovalBatchId = {0};", seed.BatchId);
                await del.Database.ExecuteSqlRawAsync("DELETE FROM RequestLineItems WHERE Id = {0};", seed.LineItemId);
                Assert.Equal(0, await del.RequestLineItems.CountAsync(li => li.Id == seed.LineItemId));
            }
        }
        finally { await CleanupAsync(seed.RequestId); }
    }

    // ── G. Schema presence ────────────────────────────────────────────────────

    [Fact]
    public async Task AllFivePhase2Tables_ExistInSchema()
    {
        if (!CanConnect()) return;
        await using var ctx = new ApplicationDbContext(Options());
        foreach (var table in new[]
                 {
                     "ApprovalBatchAdjustments", "ApprovalBatchAdjustmentReasons",
                     "ApprovalBatchAdjustmentResolutions", "ApprovalBatchAdjustmentFieldChanges",
                     "ApprovalBatchCandidateReviews"
                 })
        {
            var id = await ctx.Database
                .SqlQueryRaw<int>($"SELECT ISNULL(OBJECT_ID('dbo.{table}'), 0) AS [Value]")
                .SingleAsync();
            Assert.True(id != 0, $"Table {table} missing from the schema.");
        }
    }
}
