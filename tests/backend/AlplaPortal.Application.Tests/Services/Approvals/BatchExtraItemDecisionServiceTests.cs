using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Covers IBatchExtraItemDecisionService — the shared Buyer batch-composition decision service
/// used identically by CreateBatch and UpdateBatch. Scenario mirrors request REQ-15/07/2026-075's
/// shape (1 requested item, 1 winning SUBSTITUTE line, 1 genuine EXTRA_ITEM line, 1 IGNORED line)
/// so the tests double as regression coverage for the investigated incident.
/// </summary>
public class BatchExtraItemDecisionServiceTests
{
    private static ApplicationDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class Scenario
    {
        public ApplicationDbContext Ctx = null!;
        public Request Request = null!;
        public RequestLineItem RequestedItem = null!;
        public ApprovalBatch Batch = null!;
        public QuotationItem WinnerItem = null!;
        public QuotationItem ExtraItem = null!;
        public QuotationItem IgnoredItem = null!;
        public Guid ActorId;
    }

    private const string ValidJustification = "Fornecedor propôs item adicional para complementar o pedido original.";
    private const string ValidExcludeComment = "Item não será adquirido neste momento por decisão do comprador.";

    private static async Task<Scenario> SeedAsync()
    {
        var ctx = GetInMemoryDbContext();
        var actorId = Guid.NewGuid();

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            RequestedDateUtc = DateTime.UtcNow,
            StatusId = 1
        };

