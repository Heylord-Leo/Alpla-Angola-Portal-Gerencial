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
using Xunit;
using S = AlplaPortal.Domain.Constants.BuyerQueueConstants;
using K = AlplaPortal.Domain.Constants.PoGroupCancellationConstants.FinanceReturnKinds;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.249.2 — the Buyer Workspace read model on the REQ-28/10/2026-628 shape (in-memory EF): after "voltar a cotar" of
/// group S1 the released line 1 has only the historical TREVOTECH candidate (reuse-blocked) → "Pendente de cotação",
/// PARTIAL_COVERAGE with ADD_QUOTATION, quotation window OPEN for line 1 only, the old quotation flagged "reuse requires
/// authorization"; an active authorization flips it to ready. Group actions: WAITING_PO → "Registrar P.O." only;
/// WAITING_PO_CORRECTION → "Corrigir P.O." only; the state label follows the stored return kind. Legacy pre-batch
/// requests keep the window closed.
/// </summary>
public class BuyerWorkspaceReuseAndPoActionsEndpointTests
{
    private const int QuotationTypeId = 1;
    private const int PoPartiallyUploadedId = 5, FinalApprovedId = 6, PoIssuedId = 8;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static void SeedLookups(ApplicationDbContext ctx)
    {
        ctx.RequestTypes.Add(new RequestType { Id = QuotationTypeId, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = PoPartiallyUploadedId, Code = RequestConstants.Statuses.PoPartiallyUploaded, Name = "P.O. parcial" },
            new RequestStatus { Id = FinalApprovedId, Code = RequestConstants.Statuses.FinalApproved, Name = "Aprovado" },
            new RequestStatus { Id = PoIssuedId, Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida" });
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaSOPRO" });
        ctx.Departments.Add(new Department { Id = 1, Name = "Manutenção" });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Viana 3", CompanyId = 1 });
    }

    private sealed record Seed(Guid RequestId, Guid Line1, Guid Line2, Guid Quotation1, Guid OldCandidate, Guid BatchId, Guid GroupS1, Guid GroupS2, Guid Buyer);

    /// <summary>REQ-628 after the cancellation of S1 (restart): batch APPROVED, item 1 released, item 2 approved and PO_ISSUED.</summary>
    private static async Task<Seed> SeedReq628Async(ApplicationDbContext ctx, bool withAuthorization = false)
    {
        SeedLookups(ctx);
        var buyer = Guid.NewGuid();
        ctx.Users.Add(new User { Id = buyer, FullName = "Samuel Mpoco", Email = "samuel@t.local", IsActive = true });
        var req = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-28/10/2026-628", Title = "TEST v2.249.0 – A – voltar a cotar", Description = "d",
            RequestTypeId = QuotationTypeId, StatusId = PoPartiallyUploadedId, RequesterId = buyer, CreatedByUserId = buyer, BuyerId = buyer,
            DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
        };
        ctx.Requests.Add(req);
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = req.Id, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.Approved, CreatedByUserId = buyer, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), ApprovedTotalAmount = 250_000m };
        ctx.ApprovalBatches.Add(batch);
        var s1 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, SupplierId = 10, SupplierNameSnapshot = "TREVOTECH LDA", Status = RequestConstants.PoGroupStatuses.Cancelled, CurrencyCode = "AOA", TotalAmount = 250_000m, PurchaseOrderNumber = "ECF99 2026/9001", ApprovalBatchId = batch.Id, CreatedByUserId = buyer, CreatedAtUtc = DateTime.UtcNow.AddDays(-1) };
        var s2 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, SupplierId = 11, SupplierNameSnapshot = "MULTI BIZ", Status = RequestConstants.PoGroupStatuses.PoIssued, CurrencyCode = "AOA", TotalAmount = 120_000m, PurchaseOrderNumber = "ECF99 2026/9002", ApprovalBatchId = batch.Id, CreatedByUserId = buyer, CreatedAtUtc = DateTime.UtcNow.AddDays(-1) };
        ctx.RequestPoGroups.AddRange(s1, s2);
        var line1 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 1, Description = "Rolo de filme stretch 500 mm — TEST", Quantity = 10, UnitPrice = 0, TotalAmount = 0, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationPending };
        var line2 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 2, Description = "Fita adesiva 48 mm — TEST", Quantity = 20, UnitPrice = 6000, TotalAmount = 120_000, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved, RequestPoGroupId = s2.Id };
        ctx.RequestLineItems.AddRange(line1, line2);
        var q1 = new Quotation { Id = Guid.NewGuid(), RequestId = req.Id, SupplierId = 10, SupplierNameSnapshot = "TREVOTECH LDA", Currency = "AOA", TotalAmount = 250_000m, SourceType = "MANUAL", CreatedByUserId = buyer, CreatedAtUtc = DateTime.UtcNow.AddDays(-3) };
        var oldCandidate = new QuotationItem { Id = Guid.NewGuid(), QuotationId = q1.Id, Description = line1.Description, Quantity = 10, UnitPrice = 25_000m, LineTotal = 250_000m, ReconciliationStatus = RequestConstants.ReconciliationStatuses.Mapped, MappedRequestLineItemId = line1.Id };
        ctx.Quotations.Add(q1); ctx.QuotationItems.Add(oldCandidate);
        var bi1 = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = line1.Id, SelectedQuotationItemId = oldCandidate.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), ReleasedAtUtc = DateTime.UtcNow.AddHours(-1), ReleasedByUserId = buyer, ReleaseKind = "RESTART_QUOTATION" };
        var bi2 = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = line2.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-2) };
        ctx.ApprovalBatchItems.AddRange(bi1, bi2);
        ctx.ApprovalBatchItemCandidates.Add(new ApprovalBatchItemCandidate { Id = Guid.NewGuid(), ApprovalBatchItemId = bi1.Id, QuotationItemId = oldCandidate.Id, QuotationId = q1.Id, SupplierNameSnapshot = "TREVOTECH LDA", QuotedDescription = line1.Description, QuotedQuantity = 10, UnitPrice = 25_000m, LineTotal = 250_000m, Currency = "AOA", CreatedAtUtc = DateTime.UtcNow.AddDays(-2), CreatedByUserId = buyer });
        if (withAuthorization)
            ctx.QuotationReuseAuthorizations.Add(new QuotationReuseAuthorization { Id = Guid.NewGuid(), RequestId = req.Id, QuotationId = q1.Id, QuotationItemId = oldCandidate.Id, SourceApprovalBatchId = batch.Id, AuthorizedByUserId = buyer, AuthorizedAtUtc = DateTime.UtcNow, Reason = "Proposta mantém-se válida", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        return new Seed(req.Id, line1.Id, line2.Id, q1.Id, oldCandidate.Id, batch.Id, s1.Id, s2.Id, buyer);
    }

    private static BuyerWorkspaceController Ctrl(ApplicationDbContext ctx, Guid actor, string role = RoleConstants.Buyer)
    {
        var c = new BuyerWorkspaceController(ctx);
        c.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actor.ToString()), new(ClaimTypes.Role, role) }, "Test"))
            }
        };
        return c;
    }

    private static async Task<BuyerWorkspaceDto> Workspace(ApplicationDbContext ctx, Guid actor, Guid requestId)
    {
        var result = await Ctrl(ctx, actor).GetWorkspace(requestId);
        return (BuyerWorkspaceDto)((OkObjectResult)((ActionResult<BuyerWorkspaceDto>)result).Result!).Value!;
    }

    [Fact]
    public async Task Req628_after_restart_released_line_is_pending_window_open_for_it_only_old_quotation_needs_authorization()
    {
        using var ctx = NewContext();
        var s = await SeedReq628Async(ctx);
        var ws = await Workspace(ctx, s.Buyer, s.RequestId);

        // Header / counters / line state agree: nothing is "ready" — the old candidate needs the explicit reuse authorization.
        Assert.Equal(S.OperationalStates.PartialCoverage, ws.OperationalState);
        Assert.Contains(ws.NextActions, a => a.Code == S.ActionCodes.AddQuotation && a.Actionable);
        Assert.DoesNotContain(ws.NextActions, a => a.Code == S.ActionCodes.SubmitBatch);
        Assert.Equal(1, ws.Coverage.Pending); Assert.Equal(0, ws.Coverage.ReadyForBatch); Assert.Equal(1, ws.Coverage.Approved);
        Assert.Equal(S.CoverageBuckets.PendingQuotation, ws.Items.Single(i => i.Id == s.Line1).CoverageBucket);
        Assert.Equal(S.CoverageBuckets.Approved, ws.Items.Single(i => i.Id == s.Line2).CoverageBucket);

        // The server's quotation window: open, for the released line only (scalar PO_PARTIALLY_UPLOADED untouched).
        Assert.True(ws.QuotationWindowOpen);
        Assert.Null(ws.QuotationWindowClosedReasonCode);
        Assert.Equal(new[] { s.Line1 }, ws.OpenLineItemIds);
        Assert.Equal(RequestConstants.Statuses.PoPartiallyUploaded, ws.RequestStatusCode);

        // The historical quotation is flagged — nothing is reused silently.
        var q = ws.Quotations.Single(x => x.Id == s.Quotation1);
        Assert.Equal(1, q.ReuseBlockedItemCount); Assert.Equal(0, q.ReuseAuthorizedItemCount);

        // Sibling group untouched: PO_ISSUED offers neither registration nor correction; cancelled group offers nothing.
        var s2 = ws.PoGroups.Single(g => g.Id == s.GroupS2);
        Assert.False(s2.CanRegisterPo); Assert.False(s2.CanCorrectPo);
        Assert.Equal(120_000m, s2.TotalAmount);
        Assert.False(ws.PoGroups.Single(g => g.Id == s.GroupS1).CanRegisterPo);
    }

    [Fact]
    public async Task Req628_with_active_reuse_authorization_released_line_is_ready()
    {
        using var ctx = NewContext();
        var s = await SeedReq628Async(ctx, withAuthorization: true);
        var ws = await Workspace(ctx, s.Buyer, s.RequestId);
        Assert.Equal(S.CoverageBuckets.QuotedReadyForBatch, ws.Items.Single(i => i.Id == s.Line1).CoverageBucket);
        Assert.Equal(S.OperationalStates.ReadyForApproval, ws.OperationalState);
        Assert.Contains(ws.NextActions, a => a.Code == S.ActionCodes.SubmitBatch && a.Actionable);
        var q = ws.Quotations.Single(x => x.Id == s.Quotation1);
        Assert.Equal(0, q.ReuseBlockedItemCount); Assert.Equal(1, q.ReuseAuthorizedItemCount);
    }

    [Fact]
    public async Task WaitingPo_group_offers_registration_only_and_the_state_says_po_to_register()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = Guid.NewGuid();
        ctx.Users.Add(new User { Id = buyer, FullName = "Comprador", Email = "b@t.local", IsActive = true });
        var req = new Request { Id = Guid.NewGuid(), RequestNumber = "REQ-WPO", Title = "t", Description = "d", RequestTypeId = QuotationTypeId, StatusId = FinalApprovedId, RequesterId = buyer, CreatedByUserId = buyer, BuyerId = buyer, DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(req);
        ctx.RequestLineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 1, Description = "x", Quantity = 1, UnitPrice = 1, TotalAmount = 1, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved });
        var g = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, SupplierId = 10, SupplierNameSnapshot = "S1", Status = RequestConstants.PoGroupStatuses.WaitingPo, CurrencyCode = "AOA", TotalAmount = 10m, CreatedByUserId = buyer, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestPoGroups.Add(g);
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, buyer, req.Id);
        var dto = ws.PoGroups.Single();
        Assert.True(dto.CanRegisterPo);
        Assert.False(dto.CanCorrectPo); // v2.249.2: WAITING_PO never offers the correction tool
        Assert.Equal(S.OperationalStates.PoRegistrationPending, ws.OperationalState);
        Assert.Equal("P.O. a registar", ws.OperationalStateLabel);
        Assert.All(ws.NextActions, a => Assert.False(a.Actionable));
    }

    [Theory]
    [InlineData(K.PoCorrection, "Correção de P.O.", S.ActionCodes.CorrectPo)]
    [InlineData(K.OrderCancellationRequested, "P.O. devolvida — cancelamento solicitado", S.ActionCodes.CancelPoGroup)]
    [InlineData(null, "P.O. devolvida por Finanças", S.ActionCodes.CorrectPo)]
    public async Task Returned_group_offers_correction_only_and_the_label_follows_the_stored_kind(string? kind, string expectedLabel, string expectedFirstAction)
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        ctx.RequestStatuses.Add(new RequestStatus { Id = 9, Code = RequestConstants.Statuses.WaitingPoCorrection, Name = "Devolvido" });
        var buyer = Guid.NewGuid();
        ctx.Users.Add(new User { Id = buyer, FullName = "Comprador", Email = "b@t.local", IsActive = true });
        var req = new Request { Id = Guid.NewGuid(), RequestNumber = "REQ-WPC", Title = "t", Description = "d", RequestTypeId = QuotationTypeId, StatusId = 9, RequesterId = buyer, CreatedByUserId = buyer, BuyerId = buyer, DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(req);
        ctx.RequestLineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 1, Description = "x", Quantity = 1, UnitPrice = 1, TotalAmount = 1, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved });
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, SupplierId = 10, SupplierNameSnapshot = "S1", Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection, CurrencyCode = "AOA", TotalAmount = 10m, PurchaseOrderNumber = "ECF99 2026/9001", LastFinanceReturnKind = kind, CreatedByUserId = buyer, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, buyer, req.Id);
        var dto = ws.PoGroups.Single();
        Assert.True(dto.CanCorrectPo);
        Assert.False(dto.CanRegisterPo);
        Assert.Equal(S.OperationalStates.PoCorrection, ws.OperationalState);
        Assert.Equal(expectedLabel, ws.OperationalStateLabel);
        Assert.Equal(expectedFirstAction, ws.NextActions[0].Code);
        Assert.Contains(ws.NextActions, a => a.Code == S.ActionCodes.CancelPoGroup && a.Actionable);
        Assert.Contains(ws.NextActions, a => a.Code == S.ActionCodes.CorrectPo && a.Actionable);
    }

    [Fact]
    public async Task Legacy_request_without_batches_keeps_the_window_closed()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = Guid.NewGuid();
        ctx.Users.Add(new User { Id = buyer, FullName = "Comprador", Email = "b@t.local", IsActive = true });
        var req = new Request { Id = Guid.NewGuid(), RequestNumber = "REQ-LEG", Title = "t", Description = "d", RequestTypeId = QuotationTypeId, StatusId = PoIssuedId, RequesterId = buyer, CreatedByUserId = buyer, BuyerId = buyer, DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(req);
        ctx.RequestLineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 1, Description = "x", Quantity = 1, UnitPrice = 1, TotalAmount = 1, QuotationLifecycleStatus = null });
        await ctx.SaveChangesAsync();

        var ws = await Workspace(ctx, buyer, req.Id);
        Assert.False(ws.QuotationWindowOpen);
        Assert.Equal(QuotationWindowPolicy.ClosedNoBatchWorkflow, ws.QuotationWindowClosedReasonCode);
        Assert.Empty(ws.OpenLineItemIds);
        Assert.Equal(S.OperationalStates.CompletedForBuyer, ws.OperationalState);
    }
}
