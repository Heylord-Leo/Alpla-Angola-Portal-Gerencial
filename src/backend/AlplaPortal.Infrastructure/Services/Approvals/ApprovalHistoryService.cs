using AlplaPortal.Application.DTOs.Approvals;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Approvals;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Approvals;

/// <summary>
/// v2.244.0 Approval Center V2 — Phase 2. Builds the read-only approval history + audit timeline from
/// RequestStatusHistories. Classification is delegated to the single Domain classifier
/// (<see cref="ApprovalHistoryClassifier"/>). Request visibility is entirely caller-scoped via the
/// scopedRequestIds set — the service adds no visibility of its own.
///
/// Query shape: all EF-translatable filters (scope, decision-candidate action codes, date range,
/// request type, department/company/plant, approver, text search) run in SQL with AsNoTracking and a
/// column-only projection. The two derived filters — Decision and Stage — depend on the classifier
/// (a C# switch that can't be translated), so they are applied in memory over the already-narrow
/// candidate set (≤ a few hundred rows at current scale), which also yields the correct post-filter
/// TotalCount and the decision-mix summary for free. See §27 index note for the scale story.
/// </summary>
public sealed class ApprovalHistoryService : IApprovalHistoryService
{
    private readonly ApplicationDbContext _context;

    public ApprovalHistoryService(ApplicationDbContext context)
    {
        _context = context;
    }

    public const string SortDateDesc = "dateDesc";
    public const string SortDateAsc = "dateAsc";
    public const string SortValueDesc = "valueDesc";

    private const int ExportCap = 5000;

    // Materialize the (small) candidate set with the navs we need, then classify + map in memory.
    // We Include + map rather than project a single Select with a nested subquery: the latter does not
    // translate uniformly across providers (notably EF InMemory in the tests), whereas Include-then-map
    // is correct everywhere. At current volume the candidate set is a few hundred rows — see §27.
    private async Task<List<ApprovalHistoryRowDto>> BuildFilteredAsync(
        IReadOnlyCollection<Guid> scopedRequestIds, ApprovalHistoryFilter f, int cap, CancellationToken ct)
    {
        if (scopedRequestIds.Count == 0) return new List<ApprovalHistoryRowDto>();

        var decisionCodes = ApprovalHistoryClassifier.DecisionActionCodes;

        var q = _context.RequestStatusHistories
            .AsNoTracking()
            .Include(sh => sh.Request!).ThenInclude(r => r.RequestType)
            .Include(sh => sh.Request!).ThenInclude(r => r.Department)
            .Include(sh => sh.Request!).ThenInclude(r => r.Company)
            .Include(sh => sh.Request!).ThenInclude(r => r.Plant)
            .Include(sh => sh.Request!).ThenInclude(r => r.Requester)
            .Include(sh => sh.Request!).ThenInclude(r => r.Currency)
            .Include(sh => sh.Request!).ThenInclude(r => r.Quotations)
            .Include(sh => sh.ActorUser)
            .Include(sh => sh.PreviousStatus)
            .Include(sh => sh.NewStatus)
            .Where(sh => scopedRequestIds.Contains(sh.RequestId) && decisionCodes.Contains(sh.ActionTaken));

        if (f.DateFrom.HasValue) q = q.Where(sh => sh.CreatedAtUtc >= f.DateFrom.Value);
        if (f.DateTo.HasValue) q = q.Where(sh => sh.CreatedAtUtc <= f.DateTo.Value);
        if (f.ApproverId.HasValue) q = q.Where(sh => sh.ActorUserId == f.ApproverId.Value);
        if (!string.IsNullOrWhiteSpace(f.RequestType)) q = q.Where(sh => sh.Request!.RequestType!.Code == f.RequestType);
        if (f.DepartmentId.HasValue) q = q.Where(sh => sh.Request!.DepartmentId == f.DepartmentId.Value);
        if (f.CompanyId.HasValue) q = q.Where(sh => sh.Request!.CompanyId == f.CompanyId.Value);
        if (f.PlantId.HasValue) q = q.Where(sh => sh.Request!.PlantId == f.PlantId.Value);

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.ToLower();
            q = q.Where(sh =>
                (sh.Request!.RequestNumber != null && sh.Request.RequestNumber.ToLower().Contains(s)) ||
                (sh.Request!.Title != null && sh.Request.Title.ToLower().Contains(s)) ||
                (sh.ActorUser!.FullName != null && sh.ActorUser.FullName.ToLower().Contains(s)) ||
                (sh.Request!.Requester != null && sh.Request.Requester.FullName != null && sh.Request.Requester.FullName.ToLower().Contains(s)) ||
                (sh.Request!.Department != null && sh.Request.Department.Name != null && sh.Request.Department.Name.ToLower().Contains(s)) ||
                (sh.Comment != null && sh.Comment.ToLower().Contains(s)));
        }