        var requestedItem = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            LineNumber = 1,
            Description = "Martelo de Bronze/Latão 500g",
            Quantity = 1,
            UnitPrice = 100,
            TotalAmount = 100,
            IsDeleted = false
        };
        request.LineItems.Add(requestedItem);

        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "Fornecedor Teste",
            DocumentNumber = "DOC-1",
            Currency = "AOA"
        };

        var winnerItem = new QuotationItem
        {
            Id = Guid.NewGuid(),
            QuotationId = quotation.Id,
            Description = "Marreta Octogonal Bronze 450g",
            ReconciliationStatus = "SUBSTITUTE",
            MappedRequestLineItemId = requestedItem.Id,
            Quantity = 1,
            UnitPrice = 100,
            LineTotal = 100
        };
        var extraItem = new QuotationItem
        {
            Id = Guid.NewGuid(),
            QuotationId = quotation.Id,
            Description = "Marreta Octogonal Bronze 1KG",
            ReconciliationStatus = "EXTRA_ITEM",
            Quantity = 6,
            UnitPrice = 50,
            LineTotal = 300,
            ReconciliationJustification = ValidJustification
        };
        var ignoredItem = new QuotationItem
        {
            Id = Guid.NewGuid(),
            QuotationId = quotation.Id,
            Description = "Marreta Octogonal Bronze 2KG",
            ReconciliationStatus = "IGNORED",
            Quantity = 6,
            UnitPrice = 80,
            LineTotal = 480,
            ReconciliationJustification = "Fornecedor não trabalha mais com este item específico do documento."
        };

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            BatchNumber = 1,
            Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorId
        };
        var batchItem = new ApprovalBatchItem
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = batch.Id,
            RequestLineItemId = requestedItem.Id,
            SelectedQuotationItemId = winnerItem.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        batch.Items.Add(batchItem);

        ctx.Requests.Add(request);
        ctx.Quotations.Add(quotation);
        ctx.QuotationItems.AddRange(winnerItem, extraItem, ignoredItem);
        ctx.ApprovalBatches.Add(batch);
        ctx.ApprovalBatchItems.Add(batchItem);
        await ctx.SaveChangesAsync();

        return new Scenario
        {
            Ctx = ctx,
            Request = request,
            RequestedItem = requestedItem,
            Batch = batch,
            WinnerItem = winnerItem,
            ExtraItem = extraItem,
            IgnoredItem = ignoredItem,
            ActorId = actorId
        };
    }

    private static List<Guid> WinningIds(Scenario s) => new() { s.WinnerItem.Id };

    [Fact]
    public async Task ApplyAsync_ExtraItemWithNoDecision_ReturnsPendingDecisions_IgnoredLineNeverPending()
    {
        var s = await SeedAsync();
        var service = new BatchExtraItemDecisionService(s.Ctx);

        var result = await service.ApplyAsync(s.Request, s.Batch, WinningIds(s), decisions: null, s.ActorId);

        Assert.False(result.Success);
        Assert.Single(result.PendingItems);
        Assert.Equal(s.ExtraItem.Id, result.PendingItems[0].QuotationItemId);
        // IGNORED line must never appear as pending — it's a complete, valid, already-justified state.
        Assert.DoesNotContain(result.PendingItems, p => p.QuotationItemId == s.IgnoredItem.Id);
    }

    [Fact]
    public async Task ApplyAsync_NoExtraItemLines_ReturnsOkWithoutAnyDecisionRequired()
    {
        var s = await SeedAsync();
        // Reduce winning ids to a quotation with no EXTRA_ITEM/IGNORED siblings by using a fresh, isolated quotation.
        var ctx = s.Ctx;
        var quotation2 = new Quotation { Id = Guid.NewGuid(), RequestId = s.Request.Id, SupplierNameSnapshot = "Outro", Currency = "AOA" };
        var mappedOnly = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation2.Id, Description = "Item simples", ReconciliationStatus = "MAPPED", MappedRequestLineItemId = s.RequestedItem.Id, Quantity = 1, UnitPrice = 10, LineTotal = 10 };
        ctx.Quotations.Add(quotation2);
        ctx.QuotationItems.Add(mappedOnly);
        await ctx.SaveChangesAsync();

        var service = new BatchExtraItemDecisionService(ctx);
        var result = await service.ApplyAsync(s.Request, s.Batch, new List<Guid> { mappedOnly.Id }, decisions: null, s.ActorId);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ApplyAsync_Include_CreatesRequestLineItemAndBatchItemAndDecision()
    {
        var s = await SeedAsync();
        var service = new BatchExtraItemDecisionService(s.Ctx);
        var decisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "INCLUDE", Comment = ValidJustification }
        };

        var result = await service.ApplyAsync(s.Request, s.Batch, WinningIds(s), decisions, s.ActorId);
        await s.Ctx.SaveChangesAsync();

        Assert.True(result.Success);

        var newLineItem = s.Ctx.RequestLineItems.Local.FirstOrDefault(li => li.Id != s.RequestedItem.Id);
        Assert.NotNull(newLineItem);
        Assert.Equal(LineItemCreationOrigins.BuyerExtraItemIncluded, newLineItem!.CreationOrigin);
        Assert.False(newLineItem.IsDeleted);
        Assert.Contains("[Item Adicional]", newLineItem.Description);

        // Candidate model: the included extra becomes a batch item with NO winner and exactly ONE
        // frozen candidate — the Area Approver still confirms it via winner selection.
        var newBatchItem = s.Batch.Items.FirstOrDefault(bi => bi.RequestLineItemId == newLineItem.Id);
        Assert.NotNull(newBatchItem);
        Assert.Null(newBatchItem!.SelectedQuotationItemId);
        var candidate = s.Ctx.ApprovalBatchItemCandidates.Local.Single(c => c.ApprovalBatchItemId == newBatchItem.Id);
        Assert.Equal(s.ExtraItem.Id, candidate.QuotationItemId);
        Assert.Equal(s.ExtraItem.LineTotal, candidate.LineTotal);

        var decisionRow = await s.Ctx.ApprovalBatchExtraItemDecisions
            .FirstOrDefaultAsync(d => d.ApprovalBatchId == s.Batch.Id && d.QuotationItemId == s.ExtraItem.Id);
        Assert.NotNull(decisionRow);
        Assert.Equal(ExtraItemDecisionValues.Include, decisionRow!.Decision);
        Assert.Equal(newLineItem.Id, decisionRow.GeneratedRequestLineItemId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("curto")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task ApplyAsync_Exclude_WithInvalidComment_ReturnsValidationFailed(string comment)
    {
        var s = await SeedAsync();
        var service = new BatchExtraItemDecisionService(s.Ctx);
        var decisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "EXCLUDE", Comment = comment }
        };

        var result = await service.ApplyAsync(s.Request, s.Batch, WinningIds(s), decisions, s.ActorId);

        Assert.False(result.Success);
        Assert.True(result.ValidationFailed);
        Assert.False(s.Ctx.ApprovalBatchExtraItemDecisions.Local.Any());
    }

    [Fact]
    public async Task ApplyAsync_Exclude_WithValidComment_PersistsDecision_NoLineItemCreated()
    {
        var s = await SeedAsync();
        var service = new BatchExtraItemDecisionService(s.Ctx);
        var decisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "EXCLUDE", Comment = ValidExcludeComment }
        };

        var result = await service.ApplyAsync(s.Request, s.Batch, WinningIds(s), decisions, s.ActorId);
        await s.Ctx.SaveChangesAsync();

        Assert.True(result.Success);
        Assert.Single(s.Batch.Items); // only the original winner — no line item created for the exclusion
        var decisionRow = await s.Ctx.ApprovalBatchExtraItemDecisions
            .FirstOrDefaultAsync(d => d.ApprovalBatchId == s.Batch.Id && d.QuotationItemId == s.ExtraItem.Id);
        Assert.NotNull(decisionRow);
        Assert.Equal(ExtraItemDecisionValues.Exclude, decisionRow!.Decision);
        Assert.Equal(ValidExcludeComment, decisionRow.Comment);
        Assert.Null(decisionRow.GeneratedRequestLineItemId);
    }

    [Fact]
    public async Task ApplyAsync_ReversalIncludeToExclude_WhenSafe_SoftDeletesGeneratedLineItem()
    {
        var s = await SeedAsync();
        var service = new BatchExtraItemDecisionService(s.Ctx);

        // First, include.
        var includeDecisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "INCLUDE", Comment = ValidJustification }
        };
        var includeResult = await service.ApplyAsync(s.Request, s.Batch, WinningIds(s), includeDecisions, s.ActorId);
        await s.Ctx.SaveChangesAsync();
        Assert.True(includeResult.Success);

        var generatedLineItem = s.Ctx.RequestLineItems.Local.First(li => li.Id != s.RequestedItem.Id);

        // Now reverse to exclude, in a fresh service instance against the same context (simulates UpdateBatch rework).
        var reworkService = new BatchExtraItemDecisionService(s.Ctx);
        var excludeDecisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "EXCLUDE", Comment = ValidExcludeComment }
        };
        var reversalResult = await reworkService.ApplyAsync(s.Request, s.Batch, WinningIds(s), excludeDecisions, s.ActorId);
        await s.Ctx.SaveChangesAsync();

        Assert.True(reversalResult.Success);
        Assert.True(generatedLineItem.IsDeleted);
        Assert.DoesNotContain(s.Batch.Items, bi => bi.SelectedQuotationItemId == s.ExtraItem.Id);

        var decisionRow = await s.Ctx.ApprovalBatchExtraItemDecisions
            .FirstAsync(d => d.ApprovalBatchId == s.Batch.Id && d.QuotationItemId == s.ExtraItem.Id);
        Assert.Equal(ExtraItemDecisionValues.Exclude, decisionRow.Decision);
        // Audit trail preserved — the generated line's id is not erased even though it's now soft-deleted.
        Assert.Equal(generatedLineItem.Id, decisionRow.GeneratedRequestLineItemId);
    }

    [Fact]
    public async Task ApplyAsync_ReversalIncludeToExclude_WhenAllocationExists_ReturnsLocked()
    {
        var s = await SeedAsync();
        var service = new BatchExtraItemDecisionService(s.Ctx);

        var includeDecisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "INCLUDE", Comment = ValidJustification }
        };
        await service.ApplyAsync(s.Request, s.Batch, WinningIds(s), includeDecisions, s.ActorId);
        await s.Ctx.SaveChangesAsync();

        var generatedLineItem = s.Ctx.RequestLineItems.Local.First(li => li.Id != s.RequestedItem.Id);
        s.Ctx.RequestLineItemAllocations.Add(new RequestLineItemAllocation
        {
            Id = Guid.NewGuid(),
            RequestLineItemId = generatedLineItem.Id,
            PlantId = 1,
            Percentage = 100,
            AllocationOrder = 0,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = s.ActorId
        });
        await s.Ctx.SaveChangesAsync();

        var reworkService = new BatchExtraItemDecisionService(s.Ctx);
        var excludeDecisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "EXCLUDE", Comment = ValidExcludeComment }
        };
        var reversalResult = await reworkService.ApplyAsync(s.Request, s.Batch, WinningIds(s), excludeDecisions, s.ActorId);

        Assert.False(reversalResult.Success);
        Assert.True(reversalResult.ReversalLocked);
        Assert.Equal(s.ExtraItem.Id, reversalResult.ReversalLockedQuotationItemId);
        Assert.False(generatedLineItem.IsDeleted); // untouched — nothing partially applied
    }

    [Fact]
    public async Task GetInformationalLinesAsync_ReturnsThreeSeparateLists_MatchingRequest075Shape()
    {
        var s = await SeedAsync();
        var service = new BatchExtraItemDecisionService(s.Ctx);

        // Nothing decided yet — the EXTRA_ITEM line is unresolved-legacy, the IGNORED line is always informational.
        var linesBeforeDecision = await service.GetInformationalLinesAsync(s.Batch.Id, WinningIds(s));
        Assert.Empty(linesBeforeDecision.ExcludedExtraItems);
        Assert.Single(linesBeforeDecision.IgnoredLines);
        Assert.Equal(s.IgnoredItem.Id, linesBeforeDecision.IgnoredLines[0].QuotationItemId);
        Assert.Single(linesBeforeDecision.UnresolvedLegacyLines);
        Assert.Equal(s.ExtraItem.Id, linesBeforeDecision.UnresolvedLegacyLines[0].QuotationItemId);

        // Decide EXCLUDE — must move from UnresolvedLegacyLines to ExcludedExtraItems, IGNORED line unaffected.
        var decisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "EXCLUDE", Comment = ValidExcludeComment }
        };
        await service.ApplyAsync(s.Request, s.Batch, WinningIds(s), decisions, s.ActorId);
        await s.Ctx.SaveChangesAsync();

        var linesAfterDecision = await service.GetInformationalLinesAsync(s.Batch.Id, WinningIds(s));
        Assert.Empty(linesAfterDecision.UnresolvedLegacyLines);
        Assert.Single(linesAfterDecision.ExcludedExtraItems);
        Assert.Equal(ValidExcludeComment, linesAfterDecision.ExcludedExtraItems[0].Comment);
        Assert.Single(linesAfterDecision.IgnoredLines);
    }

    // ── Backward compatibility: pre-existing ApprovalBatchExtraItemDecision rows using the old
    // BatchAreaApprove-era APPROVE/REJECT vocabulary must never crash, duplicate, or become
    // invisible. Found during a Phase 2 validation review; fixed via IsIncludeDecision/IsExcludeDecision. ──

    [Fact]
    public async Task ApplyAsync_ReSubmittingIncludeForLegacyApproveRow_IsIdempotent_DoesNotDuplicateLineItem()
    {
        var s = await SeedAsync();
        var preExistingLineItem = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = s.Request.Id,
            LineNumber = 2,
            Description = "[Item Adicional] " + s.ExtraItem.Description,
            Quantity = s.ExtraItem.Quantity,
            UnitPrice = s.ExtraItem.UnitPrice,
            TotalAmount = s.ExtraItem.LineTotal,
            CreationOrigin = LineItemCreationOrigins.BuyerExtraItemIncluded,
            IsDeleted = false
        };
        s.Ctx.RequestLineItems.Add(preExistingLineItem);
        var preExistingBatchItem = new ApprovalBatchItem
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = s.Batch.Id,
            RequestLineItemId = preExistingLineItem.Id,
            SelectedQuotationItemId = s.ExtraItem.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        s.Ctx.ApprovalBatchItems.Add(preExistingBatchItem);
        s.Ctx.ApprovalBatchExtraItemDecisions.Add(new ApprovalBatchExtraItemDecision
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = s.Batch.Id,
            QuotationItemId = s.ExtraItem.Id,
            Decision = "APPROVE", // legacy value, predates the INCLUDE/EXCLUDE rename
            Comment = null,
            GeneratedRequestLineItemId = preExistingLineItem.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            CreatedByUserId = s.ActorId
        });
        await s.Ctx.SaveChangesAsync();

        var service = new BatchExtraItemDecisionService(s.Ctx);
        var decisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "INCLUDE", Comment = ValidJustification }
        };
        var result = await service.ApplyAsync(s.Request, s.Batch, WinningIds(s), decisions, s.ActorId);
        await s.Ctx.SaveChangesAsync();

        Assert.True(result.Success);
        // Exactly one RequestLineItem for this quotation item — no duplicate created.
        Assert.Single(s.Ctx.RequestLineItems.Local.Where(li => li.Id != s.RequestedItem.Id));
        Assert.Single(s.Batch.Items.Where(bi => bi.SelectedQuotationItemId == s.ExtraItem.Id));
    }

    [Fact]
    public async Task ApplyAsync_ExcludingLegacyApproveRow_GoesThroughReversalSafetyCheck()
    {
        var s = await SeedAsync();
        var preExistingLineItem = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = s.Request.Id,
            LineNumber = 2,
            Description = "[Item Adicional] " + s.ExtraItem.Description,
            Quantity = s.ExtraItem.Quantity,
            UnitPrice = s.ExtraItem.UnitPrice,
            TotalAmount = s.ExtraItem.LineTotal,
            CreationOrigin = LineItemCreationOrigins.BuyerExtraItemIncluded,
            IsDeleted = false
        };
        s.Ctx.RequestLineItems.Add(preExistingLineItem);
        s.Ctx.ApprovalBatchItems.Add(new ApprovalBatchItem
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = s.Batch.Id,
            RequestLineItemId = preExistingLineItem.Id,
            SelectedQuotationItemId = s.ExtraItem.Id,
            CreatedAtUtc = DateTime.UtcNow
        });
        s.Ctx.ApprovalBatchExtraItemDecisions.Add(new ApprovalBatchExtraItemDecision
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = s.Batch.Id,
            QuotationItemId = s.ExtraItem.Id,
            Decision = "APPROVE", // legacy value
            Comment = null,
            GeneratedRequestLineItemId = preExistingLineItem.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            CreatedByUserId = s.ActorId
        });
        // Downstream reference — a real allocation already exists for the legacy-approved line.
        s.Ctx.RequestLineItemAllocations.Add(new RequestLineItemAllocation
        {
            Id = Guid.NewGuid(),
            RequestLineItemId = preExistingLineItem.Id,
            PlantId = 1,
            Percentage = 100,
            AllocationOrder = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await s.Ctx.SaveChangesAsync();

        var service = new BatchExtraItemDecisionService(s.Ctx);
        var decisions = new Dictionary<Guid, ExtraItemDecisionDto>
        {
            [s.ExtraItem.Id] = new ExtraItemDecisionDto { Decision = "EXCLUDE", Comment = ValidExcludeComment }
        };
        var result = await service.ApplyAsync(s.Request, s.Batch, WinningIds(s), decisions, s.ActorId);

        // Must go through the reversal-safety path (not silently overwrite the decision while
        // leaving the line item in place) and correctly block on the existing allocation.
        Assert.False(result.Success);
        Assert.True(result.ReversalLocked);
        Assert.False(preExistingLineItem.IsDeleted);
    }

    [Fact]
    public async Task GetInformationalLinesAsync_LegacyRejectRow_AppearsInExcludedExtraItems_NotInvisible()
    {
        var s = await SeedAsync();
        // Old BatchAreaApprove REJECT: audit-only, no RequestLineItem was ever created for it.
        s.Ctx.ApprovalBatchExtraItemDecisions.Add(new ApprovalBatchExtraItemDecision
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = s.Batch.Id,
            QuotationItemId = s.ExtraItem.Id,
            Decision = "REJECT", // legacy value
            Comment = "Motivo histórico do rejeitamento pelo aprovador de área.",
            GeneratedRequestLineItemId = null,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            CreatedByUserId = s.ActorId
        });
        await s.Ctx.SaveChangesAsync();

        var service = new BatchExtraItemDecisionService(s.Ctx);
        var lines = await service.GetInformationalLinesAsync(s.Batch.Id, WinningIds(s));

        Assert.Empty(lines.UnresolvedLegacyLines); // has a decision — must not show as unresolved
        Assert.Single(lines.ExcludedExtraItems);    // must be visible, not silently dropped
        Assert.Equal(s.ExtraItem.Id, lines.ExcludedExtraItems[0].QuotationItemId);
    }

}
