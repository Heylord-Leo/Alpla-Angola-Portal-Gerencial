using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// The "one monetary truth" invariant of the v2.229.10 monetary reconciliation: after residual
/// allocation, Σ(item totals) == document GrossAmount, and every downstream amount — request
/// estimated total, payment group total, expected operation-invoice total — carries the same
/// number the supplier document declares. CONSULTIT CCTV Viana02 is the reference case.
/// </summary>
public class PaymentMonetaryTruthTests
{
    private const decimal DeclaredNet = 3_011_866.27m;
    private const decimal DeclaredTax = 421_661.28m;
    private const decimal DeclaredGross = 3_433_527.55m;

    // ── H / I / F: the payment group plan uses reconciled item totals ───────────────────────

    private static PaymentGroupableItem Item(Guid docId, decimal total, int supplierId = 77) => new()
    {
        LineItemId = Guid.NewGuid(),
        PaymentSourceDocumentId = docId,
        SupplierId = supplierId,
        CurrencyCode = "AOA",
        PlantId = 3,
        SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
        TotalAmount = total
    };

    [Fact]
    public void The_group_total_equals_the_declared_gross_after_reconciliation()
    {
        // H (and I by assignment: ExpectedOperationInvoiceTotal = planned.TotalAmount at the
        // payment build site). Canonical lines sum to .54; the reconciled attribution is what
        // reaches the group plan — and the group carries the supplier's declared .55.
        var docId = Guid.NewGuid();
        var reconciled = PaymentRoundingResidual.Allocate(
            new[] { 1_000_000.00m, 1_433_527.54m, 1_000_000.00m }, DeclaredGross);
        Assert.True(reconciled.Applied);

        var plan = PaymentGroupPlan.Build(
            reconciled.Totals.Select(t => Item(docId, t)).ToList(),
            RequestConstants.PaymentConditions.PostPaid);

        var group = Assert.Single(plan);
        Assert.Equal(DeclaredGross, group.TotalAmount);
    }

    [Fact]
    public void Each_documents_residual_stays_inside_its_own_document()
    {
        // F: two documents of the same supplier/plant merge into one group; each document's
        // residual was reconciled against its OWN declared gross, so the group total is exactly
        // the sum of the two declared totals — no cross-document smearing.
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();

        var reconciledA = PaymentRoundingResidual.Allocate(
            new[] { 2_433_527.54m, 1_000_000.00m }, DeclaredGross);          // +0.01 → .55
        var reconciledB = PaymentRoundingResidual.Allocate(
            new[] { 500_000.00m, 500_000.00m }, 1_000_000.00m);              // zero residual
        Assert.True(reconciledA.Applied);
        Assert.False(reconciledB.Applied);

        var items = reconciledA.Totals.Select(t => Item(docA, t))
            .Concat(reconciledB.Totals.Select(t => Item(docB, t)))
            .ToList();

        var plan = PaymentGroupPlan.Build(items, RequestConstants.PaymentConditions.PostPaid);

        var group = Assert.Single(plan);
        Assert.Equal(DeclaredGross + 1_000_000.00m, group.TotalAmount);
        Assert.Equal(2, group.SourceDocumentIds.Count);
    }

    // ── A / J: persistence keeps the declared triplet and mirrors it on the request ─────────

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static PaymentSourceDocumentsController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new PaymentSourceDocumentsController(
            ctx,
            NullLogger<PaymentSourceDocumentsController>.Instance,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx));

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

    [Fact]
    public async Task Persistence_preserves_the_declared_triplet_and_the_request_total_mirrors_it()
    {
        using var ctx = NewContext();

        var actor = new User { Id = Guid.NewGuid(), FullName = "Monetary Tester", Email = "money@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 5, Code = "DRAFT", Name = "Rascunho", DisplayOrder = 5 });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-MONEY-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST monetary truth",
            RequestTypeId = 2,
            StatusId = 5,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(request);

        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FileName = "viana02.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_RECEIPT,
            StorageReference = "zztest/viana02.pdf",
            UploadedByUserId = actor.Id,
            UploadedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        ctx.RequestAttachments.Add(attachment);
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, actor.Id);
        var result = await controller.Create(request.Id,
            new SavePaymentSourceDocumentDto
            {
                AttachmentId = attachment.Id,
                SupplierId = 77,
                SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
                DocumentNumber = "ONP_18910_v3",
                Currency = "AOA",
                NetAmount = DeclaredNet,
                TaxAmount = DeclaredTax,
                GrossAmount = DeclaredGross
            });

        Assert.IsType<OkObjectResult>(result);

        var createdId = (await ctx.PaymentSourceDocuments.AsNoTracking().SingleAsync()).Id;

        // The header sync sums PERSISTED documents, so it takes effect from the next write —
        // which is exactly how the composer drives it (create, then field saves). A no-op field
        // save stands in for that here.
        Assert.IsType<OkObjectResult>(await controller.Update(request.Id, createdId,
            new SavePaymentSourceDocumentDto { DocumentNumber = "ONP_18910_v3" }));

        ctx.ChangeTracker.Clear();
        var document = await ctx.PaymentSourceDocuments.SingleAsync();
        Assert.Equal(DeclaredNet, document.NetAmount);
        Assert.Equal(DeclaredTax, document.TaxAmount);
        Assert.Equal(DeclaredGross, document.GrossAmount);           // never .54

        // J: the request header mirrors the authoritative document gross.
        var persisted = await ctx.Requests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(DeclaredGross, persisted.EstimatedTotalAmount);
    }

    // ── B (backend side): a reconciled or one-cent-off item sum never blocks validation ─────

    [Fact]
    public void The_validator_accepts_both_the_reconciled_and_the_one_cent_item_sum()
    {
        PaymentSourceDocumentValidationResult Validate(decimal itemsTotal)
            => PaymentSourceDocumentValidator.Validate(new[]
            {
                new PaymentSourceDocumentState
                {
                    Id = Guid.NewGuid(),
                    SequenceNumber = 1,
                    Label = "Documento 1",
                    HasAttachment = true,
                    SupplierId = 77,
                    PlantId = 3,
                    DocumentNumber = "ONP_18910_v3",
                    SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
                    DocumentDate = DateTime.UtcNow.AddDays(-3),
                    DueDate = DateTime.UtcNow.AddDays(20),
                    Currency = "AOA",
                    GrossAmount = DeclaredGross,
                    ItemsTotal = itemsTotal,
                    ActiveItemCount = 3
                }
            }, requireClassification: true);

        Assert.True(Validate(DeclaredGross).CanSubmit);              // reconciled: exact
        Assert.True(Validate(3_433_527.54m).CanSubmit);              // legacy one-cent delta
        Assert.False(Validate(3_400_000.00m).CanSubmit);             // material mismatch still blocks
    }
}
