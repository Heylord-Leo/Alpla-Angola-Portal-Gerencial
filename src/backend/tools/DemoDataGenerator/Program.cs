using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Constants;

namespace DemoDataGenerator;

class Program
{
    public static async Task Main(string[] args)
    {
        bool isDryRun = args.Contains("--dry-run");
        bool isCleanAndSeed = args.Contains("--clean") && args.Contains("--seed");
        bool isRepairAttachments = args.Contains("--repair-attachments");
        bool isPatch = args.Contains("--patch");

        if (!isDryRun && !isCleanAndSeed && !isRepairAttachments && !isPatch)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- --dry-run");
            Console.WriteLine("  dotnet run -- --clean --seed");
            Console.WriteLine("  dotnet run -- --repair-attachments");
            Console.WriteLine("  dotnet run -- --patch");
            return;
        }

        var connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=AlplaPortalV1;Trusted_Connection=True;MultipleActiveResultSets=true";
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        using var db = new ApplicationDbContext(optionsBuilder.Options);

        if (isDryRun)
        {
            await RunDryRun(db);
        }
        else if (isCleanAndSeed)
        {
            await CleanData(db);
            await SeedData(db);
        }
        else if (isRepairAttachments)
        {
            await RepairAttachments(db);
        }
        else if (isPatch)
        {
            await ApplyPatch(db);
        }
    }

    static async Task ApplyPatch(ApplicationDbContext db)
    {
        Console.WriteLine("Applying patch to existing demo requests...");

        var defaultUser = await db.Users.FirstOrDefaultAsync(u => u.Email == "leonardo.cintra@alpla.com") 
            ?? await db.Users.FirstOrDefaultAsync() 
            ?? throw new Exception("No user found.");

        var requests = await db.Requests.Where(r => r.Title.StartsWith("[DEMO]")).OrderBy(r => r.CreatedAtUtc).ToListAsync();
        
        var waitingArea = await db.RequestStatuses.FirstAsync(s => s.Code == RequestConstants.Statuses.WaitingAreaApproval);
        var waitingFinal = await db.RequestStatuses.FirstAsync(s => s.Code == RequestConstants.Statuses.WaitingFinalApproval);

        foreach(var r in requests)
        {
            r.AreaApproverId = defaultUser.Id;
        }

        for(int i=0; i<Math.Min(5, requests.Count); i++)
        {
            requests[i].StatusId = waitingArea.Id;
        }

        for(int i=5; i<Math.Min(10, requests.Count); i++)
        {
            requests[i].StatusId = waitingFinal.Id;
        }

        await db.SaveChangesAsync();
        Console.WriteLine("Patch applied successfully.");
    }

    static async Task CleanData(ApplicationDbContext db)
    {
        Console.WriteLine("Running cleanup...");
        var demoRequests = await db.Requests.Where(r => r.Title.StartsWith("[DEMO]")).ToListAsync();
        if (demoRequests.Any())
        {
            var requestIds = demoRequests.Select(r => r.Id).ToList();
            
            var attachments = await db.RequestAttachments.Where(a => requestIds.Contains(a.RequestId)).ToListAsync();
            db.RequestAttachments.RemoveRange(attachments);

            var histories = await db.RequestStatusHistories.Where(h => requestIds.Contains(h.RequestId)).ToListAsync();
            db.RequestStatusHistories.RemoveRange(histories);

            var quotations = await db.Quotations.Where(q => requestIds.Contains(q.RequestId)).ToListAsync();
            db.Quotations.RemoveRange(quotations);

            var lineItems = await db.RequestLineItems.Where(l => requestIds.Contains(l.RequestId)).ToListAsync();
            db.RequestLineItems.RemoveRange(lineItems);

            db.Requests.RemoveRange(demoRequests);
            
            await db.SaveChangesAsync();
            Console.WriteLine($"Deleted {demoRequests.Count} demo requests and all related data.");
        }
        else
        {
            Console.WriteLine("No demo requests found to clean.");
        }
    }

    static async Task RunDryRun(ApplicationDbContext db)
    {
        Console.WriteLine("=== DRY RUN MODE ===");
        
        var suppliers = await db.Suppliers.CountAsync();
        var items = await db.ItemCatalogItems.CountAsync();
        var users = await db.Users.CountAsync();
        
        Console.WriteLine($"Found {suppliers} Suppliers, {items} Items, {users} Users.");
        Console.WriteLine("Planned creation:");
        Console.WriteLine("- 50 Total Requests (March-April 2026)");
        Console.WriteLine("- 28 Quotation Flow Requests");
        Console.WriteLine("- 22 Payment Flow Requests");
        Console.WriteLine("- Generated PDFs saved to: C:\\dev\\alpla-portal\\.tmp\\demo-data\\documents\\");
        Console.WriteLine("No data will be modified.");
    }

    static async Task SeedData(ApplicationDbContext db)
    {
        Console.WriteLine("=== SEED MODE ===");

        // Fetch master data
        var defaultUser = await db.Users.FirstOrDefaultAsync() ?? throw new Exception("No user found.");
        var suppliers = await db.Suppliers.Take(10).ToListAsync();
        var departments = await db.Departments.ToListAsync();
        var plants = await db.Plants.ToListAsync();
        var currencies = await db.Currencies.ToListAsync();
        var statuses = await db.RequestStatuses.ToListAsync();
        var reqTypes = await db.RequestTypes.ToListAsync();
        var ivaRates = await db.IvaRates.ToListAsync();
        
        var purchaseType = reqTypes.FirstOrDefault(rt => rt.Code == "PURCHASE") ?? reqTypes.First();
        var kwzCurrency = currencies.FirstOrDefault(c => c.Code == "AOA") ?? currencies.First();
        var usdCurrency = currencies.FirstOrDefault(c => c.Code == "USD") ?? currencies.First();

        string documentsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "data", "attachments"));
        Directory.CreateDirectory(documentsPath);

        int totalRequests = 50;
        var random = new Random(12345); // deterministic
        var startDate = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= totalRequests; i++)
        {
            var isPaymentFlow = i > 28; // First 28 Quotation, next 22 Payment
            
            var reqDate = startDate.AddDays(random.Next(0, 60)); // Distribute across Mar-Apr
            var supplier = suppliers[random.Next(suppliers.Count)];
            var dept = departments[random.Next(departments.Count)];
            var plant = plants[random.Next(plants.Count)];
            var currency = random.NextDouble() > 0.3 ? kwzCurrency : usdCurrency;
            
            // Build Request
            var request = new Request
            {
                Id = Guid.NewGuid(),
                RequestTypeId = purchaseType.Id,
                Title = $"[DEMO] {(isPaymentFlow ? "Payment" : "Quotation")} - {supplier.Name}",
                Description = "Generated demo data for March-April 2026 workflow validation.",
                DepartmentId = dept.Id,
                PlantId = plant.Id,
                CurrencyId = currency.Id,
                CreatedAtUtc = reqDate,
                CreatedByUserId = defaultUser.Id,
                RequesterId = defaultUser.Id,
                CompanyId = plant.CompanyId,
                RequestedDateUtc = reqDate,
                EstimatedTotalAmount = random.Next(1000, 50000),
                AreaApproverId = defaultUser.Id // Explicitly assign Leonardo as the Area Approver
            };

            // Quotation details
            var quotation = new Quotation
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                SupplierId = supplier.Id,
                SupplierNameSnapshot = supplier.Name,
                Currency = currency.Code,
                CreatedAtUtc = reqDate.AddHours(1),
                CreatedByUserId = defaultUser.Id,
                TotalAmount = request.EstimatedTotalAmount,
                SourceType = "MANUAL",
                DocumentDate = reqDate,
                DocumentNumber = $"DOC-{random.Next(10000, 99999)}",
                IsSelected = true
            };

            request.SelectedQuotationId = quotation.Id;

            var draftStatus = statuses.First(s => s.Code == RequestConstants.Statuses.Draft);
            var waitingArea = statuses.First(s => s.Code == RequestConstants.Statuses.WaitingAreaApproval);
            var waitingFinal = statuses.First(s => s.Code == RequestConstants.Statuses.WaitingFinalApproval);

            // Set final status based on flow type and some randomness to leave a few in pending approval
            RequestStatus finalStatus;
            bool isPendingArea = i % 10 == 0; // 10% waiting area approval
            bool isPendingFinal = i % 10 == 1; // 10% waiting final approval

            if (isPendingArea)
            {
                finalStatus = waitingArea;
            }
            else if (isPendingFinal)
            {
                finalStatus = waitingFinal;
            }
            else
            {
                finalStatus = isPaymentFlow 
                    ? statuses.First(s => s.Code == RequestConstants.Statuses.PaymentCompleted) 
                    : statuses.First(s => s.Code == RequestConstants.Statuses.FinalApproved);
            }

            request.StatusId = finalStatus.Id;

            // Histories
            var histories = new List<RequestStatusHistory>
            {
                new() { Id = Guid.NewGuid(), RequestId = request.Id, PreviousStatusId = null, NewStatusId = draftStatus.Id, ActorUserId = defaultUser.Id, CreatedAtUtc = reqDate, ActionTaken = "Created", Comment = "Created" },
                new() { Id = Guid.NewGuid(), RequestId = request.Id, PreviousStatusId = draftStatus.Id, NewStatusId = waitingArea.Id, ActorUserId = defaultUser.Id, CreatedAtUtc = reqDate.AddHours(1), ActionTaken = "Submitted", Comment = "Submitted" }
            };

            if (!isPendingArea)
            {
                histories.Add(new() { Id = Guid.NewGuid(), RequestId = request.Id, PreviousStatusId = waitingArea.Id, NewStatusId = waitingFinal.Id, ActorUserId = defaultUser.Id, CreatedAtUtc = reqDate.AddHours(2), ActionTaken = "Area Approved", Comment = "Area Approved" });
                
                if (!isPendingFinal)
                {
                    histories.Add(new() { Id = Guid.NewGuid(), RequestId = request.Id, PreviousStatusId = waitingFinal.Id, NewStatusId = finalStatus.Id, ActorUserId = defaultUser.Id, CreatedAtUtc = reqDate.AddHours(3), ActionTaken = "Approved", Comment = "Approved" });
                }
            }

            // Generate PDFs
            var attachments = new List<RequestAttachment>();
            
            // 1. Proforma
            var proformaBytes = DocumentGenerator.GenerateProforma(supplier.Name, request.Title, quotation.TotalAmount);
            var proformaAttId = Guid.NewGuid();
            var proformaFileName = $"{proformaAttId}.pdf";
            File.WriteAllBytes(Path.Combine(documentsPath, proformaFileName), proformaBytes);
            attachments.Add(new RequestAttachment
            {
                Id = proformaAttId, RequestId = request.Id, FileName = "Proforma.pdf", FileExtension = ".pdf",
                FileSizeMBytes = proformaBytes.Length / 1024m / 1024m, AttachmentTypeCode = AttachmentConstants.Types.Proforma,
                StorageReference = proformaFileName, UploadedByUserId = defaultUser.Id, UploadedAtUtc = reqDate
            });

            // 2. PO
            if (isPaymentFlow || i % 2 == 0) // some quotations have POs
            {
                var poAttId = Guid.NewGuid();
                var poBytes = DocumentGenerator.GeneratePurchaseOrder(supplier.Name, request.Id.ToString().Substring(0, 8), quotation.TotalAmount);
                var poFileName = $"{poAttId}.pdf";
                File.WriteAllBytes(Path.Combine(documentsPath, poFileName), poBytes);
                attachments.Add(new RequestAttachment
                {
                    Id = poAttId, RequestId = request.Id, FileName = "PurchaseOrder.pdf", FileExtension = ".pdf",
                    FileSizeMBytes = poBytes.Length / 1024m / 1024m, AttachmentTypeCode = AttachmentConstants.Types.PurchaseOrder,
                    StorageReference = poFileName, UploadedByUserId = defaultUser.Id, UploadedAtUtc = reqDate.AddDays(1)
                });
            }

            // 3. Payment
            if (isPaymentFlow)
            {
                var schedAttId = Guid.NewGuid();
                var schedBytes = DocumentGenerator.GeneratePaymentProof(request.Id.ToString().Substring(0, 8), "SCHEDULE", quotation.TotalAmount);
                var schedFileName = $"{schedAttId}.pdf";
                File.WriteAllBytes(Path.Combine(documentsPath, schedFileName), schedBytes);
                attachments.Add(new RequestAttachment
                {
                    Id = schedAttId, RequestId = request.Id, FileName = "SchedulingProof.pdf", FileExtension = ".pdf",
                    FileSizeMBytes = schedBytes.Length / 1024m / 1024m, AttachmentTypeCode = AttachmentConstants.Types.PaymentSchedule,
                    StorageReference = schedFileName, UploadedByUserId = defaultUser.Id, UploadedAtUtc = reqDate.AddDays(5)
                });
            }

            db.Requests.Add(request);
            db.Quotations.Add(quotation);
            db.RequestStatusHistories.AddRange(histories);
            db.RequestAttachments.AddRange(attachments);

            // Adding a line item for Price History Simulation
            var iva = ivaRates.First();
            db.RequestLineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(), RequestId = request.Id, ItemCatalogId = null,
                Description = "Simulated Item " + (i % 5), // Repeating items to simulate price history
                Quantity = random.Next(1, 100), UnitPrice = random.Next(10, 500) + (reqDate.Month == 4 ? 20 : 0), // April is more expensive
                IvaRateId = iva.Id, TotalAmount = quotation.TotalAmount, CreatedAtUtc = reqDate,
                CreatedByUserId = defaultUser.Id
            });
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"Successfully seeded 50 demo requests and generated PDFs in {documentsPath}.");
    }

    static async Task RepairAttachments(ApplicationDbContext db)
    {
        Console.WriteLine("=== REPAIR ATTACHMENTS MODE ===");

        // Get all attachments for [DEMO] requests
        var demoRequests = await db.Requests
            .Where(r => r.Title.StartsWith("[DEMO]"))
            .Select(r => r.Id)
            .ToListAsync();

        if (!demoRequests.Any())
        {
            Console.WriteLine("No demo requests found.");
            return;
        }

        var attachments = await db.RequestAttachments
            .Where(a => demoRequests.Contains(a.RequestId))
            .ToListAsync();

        string targetPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "data", "attachments"));
        Directory.CreateDirectory(targetPath);

        string oldTmpPath = @"C:\dev\alpla-portal\.tmp\demo-data\documents";

        int repaired = 0;
        int skipped = 0;
        int notFound = 0;

        foreach (var att in attachments)
        {
            if (att.StorageReference.StartsWith(@"..\.tmp\demo-data\documents\"))
            {
                string oldFileName = att.StorageReference.Replace(@"..\.tmp\demo-data\documents\", "");
                string oldFilePath = Path.Combine(oldTmpPath, oldFileName);
                
                if (File.Exists(oldFilePath))
                {
                    string newFileName = $"{att.Id}{att.FileExtension}";
                    string newFilePath = Path.Combine(targetPath, newFileName);
                    
                    File.Copy(oldFilePath, newFilePath, true);
                    att.StorageReference = newFileName;
                    repaired++;
                }
                else
                {
                    Console.WriteLine($"Warning: Old file not found: {oldFilePath}");
                    notFound++;
                }
            }
            else
            {
                skipped++;
            }
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"Repair complete. Repaired: {repaired}, Skipped (already fixed): {skipped}, Not found: {notFound}");
    }
}
