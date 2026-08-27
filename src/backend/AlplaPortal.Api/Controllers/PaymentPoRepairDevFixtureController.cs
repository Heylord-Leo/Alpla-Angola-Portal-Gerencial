// ─────────────────────────────────────────────────────────────────────────────
// PAYMENT PO-REPAIR DEV HARNESS — synthetic candidates (ZZTEST-PAY-REPAIR-*).
//
// Phase 4B.2 human-acceptance support. Seeds two deterministic PAYMENT requests in the
// disposable DEV prod-clone so a tester can exercise the SysAdmin repair endpoints
// (GET/POST /api/v1/requests/admin/payment-po-repair/*) end-to-end WITHOUT touching any
// historical request:
//   ZZTEST-PAY-REPAIR-SAFE   — APPROVED, multi-document, linked item, NO groups → SAFE_TO_REPAIR
//   ZZTEST-PAY-REPAIR-UNSAFE — APPROVED, NO groups, but a P.O. attachment      → MANUAL_REVIEW
//
// It is NOT product functionality. Defense-in-depth keeps it out of TEST/PROD:
//   (A) compile-time — the whole controller is inside #if DEBUG (a NotFound stub in Release);
//   (B) runtime env  — IWebHostEnvironment.IsDevelopment() must be true;
//   (C) explicit opt-in — configuration DevFixtures:PaymentPoRepairEnabled must be true
//                         (set only in local, gitignored appsettings.Development.json).
// Any gate not satisfied → every endpoint returns 404.
//
// Fixture identity: RequestNumber starts with "ZZTEST-PAY-REPAIR". Reset removes only those
// synthetic rows and their children.
// ─────────────────────────────────────────────────────────────────────────────
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlplaPortal.Api.Controllers;

[ApiController]
[Route("api/v1/dev/payment-po-repair-fixtures")]
#if DEBUG
public class PaymentPoRepairDevFixtureController : BaseController
{
    private const string NumberPrefix = "ZZTEST-PAY-REPAIR";
    private const string SafeNumber = "ZZTEST-PAY-REPAIR-SAFE";
    private const string UnsafeNumber = "ZZTEST-PAY-REPAIR-UNSAFE";

    private readonly IJwtService _jwt;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public PaymentPoRepairDevFixtureController(
        ApplicationDbContext context, IJwtService jwt, IWebHostEnvironment env, IConfiguration config) : base(context)
    {
        _jwt = jwt;
        _env = env;
        _config = config;
    }

    /// <summary>Guards B (Development) + C (DevFixtures:PaymentPoRepairEnabled), on top of #if DEBUG (A).</summary>
    private bool HarnessEnabled => _env.IsDevelopment() && _config.GetValue<bool>("DevFixtures:PaymentPoRepairEnabled");

    private async Task<(Guid userId, User? user)> ResolveActorAsync()
    {
        var adminId = await _context.UserRoleAssignments
            .Include(x => x.Role)
            .Where(x => x.Role.RoleName == RoleConstants.SystemAdministrator)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync();
        User? user = adminId != Guid.Empty ? await _context.Users.FindAsync(adminId) : null;
        user ??= await _context.Users.Where(u => u.IsActive).FirstOrDefaultAsync();
        return (user?.Id ?? Guid.Empty, user);
    }

    /// <summary>A SystemAdministrator token so the tester can drive the real repair endpoints.</summary>
    [HttpGet("token")]
    public async Task<IActionResult> Token()
    {
        if (!HarnessEnabled) return NotFound();
        var (userId, user) = await ResolveActorAsync();
        if (user == null) return BadRequest("No admin/active user found in clone.");
        var token = _jwt.GenerateToken(user, new List<string> { RoleConstants.SystemAdministrator });
        return Ok(new { token, userId, email = user.Email, fullName = user.FullName });
    }

