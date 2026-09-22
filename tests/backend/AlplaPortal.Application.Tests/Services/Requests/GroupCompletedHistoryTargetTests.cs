using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.245.9 — GROUP_COMPLETED audit target. The writer (Phase 1) keeps the REQUEST scalar in the
/// status FKs (request status domain, consumed by request-level readers); the DISPLAYED target,
/// resolved by <see cref="GroupLifecycleHistoryTarget"/>, is the GROUP's resulting state
/// ("Concluído") — for the only group, for one of several, whatever the scalar, and when the
/// request completes right afterwards (REQUEST_COMPLETED → "Finalizado" stays the request-level
/// transition). No STATUS_SYNC is fabricated and no legitimate event is suppressed.
/// </summary>
public class GroupCompletedHistoryTargetTests
{
    private const int STATUS_WAITING_RECEIPT_ID = 16;
    private const int STATUS_COMPLETED_ID = 17;
    private const int STATUS_PAYMENT_COMPLETED_ID = 15;
    private const int STATUS_WAITING_AREA_ID = 3;

    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static RequestCompletionService Service(ApplicationDbContext ctx) =>
        new(ctx,
            Options.Create(new PostPaymentCompletionOptions
            {
                Enabled = true,
                CompletionEnabled = true,
                EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
            }),
            NullLogger<RequestCompletionService>.Instance);

    private sealed record Seed(Guid RequestId, Guid ActorId, Guid[] GroupIds);

