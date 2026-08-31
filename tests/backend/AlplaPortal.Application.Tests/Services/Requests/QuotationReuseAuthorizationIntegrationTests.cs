using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Option C — cancelled-batch quotation reuse. SQL Server (LocalDB) integration tests that mirror
/// the real REQ-13/07/2026-024 scenario: two quotations (AB/QD) mapping the same request lines;
/// Batch #1 CANCELLED selected the AB items; a later batch uses the QD items.
///
/// Rule under test: an item selected in a CANCELLED batch is NOT automatically eligible again —
/// it needs an explicit, active, per-item Buyer authorization for that exact source batch, which
/// is consumed atomically when a new batch uses the item. History and the cancelled batch are
/// never modified. Skipped when LocalDB is unavailable; each test seeds and cleans its own data.
/// </summary>
public class QuotationReuseAuthorizationIntegrationTests
{
    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();

    private static DbContextOptions<ApplicationDbContext> Options() => IntegrationTestDatabase.CreateOptions();

    // ── Seed: mirrors REQ-024 (2 lines; AB items in CANCELLED Batch #1; QD items free) ──
    private sealed record Seed(
        Guid RequestId, Guid Actor, int SupplierId,
        Guid Line1, Guid Line2,
        Guid QuotationAb, Guid AbItem1, Guid AbItem2,
        Guid QuotationQd, Guid QdItem1, Guid QdItem2,
        Guid CancelledBatch1);

    private static async Task<Seed?> SeedMirrorAsync(bool assignLinesToActiveBatch = false)
    {
        await using var ctx = new ApplicationDbContext(Options());
        var actor = await ctx.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
        if (actor == Guid.Empty) return null;
        var supplierId = await ctx.Suppliers.AsNoTracking().Select(s => s.Id).FirstOrDefaultAsync();
        if (supplierId == 0) return null;

        var statusId = await ctx.RequestStatuses.Where(s => s.Code == "WAITING_QUOTATION").Select(s => s.Id).FirstAsync();
        var typeId = await ctx.RequestTypes.Where(t => t.Code == "QUOTATION").Select(t => t.Id).FirstAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST_REUSE_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZT-RU-" + Guid.NewGuid().ToString("N")[..8],
            StatusId = statusId,
            RequestTypeId = typeId,
            DepartmentId = 4,
            CompanyId = 1,
            PlantId = 1,
            CurrencyId = 1,
            RequesterId = actor,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);

