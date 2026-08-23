using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Direct-pay ledger completeness (FinanceController.MarkAsPaid). Paying a group from PO_ISSUED with
/// no pre-existing FINAL_BALANCE now CREATES a COMPLETED RequestPayment (request-scoped sequence,
/// actor, group, amount, date, proof) instead of leaving the ledger empty — so reconciliation's
/// actualPaidSum and the obligations projection see the paid group. Existing scheduled rows are still
/// completed in place (no duplicate); CANCELLED/COMPLETED rows are never revived or reused.
/// InMemory EF (no unique-index enforcement) — assertions are on the ledger rows the code produces.
/// </summary>
public class FinanceDirectPayLedgerTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()).Options);

    private static FinanceController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new FinanceController(
            ctx,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            NullLogger<FinanceController>.Instance,
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance),
            new FinancePaymentEligibilityService());
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.Finance)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
        return controller;
    }

    private static void SeedStatuses(ApplicationDbContext ctx)
    {
        var codes = new[]
        {
            RequestConstants.Statuses.FinalApproved, RequestConstants.Statuses.PoIssued,
            RequestConstants.Statuses.PoPartiallyUploaded, RequestConstants.Statuses.PaymentRequestSent,
            RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.Paid, RequestConstants.Statuses.WaitingReceipt,
            RequestConstants.Statuses.InFollowup, RequestConstants.Statuses.Completed,
            RequestConstants.Statuses.WaitingPoCorrection, RequestConstants.Statuses.WaitingReconciliation,
            RequestConstants.Statuses.Cancelled
        }.Distinct().ToArray();
        int id = 1;
        foreach (var code in codes)
            ctx.RequestStatuses.Add(new RequestStatus { Id = id++, Code = code, Name = code, DisplayOrder = id });
    }

    private static int StatusId(ApplicationDbContext ctx, string code) => ctx.RequestStatuses.Single(s => s.Code == code).Id;

    private static async Task<(Guid reqId, List<Guid> groupIds, Guid actorId)> SeedQuotationAsync(
        ApplicationDbContext ctx, params (string status, decimal amount)[] groups)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = $"fin-{Guid.NewGuid()}@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        SeedStatuses(ctx);
        await ctx.SaveChangesAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = $"ZZTEST-DP-{Guid.NewGuid().ToString()[..8]}",
            Title = "ZZTEST Direct Pay Ledger",
            RequestTypeId = 2,
            StatusId = StatusId(ctx, RequestConstants.Statuses.PoIssued),
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        var ids = new List<Guid>();
        foreach (var (status, amount) in groups)
        {
            var g = new RequestPoGroup
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                SupplierNameSnapshot = $"ZZTEST Supplier {ids.Count + 1}",
                CurrencyCode = "AOA",
                TotalAmount = amount,
                Status = status,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
                CreatedByUserId = actor.Id
            };
            ctx.RequestPoGroups.Add(g);
            ids.Add(g.Id);
        }
        await ctx.SaveChangesAsync();
        return (request.Id, ids, actor.Id);
    }

    private static void AddPayment(ApplicationDbContext ctx, Guid reqId, Guid groupId, string status, int seq, decimal amount, decimal? actualPaid = null)
    {
        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = reqId,
            RequestPoGroupId = groupId,
            PaymentType = RequestPayment.PaymentTypes.FinalBalance,
            PaymentStatus = status,
            PaymentSequence = seq,
            PlannedAmount = amount,
            ActualPaidAmount = actualPaid,
            CurrencyCode = "AOA",
            CreatedByUserId = ctx.Users.First().Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        ctx.SaveChanges();
    }

    private static Guid AddProof(ApplicationDbContext ctx, Guid reqId, Guid actorId)
    {
        var att = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = reqId,
            AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_PROOF,
            FileName = "proof.pdf",
            FileExtension = "pdf",
            FileSizeMBytes = 0.01m,
            StorageReference = "x/proof.pdf",
            UploadedByUserId = actorId,
            UploadedAtUtc = DateTime.UtcNow,
            IsDeleted = false
        };
        ctx.RequestAttachments.Add(att);
        ctx.SaveChanges();
        return att.Id;
    }

    private static List<RequestPayment> FinalBalancesFor(ApplicationDbContext ctx, Guid groupId) =>
        ctx.RequestPayments.AsNoTracking()
            .Where(p => p.RequestPoGroupId == groupId && p.PaymentType == RequestPayment.PaymentTypes.FinalBalance)
            .OrderBy(p => p.PaymentSequence).ToList();

    // 1/4/8/9/10. No existing payment → direct pay creates exactly one COMPLETED FINAL_BALANCE with actor/group/amount/date.
    [Fact]
    public async Task DirectPay_NoExistingPayment_CreatesOneCompletedRow()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx, (RequestConstants.PoGroupStatuses.PoIssued, 123456m));
        var proof = AddProof(ctx, reqId, actorId);
        var paidDate = DateTime.UtcNow.Date;

        var result = await BuildController(ctx, actorId).MarkAsPaid(reqId, new ConfirmPaymentDto
        {
            RequestPoGroupId = ids[0], PaymentProofAttachmentId = proof, ActualPaidAmount = 123456m, PaidDate = paidDate
        });
        Assert.IsType<OkResult>(result);

        var rows = FinalBalancesFor(ctx, ids[0]);
        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, row.PaymentStatus);
        Assert.Equal(1, row.PaymentSequence);
        Assert.Equal(123456m, row.ActualPaidAmount);
        Assert.Equal(paidDate, row.PaidDateUtc);
        Assert.Equal(actorId, row.CreatedByUserId);
        Assert.Equal(ids[0], row.RequestPoGroupId);
        Assert.Equal(proof, row.PaymentProofAttachmentId);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == ids[0]);
        Assert.Equal(RequestConstants.PoGroupStatuses.PaymentCompleted, group.Status);
    }

    // 12. Proof stays linked to the group both on the payment and the attachment.
    [Fact]
    public async Task DirectPay_ProofRemainsLinkedToGroup()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx, (RequestConstants.PoGroupStatuses.PoIssued, 5000m));
        var proof = AddProof(ctx, reqId, actorId);

        Assert.IsType<OkResult>(await BuildController(ctx, actorId).MarkAsPaid(reqId, new ConfirmPaymentDto
        { RequestPoGroupId = ids[0], PaymentProofAttachmentId = proof, ActualPaidAmount = 5000m, PaidDate = DateTime.UtcNow.Date }));

        var att = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == proof);
        Assert.Equal(ids[0], att.RequestPoGroupId);
        var row = FinalBalancesFor(ctx, ids[0]).Single();
        Assert.Equal(proof, row.PaymentProofAttachmentId);
    }

    // 2/5. Existing SCHEDULED FINAL_BALANCE → completed in place, no duplicate row.
    [Fact]
    public async Task Pay_ExistingScheduled_UpdatesInPlace_NoDuplicate()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx, (RequestConstants.PoGroupStatuses.PaymentScheduled, 80000m));
        AddPayment(ctx, reqId, ids[0], RequestPayment.PaymentStatuses.Scheduled, 1, 80000m);
        var proof = AddProof(ctx, reqId, actorId);

        Assert.IsType<OkResult>(await BuildController(ctx, actorId).MarkAsPaid(reqId, new ConfirmPaymentDto
        { RequestPoGroupId = ids[0], PaymentProofAttachmentId = proof, ActualPaidAmount = 80000m, PaidDate = DateTime.UtcNow.Date }));

        var rows = FinalBalancesFor(ctx, ids[0]);
        Assert.Single(rows); // updated, not duplicated
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, rows[0].PaymentStatus);
        Assert.Equal(1, rows[0].PaymentSequence);
    }

    // 6/G. Multi-group: sibling FINAL_BALANCE seq1 completed → direct-pay 2nd group creates seq2, sibling untouched.
    [Fact]
    public async Task DirectPay_MultiGroup_SiblingSeq1_NewGroupGetsSeq2_SiblingUntouched()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx,
            (RequestConstants.PoGroupStatuses.PaymentCompleted, 300000m),
            (RequestConstants.PoGroupStatuses.PoIssued, 175000m));
        AddPayment(ctx, reqId, ids[0], RequestPayment.PaymentStatuses.Completed, 1, 300000m, 300000m);
        var proof = AddProof(ctx, reqId, actorId);

        Assert.IsType<OkResult>(await BuildController(ctx, actorId).MarkAsPaid(reqId, new ConfirmPaymentDto
        { RequestPoGroupId = ids[1], PaymentProofAttachmentId = proof, ActualPaidAmount = 175000m, PaidDate = DateTime.UtcNow.Date }));

        var newRow = FinalBalancesFor(ctx, ids[1]).Single();
        Assert.Equal(2, newRow.PaymentSequence);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, newRow.PaymentStatus);

        var sibling = FinalBalancesFor(ctx, ids[0]).Single();
        Assert.Equal(1, sibling.PaymentSequence);
        var siblingGroup = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == ids[0]);
        Assert.Equal(RequestConstants.PoGroupStatuses.PaymentCompleted, siblingGroup.Status);
    }

    // 7/E5. A CANCELLED FINAL_BALANCE seq1 on the target group is never revived → new row seq2.
    [Fact]
    public async Task DirectPay_CancelledSeq1_NotReused_CreatesSeq2()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx, (RequestConstants.PoGroupStatuses.PoIssued, 90000m));
        AddPayment(ctx, reqId, ids[0], RequestPayment.PaymentStatuses.Cancelled, 1, 90000m);
        var proof = AddProof(ctx, reqId, actorId);

        Assert.IsType<OkResult>(await BuildController(ctx, actorId).MarkAsPaid(reqId, new ConfirmPaymentDto
        { RequestPoGroupId = ids[0], PaymentProofAttachmentId = proof, ActualPaidAmount = 90000m, PaidDate = DateTime.UtcNow.Date }));

        var rows = FinalBalancesFor(ctx, ids[0]);
        Assert.Equal(2, rows.Count);
        var cancelled = rows.Single(r => r.PaymentStatus == RequestPayment.PaymentStatuses.Cancelled);
        Assert.Equal(1, cancelled.PaymentSequence); // preserved, not revived
        var completed = rows.Single(r => r.PaymentStatus == RequestPayment.PaymentStatuses.Completed);
        Assert.Equal(2, completed.PaymentSequence);
    }

    // 11/F. Reconciliation's actualPaidSum (Completed payments) now includes the direct-pay row.
    [Fact]
    public async Task DirectPay_IsIncludedInReconciliationActualPaidSum()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx, (RequestConstants.PoGroupStatuses.PoIssued, 40000m));
        var proof = AddProof(ctx, reqId, actorId);

        Assert.IsType<OkResult>(await BuildController(ctx, actorId).MarkAsPaid(reqId, new ConfirmPaymentDto
        { RequestPoGroupId = ids[0], PaymentProofAttachmentId = proof, ActualPaidAmount = 40000m, PaidDate = DateTime.UtcNow.Date }));

        // Exact expression ReconcileRequest uses: sum of Completed payments' ActualPaidAmount.
        var actualPaidSum = ctx.RequestPayments.AsNoTracking()
            .Where(p => p.RequestId == reqId && p.PaymentStatus == RequestPayment.PaymentStatuses.Completed)
            .Sum(p => p.ActualPaidAmount ?? 0);
        Assert.Equal(40000m, actualPaidSum); // previously 0 (no ledger row) — undercount eliminated
    }

    // 3/13. Re-pay guard: an already-PAYMENT_COMPLETED group is rejected and no extra row is created.
    [Fact]
    public async Task Pay_AlreadyCompletedGroup_Rejected_NoNewRow()
    {
        var ctx = NewContext();
        var (reqId, ids, actorId) = await SeedQuotationAsync(ctx, (RequestConstants.PoGroupStatuses.PaymentCompleted, 10000m));
        AddPayment(ctx, reqId, ids[0], RequestPayment.PaymentStatuses.Completed, 1, 10000m, 10000m);
        var proof = AddProof(ctx, reqId, actorId);

        var result = await BuildController(ctx, actorId).MarkAsPaid(reqId, new ConfirmPaymentDto
        { RequestPoGroupId = ids[0], PaymentProofAttachmentId = proof, ActualPaidAmount = 10000m, PaidDate = DateTime.UtcNow.Date });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Single(FinalBalancesFor(ctx, ids[0])); // no new row
    }
}
