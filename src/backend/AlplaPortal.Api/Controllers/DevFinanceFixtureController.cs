// ─────────────────────────────────────────────────────────────────────────────
// FINANCE DEV REGRESSION HARNESS — synthetic Finance fixtures (ZZTEST-FIN-*).
//
// Official, permanent DEV-ONLY maintenance tool. It seeds / inspects / resets
// deterministic ZZTEST-FIN-* scenarios in the disposable DEV prod-clone so the
// /finance/payments screen and every Finance mutation endpoint can be exercised
// end-to-end WITHOUT touching any historical (real) request. It never runs
// production business logic itself — the transitions under test are driven through
// the REAL Finance endpoints over HTTP. See docs/FINANCE_DEV_REGRESSION_HARNESS.md.
//
// It is NOT product functionality. Defense-in-depth keeps it out of TEST/PROD:
//   (A) compile-time  — the whole controller is inside #if DEBUG (a NotFound stub
//                        in Release, like DevSeedingController);
//   (B) runtime env    — IWebHostEnvironment.IsDevelopment() must be true;
//   (C) explicit opt-in — configuration DevFixtures:FinanceEnabled must be true
//                        (set only in local, gitignored appsettings.Development.json).
// Any gate not satisfied → every endpoint returns 404 (HarnessEnabled guard).
//
// Fixture identity: RequestNumber starts with "ZZTEST-FIN-" AND Title starts with
// "[ZZTEST-FIN]". Reset removes only those synthetic rows and their children — it is
// never a generic database-mutation API and never targets historical requests.
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
[Route("api/v1/dev/fin-fixtures")]
#if DEBUG
public class DevFinanceFixtureController : BaseController
{
    private const string NumberPrefix = "ZZTEST-FIN-";
    private const string TitlePrefix = "[ZZTEST-FIN]";

    private readonly IJwtService _jwt;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public DevFinanceFixtureController(
        ApplicationDbContext context, IJwtService jwt, IWebHostEnvironment env, IConfiguration config) : base(context)
    {
        _jwt = jwt;
        _env = env;
        _config = config;
    }

    /// <summary>Guards B (Development environment) + C (explicit DevFixtures:FinanceEnabled opt-in),
    /// on top of the #if DEBUG guard A. When false every endpoint is 404 — the harness is invisible.</summary>
    private bool HarnessEnabled => _env.IsDevelopment() && _config.GetValue<bool>("DevFixtures:FinanceEnabled");

    // ── Mint a SystemAdministrator+Finance token so the harness can drive the real
    //    Finance endpoints with global scope. Uses a real active admin user for FK-valid
    //    audit attribution. ──
    [HttpGet("token")]
    public async Task<IActionResult> Token()
    {
        if (!HarnessEnabled) return NotFound();
        var (userId, user) = await ResolveActorAsync();
        if (user == null) return BadRequest("No admin/active user found in clone.");
        var token = _jwt.GenerateToken(user, new List<string> { RoleConstants.SystemAdministrator, RoleConstants.Finance, RoleConstants.Buyer });
        return Ok(new { token, userId, email = user.Email, fullName = user.FullName });
    }

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