        RequestLineItem NewLine(int n) => new()
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            LineNumber = n,
            Description = $"ZZTEST line {n}",
            Quantity = 1,
            UnitPrice = 0,
            TotalAmount = 0,
            IsDeleted = false,
            QuotationLifecycleStatus = assignLinesToActiveBatch ? "BATCH_ASSIGNED" : null,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actor
        };
        var l1 = NewLine(1); var l2 = NewLine(2);
        ctx.RequestLineItems.AddRange(l1, l2);

        Quotation NewQuotation(string doc) => new()
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierId = supplierId,
            SupplierNameSnapshot = "ZZTEST Supplier",
            DocumentNumber = doc,
            Currency = "AOA",
            TotalAmount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };
        var ab = NewQuotation("ZZ-AB");
        var qd = NewQuotation("ZZ-QD");
        ctx.Quotations.AddRange(ab, qd);

        QuotationItem NewItem(Quotation q, int n, Guid mapped, decimal total) => new()
        {
            Id = Guid.NewGuid(),
            QuotationId = q.Id,
            LineNumber = n,
            Description = $"{q.DocumentNumber} item {n}",
            Quantity = 1,
            UnitPrice = total,
            GrossSubtotal = total,
            LineTotal = total,
            MappedRequestLineItemId = mapped,
            ReconciliationStatus = "MAPPED"
        };
        var ab1 = NewItem(ab, 1, l1.Id, 43263m);
        var ab2 = NewItem(ab, 2, l2.Id, 42180m);
        var qd1 = NewItem(qd, 1, l1.Id, 17875.20m);
        var qd2 = NewItem(qd, 2, l2.Id, 16917.60m);
        ctx.QuotationItems.AddRange(ab1, ab2, qd1, qd2);

        // Batch #1 (CANCELLED) selected the AB items — historical links preserved.
        var batch1 = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            BatchNumber = 1,
            Status = "CANCELLED",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
            CreatedByUserId = actor
        };
        ctx.ApprovalBatches.Add(batch1);
        ctx.ApprovalBatchItems.AddRange(
            new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch1.Id, RequestLineItemId = l1.Id, SelectedQuotationItemId = ab1.Id, CreatedAtUtc = DateTime.UtcNow.AddHours(-2) },
            new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch1.Id, RequestLineItemId = l2.Id, SelectedQuotationItemId = ab2.Id, CreatedAtUtc = DateTime.UtcNow.AddHours(-2) });

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor, supplierId, l1.Id, l2.Id, ab.Id, ab1.Id, ab2.Id, qd.Id, qd1.Id, qd2.Id, batch1.Id);
    }

    private static async Task CleanupAsync(Guid requestId)
    {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM QuotationReuseAuthorizations WHERE RequestId={0};" +
            "DELETE abi FROM ApprovalBatchItems abi INNER JOIN ApprovalBatches ab ON ab.Id=abi.ApprovalBatchId WHERE ab.RequestId={0};" +
            "DELETE FROM ApprovalBatches WHERE RequestId={0};" +
            "DELETE qi FROM QuotationItems qi INNER JOIN Quotations q ON q.Id=qi.QuotationId WHERE q.RequestId={0};" +
            "DELETE FROM Quotations WHERE RequestId={0};" +
            "DELETE FROM RequestStatusHistories WHERE RequestId={0};" +
            "DELETE FROM RequestLineItems WHERE RequestId={0};" +
            "DELETE FROM Requests WHERE Id={0};", requestId);
    }

    private static RequestsController BuildRequestsController(ApplicationDbContext ctx, Guid actorId, params string[] roles)
    {
        var adminLog = new AdminLogWriter(
            new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance);
        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object, adminLog, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object,
            new Mock<ILineItemFactory>().Object, new Mock<IRequestLineItemSubmissionValidator>().Object,
            new QuotationItemEligibilityService(ctx), new BatchExtraItemDecisionService(ctx),
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Microsoft.Extensions.Options.Options.Create(new AlplaPortal.Domain.Configuration.PostPaymentCompletionOptions()));
        SetUser(controller, actorId, roles);
        return controller;
    }

    private static ApprovalBatchController BuildBatchController(ApplicationDbContext ctx, Guid actorId)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new ApprovalRoutingResultDto { Managers = { new AreaManagerDto { UserId = actorId, FullName = "ZZ Manager" } } });
        var statusSync = new Mock<IRequestStatusSyncService>();

        var controller = new ApprovalBatchController(
            ctx, NullLogger<ApprovalBatchController>.Instance,
            statusSync.Object, new Mock<IGroupBuilderService>().Object, routing.Object,
            new QuotationItemEligibilityService(ctx), new BatchExtraItemDecisionService(ctx),
            new AdjustmentCycleService(ctx), new Mock<IWorkflowNotificationOrchestrator>().Object);
        SetUser(controller, actorId, "System Administrator");
        return controller;
    }

    private static void SetUser(ControllerBase controller, Guid actorId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
    }

    private static string? GetCode(ProblemDetails pd)
        => pd.Extensions.TryGetValue("code", out var c) ? c?.ToString() : null;

    [Fact] // Cenários 1,2,3,22,24,25: bloqueio derivado + QD normal + independência entre cotações do mesmo fornecedor
    public async Task Req024Mirror_AbItemsBlocked_QdItemsEligible()
    {
        if (!CanConnect()) return;
        var s = await SeedMirrorAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var svc = new QuotationItemEligibilityService(ctx);
            var map = await svc.GetEligibilityMapAsync(s.RequestId);

            // AB (Batch #1 cancelado): bloqueados, com proveniência do lote #1
            foreach (var abId in new[] { s.AbItem1, s.AbItem2 })
            {
                Assert.True(map[abId].IsReuseBlocked);
                Assert.False(map[abId].IsEligible);
                Assert.Equal(QuotationItemEligibilityReasons.ReuseAuthorizationRequired, map[abId].ReasonCode);
                Assert.Equal(s.CancelledBatch1, map[abId].SourceCancelledBatchId);
                Assert.Equal(1, map[abId].SourceCancelledBatchNumber);
            }
            // QD (mesmo fornecedor, nunca em lote cancelado): elegível normal
            foreach (var qdId in new[] { s.QdItem1, s.QdItem2 })
            {
                Assert.True(map[qdId].IsEligible);
                Assert.False(map[qdId].IsReuseBlocked);
                Assert.Equal(QuotationItemEligibilityReasons.EligibleNormal, map[qdId].ReasonCode);
            }
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    [Fact] // Cenário 12: CreateBatch direto com item bloqueado → 409 QUOTATION_REUSE_NOT_AUTHORIZED
    public async Task CreateBatch_WithBlockedItem_Returns409()
    {
        if (!CanConnect()) return;
        var s = await SeedMirrorAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());
            var controller = BuildBatchController(ctx, s.Actor);

            var result = await controller.CreateBatch(s.RequestId, new CreateApprovalBatchDto
            {
                Items = new List<BatchItemDto>
                {
                    new() { RequestLineItemId = s.Line1, Candidates = { new() { QuotationItemId = s.AbItem1 } } },
                    new() { RequestLineItemId = s.Line2, Candidates = { new() { QuotationItemId = s.AbItem2 } } }
                }
            });

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var pd = Assert.IsType<ProblemDetails>(conflict.Value);
            Assert.Equal("QUOTATION_REUSE_NOT_AUTHORIZED", GetCode(pd));
            // nada persistido
            Assert.Equal(1, await ctx.ApprovalBatches.CountAsync(b => b.RequestId == s.RequestId)); // só o cancelado
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    [Fact] // Cenários 5,6,7,8,9,10,11,26: endpoint de autorização (roles, reason, parcial, duplicada, história)
    public async Task AuthorizeReuse_Validations_PartialAuthorization_AndAudit()
    {
        if (!CanConnect()) return;
        var s = await SeedMirrorAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(Options());

            // (5) role exigida: sem Buyer/Admin → 403
            var noRole = BuildRequestsController(ctx, s.Actor, "Requester");
            var forbidden = await noRole.AuthorizeQuotationReuse(s.RequestId, s.QuotationAb,
                new AuthorizeQuotationReuseDto { QuotationItemIds = { s.AbItem1 }, Reason = "x" });
            Assert.Equal(403, ((ObjectResult)forbidden).StatusCode);

            var buyer = BuildRequestsController(ctx, s.Actor, "Buyer");

            // (6) reason obrigatória
            var noReason = await buyer.AuthorizeQuotationReuse(s.RequestId, s.QuotationAb,
                new AuthorizeQuotationReuseDto { QuotationItemIds = { s.AbItem1 }, Reason = "   " });
            Assert.IsType<BadRequestObjectResult>(noReason);

            // (7) item nunca usado em lote cancelado → 400
            var notUsed = await buyer.AuthorizeQuotationReuse(s.RequestId, s.QuotationQd,
                new AuthorizeQuotationReuseDto { QuotationItemIds = { s.QdItem1 }, Reason = "motivo válido" });
            Assert.IsType<BadRequestObjectResult>(notUsed);

            // (8/9) autorização PARCIAL: apenas AbItem1 (per-item record)
            var ok = await buyer.AuthorizeQuotationReuse(s.RequestId, s.QuotationAb,
                new AuthorizeQuotationReuseDto { QuotationItemIds = { s.AbItem1 }, Reason = "Reuso do item 1 após cancelamento" });
            Assert.IsType<OkObjectResult>(ok);

            await using var verify = new ApplicationDbContext(Options());
            var auths = await verify.QuotationReuseAuthorizations.AsNoTracking()
                .Where(a => a.RequestId == s.RequestId).ToListAsync();
            var auth1 = Assert.Single(auths); // um registro por item autorizado
            Assert.Equal(s.AbItem1, auth1.QuotationItemId);
            Assert.Equal(s.CancelledBatch1, auth1.SourceApprovalBatchId);
            Assert.True(auth1.IsActive);

            // (11) item autorizado elegível; o não autorizado continua bloqueado (parcial)
            var svc = new QuotationItemEligibilityService(verify);
            var map = await svc.GetEligibilityMapAsync(s.RequestId);
            Assert.True(map[s.AbItem1].IsReuseAuthorized);
            Assert.True(map[s.AbItem1].IsEligible);
            Assert.True(map[s.AbItem2].IsReuseBlocked);

            // (10) duplicada ativa → 409
            var dup = await buyer.AuthorizeQuotationReuse(s.RequestId, s.QuotationAb,
                new AuthorizeQuotationReuseDto { QuotationItemIds = { s.AbItem1 }, Reason = "de novo" });
            var dupConflict = Assert.IsType<ConflictObjectResult>(dup);
            Assert.Equal("REUSE_ALREADY_AUTHORIZED", GetCode(Assert.IsType<ProblemDetails>(dupConflict.Value)));

            // (26) história de autorização
            var history = await verify.RequestStatusHistories.AsNoTracking()
                .Where(h => h.RequestId == s.RequestId && h.ActionTaken == "QUOTATION_REUSE_AUTHORIZED").ToListAsync();
            Assert.Single(history);
            Assert.Contains("ZZ-AB", history[0].Comment);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    [Fact] // Cenários 15,16,17,19,20,21,26: consumo atômico no CreateBatch + histórico + lote cancelado intacto
    public async Task CreateBatch_WithAuthorizedItems_ConsumesAuthorizations_AndPreservesHistory()
    {
        if (!CanConnect()) return;
        var s = await SeedMirrorAsync();
        if (s == null) return;
        try
        {
            // Autorizar os DOIS itens AB
            await using (var ctx0 = new ApplicationDbContext(Options()))
            {
                var buyer = BuildRequestsController(ctx0, s.Actor, "Buyer");
                var ok = await buyer.AuthorizeQuotationReuse(s.RequestId, s.QuotationAb,
                    new AuthorizeQuotationReuseDto { QuotationItemIds = { s.AbItem1, s.AbItem2 }, Reason = "Reautorizar cotação AB completa" });
                Assert.IsType<OkObjectResult>(ok);
            }

            // (17) falha do lote NÃO consome: dto com um item inválido → 400 antes de persistir
            await using (var ctxFail = new ApplicationDbContext(Options()))
            {
                var batchCtrl = BuildBatchController(ctxFail, s.Actor);
                var bad = await batchCtrl.CreateBatch(s.RequestId, new CreateApprovalBatchDto
                {
                    Items = new List<BatchItemDto>
                    {
                        new() { RequestLineItemId = s.Line1, Candidates = { new() { QuotationItemId = s.AbItem1 } } },
                        new() { RequestLineItemId = Guid.NewGuid(), Candidates = { new() { QuotationItemId = s.AbItem2 } } } // linha inexistente
                    }
                });
                Assert.IsType<BadRequestObjectResult>(bad);
            }
            await using (var check = new ApplicationDbContext(Options()))
            {
                Assert.Equal(2, await check.QuotationReuseAuthorizations
                    .CountAsync(a => a.RequestId == s.RequestId && a.IsActive && a.ConsumedAtUtc == null)); // intactas
            }

            // Criar o lote com os itens autorizados → sucesso + consumo atômico
            Guid newBatchId;
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var batchCtrl = BuildBatchController(ctx, s.Actor);
                var okResult = await batchCtrl.CreateBatch(s.RequestId, new CreateApprovalBatchDto
                {
                    Items = new List<BatchItemDto>
                    {
                        new() { RequestLineItemId = s.Line1, Candidates = { new() { QuotationItemId = s.AbItem1 } } },
                        new() { RequestLineItemId = s.Line2, Candidates = { new() { QuotationItemId = s.AbItem2 } } }
                    }
                });
                Assert.IsType<OkObjectResult>(okResult);
                newBatchId = await ctx.ApprovalBatches.AsNoTracking()
                    .Where(b => b.RequestId == s.RequestId && b.Status != "CANCELLED")
                    .Select(b => b.Id).SingleAsync();
            }

            await using var verify = new ApplicationDbContext(Options());
            // (15) autorizações consumidas atomicamente pelo novo lote
            var auths = await verify.QuotationReuseAuthorizations.AsNoTracking()
                .Where(a => a.RequestId == s.RequestId).ToListAsync();
            Assert.Equal(2, auths.Count);
            Assert.All(auths, a =>
            {
                Assert.False(a.IsActive);
                Assert.Equal(newBatchId, a.ConsumedByApprovalBatchId);
                Assert.NotNull(a.ConsumedAtUtc);
            });

            // (16) consumida não é reutilizável: nova seleção sem novo ciclo → blocked AUTHORIZATION_CONSUMED
            var svc = new QuotationItemEligibilityService(verify);
            var (blocked, _) = await svc.ValidateSelectionAsync(s.RequestId, new[] { s.AbItem1 });
            var b1 = Assert.Single(blocked);
            Assert.Equal(QuotationItemEligibilityReasons.AuthorizationConsumed, b1.ReasonCode);

            // (19/20) lote cancelado intacto, com SelectedQuotationItemId histórico preservado
            var cancelled = await verify.ApprovalBatches.AsNoTracking().Include(b => b.Items)
                .SingleAsync(b => b.Id == s.CancelledBatch1);
            Assert.Equal("CANCELLED", cancelled.Status);
            Assert.Equal(2, cancelled.Items.Count);
            Assert.Contains(cancelled.Items, i => i.SelectedQuotationItemId == s.AbItem1);

            // (21) novo lote com ApprovalBatchItems próprios (frescos)
            var fresh = await verify.ApprovalBatchItems.AsNoTracking()
                .Where(i => i.ApprovalBatchId == newBatchId).ToListAsync();
            Assert.Equal(2, fresh.Count);
            Assert.All(fresh, i => Assert.NotEqual(s.CancelledBatch1, i.ApprovalBatchId));

            // (26) história de consumo
            var consumedHistory = await verify.RequestStatusHistories.AsNoTracking()
                .CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "QUOTATION_REUSED_IN_NEW_BATCH");
            Assert.Equal(2, consumedHistory);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    [Fact] // Cenário 18: revogação remove elegibilidade; consumida não pode ser revogada
    public async Task RevokeReuse_RemovesEligibility_AndConsumedCannotBeRevoked()
    {
        if (!CanConnect()) return;
        var s = await SeedMirrorAsync();
        if (s == null) return;
        try
        {
            Guid authId;
            await using (var ctx0 = new ApplicationDbContext(Options()))
            {
                var buyer = BuildRequestsController(ctx0, s.Actor, "Buyer");
                await buyer.AuthorizeQuotationReuse(s.RequestId, s.QuotationAb,
                    new AuthorizeQuotationReuseDto { QuotationItemIds = { s.AbItem1 }, Reason = "para revogar" });
                authId = await ctx0.QuotationReuseAuthorizations.AsNoTracking()
                    .Where(a => a.RequestId == s.RequestId).Select(a => a.Id).SingleAsync();
            }

            await using var ctx = new ApplicationDbContext(Options());
            var buyer2 = BuildRequestsController(ctx, s.Actor, "Buyer");
            var revoked = await buyer2.RevokeQuotationReuse(s.RequestId, authId,
                new RevokeQuotationReuseDto { Reason = "Cotação desatualizada" });
            Assert.IsType<OkObjectResult>(revoked);

            await using var verify = new ApplicationDbContext(Options());
            var auth = await verify.QuotationReuseAuthorizations.AsNoTracking().SingleAsync(a => a.Id == authId);
            Assert.False(auth.IsActive);
            Assert.NotNull(auth.RevokedAtUtc);
            Assert.Equal("Cotação desatualizada", auth.RevocationReason);

            // Elegibilidade volta a bloqueado (AUTHORIZATION_REVOKED)
            var svc = new QuotationItemEligibilityService(verify);
            var map = await svc.GetEligibilityMapAsync(s.RequestId);
            Assert.True(map[s.AbItem1].IsReuseBlocked);
            Assert.Equal(QuotationItemEligibilityReasons.AuthorizationRevoked, map[s.AbItem1].ReasonCode);

            // Revogar novamente → 409 (inativa)
            var again = await buyer2.RevokeQuotationReuse(s.RequestId, authId,
                new RevokeQuotationReuseDto { Reason = "de novo" });
            Assert.IsType<ConflictObjectResult>(again);

            // História de revogação
            Assert.Equal(1, await verify.RequestStatusHistories.AsNoTracking()
                .CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "QUOTATION_REUSE_REVOKED"));
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    [Fact] // Cenário 13 + "replace winners": UpdateBatch (ajuste) trocando um vencedor QD por item AB bloqueado → 409
    public async Task UpdateBatch_SwappingWinnerToBlockedItem_Returns409_AndBatchUnchanged()
    {
        if (!CanConnect()) return;
        var s = await SeedMirrorAsync(assignLinesToActiveBatch: true);
        if (s == null) return;
        try
        {
            // Batch ativo em AREA_ADJUSTMENT com os vencedores QD (espelha o Batch #2 em rework)
            Guid batch2Id;
            await using (var ctx0 = new ApplicationDbContext(Options()))
            {
                var batch2 = new ApprovalBatch
                {
                    Id = Guid.NewGuid(),
                    RequestId = s.RequestId,
                    BatchNumber = 2,
                    Status = "AREA_ADJUSTMENT",
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30),
                    CreatedByUserId = s.Actor
                };
                ctx0.ApprovalBatches.Add(batch2);
                ctx0.ApprovalBatchItems.AddRange(
                    new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch2.Id, RequestLineItemId = s.Line1, SelectedQuotationItemId = s.QdItem1, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30) },
                    new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch2.Id, RequestLineItemId = s.Line2, SelectedQuotationItemId = s.QdItem2, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30) });
                await ctx0.SaveChangesAsync();
                batch2Id = batch2.Id;
            }

            await using var ctx = new ApplicationDbContext(Options());
            var controller = BuildBatchController(ctx, s.Actor);
            var result = await controller.UpdateBatch(s.RequestId, batch2Id, new UpdateApprovalBatchDto
            {
                Items = new List<BatchItemDto>
                {
                    new() { RequestLineItemId = s.Line1, Candidates = { new() { QuotationItemId = s.AbItem1 } } }, // troca p/ AB bloqueado
                    new() { RequestLineItemId = s.Line2, Candidates = { new() { QuotationItemId = s.QdItem2 } } }
                }
            });

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.Equal("QUOTATION_REUSE_NOT_AUTHORIZED", GetCode(Assert.IsType<ProblemDetails>(conflict.Value)));

            // Batch #2 não mutado: vencedores QD preservados
            await using var verify = new ApplicationDbContext(Options());
            var winners = await verify.ApprovalBatchItems.AsNoTracking()
                .Where(i => i.ApprovalBatchId == batch2Id)
                .Select(i => i.SelectedQuotationItemId).ToListAsync();
            Assert.Contains(s.QdItem1, winners);
            Assert.Contains(s.QdItem2, winners);
            Assert.DoesNotContain(s.AbItem1, winners);
        }
        finally { await CleanupAsync(s.RequestId); }
    }

    [Fact] // Cenário 23: um segundo lote cancelado (itens QD) exige autorização separada — AB não desbloqueia QD
    public async Task TwoCancelledBatches_RequireSeparateAuthorizations()
    {
        if (!CanConnect()) return;
        var s = await SeedMirrorAsync();
        if (s == null) return;
        try
        {
            // Segundo lote cancelado usando os itens QD
            await using (var ctx0 = new ApplicationDbContext(Options()))
            {
                var batch2 = new ApprovalBatch
                {
                    Id = Guid.NewGuid(),
                    RequestId = s.RequestId,
                    BatchNumber = 2,
                    Status = "CANCELLED",
                    CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
                    CreatedByUserId = s.Actor
                };
                ctx0.ApprovalBatches.Add(batch2);
                ctx0.ApprovalBatchItems.AddRange(
                    new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch2.Id, RequestLineItemId = s.Line1, SelectedQuotationItemId = s.QdItem1, CreatedAtUtc = DateTime.UtcNow.AddHours(-1) },
                    new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch2.Id, RequestLineItemId = s.Line2, SelectedQuotationItemId = s.QdItem2, CreatedAtUtc = DateTime.UtcNow.AddHours(-1) });
                await ctx0.SaveChangesAsync();

                // Autorizar apenas a AB
                var buyer = BuildRequestsController(ctx0, s.Actor, "Buyer");
                await buyer.AuthorizeQuotationReuse(s.RequestId, s.QuotationAb,
                    new AuthorizeQuotationReuseDto { QuotationItemIds = { s.AbItem1, s.AbItem2 }, Reason = "Só a AB" });
            }

            await using var verify = new ApplicationDbContext(Options());
            var svc = new QuotationItemEligibilityService(verify);
            var map = await svc.GetEligibilityMapAsync(s.RequestId);

            Assert.True(map[s.AbItem1].IsReuseAuthorized);   // AB autorizada
            Assert.True(map[s.QdItem1].IsReuseBlocked);       // QD do lote #2 cancelado continua bloqueada
            Assert.Equal(2, map[s.QdItem1].SourceCancelledBatchNumber);
        }
        finally { await CleanupAsync(s.RequestId); }
    }
}
