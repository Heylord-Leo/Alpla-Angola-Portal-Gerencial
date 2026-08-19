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

using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// The duplicate hierarchy (v2.229.10) at the endpoint level — the CONSULTIT regression and the
/// strengthened cross-request protection.
///
/// <para>The defect: CONSULTIT reuses <c>ONP_18910_v3</c> across four materially different
/// proposals, and the old supplier+number+series 409 forced users to falsify real supplier
/// references. These tests prove the four proposals register freely, proven duplicates still
/// block, ambiguity demands the audited override, and dead requests never block anything.</para>
/// </summary>
public class PaymentSourceDocumentDuplicateEndpointTests
{
    private const string Reference = "ONP_18910_v3";
    private const string ValidReason = "ZZTEST proposta distinta: projeto CCTV Viana02, escopo próprio.";

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

    private sealed record Seed(Guid RequestId, Guid ActorId);

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Duplicate Tester", Email = "dup@test.local" };
        ctx.Users.Add(actor);

        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 5, Code = "DRAFT", Name = "Rascunho", DisplayOrder = 5 },
            new RequestStatus { Id = 6, Code = "PO_ISSUED", Name = "P.O. Emitida", DisplayOrder = 6 },
            new RequestStatus { Id = 7, Code = "CANCELLED", Name = "Cancelado", DisplayOrder = 7 });

        var request = NewRequest(actor.Id, statusId: 5);
        ctx.Requests.Add(request);

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static Request NewRequest(Guid actorId, int statusId, int companyId = 1) => new()
    {
        Id = Guid.NewGuid(),
        RequestNumber = "ZZTEST-DUP-" + Guid.NewGuid().ToString("N")[..8],
        Title = "ZZTEST duplicate hierarchy",
        RequestTypeId = 2,
        StatusId = statusId,
        RequesterId = actorId,
        DepartmentId = 1,
        CompanyId = companyId,
        CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
    };

    private static RequestAttachment AddAttachment(
        ApplicationDbContext ctx, Guid requestId, Guid actorId, string? fileHash = null)
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            FileName = "proposta.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_RECEIPT,
            StorageReference = "zztest/proposta-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            FileHash = fileHash,
            UploadedByUserId = actorId,
            UploadedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        ctx.RequestAttachments.Add(attachment);
        return attachment;
    }

    private static SavePaymentSourceDocumentDto Proposal(
        Guid attachmentId, decimal gross, string number = Reference,
        bool overrideAcknowledged = false, string? overrideReason = null) => new()
    {
        AttachmentId = attachmentId,
        SupplierId = 77,
        SourceDocumentType = Types.Proforma,
        DocumentNumber = number,
        Currency = "AOA",
        GrossAmount = gross,
        DuplicateOverrideAcknowledged = overrideAcknowledged ? true : null,
        DuplicateOverrideReason = overrideReason
    };

    // ── K. CONSULTIT: four proposals, one reference, materially different — all accepted ────

    [Fact]
    public async Task The_consultit_proposals_all_register_despite_the_shared_reference()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        foreach (var gross in new[] { 2_856_658.96m, 3_433_527.55m, 1_492_231.88m })
        {
            var attachment = AddAttachment(ctx, seed.RequestId, seed.ActorId,
                fileHash: "hash-" + gross.ToString("F2"));
            await ctx.SaveChangesAsync();

            var result = await controller.Create(seed.RequestId, Proposal(attachment.Id, gross));
            Assert.IsType<OkObjectResult>(result);
        }

        ctx.ChangeTracker.Clear();
        Assert.Equal(3, await ctx.PaymentSourceDocuments.CountAsync(d => d.RequestId == seed.RequestId));
        // Nobody was asked to confirm anything: materially different totals are LEVEL 3.
        Assert.False(await ctx.RequestStatusHistories
            .AnyAsync(h => h.ActionTaken == "DOCUMENTO_DUPLICADO_POTENCIAL_CONFIRMADO"));
    }

    // ── F. The identical file twice within one request stays blocked ────────────────────────

    [Fact]
    public async Task The_same_file_twice_on_one_request_is_still_blocked()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var first = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "same-bytes");
        await ctx.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await controller.Create(seed.RequestId, Proposal(first.Id, 100m)));

        var second = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "same-bytes");
        await ctx.SaveChangesAsync();

        var refused = await controller.Create(seed.RequestId, Proposal(second.Id, 100m, number: "OTHER-REF"));
        var conflict = Assert.IsType<ConflictObjectResult>(refused);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Contains("já está registado", problem.Detail);
    }

    // ── G. The identical file on another LIVE request is now blocked ────────────────────────

    [Fact]
    public async Task The_same_file_on_another_live_request_is_blocked_naming_the_request()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);

        // The twin: an active source document of a live (PO_ISSUED) request.
        var otherRequest = NewRequest(seed.ActorId, statusId: 6);
        ctx.Requests.Add(otherRequest);
        var twinAttachment = AddAttachment(ctx, otherRequest.Id, seed.ActorId, fileHash: "debt-in-flight");
        ctx.PaymentSourceDocuments.Add(new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = otherRequest.Id,
            AttachmentId = twinAttachment.Id,
            SupplierId = 77,
            DocumentNumber = Reference,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        });

        var attachment = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "debt-in-flight");
        await ctx.SaveChangesAsync();

        var refused = await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, Proposal(attachment.Id, 100m));

        var conflict = Assert.IsType<ConflictObjectResult>(refused);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PaymentSourceDocumentDuplicateHierarchy.CrossRequestFileCode, problem.Extensions["code"]);
        Assert.Contains(otherRequest.RequestNumber, problem.Detail);

        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.PaymentSourceDocuments.CountAsync(d => d.RequestId == seed.RequestId));
    }

    // ── H (acceptance fix). LEVEL 1 can never be overridden ─────────────────────────────────

    [Fact]
    public async Task The_L4_override_fields_cannot_beat_a_cross_request_file_twin()
    {
        // A crafted call carrying a fully valid L4 override must still hit the LEVEL 1 wall:
        // the acknowledgement/justification pair exists only for AMBIGUOUS business duplicates,
        // never for file identity.
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);

        var otherRequest = NewRequest(seed.ActorId, statusId: 6);
        ctx.Requests.Add(otherRequest);
        var twinAttachment = AddAttachment(ctx, otherRequest.Id, seed.ActorId, fileHash: "l1-wall");
        ctx.PaymentSourceDocuments.Add(new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = otherRequest.Id,
            AttachmentId = twinAttachment.Id,
            SupplierId = 77,
            DocumentNumber = Reference,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        });

        var attachment = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "l1-wall");
        await ctx.SaveChangesAsync();

        var refused = await BuildController(ctx, seed.ActorId).Create(seed.RequestId,
            Proposal(attachment.Id, 100m, overrideAcknowledged: true, overrideReason: ValidReason));

        var conflict = Assert.IsType<ConflictObjectResult>(refused);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PaymentSourceDocumentDuplicateHierarchy.CrossRequestFileCode, problem.Extensions["code"]);

        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.PaymentSourceDocuments.CountAsync(d => d.RequestId == seed.RequestId));
    }

    // ── Legacy twin (pre-Release-3): persistence blocks it exactly like a document twin ─────

    [Fact]
    public async Task A_legacy_source_typed_attachment_twin_blocks_creation_too()
    {
        // The REQ-21/07/2026-116 shape: a live request created before PaymentSourceDocuments
        // existed carries its proforma as a source-TYPED attachment with no document row. MODEL B:
        // that file is the commercial source of a live request — preflight AND persistence block.
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);

        var legacyRequest = NewRequest(seed.ActorId, statusId: 6);
        ctx.Requests.Add(legacyRequest);
        var legacyAttachment = AddAttachment(ctx, legacyRequest.Id, seed.ActorId, fileHash: "legacy-bytes");
        legacyAttachment.AttachmentTypeCode = RequestAttachment.TYPE_PROFORMA;
        // Deliberately NO PaymentSourceDocument row.

        var attachment = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "legacy-bytes");
        await ctx.SaveChangesAsync();

        var refused = await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, Proposal(attachment.Id, 100m));

        var conflict = Assert.IsType<ConflictObjectResult>(refused);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PaymentSourceDocumentDuplicateHierarchy.CrossRequestFileCode, problem.Extensions["code"]);
        Assert.Contains(legacyRequest.RequestNumber, problem.Detail);
    }

    // ── O. Dead requests never block ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_twin_on_a_cancelled_request_blocks_nothing()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);

        // Identical file AND identical reference/total — but the other request is CANCELLED, so
        // no payment can ever come from it. Blocking would be false double-payment protection.
        var deadRequest = NewRequest(seed.ActorId, statusId: 7);
        ctx.Requests.Add(deadRequest);
        var twinAttachment = AddAttachment(ctx, deadRequest.Id, seed.ActorId, fileHash: "dead-bytes");
        ctx.PaymentSourceDocuments.Add(new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = deadRequest.Id,
            AttachmentId = twinAttachment.Id,
            SupplierId = 77,
            DocumentNumber = Reference,
            Currency = "AOA",
            GrossAmount = 100m,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        });

        var attachment = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "dead-bytes");
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, Proposal(attachment.Id, 100m));

        Assert.IsType<OkObjectResult>(result);
    }

    // ── I / N. Header equality without content evidence: ambiguous, then audited override ───

    [Fact]
    public async Task Header_equality_without_items_refuses_as_ambiguous_then_accepts_the_audited_override()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId);

        var first = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "bytes-1");
        await ctx.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await controller.Create(seed.RequestId, Proposal(first.Id, 1_000_000m)));

        var second = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "bytes-2");
        await ctx.SaveChangesAsync();

        // Same reference, same total, no items on either side → AMBIGUOUS, never a silent block.
        var refused = await controller.Create(seed.RequestId, Proposal(second.Id, 1_000_000m));
        var conflict = Assert.IsType<ConflictObjectResult>(refused);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PaymentSourceDocumentDuplicateHierarchy.AmbiguousDuplicateCode, problem.Extensions["code"]);

        // A short reason is not an override.
        var shortReason = await controller.Create(seed.RequestId,
            Proposal(second.Id, 1_000_000m, overrideAcknowledged: true, overrideReason: "distinto"));
        Assert.IsType<ConflictObjectResult>(shortReason);

        // The complete ritual: acknowledged + written reason ≥ 20 chars → accepted and audited.
        var accepted = await controller.Create(seed.RequestId,
            Proposal(second.Id, 1_000_000m, overrideAcknowledged: true, overrideReason: ValidReason));
        Assert.IsType<OkObjectResult>(accepted);

        ctx.ChangeTracker.Clear();
        Assert.Equal(2, await ctx.PaymentSourceDocuments.CountAsync(d => d.RequestId == seed.RequestId));

        var audit = await ctx.RequestStatusHistories
            .SingleAsync(h => h.ActionTaken == "DOCUMENTO_DUPLICADO_POTENCIAL_CONFIRMADO");
        Assert.Contains(ValidReason, audit.Comment);
        Assert.Contains(Reference, audit.Comment);
        Assert.Equal(seed.ActorId, audit.ActorUserId);
    }

    // ── H. Proven semantic duplicate through the persisted-items fingerprint ────────────────

    [Fact]
    public async Task Identical_persisted_content_with_the_same_header_is_hard_blocked_on_update()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);

        PaymentSourceDocument Doc(int sequence, string number) => new()
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            AttachmentId = Guid.NewGuid(),
            SupplierId = 77,
            SourceDocumentType = Types.Proforma,
            DocumentNumber = number,
            Currency = "AOA",
            GrossAmount = 1_020_000m,
            SequenceNumber = sequence,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        };

        var doc1 = Doc(1, Reference);
        var doc2 = Doc(2, "PENDING-REF");     // becomes the twin through the edit below
        ctx.PaymentSourceDocuments.AddRange(doc1, doc2);

        foreach (var (doc, line) in new[] { (doc1, 1), (doc2, 2) })
        {
            ctx.RequestLineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = seed.RequestId,
                LineNumber = line,
                Description = "Câmara IP 4MP",
                Quantity = 12m,
                UnitPrice = 85_000m,
                TotalAmount = 1_020_000m,
                PaymentSourceDocumentId = doc.Id
            });
        }
        await ctx.SaveChangesAsync();

        // Editing doc2 to doc1's exact reference, with identical items and totals on both sides,
        // is the re-keyed copy of the same debt — LEVEL 2, refused with no override on offer.
        var refused = await BuildController(ctx, seed.ActorId).Update(seed.RequestId, doc2.Id,
            new SavePaymentSourceDocumentDto
            {
                DocumentNumber = Reference,
                DuplicateOverrideAcknowledged = true,          // an override cannot beat LEVEL 2
                DuplicateOverrideReason = ValidReason
            });

        var conflict = Assert.IsType<ConflictObjectResult>(refused);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PaymentSourceDocumentDuplicateHierarchy.SemanticDuplicateCode, problem.Extensions["code"]);
    }

    // ── M. Cross-request same reference with materially different content is allowed ────────

    [Fact]
    public async Task Cross_request_same_reference_with_a_different_total_is_allowed()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx);

        // Decoder, registered on another live request of another company, same reference.
        var otherRequest = NewRequest(seed.ActorId, statusId: 6, companyId: 2);
        ctx.Requests.Add(otherRequest);
        var twinAttachment = AddAttachment(ctx, otherRequest.Id, seed.ActorId, fileHash: "decoder-bytes");
        ctx.PaymentSourceDocuments.Add(new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = otherRequest.Id,
            AttachmentId = twinAttachment.Id,
            SupplierId = 77,
            DocumentNumber = Reference,
            Currency = "AOA",
            GrossAmount = 1_301_655.95m,
            SequenceNumber = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = seed.ActorId
        });

        var attachment = AddAttachment(ctx, seed.RequestId, seed.ActorId, fileHash: "viana01-bytes");
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, seed.ActorId)
            .Create(seed.RequestId, Proposal(attachment.Id, 2_856_658.96m));

        Assert.IsType<OkObjectResult>(result);
    }
}
