using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Repairs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.241.0 — the controlled legacy monetary-scale repair (incident REQ-11/08/2026-228).
/// Proves: preview writes nothing; apply requires the four preview tokens (direct apply cannot bypass
/// preview); a real apply corrects all four persisted entities and appends (never rewrites) audit; it
/// is idempotent; it never changes status or emits a stage transition; and it refuses everything that
/// is not the exact defect fingerprint. Rowversion/fingerprint CONFLICT behaviour is covered by the
/// SQL-LocalDB suite (InMemory does not honour rowversion).
/// </summary>
public class LegacyMonetaryScaleRepairTests
{
    private const decimal Wrong = 18_400_000_000m;
    private const decimal Correct = 18_400_000m;
    private const decimal BadDiscount = -18_381_600_000m; // (qty*price) - Wrong

    private static readonly Guid Actor = Guid.NewGuid();

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Seeds the exact REQ-228 defect shape and returns the request id.</summary>
    private static async Task<Guid> SeedAsync(
        ApplicationDbContext ctx,
        Action<Request, RequestLineItem, RequestPoGroup, RequestPayment>? mutate = null,
        bool alreadyCorrect = false)
    {
        var status = new RequestStatus { Id = 14, Code = "PAYMENT_SCHEDULED", Name = "Pagamento Agendado" };
        var currency = new Currency { Id = 1, Code = "AOA", Symbol = "Kz" };
        ctx.Add(status);
        ctx.Add(currency);

        var reqId = Guid.NewGuid();
        var lineTotal = alreadyCorrect ? Correct : Wrong;
        var lineDiscount = alreadyCorrect ? 0m : BadDiscount;

        var request = new Request
        {
            Id = reqId, RequestNumber = "REQ-11/08/2026-228", Title = "Pagamento AD-CERTO",
            StatusId = 14, Status = status, CurrencyId = 1, Currency = currency, DiscountAmount = 0m,
            EstimatedTotalAmount = lineTotal, ApprovedTotalAmount = lineTotal, ApprovedCurrencyCode = "AOA",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
        };
        var line = new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = reqId, LineNumber = 1, Description = "Palete Madeira",
            Quantity = 4000m, UnitPrice = 4600m, DiscountAmount = lineDiscount, DiscountPercent = null,
            IvaRateId = null, TotalAmount = lineTotal, CurrencyId = 1, IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
        };
        var po = new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = reqId, TotalAmount = lineTotal, CurrencyCode = "AOA",
            Status = "PAYMENT_SCHEDULED", PurchaseOrderNumber = "ECF 2026/107",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-9), CreatedByUserId = Actor,
        };
        var pay = new RequestPayment
        {
            Id = 1, RequestId = reqId, PaymentType = "FINAL_BALANCE", PlannedAmount = lineTotal,
            CurrencyCode = "AOA", PaymentStatus = "SCHEDULED", ActualPaidAmount = null,
            CreatedByUserId = Actor, CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
        };

        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(), RequestId = reqId, ActorUserId = Actor, ActionTaken = "REGISTER_PO",
            PreviousStatusId = 14, NewStatusId = 14, Comment = "Total: 18,400,000,000.00 (histórico original)",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
        });

        mutate?.Invoke(request, line, po, pay);

        ctx.Requests.Add(request);
        ctx.RequestLineItems.Add(line);
        ctx.RequestPoGroups.Add(po);
        ctx.RequestPayments.Add(pay);
        await ctx.SaveChangesAsync();
        return reqId;
    }

    /// <summary>Runs a real preview and returns an apply body carrying its four concurrency tokens —
    /// the only legitimate way to build an apply body.</summary>
    private static async Task<LegacyMonetaryScaleRepairRequest> TokenIntentAsync(
        ApplicationDbContext ctx, Guid id, decimal expCurrent, decimal expCorrect, string reason = "Correção controlada.")
    {
        var p = await new LegacyMonetaryScaleRepairService(ctx).PreviewAsync(id, expCurrent, expCorrect);
        return new LegacyMonetaryScaleRepairRequest
        {
            ExpectedCurrentTotal = expCurrent, ExpectedCorrectTotal = expCorrect, Reason = reason,
            ExpectedRequestRowVersion = p!.RequestRowVersion,
            ExpectedPoGroupRowVersion = p.PoGroupRowVersion,
            ExpectedLineFingerprint = p.LineFingerprint,
            ExpectedPaymentFingerprint = p.PaymentFingerprint,
        };
    }

    // ── Preview ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_writes_nothing_and_reports_the_correction()
    {
        using var ctx = NewContext();
        var id = await SeedAsync(ctx);
        var transitionsBefore = await ctx.Set<OperationalStageTransition>().CountAsync();

        var preview = await new LegacyMonetaryScaleRepairService(ctx).PreviewAsync(id, Wrong, Correct);

        Assert.NotNull(preview);
        Assert.True(preview!.AllSafetyChecksPassed);
        Assert.True(preview.WillWrite);
        Assert.False(preview.AlreadyCorrect);
        Assert.Equal(Wrong, preview.LineCurrentTotal);
        Assert.Equal(Correct, preview.LineCorrectedTotal);
        // Fingerprint tokens are exposed for the operator to hand back to apply.
        Assert.NotEqual(string.Empty, preview.LineFingerprint);
        Assert.NotEqual(string.Empty, preview.PaymentFingerprint);

        ctx.ChangeTracker.Clear();
        var line = await ctx.RequestLineItems.FirstAsync(l => l.RequestId == id);
        Assert.Equal(Wrong, line.TotalAmount);
        Assert.Equal(BadDiscount, line.DiscountAmount);
        Assert.Equal(0, await ctx.Set<RequestFieldChangeHistory>().CountAsync(h => h.RequestId == id));
        Assert.Equal(transitionsBefore, await ctx.Set<OperationalStageTransition>().CountAsync());
    }

    // ── Apply (happy path) ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_corrects_all_four_entities_and_appends_audit_without_status_change()
    {
        using var ctx = NewContext();
        var id = await SeedAsync(ctx);
        var transitionsBefore = await ctx.Set<OperationalStageTransition>().CountAsync();
        var body = await TokenIntentAsync(ctx, id, Wrong, Correct);

        var result = await new LegacyMonetaryScaleRepairService(ctx).ApplyAsync(id, body, Actor, "Op Tester");
        Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.Applied, result.Status);

        ctx.ChangeTracker.Clear();
        var req = await ctx.Requests.Include(r => r.Status).FirstAsync(r => r.Id == id);
        var line = await ctx.RequestLineItems.FirstAsync(l => l.RequestId == id);
        var po = await ctx.RequestPoGroups.FirstAsync(g => g.RequestId == id);
        var pay = await ctx.RequestPayments.FirstAsync(p => p.RequestId == id);

        Assert.Equal(Correct, line.TotalAmount);
        Assert.Equal(0m, line.DiscountAmount);
        Assert.Equal(Correct, req.EstimatedTotalAmount);
        Assert.Equal(Correct, req.ApprovedTotalAmount);
        Assert.Equal(Correct, po.TotalAmount);
        Assert.Equal(Correct, pay.PlannedAmount);

        Assert.Equal(14, req.StatusId);
        Assert.Equal("ECF 2026/107", po.PurchaseOrderNumber);
        Assert.Equal("SCHEDULED", pay.PaymentStatus);
        Assert.Null(pay.ActualPaidAmount);

        Assert.NotNull(await ctx.RequestStatusHistories.FirstOrDefaultAsync(h => h.RequestId == id && h.ActionTaken == "REGISTER_PO"));
        var correction = await ctx.RequestStatusHistories.FirstOrDefaultAsync(h => h.RequestId == id && h.ActionTaken == "FINANCIAL_CORRECTION");
        Assert.NotNull(correction);
        Assert.Equal(correction!.PreviousStatusId, correction.NewStatusId);
        Assert.Equal(6, await ctx.Set<RequestFieldChangeHistory>().CountAsync(h => h.RequestId == id));
        Assert.Equal(transitionsBefore, await ctx.Set<OperationalStageTransition>().CountAsync());
    }

    // ── Idempotency ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Second_apply_is_a_noop_and_writes_no_further_audit()
    {
        using var ctx = NewContext();
        var id = await SeedAsync(ctx);
        var svc = new LegacyMonetaryScaleRepairService(ctx);

        await svc.ApplyAsync(id, await TokenIntentAsync(ctx, id, Wrong, Correct), Actor, "Op");
        var auditAfterFirst = await ctx.Set<RequestFieldChangeHistory>().CountAsync(h => h.RequestId == id);

        var preview = await svc.PreviewAsync(id, Wrong, Correct);
        Assert.True(preview!.AlreadyCorrect);
        Assert.False(preview.WillWrite);

        // Second apply needs tokens from a fresh (post-correction) preview.
        var second = await svc.ApplyAsync(id, await TokenIntentAsync(ctx, id, Correct, Correct), Actor, "Op");
        Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.AlreadyCorrect, second.Status);
        Assert.Equal(0, second.RowsChanged);
        Assert.Equal(auditAfterFirst, await ctx.Set<RequestFieldChangeHistory>().CountAsync(h => h.RequestId == id));
    }

    // ── Missing-token contract: apply cannot bypass preview ──────────────────────────────────

    private static async Task AssertMissingTokenRefusedAsync(Action<LegacyMonetaryScaleRepairRequest> drop)
    {
        using var ctx = NewContext();
        var id = await SeedAsync(ctx);
        var body = await TokenIntentAsync(ctx, id, Wrong, Correct);
        drop(body); // null out one required token

        var result = await new LegacyMonetaryScaleRepairService(ctx).ApplyAsync(id, body, Actor, "Op");
        Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.Refused, result.Status);
        Assert.Contains("pré-visualização", result.Message);
        Assert.Equal(0, await ctx.Set<RequestFieldChangeHistory>().CountAsync(h => h.RequestId == id));
    }

    [Fact] public Task Refuses_apply_missing_request_rowversion_token() => AssertMissingTokenRefusedAsync(b => b.ExpectedRequestRowVersion = null);
    [Fact] public Task Refuses_apply_missing_pogroup_rowversion_token() => AssertMissingTokenRefusedAsync(b => b.ExpectedPoGroupRowVersion = null);
    [Fact] public Task Refuses_apply_missing_line_fingerprint_token() => AssertMissingTokenRefusedAsync(b => b.ExpectedLineFingerprint = null);
    [Fact] public Task Refuses_apply_missing_payment_fingerprint_token() => AssertMissingTokenRefusedAsync(b => b.ExpectedPaymentFingerprint = null);

    // ── Refusals (safety gates); each must leave the data untouched ──────────────────────────

    private static async Task AssertRefusedAsync(
        Action<Request, RequestLineItem, RequestPoGroup, RequestPayment>? mutate,
        decimal? expCurrent = null, decimal? expCorrect = null)
    {
        using var ctx = NewContext();
        var id = await SeedAsync(ctx, mutate);
        var seededTotal = await ctx.RequestLineItems.Where(l => l.RequestId == id).Select(l => l.TotalAmount).FirstAsync();
        var body = await TokenIntentAsync(ctx, id, expCurrent ?? Wrong, expCorrect ?? Correct);

        var result = await new LegacyMonetaryScaleRepairService(ctx).ApplyAsync(id, body, Actor, "Op");

        Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.Refused, result.Status);
        ctx.ChangeTracker.Clear();
        var line = await ctx.RequestLineItems.FirstAsync(l => l.RequestId == id);
        Assert.Equal(seededTotal, line.TotalAmount);
        Assert.Equal(0, await ctx.Set<RequestFieldChangeHistory>().CountAsync(h => h.RequestId == id));
    }

    [Fact] public Task Refuses_when_payment_is_paid() => AssertRefusedAsync((r, l, p, pay) => { pay.PaymentStatus = "COMPLETED"; pay.ActualPaidAmount = Correct; });
    [Fact] public Task Refuses_when_actual_paid_amount_present() => AssertRefusedAsync((r, l, p, pay) => { pay.ActualPaidAmount = 123m; });
    [Fact] public Task Refuses_when_currency_not_aoa() => AssertRefusedAsync((r, l, p, pay) => { pay.CurrencyCode = "USD"; });
    [Fact] public Task Refuses_when_line_has_iva() => AssertRefusedAsync((r, l, p, pay) => { l.IvaRateId = 1; });
    [Fact] public Task Refuses_when_line_has_percent_discount() => AssertRefusedAsync((r, l, p, pay) => { l.DiscountPercent = 5m; });
    [Fact] public Task Refuses_when_status_not_payment_scheduled() => AssertRefusedAsync((r, l, p, pay) => { r.Status!.Code = "PAID"; });
    [Fact] public Task Refuses_when_expected_current_total_mismatch() => AssertRefusedAsync(null, expCurrent: 123m);
    [Fact] public Task Refuses_when_expected_correct_total_mismatch() => AssertRefusedAsync(null, expCorrect: 999m);

    [Fact]
    public Task Refuses_a_legitimate_positive_discount_line() // not the defect fingerprint
        => AssertRefusedAsync((r, l, p, pay) =>
        {
            l.DiscountAmount = 100m;
            l.TotalAmount = 4000m * 4600m - 100m;
            r.EstimatedTotalAmount = l.TotalAmount; r.ApprovedTotalAmount = l.TotalAmount;
            p.TotalAmount = l.TotalAmount; pay.PlannedAmount = l.TotalAmount;
        }, expCurrent: 18_399_900m, expCorrect: 18_400_000m);

    [Fact]
    public async Task Refuses_when_more_than_one_active_line()
    {
        using var ctx = NewContext();
        var id = await SeedAsync(ctx);
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = id, LineNumber = 2, Description = "extra",
            Quantity = 1m, UnitPrice = 1m, TotalAmount = 1m, IsDeleted = false, CreatedAtUtc = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var body = await TokenIntentAsync(ctx, id, Wrong, Correct);
        var result = await new LegacyMonetaryScaleRepairService(ctx).ApplyAsync(id, body, Actor, "Op");
        Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.Refused, result.Status);
    }

    [Fact]
    public async Task Missing_reason_is_refused()
    {
        using var ctx = NewContext();
        var id = await SeedAsync(ctx);
        var body = await TokenIntentAsync(ctx, id, Wrong, Correct);
        body.Reason = "  ";
        var result = await new LegacyMonetaryScaleRepairService(ctx).ApplyAsync(id, body, Actor, "Op");
        Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.Refused, result.Status);
    }
}

