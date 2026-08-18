using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Suppliers;

/// <summary>
/// The database-backed half of the internal-company rule: supplier matching, quick creation, and
/// resolving an existing supplier row that turns out to be an ALPLA entity.
/// </summary>
public class InternalCompanySupplierGuardTests
{
    private static ApplicationDbContext NewContext()
    {
        var ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        // The authoritative rows, as seeded in production.
        ctx.Companies.AddRange(
            new Company { Id = 1, Name = "AlplaPLASTICO", Code = "APA", TaxId = "5417567485", IsActive = true },
            new Company { Id = 2, Name = "AlplaSOPRO", Code = "APS", TaxId = "5001760246", IsActive = true });
        ctx.SaveChanges();
        return ctx;
    }

    private static SupplierCreationService NewService(ApplicationDbContext ctx)
        => new(ctx, NullLogger<SupplierCreationService>.Instance);

    private static Supplier Sup(int id, string name, string? taxId, bool active = true)
        => new()
        {
            Id = id, Name = name, TaxId = taxId, PortalCode = $"SUP-{id:D6}",
            IsActive = active, RegistrationStatus = active ? "ACTIVE" : "SUSPENDED"
        };

    // ── Matching ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The reported defect. The document named ALPLA and carried its NIF; the match must come back
    /// as internal rather than as "no supplier found — create one".
    /// </summary>
    [Fact]
    public async Task Match_InternalNif_IsReportedAsInternal()
    {
        using var ctx = NewContext();

        var result = await NewService(ctx).MatchAsync("ALPLA ANGOLA PLASTICOS LDA.", "5417567485");

        Assert.Equal(SupplierCreationStatus.InternalCompanyTaxId, result.Status);
        Assert.Equal(1, result.InternalCompanyId);
        Assert.Null(result.Supplier);   // nothing for the client to auto-select
    }

    /// <summary>
    /// The case NIF-only checking missed: the name is ALPLA's, the fiscal number was never read.
    /// </summary>
    [Fact]
    public async Task Match_InternalNameWithoutNif_IsReportedAsInternal()
    {
        using var ctx = NewContext();

        var result = await NewService(ctx).MatchAsync("ALPLA ANGOLA PLASTICOS LDA.", null);

        Assert.Equal(SupplierCreationStatus.InternalCompanyTaxId, result.Status);
        Assert.Equal(1, result.InternalCompanyId);
        Assert.True(result.InternalCompanyMatchedByName);
    }

