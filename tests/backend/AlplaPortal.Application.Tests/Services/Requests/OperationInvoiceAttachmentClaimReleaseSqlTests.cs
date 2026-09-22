using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

using Agg = RequestConstants.OperationInvoiceStatuses;
using Doc = RequestConstants.OperationInvoiceDocumentStatuses;

/// <summary>
/// v2.245.8 hardening — REAL-PROVIDER arbitration pin (SQL Server LocalDB) for the claim/release race
/// over one OPERATION_INVOICE attachment. The InMemory suites prove the POLICY; this proves the
/// MECHANISM: both the claim (<c>Create</c>) and the release take <c>UPDLOCK, ROWLOCK</c> on the
/// attachment row FIRST, inside their transaction, so the second writer blocks until the first commits
/// and then reads the committed outcome — never an invoice referencing a released attachment, never a
/// released attachment that an invoice claims.
///
/// <para>Each scenario holds the row lock from an independent connection (the exact statement the
/// controllers execute), starts the competing endpoint, proves it is BLOCKED (does not complete while
/// the lock is held), commits the holder's decision, and asserts the deterministic outcome.</para>
///
/// <para>Requires SQL Server LocalDB (MSSQLLocalDB) — present on the project's Windows dev and build
/// machines; CI does not execute tests on non-Windows runners.</para>
/// </summary>
public class OperationInvoiceAttachmentClaimReleaseSqlTests
{
    private static DbContextOptions<ApplicationDbContext> SqlOptions(string dbName) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                $@"Server=(localdb)\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;" +
                "MultipleActiveResultSets=true;Connection Timeout=60")
            .Options;

    private static OperationInvoicesController Build(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new OperationInvoicesController(
            ctx,
            NullLogger<OperationInvoicesController>.Instance,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            new OperationInvoiceCoverageService(ctx));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, RoleConstants.Finance)
                }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid ActorId, int SupplierId, Guid AttachmentId);

    private static async Task<Seed> SeedAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var ctx = new ApplicationDbContext(options);
        await ctx.Database.EnsureCreatedAsync();

        var actor = new User { Id = Guid.NewGuid(), FullName = "Claim Tester", Email = "claim@test.local" };
        var department = new Department { Name = "ZZTEST Dept" };
        var company = new Company { Name = "ZZTEST Co" };
        var supplier = new Supplier { Name = "ZZTEST Supplier", TaxId = "500100200" };
        ctx.Users.Add(actor); ctx.Departments.Add(department); ctx.Companies.Add(company); ctx.Suppliers.Add(supplier);

        var requestType = await ctx.RequestTypes.FirstOrDefaultAsync(t => t.Code == RequestConstants.Types.Payment)
            ?? ctx.RequestTypes.Add(new RequestType { Code = RequestConstants.Types.Payment, Name = "Pagamento" }).Entity;
        var requestStatus = await ctx.RequestStatuses.FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.WaitingReceipt)
            ?? ctx.RequestStatuses.Add(new RequestStatus { Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 }).Entity;
        await ctx.SaveChangesAsync();

        var plant = new Plant { Name = "ZZTEST Plant", CompanyId = company.Id };
        ctx.Plants.Add(plant);
        await ctx.SaveChangesAsync();

        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "ZZTEST-CLM-" + Guid.NewGuid().ToString("N")[..8], Title = "ZZTEST claim/release race",
            RequestTypeId = requestType.Id, StatusId = requestStatus.Id, RequesterId = actor.Id,
            DepartmentId = department.Id, CompanyId = company.Id, PlantId = plant.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        };
        ctx.Requests.Add(request);
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, SupplierNameSnapshot = "ZZTEST Supplier", CurrencyCode = "AOA",
            TotalAmount = 250_000m, Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma, OperationInvoiceStatus = Agg.PendingUpload,
            RequiresOperationInvoice = true, ExpectedOperationInvoiceTotal = 250_000m, ExpectedOperationInvoiceCurrency = "AOA",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5), CreatedByUserId = actor.Id
        });
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = request.Id, FileName = "fatura.pdf", FileExtension = ".pdf", FileSizeMBytes = 0.2m,
            FileHash = Guid.NewGuid().ToString("N"), AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/clm-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = actor.Id, UploadedAtUtc = DateTime.UtcNow
        };
        ctx.RequestAttachments.Add(attachment);
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id, supplier.Id, attachment.Id);
    }

    private static SaveOperationInvoiceDto Dto(Seed seed, string number = "FT ZZTEST/1") => new()
    {
        AttachmentId = seed.AttachmentId, SupplierId = seed.SupplierId, DocumentNumber = number, DocumentSeries = "A",
        DocumentDate = new DateTime(2026, 9, 1), Currency = "AOA", NetAmount = 219_298.25m, TaxAmount = 30_701.75m, GrossAmount = 250_000m
    };

    /// <summary>The exact arbitration statement the controllers execute, held by an independent connection.</summary>
    private static Task HoldAttachmentLockAsync(ApplicationDbContext holder, Guid attachmentId) =>
        holder.RequestAttachments
            .FromSqlRaw("SELECT * FROM RequestAttachments WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", attachmentId)
            .AsNoTracking()
            .ToListAsync();

    private static async Task AssertBlockedAsync(Task competitor)
    {
        var finished = await Task.WhenAny(competitor, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.NotSame(competitor, finished);   // still waiting on the row lock
    }

    [Fact]
    public async Task Release_wins_the_blocked_create_sees_the_released_attachment_and_creates_nothing()
    {
        var dbName = "AlplaPortal_ZZTEST_Clm_" + Guid.NewGuid().ToString("N")[..12];
        var options = SqlOptions(dbName);
        try
        {
            var seed = await SeedAsync(options);

            await using var holder = new ApplicationDbContext(options);
            await using var tx = await holder.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            await HoldAttachmentLockAsync(holder, seed.AttachmentId);        // the release's first step

            await using var competitorCtx = new ApplicationDbContext(options);
            var createTask = Build(competitorCtx, seed.ActorId).Create(seed.RequestId, Dto(seed));
            await AssertBlockedAsync(createTask);                            // lock acquired BEFORE eligibility

            // the release's decision commits while the create waits
            await holder.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE RequestAttachments SET IsDeleted = 1 WHERE Id = {seed.AttachmentId}");
            await tx.CommitAsync();

            var result = await createTask;
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("removido ou anulado", Assert.IsType<ProblemDetails>(bad.Value).Detail);

            await using var verify = new ApplicationDbContext(options);
            Assert.Equal(0, await verify.OperationInvoices.CountAsync());
            Assert.True((await verify.RequestAttachments.SingleAsync(a => a.Id == seed.AttachmentId)).IsDeleted);
        }
        finally
        {
            await using var drop = new ApplicationDbContext(options);
            await drop.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Create_wins_the_blocked_release_sees_the_committed_claim_and_writes_nothing()
    {
        var dbName = "AlplaPortal_ZZTEST_Clm_" + Guid.NewGuid().ToString("N")[..12];
        var options = SqlOptions(dbName);
        try
        {
            var seed = await SeedAsync(options);

            await using var holder = new ApplicationDbContext(options);
            await using var tx = await holder.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            await HoldAttachmentLockAsync(holder, seed.AttachmentId);        // the create's first step

            await using var competitorCtx = new ApplicationDbContext(options);
            var releaseTask = Build(competitorCtx, seed.ActorId).ReleaseUnclaimedAttachment(seed.RequestId, seed.AttachmentId);
            await AssertBlockedAsync(releaseTask);                           // lock acquired BEFORE eligibility

            // the claim commits while the release waits
            holder.OperationInvoices.Add(new OperationInvoice
            {
                Id = Guid.NewGuid(), RequestId = seed.RequestId, AttachmentId = seed.AttachmentId, SupplierId = seed.SupplierId,
                DocumentNumber = "FT ZZTEST/1", Currency = "AOA", GrossAmount = 250_000m, Status = Doc.PendingValidation,
                UploadedAtUtc = DateTime.UtcNow, UploadedByUserId = seed.ActorId
            });
            await holder.SaveChangesAsync();
            await tx.CommitAsync();

            var result = await releaseTask;
            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.Equal(OperationInvoicesController.AttachmentClaimedCode, Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);

            await using var verify = new ApplicationDbContext(options);
            Assert.False((await verify.RequestAttachments.SingleAsync(a => a.Id == seed.AttachmentId)).IsDeleted);
            Assert.Equal(1, await verify.OperationInvoices.CountAsync());
            Assert.False(await verify.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "DOCUMENTO REMOVIDO"));
        }
        finally
        {
            await using var drop = new ApplicationDbContext(options);
            await drop.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Lost_success_then_retry_returns_the_same_invoice_on_the_real_provider()
    {
        var dbName = "AlplaPortal_ZZTEST_Clm_" + Guid.NewGuid().ToString("N")[..12];
        var options = SqlOptions(dbName);
        try
        {
            var seed = await SeedAsync(options);
            Guid firstId;
            await using (var a = new ApplicationDbContext(options))
                firstId = Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(await Build(a, seed.ActorId).Create(seed.RequestId, Dto(seed))).Value).Id;
            await using (var b = new ApplicationDbContext(options))
            {
                var retry = Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(await Build(b, seed.ActorId).Create(seed.RequestId, Dto(seed, "FT RETRY"))).Value);
                Assert.Equal(firstId, retry.Id);
                Assert.Equal(1, await b.OperationInvoices.CountAsync());
            }
        }
        finally
        {
            await using var drop = new ApplicationDbContext(options);
            await drop.Database.EnsureDeletedAsync();
        }
    }
}
