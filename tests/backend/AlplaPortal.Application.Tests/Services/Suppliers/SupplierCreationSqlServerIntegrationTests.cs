using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Suppliers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Suppliers;

/// <summary>
/// Real SQL Server (LocalDB) evidence for the DRAFT persistence, provenance/audit and PortalCode
/// generation (FromSqlRaw + unique indexes) that the EF in-memory provider cannot exercise.
/// Skipped automatically when LocalDB is unavailable (e.g. CI). Test rows are prefixed ZZTEST_ and
/// deleted in a finally block so the dev database is left unchanged.
/// </summary>
public class SupplierCreationSqlServerIntegrationTests
{
    private const string Conn =
        @"Server=(localdb)\MSSQLLocalDB;Database=AlplaPortalV1;Trusted_Connection=True;TrustServerCertificate=True";

    private static bool CanConnect()
    {
        try { using var c = new SqlConnection(Conn); c.Open(); return true; }
        catch { return false; }
    }

    private static DbContextOptions<ApplicationDbContext> Options()
        => new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(Conn).Options;

    [Fact]
    public async Task CreateAsync_PersistsDraftWithProvenanceAndAudit_OnSqlServer()
    {
        if (!CanConnect()) return; // inconclusive/skip when LocalDB is not present

        var uniqueName = "ZZTEST_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var uniqueNif = "ZZ" + Guid.NewGuid().ToString("N").Substring(0, 8);
        int? createdId = null;

        // The audit trail (SupplierStatusHistory.ActorUserId) has an FK to Users, so use a real user id
        // (in production the actor is always the authenticated user).
        Guid actor;
        await using (var seed = new ApplicationDbContext(Options()))
        {
            var existing = await seed.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
            if (existing == Guid.Empty) return; // no users seeded → skip
            actor = existing;
        }

        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new SupplierCreationService(ctx, NullLogger<SupplierCreationService>.Instance);
                var res = await svc.CreateAsync(new SupplierCreationInput
                {
                    Name = uniqueName,
                    TaxId = uniqueNif,
                    Origin = "PAYMENT_OCR",
                    ExtractedName = uniqueName,
                    ExtractedTaxId = uniqueNif,
                    // Administrative fields intentionally absent — the service sets DRAFT/active/null Primavera.
                }, actor);

                Assert.Equal(SupplierCreationStatus.Created, res.Status);
                createdId = res.Supplier!.Id;
            }

            await using (var verify = new ApplicationDbContext(Options()))
            {
                var sup = await verify.Suppliers.AsNoTracking().FirstAsync(s => s.Id == createdId);
                Assert.Equal("DRAFT", sup.RegistrationStatus);   // DRAFT
                Assert.True(sup.IsActive);                       // active per existing DRAFT rule
                Assert.Null(sup.PrimaveraCode);                  // never set via this flow
                Assert.Equal("PAYMENT_OCR", sup.Origin);         // provenance
                Assert.Equal(actor.ToString(), sup.CreatedByUserId);
                Assert.False(string.IsNullOrEmpty(sup.PortalCode));
                Assert.StartsWith("SUP-", sup.PortalCode);       // unique PortalCode generated

                var history = await verify.SupplierStatusHistories.AsNoTracking()
                    .Where(h => h.SupplierId == createdId).ToListAsync();
                Assert.Contains(history, h => h.EventType == "CREATED" && h.Comment.Contains("OCR de pagamento"));
            }

            // Exactly one CREATED history entry (not one per controller + service).
            await using (var histCtx = new ApplicationDbContext(Options()))
            {
                var createdCount = await histCtx.SupplierStatusHistories.AsNoTracking()
                    .CountAsync(h => h.SupplierId == createdId && h.EventType == "CREATED");
                Assert.Equal(1, createdCount);
            }