/// <summary>
/// Controller-level pins: SysAdmin authorization, the preview-token contract, and — critically — that
/// the corrective audit attributes the repair to the AUTHENTICATED caller's Portal UserId (never the
/// request creator, never a fallback), and refuses when the principal cannot be mapped to a Portal user.
/// </summary>
public class AdminRepairsControllerTests
{
    private const decimal Wrong = 18_400_000_000m;
    private const decimal Correct = 18_400_000m;
    private const decimal BadDiscount = -18_381_600_000m;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static AdminRepairsController BuildController(ApplicationDbContext ctx, string role, Guid callerUserId)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, callerUserId.ToString()),
            new Claim(ClaimTypes.Role, role),
        }, "test"));
        return new AdminRepairsController(ctx)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = claims } },
        };
    }

    /// <summary>Seeds the defect with a specific request creator, plus the given caller users.</summary>
    private static async Task<Guid> SeedAsync(ApplicationDbContext ctx, Guid creatorId, params User[] users)
    {
        ctx.Add(new RequestStatus { Id = 14, Code = "PAYMENT_SCHEDULED", Name = "Pagamento Agendado" });
        ctx.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        foreach (var u in users) ctx.Users.Add(u);

        var reqId = Guid.NewGuid();
        ctx.Requests.Add(new Request
        {
            Id = reqId, RequestNumber = "REQ-11/08/2026-228", Title = "Pagamento", StatusId = 14, CurrencyId = 1,
            DiscountAmount = 0m, EstimatedTotalAmount = Wrong, ApprovedTotalAmount = Wrong, ApprovedCurrencyCode = "AOA",
            CreatedByUserId = creatorId, CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
        });
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = reqId, LineNumber = 1, Description = "Palete",
            Quantity = 4000m, UnitPrice = 4600m, DiscountAmount = BadDiscount, TotalAmount = Wrong, CurrencyId = 1,
            IsDeleted = false, CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
        });
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = reqId, TotalAmount = Wrong, CurrencyCode = "AOA",
            Status = "PAYMENT_SCHEDULED", PurchaseOrderNumber = "ECF 2026/107",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-9), CreatedByUserId = creatorId,
        });
        ctx.RequestPayments.Add(new RequestPayment
        {
            Id = 1, RequestId = reqId, PaymentType = "FINAL_BALANCE", PlannedAmount = Wrong, CurrencyCode = "AOA",
            PaymentStatus = "SCHEDULED", ActualPaidAmount = null, CreatedByUserId = creatorId, CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
        });
        await ctx.SaveChangesAsync();
        return reqId;
    }

    private static async Task<LegacyMonetaryScaleRepairRequest> TokenBodyAsync(ApplicationDbContext ctx, Guid reqId)
    {
        var p = await new LegacyMonetaryScaleRepairService(ctx).PreviewAsync(reqId, Wrong, Correct);
        return new LegacyMonetaryScaleRepairRequest
        {
            ExpectedCurrentTotal = Wrong, ExpectedCorrectTotal = Correct, Reason = "correção",
            ExpectedRequestRowVersion = p!.RequestRowVersion, ExpectedPoGroupRowVersion = p.PoGroupRowVersion,
            ExpectedLineFingerprint = p.LineFingerprint, ExpectedPaymentFingerprint = p.PaymentFingerprint,
        };
    }

    private static async Task<Guid?> CorrectionActorAsync(ApplicationDbContext ctx, Guid reqId) =>
        (await ctx.RequestStatusHistories.FirstOrDefaultAsync(h => h.RequestId == reqId && h.ActionTaken == "FINANCIAL_CORRECTION"))?.ActorUserId;

    [Fact]
    public async Task Audit_actor_is_the_authenticated_admin_A_not_the_creator()
    {
        using var ctx = NewContext();
        var adminA = Guid.NewGuid();
        var creator = Guid.NewGuid();
        var reqId = await SeedAsync(ctx, creator,
            new User { Id = adminA, FullName = "Admin A", Email = "a@t.local" },
            new User { Id = creator, FullName = "Creator", Email = "c@t.local" });

        var controller = BuildController(ctx, RoleConstants.SystemAdministrator, adminA);
        var result = await controller.LegacyMonetaryScale(reqId, confirm: true, await TokenBodyAsync(ctx, reqId));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(adminA, await CorrectionActorAsync(ctx, reqId));
        Assert.NotEqual(creator, await CorrectionActorAsync(ctx, reqId));
    }

    [Fact]
    public async Task Audit_actor_is_the_authenticated_admin_B()
    {
        using var ctx = NewContext();
        var adminB = Guid.NewGuid();
        var reqId = await SeedAsync(ctx, Guid.NewGuid(), new User { Id = adminB, FullName = "Admin B", Email = "b@t.local" });

        var controller = BuildController(ctx, RoleConstants.SystemAdministrator, adminB);
        var result = await controller.LegacyMonetaryScale(reqId, confirm: true, await TokenBodyAsync(ctx, reqId));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(adminB, await CorrectionActorAsync(ctx, reqId));
    }

    [Fact]
    public async Task Apply_refused_when_principal_not_a_portal_user_and_nothing_is_written()
    {
        using var ctx = NewContext();
        // Seed defect but do NOT seed the caller as a User.
        var reqId = await SeedAsync(ctx, Guid.NewGuid());
        var body = await TokenBodyAsync(ctx, reqId);

        var controller = BuildController(ctx, RoleConstants.SystemAdministrator, Guid.NewGuid()); // unknown principal
        var result = await controller.LegacyMonetaryScale(reqId, confirm: true, body);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("utilizador autenticado", System.Text.Json.JsonSerializer.Serialize(bad.Value));
        // No financial write, no correction audit.
        ctx.ChangeTracker.Clear();
        Assert.Equal(Wrong, (await ctx.RequestLineItems.FirstAsync(l => l.RequestId == reqId)).TotalAmount);
        Assert.Null(await CorrectionActorAsync(ctx, reqId));
        Assert.Equal(0, await ctx.Set<RequestFieldChangeHistory>().CountAsync(h => h.RequestId == reqId));
    }

    [Fact]
    public async Task Apply_confirm_true_without_preview_tokens_is_bad_request()
    {
        using var ctx = NewContext();
        var admin = Guid.NewGuid();
        var reqId = await SeedAsync(ctx, Guid.NewGuid(), new User { Id = admin, FullName = "Admin", Email = "a@t.local" });
        var controller = BuildController(ctx, RoleConstants.SystemAdministrator, admin);
        var body = new LegacyMonetaryScaleRepairRequest
        {
            ExpectedCurrentTotal = Wrong, ExpectedCorrectTotal = Correct, Reason = "sem preview", // no tokens
        };

        var result = await controller.LegacyMonetaryScale(reqId, confirm: true, body);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<LegacyMonetaryScaleRepairResult>(bad.Value);
        Assert.Equal(LegacyMonetaryScaleRepairResult.Statuses.Refused, payload.Status);
        Assert.Contains("pré-visualização", payload.Message);
    }

    [Fact]
    public async Task Apply_without_sysadmin_is_forbidden_and_writes_nothing()
    {
        using var ctx = NewContext();
        var buyer = Guid.NewGuid();
        var reqId = await SeedAsync(ctx, Guid.NewGuid(), new User { Id = buyer, FullName = "Buyer", Email = "buy@t.local" });
        var controller = BuildController(ctx, "Buyer", buyer);

        var result = await controller.LegacyMonetaryScale(reqId, confirm: true, await TokenBodyAsync(ctx, reqId));
        Assert.IsType<ForbidResult>(result);
        Assert.Null(await CorrectionActorAsync(ctx, reqId));
    }
}
