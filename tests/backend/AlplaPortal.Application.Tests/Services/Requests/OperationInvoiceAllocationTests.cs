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
using Req = RequestConstants.Statuses;
using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// Release 4 Phase 3A: PUT/GET …/operation-invoices/{id}/allocations — the atomic replace-set.
///
/// <para>Pinned: the payload is the resulting set (add/update/remove derived server-side); the
/// whole result validates BEFORE anything mutates; group eligibility, supplier/currency identity
/// and both over-allocation rules hold; Buyer is hard-blocked over the expected total while
/// Finance must explain the divergence in the allocation notes; an identical payload is an
/// idempotent no-op; and every write re-derives the touched groups' aggregate in the same
/// transaction — a draft moves the group to PENDING_VALIDATION, never to covered.</para>
/// </summary>
public class OperationInvoiceAllocationTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static OperationInvoicesController BuildController(
        ApplicationDbContext ctx, Guid actorId, string role = RoleConstants.Finance)
    {
        var controller = new OperationInvoicesController(
            ctx,
            NullLogger<OperationInvoicesController>.Instance,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
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

    private sealed record Seed(Guid RequestId, Guid ActorId);

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Allocation Tester", Email = "alloc@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 30, Code = Req.Paid, Name = Req.Paid, DisplayOrder = 30 });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "ZZTEST Supplier", TaxId = "111000111" });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-ALLOC-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST allocations",
            RequestTypeId = 2,
            StatusId = 30,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-20)
        };
        ctx.Requests.Add(request);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static OperationInvoice AddInvoice(
        ApplicationDbContext ctx, Seed seed, decimal gross = 1_000_000m,
        string status = Doc.PendingValidation, string number = "FT 300",
        int supplierId = 10, string currency = "AOA")
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            FileName = "fatura.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/a-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow.AddHours(-3)
        };
        ctx.RequestAttachments.Add(attachment);

        var invoice = new OperationInvoice
        {
            RequestId = seed.RequestId,
            AttachmentId = attachment.Id,
            SupplierId = supplierId,
            SupplierTaxIdSnapshot = "111000111",
            DocumentNumber = number,
            DocumentSeries = "A",
            DocumentDate = new DateTime(2026, 8, 1),
            Currency = currency,
            GrossAmount = gross,
            Status = status,
            AmountsEnteredManually = true,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1),
            UploadedByUserId = seed.ActorId
        };
        ctx.OperationInvoices.Add(invoice);
        return invoice;
    }

    private static RequestPoGroup AddGroup(
        ApplicationDbContext ctx, Seed seed, decimal? expected = 1_000_000m,
        int? supplierId = 10, string? currency = "AOA",
        string? type = Types.Proforma, bool requires = true,
        string aggStatus = Agg.PendingUpload,
        string groupStatus = RequestConstants.PoGroupStatuses.WaitingReceipt)
    {
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierId = supplierId,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = currency,
            TotalAmount = expected ?? 0m,
            Status = groupStatus,
            SourceDocumentType = type,
            OperationInvoiceStatus = aggStatus,
            RequiresOperationInvoice = requires,
            ExpectedOperationInvoiceTotal = expected,
            ExpectedOperationInvoiceCurrency = expected.HasValue ? currency : null,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        return group;
    }

    private static SaveOperationInvoiceAllocationsDto Payload(
        params (Guid GroupId, decimal Gross)[] items) => new()
    {
        Allocations = items.Select(i => new SaveOperationInvoiceAllocationItemDto
        {
            RequestPoGroupId = i.GroupId,
            AllocatedGrossAmount = i.Gross
        }).ToList()
    };

    private static List<OperationInvoiceAllocationDto> Body(IActionResult result) =>
        Assert.IsType<List<OperationInvoiceAllocationDto>>(
            Assert.IsType<OkObjectResult>(result).Value);

    private static void AssertCode(IActionResult result, string expectedCode)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(expectedCode,
            Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
    }

    // ── The replace-set contract ──

    [Fact]
    public async Task A_draft_set_persists_with_audit_and_moves_the_groups_to_pending_validation()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed, gross: 1_000_000m);
        var groupA = AddGroup(ctx, seed, expected: 600_000m);
        var groupB = AddGroup(ctx, seed, expected: 400_000m);
        await ctx.SaveChangesAsync();

        var rows = Body(await BuildController(ctx, seed.ActorId, RoleConstants.Buyer).SaveAllocations(
            seed.RequestId, invoice.Id, Payload((groupA.Id, 600_000m), (groupB.Id, 400_000m))));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(1, r.SequenceNumber);       // first allocation each group ever received
            Assert.False(r.IsEffective);             // a draft counts toward NOTHING validated
            Assert.True(r.IsPendingDecision);
            Assert.Equal(Doc.PendingValidation, r.InvoiceStatus);
        });

        ctx.ChangeTracker.Clear();
        var persistedA = await ctx.RequestPoGroups.SingleAsync(g => g.Id == groupA.Id);
        var persistedB = await ctx.RequestPoGroups.SingleAsync(g => g.Id == groupB.Id);
        Assert.Equal(Agg.PendingValidation, persistedA.OperationInvoiceStatus);
        Assert.Equal(Agg.PendingValidation, persistedB.OperationInvoiceStatus);

        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.ActionTaken == "OI_ALLOC_SET"));
        Assert.Equal(2, await ctx.RequestStatusHistories.CountAsync(h => h.ActionTaken == "GROUP_OI_STATUS"));

        var stored = await ctx.OperationInvoiceAllocations.Where(a => a.OperationInvoiceId == invoice.Id).ToListAsync();
        Assert.All(stored, a =>
        {
            Assert.Equal(seed.ActorId, a.CreatedByUserId);
            Assert.Null(a.UpdatedAtUtc);
        });
    }

    [Fact]
    public async Task Replacing_the_set_updates_removes_and_rederives_the_abandoned_group()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed, gross: 1_000_000m);
        var groupA = AddGroup(ctx, seed, expected: 1_000_000m);
        var groupB = AddGroup(ctx, seed, expected: 400_000m);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Body(await controller.SaveAllocations(
            seed.RequestId, invoice.Id, Payload((groupA.Id, 500_000m), (groupB.Id, 400_000m))));

        // New resulting set: A grows to the full million, B is no longer covered by this invoice.
        var rows = Body(await controller.SaveAllocations(
            seed.RequestId, invoice.Id, Payload((groupA.Id, 1_000_000m))));

        var row = Assert.Single(rows);
        Assert.Equal(1_000_000m, row.AllocatedGrossAmount);
        Assert.NotNull(row.UpdatedAtUtc);            // an update, not a delete+recreate

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.OperationInvoiceAllocations.CountAsync(a => a.OperationInvoiceId == invoice.Id));

        // The abandoned group fell back to PENDING_UPLOAD in the same transaction.
        Assert.Equal(Agg.PendingUpload,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == groupB.Id)).OperationInvoiceStatus);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.ActionTaken == "OI_ALLOC_CHANGED"));
    }

    [Fact]
    public async Task An_identical_payload_is_an_idempotent_no_op()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed, gross: 500_000m);
        var group = AddGroup(ctx, seed, expected: 500_000m);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Body(await controller.SaveAllocations(seed.RequestId, invoice.Id, Payload((group.Id, 500_000m))));
        Body(await controller.SaveAllocations(seed.RequestId, invoice.Id, Payload((group.Id, 500_000m))));

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.RequestStatusHistories
            .CountAsync(h => h.ActionTaken == "OI_ALLOC_SET" || h.ActionTaken == "OI_ALLOC_CHANGED"));
        var stored = Assert.Single(await ctx.OperationInvoiceAllocations
            .Where(a => a.OperationInvoiceId == invoice.Id).ToListAsync());
        Assert.Null(stored.UpdatedAtUtc);            // the retry touched nothing
    }

    // ── Payload and group integrity ──

    [Fact]
    public async Task A_duplicated_group_in_the_payload_is_rejected_as_validation()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        var group = AddGroup(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId).SaveAllocations(
            seed.RequestId, invoice.Id, Payload((group.Id, 400_000m), (group.Id, 600_000m)));

        Assert.IsType<BadRequestObjectResult>(result);
        ctx.ChangeTracker.Clear();
        Assert.False(await ctx.OperationInvoiceAllocations.AnyAsync());
    }

    [Fact]
    public async Task A_group_of_another_request_is_invalid()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        var foreignGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),                     // some other request entirely
            SupplierId = 10,
            CurrencyCode = "AOA",
            TotalAmount = 100m,
            Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = Types.Proforma,
            OperationInvoiceStatus = Agg.PendingUpload,
            RequiresOperationInvoice = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(foreignGroup);
        await ctx.SaveChangesAsync();

        AssertCode(await BuildController(ctx, seed.ActorId).SaveAllocations(
                seed.RequestId, invoice.Id, Payload((foreignGroup.Id, 100m))),
            OperationInvoicesController.AllocationGroupInvalidCode);
    }

    [Fact]
    public async Task An_unclassified_or_not_requiring_group_is_not_eligible()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        var unclassified = AddGroup(ctx, seed, type: null, requires: false, aggStatus: Agg.Unclassified);
        var notRequiring = AddGroup(ctx, seed, requires: false, aggStatus: Agg.NotRequired);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        AssertCode(await controller.SaveAllocations(
                seed.RequestId, invoice.Id, Payload((unclassified.Id, 100_000m))),
            OperationInvoicesController.AllocationGroupInvalidCode);
        AssertCode(await controller.SaveAllocations(
                seed.RequestId, invoice.Id, Payload((notRequiring.Id, 100_000m))),
            OperationInvoicesController.AllocationGroupInvalidCode);
    }

    [Fact]
    public async Task Supplier_and_currency_identity_must_match_the_group_snapshot()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        ctx.Suppliers.Add(new Supplier { Id = 20, Name = "ZZTEST Other", TaxId = "222000222" });
        var invoice = AddInvoice(ctx, seed, supplierId: 20);
        var wrongSupplier = AddGroup(ctx, seed, supplierId: 10);
        var wrongCurrency = AddGroup(ctx, seed, supplierId: 20, currency: "USD");
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        AssertCode(await controller.SaveAllocations(
                seed.RequestId, invoice.Id, Payload((wrongSupplier.Id, 100_000m))),
            OperationInvoicesController.AllocationSupplierMismatchCode);
        AssertCode(await controller.SaveAllocations(
                seed.RequestId, invoice.Id, Payload((wrongCurrency.Id, 100_000m))),
            OperationInvoicesController.AllocationCurrencyMismatchCode);
    }

    // ── Over-allocation, both sides ──

    [Fact]
    public async Task The_invoice_cannot_distribute_more_than_it_is_worth()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed, gross: 500_000m);
        var groupA = AddGroup(ctx, seed, expected: 400_000m);
        var groupB = AddGroup(ctx, seed, expected: 400_000m);
        await ctx.SaveChangesAsync();

        AssertCode(await BuildController(ctx, seed.ActorId).SaveAllocations(
                seed.RequestId, invoice.Id, Payload((groupA.Id, 400_000m), (groupB.Id, 400_000m))),
            OperationInvoicesController.AllocationInvoiceOverCode);
    }

    [Fact]
    public async Task Buyer_is_hard_blocked_over_the_expected_total()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);

        // 700k already VALIDATED on another invoice; a further 400k overshoots the 1M expected.
        var group = AddGroup(ctx, seed, expected: 1_000_000m);
        var validatedInvoice = AddInvoice(ctx, seed, gross: 700_000m, status: Doc.Validated, number: "FT 290");
        await ctx.SaveChangesAsync();
        ctx.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
        {
            OperationInvoiceId = validatedInvoice.Id,
            RequestPoGroupId = group.Id,
            AllocatedGrossAmount = 700_000m,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        });
        var invoice = AddInvoice(ctx, seed, gross: 400_000m, number: "FT 300");
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId, RoleConstants.Buyer).SaveAllocations(
            seed.RequestId, invoice.Id, Payload((group.Id, 400_000m)));

        AssertCode(result, OperationInvoicesController.AllocationGroupOverCode);
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(result).Value);
        Assert.Equal(1_000_000m, problem.Extensions["expectedTotal"]);
        Assert.Equal(700_000m, problem.Extensions["currentValidated"]);
        Assert.Equal(400_000m, problem.Extensions["attemptedAllocated"]);
    }

    [Fact]
    public async Task Finance_over_expected_requires_meaningful_notes_and_then_saves_uncapped()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, expected: 500_000m);
        var invoice = AddInvoice(ctx, seed, gross: 620_000m);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);

        // Without an explanation: refused as a field error, not silently capped.
        Assert.IsType<BadRequestObjectResult>(await controller.SaveAllocations(
            seed.RequestId, invoice.Id, Payload((group.Id, 620_000m))));

        // With a meaningful explanation: saved at full value — a divergence CANDIDATE, whose
        // acceptance still belongs to validation.
        var dto = Payload((group.Id, 620_000m));
        dto.Allocations[0].Notes = "ZZTEST frete e taxas alfandegárias adicionais faturadas";
        var row = Assert.Single(Body(await controller.SaveAllocations(seed.RequestId, invoice.Id, dto)));
        Assert.Equal(620_000m, row.AllocatedGrossAmount);

        ctx.ChangeTracker.Clear();
        Assert.Equal(620_000m,
            (await ctx.OperationInvoiceAllocations.SingleAsync()).AllocatedGrossAmount);
    }

    // ── SATISFIED vs ClosedShort (v2.228.3) ──

    /// <summary>A group at 100% validated coverage (938.220 of 938.220) — the canonical TEST shape.</summary>
    private static RequestPoGroup AddSatisfiedGroup(
        ApplicationDbContext ctx, Seed seed, decimal expected = 938_220m,
        int? supplierId = 10, string? currency = "AOA")
    {
        var group = AddGroup(ctx, seed, expected: expected, supplierId: supplierId,
            currency: currency, aggStatus: Agg.Satisfied);
        var covering = AddInvoice(ctx, seed, gross: expected, status: Doc.Validated,
            number: "FT COV-" + Guid.NewGuid().ToString("N")[..6]);
        ctx.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
        {
            OperationInvoiceId = covering.Id,
            RequestPoGroupId = group.Id,
            AllocatedGrossAmount = expected,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = seed.ActorId
        });
        return group;
    }

    [Fact]
    public async Task Finance_allocates_to_a_satisfied_group_as_a_divergence_candidate()
    {
        // SATISFIED means "expected coverage is currently satisfied" — a financial reading, not
        // structural ineligibility. Finance's 30k over the fully covered 938.220 group is the
        // approved divergence-candidate path, never a generic eligibility error.
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddSatisfiedGroup(ctx, seed);
        var invoice = AddInvoice(ctx, seed, gross: 30_000m, number: "FT-LU-003");
        await ctx.SaveChangesAsync();

        var dto = Payload((group.Id, 30_000m));
        dto.Allocations[0].Notes = "ZZTEST serviço adicional faturado após a cobertura completa";

        var row = Assert.Single(Body(await BuildController(ctx, seed.ActorId).SaveAllocations(
            seed.RequestId, invoice.Id, dto)));
        Assert.Equal(30_000m, row.AllocatedGrossAmount);
        Assert.True(row.IsPendingDecision);
        Assert.False(row.IsEffective);          // pending only — validation decides the divergence

        // Effective coverage is untouched; the aggregate re-enters Finance's court.
        ctx.ChangeTracker.Clear();
        Assert.Equal(Agg.PendingValidation,
            (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id)).OperationInvoiceStatus);
    }

    [Fact]
    public async Task Buyer_on_a_satisfied_group_gets_the_precise_over_coverage_error()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddSatisfiedGroup(ctx, seed);
        var invoice = AddInvoice(ctx, seed, gross: 30_000m, number: "FT-LU-003");
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId, RoleConstants.Buyer).SaveAllocations(
            seed.RequestId, invoice.Id, Payload((group.Id, 30_000m)));

        // The financial reason, not the generic eligibility error.
        AssertCode(result, OperationInvoicesController.AllocationGroupOverCode);
    }

    [Fact]
    public async Task Satisfied_groups_keep_the_structural_supplier_and_currency_blockers()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        ctx.Suppliers.Add(new Supplier { Id = 20, Name = "ZZTEST Other", TaxId = "222000222" });
        var wrongSupplier = AddSatisfiedGroup(ctx, seed, supplierId: 20);
        var wrongCurrency = AddSatisfiedGroup(ctx, seed, expected: 500_000m, currency: "USD");
        var invoice = AddInvoice(ctx, seed, gross: 30_000m, number: "FT-LU-003");
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);   // Finance — divergence never bypasses identity
        AssertCode(await controller.SaveAllocations(
                seed.RequestId, invoice.Id, Payload((wrongSupplier.Id, 30_000m))),
            OperationInvoicesController.AllocationSupplierMismatchCode);
        AssertCode(await controller.SaveAllocations(
                seed.RequestId, invoice.Id, Payload((wrongCurrency.Id, 30_000m))),
            OperationInvoicesController.AllocationCurrencyMismatchCode);
    }

    [Theory]
    [InlineData(RoleConstants.Buyer)]
    [InlineData(RoleConstants.Finance)]
    [InlineData(RoleConstants.SystemAdministrator)]
    public async Task A_short_closed_group_refuses_new_allocations_for_every_actor(string role)
    {
        // An APPROVED short-close is an explicit audited closure — different from SATISFIED by
        // full coverage. No actor allocates into it until an explicit reopening workflow exists.
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, expected: 1_000_000m, aggStatus: Agg.Satisfied);
        ctx.OperationInvoiceShortCloses.Add(new OperationInvoiceShortClose
        {
            RequestPoGroupId = group.Id,
            Status = RequestConstants.ShortCloseStatuses.Approved,
            ProposedByUserId = seed.ActorId,
            ProposedAtUtc = DateTime.UtcNow.AddDays(-2),
            ProposalJustification = "ZZTEST fornecedor não emitirá o restante",
            RemainingAmountAtProposal = 400_000m,
            DecidedByUserId = Guid.NewGuid(),
            DecidedAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        var invoice = AddInvoice(ctx, seed, gross: 30_000m, number: "FT-LU-003");
        await ctx.SaveChangesAsync();

        var dto = Payload((group.Id, 30_000m));
        dto.Allocations[0].Notes = "ZZTEST justificativa suficientemente longa para divergência";

        var result = await BuildController(ctx, seed.ActorId, role).SaveAllocations(
            seed.RequestId, invoice.Id, dto);

        AssertCode(result, OperationInvoicesController.AllocationGroupClosedShortCode);
        ctx.ChangeTracker.Clear();
        Assert.False(await ctx.OperationInvoiceAllocations
            .AnyAsync(a => a.OperationInvoiceId == invoice.Id));
    }

    // ── Editability window ──

    [Fact]
    public async Task A_validated_invoice_no_longer_edits_its_allocations()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed, status: Doc.Validated);
        var group = AddGroup(ctx, seed);
        await ctx.SaveChangesAsync();

        AssertCode(await BuildController(ctx, seed.ActorId).SaveAllocations(
                seed.RequestId, invoice.Id, Payload((group.Id, 1_000_000m))),
            OperationInvoicesController.AllocationNotEditableCode);
    }

    [Fact]
    public async Task Requester_cannot_touch_allocations()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed);
        var group = AddGroup(ctx, seed);
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId, RoleConstants.Requester).SaveAllocations(
            seed.RequestId, invoice.Id, Payload((group.Id, 1_000_000m)));
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ── Header-drift guard: an edit cannot invalidate accepted allocation evidence ──

    [Fact]
    public async Task Editing_the_supplier_away_from_an_allocated_group_is_refused_and_nothing_moves()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        ctx.Suppliers.Add(new Supplier { Id = 20, Name = "ZZTEST Other", TaxId = "222000222" });
        var invoice = AddInvoice(ctx, seed, gross: 500_000m);
        var group = AddGroup(ctx, seed, expected: 500_000m);
        // Supplier 20 owns its own obligation group, so the v2.228.4 supplier-in-request rule
        // passes and this pin keeps testing the ALLOCATED-evidence drift guard specifically.
        AddGroup(ctx, seed, expected: 200_000m, supplierId: 20);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Body(await controller.SaveAllocations(seed.RequestId, invoice.Id, Payload((group.Id, 500_000m))));

        AssertCode(await controller.Update(seed.RequestId, invoice.Id,
                new SaveOperationInvoiceDto { SupplierId = 20 }),
            OperationInvoicesController.AllocationSupplierMismatchCode);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(10, persisted.SupplierId);                       // header unchanged
        var allocation = Assert.Single(await ctx.OperationInvoiceAllocations.ToListAsync());
        Assert.Equal(500_000m, allocation.AllocatedGrossAmount);      // evidence unchanged
    }

    [Fact]
    public async Task Editing_the_currency_away_from_an_allocated_group_is_refused()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed, gross: 500_000m);
        var group = AddGroup(ctx, seed, expected: 500_000m);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Body(await controller.SaveAllocations(seed.RequestId, invoice.Id, Payload((group.Id, 500_000m))));

        AssertCode(await controller.Update(seed.RequestId, invoice.Id,
                new SaveOperationInvoiceDto { Currency = "USD" }),
            OperationInvoicesController.AllocationCurrencyMismatchCode);

        ctx.ChangeTracker.Clear();
        Assert.Equal("AOA", (await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Currency);
    }

    [Fact]
    public async Task Editing_an_unrelated_field_stays_allowed_with_allocations_present()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed, gross: 500_000m);
        var group = AddGroup(ctx, seed, expected: 500_000m);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Body(await controller.SaveAllocations(seed.RequestId, invoice.Id, Payload((group.Id, 500_000m))));

        var result = await controller.Update(seed.RequestId, invoice.Id,
            new SaveOperationInvoiceDto { Notes = "ZZTEST nota administrativa" });

        Assert.IsType<OkObjectResult>(result);
        ctx.ChangeTracker.Clear();
        Assert.Equal("ZZTEST nota administrativa",
            (await ctx.OperationInvoices.SingleAsync(i => i.Id == invoice.Id)).Notes);
    }

    // ── Read ──

    [Fact]
    public async Task Get_returns_the_rows_with_server_derived_effectiveness()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var invoice = AddInvoice(ctx, seed, gross: 300_000m);
        var group = AddGroup(ctx, seed, expected: 300_000m);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Body(await controller.SaveAllocations(seed.RequestId, invoice.Id, Payload((group.Id, 300_000m))));

        var read = Assert.IsType<List<OperationInvoiceAllocationDto>>(
            Assert.IsType<OkObjectResult>((await controller.GetAllocations(seed.RequestId, invoice.Id)).Result).Value);

        var row = Assert.Single(read);
        Assert.Equal(group.Id, row.RequestPoGroupId);
        Assert.True(row.IsPendingDecision);
        Assert.False(row.IsEffective);
        Assert.Equal("ZZTEST Supplier", row.GroupSupplierName);
        Assert.Equal("AOA", row.GroupCurrencyCode);
    }
}
