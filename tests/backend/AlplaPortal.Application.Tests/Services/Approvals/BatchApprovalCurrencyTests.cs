using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Batch final approval (ApprovalBatchController.BatchFinalApprove) now records the request-level approved currency and
/// approval timestamp (docs §G.18), which it never did before: the currency comes from the amounts actually summed into
/// the batch (the PENDING groups it activates), the request value is combined across batches ("MULTIPLE" when they
/// differ), legacy cumulative approvals without a recorded currency are never inferred and never re-stamped, pending
/// groups are never counted, and approval permissions / status transitions / cumulative amounts are unchanged.
/// Flow cloned from BatchWorkflowNotificationTests (create → area approve with cheapest winners → final approve).
/// </summary>
public class BatchApprovalCurrencyTests
{
    private const string BudgetJustification = "Justificativa orçamental de teste com tamanho suficiente para o gate.";
    private static readonly decimal[] KwanzaTotals = { 253_080m, 660_060m, 266_760m, 328_320m };
    private static readonly decimal[] LuandaTotals = { 272_232m, 625_860m, 287_280m, 312_360m };

    private sealed record Seed(DbContextOptions<ApplicationDbContext> Options, Guid RequestId, Guid Actor, Guid[] LineIds, Guid[] KwanzaItems, Guid[] LuandaItems);

