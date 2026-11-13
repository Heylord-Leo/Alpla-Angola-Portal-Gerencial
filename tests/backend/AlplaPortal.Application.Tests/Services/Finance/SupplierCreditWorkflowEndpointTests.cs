using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.DTOs.SupplierCredit;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Interfaces.Suppliers;
using AlplaPortal.Application.Tests.Services.Suppliers;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;
using SC = AlplaPortal.Domain.Constants.SupplierCreditConstants;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Supplier account credit — the real endpoints on InMemory: register-po reserves credit (responsible Buyer only, flag on),
/// finance/{id}/apply-credit settles a full coverage exactly once, finance/{id}/pay enforces the strict bank amount and
/// applies the credit atomically, reverse-credit needs a different Finance user, and the P.O. group cancellation releases
/// the hold. Account: supplier B × company 1 × AOA with 1 000; group B = 500 AOA, WAITING_PO.
/// </summary>
public class SupplierCreditWorkflowEndpointTests
{
    internal static SupplierCreditSettlementService Svc(ApplicationDbContext ctx, bool enabled = true)
        => new(ctx, Options.Create(new SupplierCreditOptions { NewReservationsEnabled = enabled }), NullLogger<SupplierCreditSettlementService>.Instance);

    internal static RequestsController Buyer(ApplicationDbContext ctx, Guid userId, bool enabled = true, Mock<IWorkflowNotificationOrchestrator>? orchestrator = null, string? role = null)
    {
        orchestrator ??= new Mock<IWorkflowNotificationOrchestrator>();
        orchestrator.Setup(o => o.EmitAsync(It.IsAny<Domain.Events.WorkflowEvent>())).Returns(Task.CompletedTask);
        var ctl = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, S.AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions()),
            Svc(ctx, enabled), Options.Create(new SupplierCreditOptions { NewReservationsEnabled = enabled }));
        ctl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, role ?? RoleConstants.Buyer) }, "Test")),
                RequestServices = new ServiceCollection().AddScoped<IStatusAggregationService>(_ => new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance)).BuildServiceProvider()
            }
        };
        return ctl;
    }

    internal static FinanceController Finance(ApplicationDbContext ctx, Guid userId, params string[] roles) => Finance(ctx, userId, true, roles);

    internal static FinanceController Finance(ApplicationDbContext ctx, Guid userId, bool enabled, params string[] roles)
    {
        var ctl = new FinanceController(ctx, new Mock<IWorkflowNotificationOrchestrator>().Object, NullLogger<FinanceController>.Instance,
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance), new FinancePaymentEligibilityService(), null, Svc(ctx, enabled));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        foreach (var r in roles.Length == 0 ? new[] { RoleConstants.Finance } : roles) claims.Add(new Claim(ClaimTypes.Role, r));
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } };
        return ctl;
    }

    internal const string Hash = "cccc0000cccc0000cccc0000cccc0000cccc0000cccc0000cccc0000cccc0000";

    /// <summary>Group B with a P.O. file whose server-held extraction identifies it (no divergence) and an account with 1 000 AOA.</summary>
    internal static async Task<(S.Seed s, Guid file, Guid account)> SeedAsync(ApplicationDbContext ctx, decimal credit = 1000m)
    {
        var s = await S.SeedAsync(ctx);
        var file = Guid.NewGuid();
        ctx.RequestAttachments.Add(new RequestAttachment { Id = file, RequestId = s.RequestId, RequestPoGroupId = s.GroupB, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = "po-b.pdf", FileExtension = ".pdf", StorageReference = "po-b.pdf", FileHash = Hash, UploadedByUserId = s.Owner, UploadedAtUtc = DateTime.UtcNow });
        ctx.PoDocumentExtractions.Add(new PoDocumentExtraction { RequestId = s.RequestId, FileHash = Hash, FileName = "po-b.pdf", ExtractedByUserId = s.Owner, CreatedAtUtc = DateTime.UtcNow, Success = true, DocumentNumber = "ECF10 2026/9630", DocumentTitle = "Encomenda Mat Escritório/Diversos ECF10 2026/9630", SupplierName = "OUTRO FORNECEDOR", CurrencyCode = "AOA", GrandTotal = 500m });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var account = await SupplierCreditSettlementServiceTests.SeedAccountAsync(ctx, S.SupplierB, S.Company, "AOA", credit, s.Owner);
        return (s, file, account);
    }

    internal static RegisterPoActionDto Dto(Guid groupId, Guid fileId, decimal? credit, string terms = RequestConstants.PaymentConditions.PostPaid, string? correction = null) => new()
    {
        PoGroupId = groupId, PoAttachmentId = fileId, PaymentConditionCode = terms, PurchaseOrderNumber = "ECF10 2026/9630", PurchaseOrderNumberSource = "EXTRACTED",
        UseSupplierCredit = credit.HasValue && correction == null, SupplierCreditAmount = credit, SupplierCreditCorrectionAction = correction,
    };

    internal static ProblemDetails Problem(IActionResult r) => Assert.IsType<ProblemDetails>(((ObjectResult)r).Value);

    [Fact]
    public async Task RegisterPo_with_partial_credit_reserves_creates_owed_rows_and_issues_the_po()
    {
        await using var ctx = S.NewCtx();
        var (s, file, account) = await SeedAsync(ctx);
        var result = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, file, 350m));
        Assert.IsType<OkObjectResult>(result);
        ctx.ChangeTracker.Clear();

        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB);
        Assert.Equal(RequestConstants.Statuses.PoIssued, g.Status); Assert.Equal(350m, g.CreditCoverageAmount); Assert.NotNull(g.SupplierCreditReservationId);
        var res = await ctx.SupplierCreditReservations.SingleAsync(x => x.RequestPoGroupId == s.GroupB);
        Assert.Equal(SC.ReservationStatuses.Reserved, res.Status); Assert.Equal(350m, res.Amount); Assert.Equal(s.Owner, res.ReservedByUserId);
        var rows = await ctx.RequestPayments.Where(p => p.RequestPoGroupId == s.GroupB).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, p => p.PaymentType == RequestPayment.PaymentTypes.FinalBalance && p.PlannedAmount == 150m && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned);
        Assert.Contains(rows, p => p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PlannedAmount == 350m && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned);
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == SC.HistoryActions.Reserved && h.IdempotencyKey == $"CRES:{res.Id:D}"));
        Assert.Equal((1000m, 350m, 650m), await Svc(ctx).AccountFiguresAsync(account));
    }

    [Fact]
    public async Task RegisterPo_with_full_credit_on_advance_full_creates_no_bank_row_and_waits_for_the_credit_application()
    {
        await using var ctx = S.NewCtx();
        var (s, file, _) = await SeedAsync(ctx);
        var result = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, file, 500m, RequestConstants.PaymentConditions.AdvanceFull));
        Assert.IsType<OkObjectResult>(result);
        ctx.ChangeTracker.Clear();
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, g.Status); Assert.Equal(500m, g.CreditCoverageAmount);
        var rows = await ctx.RequestPayments.Where(p => p.RequestPoGroupId == s.GroupB).ToListAsync();
        Assert.Single(rows); Assert.Equal(RequestPayment.PaymentTypes.SupplierCredit, rows[0].PaymentType); Assert.Equal(500m, rows[0].PlannedAmount);
    }

    [Fact]
    public async Task RegisterPo_credit_path_refuses_another_buyer_and_the_flag_off_without_writing()
    {
        await using var ctx = S.NewCtx();
        var (s, file, _) = await SeedAsync(ctx);

        var other = await Buyer(ctx, s.OtherBuyer).RegisterPo(s.RequestId, Dto(s.GroupB, file, 350m));
        Assert.Equal(403, ((ObjectResult)other).StatusCode); Assert.Equal(SC.ErrorCodes.NotResponsibleBuyer, Problem(other).Extensions["code"]);

        var off = await Buyer(ctx, s.Owner, enabled: false).RegisterPo(s.RequestId, Dto(s.GroupB, file, 350m));
        Assert.Equal(400, ((ObjectResult)off).StatusCode); Assert.Equal(SC.ErrorCodes.Disabled, Problem(off).Extensions["code"]);

        ctx.ChangeTracker.Clear();
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB);
        Assert.Equal("WAITING_PO", g.Status); Assert.Equal(0m, g.CreditCoverageAmount); Assert.Null(g.PaymentConditionCode);
        Assert.Equal(0, await ctx.SupplierCreditReservations.CountAsync()); Assert.Equal(0, await ctx.RequestPayments.CountAsync());
        var groupBRef = s.GroupB.ToString().Substring(0, 8);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == SC.HistoryActions.Reserved));
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO" && h.Comment != null && h.Comment.Contains(groupBRef)));

        // Without credit the standard registration is untouched (role-only check): the other Buyer still registers.
        var standard = await Buyer(ctx, s.OtherBuyer, enabled: false).RegisterPo(s.RequestId, Dto(s.GroupB, file, null));
        Assert.IsType<OkObjectResult>(standard);
    }

    [Fact]
    public async Task ApplyCredit_settles_a_full_coverage_once_and_bank_endpoints_refuse_it()
    {
        await using var ctx = S.NewCtx();
        var (s, file, account) = await SeedAsync(ctx);
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, file, 500m)));
        ctx.ChangeTracker.Clear();

        // Bank endpoints: schedule and pay refuse the full coverage with CREDIT_APPLICATION_REQUIRED.
        var schedule = await Finance(ctx, s.Finance1).SchedulePayment(s.RequestId, new SchedulePaymentDto { RequestPoGroupId = s.GroupB, ScheduledDate = DateTime.UtcNow.AddDays(3) });
        Assert.Equal(409, ((ObjectResult)schedule).StatusCode); Assert.Equal(SC.ErrorCodes.ApplicationRequired, Problem(schedule).Extensions["code"]);
        ctx.ChangeTracker.Clear();

        var apply = await Finance(ctx, s.Finance1).ApplyCredit(s.RequestId, new ApplySupplierCreditDto { RequestPoGroupId = s.GroupB, Note = "Aplicação de crédito integral" });
        var ok = Assert.IsType<OkObjectResult>(apply);
        var dto = Assert.IsType<SupplierCreditOperationResultDto>(ok.Value);
        Assert.Equal(500m, dto.AppliedAmount); Assert.Equal(SC.ReservationStatuses.Applied, dto.ReservationStatus); Assert.Equal(RequestConstants.Statuses.PaymentCompleted, dto.GroupStatus);
        ctx.ChangeTracker.Clear();

        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB);
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, g.Status);
        Assert.Null((await ctx.Requests.SingleAsync(r => r.Id == s.RequestId)).ActualPaidAmount); // cash figure untouched by credit
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == SC.HistoryActions.Applied));
        Assert.Equal((500m, 0m, 500m), await Svc(ctx).AccountFiguresAsync(account));

        var again = await Finance(ctx, s.Finance1).ApplyCredit(s.RequestId, new ApplySupplierCreditDto { RequestPoGroupId = s.GroupB });
        Assert.Equal(400, ((ObjectResult)again).StatusCode); // group no longer in an applicable status
        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync(e => e.EntryType == SC.EntryTypes.Application));
    }

    [Fact]
    public async Task MarkAsPaid_on_partial_coverage_enforces_the_exact_bank_part_and_applies_the_credit_atomically()
    {
        await using var ctx = S.NewCtx();
        var (s, file, account) = await SeedAsync(ctx);
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, file, 350m)));
        var proof = Guid.NewGuid();
        ctx.RequestAttachments.Add(new RequestAttachment { Id = proof, RequestId = s.RequestId, RequestPoGroupId = s.GroupB, AttachmentTypeCode = AttachmentConstants.Types.PaymentProof, FileName = "proof.pdf", FileExtension = ".pdf", StorageReference = "proof.pdf", UploadedByUserId = s.Finance1, UploadedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        // apply-credit alone is refused while a bank part is pending.
        var alone = await Finance(ctx, s.Finance1).ApplyCredit(s.RequestId, new ApplySupplierCreditDto { RequestPoGroupId = s.GroupB });
        Assert.Equal(409, ((ObjectResult)alone).StatusCode); Assert.Equal(SC.ErrorCodes.AppliesWithBankPayment, Problem(alone).Extensions["code"]);
        ctx.ChangeTracker.Clear();

        // The group total (500) is refused: the bank part is exactly 150.
        var wrong = await Finance(ctx, s.Finance1).MarkAsPaid(s.RequestId, new ConfirmPaymentDto { RequestPoGroupId = s.GroupB, ActualPaidAmount = 500m, PaidDate = DateTime.UtcNow, PaymentProofAttachmentId = proof });
        Assert.Equal(400, ((ObjectResult)wrong).StatusCode); Assert.Equal(SC.ErrorCodes.PaymentAmountMismatch, Problem(wrong).Extensions["code"]);
        ctx.ChangeTracker.Clear();
        Assert.Equal(RequestConstants.Statuses.PoIssued, (await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB)).Status);

        var right = await Finance(ctx, s.Finance1).MarkAsPaid(s.RequestId, new ConfirmPaymentDto { RequestPoGroupId = s.GroupB, ActualPaidAmount = 150m, PaidDate = DateTime.UtcNow, PaymentProofAttachmentId = proof });
        Assert.IsType<OkResult>(right);
        ctx.ChangeTracker.Clear();

        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB);
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, g.Status);
        var rows = await ctx.RequestPayments.Where(p => p.RequestPoGroupId == s.GroupB).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, p => Assert.Equal(RequestPayment.PaymentStatuses.Completed, p.PaymentStatus));
        Assert.Equal(150m, rows.Single(p => p.PaymentType == RequestPayment.PaymentTypes.FinalBalance).ActualPaidAmount);
        Assert.Equal(350m, rows.Single(p => p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit).ActualPaidAmount);
        Assert.Equal(150m, (await ctx.Requests.SingleAsync(r => r.Id == s.RequestId)).ActualPaidAmount); // cash only
        Assert.Equal(SC.ReservationStatuses.Applied, (await ctx.SupplierCreditReservations.SingleAsync()).Status);
        Assert.Equal((650m, 0m, 650m), await Svc(ctx).AccountFiguresAsync(account));
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "PAYMENT_DIVERGENCE_DETECTED")); // 150 == bank part: no divergence
    }

    [Fact]
    public async Task ReverseCredit_needs_a_different_finance_user_and_reopens_the_bank_obligation()
    {
        await using var ctx = S.NewCtx();
        var (s, file, account) = await SeedAsync(ctx);
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, file, 500m)));
        ctx.ChangeTracker.Clear();
        Assert.IsType<OkObjectResult>(await Finance(ctx, s.Finance1).ApplyCredit(s.RequestId, new ApplySupplierCreditDto { RequestPoGroupId = s.GroupB }));
        ctx.ChangeTracker.Clear();

        var self = await Finance(ctx, s.Finance1).ReverseCredit(s.RequestId, new ReverseSupplierCreditDto { RequestPoGroupId = s.GroupB, Reason = "Reversão pelo próprio utilizador que aplicou." });
        Assert.Equal(403, ((ObjectResult)self).StatusCode); Assert.Equal(SC.ErrorCodes.SelfReversalForbidden, Problem(self).Extensions["code"]);

        var adminOnly = await Finance(ctx, s.Admin, RoleConstants.SystemAdministrator).ReverseCredit(s.RequestId, new ReverseSupplierCreditDto { RequestPoGroupId = s.GroupB, Reason = "Administrador sem perfil Finanças na base de dados." });
        Assert.Equal(403, ((ObjectResult)adminOnly).StatusCode); Assert.Equal(SC.ErrorCodes.FinanceScopeRequired, Problem(adminOnly).Extensions["code"]);

        var shortReason = await Finance(ctx, s.Finance2).ReverseCredit(s.RequestId, new ReverseSupplierCreditDto { RequestPoGroupId = s.GroupB, Reason = "curto" });
        Assert.Equal(400, ((ObjectResult)shortReason).StatusCode);
        ctx.ChangeTracker.Clear();

        var ok = await Finance(ctx, s.Finance2).ReverseCredit(s.RequestId, new ReverseSupplierCreditDto { RequestPoGroupId = s.GroupB, Reason = "Crédito aplicado ao grupo errado; reabrir pagamento bancário." });
        var dto = Assert.IsType<SupplierCreditOperationResultDto>(Assert.IsType<OkObjectResult>(ok).Value);
        Assert.Equal(SC.ReservationStatuses.Reversed, dto.ReservationStatus); Assert.Equal(RequestConstants.Statuses.PoIssued, dto.GroupStatus); Assert.Single(dto.ReopenedPaymentIds);
        ctx.ChangeTracker.Clear();
        Assert.Equal((1000m, 0m, 1000m), await Svc(ctx).AccountFiguresAsync(account));
        var reopened = await ctx.RequestPayments.SingleAsync(p => p.RequestPoGroupId == s.GroupB && p.PaymentType == RequestPayment.PaymentTypes.FinalBalance);
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, reopened.PaymentStatus); Assert.Equal(500m, reopened.PlannedAmount);
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == SC.HistoryActions.Reversed));

        // The reopened obligation is bank-only and exact: pay 500 completes it.
        var proof = Guid.NewGuid();
        ctx.RequestAttachments.Add(new RequestAttachment { Id = proof, RequestId = s.RequestId, RequestPoGroupId = s.GroupB, AttachmentTypeCode = AttachmentConstants.Types.PaymentProof, FileName = "proof.pdf", FileExtension = ".pdf", StorageReference = "proof.pdf", UploadedByUserId = s.Finance1, UploadedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var wrong = await Finance(ctx, s.Finance1).MarkAsPaid(s.RequestId, new ConfirmPaymentDto { RequestPoGroupId = s.GroupB, ActualPaidAmount = 499.99m, PaidDate = DateTime.UtcNow, PaymentProofAttachmentId = proof });
        Assert.Equal(400, ((ObjectResult)wrong).StatusCode);
        ctx.ChangeTracker.Clear();
        Assert.IsType<OkResult>(await Finance(ctx, s.Finance1).MarkAsPaid(s.RequestId, new ConfirmPaymentDto { RequestPoGroupId = s.GroupB, ActualPaidAmount = 500m, PaidDate = DateTime.UtcNow, PaymentProofAttachmentId = proof }));
        ctx.ChangeTracker.Clear();
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, (await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB)).Status);
    }

    [Fact]
    public async Task Cancelling_a_reserved_group_releases_the_hold()
    {
        await using var ctx = S.NewCtx();
        var (s, file, account) = await SeedAsync(ctx);
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, file, 350m)));
        ctx.ChangeTracker.Clear();

        var (orch, _) = S.OrchestratorMock();
        var service = new PoGroupCancellationService(ctx, new AlplaPortal.Infrastructure.Services.Approvals.RequestStatusSyncService(ctx, NullLogger<AlplaPortal.Infrastructure.Services.Approvals.RequestStatusSyncService>.Instance),
            orch.Object, S.AdminLog().Object, NullLogger<PoGroupCancellationService>.Instance, Svc(ctx));
        var result = await service.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), groupId: s.GroupB, ecfReference: "ECF10 2026/9630"));
        Assert.True(result.Succeeded, result.Detail);
        ctx.ChangeTracker.Clear();

        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB);
        Assert.Equal(RequestConstants.PoGroupStatuses.Cancelled, g.Status); Assert.Equal(0m, g.CreditCoverageAmount);
        var res = await ctx.SupplierCreditReservations.SingleAsync();
        Assert.Equal(SC.ReservationStatuses.Released, res.Status); Assert.Equal(SC.ReleaseReasons.GroupCancelled, res.ReleaseReason); Assert.Equal(350m, res.ReleasedHoldAmount);
        Assert.Equal((1000m, 0m, 1000m), await Svc(ctx).AccountFiguresAsync(account));
        Assert.True(await ctx.RequestPayments.Where(p => p.RequestPoGroupId == s.GroupB).AllAsync(p => p.PaymentStatus == RequestPayment.PaymentStatuses.Cancelled));
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == SC.HistoryActions.Released));
    }

    [Fact]
    public async Task Legacy_scalar_settlement_and_reconciliation_are_refused_for_credit_touched_requests()
    {
        await using var ctx = S.NewCtx();
        var (s, file, _) = await SeedAsync(ctx);
        Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s.GroupB, file, 350m)));
        ctx.ChangeTracker.Clear();
        var legacy = await Buyer(ctx, s.Owner).CompletePayment(s.RequestId, new ApprovalActionDto { Comment = "legacy" });
        Assert.Equal(409, ((ObjectResult)legacy).StatusCode); Assert.Equal(SC.ErrorCodes.LegacySettlementBlocked, Problem(legacy).Extensions["code"]);
    }
}
