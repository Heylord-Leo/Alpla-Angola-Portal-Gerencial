// ─────────────────────────────────────────────────────────────────────────────
// BUYER DEV REGRESSION HARNESS — synthetic Buyer/quotation fixtures (ZZTEST-BUY-*).
//
// Official, permanent DEV-ONLY maintenance tool. It seeds / inspects / resets
// deterministic ZZTEST-BUY-* QUOTATION scenarios in the disposable DEV prod-clone so
// the canonical Buyer queue (GET /api/v1/buyer/queue) and the Buyer projection can be
// exercised across every operational state WITHOUT touching any historical request.
// It never runs production business logic itself. See docs/BUYER_DEV_REGRESSION_HARNESS.md.
//
// It is NOT product functionality. Defense-in-depth keeps it out of TEST/PROD:
//   (A) compile-time  — the whole controller is inside #if DEBUG (a NotFound stub in Release);
//   (B) runtime env    — IWebHostEnvironment.IsDevelopment() must be true;
//   (C) explicit opt-in — configuration DevFixtures:BuyerEnabled must be true
//                        (set only in local, gitignored appsettings.Development.json).
// Any gate not satisfied → every endpoint returns 404 (HarnessEnabled guard).
//
// Fixture identity: RequestNumber starts with "ZZTEST-BUY-" AND Title starts with
// "[ZZTEST-BUY]". Reset removes only those synthetic rows and their children.
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
[Route("api/v1/dev/buyer-fixtures")]
#if DEBUG
public class BuyerDevFixtureController : BaseController
{
    private const string NumberPrefix = "ZZTEST-BUY-";
    private const string TitlePrefix = "[ZZTEST-BUY]";

    private readonly IJwtService _jwt;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public BuyerDevFixtureController(
        ApplicationDbContext context, IJwtService jwt, IWebHostEnvironment env, IConfiguration config) : base(context)
    {
        _jwt = jwt;
        _env = env;
        _config = config;
    }

    /// <summary>Guards B (Development) + C (DevFixtures:BuyerEnabled opt-in), on top of #if DEBUG (A).</summary>
    private bool HarnessEnabled => _env.IsDevelopment() && _config.GetValue<bool>("DevFixtures:BuyerEnabled");

    // Mint a SystemAdministrator+Buyer token so the harness can drive the real queue endpoints with
    // global scope (and self-claim). Uses a real active admin user for FK-valid audit attribution.
    [HttpGet("token")]
    public async Task<IActionResult> Token()
    {
        if (!HarnessEnabled) return NotFound();
        var (userId, user) = await ResolveActorAsync();
        if (user == null) return BadRequest("No admin/active user found in clone.");
        var token = _jwt.GenerateToken(user, new List<string> { RoleConstants.SystemAdministrator, RoleConstants.Buyer });
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
            .Include(r => r.NeedLevel)
            .Include(r => r.LineItems)
            .Include(r => r.ApprovalBatches).ThenInclude(b => b.Items)
            .Include(r => r.Quotations).ThenInclude(q => q.Items)
            .OrderBy(r => r.RequestNumber)
            .ToListAsync();

        var result = reqs.Select(r => new
        {
            r.RequestNumber,
            r.Title,
            RequestStatus = r.Status != null ? r.Status.Code : null,
            r.BuyerId,
            NeedLevel = r.NeedLevel != null ? r.NeedLevel.Code : null,
            r.NeedByDateUtc,
            Items = r.LineItems.OrderBy(li => li.LineNumber).Select(li => new
            {
                li.Id, li.LineNumber, li.Description, li.IsDeleted,
                li.QuotationLifecycleStatus, li.SupplierId, li.SupplierName
            }),
            Batches = r.ApprovalBatches.Select(b => new { b.Id, b.BatchNumber, b.Status, ItemCount = b.Items.Count }),
            Quotations = r.Quotations.Select(q => new
            {
                q.Id, q.SupplierNameSnapshot,
                Items = q.Items.Select(qi => new { qi.Id, qi.ReconciliationStatus, qi.MappedRequestLineItemId })
            })
        });
        return Ok(result);
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        if (!HarnessEnabled) return NotFound();
        var removed = await ResetInternalAsync();
        await _context.SaveChangesAsync();
        return Ok(new { message = "Fixtures ZZTEST-BUY removidas.", removed });
    }