        var entities = await q.Take(cap).ToListAsync(ct);

        var rows = new List<ApprovalHistoryRowDto>(entities.Count);
        foreach (var sh in entities)
        {
            var newCode = sh.NewStatus?.Code;
            var prevCode = sh.PreviousStatus?.Code;
            var c = ApprovalHistoryClassifier.Classify(sh.ActionTaken, newCode, prevCode);
            if (!c.IsApprovalDecision) continue;

            if (!string.IsNullOrWhiteSpace(f.Decision) && !string.Equals(c.DecisionCode, f.Decision, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(f.Stage) && !string.Equals(c.LevelCode, f.Stage, StringComparison.OrdinalIgnoreCase))
                continue;

            var req = sh.Request;
            string? supplier = null;
            if (req?.SelectedQuotationId != null && req.Quotations != null)
                supplier = req.Quotations.FirstOrDefault(qt => qt.Id == req.SelectedQuotationId.Value)?.SupplierNameSnapshot;

            rows.Add(new ApprovalHistoryRowDto
            {
                Id = sh.Id,
                RequestId = sh.RequestId,
                RequestNumber = req?.RequestNumber ?? "---",
                RequestTitle = req?.Title ?? "---",
                RequestTypeCode = req?.RequestType?.Code ?? "",
                ApprovalLevel = c.LevelCode,
                Decision = c.DecisionCode,
                ActionTaken = sh.ActionTaken ?? "",
                BatchNumber = ApprovalHistoryClassifier.ParseLoteNumber(sh.Comment),
                RequesterName = req?.Requester?.FullName ?? "---",
                DepartmentName = req?.Department?.Name,
                CompanyName = req?.Company?.Name,
                PlantName = req?.Plant?.Name,
                ApproverUserId = sh.ActorUserId,
                ApproverName = sh.ActorUser?.FullName ?? "---",
                DecisionAtUtc = sh.CreatedAtUtc,
                Comment = sh.Comment,
                Amount = req?.ApprovedTotalAmount ?? req?.EstimatedTotalAmount,
                CurrencyCode = req?.Currency?.Code,
                SupplierName = string.IsNullOrWhiteSpace(supplier) ? null : supplier,
                PreviousStatusCode = prevCode,
                NewStatusCode = newCode,
            });
        }
        return rows;
    }

    private static IEnumerable<ApprovalHistoryRowDto> ApplySort(IEnumerable<ApprovalHistoryRowDto> rows, string sort) => sort switch
    {
        SortDateAsc => rows.OrderBy(r => r.DecisionAtUtc),
        SortValueDesc => rows.OrderByDescending(r => r.Amount ?? decimal.MinValue).ThenByDescending(r => r.DecisionAtUtc),
        _ => rows.OrderByDescending(r => r.DecisionAtUtc),
    };

    public async Task<ApprovalHistoryPageDto> GetHistoryAsync(
        IReadOnlyCollection<Guid> scopedRequestIds, ApprovalHistoryFilter filter, int page, int pageSize, string sort, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 200) pageSize = 200;