    [HttpGet("state")]
    public async Task<IActionResult> State()
    {
        if (!HarnessEnabled) return NotFound();
        var reqs = await _context.Requests
            .Where(r => r.RequestNumber != null && r.RequestNumber.StartsWith(NumberPrefix))
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .OrderBy(r => r.RequestNumber)
            .ToListAsync();

        var result = new List<object>();
        foreach (var r in reqs)
        {
            var groupCount = await _context.RequestPoGroups.CountAsync(g => g.RequestId == r.Id);
            var docCount = await _context.PaymentSourceDocuments.CountAsync(d => d.RequestId == r.Id && !d.IsVoided);
            result.Add(new
            {
                r.Id,
                r.RequestNumber,
                r.Title,
                Status = r.Status?.Code,
                r.ApprovedAtUtc,
                LineItemCount = r.LineItems.Count(li => !li.IsDeleted),
                SourceDocumentCount = docCount,
                PoGroupCount = groupCount
            });
        }
        return Ok(result);
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        if (!HarnessEnabled) return NotFound();
        var removed = await ResetInternalAsync();
        await _context.SaveChangesAsync();
        return Ok(new { message = "Fixtures ZZTEST-PAY-REPAIR removidas.", removed });
    }

    private async Task<object> ResetInternalAsync()
    {
        var reqIds = await _context.Requests
            .Where(r => r.RequestNumber != null && r.RequestNumber.StartsWith(NumberPrefix))
            .Select(r => r.Id)
            .ToListAsync();

        if (reqIds.Count == 0) return new { requests = 0 };

        // Delete children before parents — every payment FK here is NoAction, so a parent cannot be
        // removed while a child still points at it.
        _context.RequestLineItems.RemoveRange(_context.RequestLineItems.Where(li => reqIds.Contains(li.RequestId)));
        _context.PaymentSourceDocuments.RemoveRange(_context.PaymentSourceDocuments.Where(d => reqIds.Contains(d.RequestId)));
        _context.RequestPoGroups.RemoveRange(_context.RequestPoGroups.Where(g => reqIds.Contains(g.RequestId)));
        _context.RequestAttachments.RemoveRange(_context.RequestAttachments.Where(a => reqIds.Contains(a.RequestId)));
        _context.RequestStatusHistories.RemoveRange(_context.RequestStatusHistories.Where(h => reqIds.Contains(h.RequestId)));
        await _context.SaveChangesAsync();
        _context.Requests.RemoveRange(_context.Requests.Where(r => reqIds.Contains(r.Id)));

        return new { requests = reqIds.Count };
    }

    [HttpPost("seed")]
    public async Task<IActionResult> Seed()
    {
        if (!HarnessEnabled) return NotFound();

        await ResetInternalAsync();
        await _context.SaveChangesAsync();

        var (actorId, _) = await ResolveActorAsync();
        if (actorId == Guid.Empty) return BadRequest("No active user to attribute fixtures to.");

        var paymentTypeId = await _context.RequestTypes
            .Where(t => t.Code == RequestConstants.Types.Payment).Select(t => t.Id).FirstAsync();
        var approvedStatusId = await _context.RequestStatuses
            .Where(s => s.Code == RequestConstants.Statuses.FinalApproved).Select(s => s.Id).FirstAsync();
        var currencyId = await _context.Currencies.Where(c => c.Code == "AOA").Select(c => (int?)c.Id).FirstOrDefaultAsync()
                         ?? await _context.Currencies.Select(c => c.Id).FirstAsync();
        var company = await _context.Companies.OrderBy(c => c.Id).FirstAsync();
        var plant = await _context.Plants.Where(p => p.CompanyId == company.Id).OrderBy(p => p.Id).FirstOrDefaultAsync()
                    ?? await _context.Plants.OrderBy(p => p.Id).FirstAsync();
        var dept = await _context.Departments.OrderBy(d => d.Id).FirstAsync();
        var supplier = await _context.Suppliers.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (supplier == null) return BadRequest("No supplier in clone to attribute the fixture to.");
        var unitId = await _context.Units.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync();
        var now = DateTime.UtcNow;

        Request NewPaymentRequest(string number, string title, int? reqSupplierId) => new()
        {
            Id = Guid.NewGuid(),
            RequestNumber = number,
            Title = title,
            Description = "Synthetic Phase 4B.2 repair-acceptance fixture. Safe to mutate/reset.",
            RequestTypeId = paymentTypeId,
            StatusId = approvedStatusId,
            RequesterId = actorId,
            CreatedByUserId = actorId,
            DepartmentId = dept.Id,
            CompanyId = company.Id,
            PlantId = plant.Id,
            CurrencyId = currencyId,
            SupplierId = reqSupplierId,
            EstimatedTotalAmount = 100m,
            PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid,
            ApprovedAtUtc = now.AddDays(-5),
            ApprovedTotalAmount = 100m,
            ApprovedCurrencyCode = "AOA",
            CreatedAtUtc = now.AddDays(-10),
            RequestedDateUtc = now.AddDays(-10)
        };

        RequestAttachment NewAttachment(Guid requestId, string typeCode, string fileName) => new()
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            AttachmentTypeCode = typeCode,
            FileName = fileName,
            FileExtension = "pdf",
            FileSizeMBytes = 0.01m,
            StorageReference = "dev-fixture://" + fileName,
            UploadedByUserId = actorId,
            UploadedAtUtc = now.AddDays(-6),
            IsDeleted = false
        };

