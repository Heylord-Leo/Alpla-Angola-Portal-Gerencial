using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

using Doc = RequestConstants.OperationInvoiceDocumentStatuses;
using Agg = RequestConstants.OperationInvoiceStatuses;
using SC = RequestConstants.ShortCloseStatuses;
using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// Release 4 Phase 3A: the short-close lifecycle — propose / approve / reject.
///
/// <para>Pinned: the frozen RemainingAmountAtProposal; the meaningful-justification bar; one
/// active slot per group; structural separation of duties (the proposer never approves their own
/// proposal, whatever their role); the proposer's self-rejection as the model's withdrawal path;
/// approval re-deriving the group to SATISFIED/ClosedShort in the same transaction; and
/// idempotent decision retries.</para>
/// </summary>
public class OperationInvoiceShortCloseTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static OperationInvoiceShortClosesController BuildController(
        ApplicationDbContext ctx, Guid actorId, string role = RoleConstants.Finance)
    {
        var controller = new OperationInvoiceShortClosesController(
            ctx,
            NullLogger<OperationInvoiceShortClosesController>.Instance,
            new AlplaPortal.Infrastructure.Services.OperationInvoiceCoverageService(ctx));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, role)
                }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid ActorId, Guid SecondActorId);

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var proposer = new User { Id = Guid.NewGuid(), FullName = "ShortClose Proposer", Email = "sc1@test.local" };
        var decider = new User { Id = Guid.NewGuid(), FullName = "ShortClose Decider", Email = "sc2@test.local" };
        ctx.Users.AddRange(proposer, decider);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus
        {
            Id = 30, Code = RequestConstants.Statuses.Paid, Name = "Pago", DisplayOrder = 30
        });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "ZZTEST Supplier", TaxId = "111000111" });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-SC-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST short-close",
            RequestTypeId = 2,
            StatusId = 30,
            RequesterId = proposer.Id,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
        };
        ctx.Requests.Add(request);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, proposer.Id, decider.Id);
    }

    private static RequestPoGroup AddGroup(
        ApplicationDbContext ctx, Seed seed, decimal? expected = 1_000_000m,
        bool requires = true, string aggStatus = Agg.PartiallyInvoiced)
    {
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierId = 10,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = "AOA",
            TotalAmount = expected ?? 0m,
            Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = Types.Proforma,
            OperationInvoiceStatus = aggStatus,
            RequiresOperationInvoice = requires,
            ExpectedOperationInvoiceTotal = expected,
            ExpectedOperationInvoiceCurrency = expected.HasValue ? "AOA" : null,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        return group;
    }

    /// <summary>600k of validated effective coverage on the group, via a VALIDATED invoice.</summary>
    private static void AddValidatedCoverage(
        ApplicationDbContext ctx, Seed seed, RequestPoGroup group, decimal amount = 600_000m)
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            FileName = "fatura.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/sc-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-2)
        };
        ctx.RequestAttachments.Add(attachment);
        var invoice = new OperationInvoice
        {
            RequestId = seed.RequestId,
            AttachmentId = attachment.Id,
            SupplierId = 10,
            DocumentNumber = "FT " + Guid.NewGuid().ToString("N")[..6],
            Currency = "AOA",
            GrossAmount = amount,
            Status = Doc.Validated,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-2),
            UploadedByUserId = seed.ActorId
        };
        ctx.OperationInvoices.Add(invoice);
        ctx.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
        {
            OperationInvoiceId = invoice.Id,
            RequestPoGroupId = group.Id,
            AllocatedGrossAmount = amount,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = seed.ActorId
        });
    }

    private const string ValidJustification = "ZZTEST fornecedor não emitirá o valor remanescente";

    private static OperationInvoiceShortCloseDto Body(IActionResult result) =>
        Assert.IsType<OperationInvoiceShortCloseDto>(Assert.IsType<OkObjectResult>(result).Value);

    private static void AssertCode(IActionResult result, string expectedCode)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(expectedCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
    }

    // ── Propose ──

    [Fact]
    public async Task Buyer_proposes_and_the_remaining_amount_is_frozen()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed);
        AddValidatedCoverage(ctx, seed, group);   // 600k of 1M
        await ctx.SaveChangesAsync();

        var body = Body(await BuildController(ctx, seed.ActorId, RoleConstants.Buyer).Propose(
            seed.RequestId, group.Id,
            new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));

        Assert.Equal(SC.Proposed, body.Status);
        Assert.Equal(400_000m, body.RemainingAmountAtProposal);
        Assert.Equal(seed.ActorId, body.ProposedByUserId);

        ctx.ChangeTracker.Clear();
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "OI_SHORTCLOSE_PROPOSED"));
        // A proposal decides nothing yet: the aggregate is untouched.
        Assert.Equal(Agg.PartiallyInvoiced,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id)).OperationInvoiceStatus);
    }

    [Fact]
    public async Task Propose_requires_a_meaningful_justification_and_remaining_above_tolerance()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var covered = AddGroup(ctx, seed);
        AddValidatedCoverage(ctx, seed, covered, amount: 1_000_000m);   // fully covered
        var open = AddGroup(ctx, seed);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        Assert.IsType<BadRequestObjectResult>(await controller.Propose(
            seed.RequestId, open.Id, new ProposeOperationInvoiceShortCloseDto { Justification = "curto" }));

        AssertCode(await controller.Propose(
                seed.RequestId, covered.Id,
                new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }),
            OperationInvoiceShortClosesController.NothingRemainingCode);
    }

    [Fact]
    public async Task Propose_refuses_a_group_without_obligation_or_without_baseline()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var notRequiring = AddGroup(ctx, seed, requires: false, aggStatus: Agg.NotRequired);
        var noBaseline = AddGroup(ctx, seed, expected: null, aggStatus: Agg.PendingUpload);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        AssertCode(await controller.Propose(
                seed.RequestId, notRequiring.Id,
                new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }),
            OperationInvoiceShortClosesController.NotEligibleCode);
        AssertCode(await controller.Propose(
                seed.RequestId, noBaseline.Id,
                new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }),
            OperationInvoiceShortClosesController.NotEligibleCode);
    }

    [Fact]
    public async Task One_active_slot_per_group_and_the_same_proposers_retry_is_idempotent()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed);
        AddValidatedCoverage(ctx, seed, group);
        await ctx.SaveChangesAsync();

        var proposerController = BuildController(ctx, seed.ActorId, RoleConstants.Buyer);
        var first = Body(await proposerController.Propose(seed.RequestId, group.Id,
            new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));

        // Same proposer again: the existing proposal comes back, no second row.
        var retry = Body(await proposerController.Propose(seed.RequestId, group.Id,
            new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));
        Assert.Equal(first.Id, retry.Id);

        // A different actor hits the occupied slot.
        AssertCode(await BuildController(ctx, seed.SecondActorId).Propose(seed.RequestId, group.Id,
                new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }),
            OperationInvoiceShortClosesController.ActiveExistsCode);

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.OperationInvoiceShortCloses.CountAsync());
    }

    // ── Approve ──

    [Fact]
    public async Task The_proposer_never_approves_their_own_proposal()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed);
        AddValidatedCoverage(ctx, seed, group);
        await ctx.SaveChangesAsync();

        // Proposed by a Finance user — the strictest case: the ROLE may decide, the PERSON may not.
        var proposerController = BuildController(ctx, seed.ActorId, RoleConstants.Finance);
        var proposal = Body(await proposerController.Propose(seed.RequestId, group.Id,
            new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));

        var selfApproval = await proposerController.Approve(
            seed.RequestId, group.Id, proposal.Id, new DecideOperationInvoiceShortCloseDto());
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(selfApproval).Value);
        Assert.Equal(403, problem.Status);
        Assert.Equal(OperationInvoiceShortClosesController.SelfApprovalCode, problem.Extensions["code"]);
    }

    [Fact]
    public async Task Approval_closes_the_group_short_and_a_retry_is_idempotent()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed);
        AddValidatedCoverage(ctx, seed, group);
        await ctx.SaveChangesAsync();

        var proposal = Body(await BuildController(ctx, seed.ActorId, RoleConstants.Buyer)
            .Propose(seed.RequestId, group.Id,
                new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));

        var deciderController = BuildController(ctx, seed.SecondActorId);
        var approved = Body(await deciderController.Approve(
            seed.RequestId, group.Id, proposal.Id,
            new DecideOperationInvoiceShortCloseDto { DecisionReason = "ZZTEST aceite" }));

        Assert.Equal(SC.Approved, approved.Status);
        Assert.Equal(seed.SecondActorId, approved.DecidedByUserId);

        ctx.ChangeTracker.Clear();
        // The aggregate moved in the same transaction: SATISFIED by short-close, audited.
        Assert.Equal(Agg.Satisfied,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id)).OperationInvoiceStatus);
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "OI_SHORTCLOSE_APPROVED"));
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "GROUP_OI_STATUS"));

        // Retry: same answer, still one approval event.
        var retry = Body(await deciderController.Approve(
            seed.RequestId, group.Id, proposal.Id, new DecideOperationInvoiceShortCloseDto()));
        Assert.Equal(SC.Approved, retry.Status);
        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.RequestStatusHistories
            .CountAsync(h => h.ActionTaken == "OI_SHORTCLOSE_APPROVED"));
    }

    [Fact]
    public async Task Buyer_cannot_decide()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed);
        AddValidatedCoverage(ctx, seed, group);
        await ctx.SaveChangesAsync();

        var proposal = Body(await BuildController(ctx, seed.ActorId, RoleConstants.Buyer)
            .Propose(seed.RequestId, group.Id,
                new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));

        var result = await BuildController(ctx, seed.SecondActorId, RoleConstants.Buyer).Approve(
            seed.RequestId, group.Id, proposal.Id, new DecideOperationInvoiceShortCloseDto());
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ── Reject / withdraw ──

    [Fact]
    public async Task A_decider_rejects_with_a_mandatory_reason_and_the_slot_frees_up()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed);
        AddValidatedCoverage(ctx, seed, group);
        await ctx.SaveChangesAsync();

        var proposal = Body(await BuildController(ctx, seed.ActorId, RoleConstants.Buyer)
            .Propose(seed.RequestId, group.Id,
                new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));

        var deciderController = BuildController(ctx, seed.SecondActorId);
        Assert.IsType<BadRequestObjectResult>(await deciderController.Reject(
            seed.RequestId, group.Id, proposal.Id, new DecideOperationInvoiceShortCloseDto()));

        var rejected = Body(await deciderController.Reject(
            seed.RequestId, group.Id, proposal.Id,
            new DecideOperationInvoiceShortCloseDto { DecisionReason = "ZZTEST aguardar a fatura em falta" }));
        Assert.Equal(SC.Rejected, rejected.Status);

        ctx.ChangeTracker.Clear();
        // The aggregate never moved, and the group can be proposed again.
        Assert.Equal(Agg.PartiallyInvoiced,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id)).OperationInvoiceStatus);
        var second = Body(await BuildController(ctx, seed.ActorId, RoleConstants.Buyer)
            .Propose(seed.RequestId, group.Id,
                new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));
        Assert.NotEqual(proposal.Id, second.Id);
    }

    [Fact]
    public async Task The_proposer_withdraws_by_self_rejection_even_without_a_decider_role()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed);
        AddValidatedCoverage(ctx, seed, group);
        await ctx.SaveChangesAsync();

        var proposerController = BuildController(ctx, seed.ActorId, RoleConstants.Buyer);
        var proposal = Body(await proposerController.Propose(seed.RequestId, group.Id,
            new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));

        var withdrawn = Body(await proposerController.Reject(
            seed.RequestId, group.Id, proposal.Id,
            new DecideOperationInvoiceShortCloseDto { DecisionReason = "ZZTEST proposta enviada por engano" }));

        Assert.Equal(SC.Rejected, withdrawn.Status);
        ctx.ChangeTracker.Clear();
        var history = await ctx.RequestStatusHistories
            .SingleAsync(h => h.ActionTaken == "OI_SHORTCLOSE_REJECTED");
        Assert.Contains("RETIRADA", history.Comment);
    }

    [Fact]
    public async Task A_third_party_without_decider_role_cannot_reject()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed);
        AddValidatedCoverage(ctx, seed, group);
        await ctx.SaveChangesAsync();

        var proposal = Body(await BuildController(ctx, seed.ActorId, RoleConstants.Buyer)
            .Propose(seed.RequestId, group.Id,
                new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));

        var result = await BuildController(ctx, seed.SecondActorId, RoleConstants.Requester).Reject(
            seed.RequestId, group.Id, proposal.Id,
            new DecideOperationInvoiceShortCloseDto { DecisionReason = "ZZTEST tentativa indevida" });
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task An_approved_short_close_cannot_flip_to_rejected_or_vice_versa()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed);
        AddValidatedCoverage(ctx, seed, group);
        await ctx.SaveChangesAsync();

        var proposal = Body(await BuildController(ctx, seed.ActorId, RoleConstants.Buyer)
            .Propose(seed.RequestId, group.Id,
                new ProposeOperationInvoiceShortCloseDto { Justification = ValidJustification }));

        var deciderController = BuildController(ctx, seed.SecondActorId);
        Body(await deciderController.Approve(
            seed.RequestId, group.Id, proposal.Id, new DecideOperationInvoiceShortCloseDto()));

        AssertCode(await deciderController.Reject(
                seed.RequestId, group.Id, proposal.Id,
                new DecideOperationInvoiceShortCloseDto { DecisionReason = "ZZTEST mudança de ideia" }),
            OperationInvoiceShortClosesController.NotDecidableCode);
    }
}