    [HttpGet("state")]
    public async Task<IActionResult> State()
    {
        if (!HarnessEnabled) return NotFound();
        var reqs = await _context.Requests
            .Where(r => r.RequestNumber != null && r.RequestNumber.StartsWith(NumberPrefix))
            .Include(r => r.Status)
            .Include(r => r.Company)
            .Include(r => r.Plant)
            .Include(r => r.Department)
            .Include(r => r.PoGroups).ThenInclude(g => g.Payments)
            .OrderBy(r => r.RequestNumber)
            .ToListAsync();

        var reqIds = reqs.Select(r => r.Id).ToList();
        var histories = await _context.RequestStatusHistories
            .Where(h => reqIds.Contains(h.RequestId))
            .OrderBy(h => h.CreatedAtUtc)
            .Select(h => new { h.RequestId, h.ActionTaken, h.PreviousStatusId, h.NewStatusId, h.Comment, h.CreatedAtUtc })
            .ToListAsync();

        var attachments = await _context.RequestAttachments
            .Where(a => reqIds.Contains(a.RequestId))
            .Select(a => new { a.Id, a.RequestId, a.RequestPoGroupId, a.AttachmentTypeCode, a.IsDeleted, a.VoidedAtUtc })
            .ToListAsync();

        // Request-level payment ledger (includes GROUP-LESS rows, e.g. the reconciliation remaining
        // balance, which never appear under a group's Payments collection).
        var allPayments = await _context.RequestPayments
            .Where(p => reqIds.Contains(p.RequestId))
            .OrderBy(p => p.PaymentType).ThenBy(p => p.PaymentSequence)
            .Select(p => new { p.RequestId, p.RequestPoGroupId, p.PaymentType, p.PaymentStatus, p.PaymentSequence, p.PlannedAmount, p.ActualPaidAmount, p.CreatedByUserId, p.PaidByUserId, HasProof = p.PaymentProofAttachmentId != null })
            .ToListAsync();

        var result = reqs.Select(r => new
        {
            r.RequestNumber,
            r.Title,
            RequestStatus = r.Status != null ? r.Status.Code : null,
            Company = r.Company != null ? r.Company.Name : null,
            r.CompanyId,
            Plant = r.Plant != null ? r.Plant.Name : null,
            r.PlantId,
            Department = r.Department != null ? r.Department.Name : null,
            r.DepartmentId,
            Groups = r.PoGroups.OrderBy(g => g.CreatedAtUtc).Select(g => new
            {
                g.Id,
                g.Status,
                g.TotalAmount,
                g.PurchaseOrderNumber,
                g.SupplierNameSnapshot,
                g.SupplierNifSnapshot,
                g.CurrencyCode,
                Payments = g.Payments.OrderBy(p => p.Id).Select(p => new
                {
                    p.Id, p.PaymentType, p.PaymentStatus, p.PaymentSequence,
                    p.PlannedAmount, p.ActualPaidAmount, p.ScheduledDateUtc, p.PaidDateUtc,
                    HasProof = p.PaymentProofAttachmentId != null
                })
            }),
            Attachments = attachments.Where(a => a.RequestId == r.Id),
            AllPayments = allPayments.Where(p => p.RequestId == r.Id),
            History = histories.Where(h => h.RequestId == r.Id)
                .Select(h => new { h.ActionTaken, h.PreviousStatusId, h.NewStatusId, h.Comment, h.CreatedAtUtc })
        });

        return Ok(result);
    }

    // ── Create a DEV-safe PAYMENT_PROOF attachment for a group and return its id, so the
    //    real /pay endpoint (which mandates a proof) can associate a real attachment. ──
    public class ProofRequest { public Guid RequestId { get; set; } public Guid GroupId { get; set; } }

