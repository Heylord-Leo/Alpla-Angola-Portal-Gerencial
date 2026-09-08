using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Repairs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.241.0 — REAL-PROVIDER concurrency pins (SQL Server LocalDB) for the legacy monetary-scale repair.
/// The InMemory suite proves the POLICY; these prove the four MECHANISMS that make apply safe under
/// concurrency, and that a mid-persist failure rolls the whole transaction back. Every apply carries
/// the four preview tokens; the guarded row that a concurrent writer moved makes its token stale:
///   1. Request rowversion  — tokens still match (stale tracked entity) so SaveChanges throws → CONFLICT,
///                            transaction rolls back (mid-persist rollback proof).
///   2. PoGroup rowversion  — the PoGroup token no longer matches → CONFLICT before write.
///   3. Line fingerprint    — the Line has no rowversion; its value fingerprint moved → CONFLICT.
///   4. Payment fingerprint — the Payment has no rowversion; its value fingerprint moved → CONFLICT.
/// In every case: no repair values written, no corrective audit, the concurrent change preserved.
///
/// <para>Requires SQL Server LocalDB (MSSQLLocalDB); skips cleanly when it cannot connect.</para>
/// </summary>
public class LegacyMonetaryScaleRepairRowVersionSqlTests
{
    private const decimal Wrong = 18_400_000_000m;
    private const decimal Correct = 18_400_000m;
    private const decimal BadDiscount = -18_381_600_000m;