        // ── SAFE — multi-document, one linked item, no groups, no downstream evidence ──
        var safe = NewPaymentRequest(SafeNumber, "[ZZTEST-PAY-REPAIR] SAFE multi-document candidate", reqSupplierId: null);
        _context.Requests.Add(safe);

        var safeAttachment = NewAttachment(safe.Id, RequestAttachment.TYPE_PAYMENT_SOURCE_DOCUMENT, "safe-proforma.pdf");
        _context.RequestAttachments.Add(safeAttachment);

        var safeDoc = new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = safe.Id,
            AttachmentId = safeAttachment.Id,
            SupplierId = supplier.Id,
            SupplierNameSnapshot = supplier.Name,
            SupplierTaxIdSnapshot = supplier.TaxId,
            PlantId = plant.Id,
            SourceDocumentType = "PROFORMA",
            DocumentNumber = "ZZ-SAFE-001",
            DocumentDate = now.AddDays(-8),
            DueDate = now.AddDays(20),
            Currency = "AOA",
            NetAmount = 100m,
            TaxAmount = 0m,
            GrossAmount = 100m,
            SequenceNumber = 1,
            IsVoided = false,
            CreatedAtUtc = now.AddDays(-9),
            CreatedByUserId = actorId
        };
        _context.PaymentSourceDocuments.Add(safeDoc);

        _context.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = safe.Id,
            PaymentSourceDocumentId = safeDoc.Id,
            LineNumber = 1,
            Description = "Serviço sintético (SAFE)",
            Quantity = 1,
            UnitId = unitId,
            UnitPrice = 100m,
            TotalAmount = 100m,
            CurrencyId = currencyId,
            DueDate = now.AddDays(20),
            IsDeleted = false
        });

        // ── UNSAFE — APPROVED, no groups, but a legitimate P.O. attachment (downstream evidence) ──
        var unsafeReq = NewPaymentRequest(UnsafeNumber, "[ZZTEST-PAY-REPAIR] UNSAFE downstream evidence", reqSupplierId: supplier.Id);
        _context.Requests.Add(unsafeReq);
        _context.RequestAttachments.Add(NewAttachment(unsafeReq.Id, RequestAttachment.TYPE_PO, "unsafe-po.pdf"));
        _context.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = unsafeReq.Id,
            LineNumber = 1,
            Description = "Serviço sintético (UNSAFE)",
            Quantity = 1,
            UnitId = unitId,
            UnitPrice = 100m,
            TotalAmount = 100m,
            CurrencyId = currencyId,
            DueDate = now.AddDays(20),
            IsDeleted = false
        });

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Fixtures ZZTEST-PAY-REPAIR criadas.",
            safe = new { safe.Id, safe.RequestNumber, safe.Title, expectedClassification = "SAFE_TO_REPAIR" },
            unsafeCandidate = new { unsafeReq.Id, unsafeReq.RequestNumber, unsafeReq.Title, expectedClassification = "MANUAL_REVIEW" }
        });
    }
}
#else
public class PaymentPoRepairDevFixtureController : ControllerBase { }
#endif