    /// <summary>
    /// A PAYMENT request owning N classified groups that already satisfy every obligation except the
    /// separate Fiscal Receipt: <paramref name="requiresFiscalReceipt"/>[i] = true keeps group i short of
    /// completion (antechamber), false lets it complete directly (NOFR). A legacy SUBMIT row is seeded
    /// so pre-existing history is proven to stay readable.
    /// </summary>
    private static async Task<Seed> SeedAsync(
        ApplicationDbContext ctx, int requestStatusId = STATUS_WAITING_RECEIPT_ID, params bool[] requiresFiscalReceipt)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST History Target Actor", Email = "ht@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = STATUS_WAITING_AREA_ID, Code = "WAITING_AREA_APPROVAL", Name = "Aguardando Aprovação de Área", DisplayOrder = 3 },
            new RequestStatus { Id = STATUS_PAYMENT_COMPLETED_ID, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Realizado", DisplayOrder = 16 },
            new RequestStatus { Id = STATUS_WAITING_RECEIPT_ID, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 },
            new RequestStatus { Id = STATUS_COMPLETED_ID, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-HT-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST history target",
            RequestTypeId = 2,
            StatusId = requestStatusId,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-20)
        };
        ctx.Requests.Add(request);

        // Pre-existing (legacy) request-level transition row.
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(), RequestId = request.Id, ActorUserId = actor.Id, ActionTaken = "SUBMIT",
            PreviousStatusId = null, NewStatusId = STATUS_WAITING_AREA_ID, Comment = "legacy", CreatedAtUtc = DateTime.UtcNow.AddDays(-19)
        });

        var groupIds = new Guid[requiresFiscalReceipt.Length];
        for (var i = 0; i < requiresFiscalReceipt.Length; i++)
        {
            var group = new RequestPoGroup
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                SupplierNameSnapshot = $"ZZTEST Supplier {i + 1}",
                CurrencyCode = "AOA",
                TotalAmount = 100_000m,
                Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
                SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
                OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied,
                RequiresOperationInvoice = true,
                RequiresSeparateFiscalReceipt = requiresFiscalReceipt[i],
                OperationalReceiptCompletedAtUtc = DateTime.UtcNow.AddDays(-2),
                OperationalReceiptCompletedByUserId = actor.Id,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-20),
                CreatedByUserId = actor.Id
            };
            ctx.RequestPoGroups.Add(group);
            groupIds[i] = group.Id;
        }

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id, groupIds);
    }

    /// <summary>The caller contract: Phase 1 inside the caller's unit of work, Phase 2 after it committed.</summary>
    private static async Task RunCompletionAsync(ApplicationDbContext ctx, Seed seed)
    {
        await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, null, seed.ActorId);
        await ctx.SaveChangesAsync();
        await Service(ctx).EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);
    }

    private static string Display(RequestStatusHistory row, ApplicationDbContext ctx) =>
        GroupLifecycleHistoryTarget.ResolveDisplayName(row.ActionTaken, ctx.RequestStatuses.Single(s => s.Id == row.NewStatusId).Name);

    [Fact]
    public async Task Single_group_completion_then_request_completion_one_group_completed_targeting_concluido_and_one_request_completed_targeting_finalizado()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, STATUS_WAITING_RECEIPT_ID, false);

        await RunCompletionAsync(ctx, seed);

        var rows = await ctx.RequestStatusHistories.Where(h => h.RequestId == seed.RequestId).ToListAsync();

        var groupCompleted = Assert.Single(rows, h => h.ActionTaken == WorkflowEventCodes.GroupCompleted);
        // FK truthfulness: the request scalar at the time (WAITING_RECEIPT) — the request status domain is NOT rewritten.
        Assert.Equal(STATUS_WAITING_RECEIPT_ID, groupCompleted.NewStatusId);
        Assert.Equal(STATUS_WAITING_RECEIPT_ID, groupCompleted.PreviousStatusId);
        // Displayed target: the GROUP's resulting state.
        Assert.Equal("Concluído", Display(groupCompleted, ctx));

        var requestCompleted = Assert.Single(rows, h => h.ActionTaken == "REQUEST_COMPLETED");
        Assert.Equal(STATUS_COMPLETED_ID, requestCompleted.NewStatusId);
        Assert.Equal(STATUS_WAITING_RECEIPT_ID, requestCompleted.PreviousStatusId);
        Assert.Equal("Finalizado", Display(requestCompleted, ctx));
        Assert.True(requestCompleted.CreatedAtUtc >= groupCompleted.CreatedAtUtc);

        Assert.DoesNotContain(rows, h => h.ActionTaken == "STATUS_SYNC");
        Assert.Equal(STATUS_COMPLETED_ID, (await ctx.Requests.SingleAsync(r => r.Id == seed.RequestId)).StatusId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, (await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupIds[0])).Status);
    }

    [Fact]
    public async Task One_group_completes_while_another_remains_pending_exactly_one_group_completed_targeting_concluido_and_no_request_completed()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, STATUS_WAITING_RECEIPT_ID, false, true);

        await RunCompletionAsync(ctx, seed);

        var rows = await ctx.RequestStatusHistories.Where(h => h.RequestId == seed.RequestId).ToListAsync();

        var groupCompleted = Assert.Single(rows, h => h.ActionTaken == WorkflowEventCodes.GroupCompleted);
        Assert.Equal(PostPaymentIdempotencyKeys.GroupCompletedWithoutFiscalReceipt(seed.GroupIds[0]), groupCompleted.IdempotencyKey);
        Assert.Equal(STATUS_WAITING_RECEIPT_ID, groupCompleted.NewStatusId);
        Assert.Equal("Concluído", Display(groupCompleted, ctx));

        // The sibling entered the antechamber: its own group-scoped row targets the GROUP's state too.
        var unlocked = Assert.Single(rows, h => h.ActionTaken == WorkflowEventCodes.FiscalReceiptUnlocked);
        Assert.Equal(STATUS_WAITING_RECEIPT_ID, unlocked.NewStatusId);
        Assert.Equal("Aguardando Recibo Fiscal", Display(unlocked, ctx));

        Assert.DoesNotContain(rows, h => h.ActionTaken == "REQUEST_COMPLETED");
        Assert.DoesNotContain(rows, h => h.ActionTaken == "STATUS_SYNC");
        Assert.Equal(STATUS_WAITING_RECEIPT_ID, (await ctx.Requests.SingleAsync(r => r.Id == seed.RequestId)).StatusId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, (await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupIds[0])).Status);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingFiscalReceipt, (await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupIds[1])).Status);
    }

    [Fact]
    public async Task Request_scalar_does_not_alter_the_group_completed_target()
    {
        // Two groups, one completes, the scalar is PAYMENT_COMPLETED (not yet aggregated to WAITING_RECEIPT).
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, STATUS_PAYMENT_COMPLETED_ID, false, true);

        await RunCompletionAsync(ctx, seed);

        var groupCompleted = await ctx.RequestStatusHistories.SingleAsync(h => h.ActionTaken == WorkflowEventCodes.GroupCompleted);
        Assert.Equal(STATUS_PAYMENT_COMPLETED_ID, groupCompleted.NewStatusId);   // FK still the scalar of the moment
        Assert.Equal("Concluído", Display(groupCompleted, ctx));                  // display still the group's state
        Assert.Equal(STATUS_PAYMENT_COMPLETED_ID, (await ctx.Requests.SingleAsync(r => r.Id == seed.RequestId)).StatusId);
    }

    [Fact]
    public async Task Re_evaluation_never_duplicates_group_completed_or_request_completed()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, STATUS_WAITING_RECEIPT_ID, false);

        await RunCompletionAsync(ctx, seed);
        await RunCompletionAsync(ctx, seed);   // retry / second trigger

        var rows = await ctx.RequestStatusHistories.Where(h => h.RequestId == seed.RequestId).ToListAsync();
        Assert.Single(rows, h => h.ActionTaken == WorkflowEventCodes.GroupCompleted);
        Assert.Single(rows, h => h.ActionTaken == "REQUEST_COMPLETED");
        Assert.DoesNotContain(rows, h => h.ActionTaken == "STATUS_SYNC");
    }

    [Fact]
    public async Task Existing_history_rows_remain_readable_and_unchanged()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, STATUS_WAITING_RECEIPT_ID, false);

        await RunCompletionAsync(ctx, seed);

        var legacy = await ctx.RequestStatusHistories.SingleAsync(h => h.RequestId == seed.RequestId && h.ActionTaken == "SUBMIT");
        Assert.Equal(STATUS_WAITING_AREA_ID, legacy.NewStatusId);
        Assert.Equal("Aguardando Aprovação de Área", Display(legacy, ctx));   // pass-through, never relabelled
        Assert.Equal("legacy", legacy.Comment);
    }
}