    private static DbContextOptions<ApplicationDbContext> SqlOptions(string dbName) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                $@"Server=(localdb)\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;" +
                "MultipleActiveResultSets=true;Connection Timeout=60")
            .Options;

    private static LegacyMonetaryScaleRepairRequest BodyFrom(LegacyMonetaryScaleRepairPreview p) => new()
    {
        ExpectedCurrentTotal = Wrong, ExpectedCorrectTotal = Correct, Reason = "concurrency pin",
        ExpectedRequestRowVersion = p.RequestRowVersion,
        ExpectedPoGroupRowVersion = p.PoGroupRowVersion,
        ExpectedLineFingerprint = p.LineFingerprint,
        ExpectedPaymentFingerprint = p.PaymentFingerprint,
    };

    private static async Task<bool> TrySeedAsync(DbContextOptions<ApplicationDbContext> options, Guid actorId, Guid requestId)
    {
        await using var seed = new ApplicationDbContext(options);
        try { await seed.Database.EnsureCreatedAsync(); }
        catch (Exception) { return false; }

        var actor = new User { Id = actorId, FullName = "Rv Tester", Email = "lmsrv@test.local" };
        var department = new Department { Name = "ZZTEST Dept" };
        var company = new Company { Name = "ZZTEST Co" };
        seed.Users.Add(actor);
        seed.Departments.Add(department);
        seed.Companies.Add(company);

        var type = await seed.RequestTypes.FirstOrDefaultAsync(t => t.Code == RequestConstants.Types.Payment)
            ?? seed.RequestTypes.Add(new RequestType { Code = RequestConstants.Types.Payment, Name = "Pagamento" }).Entity;
        var status = await seed.RequestStatuses.FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.PaymentScheduled)
            ?? seed.RequestStatuses.Add(new RequestStatus { Code = RequestConstants.Statuses.PaymentScheduled, Name = "Pagamento Agendado", DisplayOrder = 25 }).Entity;
        var currency = await seed.Currencies.FirstOrDefaultAsync(c => c.Code == "AOA")
            ?? seed.Currencies.Add(new Currency { Code = "AOA", Symbol = "Kz" }).Entity;
        await seed.SaveChangesAsync();

        seed.Requests.Add(new Request
        {
            Id = requestId, RequestNumber = "ZZTEST-LMS-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST legacy scale", RequestTypeId = type.Id, StatusId = status.Id, RequesterId = actor.Id,
            DepartmentId = department.Id, CompanyId = company.Id, CurrencyId = currency.Id,
            DiscountAmount = 0m, EstimatedTotalAmount = Wrong, ApprovedTotalAmount = Wrong,
            ApprovedCurrencyCode = "AOA", CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
        });
        seed.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = requestId, LineNumber = 1, Description = "Palete",
            Quantity = 4000m, UnitPrice = 4600m, DiscountAmount = BadDiscount, TotalAmount = Wrong,
            CurrencyId = currency.Id, IsDeleted = false, CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
        });
        seed.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = requestId, TotalAmount = Wrong, CurrencyCode = "AOA",
            Status = "PAYMENT_SCHEDULED", PurchaseOrderNumber = "ECF 2026/107",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-9), CreatedByUserId = actor.Id,
        });
        seed.RequestPayments.Add(new RequestPayment
        {
            RequestId = requestId, PaymentType = "FINAL_BALANCE", PlannedAmount = Wrong,
            CurrencyCode = "AOA", PaymentStatus = "SCHEDULED", ActualPaidAmount = null,
            CreatedByUserId = actor.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
        });
        await seed.SaveChangesAsync();
        return true;
    }

    private static async Task AssertNoRepairAsync(DbContextOptions<ApplicationDbContext> options, Guid requestId)
    {
        await using var verify = new ApplicationDbContext(options);
        var line = await verify.RequestLineItems.FirstAsync(l => l.RequestId == requestId);
        Assert.Equal(BadDiscount, line.DiscountAmount);
        Assert.Equal(0, await verify.Set<RequestFieldChangeHistory>().CountAsync(h => h.RequestId == requestId));
        Assert.Null(await verify.RequestStatusHistories.FirstOrDefaultAsync(h => h.RequestId == requestId && h.ActionTaken == "FINANCIAL_CORRECTION"));
    }

    private static async Task DropAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var drop = new ApplicationDbContext(options);
        try { await drop.Database.EnsureDeletedAsync(); } catch { /* best effort */ }
    }

    // ── 1. Request rowversion: tokens still match (stale tracked entity) → SaveChanges throws →
    //       CONFLICT and the transaction rolls back (mid-persist rollback proof). ──────────────
    [Fact]
    public async Task Request_rowversion_conflict_rolls_back_with_no_write()
    {
        var options = SqlOptions("AlplaPortal_ZZTEST_LmsReq_" + Guid.NewGuid().ToString("N")[..12]);
        var actorId = Guid.NewGuid(); var requestId = Guid.NewGuid();
        try
        {
            if (!await TrySeedAsync(options, actorId, requestId)) return;

            // ctx1 previews (loading + tracking the graph at rowversion v1) and yields matching tokens.
            await using var ctx1 = new ApplicationDbContext(options);
            var body = BodyFrom((await new LegacyMonetaryScaleRepairService(ctx1).PreviewAsync(requestId, Wrong, Correct))!);

            await using (var ctx2 = new ApplicationDbContext(options))
            {
                var r2 = await ctx2.Requests.FirstAsync(r => r.Id == requestId);
                r2.Title += " [touched]";
                await ctx2.SaveChangesAsync();
            }

            // Tokens match the (stale) tracked Request, so the token guard passes and the stale
            // rowversion surfaces at SaveChanges inside the transaction → CONFLICT + rollback.
            var result = await new LegacyMonetaryScaleRepairService(ctx1).ApplyAsync(requestId, body, actorId, "Op");
            Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.Conflict, result.Status);
            Assert.Equal(0, result.RowsChanged);
            await AssertNoRepairAsync(options, requestId);
        }
        finally { await DropAsync(options); }
    }

    // ── 2. PoGroup rowversion token moved since preview → CONFLICT before any write ────────────
    [Fact]
    public async Task PoGroup_rowversion_token_moved_yields_conflict()
    {
        var options = SqlOptions("AlplaPortal_ZZTEST_LmsPo_" + Guid.NewGuid().ToString("N")[..12]);
        var actorId = Guid.NewGuid(); var requestId = Guid.NewGuid();
        try
        {
            if (!await TrySeedAsync(options, actorId, requestId)) return;

            LegacyMonetaryScaleRepairRequest body;
            await using (var preview = new ApplicationDbContext(options))
                body = BodyFrom((await new LegacyMonetaryScaleRepairService(preview).PreviewAsync(requestId, Wrong, Correct))!);

            await using (var mutate = new ApplicationDbContext(options))
            {
                var po = await mutate.RequestPoGroups.FirstAsync(g => g.RequestId == requestId);
                po.SupplierNifSnapshot = "concurrent-touch"; // advances rowversion, not a gated value
                await mutate.SaveChangesAsync();
            }

            await using var apply = new ApplicationDbContext(options);
            var result = await new LegacyMonetaryScaleRepairService(apply).ApplyAsync(requestId, body, actorId, "Op");

            Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.Conflict, result.Status);
            await AssertNoRepairAsync(options, requestId);
            await using var verify = new ApplicationDbContext(options);
            Assert.Equal("concurrent-touch", (await verify.RequestPoGroups.FirstAsync(g => g.RequestId == requestId)).SupplierNifSnapshot);
        }
        finally { await DropAsync(options); }
    }

    // ── 3. Line fingerprint moved since preview → CONFLICT (Line has no rowversion) ────────────
    [Fact]
    public async Task Line_fingerprint_moved_yields_conflict()
    {
        var options = SqlOptions("AlplaPortal_ZZTEST_LmsLine_" + Guid.NewGuid().ToString("N")[..12]);
        var actorId = Guid.NewGuid(); var requestId = Guid.NewGuid();
        try
        {
            if (!await TrySeedAsync(options, actorId, requestId)) return;

            LegacyMonetaryScaleRepairRequest body;
            await using (var preview = new ApplicationDbContext(options))
                body = BodyFrom((await new LegacyMonetaryScaleRepairService(preview).PreviewAsync(requestId, Wrong, Correct))!);

            const decimal concurrentTotal = 12_345m;
            await using (var mutate = new ApplicationDbContext(options))
            {
                var line = await mutate.RequestLineItems.FirstAsync(l => l.RequestId == requestId);
                line.DiscountAmount = -1m; line.TotalAmount = concurrentTotal; line.UpdatedAtUtc = DateTime.UtcNow;
                await mutate.SaveChangesAsync();
            }

            await using var apply = new ApplicationDbContext(options);
            var result = await new LegacyMonetaryScaleRepairService(apply).ApplyAsync(requestId, body, actorId, "Op");

            // The fingerprint token guard fires before any value gate → CONFLICT, not REFUSED.
            Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.Conflict, result.Status);
            Assert.Equal(0, await apply.Set<RequestFieldChangeHistory>().CountAsync(h => h.RequestId == requestId));
            await using var verify = new ApplicationDbContext(options);
            var vLine = await verify.RequestLineItems.FirstAsync(l => l.RequestId == requestId);
            Assert.Equal(concurrentTotal, vLine.TotalAmount);   // concurrent value preserved
            Assert.Equal(-1m, vLine.DiscountAmount);
        }
        finally { await DropAsync(options); }
    }

    // ── 4. Payment fingerprint moved since preview → CONFLICT (Payment has no rowversion) ──────
    [Fact]
    public async Task Payment_fingerprint_moved_yields_conflict()
    {
        var options = SqlOptions("AlplaPortal_ZZTEST_LmsPay_" + Guid.NewGuid().ToString("N")[..12]);
        var actorId = Guid.NewGuid(); var requestId = Guid.NewGuid();
        try
        {
            if (!await TrySeedAsync(options, actorId, requestId)) return;

            LegacyMonetaryScaleRepairRequest body;
            await using (var preview = new ApplicationDbContext(options))
                body = BodyFrom((await new LegacyMonetaryScaleRepairService(preview).PreviewAsync(requestId, Wrong, Correct))!);

            const decimal concurrentPlanned = 777m;
            await using (var mutate = new ApplicationDbContext(options))
            {
                var pay = await mutate.RequestPayments.FirstAsync(p => p.RequestId == requestId);
                pay.PlannedAmount = concurrentPlanned; pay.UpdatedAtUtc = DateTime.UtcNow;
                await mutate.SaveChangesAsync();
            }

            await using var apply = new ApplicationDbContext(options);
            var result = await new LegacyMonetaryScaleRepairService(apply).ApplyAsync(requestId, body, actorId, "Op");

            Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.Conflict, result.Status);
            await AssertNoRepairAsync(options, requestId);
            await using var verify = new ApplicationDbContext(options);
            Assert.Equal(concurrentPlanned, (await verify.RequestPayments.FirstAsync(p => p.RequestId == requestId)).PlannedAmount);
        }
        finally { await DropAsync(options); }
    }
}
