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

using Agg = RequestConstants.OperationInvoiceStatuses;
using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// Release 4 activation tooling: GET preview / POST apply of the expected-operation-invoice-total
/// backfill.
///
/// <para>Pinned: preview mutates nothing and explains every skip; apply is SysAdmin-only with a
/// mandatory meaningful reason; the write freezes the group's own TotalAmount with the audit trio
/// and the "[ATIVAÇÃO R4]" justification prefix; a non-null expected total is NEVER overwritten;
/// and a second apply run finds nothing left to write — structural idempotency.</para>
/// </summary>
public class Release4ActivationTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static Release4ActivationController BuildController(
        ApplicationDbContext ctx, Guid actorId, string role = RoleConstants.SystemAdministrator)
    {
        var controller = new Release4ActivationController(
            ctx,
            NullLogger<Release4ActivationController>.Instance,
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
        var actor = new User { Id = Guid.NewGuid(), FullName = "Activation Tester", Email = "activation@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus
        {
            Id = 30, Code = RequestConstants.Statuses.Paid, Name = "Pago", DisplayOrder = 30
        });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-ACT-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST activation",
            RequestTypeId = 2,
            StatusId = 30,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-40)
        };
        ctx.Requests.Add(request);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static RequestPoGroup AddGroup(
        ApplicationDbContext ctx, Seed seed, decimal total,
        string? type = Types.Proforma, bool requires = true, decimal? expected = null)
    {
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = "AOA",
            TotalAmount = total,
            Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = type,
            OperationInvoiceStatus = requires ? Agg.PendingUpload : Agg.NotRequired,
            RequiresOperationInvoice = requires,
            ExpectedOperationInvoiceTotal = expected,
            ExpectedOperationInvoiceCurrency = expected.HasValue ? "AOA" : null,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(group);
        return group;
    }

    private const string ValidReason = "ZZTEST ativação Release 4 aprovada pelo Financeiro";

    // ── Preview ──

    [Fact]
    public async Task Preview_splits_eligible_from_skipped_with_reasons_and_mutates_nothing()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var eligible = AddGroup(ctx, seed, total: 750_000m);
        var unclassified = AddGroup(ctx, seed, total: 500_000m, type: null, requires: false);
        var notRequired = AddGroup(ctx, seed, total: 500_000m, requires: false);
        var noTotal = AddGroup(ctx, seed, total: 0m);
        var alreadySet = AddGroup(ctx, seed, total: 900_000m, expected: 900_000m);   // not a candidate at all
        await ctx.SaveChangesAsync();

        var preview = Assert.IsType<ExpectedTotalBackfillPreviewDto>(
            Assert.IsType<OkObjectResult>(
                await BuildController(ctx, seed.ActorId, RoleConstants.Finance).Preview()).Value);

        Assert.Equal(1, preview.EligibleCount);
        Assert.Equal(3, preview.SkippedCount);
        Assert.Equal(4, preview.Groups.Count);   // the non-null group never appears

        var eligibleRow = Assert.Single(preview.Groups, g => g.Eligible);
        Assert.Equal(eligible.Id, eligibleRow.RequestPoGroupId);
        Assert.Equal(750_000m, eligibleRow.ProposedExpectedTotal);

        Assert.Equal(Release4ActivationController.SkipNotClassified,
            preview.Groups.Single(g => g.RequestPoGroupId == unclassified.Id).SkipReason);
        Assert.Equal(Release4ActivationController.SkipNotRequired,
            preview.Groups.Single(g => g.RequestPoGroupId == notRequired.Id).SkipReason);
        Assert.Equal(Release4ActivationController.SkipNoTotal,
            preview.Groups.Single(g => g.RequestPoGroupId == noTotal.Id).SkipReason);
        Assert.DoesNotContain(preview.Groups, g => g.RequestPoGroupId == alreadySet.Id);

        // A dry run changed nothing.
        ctx.ChangeTracker.Clear();
        Assert.Null((await ctx.RequestPoGroups.SingleAsync(g => g.Id == eligible.Id))
            .ExpectedOperationInvoiceTotal);
    }

    [Fact]
    public async Task Preview_is_finance_or_sysadmin_apply_is_sysadmin_only()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        await ctx.SaveChangesAsync();

        Assert.Equal(403, Assert.IsType<ObjectResult>(
            await BuildController(ctx, seed.ActorId, RoleConstants.Buyer).Preview()).StatusCode);

        var financeApply = await BuildController(ctx, seed.ActorId, RoleConstants.Finance)
            .Apply(new ApplyExpectedTotalBackfillDto { Reason = ValidReason });
        Assert.Equal(403, Assert.IsType<ObjectResult>(financeApply.Result).StatusCode);
    }

    // ── Apply ──

    [Fact]
    public async Task Apply_writes_the_baseline_with_the_audit_trio_and_rederives()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, total: 750_000m);
        await ctx.SaveChangesAsync();

        var result = Assert.IsType<ExpectedTotalBackfillResultDto>(
            Assert.IsType<OkObjectResult>((await BuildController(ctx, seed.ActorId)
                .Apply(new ApplyExpectedTotalBackfillDto { Reason = ValidReason })).Result).Value);

        Assert.Equal(1, result.WrittenCount);

        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal(750_000m, persisted.ExpectedOperationInvoiceTotal);
        Assert.Equal("AOA", persisted.ExpectedOperationInvoiceCurrency);
        Assert.Equal(seed.ActorId, persisted.ExpectedTotalSetByUserId);
        Assert.NotNull(persisted.ExpectedTotalSetAtUtc);
        Assert.StartsWith("[ATIVAÇÃO R4]", persisted.ExpectedTotalJustification);
        Assert.Contains(ValidReason, persisted.ExpectedTotalJustification);

        Assert.True(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "OI_EXPECTED_TOTAL_BACKFILLED" && h.RequestId == seed.RequestId));
    }

    [Fact]
    public async Task Apply_requires_a_meaningful_reason()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        AddGroup(ctx, seed, total: 750_000m);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        Assert.IsType<BadRequestObjectResult>(
            (await controller.Apply(new ApplyExpectedTotalBackfillDto { Reason = "curto" })).Result);
        Assert.IsType<BadRequestObjectResult>(
            (await controller.Apply(new ApplyExpectedTotalBackfillDto { Reason = null })).Result);

        ctx.ChangeTracker.Clear();
        Assert.False(await ctx.RequestPoGroups.AnyAsync(g => g.ExpectedOperationInvoiceTotal != null));
    }

    [Fact]
    public async Task A_second_apply_finds_nothing_and_never_overwrites()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var group = AddGroup(ctx, seed, total: 750_000m);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId);
        await controller.Apply(new ApplyExpectedTotalBackfillDto { Reason = ValidReason });

        // The group's baseline moves in real life; the activation value must not follow it.
        ctx.ChangeTracker.Clear();
        var persisted = await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id);
        persisted.TotalAmount = 999_999m;
        await ctx.SaveChangesAsync();

        var second = Assert.IsType<ExpectedTotalBackfillResultDto>(
            Assert.IsType<OkObjectResult>((await controller
                .Apply(new ApplyExpectedTotalBackfillDto { Reason = ValidReason })).Result).Value);

        Assert.Equal(0, second.WrittenCount);
        ctx.ChangeTracker.Clear();
        Assert.Equal(750_000m, (await ctx.RequestPoGroups.SingleAsync(g => g.Id == group.Id))
            .ExpectedOperationInvoiceTotal);
        Assert.Equal(1, await ctx.RequestStatusHistories
            .CountAsync(h => h.ActionTaken == "OI_EXPECTED_TOTAL_BACKFILLED"));
    }
}
