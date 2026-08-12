using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

using Doc = RequestConstants.OperationInvoiceDocumentStatuses;
using Agg = RequestConstants.OperationInvoiceStatuses;
using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// Release 4 Phase 3A — REAL-PROVIDER concurrency pin (SQL Server LocalDB).
///
/// <para>The InMemory suites prove the coverage POLICY; this test proves the MECHANISM: the
/// [Timestamp] rowversion on RequestPoGroup, combined with the forced group touch that
/// <c>RederiveAsync(forceGroupTouch: true)</c> performs on effective-coverage writes.</para>
///
/// <para>The scenario is the dangerous one where the forced touch is the ONLY protection: a
/// group expects 1,000,000 with 900,000 already validated, and two pending invoices of 100,000
/// each are validated by two independent contexts racing for the remaining 100,000. Because a
/// pending allocation remains in both writers' views, BOTH derive the same aggregate status the
/// group already has (PENDING_VALIDATION) — so without the forced touch neither writer would
/// modify the group row at all, both would commit, and validated coverage would silently reach
/// 1,100,000. With the forced touch, the second writer's UPDATE runs against a stale rowversion
/// and the provider raises DbUpdateConcurrencyException — the exception the controllers turn
/// into the structured 409 concurrency conflict.</para>
///
/// <para>Requires SQL Server LocalDB (MSSQLLocalDB) — present on the project's Windows dev and
/// build machines; CI does not execute tests on non-Windows runners.</para>
/// </summary>
public class OperationInvoiceCoverageRowVersionSqlTests
{
    private static DbContextOptions<ApplicationDbContext> SqlOptions(string dbName) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                $@"Server=(localdb)\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;" +
                "MultipleActiveResultSets=true;Connection Timeout=60")
            .Options;

    [Fact]
    public async Task Two_writers_racing_for_the_last_remaining_coverage_conflict_on_the_group_rowversion()
    {
        var dbName = "AlplaPortal_ZZTEST_Rv_" + Guid.NewGuid().ToString("N")[..12];
        var options = SqlOptions(dbName);

        try
        {
            Guid requestId, groupId, invoiceBId, invoiceCId;

            // ── Arrange: real schema, minimal seeded graph ──
            await using (var seedCtx = new ApplicationDbContext(options))
            {
                await seedCtx.Database.EnsureCreatedAsync();

                // Identity columns and unique indexes are real on this provider, and EnsureCreated
                // applies the model's own HasData seeds — so master data is LOOKED UP where the
                // model seeds it and inserted (id database-generated) only where it does not.
                var actor = new User { Id = Guid.NewGuid(), FullName = "Rv Tester", Email = "rv@test.local" };
                var department = new Department { Name = "ZZTEST Dept" };
                var company = new Company { Name = "ZZTEST Co" };
                var supplier = new Supplier { Name = "ZZTEST Supplier", TaxId = "111000111" };
                seedCtx.Users.Add(actor);
                seedCtx.Departments.Add(department);
                seedCtx.Companies.Add(company);
                seedCtx.Suppliers.Add(supplier);

                var requestType = await seedCtx.RequestTypes
                        .FirstOrDefaultAsync(t => t.Code == RequestConstants.Types.Payment)
                    ?? seedCtx.RequestTypes.Add(new RequestType
                    {
                        Code = RequestConstants.Types.Payment, Name = "Pagamento"
                    }).Entity;
                var requestStatus = await seedCtx.RequestStatuses
                        .FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.Paid)
                    ?? seedCtx.RequestStatuses.Add(new RequestStatus
                    {
                        Code = RequestConstants.Statuses.Paid, Name = "Pago", DisplayOrder = 30
                    }).Entity;
                await seedCtx.SaveChangesAsync();

                var plant = new Plant { Name = "ZZTEST Plant", CompanyId = company.Id };
                seedCtx.Plants.Add(plant);
                await seedCtx.SaveChangesAsync();

                var request = new Request
                {
                    Id = Guid.NewGuid(),
                    RequestNumber = "ZZTEST-RV-" + Guid.NewGuid().ToString("N")[..8],
                    Title = "ZZTEST rowversion race",
                    RequestTypeId = requestType.Id,
                    StatusId = requestStatus.Id,
                    RequesterId = actor.Id,
                    DepartmentId = department.Id,
                    CompanyId = company.Id,
                    PlantId = plant.Id,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
                };
                seedCtx.Requests.Add(request);
                requestId = request.Id;

                var group = new RequestPoGroup
                {
                    Id = Guid.NewGuid(),
                    RequestId = requestId,
                    SupplierId = supplier.Id,
                    SupplierNameSnapshot = "ZZTEST Supplier",
                    CurrencyCode = "AOA",
                    TotalAmount = 1_000_000m,
                    Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
                    SourceDocumentType = Types.Proforma,
                    OperationInvoiceStatus = Agg.PendingValidation,   // pending drafts exist below
                    RequiresOperationInvoice = true,
                    ExpectedOperationInvoiceTotal = 1_000_000m,
                    ExpectedOperationInvoiceCurrency = "AOA",
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
                    CreatedByUserId = actor.Id
                };
                seedCtx.RequestPoGroups.Add(group);
                groupId = group.Id;

                OperationInvoice AddInvoice(string number, decimal gross, string status)
                {
                    var attachment = new RequestAttachment
                    {
                        Id = Guid.NewGuid(),
                        RequestId = requestId,
                        FileName = "fatura.pdf",
                        FileExtension = ".pdf",
                        AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
                        StorageReference = "zztest/rv-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
                        UploadedByUserId = actor.Id,
                        UploadedAtUtc = DateTime.UtcNow.AddDays(-2)
                    };
                    seedCtx.RequestAttachments.Add(attachment);
                    var invoice = new OperationInvoice
                    {
                        RequestId = requestId,
                        AttachmentId = attachment.Id,
                        SupplierId = supplier.Id,
                        DocumentNumber = number,
                        Currency = "AOA",
                        GrossAmount = gross,
                        Status = status,
                        UploadedAtUtc = DateTime.UtcNow.AddDays(-2),
                        UploadedByUserId = actor.Id
                    };
                    seedCtx.OperationInvoices.Add(invoice);
                    return invoice;
                }

                // 900k already validated; B and C race for the remaining 100k.
                var validated = AddInvoice("FT RV-VAL", 900_000m, Doc.Validated);
                var invoiceB = AddInvoice("FT RV-B", 100_000m, Doc.PendingValidation);
                var invoiceC = AddInvoice("FT RV-C", 100_000m, Doc.PendingValidation);
                invoiceBId = invoiceB.Id;
                invoiceCId = invoiceC.Id;

                var sequence = 1;
                foreach (var invoice in new[] { validated, invoiceB, invoiceC })
                {
                    seedCtx.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
                    {
                        OperationInvoiceId = invoice.Id,
                        RequestPoGroupId = groupId,
                        AllocatedGrossAmount = invoice.GrossAmount!.Value,
                        SequenceNumber = sequence++,
                        CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
                        CreatedByUserId = actor.Id
                    });
                }

                await seedCtx.SaveChangesAsync();
            }

            // ── Act: two independent contexts, both load the group BEFORE either saves ──
            await using var ctxA = new ApplicationDbContext(options);
            await using var ctxB = new ApplicationDbContext(options);

            // Writer B first materializes its stale view of the world (validates C).
            var invoiceCEntity = await ctxB.OperationInvoices.SingleAsync(i => i.Id == invoiceCId);
            invoiceCEntity.Status = Doc.Validated;
            var serviceB = new OperationInvoiceCoverageService(ctxB);
            var changesB = await serviceB.RederiveAsync(new[] { groupId }, forceGroupTouch: true);
            // B still sees B pending → same aggregate status → the touch is the ONLY group write.
            Assert.False(changesB.Single().StatusChanged);

            // Writer A validates B and commits first.
            var invoiceBEntity = await ctxA.OperationInvoices.SingleAsync(i => i.Id == invoiceBId);
            invoiceBEntity.Status = Doc.Validated;
            var serviceA = new OperationInvoiceCoverageService(ctxA);
            var changesA = await serviceA.RederiveAsync(new[] { groupId }, forceGroupTouch: true);
            Assert.False(changesA.Single().StatusChanged);   // C pending in A's view too
            await ctxA.SaveChangesAsync();                   // bumps the group rowversion

            // Writer B commits against the now-stale rowversion → the SQL mechanism must refuse.
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());

            // ── Assert: exactly one writer's coverage landed ──
            await using var verifyCtx = new ApplicationDbContext(options);
            Assert.Equal(Doc.Validated,
                (await verifyCtx.OperationInvoices.SingleAsync(i => i.Id == invoiceBId)).Status);
            Assert.Equal(Doc.PendingValidation,
                (await verifyCtx.OperationInvoices.SingleAsync(i => i.Id == invoiceCId)).Status);

            var effectiveCoverage = await verifyCtx.OperationInvoiceAllocations
                .Join(verifyCtx.OperationInvoices.Where(i => i.Status == Doc.Validated),
                    a => a.OperationInvoiceId, i => i.Id,
                    (a, i) => a.AllocatedGrossAmount)
                .SumAsync();
            Assert.Equal(1_000_000m, effectiveCoverage);     // never 1,100,000
        }
        finally
        {
            await using var dropCtx = new ApplicationDbContext(options);
            await dropCtx.Database.EnsureDeletedAsync();
        }
    }
}