            // Same NIF, different name → creation blocked by the application matching (returns existing/conflict).
            await using (var ctx2 = new ApplicationDbContext(Options()))
            {
                var svc2 = new SupplierCreationService(ctx2, NullLogger<SupplierCreationService>.Instance);
                var dup = await svc2.CreateAsync(new SupplierCreationInput
                {
                    Name = uniqueName + "_OTHER",
                    TaxId = uniqueNif,
                    Origin = "PAYMENT_OCR"
                }, actor);
                Assert.Equal(SupplierCreationStatus.Conflict, dup.Status); // matched by NIF
            }
        }
        finally
        {
            if (createdId != null)
            {
                await using var cleanup = new ApplicationDbContext(Options());
                await cleanup.Database.ExecuteSqlRawAsync(
                    "DELETE FROM SupplierStatusHistories WHERE SupplierId = {0}; DELETE FROM Suppliers WHERE Id = {0};",
                    createdId.Value);
            }
        }
    }

    [Fact]
    public async Task CreateAsync_NoNif_ResolvesAuditServerSide_OnSqlServer()
    {
        if (!CanConnect()) return;

        // Same normalized name, but DIFFERENT raw form (Supplier.Name has a unique index on the raw value):
        // seeded "ZZTESTNFabc LDA" vs created "zztestnfabc, lda" both normalize to "ZZTESTNFABC LDA".
        var baseName = "ZZTESTNF" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var seededName = baseName + " LDA";
        var createdName = baseName.ToLowerInvariant() + ", lda";
        int seededId = 0, createdId = 0, bogusId = 0;
        Guid actor;
        await using (var seed = new ApplicationDbContext(Options()))
        {
            var existing = await seed.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
            if (existing == Guid.Empty) return;
            actor = existing;
            // Seed the "existing" supplier the user will decline (same normalized name → plausible candidate).
            var s = new AlplaPortal.Domain.Entities.Supplier { Name = seededName, PortalCode = "ZZ-" + Guid.NewGuid().ToString("N").Substring(0, 8), IsActive = true, RegistrationStatus = "ACTIVE", Origin = "MANUAL" };
            seed.Suppliers.Add(s);
            await seed.SaveChangesAsync();
            seededId = s.Id;
        }
        try
        {
            // Create WITHOUT a NIF, declining the seeded supplier, with a valid internal NIF (AlplaPLASTICO = 5417567485).
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new SupplierCreationService(ctx, NullLogger<SupplierCreationService>.Instance);
                var res = await svc.CreateAsync(new SupplierCreationInput
                {
                    Name = createdName,              // same normalized name as seeded → requires confirmation
                    TaxId = null,                    // no NIF
                    Origin = "PAYMENT_OCR",
                    ExtractedName = createdName,
                    ConfirmCreateDespiteDuplicate = true,
                    InternalCompanyTaxIdExtracted = "5417567485", // valid internal NIF
                    RejectedSuggestedSupplierId = seededId          // plausible (same normalized name)
                }, actor);
                Assert.Equal(SupplierCreationStatus.Created, res.Status);
                createdId = res.Supplier!.Id;
            }
            await using (var verify = new ApplicationDbContext(Options()))
            {
                var sup = await verify.Suppliers.AsNoTracking().FirstAsync(s => s.Id == createdId);
                Assert.Null(sup.TaxId);                          // #6/#7 — no NIF persisted, internal NIF never stored
                Assert.Equal("DRAFT", sup.RegistrationStatus);
                Assert.Equal("PAYMENT_OCR", sup.Origin);

                var hist = await verify.SupplierStatusHistories.AsNoTracking().Where(h => h.SupplierId == createdId).ToListAsync();
                Assert.Single(hist);                             // #10 — exactly one CREATED entry
                Assert.Equal("CREATED", hist[0].EventType);
                Assert.Contains("AlplaPLASTICO", hist[0].Comment);        // resolved internal company name (DB)
                Assert.Contains($"(Id {seededId})", hist[0].Comment);     // resolved rejected supplier id (DB)

                // #9 — declining/seeding did not create a CREATED history for the existing supplier.
                var seededHist = await verify.SupplierStatusHistories.AsNoTracking().CountAsync(h => h.SupplierId == seededId);
                Assert.Equal(0, seededHist);
            }

            // Bogus metadata (non-internal NIF + arbitrary rejected id) must NOT fabricate audit.
            var bogusName = "ZZTEST_BG_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            await using (var ctx2 = new ApplicationDbContext(Options()))
            {
                var svc = new SupplierCreationService(ctx2, NullLogger<SupplierCreationService>.Instance);
                var res = await svc.CreateAsync(new SupplierCreationInput
                {
                    Name = bogusName, TaxId = null, Origin = "PAYMENT_OCR", ExtractedName = bogusName,
                    InternalCompanyTaxIdExtracted = "0000BOGUS999",  // not an internal company
                    RejectedSuggestedSupplierId = 987654321          // arbitrary id
                }, actor);
                Assert.Equal(SupplierCreationStatus.Created, res.Status);
                bogusId = res.Supplier!.Id;
            }
            await using (var verify2 = new ApplicationDbContext(Options()))
            {
                var hist = await verify2.SupplierStatusHistories.AsNoTracking().Where(h => h.SupplierId == bogusId).ToListAsync();
                Assert.Single(hist);
                Assert.DoesNotContain("empresa interna", hist[0].Comment); // #2 — false internal claim ignored
                Assert.DoesNotContain("recusou", hist[0].Comment);          // #4 — arbitrary rejected id ignored
            }
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(Options());
            foreach (var id in new[] { createdId, bogusId, seededId })
                if (id != 0)
                    await cleanup.Database.ExecuteSqlRawAsync(
                        "DELETE FROM SupplierStatusHistories WHERE SupplierId = {0}; DELETE FROM Suppliers WHERE Id = {0};", id);
        }
    }

    [Fact]
    public async Task CreateAsync_GeneralManualOrigin_RecordsSingleManualAudit_OnSqlServer()
    {
        if (!CanConnect()) return;

        var uniqueName = "ZZTEST_M_" + Guid.NewGuid().ToString("N").Substring(0, 10);
        int? createdId = null;
        Guid actor;
        await using (var seed = new ApplicationDbContext(Options()))
        {
            var existing = await seed.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync();
            if (existing == Guid.Empty) return;
            actor = existing;
        }
        try
        {
            await using (var ctx = new ApplicationDbContext(Options()))
            {
                var svc = new SupplierCreationService(ctx, NullLogger<SupplierCreationService>.Instance);
                var res = await svc.CreateAsync(new SupplierCreationInput { Name = uniqueName, Origin = "MANUAL" }, actor);
                Assert.Equal(SupplierCreationStatus.Created, res.Status);
                createdId = res.Supplier!.Id;
            }
            await using (var verify = new ApplicationDbContext(Options()))
            {
                var sup = await verify.Suppliers.AsNoTracking().FirstAsync(s => s.Id == createdId);
                Assert.Equal("MANUAL", sup.Origin); // general endpoint origin is MANUAL (not PAYMENT_OCR)
                var hist = await verify.SupplierStatusHistories.AsNoTracking().Where(h => h.SupplierId == createdId).ToListAsync();
                Assert.Single(hist);
                Assert.Equal("CREATED", hist[0].EventType);
                Assert.DoesNotContain("OCR", hist[0].Comment); // manual comment, not payment-OCR
            }
        }
        finally
        {
            if (createdId != null)
            {
                await using var cleanup = new ApplicationDbContext(Options());
                await cleanup.Database.ExecuteSqlRawAsync(
                    "DELETE FROM SupplierStatusHistories WHERE SupplierId = {0}; DELETE FROM Suppliers WHERE Id = {0};",
                    createdId.Value);
            }
        }
    }
}
