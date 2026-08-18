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
/// Release 4 Phase 4A: the REAL Phase 1 (group completion) lifecycle — transitions, stamps,
/// idempotent history and the strict caller contract (no SaveChanges, no transaction, exact
/// no-op while the completion flag is off, parent untouched).
/// </summary>
public class RequestCompletionServicePhaseOneTests
{
    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static RequestCompletionService Service(
        ApplicationDbContext context, bool completionEnabled = true) =>
        new(context,
            Options.Create(new PostPaymentCompletionOptions
            {
                Enabled = true,
                CompletionEnabled = completionEnabled,
                EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
            }),
            NullLogger<RequestCompletionService>.Instance);

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ActorId, int RequestStatusId);

    /// <summary>
    /// A PAYMENT request in WAITING_RECEIPT owning one group that satisfies every obligation
    /// except what each test varies. Defaults: classified PROFORMA, SATISFIED invoice, paid
    /// stage, receipt stamped, separate fiscal receipt required and NOT yet uploaded.
    /// </summary>
    private static async Task<Seed> SeedAsync(
        ApplicationDbContext ctx,
        Action<RequestPoGroup>? mutateGroup = null,
        Action<ApplicationDbContext, Request, RequestPoGroup>? extraSeed = null)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Completion Actor", Email = "p4a@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);

        var waitingReceipt = new RequestStatus { Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 };
        var completed = new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 };
        ctx.RequestStatuses.AddRange(waitingReceipt, completed);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-P4A-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST phase 4A",
            RequestTypeId = requestType.Id,
            StatusId = waitingReceipt.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-20)
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST Supplier P4A",
            CurrencyCode = "AOA",
            TotalAmount = 500_000m,
            Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied,
            RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = true,
            OperationalReceiptCompletedAtUtc = DateTime.UtcNow.AddDays(-2),
            OperationalReceiptCompletedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-20),
            CreatedByUserId = actor.Id
        };
        mutateGroup?.Invoke(group);
        ctx.RequestPoGroups.Add(group);

        extraSeed?.Invoke(ctx, request, group);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, group.Id, actor.Id, waitingReceipt.Id);
    }

    private static Task<Seed> SeedNofrCompletableAsync(ApplicationDbContext ctx) =>
        SeedAsync(ctx, g => g.RequiresSeparateFiscalReceipt = false);

    // ── §12/§14: WAITING_FISCAL_RECEIPT antechamber ──

    [Fact]
    public async Task Ready_group_with_required_receipt_moves_to_waiting_fiscal_receipt()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx);

        var result = await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingFiscalReceipt, group.Status);
        Assert.Null(group.CompletedAtUtc);
        Assert.False(result.AnyGroupCompleted);
        Assert.False(result.ParentEvaluationRequired);

        var unlock = await ctx.RequestStatusHistories.SingleAsync(
            h => h.ActionTaken == WorkflowEventCodes.FiscalReceiptUnlocked);
        Assert.Equal(PostPaymentIdempotencyKeys.FiscalReceiptUnlocked(seed.GroupId), unlock.IdempotencyKey);
        Assert.Equal(seed.ActorId, unlock.ActorUserId);
        // Group-scoped event: the parent status ids are carried, not changed.
        Assert.Equal(seed.RequestStatusId, unlock.NewStatusId);
    }

    [Fact]
    public async Task Waiting_fiscal_receipt_group_completes_once_the_receipt_stamp_exists()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var attachmentId = Guid.NewGuid();
        var seed = await SeedAsync(ctx, g =>
        {
            g.Status = RequestConstants.PoGroupStatuses.WaitingFiscalReceipt;
            g.FiscalReceiptAttachmentId = attachmentId;
            g.FiscalReceiptUploadedAtUtc = DateTime.UtcNow;
        });

        var result = await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, group.Status);
        Assert.NotNull(group.CompletedAtUtc);
        Assert.True(result.AnyGroupCompleted);
        Assert.Contains(seed.GroupId, result.CompletedGroupIds);
        Assert.True(result.ParentEvaluationRequired);

        var history = await ctx.RequestStatusHistories.SingleAsync(
            h => h.ActionTaken == WorkflowEventCodes.GroupCompleted);
        Assert.Equal(
            PostPaymentIdempotencyKeys.GroupCompleted(seed.GroupId, attachmentId),
            history.IdempotencyKey);
    }

    // ── §13: no separate fiscal receipt → direct completion, NOFR identity ──

    [Fact]
    public async Task Nofr_group_completes_directly_without_visiting_waiting_fiscal_receipt()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedNofrCompletableAsync(ctx);

        var result = await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, group.Status);
        Assert.NotNull(group.CompletedAtUtc);
        Assert.Null(group.FiscalReceiptAttachmentId);
        Assert.True(result.AnyGroupCompleted);

        // Never passed through the antechamber and never asked for a receipt.
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(
            h => h.ActionTaken == WorkflowEventCodes.FiscalReceiptUnlocked));

        var history = await ctx.RequestStatusHistories.SingleAsync(
            h => h.ActionTaken == WorkflowEventCodes.GroupCompleted);
        Assert.Equal(
            PostPaymentIdempotencyKeys.GroupCompletedWithoutFiscalReceipt(seed.GroupId),
            history.IdempotencyKey);
        Assert.EndsWith(":NOFR", history.IdempotencyKey);
        Assert.Contains("Recibo Fiscal separado não exigido", history.Comment);
    }

    // ── §15: lazy operational receipt ──

    [Fact]
    public async Task Lazy_receipt_stamp_derives_from_item_records_and_says_so()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var received = new LineItemStatus { Id = 91, Code = "RECEIVED", Name = "Recebido" };
        ctx.LineItemStatuses.Add(received);

        var seed = await SeedAsync(ctx,
            mutateGroup: g =>
            {
                g.OperationalReceiptCompletedAtUtc = null;
                g.OperationalReceiptCompletedByUserId = null;
            },
            extraSeed: (c, request, group) =>
            {
                c.RequestLineItems.Add(new RequestLineItem
                {
                    Id = Guid.NewGuid(),
                    RequestId = request.Id,
                    RequestPoGroupId = group.Id,
                    LineNumber = 1,
                    Description = "ZZTEST received item",
                    Quantity = 2,
                    ReceivedQuantity = 2,
                    LineItemStatusId = received.Id
                });
            });

        var before = DateTime.UtcNow;
        await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        Assert.NotNull(group.OperationalReceiptCompletedAtUtc);
        // The stamp is the EVALUATION instant — the physical receiving date is never fabricated.
        Assert.True(group.OperationalReceiptCompletedAtUtc >= before.AddSeconds(-5));
        Assert.Equal(seed.ActorId, group.OperationalReceiptCompletedByUserId);

        var stamp = await ctx.RequestStatusHistories.SingleAsync(
            h => h.ActionTaken == WorkflowEventCodes.OperationalReceiptCompleted);
        Assert.Equal(PostPaymentIdempotencyKeys.OperationalReceiptCompleted(seed.GroupId), stamp.IdempotencyKey);
        Assert.Contains("derivado dos registos de recebimento pré-existentes", stamp.Comment);
        Assert.Contains("não a data física", stamp.Comment);

        // With the receipt derived and everything else satisfied, the required-receipt group
        // proceeds to the antechamber in the same evaluation.
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingFiscalReceipt, group.Status);
    }

    [Fact]
    public async Task Partial_receiving_gets_no_stamp_and_no_transition()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var received = new LineItemStatus { Id = 91, Code = "RECEIVED", Name = "Recebido" };
        var partial = new LineItemStatus { Id = 92, Code = "PARTIALLY_RECEIVED", Name = "Parcial" };
        ctx.LineItemStatuses.AddRange(received, partial);

        var seed = await SeedAsync(ctx,
            mutateGroup: g => g.OperationalReceiptCompletedAtUtc = null,
            extraSeed: (c, request, group) =>
            {
                c.RequestLineItems.AddRange(
                    new RequestLineItem
                    {
                        Id = Guid.NewGuid(), RequestId = request.Id, RequestPoGroupId = group.Id,
                        LineNumber = 1, Description = "ZZTEST ok", Quantity = 1, LineItemStatusId = received.Id
                    },
                    new RequestLineItem
                    {
                        Id = Guid.NewGuid(), RequestId = request.Id, RequestPoGroupId = group.Id,
                        LineNumber = 2, Description = "ZZTEST partial", Quantity = 5, LineItemStatusId = partial.Id
                    });
            });

        await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        Assert.Null(group.OperationalReceiptCompletedAtUtc);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, group.Status);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(
            h => h.IdempotencyKey != null));
    }

    // ── Idempotency / no-ops ──

    [Fact]
    public async Task Repeated_evaluation_is_idempotent_and_writes_no_duplicate_history()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedNofrCompletableAsync(ctx);
        var service = Service(ctx);

        var first = await service.EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();
        var second = await service.EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        Assert.True(first.AnyGroupCompleted);
        // Second pass finds the group COMPLETED: strict no-op, nothing re-stamped.
        Assert.False(second.AnyGroupCompleted);
        Assert.Empty(second.CompletedGroupIds);

        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == WorkflowEventCodes.GroupCompleted));

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, group.Status);
    }

    [Fact]
    public async Task Same_transaction_double_evaluation_does_not_duplicate_pending_history()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedNofrCompletableAsync(ctx);
        var service = Service(ctx);

        // Two evaluations BEFORE the caller saves: the change-tracker check must dedupe.
        await service.EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await service.EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == WorkflowEventCodes.GroupCompleted));
    }

    [Fact]
    public async Task Unclassified_group_is_skipped_untouched()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, g =>
        {
            g.SourceDocumentType = null;
            g.OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Unclassified;
        });

        var result = await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, group.Status);
        Assert.Null(group.CompletedAtUtc);
        Assert.False(result.AnyGroupCompleted);
        Assert.Null(result.ErrorMessage); // skipped safely, never thrown on
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.IdempotencyKey != null));
    }

    [Fact]
    public async Task Planned_owed_payment_blocks_the_transition()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx,
            extraSeed: (c, request, group) => c.RequestPayments.Add(new RequestPayment
            {
                RequestId = request.Id,
                RequestPoGroupId = group.Id,
                PaymentType = RequestPayment.PaymentTypes.FinalBalance,
                PaymentStatus = RequestPayment.PaymentStatuses.Planned,
                PlannedAmount = 100m,
                CurrencyCode = "AOA",
                CreatedByUserId = request.RequesterId,
                CreatedAtUtc = DateTime.UtcNow
            }));

        await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, group.Status);
    }

    // ── v2.229.2 (REQ-17/08/2026-232): full-advance evidence no longer blocks Phase 1 ──

    [Fact]
    public async Task Full_advance_group_completes_once_every_other_dimension_is_satisfied()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, g =>
        {
            // The real post-ConfirmAdvancePayment shape: never a paid-stage ladder status.
            g.Status = RequestConstants.PoGroupStatuses.AdvancePaymentCompleted;
            g.TotalAmount = 100_000m;
            g.PaymentConditionCode = RequestConstants.PaymentConditions.AdvanceFull;
            g.RequiresSeparateFiscalReceipt = false; // NOFR so only payment could block
        });
        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = seed.RequestId,
            RequestPoGroupId = seed.GroupId,
            PaymentType = RequestPayment.PaymentTypes.Advance,
            PaymentStatus = RequestPayment.PaymentStatuses.Completed,
            PlannedAmount = 100_000m,
            ActualPaidAmount = 100_000m,
            CurrencyCode = "AOA",
            CreatedByUserId = seed.ActorId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        // PAYMENT_PENDING is gone, and with every other dimension satisfied the group completes.
        Assert.True(result.AnyGroupCompleted);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, group.Status);
    }

    [Fact]
    public async Task Partial_advance_group_never_completes_on_evidence()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, g =>
        {
            g.Status = RequestConstants.PoGroupStatuses.AdvancePaymentCompleted;
            g.TotalAmount = 100_000m;
            g.PaymentConditionCode = RequestConstants.PaymentConditions.AdvancePartial;
            g.RequiresSeparateFiscalReceipt = false;
        });
        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = seed.RequestId,
            RequestPoGroupId = seed.GroupId,
            PaymentType = RequestPayment.PaymentTypes.Advance,
            PaymentStatus = RequestPayment.PaymentStatuses.Completed,
            PlannedAmount = 30_000m,
            ActualPaidAmount = 30_000m,
            CurrencyCode = "AOA",
            CreatedByUserId = seed.ActorId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        Assert.False(result.AnyGroupCompleted);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.AdvancePaymentCompleted, group.Status);
    }

    [Fact]
    public async Task Missing_request_reports_an_error_instead_of_throwing()
    {
        using var ctx = new ApplicationDbContext(NewOptions());

        var result = await Service(ctx).EvaluateGroupCompletionAsync(Guid.NewGuid(), null, Guid.NewGuid());

        Assert.NotNull(result.ErrorMessage);
        Assert.False(result.AnyGroupCompleted);
    }

    // ── §18: the completion flag ──

    [Fact]
    public async Task Completion_disabled_is_an_exact_no_op_even_over_a_ready_group()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedNofrCompletableAsync(ctx);
        ctx.ChangeTracker.Clear();

        var result = await Service(ctx, completionEnabled: false)
            .EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);

        Assert.False(result.AnyGroupCompleted);
        // Not even a read: nothing tracked, nothing stamped, nothing to save.
        Assert.Empty(ctx.ChangeTracker.Entries());

        var untouched = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, untouched.Status);
        Assert.Null(untouched.CompletedAtUtc);
    }

    // ── Caller contract: no SaveChanges, no transaction, parent untouched ──

    [Fact]
    public async Task Service_persists_nothing_by_itself()
    {
        var options = NewOptions();
        using var ctx = new ApplicationDbContext(options);
        var seed = await SeedNofrCompletableAsync(ctx);

        var result = await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        Assert.True(result.AnyGroupCompleted);

        // A second context over the SAME store sees only saved state: the caller owns persistence.
        using var fresh = new ApplicationDbContext(options);
        var storedGroup = await fresh.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, storedGroup.Status);
        Assert.Null(storedGroup.CompletedAtUtc);
        Assert.False(await fresh.RequestStatusHistories.AsNoTracking().AnyAsync());

        // No transaction was opened by the service.
        Assert.Null(ctx.Database.CurrentTransaction);

        // The caller's SaveChanges is what persists the evaluation.
        await ctx.SaveChangesAsync();
        var saved = await fresh.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, saved.Status);
    }

    [Fact]
    public async Task Phase_one_alone_never_completes_the_parent()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedNofrCompletableAsync(ctx);
        var service = Service(ctx);

        await service.EvaluateGroupCompletionAsync(seed.RequestId, seed.GroupId, seed.ActorId);
        await ctx.SaveChangesAsync();

        // Every group is COMPLETED, yet Phase 1 never touches the parent — that transition
        // belongs exclusively to Phase 2 (Phase 4C), invoked by callers strictly after commit.
        var request = await ctx.Requests.SingleAsync(r => r.Id == seed.RequestId);
        Assert.Equal(seed.RequestStatusId, request.StatusId);
        Assert.Null(request.CompletionCycleId);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));

        // Phase 2 (real since 4C) then performs the authoritative transition once.
        var parent = await service.EvaluateParentCompletionAsync(seed.RequestId, seed.ActorId);
        Assert.True(parent.RequestCompleted);
        Assert.NotNull(parent.CompletionCycleId);
    }

    [Fact]
    public async Task Evaluating_the_whole_request_covers_sibling_groups_independently()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        Guid blockedGroupId = Guid.Empty;
        var seed = await SeedAsync(ctx,
            mutateGroup: g => g.RequiresSeparateFiscalReceipt = false,
            extraSeed: (c, request, _) =>
            {
                var blocked = new RequestPoGroup
                {
                    Id = Guid.NewGuid(),
                    RequestId = request.Id,
                    SupplierNameSnapshot = "ZZTEST Blocked Sibling",
                    CurrencyCode = "AOA",
                    TotalAmount = 100m,
                    Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
                    SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
                    OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.PendingUpload,
                    RequiresOperationInvoice = true,
                    RequiresSeparateFiscalReceipt = true,
                    OperationalReceiptCompletedAtUtc = DateTime.UtcNow,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedByUserId = request.RequesterId
                };
                blockedGroupId = blocked.Id;
                c.RequestPoGroups.Add(blocked);
            });

        var result = await Service(ctx).EvaluateGroupCompletionAsync(seed.RequestId, null, seed.ActorId);
        await ctx.SaveChangesAsync();

        Assert.Equal(new[] { seed.GroupId }, result.CompletedGroupIds);

        var completed = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        var blocked = await ctx.RequestPoGroups.SingleAsync(g => g.Id == blockedGroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, completed.Status);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, blocked.Status);
    }
}