        var all = await BuildFilteredAsync(scopedRequestIds, filter, ExportCap, ct);
        var total = all.Count;
        var ordered = ApplySort(all, sort).ToList();
        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new ApprovalHistoryPageDto
        {
            Items = pageItems,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize),
            ApprovedCount = all.Count(r => r.Decision == "APPROVED"),
            RejectedCount = all.Count(r => r.Decision == "REJECTED"),
            ReturnedCount = all.Count(r => r.Decision == "RETURNED"),
            ResubmittedCount = all.Count(r => r.Decision == "RESUBMITTED"),
        };
    }

    public async Task<List<ApprovalHistoryRowDto>> GetExportRowsAsync(
        IReadOnlyCollection<Guid> scopedRequestIds, ApprovalHistoryFilter filter, string sort, CancellationToken ct = default)
    {
        var all = await BuildFilteredAsync(scopedRequestIds, filter, ExportCap, ct);
        return ApplySort(all, sort).ToList();
    }

    // PT labels for the curated timeline context events (decisions get their own badge in the UI).
    private static string TimelineLabel(string actionTaken, ApprovalClassification c) => c.IsApprovalDecision
        ? (c.DecisionCode switch
        {
            "APPROVED" => c.LevelCode == "FINAL" ? "Aprovação Final" : "Aprovação de Área",
            "REJECTED" => c.LevelCode == "FINAL" ? "Rejeição Final" : "Rejeição de Área",
            "RETURNED" => c.LevelCode == "FINAL" ? "Devolvido (Final)" : "Devolvido (Área)",
            "RESUBMITTED" => "Reenviado",
            _ => actionTaken,
        })
        : actionTaken switch
        {
            "CREATED" => "Pedido criado",
            "SUBMIT" => "Submetido para aprovação",
            "BATCH_CREATED" => "Lote de aprovação criado",
            "BATCH_CANDIDATES_SUBMITTED" => "Candidatos submetidos",
            "BATCH_EDITED" => "Lote editado",
            "FINANCE_RETURN_ADJUSTMENT" => "Devolução (Finanças)",
            _ => actionTaken,
        };

    public async Task<List<ApprovalTimelineEventDto>> GetTimelineAsync(Guid requestId, CancellationToken ct = default)
    {
        var timelineCodes = ApprovalHistoryClassifier.TimelineActionCodes;

        var raws = await _context.RequestStatusHistories
            .AsNoTracking()
            .Where(sh => sh.RequestId == requestId && timelineCodes.Contains(sh.ActionTaken))
            .OrderBy(sh => sh.CreatedAtUtc)
            .Select(sh => new
            {
                sh.Id,
                sh.ActionTaken,
                sh.ActorUserId,
                ActorName = sh.ActorUser!.FullName ?? "---",
                sh.Comment,
                PreviousStatusCode = sh.PreviousStatus != null ? sh.PreviousStatus.Code : null,
                NewStatusCode = sh.NewStatus != null ? sh.NewStatus.Code : null,
                sh.CreatedAtUtc,
            })
            .ToListAsync(ct);

        return raws.Select(r =>
        {
            var c = ApprovalHistoryClassifier.Classify(r.ActionTaken, r.NewStatusCode, r.PreviousStatusCode);
            return new ApprovalTimelineEventDto
            {
                Id = r.Id,
                ActionTaken = r.ActionTaken ?? "",
                ActionLabel = TimelineLabel(r.ActionTaken ?? "", c),
                ApprovalLevel = c.LevelCode,
                Decision = c.DecisionCode,
                IsDecision = c.IsApprovalDecision,
                ActorUserId = r.ActorUserId,
                ActorName = r.ActorName,
                Comment = r.Comment,
                PreviousStatusCode = r.PreviousStatusCode,
                NewStatusCode = r.NewStatusCode,
                BatchNumber = ApprovalHistoryClassifier.ParseLoteNumber(r.Comment),
                CreatedAtUtc = r.CreatedAtUtc,
            };
        }).ToList();
    }
}