    private async Task<object> ResetInternalAsync()
    {
        var reqs = await _context.Requests
            .Where(r => (r.RequestNumber != null && r.RequestNumber.StartsWith(NumberPrefix))
                     || (r.Title != null && r.Title.StartsWith(TitlePrefix)))
            .Select(r => r.Id)
            .ToListAsync();

        // Children first (respect FKs). Candidates → batch items → batches; quotation items → quotations.
        var batchIds = await _context.ApprovalBatches.Where(b => reqs.Contains(b.RequestId)).Select(b => b.Id).ToListAsync();
        var batchItemIds = await _context.Set<ApprovalBatchItem>().Where(bi => batchIds.Contains(bi.ApprovalBatchId)).Select(bi => bi.Id).ToListAsync();
        _context.Set<ApprovalBatchItemCandidate>().RemoveRange(_context.Set<ApprovalBatchItemCandidate>().Where(c => batchItemIds.Contains(c.ApprovalBatchItemId)));
        _context.Set<ApprovalBatchItem>().RemoveRange(_context.Set<ApprovalBatchItem>().Where(bi => batchIds.Contains(bi.ApprovalBatchId)));
        _context.ApprovalBatches.RemoveRange(_context.ApprovalBatches.Where(b => reqs.Contains(b.RequestId)));

        var quotationIds = await _context.Quotations.Where(q => reqs.Contains(q.RequestId)).Select(q => q.Id).ToListAsync();
        _context.QuotationItems.RemoveRange(_context.QuotationItems.Where(qi => quotationIds.Contains(qi.QuotationId)));
        _context.Quotations.RemoveRange(_context.Quotations.Where(q => reqs.Contains(q.RequestId)));

        _context.RequestPoGroups.RemoveRange(_context.RequestPoGroups.Where(g => reqs.Contains(g.RequestId)));
        _context.RequestStatusHistories.RemoveRange(_context.RequestStatusHistories.Where(h => reqs.Contains(h.RequestId)));
        _context.RequestAttachments.RemoveRange(_context.RequestAttachments.Where(a => reqs.Contains(a.RequestId)));
        _context.RequestLineItems.RemoveRange(_context.RequestLineItems.Where(li => reqs.Contains(li.RequestId)));
        _context.Requests.RemoveRange(_context.Requests.Where(r => reqs.Contains(r.Id)));

        // Synthetic suppliers created for the Workspace supplier-carousel scenario.
        _context.Suppliers.RemoveRange(_context.Suppliers.Where(s => s.Name.StartsWith("ZZTEST-BUY")));

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
            int Sid(string code) => statusIdByCode.TryGetValue(code, out var v) ? v : statusIdByCode[RequestConstants.Statuses.WaitingQuotation];

            var needLevelIdByCode = await _context.Set<NeedLevel>().ToDictionaryAsync(n => n.Code, n => n.Id);
            int? Nid(string code) => needLevelIdByCode.TryGetValue(code, out var v) ? v : (int?)null;

            var aoaId = await _context.Currencies.Where(c => c.Code == "AOA").Select(c => (int?)c.Id).FirstOrDefaultAsync()
                        ?? await _context.Currencies.Select(c => c.Id).FirstAsync();

            var company = await _context.Companies.OrderBy(c => c.Id).FirstAsync();
            var plant = await _context.Plants.Where(p => p.CompanyId == company.Id).OrderBy(p => p.Id).FirstOrDefaultAsync()
                        ?? await _context.Plants.OrderBy(p => p.Id).FirstAsync();
            var dept = await _context.Departments.OrderBy(d => d.Id).FirstAsync();

            var today = DateTime.UtcNow.Date;
            var now = DateTime.UtcNow;
            var map = new List<object>();
            var lineSeq = 0;

            Request NewReq(string letter, string desc, string statusCode, Guid? buyerId, DateTime? needBy, string? needLevel)
            {
                var req = new Request
                {
                    Id = Guid.NewGuid(),
                    RequestNumber = NumberPrefix + letter,
                    Title = $"{TitlePrefix} {desc}",
                    Description = "Synthetic Buyer acceptance fixture. Safe to mutate/reset.",
                    RequestTypeId = qType,
                    StatusId = Sid(statusCode),
                    RequesterId = actorId,
                    CreatedByUserId = actorId,
                    BuyerId = buyerId,
                    DepartmentId = dept.Id,
                    CompanyId = company.Id,
                    PlantId = plant.Id,
                    CurrencyId = aoaId,
                    NeedLevelId = needLevel != null ? Nid(needLevel) : null,
                    EstimatedTotalAmount = 0m,
                    NeedByDateUtc = needBy,
                    CreatedAtUtc = now.AddDays(-3),
                    RequestedDateUtc = now.AddDays(-3)
                };
                _context.Requests.Add(req);
                return req;
            }

            RequestLineItem NewItem(Request req, string desc, string? lifecycle)
            {
                var li = new RequestLineItem
                {
                    Id = Guid.NewGuid(),
                    RequestId = req.Id,
                    LineNumber = ++lineSeq,
                    Description = desc,
                    Quantity = 1m,
                    UnitPrice = 1000m,
                    TotalAmount = 1000m,
                    CurrencyId = aoaId,
                    QuotationLifecycleStatus = lifecycle
                };
                _context.RequestLineItems.Add(li);
                return li;
            }

            QuotationItem NewMappedQuotationItem(Request req, RequestLineItem line)
            {
                var quotation = new Quotation
                {
                    Id = Guid.NewGuid(),
                    RequestId = req.Id,
                    SupplierNameSnapshot = "ZZTEST-BUY Fornecedor",
                    Currency = "AOA",
                    TotalAmount = 1000m,
                    SourceType = "MANUAL",
                    CreatedByUserId = actorId,
                    CreatedAtUtc = now
                };
                _context.Quotations.Add(quotation);
                var qi = new QuotationItem
                {
                    Id = Guid.NewGuid(),
                    QuotationId = quotation.Id,
                    Description = line.Description,
                    ReconciliationStatus = RequestConstants.ReconciliationStatuses.Mapped,
                    MappedRequestLineItemId = line.Id
                };
                _context.QuotationItems.Add(qi);
                return qi;
            }

            ApprovalBatch NewBatch(Request req, string status, params RequestLineItem[] lines)
            {
                var batch = new ApprovalBatch
                {
                    Id = Guid.NewGuid(),
                    RequestId = req.Id,
                    BatchNumber = 1,
                    Status = status,
                    CreatedByUserId = actorId,
                    CreatedAtUtc = now
                };
                _context.ApprovalBatches.Add(batch);
                foreach (var line in lines)
                {
                    _context.Set<ApprovalBatchItem>().Add(new ApprovalBatchItem
                    {
                        Id = Guid.NewGuid(),
                        ApprovalBatchId = batch.Id,
                        RequestLineItemId = line.Id,
                        CreatedAtUtc = now
                    });
                }
                return batch;
            }

            void Record(string letter, Request req, string expectedState)
                => map.Add(new { scenario = letter, req.Id, req.RequestNumber, expectedState });

            // B1 — NEEDS_QUOTATION: two pending items, unassigned, no deadline.
            var b1 = NewReq("B1", "Cotação pendente (não atribuído)", RequestConstants.Statuses.WaitingQuotation, null, null, RequestConstants.NeedLevels.Normal);
            NewItem(b1, "Item pendente 1", RequestConstants.QuotationLifecycleStatuses.QuotationPending);
            NewItem(b1, "Item pendente 2", null);
            Record("B1", b1, BuyerQueueConstants.OperationalStates.NeedsQuotation);

            // B2 — READY_FOR_APPROVAL: one pending item with a MAPPED quotation candidate; assigned to actor.
            var b2 = NewReq("B2", "Pronto para aprovação", RequestConstants.Statuses.WaitingQuotation, actorId, today.AddDays(10), RequestConstants.NeedLevels.Normal);
            var b2Line = NewItem(b2, "Item cotado", RequestConstants.QuotationLifecycleStatuses.QuotationPending);
            NewMappedQuotationItem(b2, b2Line);
            Record("B2", b2, BuyerQueueConstants.OperationalStates.ReadyForApproval);

            // B3 — PARTIAL_COVERAGE: one approved + one pending; assigned.
            var b3 = NewReq("B3", "Cobertura parcial", RequestConstants.Statuses.WaitingQuotation, actorId, today.AddDays(5), RequestConstants.NeedLevels.Urgente);
            NewItem(b3, "Item aprovado", RequestConstants.QuotationLifecycleStatuses.QuotationApproved);
            NewItem(b3, "Item ainda pendente", RequestConstants.QuotationLifecycleStatuses.QuotationPending);
            Record("B3", b3, BuyerQueueConstants.OperationalStates.PartialCoverage);

            // B4 — AWAITING_APPROVAL: one BATCH_ASSIGNED item + WAITING_AREA_APPROVAL batch.
            var b4 = NewReq("B4", "Em aprovação", RequestConstants.Statuses.WaitingAreaApproval, actorId, today.AddDays(8), RequestConstants.NeedLevels.Normal);
            var b4Line = NewItem(b4, "Item no lote", RequestConstants.QuotationLifecycleStatuses.BatchAssigned);
            NewBatch(b4, RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, b4Line);
            Record("B4", b4, BuyerQueueConstants.OperationalStates.AwaitingApproval);

            // B5 — ADJUSTMENT_REQUIRED: BATCH_ASSIGNED item + AREA_ADJUSTMENT batch; OVERDUE deadline.
            var b5 = NewReq("B5", "Ajuste solicitado (vencido)", RequestConstants.Statuses.AreaAdjustment, actorId, today.AddDays(-2), RequestConstants.NeedLevels.Critico);
            var b5Line = NewItem(b5, "Item a rever", RequestConstants.QuotationLifecycleStatuses.BatchAssigned);
            NewBatch(b5, RequestConstants.ApprovalBatchStatuses.AreaAdjustment, b5Line);
            Record("B5", b5, BuyerQueueConstants.OperationalStates.AdjustmentRequired);

            // B6 — AWAITING_REQUESTER_DECISION: NOT_QUOTED_PROPOSED item + one approved.
            var b6 = NewReq("B6", "Aguardando decisão do requisitante", RequestConstants.Statuses.WaitingQuotation, actorId, today.AddDays(6), RequestConstants.NeedLevels.Normal);
            NewItem(b6, "Item proposto não cotado", RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed);
            NewItem(b6, "Item aprovado", RequestConstants.QuotationLifecycleStatuses.QuotationApproved);
            Record("B6", b6, BuyerQueueConstants.OperationalStates.AwaitingRequesterDecision);

            // B7 — COMPLETED_FOR_BUYER (hidden by default): all approved.
            var b7 = NewReq("B7", "Concluído para compras", RequestConstants.Statuses.WaitingAreaApproval, actorId, today.AddDays(9), RequestConstants.NeedLevels.Baixo);
            NewItem(b7, "Item aprovado A", RequestConstants.QuotationLifecycleStatuses.QuotationApproved);
            NewItem(b7, "Item aprovado B", RequestConstants.QuotationLifecycleStatuses.QuotationApproved);
            Record("B7", b7, BuyerQueueConstants.OperationalStates.CompletedForBuyer);

            // B8 — NEEDS_QUOTATION + OVERDUE + unassigned (priority Band 1 + near-deadline warning).
            var b8 = NewReq("B8", "Cotação pendente vencida (não atribuído)", RequestConstants.Statuses.WaitingQuotation, null, today.AddDays(-1), RequestConstants.NeedLevels.Critico);
            NewItem(b8, "Item urgente pendente", RequestConstants.QuotationLifecycleStatuses.QuotationPending);
            Record("B8", b8, BuyerQueueConstants.OperationalStates.NeedsQuotation);

            // B9 — Workspace supplier-carousel scenario: a SELECTED quotation from a real supplier plus a
            // global track record (two issued POs in DIFFERENT currencies) to exercise the contextual
            // "Inteligência dos Fornecedores deste Pedido" carousel and per-currency (never-summed) totals.
            var b9 = NewReq("B9", "Cobertura com fornecedor selecionado", RequestConstants.Statuses.WaitingQuotation, actorId, today.AddDays(8), RequestConstants.NeedLevels.Normal);
            var b9Line = NewItem(b9, "Item cotado c/ fornecedor", RequestConstants.QuotationLifecycleStatuses.QuotationPending);

            var zzSupplier = new Supplier { Name = "ZZTEST-BUY Fornecedor Alfa", TaxId = "ZZTESTBUY01", IsActive = true, Origin = "MANUAL", RegistrationStatus = "ACTIVE" };
            _context.Suppliers.Add(zzSupplier);
            await _context.SaveChangesAsync(); // materialize supplier identity Id for the FKs

            var b9Quotation = new Quotation
            {
                Id = Guid.NewGuid(), RequestId = b9.Id, SupplierId = zzSupplier.Id, SupplierNameSnapshot = zzSupplier.Name,
                Currency = "AOA", TotalAmount = 1000m, IsSelected = true, SourceType = "MANUAL",
                CreatedByUserId = actorId, CreatedAtUtc = now
            };
            _context.Quotations.Add(b9Quotation);
            _context.QuotationItems.Add(new QuotationItem
            {
                Id = Guid.NewGuid(), QuotationId = b9Quotation.Id, Description = b9Line.Description,
                ReconciliationStatus = RequestConstants.ReconciliationStatuses.Mapped, MappedRequestLineItemId = b9Line.Id
            });
            _context.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = b9.Id, SupplierId = zzSupplier.Id, PurchaseOrderNumber = "ZZ-BUY-PO-1", Status = RequestConstants.PoGroupStatuses.PoIssued, CurrencyCode = "AOA", TotalAmount = 1000m, CreatedByUserId = actorId, CreatedAtUtc = now.AddDays(-10) });
            _context.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = b9.Id, SupplierId = zzSupplier.Id, PurchaseOrderNumber = "ZZ-BUY-PO-2", Status = RequestConstants.PoGroupStatuses.PoIssued, CurrencyCode = "EUR", TotalAmount = 200m, CreatedByUserId = actorId, CreatedAtUtc = now.AddDays(-3) });
            Record("B9", b9, BuyerQueueConstants.OperationalStates.ReadyForApproval);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Fixtures ZZTEST-BUY semeadas.",
                actorUserId = actorId,
                org = new { company = new { company.Id, company.Name }, plant = new { plant.Id, plant.Name }, dept = new { dept.Id, dept.Name } },
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
public class BuyerDevFixtureController : ControllerBase { [HttpGet] public IActionResult Index() => NotFound(); }
#endif