    private static async Task<Seed> SeedAsync(string luandaCurrency = "AOA")
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var ctx = new ApplicationDbContext(options);
        var actor = Guid.NewGuid();
        var quotationType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        var status = new RequestStatus { Id = 1, Code = "WAITING_QUOTATION", Name = "Em Cotação" };
        ctx.RequestTypes.Add(quotationType);
        ctx.RequestStatuses.Add(status);
        ctx.Currencies.Add(new Currency { Id = 900, Code = "AOA", Symbol = "Kz" });
        ctx.Departments.Add(new Department { Id = 940, Name = "ZZ Departamento" });
        ctx.Companies.Add(new Company { Id = 940, Name = "ZZ Companhia" });
        ctx.Users.Add(new User { Id = actor, FullName = "ZZ Aprovador", Email = "zz.aprovador@test.local" });
        var kwanza = new Supplier { Id = 9101, Name = "Kwanza Industrial", TaxId = "5417000101", PortalCode = "ZZK1" };
        var luanda = new Supplier { Id = 9102, Name = "Luanda Suprimentos", TaxId = "5417000102", PortalCode = "ZZL1" };
        ctx.Suppliers.AddRange(kwanza, luanda);

        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-TEST-CUR", Title = "ZZTEST moeda aprovação lote",
            StatusId = status.Id, Status = status, RequestTypeId = quotationType.Id, RequestType = quotationType,
            DepartmentId = 940, CompanyId = 940, CurrencyId = 900, CreatedAtUtc = DateTime.UtcNow, RequesterId = actor
        };
        ctx.Requests.Add(request);

        var lineIds = new Guid[4];
        for (var i = 0; i < 4; i++)
        {
            var li = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = i + 1, Description = $"Item {i + 1}", Quantity = 1, UnitPrice = 0, TotalAmount = 0, IsDeleted = false, CreatedAtUtc = DateTime.UtcNow };
            ctx.RequestLineItems.Add(li);
            lineIds[i] = li.Id;
        }

        Guid[] AddQuotation(Supplier supplier, string doc, decimal[] totals, string currency)
        {
            var q = new Quotation { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, SupplierNameSnapshot = supplier.Name, DocumentNumber = doc, DocumentDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), Currency = currency, SourceType = "MANUAL", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor };
            ctx.Quotations.Add(q);
            var ids = new Guid[4];
            for (var i = 0; i < 4; i++)
            {
                var qi = new QuotationItem { Id = Guid.NewGuid(), QuotationId = q.Id, LineNumber = i + 1, Description = $"Item {i + 1} — {supplier.Name}", ReconciliationStatus = "MAPPED", MappedRequestLineItemId = lineIds[i], Quantity = 1, UnitPrice = totals[i], GrossSubtotal = totals[i], IvaRatePercent = 0, IvaAmount = 0, LineTotal = totals[i] };
                ctx.QuotationItems.Add(qi);
                ids[i] = qi.Id;
            }
            return ids;
        }
        var kwanzaItems = AddQuotation(kwanza, "FP-KWZ-001", KwanzaTotals, "AOA");
        var luandaItems = AddQuotation(luanda, "FP-LDA-001", LuandaTotals, luandaCurrency);
        await ctx.SaveChangesAsync();
        return new Seed(options, request.Id, actor, lineIds, kwanzaItems, luandaItems);
    }

    private static ApprovalBatchController BuildController(ApplicationDbContext ctx, Guid actorId, params string[] roles)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new ApprovalRoutingResultDto { Managers = { new AreaManagerDto { UserId = actorId, FullName = "ZZ Manager" } } });
        routing.Setup(r => r.IsAreaManagerAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(false);
        var controller = new ApprovalBatchController(
            ctx, NullLogger<ApprovalBatchController>.Instance, new Mock<IRequestStatusSyncService>().Object,
            new GroupBuilderService(ctx), routing.Object, new QuotationItemEligibilityService(ctx),
            new BatchExtraItemDecisionService(ctx), new AdjustmentCycleService(ctx), new Mock<IWorkflowNotificationOrchestrator>().Object);
        var effective = roles.Length > 0 ? roles : new[] { RoleConstants.SystemAdministrator, RoleConstants.FinalApprover };
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()) };
        claims.AddRange(effective.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } };
        return controller;
    }

    private static CreateApprovalBatchDto BatchDto(Seed s, params int[] lineIndexes)
    {
        var dto = new CreateApprovalBatchDto { Items = new List<BatchItemDto>() };
        foreach (var i in lineIndexes)
            dto.Items.Add(new BatchItemDto { RequestLineItemId = s.LineIds[i], Candidates = { new BatchCandidateInputDto { QuotationItemId = s.KwanzaItems[i] }, new BatchCandidateInputDto { QuotationItemId = s.LuandaItems[i] } } });
        return dto;
    }

    private static async Task<Guid> CreateAndAreaApproveAsync(Seed s, params int[] lineIndexes)
    {
        Guid batchId;
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var ok = Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).CreateBatch(s.RequestId, BatchDto(s, lineIndexes)));
            batchId = Assert.IsType<ApprovalBatchDto>(ok.Value).Id;
        }
        BatchApprovalActionDto approve;
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var items = await ctx.ApprovalBatchItems.AsNoTracking().Include(bi => bi.Candidates).Where(bi => bi.ApprovalBatchId == batchId).ToListAsync();
            approve = new BatchApprovalActionDto { BudgetJustification = BudgetJustification, Comment = "Aprovado em teste.", Selections = new List<BatchWinnerSelectionDto>(), ItemAssignments = new Dictionary<Guid, ItemApprovalAssignmentDto>() };
            foreach (var bi in items)
            {
                var index = Array.IndexOf(s.LineIds, bi.RequestLineItemId);
                var cheapest = KwanzaTotals[index] <= LuandaTotals[index] ? s.KwanzaItems[index] : s.LuandaItems[index];
                approve.Selections.Add(new BatchWinnerSelectionDto { ApprovalBatchItemId = bi.Id, SelectedCandidateId = bi.Candidates.Single(c => c.QuotationItemId == cheapest).Id });
                approve.ItemAssignments[bi.RequestLineItemId] = new ItemApprovalAssignmentDto { PlantId = 1, CostCenterId = 1 };
            }
        }
        await using (var ctx = new ApplicationDbContext(s.Options))
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, approve));
        return batchId;
    }

    private static async Task FinalApproveAsync(Seed s, Guid batchId)
    {
        await using var ctx = new ApplicationDbContext(s.Options);
        Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchFinalApprove(s.RequestId, batchId, new BatchApprovalActionDto { Comment = "Aprovação final de teste." }));
    }

    private static async Task<Request> RequestAsync(Seed s)
    {
        await using var ctx = new ApplicationDbContext(s.Options);
        return await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId);
    }

    // Cheapest winners per line: Kwanza(0), Luanda(1), Kwanza(2), Luanda(3)
    private static decimal Cheapest(params int[] lines) => lines.Sum(i => Math.Min(KwanzaTotals[i], LuandaTotals[i]));

    [Fact]
    public async Task Single_batch_approval_records_the_single_currency_of_the_activated_groups_and_the_timestamp()
    {
        var s = await SeedAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);
        var batch = await CreateAndAreaApproveAsync(s, 0, 1, 2, 3);
        Assert.Null((await RequestAsync(s)).ApprovedCurrencyCode);                                   // area approval records nothing

        await FinalApproveAsync(s, batch);

        var r = await RequestAsync(s);
        Assert.Equal(Cheapest(0, 1, 2, 3), r.ApprovedTotalAmount);
        Assert.Equal("AOA", r.ApprovedCurrencyCode);
        Assert.NotNull(r.ApprovedAtUtc); Assert.True(r.ApprovedAtUtc >= before);
        await using var ctx = new ApplicationDbContext(s.Options);
        Assert.Equal(2, await ctx.RequestPoGroups.CountAsync(g => g.ApprovalBatchId == batch && g.Status == RequestConstants.PoGroupStatuses.WaitingPo)); // transitions unchanged
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.Approved, (await ctx.ApprovalBatches.SingleAsync(b => b.Id == batch)).Status);
    }

    [Fact]
    public async Task Cumulative_batches_in_one_currency_keep_the_currency_the_first_timestamp_and_sum_the_amounts()
    {
        var s = await SeedAsync();
        var b1 = await CreateAndAreaApproveAsync(s, 0, 1);
        await FinalApproveAsync(s, b1);
        var afterFirst = await RequestAsync(s);
        Assert.Equal(Cheapest(0, 1), afterFirst.ApprovedTotalAmount); Assert.Equal("AOA", afterFirst.ApprovedCurrencyCode);
        var firstStamp = afterFirst.ApprovedAtUtc!.Value;

        var b2 = await CreateAndAreaApproveAsync(s, 2, 3);
        Assert.Equal(Cheapest(0, 1), (await RequestAsync(s)).ApprovedTotalAmount);                    // pending groups of batch 2 are NOT counted
        await FinalApproveAsync(s, b2);

        var r = await RequestAsync(s);
        Assert.Equal(Cheapest(0, 1, 2, 3), r.ApprovedTotalAmount);
        Assert.Equal("AOA", r.ApprovedCurrencyCode);
        Assert.Equal(firstStamp, r.ApprovedAtUtc);                                                    // snapshot moment is the first recorded approval
    }

    [Fact]
    public async Task Winners_in_two_currencies_record_MULTIPLE_never_a_single_currency_scalar()
    {
        var s = await SeedAsync(luandaCurrency: "USD");                                              // Luanda wins lines 1 and 3 → an AOA group and a USD group
        var batch = await CreateAndAreaApproveAsync(s, 0, 1, 2, 3);
        await FinalApproveAsync(s, batch);

        var r = await RequestAsync(s);
        Assert.Equal("MULTIPLE", r.ApprovedCurrencyCode);
        Assert.Equal(Cheapest(0, 1, 2, 3), r.ApprovedTotalAmount);                                    // cumulative amount unchanged in behaviour (documented limitation: it adds currencies)
        Assert.NotNull(r.ApprovedAtUtc);
    }

    [Fact]
    public async Task Second_batch_in_another_currency_turns_a_recorded_single_currency_into_MULTIPLE()
    {
        var s = await SeedAsync(luandaCurrency: "USD");
        var b1 = await CreateAndAreaApproveAsync(s, 0, 2);                                           // Kwanza wins both → AOA
        await FinalApproveAsync(s, b1);
        Assert.Equal("AOA", (await RequestAsync(s)).ApprovedCurrencyCode);

        var b2 = await CreateAndAreaApproveAsync(s, 1, 3);                                           // Luanda wins both → USD
        await FinalApproveAsync(s, b2);
        Assert.Equal("MULTIPLE", (await RequestAsync(s)).ApprovedCurrencyCode);
    }

    [Fact]
    public async Task Legacy_cumulative_approval_without_recorded_currency_is_never_inferred_and_never_re_stamped()
    {
        var s = await SeedAsync();
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            // A batch approved before the currency existed: APPROVED, amount known, no currency/timestamp on the request.
            ctx.ApprovalBatches.Add(new ApprovalBatch { Id = Guid.NewGuid(), RequestId = s.RequestId, BatchNumber = 99, Status = RequestConstants.ApprovalBatchStatuses.Approved, ApprovedTotalAmount = 1000m, CreatedAtUtc = DateTime.UtcNow.AddDays(-30), CreatedByUserId = s.Actor });
            var r = await ctx.Requests.SingleAsync(x => x.Id == s.RequestId);
            r.ApprovedTotalAmount = 1000m; r.ApprovedCurrencyCode = null; r.ApprovedAtUtc = null;
            await ctx.SaveChangesAsync();
        }

        var batch = await CreateAndAreaApproveAsync(s, 0, 1);
        await FinalApproveAsync(s, batch);

        var after = await RequestAsync(s);
        Assert.Equal(1000m + Cheapest(0, 1), after.ApprovedTotalAmount);                               // cumulative amount preserved
        Assert.Null(after.ApprovedCurrencyCode);                                                      // legacy batch currency not inferred
        Assert.Null(after.ApprovedAtUtc);                                                             // no new historical approval date
    }

    [Fact]
    public async Task Final_approval_permission_is_unchanged()
    {
        var s = await SeedAsync();
        var batch = await CreateAndAreaApproveAsync(s, 0, 1, 2, 3);
        await using var ctx = new ApplicationDbContext(s.Options);
        var result = await BuildController(ctx, s.Actor, RoleConstants.Buyer).BatchFinalApprove(s.RequestId, batch, new BatchApprovalActionDto { Comment = "sem papel" });
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Null((await RequestAsync(s)).ApprovedCurrencyCode);
    }
    [Fact]
    public async Task First_approval_with_unknown_currency_records_the_timestamp_but_no_currency_and_a_later_known_batch_never_makes_it_known()
    {
        var s = await SeedAsync(luandaCurrency: "");                                                  // Luanda quotation without a currency
        var b1 = await CreateAndAreaApproveAsync(s, 1, 3);                                           // Luanda wins both → group(s) with blank currency
        await FinalApproveAsync(s, b1);
        var afterFirst = await RequestAsync(s);
        Assert.Null(afterFirst.ApprovedCurrencyCode);                                                  // unknown stays explicit
        Assert.NotNull(afterFirst.ApprovedAtUtc);                                                      // the approval itself did happen now
        Assert.Equal(Cheapest(1, 3), afterFirst.ApprovedTotalAmount);

        var b2 = await CreateAndAreaApproveAsync(s, 0, 2);                                           // Kwanza wins both → AOA
        await FinalApproveAsync(s, b2);
        var afterSecond = await RequestAsync(s);
        Assert.Null(afterSecond.ApprovedCurrencyCode);                                                 // a known later batch never upgrades an unknown prior cumulative
        Assert.Equal(afterFirst.ApprovedAtUtc, afterSecond.ApprovedAtUtc);
        Assert.Equal(Cheapest(0, 1, 2, 3), afterSecond.ApprovedTotalAmount);
    }

    [Fact]
    public async Task Known_currency_followed_by_an_unknown_batch_leaves_the_cumulative_currency_unknown()
    {
        var s = await SeedAsync(luandaCurrency: "");
        var b1 = await CreateAndAreaApproveAsync(s, 0, 2);                                           // Kwanza (AOA) wins both
        await FinalApproveAsync(s, b1);
        Assert.Equal("AOA", (await RequestAsync(s)).ApprovedCurrencyCode);
        var stamp = (await RequestAsync(s)).ApprovedAtUtc;

        var b2 = await CreateAndAreaApproveAsync(s, 1, 3);                                           // Luanda (no currency) wins both
        await FinalApproveAsync(s, b2);
        var r = await RequestAsync(s);
        Assert.Null(r.ApprovedCurrencyCode);                                                           // not provably single any more, not provably multiple either
        Assert.Equal(stamp, r.ApprovedAtUtc);
    }

    [Fact]
    public async Task Zero_total_fallback_takes_the_currency_from_the_candidates_quotations_that_produced_the_amount()
    {
        var s = await SeedAsync(luandaCurrency: "USD");
        var batch = await CreateAndAreaApproveAsync(s, 1, 3);                                        // Luanda (USD) wins both lines
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            // Simulate the legacy condition the fallback exists for: group totals at zero although the winners are priced.
            foreach (var g in await ctx.RequestPoGroups.Where(g => g.ApprovalBatchId == batch).ToListAsync()) { g.TotalAmount = 0m; g.CurrencyCode = null; }
            await ctx.SaveChangesAsync();
        }
        await FinalApproveAsync(s, batch);

        var r = await RequestAsync(s);
        Assert.Equal(Cheapest(1, 3), r.ApprovedTotalAmount);                                           // amount recomputed from the candidates
        Assert.Equal("USD", r.ApprovedCurrencyCode);                                                   // currency from the SAME candidates, not from the (blank) groups
        Assert.NotNull(r.ApprovedAtUtc);
    }

    [Fact]
    public async Task Group_currency_known_only_through_its_lookup_is_honoured()
    {
        var s = await SeedAsync();
        var batch = await CreateAndAreaApproveAsync(s, 0, 2);
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            foreach (var g in await ctx.RequestPoGroups.Where(g => g.ApprovalBatchId == batch).ToListAsync()) { g.CurrencyCode = null; g.CurrencyId = 900; }
            await ctx.SaveChangesAsync();
        }
        await FinalApproveAsync(s, batch);
        Assert.Equal("AOA", (await RequestAsync(s)).ApprovedCurrencyCode);
    }
}
