using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces.Suppliers;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;
using SC = AlplaPortal.Domain.Constants.SupplierCreditConstants;

namespace AlplaPortal.Application.Tests.Services.Suppliers;

/// <summary>
/// Supplier account credit — the workflow service on InMemory (the raw-SQL locks are skipped off SQL Server; every rule is
/// identical): reservation (partial, near-full normalisation, insufficient, flag off, unsupported terms), application
/// (once, idempotent), release (no ledger entry), reversal (FULL_COVERAGE only; evidence blocks; duplicate refused) and the
/// correction decisions keep / change / remove. Account: supplier B × company 1 × AOA with 1 000 CREDIT_IN; group B = 500 AOA.
/// </summary>
public class SupplierCreditSettlementServiceTests
{
    private static SupplierCreditSettlementService Svc(ApplicationDbContext ctx, bool enabled = true)
        => new(ctx, Options.Create(new SupplierCreditOptions { NewReservationsEnabled = enabled }), NullLogger<SupplierCreditSettlementService>.Instance);

    internal static async Task<Guid> SeedAccountAsync(ApplicationDbContext ctx, int supplierId, int companyId, string currency, decimal credit, Guid actor)
    {
        var acc = new SupplierCreditAccount { SupplierId = supplierId, CompanyId = companyId, CurrencyCode = currency, AccountNumber = $"CCS-{supplierId:D6}", Status = SC.AccountStatuses.Active, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor };
        ctx.SupplierCreditAccounts.Add(acc);
        ctx.SupplierCreditEntries.Add(new SupplierCreditEntry { AccountId = acc.Id, EntryType = SC.EntryTypes.CreditIn, SignedAmount = credit, BalanceAfter = credit, Reference = "NC-1", Reason = "Crédito inicial de teste", CreatedByUserId = actor, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return acc.Id;
    }

    private static async Task<(Request request, RequestPoGroup group)> LoadAsync(ApplicationDbContext ctx, S.Seed s, Guid groupId)
    {
        var request = await ctx.Requests.Include(r => r.RequestType).Include(r => r.Currency).Include(r => r.PoGroups).FirstAsync(r => r.Id == s.RequestId);
        return (request, request.PoGroups.First(g => g.Id == groupId));
    }

    private static ReserveSupplierCreditCommand Reserve(Request r, RequestPoGroup g, decimal amount, Guid actor, string terms = RequestConstants.PaymentConditions.PostPaid, bool enabled = true, bool correction = false, string? action = null)
        => new() { Request = r, Group = g, PaymentConditionCode = terms, AdvancePercent = terms == RequestConstants.PaymentConditions.AdvanceFull ? 100m : 0m, UseSupplierCredit = !correction || action == null, RequestedAmount = amount, IsCorrection = correction, CorrectionAction = action, NewReservationsEnabled = enabled, ActorUserId = actor };

    [Fact]
    public async Task Reserve_partial_creates_hold_split_and_owed_rows_without_a_ledger_entry()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx);
        var accountId = await SeedAccountAsync(ctx, S.SupplierB, S.Company, "AOA", 1000m, s.Owner);
        var (r, g) = await LoadAsync(ctx, s, s.GroupB);

        var outcome = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 350m, s.Owner));
        Assert.True(outcome.Succeeded, outcome.Detail);
        await ctx.SaveChangesAsync();

        var res = outcome.Value!.Reservation!;
        Assert.Equal(SC.ReservationStatuses.Reserved, res.Status); Assert.Equal(350m, res.Amount); Assert.Equal(0m, res.AdvanceCreditAmount); Assert.Equal(350m, res.BalanceCreditAmount);
        Assert.Equal(350m, g.CreditCoverageAmount); Assert.Equal(res.Id, g.SupplierCreditReservationId);
        Assert.False(outcome.Value.IsFullCoverage);
        var rows = outcome.Value.OwedRows;
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, p => p.PaymentType == RequestPayment.PaymentTypes.FinalBalance && p.PlannedAmount == 150m && p.SettlementStage == SC.Stages.Single && p.SupplierCreditReservationId == res.Id);
        Assert.Contains(rows, p => p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PlannedAmount == 350m && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned);
        Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync(e => e.AccountId == accountId)); // the hold is not a movement
        var f = await Svc(ctx).AccountFiguresAsync(accountId);
        Assert.Equal((1000m, 350m, 650m), f);
        Assert.Equal(SC.HistoryActions.Reserved, outcome.Value.HistoryAction);
    }

    [Fact]
    public async Task Reserve_near_full_normalises_to_full_coverage_and_creates_only_the_credit_row()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx);
        await SeedAccountAsync(ctx, S.SupplierB, S.Company, "AOA", 1000m, s.Owner);
        var (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var outcome = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 499.60m, s.Owner));
        Assert.True(outcome.Succeeded, outcome.Detail);
        Assert.True(outcome.Value!.IsFullCoverage); Assert.Equal(500m, outcome.Value.Coverage);
        Assert.Single(outcome.Value.OwedRows);
        Assert.Equal(RequestPayment.PaymentTypes.SupplierCredit, outcome.Value.OwedRows[0].PaymentType);
    }

    [Fact]
    public async Task Reserve_refuses_insufficient_flag_off_unsupported_terms_and_payment_requests_without_writing()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx);
        await SeedAccountAsync(ctx, S.SupplierB, S.Company, "AOA", 300m, s.Owner);
        var (r, g) = await LoadAsync(ctx, s, s.GroupB);

        var insufficient = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 350m, s.Owner));
        Assert.False(insufficient.Succeeded); Assert.Equal(409, insufficient.StatusCode); Assert.Equal(SC.ErrorCodes.Insufficient, insufficient.Code);

        var off = await Svc(ctx, enabled: false).ReserveOrCorrectAsync(Reserve(r, g, 100m, s.Owner, enabled: false));
        Assert.False(off.Succeeded); Assert.Equal(SC.ErrorCodes.Disabled, off.Code);

        var partialAdvance = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 100m, s.Owner, terms: RequestConstants.PaymentConditions.AdvancePartial));
        Assert.False(partialAdvance.Succeeded); Assert.Equal(SC.ErrorCodes.UnsupportedTerms, partialAdvance.Code);

        Assert.Equal(0m, g.CreditCoverageAmount); Assert.Null(g.SupplierCreditReservationId);
        Assert.Empty(ctx.SupplierCreditReservations.Local); Assert.Empty(ctx.RequestPayments.Local);
    }

    [Fact]
    public async Task Apply_full_coverage_debits_once_completes_the_row_and_refuses_a_second_application()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx);
        var accountId = await SeedAccountAsync(ctx, S.SupplierB, S.Company, "AOA", 1000m, s.Owner);
        var (r, g) = await LoadAsync(ctx, s, s.GroupB);
        Assert.True((await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 500m, s.Owner))).Succeeded);
        g.Status = RequestConstants.Statuses.PoIssued;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var apply = await Svc(ctx).ApplyStageAsync(r, g, SC.Stages.Single, s.Finance1, "ok");
        Assert.True(apply.Succeeded, apply.Detail);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        var res = await ctx.SupplierCreditReservations.SingleAsync();
        Assert.Equal(SC.ReservationStatuses.Applied, res.Status); Assert.Equal(500m, res.AppliedAmount); Assert.Equal(s.Finance1, res.AppliedByUserId); Assert.NotNull(res.BalanceAppliedEntryId);
        var entry = await ctx.SupplierCreditEntries.SingleAsync(e => e.EntryType == SC.EntryTypes.Application);
        Assert.Equal(-500m, entry.SignedAmount); Assert.Equal(500m, entry.BalanceAfter); Assert.Equal($"APPLY:{res.Id:D}:SINGLE", entry.IdempotencyKey); Assert.Equal(s.GroupB, entry.RequestPoGroupId);
        var row = await ctx.RequestPayments.SingleAsync(p => p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, row.PaymentStatus); Assert.Equal(500m, row.ActualPaidAmount);
        Assert.Equal((500m, 0m, 500m), await Svc(ctx).AccountFiguresAsync(accountId));

        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var again = await Svc(ctx).ApplyStageAsync(r, g, SC.Stages.Single, s.Finance1, null);
        Assert.False(again.Succeeded); Assert.Equal(409, again.StatusCode); Assert.Equal(SC.ErrorCodes.AlreadyApplied, again.Code);
    }

    [Fact]
    public async Task Release_reserved_hold_restores_availability_with_no_ledger_entry()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx);
        var accountId = await SeedAccountAsync(ctx, S.SupplierB, S.Company, "AOA", 1000m, s.Owner);
        var (r, g) = await LoadAsync(ctx, s, s.GroupB);
        Assert.True((await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 350m, s.Owner))).Succeeded);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var rel = await Svc(ctx).ReleaseAsync(g, SC.ReleaseReasons.GroupCancelled, "cancelado", s.Owner);
        Assert.True(rel.Succeeded, rel.Detail);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        var res = await ctx.SupplierCreditReservations.SingleAsync();
        Assert.Equal(SC.ReservationStatuses.Released, res.Status); Assert.Equal(350m, res.ReleasedHoldAmount); Assert.Equal(SC.ReleaseReasons.GroupCancelled, res.ReleaseReason);
        Assert.Equal(0m, (await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB)).CreditCoverageAmount);
        Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync()); // still only the CREDIT_IN
        Assert.Equal((1000m, 0m, 1000m), await Svc(ctx).AccountFiguresAsync(accountId));
        Assert.True(await ctx.RequestPayments.AllAsync(p => p.PaymentType != RequestPayment.PaymentTypes.SupplierCredit || p.PaymentStatus == RequestPayment.PaymentStatuses.Cancelled));
    }

    [Fact]
    public async Task Reverse_full_coverage_returns_the_applied_amount_cancels_credit_rows_and_reopens_the_bank_obligation()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx);
        var accountId = await SeedAccountAsync(ctx, S.SupplierB, S.Company, "AOA", 1000m, s.Owner);
        var (r, g) = await LoadAsync(ctx, s, s.GroupB);
        g.PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid;
        Assert.True((await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 500m, s.Owner))).Succeeded);
        g.Status = RequestConstants.Statuses.PoIssued;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        Assert.True((await Svc(ctx).ApplyStageAsync(r, g, SC.Stages.Single, s.Finance1, null)).Succeeded);
        g.Status = RequestConstants.Statuses.PaymentCompleted;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var rev = await Svc(ctx).ReverseAsync(r, g, s.Finance2, "Crédito aplicado por engano ao fornecedor errado.");
        Assert.True(rev.Succeeded, rev.Detail);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        Assert.Equal(SC.ReversalKinds.FullCoverage, rev.Value!.Kind); Assert.Equal(500m, rev.Value.ReversedAmount); Assert.Equal(RequestConstants.Statuses.PoIssued, rev.Value.ReopenedGroupStatus);
        var res = await ctx.SupplierCreditReservations.SingleAsync();
        Assert.Equal(SC.ReservationStatuses.Reversed, res.Status); Assert.Equal(500m, res.ReversedAppliedAmount); Assert.Equal(s.Finance2, res.ReversedByUserId);
        Assert.Equal(500m, (await ctx.SupplierCreditEntries.SingleAsync(e => e.EntryType == SC.EntryTypes.Reversal)).SignedAmount);
        Assert.Equal((1000m, 0m, 1000m), await Svc(ctx).AccountFiguresAsync(accountId));
        var rows = await ctx.RequestPayments.Where(p => p.RequestPoGroupId == s.GroupB).ToListAsync();
        Assert.Single(rows.Where(p => p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PaymentStatus == RequestPayment.PaymentStatuses.Cancelled));
        var reopened = rows.Single(p => p.PaymentType == RequestPayment.PaymentTypes.FinalBalance);
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, reopened.PaymentStatus); Assert.Equal(500m, reopened.PlannedAmount); Assert.Equal(res.Id, reopened.SupplierCreditReservationId);
        Assert.Equal(0m, (await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB)).CreditCoverageAmount);
        Assert.Equal(res.Id, (await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB)).SupplierCreditReservationId); // pointer kept: still credit-touched

        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var again = await Svc(ctx).ReverseAsync(r, g, s.Finance2, "Tentativa duplicada de reversão do crédito.");
        Assert.False(again.Succeeded); Assert.Equal(SC.ErrorCodes.AlreadyReversed, again.Code);
    }

    [Fact]
    public async Task Reverse_is_refused_for_partial_coverage_and_when_evidence_exists()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx);
        await SeedAccountAsync(ctx, S.SupplierB, S.Company, "AOA", 1000m, s.Owner);
        var (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var reserve = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 350m, s.Owner));
        Assert.True(reserve.Succeeded);
        g.Status = RequestConstants.Statuses.PoIssued;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        Assert.True((await Svc(ctx).ApplyStageAsync(r, g, SC.Stages.Single, s.Finance1, null)).Succeeded);
        var bank = await ctx.RequestPayments.SingleAsync(p => p.RequestPoGroupId == s.GroupB && p.PaymentType == RequestPayment.PaymentTypes.FinalBalance);
        bank.PaymentStatus = RequestPayment.PaymentStatuses.Completed; bank.ActualPaidAmount = 150m;
        g.Status = RequestConstants.Statuses.PaymentCompleted;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var partial = await Svc(ctx).ReverseAsync(r, g, s.Finance2, "Reversão com cobertura parcial — fase 2.");
        Assert.False(partial.Succeeded); Assert.Equal(SC.ErrorCodes.ReversalPartialUnsupported, partial.Code);

        g.Status = RequestConstants.Statuses.WaitingReceipt;
        var evidence = await Svc(ctx).ReverseAsync(r, g, s.Finance2, "Reversão após receção — deve ser bloqueada.");
        Assert.False(evidence.Succeeded); Assert.Equal(SC.ErrorCodes.ReversalBlockedByEvidence, evidence.Code);
    }

    [Fact]
    public async Task Correction_keep_recreates_rows_change_supersedes_remove_releases()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx);
        var accountId = await SeedAccountAsync(ctx, S.SupplierB, S.Company, "AOA", 1000m, s.Owner);
        var (r, g) = await LoadAsync(ctx, s, s.GroupB);
        Assert.True((await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 350m, s.Owner))).Succeeded);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        // Finance return cancelled the owed rows (simulated) and parked the group in correction.
        foreach (var p in await ctx.RequestPayments.Where(p => p.RequestPoGroupId == s.GroupB).ToListAsync()) p.PaymentStatus = RequestPayment.PaymentStatuses.Cancelled;
        (await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB)).Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        // KEEP → same hold, rows recreated.
        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var keep = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 0m, s.Owner, correction: true, action: SC.CorrectionActions.Keep));
        Assert.True(keep.Succeeded, keep.Detail);
        Assert.Equal(350m, keep.Value!.Coverage); Assert.Equal(2, keep.Value.OwedRows.Count); Assert.Null(keep.Value.HistoryAction);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.SupplierCreditReservations.CountAsync());

        // CHANGE (flag on) → old RELEASED/superseded, new RESERVED 200; figures hold 200.
        foreach (var p in await ctx.RequestPayments.Where(p => p.RequestPoGroupId == s.GroupB && p.PaymentStatus == "PLANNED").ToListAsync()) p.PaymentStatus = RequestPayment.PaymentStatuses.Cancelled;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var change = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 200m, s.Owner, correction: true, action: SC.CorrectionActions.Change));
        Assert.True(change.Succeeded, change.Detail);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var all = await ctx.SupplierCreditReservations.OrderBy(x => x.ReservedAtUtc).ToListAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(SC.ReservationStatuses.Released, all[0].Status); Assert.Equal(SC.ReleaseReasons.CorrectionChanged, all[0].ReleaseReason); Assert.Equal(all[1].Id, all[0].SupersededByReservationId);
        Assert.Equal(SC.ReservationStatuses.Reserved, all[1].Status); Assert.Equal(200m, all[1].Amount);
        Assert.Equal((1000m, 200m, 800m), await Svc(ctx).AccountFiguresAsync(accountId));
        Assert.Equal(200m, (await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB)).CreditCoverageAmount);

        // CHANGE with the flag off → refused, nothing written.
        (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var changeOff = await Svc(ctx, enabled: false).ReserveOrCorrectAsync(Reserve(r, g, 100m, s.Owner, enabled: false, correction: true, action: SC.CorrectionActions.Change));
        Assert.False(changeOff.Succeeded); Assert.Equal(SC.ErrorCodes.Disabled, changeOff.Code);

        // REMOVE (flag off still allowed) → released, coverage 0, available restored.
        var remove = await Svc(ctx, enabled: false).ReserveOrCorrectAsync(Reserve(r, g, 0m, s.Owner, enabled: false, correction: true, action: SC.CorrectionActions.Remove));
        Assert.True(remove.Succeeded, remove.Detail);
        Assert.Null(remove.Value!.Reservation); Assert.Equal(SC.HistoryActions.Released, remove.Value.HistoryAction);
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        Assert.Equal(0m, (await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupB)).CreditCoverageAmount);
        Assert.Equal((1000m, 0m, 1000m), await Svc(ctx).AccountFiguresAsync(accountId));
        Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync()); // holds never wrote a ledger entry
    }

    [Fact]
    public async Task Eligibility_reports_account_max_coverage_and_the_reasons()
    {
        await using var ctx = S.NewCtx();
        var s = await S.SeedAsync(ctx, groupAStatus: "WAITING_PO", groupAPoNumber: null, groupAPoAttachment: false);
        await SeedAccountAsync(ctx, S.SupplierB, S.Company, "AOA", 300m, s.Owner);
        var (r, g) = await LoadAsync(ctx, s, s.GroupB);
        var e = await Svc(ctx).EvaluateEligibilityAsync(r, g, RequestConstants.PaymentConditions.PostPaid, actorMayReserve: true);
        Assert.True(e.Enabled); Assert.True(e.CanReserve); Assert.NotNull(e.Account); Assert.Equal(300m, e.MaxCoverage); Assert.Equal(500m, e.GroupTotalAmount);

        var notOwner = await Svc(ctx).EvaluateEligibilityAsync(r, g, RequestConstants.PaymentConditions.PostPaid, actorMayReserve: false);
        Assert.False(notOwner.CanReserve); Assert.Equal(SC.ErrorCodes.NotResponsibleBuyer, notOwner.ReasonCode);

        var partial = await Svc(ctx).EvaluateEligibilityAsync(r, g, RequestConstants.PaymentConditions.AdvancePartial, actorMayReserve: true);
        Assert.False(partial.CanReserve); Assert.Equal(SC.ErrorCodes.UnsupportedTerms, partial.ReasonCode);

        var off = await Svc(ctx, enabled: false).EvaluateEligibilityAsync(r, g, RequestConstants.PaymentConditions.PostPaid, actorMayReserve: true);
        Assert.False(off.Enabled); Assert.False(off.CanReserve); Assert.Equal(SC.ErrorCodes.Disabled, off.ReasonCode);

        // Group A (supplier A, RND) has no account → the option does not exist.
        var (_, a) = await LoadAsync(ctx, s, s.GroupA);
        var none = await Svc(ctx).EvaluateEligibilityAsync(r, a, RequestConstants.PaymentConditions.PostPaid, actorMayReserve: true);
        Assert.Null(none.Account); Assert.Equal(SC.ErrorCodes.AccountNotFound, none.ReasonCode);
    }
}
