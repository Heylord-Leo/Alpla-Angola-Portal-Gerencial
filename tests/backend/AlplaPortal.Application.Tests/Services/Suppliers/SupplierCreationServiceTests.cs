using System;
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
/// Covers the authoritative supplier MATCHING and normalization. The DRAFT creation persistence path
/// uses a SQL-Server-specific counter (FromSqlRaw + UPDLOCK) and unique indexes, so it is verified via
/// runtime/integration (documented in the checkpoint) rather than the EF in-memory provider.
/// </summary>
public class SupplierCreationServiceTests
{
    private static ApplicationDbContext NewContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SupplierCreationService NewService(ApplicationDbContext ctx)
        => new(ctx, NullLogger<SupplierCreationService>.Instance);

    private static Supplier Sup(string name, string? taxId, bool active = true, int id = 0)
        => new() { Id = id, Name = name, TaxId = taxId, PortalCode = $"SUP-{id:D6}", IsActive = active, RegistrationStatus = active ? "ACTIVE" : "SUSPENDED" };

    // ── Normalization (cases 6, 7) ──

    [Fact]
    public void NormalizeName_StripsAccentsAndPunctuation_ButKeepsSuffix()
    {
        Assert.Equal(NormalizeName("ACME LDA"), NormalizeName("  ácme,  lda.  "));
        Assert.Contains("LDA", NormalizeName("Acme, Lda")); // corporate suffix preserved
    }

    [Fact]
    public void NormalizeNif_IgnoresFormatting()
        => Assert.Equal(SupplierCreationService.NormalizeNif("500 123.456"), SupplierCreationService.NormalizeNif("500123456"));

    [Fact]
    public void NormalizeName_AguiaCase_NormalizesEqual()
        => Assert.Equal(NormalizeName("Fornecedor Águia, Lda."), NormalizeName("FORNECEDOR AGUIA LDA"));

    [Fact]
    public void NormalizeName_CollapsesDoubleSpaces()
        => Assert.Equal(NormalizeName("ACME   LDA"), NormalizeName("ACME LDA"));

    [Fact]
    public void NormalizeNif_KeepsLetters()
        => Assert.Equal("PT500999", SupplierCreationService.NormalizeNif("pt-500.999"));

