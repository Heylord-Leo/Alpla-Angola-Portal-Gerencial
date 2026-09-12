using System.Text;
using AlplaPortal.Application.DTOs.Approvals;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// v2.244.0 Approval Center V2 — Phase 2. Read-only approval history, per-request audit timeline and
/// CSV export. Visibility mirrors the Approval Center exactly: the same approver/admin roles gate the
/// endpoints, and request selection flows through GetScopedRequestsQuery (plant/department scope), so
/// no plant/department outside the user's scope can leak. No writes, no migration, no new permission.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/approvals")]
public class ApprovalHistoryController : BaseController
{
    private readonly IApprovalHistoryService _historyService;
    private readonly IApprovalAnalyticsService _analyticsService;

    public ApprovalHistoryController(ApplicationDbContext context, IApprovalHistoryService historyService, IApprovalAnalyticsService analyticsService) : base(context)
    {
        _historyService = historyService;
        _analyticsService = analyticsService;
    }

    // Same roles as the /approvals page (AdminRoute AREA_APPROVER + FINAL_APPROVER), plus admin.
    private bool HasApprovalAccess()
    {
        var roles = CurrentUserRoles;
        return roles.Contains(RoleConstants.SystemAdministrator)
            || roles.Contains(RoleConstants.AreaApprover)
            || roles.Contains(RoleConstants.FinalApprover);
    }

    private static ApprovalHistoryFilter BuildFilter(
        string? search, string? decision, string? stage, Guid? approverId, string? requestType,
        int? departmentId, int? companyId, int? plantId, DateTime? dateFrom, DateTime? dateTo)
        => new()
        {
            Search = search,
            Decision = decision,
            Stage = stage,
            ApproverId = approverId,
            RequestType = requestType,
            DepartmentId = departmentId,
            CompanyId = companyId,
            PlantId = plantId,
            DateFrom = dateFrom,
            // Make an inclusive end-of-day when only a date is supplied.
            DateTo = dateTo.HasValue && dateTo.Value.TimeOfDay == TimeSpan.Zero ? dateTo.Value.AddDays(1).AddTicks(-1) : dateTo,
        };

    [HttpGet("history")]
    public async Task<ActionResult<ApprovalHistoryPageDto>> GetHistory(
        [FromQuery] string? search = null,
        [FromQuery] string? decision = null,
        [FromQuery] string? stage = null,
        [FromQuery] Guid? approverId = null,
        [FromQuery] string? requestType = null,
        [FromQuery] int? departmentId = null,
        [FromQuery] int? companyId = null,
        [FromQuery] int? plantId = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string sort = "dateDesc")
    {
        if (!HasApprovalAccess()) return Forbid();

        var scopedIds = await (await GetScopedRequestsQuery()).Select(r => r.Id).ToListAsync();
        var filter = BuildFilter(search, decision, stage, approverId, requestType, departmentId, companyId, plantId, dateFrom, dateTo);
        var result = await _historyService.GetHistoryAsync(scopedIds, filter, page, pageSize, sort);
        return Ok(result);
    }

    [HttpGet("history/export")]
    public async Task<IActionResult> ExportHistory(
        [FromQuery] string? search = null,
        [FromQuery] string? decision = null,
        [FromQuery] string? stage = null,
        [FromQuery] Guid? approverId = null,
        [FromQuery] string? requestType = null,
        [FromQuery] int? departmentId = null,
        [FromQuery] int? companyId = null,
        [FromQuery] int? plantId = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string sort = "dateDesc")
    {
        if (!HasApprovalAccess()) return Forbid();

        var scopedIds = await (await GetScopedRequestsQuery()).Select(r => r.Id).ToListAsync();
        var filter = BuildFilter(search, decision, stage, approverId, requestType, departmentId, companyId, plantId, dateFrom, dateTo);
        var rows = await _historyService.GetExportRowsAsync(scopedIds, filter, sort);

        var csv = new StringBuilder();
        // Same ';'-separated, UTF-8 (no BOM) convention as FinanceController.ExportHistory.
        csv.AppendLine("DecisionDate;RequestNumber;RequestTitle;RequestType;Stage;Decision;Requester;Approver;Department;Company;Plant;Supplier;Lote;Amount;Currency;Comment");
        foreach (var r in rows)
        {
            csv.AppendLine(string.Join(';', new[]
            {
                r.DecisionAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                Clean(r.RequestNumber),
                Clean(r.RequestTitle),
                Clean(r.RequestTypeCode),
                StageLabel(r.ApprovalLevel),
                DecisionLabel(r.Decision),
                Clean(r.RequesterName),
                Clean(r.ApproverName),
                Clean(r.DepartmentName),
                Clean(r.CompanyName),
                Clean(r.PlantName),
                Clean(r.SupplierName),
                r.BatchNumber?.ToString() ?? "",
                r.Amount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                Clean(r.CurrencyCode),
                Clean(r.Comment),
            }));
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv", $"approval-history-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<ApprovalAnalyticsDto>> GetAnalytics(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string resolution = "day",
        [FromQuery] string? requestType = null,
        [FromQuery] int? departmentId = null,
        [FromQuery] int? companyId = null,
        [FromQuery] int? plantId = null,
        [FromQuery] Guid? approverId = null,
        [FromQuery] string? stage = null)
    {
        if (!HasApprovalAccess()) return Forbid();

        var scopedIds = await (await GetScopedRequestsQuery()).Select(r => r.Id).ToListAsync();
        var filter = BuildFilter(null, null, stage, approverId, requestType, departmentId, companyId, plantId, dateFrom, dateTo);
        var result = await _analyticsService.GetAnalyticsAsync(scopedIds, filter, resolution);
        return Ok(result);
    }

    [HttpGet("history/{requestId:guid}")]
    public async Task<ActionResult<List<ApprovalTimelineEventDto>>> GetTimeline(Guid requestId)
    {
        if (!HasApprovalAccess()) return Forbid();

        // Scope guard: the request must be inside the caller's visibility, or 404 (never leak existence).
        var inScope = await (await GetScopedRequestsQuery()).AnyAsync(r => r.Id == requestId);
        if (!inScope) return NotFound();

        var events = await _historyService.GetTimelineAsync(requestId);
        return Ok(events);
    }

    private static string Clean(string? value) => (value ?? "").Replace(";", ",").Replace("\r", "").Replace("\n", " ");

    private static string StageLabel(string? level) => level switch { "AREA" => "Área", "FINAL" => "Final", _ => "" };

    private static string DecisionLabel(string? decision) => decision switch
    {
        "APPROVED" => "Aprovado",
        "REJECTED" => "Rejeitado",
        "RETURNED" => "Devolvido",
        "RESUBMITTED" => "Reenviado",
        _ => decision ?? "",
    };
}
