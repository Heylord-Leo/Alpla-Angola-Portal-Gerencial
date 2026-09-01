using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
/// Candidate-based approval model (Phase A): the Buyer submits candidate OPTIONS per requested
/// item; the AREA APPROVER selects exactly one winner per item at area approval (all-or-return);
/// commercial facts are frozen in ApprovalBatchItemCandidate snapshots at submission time and are
/// the financial truth for approval, audit, and PO-group building.
///
/// <para>Seeds mirror the manual TEST request: 4 requested items (rolamento, sensor, kit,
/// serviço) quoted by two suppliers (Kwanza, Luanda) — the expected outcome of the mixed-winner
/// approval is EXACTLY two PO groups (Kwanza: rolamento+kit; Luanda: sensor+serviço) with losing
/// candidates contributing nothing.</para>
/// </summary>
public class CandidateBasedApprovalBatchTests
{
    private const string ValidJustification = "Fornecedor com melhor prazo de entrega e assistência técnica local.";
    private const string ValidBudgetJustification = "Justificativa orçamental de teste com tamanho suficiente para o gate.";

    // Line totals from the manual TEST request (user-provided example).
    private static readonly decimal[] KwanzaTotals = { 253_080m, 660_060m, 266_760m, 328_320m };
    private static readonly decimal[] LuandaTotals = { 272_232m, 625_860m, 287_280m, 312_360m };

    private sealed record Seed(
        DbContextOptions<ApplicationDbContext> Options,
        Guid RequestId,
        Guid Actor,
        Guid[] LineIds,
        Guid[] KwanzaItems,
        Guid[] LuandaItems,
        Guid KwanzaQuotationId,
        Guid LuandaQuotationId);

    private static DbContextOptions<ApplicationDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static async Task<Seed> SeedAsync()
    {
        var options = NewDbOptions();
        await using var ctx = new ApplicationDbContext(options);
        var actor = Guid.NewGuid();

        var quotationType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        var status = new RequestStatus { Id = 1, Code = "WAITING_QUOTATION", Name = "Em Cotação" };
        ctx.RequestTypes.Add(quotationType);
        ctx.RequestStatuses.Add(status);
        ctx.Currencies.Add(new Currency { Id = 900, Code = "AOA", Symbol = "Kz" });
        // Required navigations of the budget-preview request load and queue projection
        // (Include/projection = inner-join semantics on InMemory: without these rows the
        // request silently vanishes → 404/empty queue).
        ctx.Departments.Add(new Department { Id = 940, Name = "ZZ Departamento" });
        ctx.Companies.Add(new Company { Id = 940, Name = "ZZ Companhia" });
        ctx.Users.Add(new User { Id = actor, FullName = "ZZ Aprovador", Email = "zz.aprovador@test.local" });

        var kwanza = new Supplier { Id = 9101, Name = "Kwanza Industrial", TaxId = "5417000101", PortalCode = "ZZK1" };
        var luanda = new Supplier { Id = 9102, Name = "Luanda Suprimentos", TaxId = "5417000102", PortalCode = "ZZL1" };
        ctx.Suppliers.AddRange(kwanza, luanda);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST candidato",
            StatusId = status.Id,
            Status = status,
            RequestTypeId = quotationType.Id,
            RequestType = quotationType,
            DepartmentId = 940,
            CompanyId = 940,
            CreatedAtUtc = DateTime.UtcNow,
            RequesterId = actor
        };
        ctx.Requests.Add(request);

        var descriptions = new[]
        {
            "Rolamento industrial 6205-2RS",
            "Sensor fotoelétrico M18 24VDC",
            "Kit de conectores industriais M12",
            "Serviço de calibração de sensores"
        };