    /// <summary>
    /// <c>ALPLA ANGOLA SOPRO, LDA</c> really is in the supplier master — it arrives from the
    /// Primavera sync. Matching by name would otherwise find it, mark it Conflict+active, and the
    /// composer would auto-select an ALPLA company as the payable supplier.
    /// </summary>
    [Fact]
    public async Task Match_ExistingSupplierRowThatIsInternal_IsNotOfferedForSelection()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup(133, "ALPLA ANGOLA SOPRO, LDA", "5001760246"));
        await ctx.SaveChangesAsync();

        var result = await NewService(ctx).MatchAsync("ALPLA ANGOLA SOPRO, LDA", null);

        Assert.Equal(SupplierCreationStatus.InternalCompanyTaxId, result.Status);
        Assert.Equal(2, result.InternalCompanyId);
        Assert.Null(result.Supplier);
    }

    /// <summary>An ordinary third party is matched exactly as before.</summary>
    [Fact]
    public async Task Match_ExternalSupplier_IsUnaffected()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup(264, "FIX4U - Comercio e Industria, Lda", "5000123456"));
        await ctx.SaveChangesAsync();

        var result = await NewService(ctx).MatchAsync("FIX4U - Comercio e Industria, Lda", "5000123456");

        Assert.Equal(SupplierCreationStatus.Conflict, result.Status);
        Assert.NotNull(result.Supplier);
        Assert.Equal(264, result.Supplier!.Id);
    }

    /// <summary>
    /// A supplier row carrying an internal NIF is treated as internal whatever it calls itself.
    /// This is not hypothetical — the development database has exactly one such row, where an ALPLA
    /// fiscal number was recorded against an external company's name.
    /// </summary>
    [Fact]
    public async Task Match_ExternalNameCarryingInternalNif_IsReportedAsInternal()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup(264, "FIX4U - Comercio e Industria, Lda", "5417567485"));
        await ctx.SaveChangesAsync();

        var result = await NewService(ctx).MatchAsync("FIX4U - Comercio e Industria, Lda", null);

        Assert.Equal(SupplierCreationStatus.InternalCompanyTaxId, result.Status);
        Assert.Null(result.Supplier);
    }

    // ── Quick creation ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_InternalNif_IsRefused()
    {
        using var ctx = NewContext();

        var result = await NewService(ctx).CreateAsync(
            new SupplierCreationInput { Name = "Qualquer Nome Lda", TaxId = "5417567485" },
            Guid.NewGuid());

        Assert.Equal(SupplierCreationStatus.InternalCompanyTaxId, result.Status);
        Assert.False(result.InternalCompanyMatchedByName);
        Assert.Empty(ctx.Suppliers);
    }

    [Fact]
    public async Task Create_InternalName_IsRefused()
    {
        using var ctx = NewContext();

        var result = await NewService(ctx).CreateAsync(
            new SupplierCreationInput { Name = "ALPLA ANGOLA PLASTICOS LDA.", TaxId = null },
            Guid.NewGuid());

        Assert.Equal(SupplierCreationStatus.InternalCompanyTaxId, result.Status);
        // Recognised as the entity itself, so the UI offers no "save by name anyway" path.
        Assert.True(result.InternalCompanyMatchedByName);
        Assert.Empty(ctx.Suppliers);
    }

    /// <summary>
    /// No duplicate Supplier row is ever produced for an internal company, however many times it is
    /// attempted.
    /// </summary>
    [Fact]
    public async Task Create_InternalEntity_NeverProducesADuplicateRow()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup(133, "ALPLA ANGOLA SOPRO, LDA", "5001760246"));
        await ctx.SaveChangesAsync();

        var service = NewService(ctx);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = await service.CreateAsync(
                new SupplierCreationInput { Name = "ALPLA ANGOLA SOPRO, LDA", TaxId = "5001760246" },
                Guid.NewGuid());

            Assert.Equal(SupplierCreationStatus.InternalCompanyTaxId, result.Status);
        }

        Assert.Single(ctx.Suppliers);
    }

    // ── Resolving an existing row ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_ResolvesASupplierRowThatIsAnInternalEntity()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup(133, "ALPLA ANGOLA SOPRO, LDA", "5001760246"));
        await ctx.SaveChangesAsync();

        var resolved = await new InternalCompanyGuard(ctx).ResolveSupplierAsync(133);

        Assert.NotNull(resolved);
        Assert.Equal("AlplaSOPRO", resolved!.Name);
    }

    [Fact]
    public async Task Guard_ReturnsNullForAGenuineSupplier()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup(264, "FIX4U - Comercio e Industria, Lda", "5000123456"));
        await ctx.SaveChangesAsync();

        Assert.Null(await new InternalCompanyGuard(ctx).ResolveSupplierAsync(264));
    }

    /// <summary>
    /// No supplier chosen yet, or one that does not exist, is not "internal" — that is the ordinary
    /// mandatory-field case and the existing validation already reports it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(999999)]
    public async Task Guard_UnknownOrMissingSupplier_FollowsExistingValidation(int? supplierId)
    {
        using var ctx = NewContext();

        Assert.Null(await new InternalCompanyGuard(ctx).ResolveSupplierAsync(supplierId));
    }

    /// <summary>
    /// Deactivating an ALPLA legal entity must not turn it into an acceptable payable supplier. A
    /// rule that can be switched off by editing a lookup row is not financial integrity.
    /// </summary>
    [Fact]
    public async Task Guard_InactiveInternalCompanyIsStillInternal()
    {
        using var ctx = NewContext();
        var company = ctx.Companies.First(c => c.Id == 2);
        company.IsActive = false;
        ctx.Suppliers.Add(Sup(133, "ALPLA ANGOLA SOPRO, LDA", "5001760246"));
        await ctx.SaveChangesAsync();

        Assert.NotNull(await new InternalCompanyGuard(ctx).ResolveSupplierAsync(133));
    }
}