    [HttpPost("proof")]
    public async Task<IActionResult> CreateProof([FromBody] ProofRequest body)
    {
        if (!HarnessEnabled) return NotFound();
        var (userId, _) = await ResolveActorAsync();
        var att = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = body.RequestId,
            AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_PROOF,
            FileName = "zztest-fin-proof.pdf",
            FileExtension = "pdf",
            FileSizeMBytes = 0.01m,
            StorageReference = "zztest-fin/fake-proof.pdf",
            UploadedByUserId = userId,
            UploadedAtUtc = DateTime.UtcNow,
            IsDeleted = false
        };
        _context.RequestAttachments.Add(att);
        await _context.SaveChangesAsync();
        return Ok(new { attachmentId = att.Id });
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        if (!HarnessEnabled) return NotFound();
        var removed = await ResetInternalAsync();
        await _context.SaveChangesAsync();
        return Ok(new { message = "Fixtures ZZTEST-FIN removidas.", removed });
    }

    private async Task<object> ResetInternalAsync()
    {
        var reqs = await _context.Requests
            .Where(r => (r.RequestNumber != null && r.RequestNumber.StartsWith(NumberPrefix))
                     || (r.Title != null && r.Title.StartsWith(TitlePrefix)))
            .Select(r => r.Id)
            .ToListAsync();

        var payments = _context.RequestPayments.Where(p => reqs.Contains(p.RequestId));
        _context.RequestPayments.RemoveRange(payments);

        var atts = _context.RequestAttachments.Where(a => reqs.Contains(a.RequestId));
        _context.RequestAttachments.RemoveRange(atts);

        var hist = _context.RequestStatusHistories.Where(h => reqs.Contains(h.RequestId));
        _context.RequestStatusHistories.RemoveRange(hist);

        // Reconciliations (created by the RECON scenario's ReconcileRequest run) reference the
        // request/group and must be removed before the groups and requests.
        var recons = _context.RequestReconciliations.Where(rc => reqs.Contains(rc.RequestId));
        _context.RequestReconciliations.RemoveRange(recons);

        var items = _context.RequestLineItems.Where(li => reqs.Contains(li.RequestId));
        _context.RequestLineItems.RemoveRange(items);

        var groups = _context.RequestPoGroups.Where(g => reqs.Contains(g.RequestId));
        _context.RequestPoGroups.RemoveRange(groups);

        var reqEntities = _context.Requests.Where(r => reqs.Contains(r.Id));
        _context.Requests.RemoveRange(reqEntities);

        // Synthetic suppliers created for the ADV/RECON scenarios (RegisterPo requires a real SupplierId).
        var suppliers = _context.Suppliers.Where(s => s.Name.StartsWith("ZZTEST-FIN"));
        _context.Suppliers.RemoveRange(suppliers);

        return new { requests = reqs.Count };
    }

    [HttpPost("seed")]
    public async Task<IActionResult> Seed()
    {
        if (!HarnessEnabled) return NotFound();
        try
        {
            await ResetInternalAsync();
            await _context.SaveChangesAsync();

            var (actorId, _) = await ResolveActorAsync();
            if (actorId == Guid.Empty) return BadRequest("No active user to attribute fixtures to.");

            var qType = await _context.RequestTypes.Where(t => t.Code == RequestConstants.Types.Quotation).Select(t => t.Id).FirstAsync();
            var statusIdByCode = await _context.RequestStatuses.ToDictionaryAsync(s => s.Code, s => s.Id);
            int Sid(string code) => statusIdByCode.TryGetValue(code, out var v) ? v : statusIdByCode[RequestConstants.Statuses.PoIssued];

            var aoaId = await _context.Currencies.Where(c => c.Code == "AOA").Select(c => (int?)c.Id).FirstOrDefaultAsync()
                        ?? await _context.Currencies.Select(c => c.Id).FirstAsync();

            // Organizational units — resolved dynamically against the clone (PROD data).
            var companies = await _context.Companies.OrderBy(c => c.Id).ToListAsync();
            var plants = await _context.Plants.OrderBy(p => p.Id).ToListAsync();
            var departments = await _context.Departments.OrderBy(d => d.Id).ToListAsync();

            var plastico = companies.FirstOrDefault(c => c.Name.ToUpper().Contains("PLASTICO")) ?? companies.First();
            var sopro = companies.FirstOrDefault(c => c.Name.ToUpper().Contains("SOPRO"))
                        ?? companies.FirstOrDefault(c => c.Id != plastico.Id) ?? plastico;

            Plant PlantOf(Company co, string nameHint)
                => plants.FirstOrDefault(p => p.CompanyId == co.Id && (p.Name ?? "").ToUpper().Contains(nameHint.ToUpper()))
                   ?? plants.FirstOrDefault(p => p.CompanyId == co.Id)
                   ?? plants.First();

            var plantViana1 = PlantOf(plastico, "Viana 1");
            var plantViana3 = PlantOf(sopro, "Viana 3");

            var deptTI = departments.FirstOrDefault(d => (d.Name ?? "").ToUpper() == "TI"
                        || (d.Name ?? "").ToUpper().Contains("INFORMÁ") || (d.Name ?? "").ToUpper().Contains("INFORMA"))
                        ?? departments.First();
            var deptRH = departments.FirstOrDefault(d => (d.Name ?? "").ToUpper().Contains("RECURSOS HUMANOS")
                        || (d.Name ?? "").ToUpper() == "RH")
                        ?? departments.FirstOrDefault(d => d.Id != deptTI.Id) ?? deptTI;

            // Default org for scenarios A–I: AlplaPLASTICO / its Viana-1 plant / TI department.
            var defCompany = plastico;
            var defPlant = plantViana1;
            var defDept = deptTI;

            var today = DateTime.UtcNow.Date;
            var now = DateTime.UtcNow;
            var map = new List<object>();

            // ── local builders ──
            Request NewReq(string letter, string desc, string statusCode, Company co, Plant plant, Department dept, int daysOldCreated)
            {
                var req = new Request
                {
                    Id = Guid.NewGuid(),
                    RequestNumber = NumberPrefix + letter,
                    Title = $"{TitlePrefix} {desc}",
                    Description = "Synthetic Finance acceptance fixture. Safe to mutate/reset.",
                    RequestTypeId = qType,
                    StatusId = Sid(statusCode),
                    RequesterId = actorId,
                    CreatedByUserId = actorId,
                    DepartmentId = dept.Id,
                    CompanyId = co.Id,
                    PlantId = plant.Id,
                    CurrencyId = aoaId,
                    EstimatedTotalAmount = 0m,
                    NeedByDateUtc = today.AddDays(30),
                    CreatedAtUtc = now.AddDays(-daysOldCreated),
                    RequestedDateUtc = now.AddDays(-daysOldCreated)
                };
                _context.Requests.Add(req);
                return req;
            }

            RequestPoGroup NewGroup(Request req, string status, decimal amount, string supName, string supNif, string? po, Plant plant)
            {
                var g = new RequestPoGroup
                {
                    Id = Guid.NewGuid(),
                    RequestId = req.Id,
                    Status = status,
                    TotalAmount = amount,
                    SupplierNameSnapshot = supName,
                    SupplierNifSnapshot = supNif,
                    CurrencyId = aoaId,
                    CurrencyCode = "AOA",
                    PlantId = plant.Id,
                    PurchaseOrderNumber = po,
                    CreatedByUserId = actorId,
                    CreatedAtUtc = now
                };
                _context.RequestPoGroups.Add(g);
                return g;
            }

            // NOTE: PaymentSequence is unique per (RequestId, PaymentType) — request-scoped, NOT
            // per-group. Seeded multi-payment requests therefore assign distinct sequences.
            void NewScheduledPayment(Request req, RequestPoGroup g, DateTime scheduled, int seq = 1)
            {
                _context.RequestPayments.Add(new RequestPayment
                {
                    RequestId = req.Id,
                    RequestPoGroupId = g.Id,
                    PaymentType = RequestPayment.PaymentTypes.FinalBalance,
                    PaymentSequence = seq,
                    PlannedAmount = g.TotalAmount,
                    CurrencyCode = "AOA",
                    ScheduledDateUtc = scheduled,
                    ScheduledByUserId = actorId,
                    PaymentStatus = RequestPayment.PaymentStatuses.Scheduled,
                    CreatedByUserId = actorId,
                    CreatedAtUtc = now
                });
            }

            void NewCompletedPayment(Request req, RequestPoGroup g, int seq = 1)
            {
                _context.RequestPayments.Add(new RequestPayment
                {
                    RequestId = req.Id,
                    RequestPoGroupId = g.Id,
                    PaymentType = RequestPayment.PaymentTypes.FinalBalance,
                    PaymentSequence = seq,
                    PlannedAmount = g.TotalAmount,
                    ActualPaidAmount = g.TotalAmount,
                    CurrencyCode = "AOA",
                    PaidDateUtc = today.AddDays(-3),
                    PaidByUserId = actorId,
                    PaymentStatus = RequestPayment.PaymentStatuses.Completed,
                    CreatedByUserId = actorId,
                    CreatedAtUtc = now
                });
            }

            void AddPo(Request req, RequestPoGroup g)
            {
                _context.RequestAttachments.Add(new RequestAttachment
                {
                    Id = Guid.NewGuid(),
                    RequestId = req.Id,
                    RequestPoGroupId = g.Id,
                    AttachmentTypeCode = RequestAttachment.TYPE_PO,
                    FileName = "zztest-fin-po.pdf",
                    FileExtension = "pdf",
                    FileSizeMBytes = 0.01m,
                    StorageReference = "zztest-fin/po.pdf",
                    UploadedByUserId = actorId,
                    UploadedAtUtc = now,
                    IsDeleted = false
                });
            }

            void Record(string letter, Request req, params RequestPoGroup[] groups)
                => map.Add(new { scenario = letter, requestId = req.Id, requestNumber = req.RequestNumber, groups = groups.Select(g => new { g.Id, key = g.Status, g.Status, g.TotalAmount }) });

            // Synthetic supplier — RegisterPo requires a real, non-DRAFT SupplierId on the group.
            var zzSupplier = new Supplier
            {
                Name = "ZZTEST-FIN Fornecedor ADV",
                TaxId = "5401990500",
                IsActive = true,
                Origin = "MANUAL",
                RegistrationStatus = "ACTIVE"
            };
            _context.Suppliers.Add(zzSupplier);
            await _context.SaveChangesAsync(); // materialize identity Id for the FK

            // A. SINGLE GROUP — PO_ISSUED
            var a = NewReq("A", "Cenário A — Grupo único PO_ISSUED", RequestConstants.Statuses.PoIssued, defCompany, defPlant, defDept, 2);
            var ag = NewGroup(a, RequestConstants.PoGroupStatuses.PoIssued, 150000m, "ZZTEST Fornecedor Alfa", "5401990001", "ZZ-PO-A-001", defPlant);
            Record("A", a, ag);

            // B. SINGLE GROUP — PAYMENT_SCHEDULED (future)
            var b = NewReq("B", "Cenário B — PAYMENT_SCHEDULED futuro", RequestConstants.Statuses.PaymentScheduled, defCompany, defPlant, defDept, 4);
            var bg = NewGroup(b, RequestConstants.PoGroupStatuses.PaymentScheduled, 220000m, "ZZTEST Fornecedor Bravo", "5401990002", "ZZ-PO-B-001", defPlant);
            NewScheduledPayment(b, bg, today.AddDays(7));
            Record("B", b, bg);

            // C. SINGLE GROUP — OVERDUE PAYMENT_SCHEDULED (5 days past)
            var c = NewReq("C", "Cenário C — PAYMENT_SCHEDULED vencido", RequestConstants.Statuses.PaymentScheduled, defCompany, defPlant, defDept, 12);
            var cg = NewGroup(c, RequestConstants.PoGroupStatuses.PaymentScheduled, 90000m, "ZZTEST Fornecedor Charlie", "5401990003", "ZZ-PO-C-001", defPlant);
            NewScheduledPayment(c, cg, today.AddDays(-5));
            Record("C", c, cg);

            // D. MULTI-GROUP — PAYMENT_COMPLETED sibling + PO_ISSUED sibling
            var d = NewReq("D", "Cenário D — Pago + PO_ISSUED (isolamento)", RequestConstants.Statuses.PoIssued, defCompany, defPlant, defDept, 8);
            var d1 = NewGroup(d, RequestConstants.PoGroupStatuses.PaymentCompleted, 300000m, "ZZTEST Fornecedor Delta-1", "5401990041", "ZZ-PO-D-001", defPlant);
            NewCompletedPayment(d, d1);
            var d2 = NewGroup(d, RequestConstants.PoGroupStatuses.PoIssued, 175000m, "ZZTEST Fornecedor Delta-2", "5401990042", "ZZ-PO-D-002", defPlant);
            Record("D", d, d1, d2);

            // E. MULTI-GROUP — PAYMENT_COMPLETED sibling + PAYMENT_SCHEDULED sibling
            var e = NewReq("E", "Cenário E — Pago + Agendado (isolamento)", RequestConstants.Statuses.PaymentScheduled, defCompany, defPlant, defDept, 8);
            var e1 = NewGroup(e, RequestConstants.PoGroupStatuses.PaymentCompleted, 260000m, "ZZTEST Fornecedor Echo-1", "5401990051", "ZZ-PO-E-001", defPlant);
            NewCompletedPayment(e, e1);
            var e2 = NewGroup(e, RequestConstants.PoGroupStatuses.PaymentScheduled, 140000m, "ZZTEST Fornecedor Echo-2", "5401990052", "ZZ-PO-E-002", defPlant);
            NewScheduledPayment(e, e2, today.AddDays(5), seq: 2);
            Record("E", e, e1, e2);

            // F. MULTI-GROUP — Return-for-adjustment target (PAYMENT_COMPLETED + PO_ISSUED)
            var f = NewReq("F", "Cenário F — Devolução de grupo (isolamento)", RequestConstants.Statuses.PoIssued, defCompany, defPlant, defDept, 6);
            var f1 = NewGroup(f, RequestConstants.PoGroupStatuses.PaymentCompleted, 410000m, "ZZTEST Fornecedor Foxtrot-1", "5401990061", "ZZ-PO-F-001", defPlant);
            NewCompletedPayment(f, f1);
            var f2 = NewGroup(f, RequestConstants.PoGroupStatuses.PoIssued, 95000m, "ZZTEST Fornecedor Foxtrot-2", "5401990062", "ZZ-PO-F-002", defPlant);
            Record("F", f, f1, f2);

            // G. ADVANCE PAYMENT — ADVANCE_PAYMENT_REQUIRED
            var g = NewReq("G", "Cenário G — Adiantamento requerido", RequestConstants.Statuses.AdvancePaymentRequired, defCompany, defPlant, defDept, 5);
            var gg = NewGroup(g, RequestConstants.PoGroupStatuses.AdvancePaymentRequired, 500000m, "ZZTEST Fornecedor Golf", "5401990007", "ZZ-PO-G-001", defPlant);
            gg.AdvancePaymentPercent = 30m;
            // RegisterPo creates a PLANNED Advance RequestPayment when a group requires an advance;
            // the b2p/schedule-advance endpoint reschedules THAT row (it does not create one). Seed it.
            _context.RequestPayments.Add(new RequestPayment
            {
                RequestId = g.Id,
                RequestPoGroupId = gg.Id,
                PaymentType = RequestPayment.PaymentTypes.Advance,
                PaymentSequence = 1,
                PlannedPercent = 30m,
                PlannedAmount = 150000m,
                CurrencyCode = "AOA",
                PaymentStatus = RequestPayment.PaymentStatuses.Planned,
                CreatedByUserId = actorId,
                CreatedAtUtc = now
            });
            Record("G", g, gg);

            // H. WAITING_PO — Buyer responsibility
            var h = NewReq("H", "Cenário H — WAITING_PO (Comprador)", RequestConstants.Statuses.PoRequested, defCompany, defPlant, defDept, 3);
            var hg = NewGroup(h, RequestConstants.PoGroupStatuses.WaitingPo, 80000m, "ZZTEST Fornecedor Hotel", "5401990008", null, defPlant);
            Record("H", h, hg);

            // I. NOTES — start with zero notes (PO_ISSUED so it surfaces)
            var i = NewReq("I", "Cenário I — Observações", RequestConstants.Statuses.PoIssued, defCompany, defPlant, defDept, 1);
            var ig = NewGroup(i, RequestConstants.PoGroupStatuses.PoIssued, 130000m, "ZZTEST Fornecedor India", "5401990009", "ZZ-PO-I-001", defPlant);
            Record("I", i, ig);

            // K. DIRECT PAY probe — single group PO_ISSUED (never scheduled)
            var k = NewReq("K", "Cenário K — Pagamento direto de PO_ISSUED", RequestConstants.Statuses.PoIssued, defCompany, defPlant, defDept, 1);
            var kg = NewGroup(k, RequestConstants.PoGroupStatuses.PoIssued, 123456m, "ZZTEST Fornecedor Kilo", "5401990011", "ZZ-PO-K-001", defPlant);
            Record("K", k, kg);

            // J1. ORG — AlplaPLASTICO / Viana 1 / TI
            var j1 = NewReq("J1", "Cenário J1 — PLASTICO/Viana1/TI", RequestConstants.Statuses.PoIssued, plastico, plantViana1, deptTI, 2);
            var j1g = NewGroup(j1, RequestConstants.PoGroupStatuses.PoIssued, 111000m, "ZZTEST Fornecedor Juliet-1", "5401990101", "ZZ-PO-J1-001", plantViana1);
            Record("J1", j1, j1g);

            // J2. ORG — AlplaSOPRO / Viana 3 / Recursos Humanos
            var j2 = NewReq("J2", "Cenário J2 — SOPRO/Viana3/RH", RequestConstants.Statuses.PoIssued, sopro, plantViana3, deptRH, 2);
            var j2g = NewGroup(j2, RequestConstants.PoGroupStatuses.PoIssued, 222000m, "ZZTEST Fornecedor Juliet-2", "5401990102", "ZZ-PO-J2-001", plantViana3);
            Record("J2", j2, j2g);

            // ADV. Two sibling groups both requiring an advance (WAITING_PO + real SupplierId + PO doc),
            // to drive RequestsController.RegisterPo (Buyer) twice → ADVANCE seq1 then seq2.
            var adv = NewReq("ADV", "Cenário ADV — dois grupos c/ adiantamento", RequestConstants.Statuses.PoRequested, defCompany, defPlant, defDept, 2);
            var adv1 = NewGroup(adv, RequestConstants.PoGroupStatuses.WaitingPo, 400000m, "ZZTEST-FIN Fornecedor ADV", "5401990500", null, defPlant);
            adv1.SupplierId = zzSupplier.Id;
            var adv2 = NewGroup(adv, RequestConstants.PoGroupStatuses.WaitingPo, 250000m, "ZZTEST-FIN Fornecedor ADV", "5401990500", null, defPlant);
            adv2.SupplierId = zzSupplier.Id;
            AddPo(adv, adv1);
            AddPo(adv, adv2);
            Record("ADV", adv, adv1, adv2);

            // RECON. One group already owns a FINAL_BALANCE seq1 (completed 200k); request is
            // WAITING_RECONCILIATION so ReconcileRequest (Finance) creates a group-less remaining-balance
            // FINAL_BALANCE → must land on seq2.
            var recon = NewReq("RECON", "Cenário RECON — saldo remanescente", RequestConstants.Statuses.WaitingReconciliation, defCompany, defPlant, defDept, 3);
            var recong = NewGroup(recon, RequestConstants.PoGroupStatuses.WaitingReconciliation, 200000m, "ZZTEST Fornecedor Recon", "5401990600", "ZZ-PO-RECON-001", defPlant);
            _context.RequestPayments.Add(new RequestPayment
            {
                RequestId = recon.Id,
                RequestPoGroupId = recong.Id,
                PaymentType = RequestPayment.PaymentTypes.FinalBalance,
                PaymentSequence = 1,
                PlannedAmount = 200000m,
                ActualPaidAmount = 200000m,
                CurrencyCode = "AOA",
                PaidDateUtc = today.AddDays(-3),
                PaidByUserId = actorId,
                PaymentStatus = RequestPayment.PaymentStatuses.Completed,
                CreatedByUserId = actorId,
                CreatedAtUtc = now
            });
            Record("RECON", recon, recong);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Fixtures ZZTEST-FIN semeadas.",
                actorUserId = actorId,
                org = new
                {
                    plastico = new { plastico.Id, plastico.Name },
                    sopro = new { sopro.Id, sopro.Name },
                    plantViana1 = new { plantViana1.Id, plantViana1.Name, plantViana1.CompanyId },
                    plantViana3 = new { plantViana3.Id, plantViana3.Name, plantViana3.CompanyId },
                    deptTI = new { deptTI.Id, deptTI.Name },
                    deptRH = new { deptRH.Id, deptRH.Name }
                },
                fixtures = map
            });
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new ProblemDetails { Title = "DB error seeding fixtures", Detail = ex.InnerException?.Message ?? ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new ProblemDetails { Title = "Error seeding fixtures", Detail = ex.Message });
        }
    }
}
#else
public class DevFinanceFixtureController : ControllerBase { [HttpGet] public IActionResult Index() => NotFound(); }
#endif
