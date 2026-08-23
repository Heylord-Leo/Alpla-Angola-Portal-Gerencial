using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Api.Services;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// Unit coverage for the canonical PaymentSequenceAllocator — the single request-scoped sequence
/// source now shared by all three production writers (FinanceController.SchedulePayment,
/// RequestsController.RegisterPo advance, RequestsController.ReconcileRequest final balance). The
/// unique key is (RequestId, PaymentType, PaymentSequence); the allocator must count every payment
/// of the request+type (group-attached AND group-less), keep types independent, and never reuse a
/// CANCELLED sequence. InMemory EF does not enforce the unique index — these tests assert the
/// allocated VALUE (the logic); DB-level non-collision is proven by the ZZTEST-FIN acceptance battery.
/// </summary>
public class PaymentSequenceAllocatorTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()).Options);

    private static void AddPayment(ApplicationDbContext ctx, Guid requestId, Guid? groupId, string type, string status, int seq)
    {
        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = requestId,
            RequestPoGroupId = groupId,
            PaymentType = type,
            PaymentStatus = status,
            PaymentSequence = seq,
            PlannedAmount = 1000m,
            CurrencyCode = "AOA",
            CreatedAtUtc = DateTime.UtcNow
        });
        ctx.SaveChanges();
    }

    // 1. RegisterPo ADVANCE sibling: group A already owns ADVANCE seq1 → next ADVANCE = seq2.
    [Fact]
    public async Task Advance_SiblingAlreadySeq1_NextIsSeq2()
    {
        using var ctx = NewContext();
        var reqId = Guid.NewGuid();
        AddPayment(ctx, reqId, Guid.NewGuid(), RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Planned, 1);

        var next = await PaymentSequenceAllocator.NextSequenceAsync(ctx, reqId, RequestPayment.PaymentTypes.Advance);
        Assert.Equal(2, next);
    }

    // 2. Type independence: an existing ADVANCE seq1 does not push a fresh FINAL_BALANCE off seq1.
    [Fact]
    public async Task Types_AreIndependent()
    {
        using var ctx = NewContext();
        var reqId = Guid.NewGuid();
        AddPayment(ctx, reqId, Guid.NewGuid(), RequestPayment.PaymentTypes.Advance, RequestPayment.PaymentStatuses.Planned, 1);

        var nextFinal = await PaymentSequenceAllocator.NextSequenceAsync(ctx, reqId, RequestPayment.PaymentTypes.FinalBalance);
        Assert.Equal(1, nextFinal);
        var nextAdvance = await PaymentSequenceAllocator.NextSequenceAsync(ctx, reqId, RequestPayment.PaymentTypes.Advance);
        Assert.Equal(2, nextAdvance);
    }

    // 3. Reconciliation FINAL_BALANCE with a GROUP-ATTACHED FINAL_BALANCE seq1 already present →
    //    the group-less remaining-balance row must be seq2 (request-scoped, spans group-less rows).
    [Fact]
    public async Task FinalBalance_GroupAttachedSeq1_NextGroupLessIsSeq2()
    {
        using var ctx = NewContext();
        var reqId = Guid.NewGuid();
        AddPayment(ctx, reqId, Guid.NewGuid(), RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Completed, 1);

        var next = await PaymentSequenceAllocator.NextSequenceAsync(ctx, reqId, RequestPayment.PaymentTypes.FinalBalance);
        Assert.Equal(2, next); // even though the caller row will be group-less
    }

    // 4. Reconciliation FINAL_BALANCE with none existing → seq1.
    [Fact]
    public async Task FinalBalance_NoneExisting_IsSeq1()
    {
        using var ctx = NewContext();
        var next = await PaymentSequenceAllocator.NextSequenceAsync(ctx, Guid.NewGuid(), RequestPayment.PaymentTypes.FinalBalance);
        Assert.Equal(1, next);
    }

    // 5. A CANCELLED sequence is counted and never reused.
    [Fact]
    public async Task CancelledSequence_IsNotReused()
    {
        using var ctx = NewContext();
        var reqId = Guid.NewGuid();
        AddPayment(ctx, reqId, Guid.NewGuid(), RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Cancelled, 1);

        var next = await PaymentSequenceAllocator.NextSequenceAsync(ctx, reqId, RequestPayment.PaymentTypes.FinalBalance);
        Assert.Equal(2, next);
    }

    // 6a. Scope isolation — payments of OTHER requests never influence this request's sequence.
    [Fact]
    public async Task OtherRequests_DoNotAffectSequence()
    {
        using var ctx = NewContext();
        var reqId = Guid.NewGuid();
        var otherReqId = Guid.NewGuid();
        AddPayment(ctx, otherReqId, Guid.NewGuid(), RequestPayment.PaymentTypes.FinalBalance, RequestPayment.PaymentStatuses.Completed, 7);

        var next = await PaymentSequenceAllocator.NextSequenceAsync(ctx, reqId, RequestPayment.PaymentTypes.FinalBalance);
        Assert.Equal(1, next);
    }

    // 6b. Tracked-but-unsaved rows in the same unit of work are counted (monotonic before SaveChanges).
    [Fact]
    public async Task TrackedUnsavedRow_IsCounted()
    {
        using var ctx = NewContext();
        var reqId = Guid.NewGuid();
        // Added but NOT saved — visible via ctx.RequestPayments.Local only.
        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = reqId,
            PaymentType = RequestPayment.PaymentTypes.Advance,
            PaymentStatus = RequestPayment.PaymentStatuses.Planned,
            PaymentSequence = 1,
            PlannedAmount = 500m,
            CurrencyCode = "AOA",
            CreatedAtUtc = DateTime.UtcNow
        });

        var next = await PaymentSequenceAllocator.NextSequenceAsync(ctx, reqId, RequestPayment.PaymentTypes.Advance);
        Assert.Equal(2, next);
    }

    // The uniqueness contract is unchanged: the unique index still exists on the model.
    [Fact]
    public void UniqueIndex_StillDefined()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(RequestPayment))!;
        var match = entity.GetIndexes().FirstOrDefault(ix =>
            ix.IsUnique &&
            ix.Properties.Select(p => p.Name).SequenceEqual(new[] { "RequestId", "PaymentType", "PaymentSequence" }));
        Assert.NotNull(match);
    }
}