    [Fact]
    public void NormalizeNif_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, SupplierCreationService.NormalizeNif("   "));

    private static string NormalizeName(string s) => SupplierCreationService.NormalizeName(s);

    // ── Matching (cases 1–10) ──

    [Fact] // 1
    public async Task SameNif_Active_ReturnsExistingConflict()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Fornecedor A", "500123456", active: true, id: 1));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Outro Nome", "500 123 456");
        Assert.Equal(SupplierCreationStatus.Conflict, r.Status);
        Assert.Equal(1, r.Supplier!.Id);
        Assert.Equal("SUPPLIER_ALREADY_EXISTS", r.Code);
    }

    [Fact] // 2
    public async Task SameNif_Inactive_ReturnsExistingInactive()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Fornecedor B", "500123456", active: false, id: 2));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Fornecedor B", "500123456");
        Assert.Equal(SupplierCreationStatus.Conflict, r.Status);
        Assert.False(r.Supplier!.IsActive);
        Assert.Equal("SUPPLIER_INACTIVE_EXISTS", r.Code);
    }

    [Fact] // 3
    public async Task SameNif_DifferentName_StillBlocks()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Nome Cadastrado", "500123456", id: 3));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Nome Totalmente Diferente", "500123456");
        Assert.Equal(SupplierCreationStatus.Conflict, r.Status);
    }

    [Fact] // 4
    public async Task SameName_SameNif_ReturnsExisting()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Fornecedor C", "111222333", id: 4));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("fornecedor c", "111.222.333");
        Assert.Equal(SupplierCreationStatus.Conflict, r.Status);
        Assert.Equal(4, r.Supplier!.Id);
    }

    [Fact] // 5
    public async Task SameName_DifferentNif_DuplicateSuspected()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Fornecedor D", "111222333", id: 5));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Fornecedor D", "999888777");
        Assert.Equal(SupplierCreationStatus.DuplicateSuspected, r.Status);
        Assert.Single(r.Candidates);
    }

    [Fact] // 6
    public async Task Name_WithAccentsAndPunctuation_Matches()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Cimentos São João, Lda", null, id: 6));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("CIMENTOS SAO JOAO LDA", null);
        Assert.Equal(SupplierCreationStatus.DuplicateSuspected, r.Status); // same name, no NIF → suspected
    }

    [Fact] // 7
    public async Task Nif_FormattedVsUnformatted_Matches()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Fornecedor E", "PT 500 999 111", id: 7));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Qualquer", "PT500999111");
        Assert.Equal(SupplierCreationStatus.Conflict, r.Status);
    }

    [Fact] // 8
    public async Task NoNif_NonexistentName_AllowsCreation()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Existente", "123", id: 8));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Fornecedor Novo Inédito", null);
        Assert.Equal(SupplierCreationStatus.Created, r.Status); // no blocking match → creation allowed
        Assert.Null(r.Supplier);
    }

    [Fact] // 9
    public async Task NoNif_ExistingName_Conflicts()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Fornecedor F", null, id: 9));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Fornecedor F", null);
        Assert.Equal(SupplierCreationStatus.DuplicateSuspected, r.Status);
    }

    [Fact] // 10
    public async Task NoMatch_EmptyDb_AllowsCreation()
    {
        using var ctx = NewContext();
        var r = await NewService(ctx).MatchAsync("Fornecedor Zero", "000111");
        Assert.Equal(SupplierCreationStatus.Created, r.Status);
    }

    [Fact]
    public async Task EmptyName_IsInvalid()
    {
        using var ctx = NewContext();
        var r = await NewService(ctx).MatchAsync("   ", "123");
        Assert.Equal(SupplierCreationStatus.Invalid, r.Status);
    }

    [Fact] // NIF case 7 — confirmation must NOT bypass a NIF conflict (returns before any write)
    public async Task Confirm_DoesNotBypassNifConflict()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Nome Existente", "700111222", id: 20));
        await ctx.SaveChangesAsync();
        // CreateAsync returns the NIF conflict during classification, before reaching persistence.
        var r = await NewService(ctx).CreateAsync(new SupplierCreationInput
        {
            Name = "Nome Diferente",
            TaxId = "700 111 222",
            Origin = "PAYMENT_OCR",
            ConfirmCreateDespiteDuplicate = true
        }, Guid.NewGuid());
        Assert.Equal(SupplierCreationStatus.Conflict, r.Status);
        Assert.Equal(20, r.Supplier!.Id);
    }

    // ── Internal company NIF exclusion (Company.TaxId) ──

    private static void SeedInternalCompany(ApplicationDbContext ctx, int id, string name, string code, string normalizedTaxId)
        => ctx.Companies.Add(new Company { Id = id, Name = name, Code = code, TaxId = normalizedTaxId, IsActive = true });

    [Fact] // Internal NIF must never MATCH as a supplier
    public async Task MatchAsync_InternalCompanyNif_ReturnsInternalCompanyTaxId()
    {
        using var ctx = NewContext();
        SeedInternalCompany(ctx, 1, "AlplaPLASTICO", "APA", "5417567485");
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Zeepack", "5417567485");
        Assert.Equal(SupplierCreationStatus.InternalCompanyTaxId, r.Status);
        Assert.Equal("INTERNAL_COMPANY_TAX_ID", r.Code);
        Assert.Equal(1, r.InternalCompanyId);
        Assert.Equal("AlplaPLASTICO", r.InternalCompanyName);
        Assert.Equal("5417567485", r.InternalCompanyTaxId);
    }

    [Fact] // Formatted internal NIF still resolves (normalization applied to input)
    public async Task MatchAsync_InternalCompanyNif_Formatted_StillBlocks()
    {
        using var ctx = NewContext();
        SeedInternalCompany(ctx, 2, "AlplaSOPRO", "APS", "5001760246");
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Qualquer Fornecedor", "5001-760.246");
        Assert.Equal(SupplierCreationStatus.InternalCompanyTaxId, r.Status);
        Assert.Equal(2, r.InternalCompanyId);
    }

    [Fact] // Internal NIF must never be CREATED as a supplier, even with confirmation
    public async Task CreateAsync_InternalCompanyNif_IsBlockedBeforePersistence()
    {
        using var ctx = NewContext();
        SeedInternalCompany(ctx, 1, "AlplaPLASTICO", "APA", "5417567485");
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).CreateAsync(new SupplierCreationInput
        {
            Name = "Zeepack",
            TaxId = "541 756 7485",
            Origin = "PAYMENT_OCR",
            ConfirmCreateDespiteDuplicate = true
        }, Guid.NewGuid());
        Assert.Equal(SupplierCreationStatus.InternalCompanyTaxId, r.Status);
        Assert.Equal("INTERNAL_COMPANY_TAX_ID", r.Code);
        Assert.Empty(ctx.Suppliers); // nothing persisted
    }

    [Fact] // A NIF not belonging to any internal company passes through to normal matching
    public async Task MatchAsync_NonInternalNif_NotBlockedAsInternal()
    {
        using var ctx = NewContext();
        SeedInternalCompany(ctx, 1, "AlplaPLASTICO", "APA", "5417567485");
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Fornecedor Externo", "500999888");
        Assert.NotEqual(SupplierCreationStatus.InternalCompanyTaxId, r.Status);
        Assert.Equal(SupplierCreationStatus.Created, r.Status); // no blocking match → creation allowed
    }

    // ── Name-only fallback (backs the internal-NIF frontend fallback: re-match by name, no NIF) ──

    [Fact] // Zeepack: NIF discarded → name-only match offers the existing supplier
    public async Task MatchAsync_NameOnly_ExistingSupplier_OffersCandidate()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Zeepack Angola, Lda", "500600700", active: true, id: 30));
        await ctx.SaveChangesAsync();
        // Name-only (no NIF) — mirrors the fallback after the internal NIF was dropped.
        var r = await NewService(ctx).MatchAsync("ZEEPACK ANGOLA LDA", null);
        Assert.Equal(SupplierCreationStatus.DuplicateSuspected, r.Status);
        Assert.Contains(r.Candidates, c => c.Id == 30);
    }

    [Fact] // Inactive supplier surfaces by name with its state (no reactivation, no duplicate)
    public async Task MatchAsync_NameOnly_InactiveSupplier_ReturnsInactiveCandidate()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Fornecedor Dormente", null, active: false, id: 31));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Fornecedor Dormente", null);
        Assert.Equal(SupplierCreationStatus.DuplicateSuspected, r.Status);
        Assert.Contains(r.Candidates, c => c.Id == 31 && !c.IsActive);
    }

    [Fact] // Current matching returns the first normalized-name candidate (single). Multi-candidate
    // selection is a shared-component enhancement (see OCR/shared-panel plan), not the simple fallback.
    public async Task MatchAsync_NameOnly_NormalizedEqual_ReturnsCandidate()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Zeepack Angola, Lda", null, active: true, id: 32));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("ZEEPACK ANGOLA LDA", null);
        Assert.Equal(SupplierCreationStatus.DuplicateSuspected, r.Status);
        Assert.Single(r.Candidates);
        Assert.Equal(32, r.Candidates[0].Id);
    }

    [Fact] // No supplier by name → creation without NIF is allowed
    public async Task MatchAsync_NameOnly_NoMatch_AllowsCreation()
    {
        using var ctx = NewContext();
        ctx.Suppliers.Add(Sup("Outro Fornecedor", null, id: 34));
        await ctx.SaveChangesAsync();
        var r = await NewService(ctx).MatchAsync("Fornecedor Inexistente Único", null);
        Assert.Equal(SupplierCreationStatus.Created, r.Status);
    }

    // ── Audit metadata is validated server-side (never trust client-provided ids/names) ──

    [Fact] // Rejected id that really matched the name is accepted for audit
    public void IsPlausibleNameCandidate_MatchingName_ReturnsTrue()
    {
        var suppliers = new[] { (55, "ZEEPACK ANGOLA LDA"), (60, "Outro") };
        Assert.True(SupplierCreationService.IsPlausibleNameCandidate(55, "Zeepack Angola, Lda", suppliers));
    }

    [Fact] // Arbitrary id not in the candidate set is rejected (no false audit)
    public void IsPlausibleNameCandidate_ArbitraryId_ReturnsFalse()
    {
        var suppliers = new[] { (55, "ZEEPACK ANGOLA LDA") };
        Assert.False(SupplierCreationService.IsPlausibleNameCandidate(9999, "Zeepack Angola, Lda", suppliers));
    }

    [Fact] // Existing id whose name does NOT match the submitted name is rejected
    public void IsPlausibleNameCandidate_IdWithDifferentName_ReturnsFalse()
    {
        var suppliers = new[] { (55, "Empresa Totalmente Diferente") };
        Assert.False(SupplierCreationService.IsPlausibleNameCandidate(55, "Zeepack Angola, Lda", suppliers));
    }

    [Fact] // Audit comment uses server-resolved company/supplier values, not client text
    public void BuildPaymentOcrAuditComment_WithResolvedMetadata_UsesDbValues()
    {
        var comment = SupplierCreationService.BuildPaymentOcrAuditComment(
            extractedName: "Zeepack Angola, Lda", extractedTaxId: "5417567485",
            internalCompany: (1, "AlplaPLASTICO", "5417567485"),
            rejectedSupplier: (55, "ZEEPACK ANGOLA LDA"),
            confirmedDespiteDuplicate: true);
        Assert.Contains("empresa interna 'AlplaPLASTICO' (Id 1, NIF 5417567485)", comment);
        Assert.Contains("recusou o fornecedor sugerido pelo nome: 'ZEEPACK ANGOLA LDA' (Id 55)", comment);
        Assert.Contains("sem NIF", comment);
        Assert.Contains("confirmado", comment);
    }

    [Fact] // Without resolved metadata, the extra audit clauses are omitted (no fabricated entities)
    public void BuildPaymentOcrAuditComment_NoMetadata_OmitsClauses()
    {
        var comment = SupplierCreationService.BuildPaymentOcrAuditComment(
            "Fornecedor X", "500600700", internalCompany: null, rejectedSupplier: null, confirmedDespiteDuplicate: false);
        Assert.DoesNotContain("empresa interna", comment);
        Assert.DoesNotContain("recusou", comment);
        Assert.DoesNotContain("confirmado", comment);
    }
}
