using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Suppliers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Suppliers;

/// <summary>
/// Phase 3D / Layer B.1 — the server-side Supplier Sheet authorization matrix. Asserts the ACTUAL
/// capability evaluator every ficha endpoint enforces: Buyer is operational-only, request-scoped by the
/// CANONICAL request scope (plant/department — NOT ownership), may upload but NOT delete documents, and
/// may NOT submit for approval or touch governance/banking/identity; Contracts/Finance/SysAdmin retain
/// rights; Local Manager keeps edit but loses status.
/// </summary>
public class SupplierCapabilityEvaluatorTests
{
    private static ApplicationDbContext NewContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SupplierCapabilityEvaluator NewEvaluator(ApplicationDbContext ctx) => new(ctx);

    private const int SupplierId = 42;
    private static readonly Guid Buyer = Guid.NewGuid();

    private static Supplier Sup(int id = SupplierId)
        => new() { Id = id, Name = "Fornecedor Teste", PortalCode = $"SUP-{id:D6}", IsActive = true, RegistrationStatus = "ACTIVE" };

    private static async Task<ApplicationDbContext> SeededAsync(Action<ApplicationDbContext>? extra = null)
    {
        var ctx = NewContext();
        ctx.Suppliers.Add(Sup());
        extra?.Invoke(ctx);
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Task<AlplaPortal.Application.Interfaces.SupplierSheetCapabilities> Eval(
        ApplicationDbContext ctx, Guid userId, params string[] roles)
        => NewEvaluator(ctx).EvaluateAsync(SupplierId, userId, roles);

    // ── System Administrator: full authority ──
    [Fact]
    public async Task SystemAdministrator_HasFullAuthority_IncludingGovernance()
    {
        await using var ctx = await SeededAsync();
        var caps = await Eval(ctx, Guid.NewGuid(), RoleConstants.SystemAdministrator);
        Assert.True(caps.CanView && caps.CanEditContacts && caps.CanEditBanking && caps.CanEditTaxLegal);
        Assert.True(caps.CanUploadDocuments && caps.CanDeleteDocuments);
        Assert.True(caps.CanChangeStatus && caps.CanSubmitForApproval && caps.CanApprove && caps.CanReject);
    }

    // ── Contracts / Finance: governance owners, global (no request-scope) ──
    [Theory]
    [InlineData(RoleConstants.Contracts)]
    [InlineData(RoleConstants.Finance)]
    public async Task GovernanceOwner_CanEditAllFields_AndChangeStatus_WithoutInvolvement(string role)
    {
        await using var ctx = await SeededAsync(); // NO request links this user to the supplier
        var caps = await Eval(ctx, Guid.NewGuid(), role);
        Assert.True(caps.CanView && caps.CanEditContacts && caps.CanEditBanking && caps.CanEditTaxLegal
                    && caps.CanEditCommercialTerms && caps.CanEditGeneralIdentity);
        Assert.True(caps.CanUploadDocuments && caps.CanDeleteDocuments);
        Assert.True(caps.CanChangeStatus && caps.CanSubmitForApproval);
        Assert.False(caps.CanApprove || caps.CanReject); // approve/reject are DAF/DG, not the sheet
    }

    // ── Local Manager: full ficha edit + submit + documents, but NOT status (Phase 3D security fix) ──
    [Fact]
    public async Task LocalManager_CanEditFields_ButNotChangeStatus()
    {
        await using var ctx = await SeededAsync();
        var caps = await Eval(ctx, Guid.NewGuid(), RoleConstants.LocalManager);
        Assert.True(caps.CanView && caps.CanEditContacts && caps.CanEditBanking && caps.CanSubmitForApproval);
        Assert.True(caps.CanUploadDocuments && caps.CanDeleteDocuments);
        Assert.False(caps.CanChangeStatus);
    }

    // ── Buyer IN SCOPE: operational-only; upload-yes/delete-no; NO submit / governance / banking ──
    [Fact]
    public async Task Buyer_InScope_GetsOperationalOnly_UploadNotDelete_NoSubmit()
    {
        await using var ctx = await SeededAsync(c =>
            c.Requests.Add(new Request { Id = Guid.NewGuid(), BuyerId = Buyer, SupplierId = SupplierId }));
        var caps = await Eval(ctx, Buyer, RoleConstants.Buyer);

        Assert.True(caps.CanView && caps.CanEditContacts && caps.CanEditAddress && caps.CanEditObservations);
        Assert.True(caps.CanUploadDocuments);
        // Forbidden for Buyer:
        Assert.False(caps.CanDeleteDocuments);
        Assert.False(caps.CanSubmitForApproval);
        Assert.False(caps.CanEditBanking);
        Assert.False(caps.CanEditTaxLegal);
        Assert.False(caps.CanEditGeneralIdentity);
        Assert.False(caps.CanEditCommercialTerms);
        Assert.False(caps.CanChangeStatus);
        Assert.False(caps.CanApprove || caps.CanReject);
    }

    [Fact]
    public async Task Buyer_InvolvedViaQuotation_IsInScope()
    {
        await using var ctx = await SeededAsync(c => c.Requests.Add(new Request
        {
            Id = Guid.NewGuid(), BuyerId = Buyer,
            Quotations = new List<Quotation> { new() { Id = Guid.NewGuid(), SupplierId = SupplierId } }
        }));
        var caps = await Eval(ctx, Buyer, RoleConstants.Buyer);
        Assert.True(caps.CanView && caps.CanEditContacts);
    }

    [Fact]
    public async Task Buyer_InvolvedViaLineItem_IsInScope()
    {
        await using var ctx = await SeededAsync(c => c.Requests.Add(new Request
        {
            Id = Guid.NewGuid(), BuyerId = Buyer,
            LineItems = new List<RequestLineItem> { new() { Id = Guid.NewGuid(), SupplierId = SupplierId } }
        }));
        var caps = await Eval(ctx, Buyer, RoleConstants.Buyer);
        Assert.True(caps.CanView && caps.CanEditContacts);
    }

    [Fact]
    public async Task Buyer_InvolvedViaPoGroup_IsInScope()
    {
        await using var ctx = await SeededAsync(c => c.Requests.Add(new Request
        {
            Id = Guid.NewGuid(), BuyerId = Buyer,
            PoGroups = new List<RequestPoGroup> { new() { Id = Guid.NewGuid(), SupplierId = SupplierId } }
        }));
        var caps = await Eval(ctx, Buyer, RoleConstants.Buyer);
        Assert.True(caps.CanView && caps.CanEditContacts);
    }

    // ── CANONICAL SCOPE: authorized-but-not-owner. A Buyer with NO plant/dept scope can access every
    //    request (mirrors /buyer/requests/{id}); a supplier in ANY such request is in scope even when a
    //    DIFFERENT buyer owns the request. Ownership (BuyerId) is NOT the access boundary. ──
    [Fact]
    public async Task Buyer_AuthorizedButNotOwner_HasAccess_WhenUnscopedSeesAllRequests()
    {
        await using var ctx = await SeededAsync(c =>
            c.Requests.Add(new Request { Id = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SupplierId = SupplierId }));
        var caps = await Eval(ctx, Buyer, RoleConstants.Buyer); // this buyer does not OWN that request
        Assert.True(caps.CanView && caps.CanEditContacts);      // …but canonically may access it → supplier in scope
    }

    // ── OUT OF SCOPE: a plant-scoped Buyer whose supplier only appears in another plant's request. ──
    [Fact]
    public async Task Buyer_PlantScoped_SupplierOnlyInOtherPlant_HasNoAccess()
    {
        await using var ctx = await SeededAsync(c =>
        {
            c.UserPlantScopes.Add(new UserPlantScope { UserId = Buyer, PlantId = 1 });     // buyer scoped to plant 1
            c.Requests.Add(new Request { Id = Guid.NewGuid(), BuyerId = Buyer, PlantId = 2, SupplierId = SupplierId }); // request in plant 2
        });
        var caps = await Eval(ctx, Buyer, RoleConstants.Buyer);
        Assert.False(caps.CanView);
        Assert.False(caps.CanEditAnyField);
    }

    [Fact]
    public async Task Buyer_PlantScoped_SupplierInSamePlant_OtherOwner_HasAccess()
    {
        await using var ctx = await SeededAsync(c =>
        {
            c.UserPlantScopes.Add(new UserPlantScope { UserId = Buyer, PlantId = 1 });
            c.Requests.Add(new Request { Id = Guid.NewGuid(), BuyerId = Guid.NewGuid(), PlantId = 1, SupplierId = SupplierId });
        });
        var caps = await Eval(ctx, Buyer, RoleConstants.Buyer);
        Assert.True(caps.CanView && caps.CanEditContacts);
    }

    // ── No request involves the supplier at all → no access. ──
    [Fact]
    public async Task Buyer_SupplierInvolvedNowhere_HasNoAccess()
    {
        await using var ctx = await SeededAsync(c =>
            c.Requests.Add(new Request { Id = Guid.NewGuid(), BuyerId = Buyer, SupplierId = 999 })); // other supplier only
        var caps = await Eval(ctx, Buyer, RoleConstants.Buyer);
        Assert.False(caps.CanView);
        Assert.False(caps.CanEditAnyField);
        Assert.False(caps.CanSubmitForApproval);
    }

    // ── Buyer who also holds a global role gets the broader (global) grant ──
    [Fact]
    public async Task Buyer_AlsoContracts_GetsGlobalGovernanceOwnerGrant_EvenIfNotInvolved()
    {
        await using var ctx = await SeededAsync();
        var caps = await Eval(ctx, Buyer, RoleConstants.Buyer, RoleConstants.Contracts);
        Assert.True(caps.CanEditBanking && caps.CanChangeStatus && caps.CanDeleteDocuments);
    }

    // ── Any unrelated authenticated role: no ficha access ──
    [Fact]
    public async Task UnrelatedRole_HasNoAccess()
    {
        await using var ctx = await SeededAsync();
        var caps = await Eval(ctx, Guid.NewGuid(), RoleConstants.AreaApprover);
        Assert.False(caps.CanView);
        Assert.False(caps.CanEditAnyField);
    }
}