        var lineIds = new Guid[4];
        for (var i = 0; i < 4; i++)
        {
            var li = new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                LineNumber = i + 1,
                Description = descriptions[i],
                Quantity = 1,
                UnitPrice = 0,
                TotalAmount = 0,
                IsDeleted = false,
                CreatedAtUtc = DateTime.UtcNow
            };
            ctx.RequestLineItems.Add(li);
            lineIds[i] = li.Id;
        }

        Guid[] AddQuotation(Supplier supplier, string documentNumber, decimal[] totals, out Guid quotationId)
        {
            var quotation = new Quotation
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                SupplierId = supplier.Id,
                SupplierNameSnapshot = supplier.Name,
                DocumentNumber = documentNumber,
                DocumentDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                Currency = "AOA",
                SourceType = "MANUAL",
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actor
            };
            ctx.Quotations.Add(quotation);
            quotationId = quotation.Id;

            var itemIds = new Guid[4];
            for (var i = 0; i < 4; i++)
            {
                var qi = new QuotationItem
                {
                    Id = Guid.NewGuid(),
                    QuotationId = quotation.Id,
                    LineNumber = i + 1,
                    Description = descriptions[i] + " — " + supplier.Name,
                    ReconciliationStatus = "MAPPED",
                    MappedRequestLineItemId = lineIds[i],
                    Quantity = 1,
                    UnitPrice = totals[i],
                    GrossSubtotal = totals[i],
                    IvaRatePercent = 0,
                    IvaAmount = 0,
                    LineTotal = totals[i]
                };
                ctx.QuotationItems.Add(qi);
                itemIds[i] = qi.Id;
            }
            return itemIds;
        }

        var kwanzaItems = AddQuotation(kwanza, "FP-KWZ-001", KwanzaTotals, out var kwanzaQuotationId);
        var luandaItems = AddQuotation(luanda, "FP-LDA-001", LuandaTotals, out var luandaQuotationId);

        await ctx.SaveChangesAsync();
        return new Seed(options, request.Id, actor, lineIds, kwanzaItems, luandaItems, kwanzaQuotationId, luandaQuotationId);
    }

    private static ApprovalBatchController BuildController(
        ApplicationDbContext ctx, Guid actorId, params string[] roles)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new ApprovalRoutingResultDto { Managers = { new AreaManagerDto { UserId = actorId, FullName = "ZZ Manager" } } });
        routing.Setup(r => r.IsAreaManagerAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(false);

        var controller = new ApprovalBatchController(
            ctx,
            NullLogger<ApprovalBatchController>.Instance,
            new Mock<IRequestStatusSyncService>().Object,
            new GroupBuilderService(ctx),
            routing.Object,
            new QuotationItemEligibilityService(ctx),
            new BatchExtraItemDecisionService(ctx),
            new AdjustmentCycleService(ctx),
            new Mock<IWorkflowNotificationOrchestrator>().Object);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()) };
        var effectiveRoles = roles.Length > 0 ? roles : new[] { RoleConstants.SystemAdministrator, RoleConstants.FinalApprover };
        claims.AddRange(effectiveRoles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    private static CreateApprovalBatchDto FullCandidateBatchDto(Seed s, string? buyerNoteOnFirst = null)
    {
        var dto = new CreateApprovalBatchDto { Items = new List<BatchItemDto>() };
        for (var i = 0; i < 4; i++)
        {
            dto.Items.Add(new BatchItemDto
            {
                RequestLineItemId = s.LineIds[i],
                Candidates =
                {
                    new BatchCandidateInputDto { QuotationItemId = s.KwanzaItems[i], BuyerNote = i == 0 ? buyerNoteOnFirst : null },
                    new BatchCandidateInputDto { QuotationItemId = s.LuandaItems[i] }
                }
            });
        }
        return dto;
    }

    private static async Task<Guid> CreateFullBatchAsync(Seed s, string? buyerNoteOnFirst = null)
    {
        await using var ctx = new ApplicationDbContext(s.Options);
        var controller = BuildController(ctx, s.Actor);
        var result = await controller.CreateBatch(s.RequestId, FullCandidateBatchDto(s, buyerNoteOnFirst));
        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ApprovalBatchDto>(ok.Value);
        return dto.Id;
    }

    private static async Task<List<ApprovalBatchItem>> LoadBatchItemsAsync(Seed s, Guid batchId)
    {
        await using var ctx = new ApplicationDbContext(s.Options);
        return await ctx.ApprovalBatchItems.AsNoTracking()
            .Include(bi => bi.Candidates)
            .Include(bi => bi.RequestLineItem)
            .Where(bi => bi.ApprovalBatchId == batchId)
            .ToListAsync();
    }

    /// <summary>Builds the approve DTO with the given winner per line index (true = Kwanza).</summary>
    private static async Task<BatchApprovalActionDto> ApproveDtoAsync(
        Seed s, Guid batchId, bool[] kwanzaWins, string? justificationForAll = null)
    {
        var items = await LoadBatchItemsAsync(s, batchId);
        var dto = new BatchApprovalActionDto
        {
            BudgetJustification = ValidBudgetJustification,
            Selections = new List<BatchWinnerSelectionDto>(),
            ItemAssignments = new Dictionary<Guid, ItemApprovalAssignmentDto>()
        };

        foreach (var bi in items)
        {
            var index = Array.IndexOf(s.LineIds, bi.RequestLineItemId);
            var wantedQuotationItemId = kwanzaWins[index] ? s.KwanzaItems[index] : s.LuandaItems[index];
            var candidate = bi.Candidates.Single(c => c.QuotationItemId == wantedQuotationItemId);
            dto.Selections.Add(new BatchWinnerSelectionDto
            {
                ApprovalBatchItemId = bi.Id,
                SelectedCandidateId = candidate.Id,
                WinnerSelectionJustification = justificationForAll
            });
            dto.ItemAssignments[bi.RequestLineItemId] = new ItemApprovalAssignmentDto { PlantId = 1, CostCenterId = 1 };
        }
        return dto;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Structural pins — the Buyer contract cannot even EXPRESS a winner
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buyer_create_and_update_contracts_carry_no_winner_fields()
    {
        foreach (var dtoType in new[] { typeof(CreateApprovalBatchDto), typeof(UpdateApprovalBatchDto), typeof(BatchItemDto), typeof(BatchCandidateInputDto) })
        {
            foreach (var p in dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain("Winner", p.Name);
                Assert.DoesNotContain("Selected", p.Name);
            }
        }
    }

    [Fact]
    public void Candidate_input_carries_only_identity_and_note_no_financial_values()
    {
        var members = typeof(BatchCandidateInputDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "BuyerNote", "QuotationItemId" }, members);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Buyer: batch creation with candidates
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_with_multiple_candidates_freezes_snapshots_and_no_winner()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s, buyerNoteOnFirst: "Preferência técnica do requisitante.");

        var items = await LoadBatchItemsAsync(s, batchId);
        Assert.Equal(4, items.Count);
        Assert.All(items, bi =>
        {
            Assert.Null(bi.SelectedQuotationItemId);
            Assert.Null(bi.SelectedCandidateId);
            Assert.Null(bi.WinnerSelectedByUserId);
            Assert.Null(bi.WinnerSelectedAtUtc);
            Assert.Null(bi.WinnerSelectionJustification);
            Assert.Equal(2, bi.Candidates.Count);
        });

        // Frozen snapshot spot-check: rolamento/Kwanza carries server-side supplier + totals.
        var rolamento = items.Single(bi => bi.RequestLineItemId == s.LineIds[0]);
        var kwanzaCandidate = rolamento.Candidates.Single(c => c.QuotationItemId == s.KwanzaItems[0]);
        Assert.Equal("Kwanza Industrial", kwanzaCandidate.SupplierNameSnapshot);
        Assert.Equal("5417000101", kwanzaCandidate.SupplierNifSnapshot);
        Assert.Equal(253_080m, kwanzaCandidate.LineTotal);
        Assert.Equal("AOA", kwanzaCandidate.Currency);
        Assert.Equal("FP-KWZ-001", kwanzaCandidate.QuotationDocumentNumber);
        Assert.Equal("Preferência técnica do requisitante.", kwanzaCandidate.BuyerNote);
        Assert.Equal(s.Actor, kwanzaCandidate.CreatedByUserId);

        // Lifecycle lock + candidate-submission audit.
        await using var verify = new ApplicationDbContext(s.Options);
        var lifecycles = await verify.RequestLineItems.AsNoTracking()
            .Where(li => s.LineIds.Contains(li.Id))
            .Select(li => li.QuotationLifecycleStatus)
            .ToListAsync();
        Assert.All(lifecycles, l => Assert.Equal(RequestConstants.QuotationLifecycleStatuses.BatchAssigned, l));

        var candidateHistory = await verify.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == s.RequestId && h.ActionTaken == "BATCH_CANDIDATES_SUBMITTED")
            .ToListAsync();
        Assert.Equal(4, candidateHistory.Count);
        Assert.Contains(candidateHistory, h => h.Comment!.Contains("Kwanza Industrial") && h.Comment.Contains("Luanda Suprimentos"));
    }

    [Fact]
    public async Task Create_with_one_and_three_candidates_is_allowed()
    {
        var s = await SeedAsync();
        await using var ctx = new ApplicationDbContext(s.Options);
        var controller = BuildController(ctx, s.Actor);

        // One candidate for line 1; two for line 2 (three total suppliers is not seedable here,
        // so multiplicity 1 and 2 pin the "no arbitrary maximum / minimum one" rule).
        var dto = new CreateApprovalBatchDto
        {
            Items =
            {
                new BatchItemDto { RequestLineItemId = s.LineIds[0], Candidates = { new BatchCandidateInputDto { QuotationItemId = s.KwanzaItems[0] } } },
                new BatchItemDto
                {
                    RequestLineItemId = s.LineIds[1],
                    Candidates =
                    {
                        new BatchCandidateInputDto { QuotationItemId = s.KwanzaItems[1] },
                        new BatchCandidateInputDto { QuotationItemId = s.LuandaItems[1] }
                    }
                }
            }
        };

        var result = await controller.CreateBatch(s.RequestId, dto);
        Assert.IsType<OkObjectResult>(result);

        await using var verify = new ApplicationDbContext(s.Options);
        Assert.Equal(3, await verify.ApprovalBatchItemCandidates.CountAsync());
    }

    [Fact]
    public async Task Create_with_zero_candidates_is_rejected()
    {
        var s = await SeedAsync();
        await using var ctx = new ApplicationDbContext(s.Options);
        var controller = BuildController(ctx, s.Actor);

        var dto = new CreateApprovalBatchDto
        {
            Items = { new BatchItemDto { RequestLineItemId = s.LineIds[0] } }
        };

        var result = await controller.CreateBatch(s.RequestId, dto);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("nenhuma opção", Assert.IsType<ProblemDetails>(bad.Value).Detail);

        await using var verify = new ApplicationDbContext(s.Options);
        Assert.Equal(0, await verify.ApprovalBatches.CountAsync());
    }

    [Fact]
    public async Task Create_with_duplicate_candidate_is_rejected()
    {
        var s = await SeedAsync();
        await using var ctx = new ApplicationDbContext(s.Options);
        var controller = BuildController(ctx, s.Actor);

        var dto = new CreateApprovalBatchDto
        {
            Items =
            {
                new BatchItemDto
                {
                    RequestLineItemId = s.LineIds[0],
                    Candidates =
                    {
                        new BatchCandidateInputDto { QuotationItemId = s.KwanzaItems[0] },
                        new BatchCandidateInputDto { QuotationItemId = s.KwanzaItems[0] }
                    }
                }
            }
        };

        var result = await controller.CreateBatch(s.RequestId, dto);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("duplicadas", Assert.IsType<ProblemDetails>(bad.Value).Detail);
    }

    [Fact]
    public async Task Create_with_foreign_or_wrongly_mapped_candidate_is_rejected()
    {
        var s = await SeedAsync();
        await using var ctx = new ApplicationDbContext(s.Options);
        var controller = BuildController(ctx, s.Actor);

        // Candidate from another request (random id) → rejected.
        var foreignDto = new CreateApprovalBatchDto
        {
            Items = { new BatchItemDto { RequestLineItemId = s.LineIds[0], Candidates = { new BatchCandidateInputDto { QuotationItemId = Guid.NewGuid() } } } }
        };
        Assert.IsType<BadRequestObjectResult>(await controller.CreateBatch(s.RequestId, foreignDto));

        // Candidate mapped to a DIFFERENT requested line → rejected.
        var wrongLineDto = new CreateApprovalBatchDto
        {
            Items = { new BatchItemDto { RequestLineItemId = s.LineIds[0], Candidates = { new BatchCandidateInputDto { QuotationItemId = s.KwanzaItems[1] } } } }
        };
        var bad = Assert.IsType<BadRequestObjectResult>(await controller.CreateBatch(s.RequestId, wrongLineDto));
        Assert.Contains("não está mapeado", Assert.IsType<ProblemDetails>(bad.Value).Detail);
    }

    [Fact]
    public async Task Same_line_cannot_enter_a_second_active_batch()
    {
        var s = await SeedAsync();
        await CreateFullBatchAsync(s);

        await using var ctx = new ApplicationDbContext(s.Options);
        var controller = BuildController(ctx, s.Actor);
        var second = await controller.CreateBatch(s.RequestId, new CreateApprovalBatchDto
        {
            Items = { new BatchItemDto { RequestLineItemId = s.LineIds[0], Candidates = { new BatchCandidateInputDto { QuotationItemId = s.KwanzaItems[0] } } } }
        });

        var bad = Assert.IsType<BadRequestObjectResult>(second);
        var detail = Assert.IsType<ProblemDetails>(bad.Value).Detail!;
        Assert.True(detail.Contains("não está disponível") || detail.Contains("outro lote ativo"));
    }

    // ══════════════════════════════════════════════════════════════════════
    // Snapshot integrity
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Editing_live_quotation_after_submission_never_changes_the_candidate_snapshot()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);

        await using (var mutate = new ApplicationDbContext(s.Options))
        {
            var live = await mutate.QuotationItems.SingleAsync(qi => qi.Id == s.KwanzaItems[0]);
            live.UnitPrice = 999_999m;
            live.LineTotal = 999_999m;
            live.Description = "ALTERADO DEPOIS DO ENVIO";
            await mutate.SaveChangesAsync();
        }

        var items = await LoadBatchItemsAsync(s, batchId);
        var candidate = items.Single(bi => bi.RequestLineItemId == s.LineIds[0])
            .Candidates.Single(c => c.QuotationItemId == s.KwanzaItems[0]);
        Assert.Equal(253_080m, candidate.LineTotal);
        Assert.Equal("Rolamento industrial 6205-2RS — Kwanza Industrial", candidate.QuotedDescription);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Area approval: all-or-return + exactly-one + candidate ownership
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Area_approve_without_all_selections_is_rejected_and_persists_nothing()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);

        var dto = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        dto.Selections!.RemoveAt(0); // one item left undecided

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var controller = BuildController(ctx, s.Actor);
            var result = await controller.BatchAreaApprove(s.RequestId, batchId, dto);
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("exatamente um vencedor", Assert.IsType<ProblemDetails>(bad.Value).Detail);
        }

        // ALL-OR-RETURN: nothing persisted — no winner stamps, no groups, status unchanged.
        await using var verify = new ApplicationDbContext(s.Options);
        var batch = await verify.ApprovalBatches.AsNoTracking().Include(b => b.Items).SingleAsync(b => b.Id == batchId);
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, batch.Status);
        Assert.All(batch.Items, bi => Assert.Null(bi.SelectedQuotationItemId));
        Assert.Equal(0, await verify.RequestPoGroups.CountAsync());
    }

    [Fact]
    public async Task Area_approve_with_duplicate_or_foreign_selection_is_rejected()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var items = await LoadBatchItemsAsync(s, batchId);

        // Duplicate selection for the same batch item.
        var duplicate = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        duplicate.Selections!.Add(duplicate.Selections[0]);
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var result = await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, duplicate);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        // A candidate that belongs to ANOTHER item of the batch.
        var wrongOwnership = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        var itemA = items[0];
        var itemB = items[1];
        wrongOwnership.Selections!.Single(sel => sel.ApprovalBatchItemId == itemA.Id).SelectedCandidateId = itemB.Candidates.First().Id;
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var result = await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, wrongOwnership);
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("não pertence a este item", Assert.IsType<ProblemDetails>(bad.Value).Detail);
        }
    }

    [Fact]
    public async Task Buyer_without_area_authority_cannot_select_winners()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var dto = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });

        await using var ctx = new ApplicationDbContext(s.Options);
        var buyerController = BuildController(ctx, s.Actor, "Buyer"); // no admin, IsAreaManagerAsync=false
        var result = await buyerController.BatchAreaApprove(s.RequestId, batchId, dto);
        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbidden.StatusCode);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Non-cheapest justification rule
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cheapest_winners_need_no_justification_and_stamp_the_decision()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);

        // Kwanza wins rolamento+kit, Luanda wins sensor+serviço — every pick is the cheapest.
        var dto = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var result = await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto);
            Assert.IsType<OkObjectResult>(result);
        }

        var items = await LoadBatchItemsAsync(s, batchId);
        Assert.All(items, bi =>
        {
            Assert.NotNull(bi.SelectedCandidateId);
            Assert.NotNull(bi.SelectedQuotationItemId);
            Assert.Equal(s.Actor, bi.WinnerSelectedByUserId);
            Assert.NotNull(bi.WinnerSelectedAtUtc);
        });

        var rolamento = items.Single(bi => bi.RequestLineItemId == s.LineIds[0]);
        Assert.Equal(s.KwanzaItems[0], rolamento.SelectedQuotationItemId);
        var sensor = items.Single(bi => bi.RequestLineItemId == s.LineIds[1]);
        Assert.Equal(s.LuandaItems[1], sensor.SelectedQuotationItemId);

        // Line-level compatibility pointer follows the Area decision.
        await using var verify = new ApplicationDbContext(s.Options);
        var line1 = await verify.RequestLineItems.AsNoTracking().SingleAsync(li => li.Id == s.LineIds[0]);
        Assert.Equal(s.KwanzaItems[0], line1.SelectedQuotationItemId);
    }

    [Fact]
    public async Task More_expensive_winner_without_justification_is_rejected()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);

        // Rolamento → Luanda (272,232 vs cheapest 253,080; beyond tolerance) with NO justification.
        var dto = await ApproveDtoAsync(s, batchId, new[] { false, false, true, false });

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var result = await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto);
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            var pd = Assert.IsType<ProblemDetails>(bad.Value);
            Assert.Equal("Justificativa de Escolha Obrigatória", pd.Title);
            Assert.Contains("não é a de menor valor", pd.Detail);
        }

        await using var verify = new ApplicationDbContext(s.Options);
        var batch = await verify.ApprovalBatches.AsNoTracking().Include(b => b.Items).SingleAsync(b => b.Id == batchId);
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, batch.Status);
        Assert.All(batch.Items, bi => Assert.Null(bi.SelectedCandidateId));
    }

    [Fact]
    public async Task More_expensive_winner_with_meaningful_justification_is_approved_and_audited()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);

        var dto = await ApproveDtoAsync(s, batchId, new[] { false, false, true, false });
        var items = await LoadBatchItemsAsync(s, batchId);
        var rolamento = items.Single(bi => bi.RequestLineItemId == s.LineIds[0]);
        dto.Selections!.Single(sel => sel.ApprovalBatchItemId == rolamento.Id).WinnerSelectionJustification = ValidJustification;

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var result = await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto);
            Assert.IsType<OkObjectResult>(result);
        }

        var after = await LoadBatchItemsAsync(s, batchId);
        var decided = after.Single(bi => bi.RequestLineItemId == s.LineIds[0]);
        Assert.Equal(s.LuandaItems[0], decided.SelectedQuotationItemId);
        Assert.Equal(ValidJustification, decided.WinnerSelectionJustification);

        await using var verify = new ApplicationDbContext(s.Options);
        var award = await verify.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == s.RequestId
                     && h.ActionTaken == WorkflowEventCodes.QuotationItemAwarded
                     && h.Comment!.Contains("Luanda Suprimentos"))
            .SingleAsync(h => h.Comment!.Contains("Item #1"));
        Assert.Equal(s.Actor, award.ActorUserId);
        Assert.Contains("Aprovador de Área", award.Comment);
        Assert.Contains(ValidJustification, award.Comment!);
    }

    [Fact]
    public async Task Tie_within_tolerance_needs_no_justification()
    {
        var s = await SeedAsync();

        // Make Luanda's rolamento an exact tie with Kwanza's BEFORE batch creation (snapshots
        // freeze at submission), then pick Luanda without justification.
        await using (var mutate = new ApplicationDbContext(s.Options))
        {
            var luandaRolamento = await mutate.QuotationItems.SingleAsync(qi => qi.Id == s.LuandaItems[0]);
            luandaRolamento.UnitPrice = 253_080m;
            luandaRolamento.LineTotal = 253_080m;
            luandaRolamento.GrossSubtotal = 253_080m;
            await mutate.SaveChangesAsync();
        }

        var batchId = await CreateFullBatchAsync(s);
        var dto = await ApproveDtoAsync(s, batchId, new[] { false, false, true, false });

        await using var ctx = new ApplicationDbContext(s.Options);
        var result = await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Optional_justification_on_cheapest_pick_is_persisted()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);

        var dto = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false }, justificationForAll: ValidJustification);

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto));
        }

        var items = await LoadBatchItemsAsync(s, batchId);
        Assert.All(items, bi => Assert.Equal(ValidJustification, bi.WinnerSelectionJustification));
    }

    // ══════════════════════════════════════════════════════════════════════
    // Groups: winners only, snapshot-sourced, the exact 2-group example
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Mixed_winners_create_exactly_two_groups_and_losers_create_nothing()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);

        // Área: rolamento→Kwanza, sensor→Luanda, kit→Kwanza, serviço→Luanda (all cheapest).
        var dto = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto));
        }

        await using var verify = new ApplicationDbContext(s.Options);
        var groups = await verify.RequestPoGroups.AsNoTracking()
            .Where(g => g.ApprovalBatchId == batchId)
            .ToListAsync();

        Assert.Equal(2, groups.Count);
        var kwanzaGroup = Assert.Single(groups, g => g.SupplierId == 9101);
        var luandaGroup = Assert.Single(groups, g => g.SupplierId == 9102);
        Assert.Equal(253_080m + 266_760m, kwanzaGroup.TotalAmount);   // rolamento + kit
        Assert.Equal(625_860m + 312_360m, luandaGroup.TotalAmount);   // sensor + serviço
        Assert.Equal("Kwanza Industrial", kwanzaGroup.SupplierNameSnapshot);
        Assert.Equal("AOA", kwanzaGroup.CurrencyCode);
        Assert.All(groups, g => Assert.Equal(RequestConstants.PoGroupStatuses.Pending, g.Status));

        // Line assignment follows the winner, and the batch advanced atomically.
        var lines = await verify.RequestLineItems.AsNoTracking().Where(li => s.LineIds.Contains(li.Id)).ToListAsync();
        Assert.Equal(kwanzaGroup.Id, lines.Single(l => l.Id == s.LineIds[0]).RequestPoGroupId);
        Assert.Equal(luandaGroup.Id, lines.Single(l => l.Id == s.LineIds[1]).RequestPoGroupId);
        Assert.Equal(kwanzaGroup.Id, lines.Single(l => l.Id == s.LineIds[2]).RequestPoGroupId);
        Assert.Equal(luandaGroup.Id, lines.Single(l => l.Id == s.LineIds[3]).RequestPoGroupId);

        var batch = await verify.ApprovalBatches.AsNoTracking().SingleAsync(b => b.Id == batchId);
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, batch.Status);
        Assert.Null(batch.ApprovedTotalAmount); // final approval still owns the snapshot
    }

    [Fact]
    public async Task Group_totals_use_the_frozen_snapshot_even_after_a_live_quotation_edit()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);

        // Corrupt the live winner AFTER submission — the frozen snapshot must win.
        await using (var mutate = new ApplicationDbContext(s.Options))
        {
            var live = await mutate.QuotationItems.SingleAsync(qi => qi.Id == s.KwanzaItems[0]);
            live.LineTotal = 1m;
            await mutate.SaveChangesAsync();
        }

        var dto = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto));
        }

        await using var verify = new ApplicationDbContext(s.Options);
        var kwanzaGroup = await verify.RequestPoGroups.AsNoTracking()
            .SingleAsync(g => g.ApprovalBatchId == batchId && g.SupplierId == 9101);
        Assert.Equal(253_080m + 266_760m, kwanzaGroup.TotalAmount); // snapshot, not the live 1m
    }

    [Fact]
    public async Task Post_payment_flag_off_keeps_obligation_stamping_untouched()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var dto = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto));
        }

        // PostPaymentCompletion.Enabled=false (committed default): classification never runs.
        await using var verify = new ApplicationDbContext(s.Options);
        var groups = await verify.RequestPoGroups.AsNoTracking().Where(g => g.ApprovalBatchId == batchId).ToListAsync();
        Assert.All(groups, g =>
        {
            Assert.Null(g.SourceDocumentType);
            Assert.Null(g.ExpectedOperationInvoiceTotal);
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    // Return/adjustment: decision revoked, candidates persist
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Final_request_adjustment_revokes_the_winner_decision_but_keeps_candidates()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var approve = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, approve));
        }

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var result = await BuildController(ctx, s.Actor).BatchFinalRequestAdjustment(
                s.RequestId, batchId, new BatchAdjustmentRequestDto { Comment = "Rever a escolha do sensor com o fornecedor Kwanza.", WholeBatch = true, Reasons = { new BatchAdjustmentReasonInputDto { ReasonCode = AdjustmentConstants.ReasonCodes.PriceNegotiation } } });
            Assert.IsType<OkObjectResult>(result);
        }

        await using var verify = new ApplicationDbContext(s.Options);
        var batch = await verify.ApprovalBatches.AsNoTracking()
            .Include(b => b.Items).ThenInclude(bi => bi.Candidates)
            .SingleAsync(b => b.Id == batchId);

        Assert.Equal(RequestConstants.ApprovalBatchStatuses.FinalAdjustment, batch.Status);
        Assert.All(batch.Items, bi =>
        {
            Assert.Null(bi.SelectedCandidateId);
            Assert.Null(bi.SelectedQuotationItemId);
            Assert.Null(bi.WinnerSelectedByUserId);
            Assert.Null(bi.WinnerSelectedAtUtc);
            Assert.Null(bi.WinnerSelectionJustification);
            Assert.Equal(2, bi.Candidates.Count); // candidates persist through the return
        });

        Assert.Equal(0, await verify.RequestPoGroups.CountAsync(g => g.ApprovalBatchId == batchId));
        var lines = await verify.RequestLineItems.AsNoTracking().Where(li => s.LineIds.Contains(li.Id)).ToListAsync();
        Assert.All(lines, l => Assert.Null(l.SelectedQuotationItemId));

        // Decision history survives the revocation.
        var awards = await verify.RequestStatusHistories.AsNoTracking()
            .CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == WorkflowEventCodes.QuotationItemAwarded);
        Assert.Equal(4, awards);
    }

    [Fact]
    public async Task Returned_batch_can_be_edited_and_resubmitted()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var approve = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, approve));
        }
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchFinalRequestAdjustment(
                s.RequestId, batchId, new BatchAdjustmentRequestDto { Comment = "Rever candidatos do rolamento, por favor.", WholeBatch = true, Reasons = { new BatchAdjustmentReasonInputDto { ReasonCode = AdjustmentConstants.ReasonCodes.PriceNegotiation } } }));
        }

        // Buyer edits: drop the Luanda option of the rolamento, keep everything else, add a note.
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var update = FullCandidateBatchDto(s);
            update.Items[0].Candidates.RemoveAt(1);
            update.Items[0].Candidates[0].BuyerNote = "Única opção após revisão do reajuste.";
            var result = await BuildController(ctx, s.Actor).UpdateBatch(s.RequestId, batchId, new UpdateApprovalBatchDto { Items = update.Items });
            Assert.IsType<OkObjectResult>(result);
        }

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            // Adjustment V2 (Phase 4): the batch was returned through the structured cycle, so the
            // Buyer's "Resposta ao reajuste" is now mandatory at resubmit.
            var result = await BuildController(ctx, s.Actor).ResubmitBatch(s.RequestId, batchId,
                new BatchApprovalActionDto { AdjustmentResponse = "Rolamento revisado: mantida apenas a opção Kwanza." });
            Assert.IsType<OkObjectResult>(result);
        }

        await using var verify = new ApplicationDbContext(s.Options);
        var batch = await verify.ApprovalBatches.AsNoTracking()
            .Include(b => b.Items).ThenInclude(bi => bi.Candidates)
            .SingleAsync(b => b.Id == batchId);
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, batch.Status);
        // The structured cycle is now resolved with exactly one BUYER resolution.
        var resolvedCycle = await verify.ApprovalBatchAdjustments.AsNoTracking()
            .Include(a => a.Resolutions).SingleAsync(a => a.ApprovalBatchId == batchId);
        Assert.Equal(AdjustmentConstants.States.Resubmitted, resolvedCycle.Status);
        Assert.Single(resolvedCycle.Resolutions);
        var rolamento = batch.Items.Single(bi => bi.RequestLineItemId == s.LineIds[0]);
        var only = Assert.Single(rolamento.Candidates);
        Assert.Equal(s.KwanzaItems[0], only.QuotationItemId);
        Assert.Equal("Única opção após revisão do reajuste.", only.BuyerNote);
        Assert.Equal(7, batch.Items.Sum(bi => bi.Candidates.Count)); // 2+2+2+1
    }

    [Fact]
    public async Task Rework_payload_with_a_buyer_included_extra_line_is_accepted()
    {
        var s = await SeedAsync();

        // An EXTRA_ITEM line on the Kwanza quotation (no mapping — generated lines never have one).
        Guid extraQiId;
        await using (var seed = new ApplicationDbContext(s.Options))
        {
            var extra = new QuotationItem
            {
                Id = Guid.NewGuid(),
                QuotationId = s.KwanzaQuotationId,
                LineNumber = 9,
                Description = "Item adicional proposto pelo fornecedor",
                ReconciliationStatus = "EXTRA_ITEM",
                Quantity = 2,
                UnitPrice = 10_000m,
                LineTotal = 20_000m,
                ReconciliationJustification = "Fornecedor propôs item complementar ao pedido."
            };
            seed.QuotationItems.Add(extra);
            await seed.SaveChangesAsync();
            extraQiId = extra.Id;
        }

        // Create with INCLUDE → generated line + single-candidate batch item for the extra.
        Guid batchId;
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var dto = FullCandidateBatchDto(s);
            dto.ExtraItemDecisions = new Dictionary<Guid, ExtraItemDecisionDto>
            {
                [extraQiId] = new ExtraItemDecisionDto { Decision = "INCLUDE" }
            };
            var result = await BuildController(ctx, s.Actor).CreateBatch(s.RequestId, dto);
            var ok = Assert.IsType<OkObjectResult>(result);
            batchId = Assert.IsType<ApprovalBatchDto>(ok.Value).Id;
        }

        // Return the batch to the Buyer, then resend the SAME composition — the generated extra
        // line re-enters the payload as its own single fixed candidate (the rework-modal shape).
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaRequestAdjustment(
                s.RequestId, batchId, new BatchAdjustmentRequestDto { Comment = "Rever opções antes de aprovar, por favor.", WholeBatch = true, Reasons = { new BatchAdjustmentReasonInputDto { ReasonCode = AdjustmentConstants.ReasonCodes.BatchComposition } } }));
        }

        Guid generatedLineId;
        await using (var read = new ApplicationDbContext(s.Options))
        {
            generatedLineId = await read.ApprovalBatchItems.AsNoTracking()
                .Include(bi => bi.Candidates)
                .Where(bi => bi.ApprovalBatchId == batchId && bi.Candidates.Any(c => c.QuotationItemId == extraQiId))
                .Select(bi => bi.RequestLineItemId)
                .SingleAsync();
        }

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var update = new UpdateApprovalBatchDto { Items = FullCandidateBatchDto(s).Items };
            update.Items.Add(new BatchItemDto
            {
                RequestLineItemId = generatedLineId,
                Candidates = { new BatchCandidateInputDto { QuotationItemId = extraQiId } }
            });
            update.ExtraItemDecisions = new Dictionary<Guid, ExtraItemDecisionDto>
            {
                [extraQiId] = new ExtraItemDecisionDto { Decision = "INCLUDE" }
            };
            var result = await BuildController(ctx, s.Actor).UpdateBatch(s.RequestId, batchId, update);
            Assert.IsType<OkObjectResult>(result);
        }

        await using var verify = new ApplicationDbContext(s.Options);
        var extraItem = await verify.ApprovalBatchItems.AsNoTracking()
            .Include(bi => bi.Candidates)
            .SingleAsync(bi => bi.ApprovalBatchId == batchId && bi.RequestLineItemId == generatedLineId);
        var only = Assert.Single(extraItem.Candidates);
        Assert.Equal(extraQiId, only.QuotationItemId);
        Assert.Null(extraItem.SelectedQuotationItemId);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Legacy batches (zero candidates, buyer-selected winner)
    // ══════════════════════════════════════════════════════════════════════

    private static async Task<Guid> SeedLegacyBatchAsync(Seed s)
    {
        await using var ctx = new ApplicationDbContext(s.Options);
        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = s.RequestId,
            BatchNumber = 90,
            Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = s.Actor
        };
        ctx.ApprovalBatches.Add(batch);
        ctx.ApprovalBatchItems.Add(new ApprovalBatchItem
        {
            Id = Guid.NewGuid(),
            ApprovalBatchId = batch.Id,
            RequestLineItemId = s.LineIds[0],
            SelectedQuotationItemId = s.KwanzaItems[0], // historical buyer-selected winner
            CreatedAtUtc = DateTime.UtcNow
        });
        var line = await ctx.RequestLineItems.SingleAsync(li => li.Id == s.LineIds[0]);
        line.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.BatchAssigned;
        await ctx.SaveChangesAsync();
        return batch.Id;
    }

    [Fact]
    public async Task Legacy_batch_without_candidates_still_area_approves_without_selections()
    {
        var s = await SeedAsync();
        var batchId = await SeedLegacyBatchAsync(s);

        var dto = new BatchApprovalActionDto
        {
            BudgetJustification = ValidBudgetJustification,
            ItemAssignments = new Dictionary<Guid, ItemApprovalAssignmentDto>
            {
                [s.LineIds[0]] = new ItemApprovalAssignmentDto { PlantId = 1, CostCenterId = 1 }
            }
            // No Selections — legacy semantics.
        };

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto));
        }

        await using var verify = new ApplicationDbContext(s.Options);
        var batch = await verify.ApprovalBatches.AsNoTracking().Include(b => b.Items).SingleAsync(b => b.Id == batchId);
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, batch.Status);
        var item = Assert.Single(batch.Items);
        Assert.Equal(s.KwanzaItems[0], item.SelectedQuotationItemId); // untouched buyer winner
        Assert.Null(item.SelectedCandidateId);                        // no synthetic candidate

        Assert.Equal(0, await verify.ApprovalBatchItemCandidates.CountAsync());
        var group = Assert.Single(await verify.RequestPoGroups.AsNoTracking().Where(g => g.ApprovalBatchId == batchId).ToListAsync());
        Assert.Equal(253_080m, group.TotalAmount); // legacy path: live quotation value
    }

    [Fact]
    public async Task Legacy_batch_rejects_winner_selections_and_reads_as_legacy()
    {
        var s = await SeedAsync();
        var batchId = await SeedLegacyBatchAsync(s);

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var dto = new BatchApprovalActionDto
            {
                BudgetJustification = ValidBudgetJustification,
                ItemAssignments = new Dictionary<Guid, ItemApprovalAssignmentDto>
                {
                    [s.LineIds[0]] = new ItemApprovalAssignmentDto { PlantId = 1, CostCenterId = 1 }
                },
                Selections = new List<BatchWinnerSelectionDto>
                {
                    new() { ApprovalBatchItemId = (await LoadBatchItemsAsync(s, batchId)).Single().Id, SelectedCandidateId = Guid.NewGuid() }
                }
            };
            var result = await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, dto);
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("modelo anterior", Assert.IsType<ProblemDetails>(bad.Value).Detail);
        }

        // Read model: legacy item flagged, no synthetic candidates.
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var result = await BuildController(ctx, s.Actor).GetBatchDetail(s.RequestId, batchId);
            var ok = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<ApprovalBatchDto>(ok.Value);
            var item = Assert.Single(dto.Items);
            Assert.True(item.IsLegacyBuyerSelectedWinner);
            Assert.Empty(item.Candidates);
            Assert.Equal(s.KwanzaItems[0], item.SelectedQuotationItemId);
            Assert.Equal(0, dto.CandidateOptionCount);
            Assert.Null(dto.MinCandidateCombinationTotal);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Read model: candidates, badges, pre-decision summary, final read-only
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Batch_detail_exposes_candidates_badges_and_combination_bounds()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);

        await using var ctx = new ApplicationDbContext(s.Options);
        var result = await BuildController(ctx, s.Actor).GetBatchDetail(s.RequestId, batchId);
        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ApprovalBatchDto>(ok.Value);

        Assert.Equal(8, dto.CandidateOptionCount);
        Assert.Equal(2, dto.CandidateSupplierCount);
        Assert.Equal(253_080m + 625_860m + 266_760m + 312_360m, dto.MinCandidateCombinationTotal);
        Assert.Equal(272_232m + 660_060m + 287_280m + 328_320m, dto.MaxCandidateCombinationTotal);
        Assert.Null(dto.ApprovedTotalAmount);

        var rolamento = dto.Items.Single(i => i.RequestLineItemId == s.LineIds[0]);
        Assert.False(rolamento.IsLegacyBuyerSelectedWinner);
        Assert.Null(rolamento.SelectedCandidateId);
        Assert.Equal(2, rolamento.Candidates.Count);
        var cheapest = rolamento.Candidates.Single(c => c.QuotationItemId == s.KwanzaItems[0]);
        var pricier = rolamento.Candidates.Single(c => c.QuotationItemId == s.LuandaItems[0]);
        Assert.True(cheapest.IsLowestTotal);   // "MENOR VALOR" badge
        Assert.False(pricier.IsLowestTotal);
        Assert.All(rolamento.Candidates, c => Assert.False(c.IsWinner));
    }

    [Fact]
    public async Task After_area_decision_detail_shows_winner_and_losers_read_only()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var approve = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, approve));
        }

        await using var ctx2 = new ApplicationDbContext(s.Options);
        var result = await BuildController(ctx2, s.Actor).GetBatchDetail(s.RequestId, batchId);
        var dto = Assert.IsType<ApprovalBatchDto>(Assert.IsType<OkObjectResult>(result).Value);

        var sensor = dto.Items.Single(i => i.RequestLineItemId == s.LineIds[1]);
        Assert.NotNull(sensor.SelectedCandidateId);
        var winner = sensor.Candidates.Single(c => c.IsWinner);
        Assert.Equal(s.LuandaItems[1], winner.QuotationItemId);
        var loser = sensor.Candidates.Single(c => !c.IsWinner);
        Assert.Equal(s.KwanzaItems[1], loser.QuotationItemId); // losing candidate still visible
        Assert.Equal(625_860m, sensor.SelectedQuotationItemLineTotal); // frozen snapshot value
    }

    // ══════════════════════════════════════════════════════════════════════
    // Budget preview: tentative Area selections valued from FROZEN snapshots
    // ══════════════════════════════════════════════════════════════════════

    private static BudgetPreviewController BuildPreviewController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new BudgetPreviewController(ctx, NullLogger<BudgetPreviewController>.Instance);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.SystemAdministrator)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    [Fact]
    public void Budget_preview_is_gated_to_approver_roles()
    {
        // Direct controller instantiation bypasses the auth pipeline, so the role gate is pinned
        // structurally: the endpoint is reachable only by approver/admin roles.
        var attribute = typeof(BudgetPreviewController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Single();
        Assert.Contains(RoleConstants.SystemAdministrator, attribute.Roles);
        Assert.Contains(RoleConstants.FinalApprover, attribute.Roles);
    }

    [Fact]
    public async Task Tentative_preview_uses_frozen_snapshots_and_persists_nothing()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var items = await LoadBatchItemsAsync(s, batchId);

        // Corrupt the live quotation AFTER submission — the preview must not see it.
        await using (var mutate = new ApplicationDbContext(s.Options))
        {
            var live = await mutate.QuotationItems.SingleAsync(qi => qi.Id == s.KwanzaItems[0]);
            live.LineTotal = 1m;
            await mutate.SaveChangesAsync();
        }

        // Tentative selection = the intended manual-TEST outcome (K, L, K, L).
        var kwanzaWins = new[] { true, false, true, false };
        var dto = new BudgetPreviewRequestDto { BatchId = batchId };
        foreach (var bi in items)
        {
            var index = Array.IndexOf(s.LineIds, bi.RequestLineItemId);
            var wanted = kwanzaWins[index] ? s.KwanzaItems[index] : s.LuandaItems[index];
            dto.Selections ??= new List<BudgetPreviewSelectionDto>();
            dto.Selections.Add(new BudgetPreviewSelectionDto
            {
                ApprovalBatchItemId = bi.Id,
                SelectedCandidateId = bi.Candidates.Single(c => c.QuotationItemId == wanted).Id
            });
        }

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var result = await BuildPreviewController(ctx, s.Actor).PreviewBudget(s.RequestId, dto);
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var preview = Assert.IsType<BudgetPreviewResponseDto>(ok.Value);
            // Frozen snapshot total — NOT the corrupted live 1m for the rolamento.
            Assert.Equal(253_080m + 625_860m + 266_760m + 312_360m, preview.Summary.ThisRequestAmount);
        }

        // Preview persists NOTHING: no winner stamps, no groups, batch state untouched.
        await using var verify = new ApplicationDbContext(s.Options);
        var batch = await verify.ApprovalBatches.AsNoTracking().Include(b => b.Items).SingleAsync(b => b.Id == batchId);
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, batch.Status);
        Assert.All(batch.Items, bi =>
        {
            Assert.Null(bi.SelectedCandidateId);
            Assert.Null(bi.SelectedQuotationItemId);
        });
        Assert.Equal(0, await verify.RequestPoGroups.CountAsync());
    }

    [Fact]
    public async Task Partial_preview_sums_only_the_selected_subset()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var items = await LoadBatchItemsAsync(s, batchId);

        // Only the rolamento decided (Kwanza) — the other three items contribute nothing yet.
        var rolamento = items.Single(bi => bi.RequestLineItemId == s.LineIds[0]);
        var dto = new BudgetPreviewRequestDto
        {
            BatchId = batchId,
            Selections = new List<BudgetPreviewSelectionDto>
            {
                new()
                {
                    ApprovalBatchItemId = rolamento.Id,
                    SelectedCandidateId = rolamento.Candidates.Single(c => c.QuotationItemId == s.KwanzaItems[0]).Id
                }
            }
        };

        await using var ctx = new ApplicationDbContext(s.Options);
        var result = await BuildPreviewController(ctx, s.Actor).PreviewBudget(s.RequestId, dto);
        var preview = Assert.IsType<BudgetPreviewResponseDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(253_080m, preview.Summary.ThisRequestAmount);
    }

    [Fact]
    public async Task Preview_rejects_duplicate_and_mismatched_selections()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var items = await LoadBatchItemsAsync(s, batchId);
        var itemA = items.Single(bi => bi.RequestLineItemId == s.LineIds[0]);
        var itemB = items.Single(bi => bi.RequestLineItemId == s.LineIds[1]);

        // Candidate belonging to ANOTHER item.
        var mismatched = new BudgetPreviewRequestDto
        {
            BatchId = batchId,
            Selections = new List<BudgetPreviewSelectionDto>
            {
                new() { ApprovalBatchItemId = itemA.Id, SelectedCandidateId = itemB.Candidates.First().Id }
            }
        };
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var result = await BuildPreviewController(ctx, s.Actor).PreviewBudget(s.RequestId, mismatched);
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        // Duplicate selection for the same batch item.
        var duplicated = new BudgetPreviewRequestDto
        {
            BatchId = batchId,
            Selections = new List<BudgetPreviewSelectionDto>
            {
                new() { ApprovalBatchItemId = itemA.Id, SelectedCandidateId = itemA.Candidates.First().Id },
                new() { ApprovalBatchItemId = itemA.Id, SelectedCandidateId = itemA.Candidates.Last().Id }
            }
        };
        await using (var ctx2 = new ApplicationDbContext(s.Options))
        {
            var result = await BuildPreviewController(ctx2, s.Actor).PreviewBudget(s.RequestId, duplicated);
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Approval queue amounts: null before the Area decision, snapshot total after
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Queue_amount_is_null_before_area_decision_and_snapshot_total_after()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var statusDisplay = new Dictionary<string, (string Name, string Color)>();
        var today = DateTime.UtcNow.Date;

        // Pre-decision AREA row: no amount exists — null (rendered "A definir"), never 0/estimate.
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var rows = await AlplaPortal.Api.Projections.ApprovalQueueProjection.ProjectAsync(
                ctx.Requests.Where(r => r.Id == s.RequestId),
                AlplaPortal.Api.Projections.ApprovalQueueProjection.StageArea,
                statusDisplay, today, today.AddDays(1), today.AddDays(4));
            var row = Assert.Single(rows);
            Assert.Equal(batchId, row.ApprovalBatchId);
            Assert.Null(row.ActionableAmount);
        }

        // Area decides (K, L, K, L), then a live quotation is corrupted — the FINAL queue row
        // must still show the frozen selected-combination total.
        var approve = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, approve));
        }
        await using (var mutate = new ApplicationDbContext(s.Options))
        {
            var live = await mutate.QuotationItems.SingleAsync(qi => qi.Id == s.KwanzaItems[0]);
            live.LineTotal = 1m;
            await mutate.SaveChangesAsync();
        }

        await using (var verify = new ApplicationDbContext(s.Options))
        {
            var rows = await AlplaPortal.Api.Projections.ApprovalQueueProjection.ProjectAsync(
                verify.Requests.Where(r => r.Id == s.RequestId),
                AlplaPortal.Api.Projections.ApprovalQueueProjection.StageFinal,
                statusDisplay, today, today.AddDays(1), today.AddDays(4));
            var row = Assert.Single(rows);
            Assert.Equal(253_080m + 625_860m + 266_760m + 312_360m, row.ActionableAmount);
        }
    }

    [Fact]
    public async Task Final_approval_cannot_change_the_winner()
    {
        var s = await SeedAsync();
        var batchId = await CreateFullBatchAsync(s);
        var approve = await ApproveDtoAsync(s, batchId, new[] { true, false, true, false });
        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchAreaApprove(s.RequestId, batchId, approve));
        }

        // A final approver "smuggling" Selections into the final call changes nothing: the final
        // endpoints simply never read them (no winner-mutation path exists at final stage).
        var items = await LoadBatchItemsAsync(s, batchId);
        var rolamento = items.Single(bi => bi.RequestLineItemId == s.LineIds[0]);
        var losingCandidate = rolamento.Candidates.Single(c => c.QuotationItemId == s.LuandaItems[0]);

        await using (var ctx = new ApplicationDbContext(s.Options))
        {
            var finalDto = new BatchApprovalActionDto
            {
                Comment = "Aprovado.",
                Selections = new List<BatchWinnerSelectionDto>
                {
                    new() { ApprovalBatchItemId = rolamento.Id, SelectedCandidateId = losingCandidate.Id }
                }
            };
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor).BatchFinalApprove(s.RequestId, batchId, finalDto));
        }

        var after = await LoadBatchItemsAsync(s, batchId);
        var decided = after.Single(bi => bi.RequestLineItemId == s.LineIds[0]);
        Assert.Equal(s.KwanzaItems[0], decided.SelectedQuotationItemId); // Area decision stands

        await using var verify = new ApplicationDbContext(s.Options);
        var batch = await verify.ApprovalBatches.AsNoTracking().SingleAsync(b => b.Id == batchId);
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.Approved, batch.Status);
        Assert.Equal(253_080m + 625_860m + 266_760m + 312_360m, batch.ApprovedTotalAmount);
    }
}
