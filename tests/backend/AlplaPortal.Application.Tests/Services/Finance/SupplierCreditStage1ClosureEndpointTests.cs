using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.DTOs.SupplierCredit;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Suppliers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using E = AlplaPortal.Application.Tests.Services.Finance.SupplierCreditWorkflowEndpointTests;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;
using SC = AlplaPortal.Domain.Constants.SupplierCreditConstants;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Stage 1 closure (DEV round 2, 2026-11-12) — the checks of the validation checklist that had no automated evidence yet,
/// on the real endpoints (InMemory; the SQL Server lock / index behaviour is in <c>SupplierCreditRelationalTests</c>):
///  • bank payment blocked on a fully covered group: finance/pay 409, b2p/schedule-advance 409, b2p/confirm-advance 409;
///  • reversal refused at the endpoint for a settlement that combined partial credit and a bank payment (stage 1 boundary);
///  • NewReservationsEnabled=false: KEEP / REMOVE / release / cancellation / settlement of an EXISTING hold still work,
///    CHANGE is refused (400) and nothing is written;
///  • the release endpoint: parked group only (409 otherwise), responsible Buyer (403 for another Buyer), reason length;
///  • credit account management: Contracts creates accounts and entries, Finance and the request-scoped Buyer read only (403).
/// Account: supplier B × company 1 × AOA with 1 000; group B = 500 AOA (shared helpers of SupplierCreditWorkflowEndpointTests).
/// </summary>
public class SupplierCreditStage1ClosureEndpointTests
{
    private static async Task<Guid> ProofAsync(ApplicationDbContext ctx, S.Seed s)
    {
        var proof = Guid.NewGuid();
        ctx.RequestAttachments.Add(new RequestAttachment { Id = proof, RequestId = s.RequestId, RequestPoGroupId = s.GroupB, AttachmentTypeCode = AttachmentConstants.Types.PaymentProof, FileName = "proof.pdf", FileExtension = ".pdf", StorageReference = "proof.pdf", UploadedByUserId = s.Finance1, UploadedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return proof;
    }

    /// <summary>A Finance return of the group (owed rows cancelled, group parked in WAITING_PO_CORRECTION) — the state the correction wizard starts from.</summary>
    private static async Task SimulateFinanceReturnAsync(ApplicationDbContext ctx, Guid groupId)
    {
        foreach (var p in await ctx.RequestPayments.Where(p => p.RequestPoGroupId == groupId && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned).ToListAsync())
            p.PaymentStatus = RequestPayment.PaymentStatuses.Cancelled;
        (await ctx.RequestPoGroups.SingleAsync(g => g.Id == groupId)).Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
    }

    private static async Task<SupplierCreditReservation> SingleReservationAsync(ApplicationDbContext ctx, Guid groupId)
        => await ctx.SupplierCreditReservations.AsNoTracking().SingleAsync(r => r.RequestPoGroupId == groupId);

    // ═════════════════════════════════════════ bank payment blocked on full coverage ═════════════════════════════════════════

    [Fact]
    public async Task Bank_endpoints_refuse_a_fully_covered_group_pay_schedule_advance_and_confirm_advance()
    {
        // Pós-Pago, full coverage: finance/{id}/pay is refused (schedule is covered by the sibling suite); nothing settles.
        await using (var ctx = S.NewCtx())
        {
            var (s, file, account) = await E.SeedAsync(ctx);
            Assert.IsType<OkObjectResult>(await E.Buyer(ctx, s.Owner).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, 500m)));
            ctx.ChangeTracker.Clear();
            var proof = await ProofAsync(ctx, s);

            var pay = await E.Finance(ctx, s.Finance1).MarkAsPaid(s.RequestId, new ConfirmPaymentDto { RequestPoGroupId = s.GroupB, ActualPaidAmount = 500m, PaidDate = DateTime.UtcNow, PaymentProofAttachmentId = proof });
            Assert.Equal(409, ((ObjectResult)pay).StatusCode); Assert.Equal(SC.ErrorCodes.ApplicationRequired, E.Problem(pay).Extensions["code"]);
            ctx.ChangeTracker.Clear();

            Assert.Equal(RequestConstants.Statuses.PoIssued, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).Status);
            Assert.Equal(0, await ctx.SupplierCreditEntries.CountAsync(e => e.EntryType == SC.EntryTypes.Application));
            Assert.Equal(0, await ctx.RequestPayments.CountAsync(p => p.RequestPoGroupId == s.GroupB && p.PaymentStatus == RequestPayment.PaymentStatuses.Completed));
            Assert.Equal((1000m, 500m, 500m), await E.Svc(ctx).AccountFiguresAsync(account));
        }

        // 100 % Antecipado, full coverage: b2p/schedule-advance and b2p/confirm-advance are refused; apply-credit settles the
        // advance (→ WAITING_SUPPLIER_DELIVERY) and a later confirm-advance finds no advance to confirm.
        await using (var ctx = S.NewCtx())
        {
            var (s, file, account) = await E.SeedAsync(ctx);
            Assert.IsType<OkObjectResult>(await E.Buyer(ctx, s.Owner).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, 500m, RequestConstants.PaymentConditions.AdvanceFull)));
            ctx.ChangeTracker.Clear();
            Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).Status);

            var finance = E.Buyer(ctx, s.Finance1, role: RoleConstants.Finance);
            var schedule = await finance.ScheduleAdvancePayment(s.RequestId, new ScheduleAdvancePaymentDto { RequestPoGroupId = s.GroupB, ScheduledDate = DateTime.UtcNow.AddDays(2) });
            Assert.Equal(409, ((ObjectResult)schedule).StatusCode); Assert.Equal(SC.ErrorCodes.ApplicationRequired, E.Problem(schedule).Extensions["code"]);
            ctx.ChangeTracker.Clear();

            var proof = await ProofAsync(ctx, s);
            var confirm = await finance.ConfirmAdvancePayment(s.RequestId, new ConfirmAdvancePaymentDto { RequestPoGroupId = s.GroupB, ActualPaidAmount = 500m, PaidDate = DateTime.UtcNow, PaymentProofAttachmentId = proof });
            Assert.Equal(409, ((ObjectResult)confirm).StatusCode); Assert.Equal(SC.ErrorCodes.ApplicationRequired, E.Problem(confirm).Extensions["code"]);
            ctx.ChangeTracker.Clear();
            Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).Status);
            Assert.Equal(0, await ctx.SupplierCreditEntries.CountAsync(e => e.EntryType == SC.EntryTypes.Application));

            var apply = await E.Finance(ctx, s.Finance1).ApplyCredit(s.RequestId, new ApplySupplierCreditDto { RequestPoGroupId = s.GroupB });
            var dto = Assert.IsType<SupplierCreditOperationResultDto>(Assert.IsType<OkObjectResult>(apply).Value);
            Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, dto.GroupStatus); Assert.Equal(500m, dto.AppliedAmount);
            ctx.ChangeTracker.Clear();

            var late = await finance.ConfirmAdvancePayment(s.RequestId, new ConfirmAdvancePaymentDto { RequestPoGroupId = s.GroupB, ActualPaidAmount = 500m, PaidDate = DateTime.UtcNow, PaymentProofAttachmentId = proof });
            Assert.Equal(400, ((ObjectResult)late).StatusCode);
            ctx.ChangeTracker.Clear();
            Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync(e => e.EntryType == SC.EntryTypes.Application));
            Assert.Equal((500m, 0m, 500m), await E.Svc(ctx).AccountFiguresAsync(account));
        }
    }

    // ═════════════════════════════════════════ reversal boundary at the endpoint ═════════════════════════════════════════

    [Fact]
    public async Task ReverseCredit_endpoint_refuses_a_settlement_that_combined_partial_credit_and_a_bank_payment()
    {
        await using var ctx = S.NewCtx();
        var (s, file, account) = await E.SeedAsync(ctx);
        Assert.IsType<OkObjectResult>(await E.Buyer(ctx, s.Owner).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, 350m)));
        ctx.ChangeTracker.Clear();
        var proof = await ProofAsync(ctx, s);
        Assert.IsType<OkResult>(await E.Finance(ctx, s.Finance1).MarkAsPaid(s.RequestId, new ConfirmPaymentDto { RequestPoGroupId = s.GroupB, ActualPaidAmount = 150m, PaidDate = DateTime.UtcNow, PaymentProofAttachmentId = proof }));
        ctx.ChangeTracker.Clear();
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).Status);

        var rev = await E.Finance(ctx, s.Finance2).ReverseCredit(s.RequestId, new ReverseSupplierCreditDto { RequestPoGroupId = s.GroupB, Reason = "Tentativa de reversão de uma cobertura parcial liquidada com banco." });
        Assert.Equal(409, ((ObjectResult)rev).StatusCode); Assert.Equal(SC.ErrorCodes.ReversalPartialUnsupported, E.Problem(rev).Extensions["code"]);
        ctx.ChangeTracker.Clear();

        var res = await SingleReservationAsync(ctx, s.GroupB);
        Assert.Equal(SC.ReservationStatuses.Applied, res.Status); Assert.Equal(350m, res.AppliedAmount); Assert.Null(res.ReversedAtUtc);
        Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync(e => e.EntryType == SC.EntryTypes.Application));
        Assert.Equal(0, await ctx.SupplierCreditEntries.CountAsync(e => e.EntryType == SC.EntryTypes.Reversal));
        Assert.Equal((650m, 0m, 650m), await E.Svc(ctx).AccountFiguresAsync(account));
        var rows = await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestPoGroupId == s.GroupB).ToListAsync();
        Assert.Equal(2, rows.Count); Assert.All(rows, p => Assert.Equal(RequestPayment.PaymentStatuses.Completed, p.PaymentStatus)); // nothing reopened
        Assert.Equal(RequestConstants.Statuses.PaymentCompleted, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).Status);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == SC.HistoryActions.Reversed));
    }

    // ═════════════════════════════════════════ NewReservationsEnabled = false ═════════════════════════════════════════

    [Fact]
    public async Task Flag_off_keeps_removes_releases_and_settles_existing_holds_but_refuses_a_changed_reservation()
    {
        // Correction wizard on an existing hold: KEEP → unchanged; CHANGE → 400 and nothing written; REMOVE → released.
        await using (var ctx = S.NewCtx())
        {
            var (s, file, account) = await E.SeedAsync(ctx);
            Assert.IsType<OkObjectResult>(await E.Buyer(ctx, s.Owner).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, 350m)));
            ctx.ChangeTracker.Clear();
            await SimulateFinanceReturnAsync(ctx, s.GroupB);

            var keep = await E.Buyer(ctx, s.Owner, enabled: false).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, null, correction: SC.CorrectionActions.Keep));
            Assert.IsType<OkObjectResult>(keep);
            ctx.ChangeTracker.Clear();
            Assert.Equal(1, await ctx.SupplierCreditReservations.CountAsync(r => r.RequestPoGroupId == s.GroupB));
            var kept = await SingleReservationAsync(ctx, s.GroupB);
            Assert.Equal(SC.ReservationStatuses.Reserved, kept.Status); Assert.Equal(350m, kept.Amount);
            var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupB);
            Assert.Equal(RequestConstants.Statuses.PoIssued, g.Status); Assert.Equal(350m, g.CreditCoverageAmount); Assert.Equal(kept.Id, g.SupplierCreditReservationId);
            Assert.Equal((1000m, 350m, 650m), await E.Svc(ctx).AccountFiguresAsync(account));
            var planned = await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestPoGroupId == s.GroupB && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned).ToListAsync();
            Assert.Equal(2, planned.Count); // recreated once: bank 150 + credit 350
            Assert.Contains(planned, p => p.PaymentType == RequestPayment.PaymentTypes.FinalBalance && p.PlannedAmount == 150m);
            Assert.Contains(planned, p => p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PlannedAmount == 350m);
            Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync()); // holds never write a ledger entry

            await SimulateFinanceReturnAsync(ctx, s.GroupB);
            var change = await E.Buyer(ctx, s.Owner, enabled: false).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, 200m, correction: SC.CorrectionActions.Change));
            Assert.Equal(400, ((ObjectResult)change).StatusCode); Assert.Equal(SC.ErrorCodes.Disabled, E.Problem(change).Extensions["code"]);
            ctx.ChangeTracker.Clear();
            Assert.Equal(1, await ctx.SupplierCreditReservations.CountAsync(r => r.RequestPoGroupId == s.GroupB));
            Assert.Equal(350m, (await SingleReservationAsync(ctx, s.GroupB)).Amount);
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupB)).Status);
            Assert.Equal(0, await ctx.RequestPayments.CountAsync(p => p.RequestPoGroupId == s.GroupB && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned));
            Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == SC.HistoryActions.ReservationChanged));

            var remove = await E.Buyer(ctx, s.Owner, enabled: false).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, null, correction: SC.CorrectionActions.Remove));
            Assert.IsType<OkObjectResult>(remove);
            ctx.ChangeTracker.Clear();
            var released = await SingleReservationAsync(ctx, s.GroupB);
            Assert.Equal(SC.ReservationStatuses.Released, released.Status); Assert.Equal(SC.ReleaseReasons.CorrectionRemoved, released.ReleaseReason); Assert.Equal(350m, released.ReleasedHoldAmount);
            var after = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupB);
            Assert.Equal(RequestConstants.Statuses.PoIssued, after.Status); Assert.Equal(0m, after.CreditCoverageAmount);
            Assert.Equal((1000m, 0m, 1000m), await E.Svc(ctx).AccountFiguresAsync(account));
            Assert.Equal(0, await ctx.RequestPayments.CountAsync(p => p.RequestPoGroupId == s.GroupB && p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned));
            Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == SC.HistoryActions.Released));
            Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync());
        }

        // Settlement of an existing hold with the flag off (the owner's scenario 8): apply-credit → 200, one ledger debit.
        await using (var ctx = S.NewCtx())
        {
            var (s, file, account) = await E.SeedAsync(ctx);
            Assert.IsType<OkObjectResult>(await E.Buyer(ctx, s.Owner).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, 500m)));
            ctx.ChangeTracker.Clear();
            var apply = await E.Finance(ctx, s.Finance1, false).ApplyCredit(s.RequestId, new ApplySupplierCreditDto { RequestPoGroupId = s.GroupB });
            Assert.IsType<OkObjectResult>(apply);
            ctx.ChangeTracker.Clear();
            Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync(e => e.EntryType == SC.EntryTypes.Application));
            Assert.Equal((500m, 0m, 500m), await E.Svc(ctx).AccountFiguresAsync(account));
            Assert.Equal(RequestConstants.Statuses.PaymentCompleted, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).Status);
        }

        // Cancellation of a group holding credit while the flag is off releases the hold (no ledger entry).
        await using (var ctx = S.NewCtx())
        {
            var (s, file, account) = await E.SeedAsync(ctx);
            Assert.IsType<OkObjectResult>(await E.Buyer(ctx, s.Owner).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, 350m)));
            ctx.ChangeTracker.Clear();
            var (orch, _) = S.OrchestratorMock();
            var service = new PoGroupCancellationService(ctx, new AlplaPortal.Infrastructure.Services.Approvals.RequestStatusSyncService(ctx, NullLogger<AlplaPortal.Infrastructure.Services.Approvals.RequestStatusSyncService>.Instance),
                orch.Object, S.AdminLog().Object, NullLogger<PoGroupCancellationService>.Instance, E.Svc(ctx, false));
            var result = await service.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), groupId: s.GroupB, ecfReference: "ECF10 2026/9630"));
            Assert.True(result.Succeeded, result.Detail);
            ctx.ChangeTracker.Clear();
            var res = await SingleReservationAsync(ctx, s.GroupB);
            Assert.Equal(SC.ReservationStatuses.Released, res.Status); Assert.Equal(SC.ReleaseReasons.GroupCancelled, res.ReleaseReason);
            Assert.Equal((1000m, 0m, 1000m), await E.Svc(ctx).AccountFiguresAsync(account));
            Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync());
        }
    }

    // ═════════════════════════════════════════ release endpoint ═════════════════════════════════════════

    [Fact]
    public async Task Release_endpoint_is_for_the_responsible_buyer_on_a_parked_group_only()
    {
        await using var ctx = S.NewCtx();
        var (s, file, account) = await E.SeedAsync(ctx);
        Assert.IsType<OkObjectResult>(await E.Buyer(ctx, s.Owner).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, 350m)));
        ctx.ChangeTracker.Clear();
        var dto = new ReleaseSupplierCreditReservationDto { Reason = "Pedido abandonado após devolução por Finanças (teste)." };

        // PO_ISSUED: not a parked group → 409, hold intact (flag off makes no difference to an existing hold).
        var issued = await E.Buyer(ctx, s.Owner, enabled: false).ReleaseSupplierCreditReservation(s.RequestId, s.GroupB, dto);
        Assert.Equal(409, ((ObjectResult)issued).StatusCode); Assert.Equal(SC.ErrorCodes.ReleaseNotAllowed, E.Problem(issued).Extensions["code"]);
        ctx.ChangeTracker.Clear();
        Assert.Equal(SC.ReservationStatuses.Reserved, (await SingleReservationAsync(ctx, s.GroupB)).Status);

        await SimulateFinanceReturnAsync(ctx, s.GroupB);

        var other = await E.Buyer(ctx, s.OtherBuyer, enabled: false).ReleaseSupplierCreditReservation(s.RequestId, s.GroupB, dto);
        Assert.Equal(403, ((ObjectResult)other).StatusCode); Assert.Equal(SC.ErrorCodes.NotResponsibleBuyer, E.Problem(other).Extensions["code"]);

        var shortReason = await E.Buyer(ctx, s.Owner, enabled: false).ReleaseSupplierCreditReservation(s.RequestId, s.GroupB, new ReleaseSupplierCreditReservationDto { Reason = "curto" });
        Assert.Equal(400, ((ObjectResult)shortReason).StatusCode);
        ctx.ChangeTracker.Clear();
        Assert.Equal(SC.ReservationStatuses.Reserved, (await SingleReservationAsync(ctx, s.GroupB)).Status);
        Assert.Equal((1000m, 350m, 650m), await E.Svc(ctx).AccountFiguresAsync(account));

        var ok = await E.Buyer(ctx, s.Owner, enabled: false).ReleaseSupplierCreditReservation(s.RequestId, s.GroupB, dto);
        var result = Assert.IsType<SupplierCreditOperationResultDto>(Assert.IsType<OkObjectResult>(ok).Value);
        Assert.Equal(SC.ReservationStatuses.Released, result.ReservationStatus); Assert.Equal(350m, result.ReleasedHoldAmount); Assert.Equal(1000m, result.AccountAvailable);
        ctx.ChangeTracker.Clear();
        var res = await SingleReservationAsync(ctx, s.GroupB);
        Assert.Equal(SC.ReservationStatuses.Released, res.Status); Assert.Equal(SC.ReleaseReasons.AbandonedRequest, res.ReleaseReason);
        Assert.Equal(0m, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).CreditCoverageAmount);
        Assert.Equal((1000m, 0m, 1000m), await E.Svc(ctx).AccountFiguresAsync(account));
        Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync());
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == SC.HistoryActions.Released));

        var again = await E.Buyer(ctx, s.Owner, enabled: false).ReleaseSupplierCreditReservation(s.RequestId, s.GroupB, dto);
        Assert.Equal(409, ((ObjectResult)again).StatusCode); Assert.Equal(SC.ErrorCodes.NothingToApply, E.Problem(again).Extensions["code"]);
    }

    // ═════════════════════════════════════════ rollback path: reverse, then cancel (plan rev. 3) ═════════════════════════════════════════

    /// <summary>
    /// TEST rollback procedure for the settled full-coverage fixtures (F2 Pós-Pago, F3 100 % Antecipado): a DIFFERENT Finance user
    /// reverses the applied credit, the group reopens into its terms-specific bank status with a PLANNED bank row and no COMPLETED
    /// row, and the standard cancellation then succeeds (the cancellation guard refuses only COMPLETED / SCHEDULED rows and later
    /// evidence). Ledger after: CREDIT_IN, APPLICATION, REVERSAL; figures back to the start; the reservation stays REVERSED.
    /// </summary>
    [Theory]
    [InlineData(RequestConstants.PaymentConditions.PostPaid, RequestConstants.Statuses.PoIssued, RequestPayment.PaymentTypes.FinalBalance)]
    [InlineData(RequestConstants.PaymentConditions.AdvanceFull, RequestConstants.Statuses.AdvancePaymentRequired, RequestPayment.PaymentTypes.Advance)]
    public async Task Reversed_full_coverage_groups_reopen_into_their_bank_status_and_can_then_be_cancelled(string terms, string reopenedStatus, string reopenedRowType)
    {
        await using var ctx = S.NewCtx();
        var (s, file, account) = await E.SeedAsync(ctx);
        Assert.IsType<OkObjectResult>(await E.Buyer(ctx, s.Owner).RegisterPo(s.RequestId, E.Dto(s.GroupB, file, 500m, terms)));
        ctx.ChangeTracker.Clear();
        var applied = Assert.IsType<SupplierCreditOperationResultDto>(Assert.IsType<OkObjectResult>(await E.Finance(ctx, s.Finance1).ApplyCredit(s.RequestId, new ApplySupplierCreditDto { RequestPoGroupId = s.GroupB })).Value);
        Assert.Equal(terms == RequestConstants.PaymentConditions.AdvanceFull ? RequestConstants.Statuses.WaitingSupplierDelivery : RequestConstants.Statuses.PaymentCompleted, applied.GroupStatus);
        ctx.ChangeTracker.Clear();

        // Same user → 403; different Finance user → reversal; nothing later (no receipt / invoice / reconciliation) blocks it.
        var self = await E.Finance(ctx, s.Finance1).ReverseCredit(s.RequestId, new ReverseSupplierCreditDto { RequestPoGroupId = s.GroupB, Reason = "Rollback de teste — reversão pelo mesmo utilizador." });
        Assert.Equal(403, ((ObjectResult)self).StatusCode);
        var rev = Assert.IsType<SupplierCreditOperationResultDto>(Assert.IsType<OkObjectResult>(await E.Finance(ctx, s.Finance2).ReverseCredit(s.RequestId, new ReverseSupplierCreditDto { RequestPoGroupId = s.GroupB, Reason = "Rollback de teste — limpeza do fixture sintético antes do rollback." })).Value);
        Assert.Equal(SC.ReservationStatuses.Reversed, rev.ReservationStatus); Assert.Equal(reopenedStatus, rev.GroupStatus);
        ctx.ChangeTracker.Clear();
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupB);
        Assert.Equal(reopenedStatus, g.Status); Assert.Equal(0m, g.CreditCoverageAmount);
        var rows = await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestPoGroupId == s.GroupB).ToListAsync();
        Assert.Single(rows, p => p.PaymentType == reopenedRowType && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned && p.PlannedAmount == 500m);
        Assert.DoesNotContain(rows, p => p.PaymentStatus == RequestPayment.PaymentStatuses.Completed || p.PaymentStatus == RequestPayment.PaymentStatuses.Scheduled);
        Assert.Equal((1000m, 0m, 1000m), await E.Svc(ctx).AccountFiguresAsync(account));

        // The reopened bank obligation is cancelled through the standard cancellation (supported path) — no financial-row edit.
        var (orch, _) = S.OrchestratorMock();
        var service = new PoGroupCancellationService(ctx, new AlplaPortal.Infrastructure.Services.Approvals.RequestStatusSyncService(ctx, NullLogger<AlplaPortal.Infrastructure.Services.Approvals.RequestStatusSyncService>.Instance),
            orch.Object, S.AdminLog().Object, NullLogger<PoGroupCancellationService>.Instance, E.Svc(ctx));
        var cancel = await service.CancelAsync(S.Cancel(s, S.Buyer(s.Owner), groupId: s.GroupB, ecfReference: "ECF10 2026/9630"));
        Assert.True(cancel.Succeeded, $"{cancel.Code}: {cancel.Detail}");
        ctx.ChangeTracker.Clear();
        Assert.Equal(RequestConstants.PoGroupStatuses.Cancelled, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.GroupB)).Status);
        Assert.True(await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestPoGroupId == s.GroupB).AllAsync(p => p.PaymentStatus == RequestPayment.PaymentStatuses.Cancelled));
        var entries = await ctx.SupplierCreditEntries.AsNoTracking().Where(e => e.AccountId == account).Select(e => e.EntryType).ToListAsync();
        Assert.Equal(3, entries.Count); Assert.Contains(SC.EntryTypes.Application, entries); Assert.Contains(SC.EntryTypes.Reversal, entries);
        Assert.Equal(SC.ReservationStatuses.Reversed, (await SingleReservationAsync(ctx, s.GroupB)).Status);
        Assert.Equal((1000m, 0m, 1000m), await E.Svc(ctx).AccountFiguresAsync(account));
    }

    // ═════════════════════════════════════════ account management authorization ═════════════════════════════════════════

    private static SupplierCreditsController Accounts(ApplicationDbContext ctx, Guid userId, params string[] roles)
    {
        var ctl = new SupplierCreditsController(
            new SupplierCreditLedgerService(ctx, new InternalCompanyGuard(ctx), NullLogger<SupplierCreditLedgerService>.Instance),
            new SupplierCapabilityEvaluator(ctx));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } };
        return ctl;
    }

    private static bool CanManage(IActionResult list)
    {
        var value = Assert.IsType<OkObjectResult>(list).Value!;
        return (bool)value.GetType().GetProperty("canManage")!.GetValue(value)!;
    }

    [Fact]
    public async Task Credit_account_management_is_Contracts_only_and_reads_follow_the_ficha_capabilities()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx);
        if (!await ctx.Currencies.AnyAsync(c => c.Code == "AOA")) { ctx.Currencies.Add(new Currency { Code = "AOA", Symbol = "Kz", IsActive = true }); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear(); }
        var create = new CreateSupplierCreditAccountDto { CompanyId = S.Company, CurrencyCode = "AOA" };
        var creditIn = new AddSupplierCreditEntryDto { EntryType = SC.EntryTypes.CreditIn, Amount = 1000m, Reference = "NC-2026-001", Reason = "Nota de crédito do fornecedor (teste)" };

        // Finance (without Contracts): reads, cannot manage.
        Assert.False(CanManage(await Accounts(ctx, s.Finance1, RoleConstants.Finance).List(S.SupplierB, default)));
        Assert.IsType<ForbidResult>(await Accounts(ctx, s.Finance1, RoleConstants.Finance).Create(S.SupplierB, create, default));

        // Request-scoped Buyer (supplier B is in a group of a request in the Buyer's plant): reads, cannot manage.
        Assert.False(CanManage(await Accounts(ctx, s.Owner, RoleConstants.Buyer).List(S.SupplierB, default)));
        Assert.IsType<ForbidResult>(await Accounts(ctx, s.Owner, RoleConstants.Buyer).Create(S.SupplierB, create, default));
        Assert.Equal(0, await ctx.SupplierCreditAccounts.CountAsync());

        // Contracts (Purchasing): creates the account, records the credit, and a second account for the same key is 409.
        var contracts = Accounts(ctx, s.Admin, RoleConstants.Contracts);
        Assert.True(CanManage(await contracts.List(S.SupplierB, default)));
        var created = Assert.IsType<SupplierCreditAccountDto>(Assert.IsType<OkObjectResult>(await contracts.Create(S.SupplierB, create, default)).Value);
        Assert.StartsWith(SC.AccountNumberPrefix, created.AccountNumber); Assert.Equal(S.Company, created.CompanyId); Assert.Equal("AOA", created.CurrencyCode); Assert.Equal(0m, created.Balance);
        var dup = await contracts.Create(S.SupplierB, create, default);
        Assert.Equal(409, ((ObjectResult)dup).StatusCode); Assert.Equal(SC.ErrorCodes.AccountExists, E.Problem(dup).Extensions["code"]);
        var withCredit = Assert.IsType<SupplierCreditAccountDto>(Assert.IsType<OkObjectResult>(await contracts.AddEntry(S.SupplierB, created.Id, creditIn, default)).Value);
        Assert.Equal(1000m, withCredit.Balance); Assert.Equal(1000m, withCredit.Available);

        // Finance and the Buyer cannot add entries either.
        Assert.IsType<ForbidResult>(await Accounts(ctx, s.Finance1, RoleConstants.Finance).AddEntry(S.SupplierB, created.Id, creditIn, default));
        Assert.IsType<ForbidResult>(await Accounts(ctx, s.Owner, RoleConstants.Buyer).AddEntry(S.SupplierB, created.Id, creditIn, default));
        Assert.Equal(1, await ctx.SupplierCreditAccounts.CountAsync());
        Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync());
    }
}
