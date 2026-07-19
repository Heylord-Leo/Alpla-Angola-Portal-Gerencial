namespace AlplaPortal.Api.Controllers;
using System.Diagnostics;

using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Events;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Application.DTOs.Common;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Api.Helpers;
using AlplaPortal.Api.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;


[Authorize]
[ApiController]
[Route("api/v1/requests")]
public class RequestsController : BaseController
{
    private readonly IDocumentExtractionService _extractionService;
    private readonly AdminLogWriter _adminLog;
    private readonly ILogger<RequestsController> _logger;
    private readonly INotificationService _notificationService;
    private readonly IWorkflowNotificationOrchestrator _orchestrator;
    private readonly IPrimaveraRequestValidationService _primaveraValidationService;
    private readonly IGroupBuilderService _groupBuilderService;
    private readonly IRequestStatusSyncService _statusSyncService;
    private readonly IApprovalRoutingService _approvalRouting;
    private readonly ILineItemFactory _lineItemFactory;
    private readonly IRequestLineItemSubmissionValidator _lineItemValidator;
    private readonly IQuotationItemEligibilityService _quotationEligibility;

    public RequestsController(
        ApplicationDbContext context,
        IDocumentExtractionService extractionService,
        AdminLogWriter adminLog,
        ILogger<RequestsController> logger,
        INotificationService notificationService,
        IWorkflowNotificationOrchestrator orchestrator,
        IPrimaveraRequestValidationService primaveraValidationService,
        IGroupBuilderService groupBuilderService,
        IRequestStatusSyncService statusSyncService,
        IApprovalRoutingService approvalRouting,
        ILineItemFactory lineItemFactory,
        IRequestLineItemSubmissionValidator lineItemValidator,
        IQuotationItemEligibilityService quotationEligibility) : base(context)
    {
        _extractionService = extractionService;
        _adminLog = adminLog;
        _logger = logger;
        _notificationService = notificationService;
        _orchestrator = orchestrator;
        _primaveraValidationService = primaveraValidationService;
        _groupBuilderService = groupBuilderService;
        _statusSyncService = statusSyncService;
        _approvalRouting = approvalRouting;
        _lineItemFactory = lineItemFactory;
        _lineItemValidator = lineItemValidator;
        _quotationEligibility = quotationEligibility;
    }

    /// <summary>
    /// Area-approval authorization — DepartmentManager as single source of truth:
    /// System Administrator, OR active manager of the request's department (plant-specific
    /// or global — D1), OR the legacy nominee (see <see cref="IsLegacyNamedAreaApprover"/>).
    /// The manually assigned "Area Approver" role grants nothing by itself.
    /// </summary>
    private async Task<bool> CanActAsAreaManagerAsync(Guid actorId, Request request)
    {
        if (CurrentUserRoles.Contains(RoleConstants.SystemAdministrator)) return true;
        if (IsLegacyNamedAreaApprover(request, actorId)) return true;
        return await _approvalRouting.IsAreaManagerAsync(actorId, request.DepartmentId, request.PlantId);
    }

    /// <summary>
    /// Phase C — TEMPORARY compatibility, kept only for requests submitted BEFORE the
    /// Phase B cut: those were nominated to a single approver via the old
    /// Department.ResponsibleUserId model. Post-cut requests reach the area stage with
    /// AreaApproverId == null (the field is only written when someone decides), so no
    /// new request can ever satisfy this clause. Remove once no in-flight request in
    /// PRODUCTION matches: WAITING_AREA_APPROVAL/WAITING_COST_CENTER with AreaApproverId
    /// set (audit query in docs/department-manager-routing-redesign-plan.md §16.1).
    /// In Development on 16/07/2026 the dependent count was ZERO.
    /// </summary>
    private static bool IsLegacyNamedAreaApprover(Request request, Guid actorId)
    {
        var statusCode = request.Status?.Code;
        return request.AreaApproverId == actorId
            && (statusCode == RequestConstants.Statuses.WaitingAreaApproval
             || statusCode == RequestConstants.Statuses.WaitingCostCenter);
    }

    /// <summary>
    /// Canonical rounding helper: 2 decimal places, MidpointRounding.AwayFromZero.
    /// Matches JavaScript Math.round(x * 100) / 100 behavior to ensure frontend/backend
    /// financial calculations produce identical results.
    /// </summary>
    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);


    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetDashboardSummary()
    {
        var in4Days = DateTime.UtcNow.Date.AddDays(4);
        var terminalStates = new[] { "APPROVED", "REJECTED", "CANCELLED", "COMPLETED", "QUOTATION_COMPLETED" };

        var query = await GetScopedRequestsQuery();

        var stats = await query
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                WaitingQuotation = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingQuotation && r.RequestType!.Code == RequestConstants.Types.Quotation),
                WaitingAreaApproval = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingAreaApproval),
                WaitingFinalApproval = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingFinalApproval || r.Status!.Code == RequestConstants.Statuses.WaitingCostCenter),
                AwaitingPo = g.Count(r => r.Status!.Code == RequestConstants.Statuses.FinalApproved || r.Status!.Code == RequestConstants.Statuses.QuotationCompleted || r.Status!.Code == RequestConstants.Statuses.WaitingPoCorrection),
                InAdjustment = g.Count(r => r.Status!.Code == RequestConstants.Statuses.AreaAdjustment || r.Status!.Code == RequestConstants.Statuses.FinalAdjustment),
                InAttention = g.Count(r => !terminalStates.Contains(r.Status!.Code) && r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < in4Days)
            })
            .OrderBy(g => 1)
            .FirstOrDefaultAsync();

        return Ok(new DashboardSummaryDto
        {
            TotalRequests = stats?.Total ?? 0,
            WaitingQuotation = stats?.WaitingQuotation ?? 0,
            WaitingAreaApproval = stats?.WaitingAreaApproval ?? 0,
            WaitingFinalApproval = stats?.WaitingFinalApproval ?? 0,
            AwaitingPo = stats?.AwaitingPo ?? 0,
            InAdjustment = stats?.InAdjustment ?? 0,
            InAttention = stats?.InAttention ?? 0
        });

    }

    /// <summary>
    /// Dedicated endpoint for the Dashboard Operational Cockpit.
    /// Returns all data needed by the redesigned dashboard in a single call:
    /// my-tasks counters, pipeline KPIs, bottlenecks, financial aggregation, and attention alerts.
    /// Designed to support optional filter params in future versions.
    /// </summary>
    [HttpGet("cockpit-summary")]
    public async Task<ActionResult<CockpitSummaryDto>> GetCockpitSummary()
    {
        var query = await GetScopedRequestsQuery();
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var in4Days = today.AddDays(4);
        var terminalStates = new[] { "APPROVED", "REJECTED", "CANCELLED", "COMPLETED", "QUOTATION_COMPLETED" };
        var nonTerminalStates = new[] { "REJECTED", "CANCELLED", "COMPLETED", "QUOTATION_COMPLETED" };

        var currentUserId = CurrentUserId;
        var roles = CurrentUserRoles;
        var isAdmin = roles.Contains(RoleConstants.SystemAdministrator);
        var isFinalApprover = roles.Contains(RoleConstants.FinalApprover);
        var isBuyer = roles.Contains(RoleConstants.Buyer);
        var isFinance = roles.Contains(RoleConstants.Finance);
        var isReceiver = roles.Contains(RoleConstants.Receiving);

        var receivingCodes = new[] { "WAITING_RECEIPT", RequestConstants.Statuses.PaymentCompleted, "PAG_REALIZADO", "AG_RECIBO" };

        // ── My Tasks Criteria (reuses the same logic as GetRequests myTasksCriteria) ──
        Expression<Func<AlplaPortal.Domain.Entities.Request, bool>> myTasksCriteria = r =>
            (r.RequesterId == currentUserId && (r.Status!.Code == RequestConstants.Statuses.Draft || r.Status!.Code == RequestConstants.Statuses.AreaAdjustment || r.Status!.Code == RequestConstants.Statuses.FinalAdjustment || (r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Payment))) ||
            ((r.AreaApproverId == currentUserId || _context.DepartmentManagers.Any(dm => dm.UserId == currentUserId && dm.IsActive && dm.DepartmentId == r.DepartmentId && (dm.PlantId == null || (r.PlantId != null && dm.PlantId == r.PlantId)))) && r.Status!.Code == RequestConstants.Statuses.WaitingAreaApproval) ||
            (isFinalApprover && r.Status!.Code == RequestConstants.Statuses.WaitingFinalApproval) ||
            (isBuyer && (r.Status!.Code == RequestConstants.Statuses.WaitingQuotation || (r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Quotation)) && (r.BuyerId == currentUserId || r.BuyerId == null)) ||
            (isFinance && ((r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Payment) || r.Status!.Code == RequestConstants.Statuses.PoIssued || r.Status!.Code == RequestConstants.Statuses.PaymentRequestSent || r.Status!.Code == RequestConstants.Statuses.PaymentScheduled)) ||
            ((r.RequesterId == currentUserId || isReceiver) && receivingCodes.Contains(r.Status!.Code));

        // ── 1. My Work Queue counters ──
        var myTasksQuery = query.Where(myTasksCriteria);

        var myPendingActions = await myTasksQuery.CountAsync();

        var myUrgentItems = await myTasksQuery
            .CountAsync(r => r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= today && r.NeedByDateUtc.Value < tomorrow.AddDays(1));

        var myAdjustmentItems = await myTasksQuery
            .CountAsync(r => r.Status!.Code == RequestConstants.Statuses.AreaAdjustment || r.Status!.Code == RequestConstants.Statuses.FinalAdjustment);

        var myOverdueItems = await myTasksQuery
            .CountAsync(r => r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < today);

        var myNearDeadlineItems = await myTasksQuery
            .CountAsync(r => r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= today && r.NeedByDateUtc.Value < in4Days);

        // ── 2. Pipeline counters ──
        var pipeline = await query
            .GroupBy(r => 1)
            .Select(g => new
            {
                TotalActive = g.Count(r => !nonTerminalStates.Contains(r.Status!.Code)),
                Draft = g.Count(r => r.Status!.Code == RequestConstants.Statuses.Draft),
                WaitingQuotation = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingQuotation && r.RequestType!.Code == RequestConstants.Types.Quotation),
                WaitingAreaApproval = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingAreaApproval),
                WaitingFinalApproval = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingFinalApproval || r.Status!.Code == RequestConstants.Statuses.WaitingCostCenter),
                InAdjustment = g.Count(r => r.Status!.Code == RequestConstants.Statuses.AreaAdjustment || r.Status!.Code == RequestConstants.Statuses.FinalAdjustment),
                AwaitingPo = g.Count(r => r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Quotation),
                AwaitingPayment = g.Count(r => (r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Payment) || r.Status!.Code == RequestConstants.Statuses.PoIssued || r.Status!.Code == RequestConstants.Statuses.PaymentRequestSent || r.Status!.Code == RequestConstants.Statuses.PaymentScheduled),
                PaymentCompleted = g.Count(r => r.Status!.Code == RequestConstants.Statuses.PaymentCompleted || r.Status!.Code == RequestConstants.Statuses.Paid),
                WaitingReceipt = g.Count(r => r.Status!.Code == "WAITING_RECEIPT" || r.Status!.Code == RequestConstants.Statuses.InFollowup),
                Completed = g.Count(r => r.Status!.Code == RequestConstants.Statuses.Completed || r.Status!.Code == RequestConstants.Statuses.QuotationCompleted)
            })
            .OrderBy(g => 1)
            .FirstOrDefaultAsync();

        // ── 3. Bottlenecks (active stages with count and oldest entry) ──
        var activeStageCodes = new[]
        {
            RequestConstants.Statuses.WaitingQuotation,
            RequestConstants.Statuses.WaitingAreaApproval,
            RequestConstants.Statuses.WaitingFinalApproval,
            RequestConstants.Statuses.WaitingCostCenter,
            RequestConstants.Statuses.AreaAdjustment,
            RequestConstants.Statuses.FinalAdjustment,
            RequestConstants.Statuses.FinalApproved,
            RequestConstants.Statuses.PoIssued,
            RequestConstants.Statuses.PaymentRequestSent,
            RequestConstants.Statuses.PaymentScheduled,
            RequestConstants.Statuses.PaymentCompleted,
            "WAITING_RECEIPT",
            RequestConstants.Statuses.InFollowup
        };

        var bottlenecks = await query
            .Where(r => activeStageCodes.Contains(r.Status!.Code))
            .GroupBy(r => new { r.Status!.Code, r.Status.Name })
            .Select(g => new StageBottleneckDto
            {
                StageCode = g.Key.Code,
                StageName = g.Key.Name ?? g.Key.Code,
                Count = g.Count(),
                OldestCreatedAtUtc = g.Min(r => r.CreatedAtUtc)
            })
            .Where(b => b.Count > 0)
            .OrderByDescending(b => b.Count)
            .ToListAsync();

        // ── 4. Financial summary by group ──
        // Group 1: "Solicitado" (requests in approval stages)
        var approvalCodes = new[] { RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.WaitingFinalApproval, RequestConstants.Statuses.WaitingCostCenter };
        // Group 2: "Aprovado" (approved, awaiting PO or payment action)
        var approvedCodes = new[] { RequestConstants.Statuses.FinalApproved, RequestConstants.Statuses.PoIssued };
        // Group 3: "Pendente Pagamento" (payment in progress)
        var paymentPendingCodes = new[] { RequestConstants.Statuses.PaymentRequestSent, RequestConstants.Statuses.PaymentScheduled };
        // Group 4: "Pago" (payment completed, waiting receipt or completed)
        var paidCodes = new[] { RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.Paid, "WAITING_RECEIPT", RequestConstants.Statuses.Completed };

        var financialGroups = new List<(string Label, string[] Codes)>
        {
            ("Em Aprovação", approvalCodes),
            ("Aprovado / Ag. PO", approvedCodes),
            ("Pendente Pagamento", paymentPendingCodes),
            ("Pago / Finalizado", paidCodes)
        };

        var financialByStatus = new List<FinancialByStatusDto>();
        foreach (var (label, codes) in financialGroups)
        {
            var groupQuery = query.Where(r => codes.Contains(r.Status!.Code));
            var groupTotal = await groupQuery
                .SelectMany(
                    r => r.Quotations.Where(q => q.Id == r.SelectedQuotationId).DefaultIfEmpty(),
                    (r, q) => q != null ? q.TotalAmount : r.EstimatedTotalAmount
                )
                .SumAsync();

            var groupCurrencies = await groupQuery
                .Select(r => r.SelectedQuotationId.HasValue
                    ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId).Select(q => q.Currency).FirstOrDefault()
                    : (r.Currency != null ? r.Currency.Code : null))
                .Where(c => c != null)
                .Distinct()
                .ToListAsync();

            var groupCount = await groupQuery.CountAsync();

            if (groupCount > 0)
            {
                financialByStatus.Add(new FinancialByStatusDto
                {
                    GroupLabel = label,
                    TotalAmount = groupTotal,
                    CurrencyCodes = groupCurrencies!,
                    Count = groupCount
                });
            }
        }

        // ── 5. Attention alerts ──
        var alerts = new List<AttentionAlertDto>();

        // 5a. Overdue requests (top 10 most critical)
        var overdueRequests = await query
            .Where(r => !nonTerminalStates.Contains(r.Status!.Code) && r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < today)
            .OrderBy(r => r.NeedByDateUtc)
            .Take(10)
            .Select(r => new { r.Id, r.RequestNumber, r.Title, r.Status!.Name, r.NeedByDateUtc, r.CreatedAtUtc })
            .ToListAsync();

        foreach (var or in overdueRequests)
        {
            var daysOverdue = (today - or.NeedByDateUtc!.Value).Days;
            alerts.Add(new AttentionAlertDto
            {
                Id = $"overdue-{or.Id}",
                RequestId = or.Id.ToString(),
                RequestNumber = or.RequestNumber ?? "",
                Title = or.Title,
                Reason = $"Vencido há {daysOverdue} dia(s)",
                ResponsibleArea = or.Name ?? "",
                AlertType = "OVERDUE",
                Severity = daysOverdue > 7 ? "CRITICAL" : "WARNING",
                CreatedAtUtc = or.CreatedAtUtc,
                TargetPath = $"/requests/{or.Id}"
            });
        }

        // 5b. Near deadline (within 3 days, not overdue)
        var nearDeadline = await query
            .Where(r => !nonTerminalStates.Contains(r.Status!.Code) && r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= today && r.NeedByDateUtc.Value < in4Days)
            .OrderBy(r => r.NeedByDateUtc)
            .Take(5)
            .Select(r => new { r.Id, r.RequestNumber, r.Title, r.Status!.Name, r.NeedByDateUtc, r.CreatedAtUtc })
            .ToListAsync();

        foreach (var nd in nearDeadline)
        {
            var daysLeft = (nd.NeedByDateUtc!.Value - today).Days;
            alerts.Add(new AttentionAlertDto
            {
                Id = $"near-{nd.Id}",
                RequestId = nd.Id.ToString(),
                RequestNumber = nd.RequestNumber ?? "",
                Title = nd.Title,
                Reason = daysLeft == 0 ? "Vence hoje" : $"Vence em {daysLeft} dia(s)",
                ResponsibleArea = nd.Name ?? "",
                AlertType = "NEAR_DEADLINE",
                Severity = daysLeft == 0 ? "WARNING" : "INFO",
                CreatedAtUtc = nd.CreatedAtUtc,
                TargetPath = $"/requests/{nd.Id}"
            });
        }

        // 5c. Items in adjustment (returned for correction)
        var adjustmentItems = await query
            .Where(r => r.Status!.Code == RequestConstants.Statuses.AreaAdjustment || r.Status!.Code == RequestConstants.Statuses.FinalAdjustment)
            .OrderBy(r => r.CreatedAtUtc)
            .Take(5)
            .Select(r => new { r.Id, r.RequestNumber, r.Title, StatusName = r.Status!.Name, r.CreatedAtUtc })
            .ToListAsync();

        foreach (var adj in adjustmentItems)
        {
            alerts.Add(new AttentionAlertDto
            {
                Id = $"adjustment-{adj.Id}",
                RequestId = adj.Id.ToString(),
                RequestNumber = adj.RequestNumber ?? "",
                Title = adj.Title,
                Reason = "Devolvido para reajuste",
                ResponsibleArea = adj.StatusName ?? "",
                AlertType = "ADJUSTMENT",
                Severity = "WARNING",
                CreatedAtUtc = adj.CreatedAtUtc,
                TargetPath = $"/requests/{adj.Id}"
            });
        }

        // Sort alerts: CRITICAL first, then WARNING, then INFO
        var severityOrder = new Dictionary<string, int> { { "CRITICAL", 0 }, { "WARNING", 1 }, { "INFO", 2 } };
        alerts = alerts.OrderBy(a => severityOrder.GetValueOrDefault(a.Severity, 9)).ThenBy(a => a.CreatedAtUtc).ToList();

        return Ok(new CockpitSummaryDto
        {
            MyPendingActions = myPendingActions,
            MyUrgentItems = myUrgentItems,
            MyAdjustmentItems = myAdjustmentItems,
            MyOverdueItems = myOverdueItems,
            MyNearDeadlineItems = myNearDeadlineItems,

            TotalActiveRequests = pipeline?.TotalActive ?? 0,
            Draft = pipeline?.Draft ?? 0,
            WaitingQuotation = pipeline?.WaitingQuotation ?? 0,
            WaitingAreaApproval = pipeline?.WaitingAreaApproval ?? 0,
            WaitingFinalApproval = pipeline?.WaitingFinalApproval ?? 0,
            InAdjustment = pipeline?.InAdjustment ?? 0,
            AwaitingPo = pipeline?.AwaitingPo ?? 0,
            AwaitingPayment = pipeline?.AwaitingPayment ?? 0,
            PaymentCompleted = pipeline?.PaymentCompleted ?? 0,
            WaitingReceipt = pipeline?.WaitingReceipt ?? 0,
            Completed = pipeline?.Completed ?? 0,

            Bottlenecks = bottlenecks,
            FinancialByStatus = financialByStatus,
            Alerts = alerts
        });
    }

    [HttpGet("purchasing-summary")]
    public async Task<ActionResult<PurchasingSummaryDto>> GetPurchasingSummary()
    {
        var query = await GetScopedRequestsQuery();        var today = DateTime.UtcNow.Date;
        var terminalStates = new[] { "REJECTED", "CANCELLED", "COMPLETED", "QUOTATION_COMPLETED" };
        var receivingStatuses = new[] { "PAYMENT_COMPLETED", "WAITING_RECEIPT", "IN_FOLLOWUP", "PAG_REALIZADO", "AG_RECIBO" };

        var stats = await query
            .GroupBy(r => 1)
            .Select(g => new
            {
                TotalActive = g.Count(r => !terminalStates.Contains(r.Status!.Code)),
                WaitingQuotation = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingQuotation && r.RequestType!.Code == RequestConstants.Types.Quotation),
                AwaitingApproval = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingAreaApproval || r.Status!.Code == RequestConstants.Statuses.WaitingFinalApproval || r.Status!.Code == RequestConstants.Statuses.WaitingCostCenter),
                AwaitingPayment = g.Count(r => r.Status!.Code == RequestConstants.Statuses.PoIssued || r.Status!.Code == RequestConstants.Statuses.PaymentRequestSent || r.Status!.Code == RequestConstants.Statuses.PaymentScheduled),
                PendingReceiving = g.Count(r => receivingStatuses.Contains(r.Status!.Code)),
                Overdue = g.Count(r => !terminalStates.Contains(r.Status!.Code) && r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < today)
            })
            .OrderBy(g => 1)
            .FirstOrDefaultAsync();

        var totalActive = stats?.TotalActive ?? 0;
        var waitingQuotation = stats?.WaitingQuotation ?? 0;
        var awaitingApproval = stats?.AwaitingApproval ?? 0;
        var awaitingPayment = stats?.AwaitingPayment ?? 0;
        var pendingReceiving = stats?.PendingReceiving ?? 0;
        var overdueCount = stats?.Overdue ?? 0;

        var attentionPoints = new List<AttentionPointDto>();


        if (overdueCount > 0)
        {
            attentionPoints.Add(new AttentionPointDto
            {
                Id = "overdue",
                Title = "Pedidos Vencidos",
                Description = $"{overdueCount} pedidos ultrapassaram a data de entrega desejada.",
                Count = overdueCount,
                TargetPath = "/requests?isAttention=true",
                Type = "DANGER"
            });
        }

        // 2. Pending Approval
        if (awaitingApproval > 0)
        {
            attentionPoints.Add(new AttentionPointDto
            {
                Id = "pending-approval",
                Title = "Aprovações Pendentes",
                Description = $"{awaitingApproval} pedidos aguardam validação de fluxo.",
                Count = awaitingApproval,
                TargetPath = "/requests?statusCodes=WAITING_AREA_APPROVAL,WAITING_FINAL_APPROVAL,WAITING_COST_CENTER",
                Type = "WARNING"
            });
        }

        // 3. Waiting Quotation
        if (waitingQuotation > 0)
        {
            attentionPoints.Add(new AttentionPointDto
            {
                Id = "waiting-quotation",
                Title = "Cotações em Falta",
                Description = $"{waitingQuotation} pedidos aguardam registo de proformas.",
                Count = waitingQuotation,
                TargetPath = "/buyer/items",
                Type = "INFO"
            });
        }

        // 4. Pending Receiving
        if (pendingReceiving > 0)
        {
            attentionPoints.Add(new AttentionPointDto
            {
                Id = "pending-receiving",
                Title = "Receção Pendente",
                Description = $"{pendingReceiving} pedidos prontos para entrada em armazém.",
                Count = pendingReceiving,
                TargetPath = "/receiving/workspace",
                Type = "SUCCESS"
            });
        }

        return Ok(new PurchasingSummaryDto
        {
            TotalActiveRequests = totalActive,
            WaitingQuotation = waitingQuotation,
            AwaitingApproval = awaitingApproval,
            AwaitingPayment = awaitingPayment,
            PendingReceiving = pendingReceiving,
            AttentionPoints = attentionPoints
        });
    }


    [HttpGet("pending-approvals")]
    public async Task<ActionResult<PendingApprovalsResponseDto>> GetPendingApprovals()
    {
        var userId = CurrentUserId;
        var roles = CurrentUserRoles;
        bool isAdmin = roles.Contains(RoleConstants.SystemAdministrator);
        bool isFinalApprover = roles.Contains(RoleConstants.FinalApprover);

        var query = await GetScopedRequestsQuery();
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var in4Days = today.AddDays(4);

        // 1. Logic for Area Approvals
        // Rules: status is WAITING_AREA_APPROVAL or WAITING_COST_CENTER, OR has active WAITING_AREA_APPROVAL batch
        // Responsibility (Phase B — DepartmentManager routing, D1): Admin sees all;
        // otherwise the user must be an active manager of the request's department
        // (plant-specific or global), or the legacy nominee on an old in-flight request.
        // The manually assigned "Area Approver" role no longer grants queue visibility.
        var areaStatuses = new[] { RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.WaitingCostCenter };
        var areaQuery = query.Where(r =>
            areaStatuses.Contains(r.Status!.Code) ||
            r.ApprovalBatches.Any(b => b.Status == RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval)
        );
        if (!isAdmin)
        {
            areaQuery = areaQuery.Where(r =>
                r.AreaApproverId == userId ||
                _context.DepartmentManagers.Any(dm =>
                    dm.UserId == userId && dm.IsActive
                    && dm.DepartmentId == r.DepartmentId
                    && (dm.PlantId == null || (r.PlantId != null && dm.PlantId == r.PlantId))));
        }

        // 2. Logic for Final Approvals
        // Rules: status is WAITING_FINAL_APPROVAL, OR has active WAITING_FINAL_APPROVAL batch
        // Responsibility: either user has Final Approver role or user is Admin
        var finalStatuses = new[] { RequestConstants.Statuses.WaitingFinalApproval };
        var finalQuery = query.Where(r => 
            finalStatuses.Contains(r.Status!.Code) ||
            r.ApprovalBatches.Any(b => b.Status == RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval)
        );
        if (!isAdmin && !isFinalApprover)
        {
            // If not admin and not final approver, show nothing in this queue
            finalQuery = finalQuery.Where(r => false);
        }

        var _sw = Stopwatch.StartNew();

        // 3. Execution and Projection
        var areaTasks = await ProjectToListItem(areaQuery, today, tomorrow, in4Days);
        var finalTasks = await ProjectToListItem(finalQuery, today, tomorrow, in4Days);
        
        _sw.Stop();
        _logger.LogInformation("[PERF] GetPendingApprovals executing ProjectToListItem took {Elapsed}ms", _sw.ElapsedMilliseconds);

        return Ok(new PendingApprovalsResponseDto
        {
            AreaApprovals = areaTasks,
            FinalApprovals = finalTasks
        });
    }

    private async Task<List<RequestListItemDto>> ProjectToListItem(IQueryable<Request> query, DateTime today, DateTime tomorrow, DateTime in4Days)
    {
        return await query
            .OrderByDescending(r =>
                (r.Status!.Code == "REJECTED" || r.Status.Code == "CANCELLED" ||
                 r.Status.Code == "COMPLETED" || r.Status.Code == "QUOTATION_COMPLETED")
                    ? -1
                    : (r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < today) ? 3
                    : (r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= today && r.NeedByDateUtc.Value < tomorrow) ? 2
                    : (r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= tomorrow && r.NeedByDateUtc.Value < in4Days) ? 1
                    : 0
            )
            .ThenByDescending(r => r.NeedLevelId ?? 0)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Select(r => new
            {
                r,
                SelectedQ = r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId),
                FirstCostCenter = r.LineItems.Where(l => !l.IsDeleted && l.CostCenter != null).Select(l => l.CostCenter).FirstOrDefault(),
                CompletedStatusHistory = r.StatusHistories.Where(sh => sh.NewStatus.Code == "COMPLETED" || sh.NewStatus.Code == "QUOTATION_COMPLETED" || sh.NewStatus.Code == "PAID" || sh.NewStatus.Code == "PAYMENT_COMPLETED").OrderByDescending(sh => sh.CreatedAtUtc).FirstOrDefault()
            })
            .Select(x => new RequestListItemDto
            {
                Id = x.r.Id,
                RequestNumber = x.r.RequestNumber,
                Title = x.r.Title,
                StatusId = x.r.Status!.Id,
                StatusName = x.r.Status.Name ?? string.Empty,
                StatusCode = x.r.Status.Code ?? string.Empty,
                StatusBadgeColor = x.r.Status.BadgeColor ?? string.Empty,
                RequestTypeId = x.r.RequestType!.Id,
                RequestTypeCode = x.r.RequestType.Code ?? string.Empty,
                RequesterName = x.r.Requester.FullName ?? string.Empty,
                DepartmentName = x.r.Department != null ? x.r.Department.Name : null,
                PlantName = x.r.Plant != null ? x.r.Plant.Name : "---",
                SupplierName = x.r.SelectedQuotationId.HasValue 
                    ? (x.SelectedQ != null ? x.SelectedQ.SupplierNameSnapshot : null)
                    : (x.r.Supplier != null ? x.r.Supplier.Name : null),
                EstimatedTotalAmount = x.r.SelectedQuotationId.HasValue 
                    ? (x.SelectedQ != null ? (decimal?)x.SelectedQ.TotalAmount : 0) ?? 0
                    : x.r.EstimatedTotalAmount,
                CurrencyCode = x.r.SelectedQuotationId.HasValue 
                    ? (x.SelectedQ != null ? x.SelectedQ.Currency : null)
                    : (x.r.Currency != null ? x.r.Currency.Code : null),
                NeedByDateUtc = x.r.NeedByDateUtc,
                CreatedAtUtc = x.r.CreatedAtUtc,
                // Added for Area Approval context
                CostCenterCode = x.FirstCostCenter != null ? x.FirstCostCenter.Code : null,
                CostCenterName = x.FirstCostCenter != null ? x.FirstCostCenter.Name : null,
                CompletedAtUtc = (x.r.Status.Code == "COMPLETED" || x.r.Status.Code == "QUOTATION_COMPLETED" || x.r.Status.Code == "PAID" || x.r.Status.Code == "PAYMENT_COMPLETED")
                    ? (x.CompletedStatusHistory != null ? (DateTime?)x.CompletedStatusHistory.CreatedAtUtc : null)
                    : null,
                PaymentCompletedAtUtc = x.r.ActualPaidAtUtc
            })
            .ToListAsync();
    }

    [HttpGet]
    public async Task<ActionResult<RequestListResponseDto>> GetRequests(
        [FromQuery] string? search = null, 
        [FromQuery] string? statusIds = null,
        [FromQuery] string? typeIds = null,
        [FromQuery] string? plantIds = null,
        [FromQuery] string? companyIds = null,
        [FromQuery] string? departmentIds = null,
        [FromQuery] bool? isAttention = null,
        [FromQuery] bool? myTasksOnly = null,
        [FromQuery] bool? excludeMyTasks = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool isDescending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var _swTotal = Stopwatch.StartNew();
        var _sw = Stopwatch.StartNew();

        var query = await GetScopedRequestsQuery();
        _logger.LogInformation("[PERF GetRequests] 1-ScopeResolution: {Elapsed}ms", _sw.ElapsedMilliseconds);

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var in4Days = today.AddDays(4);

        // 1. Apply Base Filters (those that affect KPI counts too)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim().ToLower();
            query = query.Where(r => (r.RequestNumber != null && r.RequestNumber.ToLower().Contains(searchTerm)) || 
                                     r.Title.ToLower().Contains(searchTerm) ||
                                     (r.Requester != null && r.Requester.FullName != null && r.Requester.FullName.ToLower().Contains(searchTerm)));
        }

        if (!string.IsNullOrWhiteSpace(typeIds))
        {
            var parsedTypeIds = typeIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (parsedTypeIds.Any()) query = query.Where(r => parsedTypeIds.Contains(r.RequestTypeId));
        }

        if (!string.IsNullOrWhiteSpace(companyIds))
        {
            var parsedCompanyIds = companyIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (parsedCompanyIds.Any()) query = query.Where(r => parsedCompanyIds.Contains(r.CompanyId));
        }

        if (!string.IsNullOrWhiteSpace(plantIds))
        {
            var parsedPlantIds = plantIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (parsedPlantIds.Any()) query = query.Where(r => r.PlantId.HasValue && parsedPlantIds.Contains(r.PlantId.Value));
        }

        if (!string.IsNullOrWhiteSpace(departmentIds))
        {
            var parsedDepartmentIds = departmentIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (parsedDepartmentIds.Any()) query = query.Where(r => parsedDepartmentIds.Contains(r.DepartmentId));
        }

        // --- Role based Responsibility Filter ---
        var currentUserId = CurrentUserId;
        var roles = CurrentUserRoles;
        var isFinalApprover = roles.Contains(RoleConstants.FinalApprover);
        var isBuyer = roles.Contains(RoleConstants.Buyer);
        var isFinance = roles.Contains(RoleConstants.Finance);
        var isReceiver = roles.Contains(RoleConstants.Receiving);

        var receivingCodes = new[] { "WAITING_RECEIPT", RequestConstants.Statuses.PaymentCompleted, "PAG_REALIZADO", "AG_RECIBO", "WAITING_SUPPLIER_DELIVERY" };

        Expression<Func<AlplaPortal.Domain.Entities.Request, bool>> myTasksCriteria = r =>
            // Solicitante: Em rascunho, ajuste ou Aprovado (Pagamento - para acompanhamento/vencimento)
            (r.RequesterId == currentUserId && (r.Status!.Code == RequestConstants.Statuses.Draft || r.Status!.Code == RequestConstants.Statuses.AreaAdjustment || r.Status!.Code == RequestConstants.Statuses.FinalAdjustment || (r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Payment))) ||
            // Aprovador de Área (Role ou explicitamente designado)
            ((r.AreaApproverId == currentUserId || _context.DepartmentManagers.Any(dm => dm.UserId == currentUserId && dm.IsActive && dm.DepartmentId == r.DepartmentId && (dm.PlantId == null || (r.PlantId != null && dm.PlantId == r.PlantId)))) && r.Status!.Code == RequestConstants.Statuses.WaitingAreaApproval) ||
            // Aprovador Final
            (isFinalApprover && r.Status!.Code == RequestConstants.Statuses.WaitingFinalApproval) ||
            // Comprador
            (isBuyer && (r.Status!.Code == RequestConstants.Statuses.WaitingQuotation || (r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Quotation) || r.Status!.Code == "WAITING_SUPPLIER_DELIVERY") && (r.BuyerId == currentUserId || r.BuyerId == null)) ||
            // Comprador — partial quotation: QUOTATION requests with pending line items requiring buyer action (regardless of parent status)
            // Excludes terminal statuses (CANCELLED, REJECTED, COMPLETED, PAID, PAYMENT_COMPLETED) to prevent closed requests with legacy null QLStatus items from appearing
            (isBuyer && r.RequestType!.Code == RequestConstants.Types.Quotation && !new[] { RequestConstants.Statuses.Cancelled, RequestConstants.Statuses.Rejected, RequestConstants.Statuses.Completed, RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(r.Status!.Code) && r.LineItems.Any(li => !li.IsDeleted && (li.QuotationLifecycleStatus == null || li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending)) && (r.BuyerId == currentUserId || r.BuyerId == null)) ||
            // Solicitante / Aprovador de Área — NOT_QUOTED proposals awaiting decision
            // Shows QUOTATION requests where at least one active item has a pending not-quoted proposal
            ((r.RequesterId == currentUserId || _context.DepartmentManagers.Any(dm => dm.UserId == currentUserId && dm.IsActive && dm.DepartmentId == r.DepartmentId && (dm.PlantId == null || (r.PlantId != null && dm.PlantId == r.PlantId)))) && r.RequestType!.Code == RequestConstants.Types.Quotation && !new[] { RequestConstants.Statuses.Cancelled, RequestConstants.Statuses.Rejected, RequestConstants.Statuses.Completed, RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(r.Status!.Code) && r.LineItems.Any(li => !li.IsDeleted && li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed)) ||
            // Financeiro
            (isFinance && ((r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Payment) || r.Status!.Code == RequestConstants.Statuses.PoIssued || r.Status!.Code == RequestConstants.Statuses.PaymentRequestSent || r.Status!.Code == RequestConstants.Statuses.PaymentScheduled || r.Status!.Code == "ADVANCE_PAYMENT_REQUIRED" || r.Status!.Code == "WAITING_RECONCILIATION")) ||
            // Recebimento (Requester ou Role)
            ((r.RequesterId == currentUserId || isReceiver) && receivingCodes.Contains(r.Status!.Code));

        if (myTasksOnly == true)
        {
            query = query.Where(myTasksCriteria);
        }
        else if (excludeMyTasks == true)
        {
            query = query.Where(r => !(
                (r.RequesterId == currentUserId && (r.Status!.Code == RequestConstants.Statuses.Draft || r.Status!.Code == RequestConstants.Statuses.AreaAdjustment || r.Status!.Code == RequestConstants.Statuses.FinalAdjustment || (r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Payment))) ||
                ((r.AreaApproverId == currentUserId || _context.DepartmentManagers.Any(dm => dm.UserId == currentUserId && dm.IsActive && dm.DepartmentId == r.DepartmentId && (dm.PlantId == null || (r.PlantId != null && dm.PlantId == r.PlantId)))) && r.Status!.Code == RequestConstants.Statuses.WaitingAreaApproval) ||
                (isFinalApprover && r.Status!.Code == RequestConstants.Statuses.WaitingFinalApproval) ||
                (isBuyer && (r.Status!.Code == RequestConstants.Statuses.WaitingQuotation || (r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Quotation) || r.Status!.Code == "WAITING_SUPPLIER_DELIVERY") && (r.BuyerId == currentUserId || r.BuyerId == null)) ||
                // Comprador — partial quotation: QUOTATION requests with pending line items
                (isBuyer && r.RequestType!.Code == RequestConstants.Types.Quotation && !new[] { RequestConstants.Statuses.Cancelled, RequestConstants.Statuses.Rejected, RequestConstants.Statuses.Completed, RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(r.Status!.Code) && r.LineItems.Any(li => !li.IsDeleted && (li.QuotationLifecycleStatus == null || li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending)) && (r.BuyerId == currentUserId || r.BuyerId == null)) ||
                // Solicitante / Aprovador de Área — NOT_QUOTED proposals awaiting decision
                ((r.RequesterId == currentUserId || _context.DepartmentManagers.Any(dm => dm.UserId == currentUserId && dm.IsActive && dm.DepartmentId == r.DepartmentId && (dm.PlantId == null || (r.PlantId != null && dm.PlantId == r.PlantId)))) && r.RequestType!.Code == RequestConstants.Types.Quotation && !new[] { RequestConstants.Statuses.Cancelled, RequestConstants.Statuses.Rejected, RequestConstants.Statuses.Completed, RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(r.Status!.Code) && r.LineItems.Any(li => !li.IsDeleted && li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed)) ||
                (isFinance && ((r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Payment) || r.Status!.Code == RequestConstants.Statuses.PoIssued || r.Status!.Code == RequestConstants.Statuses.PaymentRequestSent || r.Status!.Code == RequestConstants.Statuses.PaymentScheduled || r.Status!.Code == "ADVANCE_PAYMENT_REQUIRED" || r.Status!.Code == "WAITING_RECONCILIATION")) ||
                ((r.RequesterId == currentUserId || isReceiver) && (receivingCodes.Contains(r.Status!.Code) || r.Status!.Code == "WAITING_SUPPLIER_DELIVERY"))
            ));
        }
        // --- 

        // 2. Calculate Dashboard Summary (Aware of base filters, but before status filters)
        _sw.Restart();
        var counts = await query
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                WaitingQuotation = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingQuotation && r.RequestType!.Code == RequestConstants.Types.Quotation),
                AwaitingApproval = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingAreaApproval || r.Status!.Code == RequestConstants.Statuses.WaitingFinalApproval || r.Status!.Code == RequestConstants.Statuses.WaitingCostCenter),
                AwaitingPo = g.Count(r => (r.Status!.Code == RequestConstants.Statuses.FinalApproved || r.Status!.Code == RequestConstants.Statuses.QuotationCompleted || r.Status!.Code == RequestConstants.Statuses.WaitingPoCorrection) && r.RequestType!.Code == RequestConstants.Types.Quotation),
                AwaitingPayment = g.Count(r => (r.Status!.Code == RequestConstants.Statuses.FinalApproved && r.RequestType!.Code == RequestConstants.Types.Payment) || r.Status!.Code == RequestConstants.Statuses.PoIssued || r.Status!.Code == RequestConstants.Statuses.PaymentRequestSent || r.Status!.Code == RequestConstants.Statuses.PaymentScheduled),
                Completed = g.Count(r => r.Status!.Code == "COMPLETED") // COMPLETED status not yet in constants
            })
            .OrderBy(g => 1)
            .FirstOrDefaultAsync();

        _logger.LogInformation("[PERF GetRequests] 2-DashboardCounts: {Elapsed}ms", _sw.ElapsedMilliseconds);
        _sw.Restart();

        var pendingMyApprovalCount = await query
            .Where(myTasksCriteria)
            .CountAsync(r => r.Status!.Code == RequestConstants.Statuses.WaitingAreaApproval || r.Status!.Code == RequestConstants.Statuses.WaitingFinalApproval || r.Status!.Code == RequestConstants.Statuses.WaitingCostCenter);
        _logger.LogInformation("[PERF GetRequests] 3-PendingMyApproval: {Elapsed}ms", _sw.ElapsedMilliseconds);

        var summary = new DashboardSummaryDto
        {
            TotalRequests = counts?.Total ?? 0,
            WaitingQuotation = counts?.WaitingQuotation ?? 0,
            AwaitingApproval = counts?.AwaitingApproval ?? 0,
            PendingMyApproval = pendingMyApprovalCount,
            AwaitingPo = counts?.AwaitingPo ?? 0,
            AwaitingPayment = counts?.AwaitingPayment ?? 0,
            CompletedRequests = counts?.Completed ?? 0
        };

        // 3. Apply List-Specific Filters (Status, Attention)
        if (!string.IsNullOrWhiteSpace(statusIds))
        {
            var parsedStatusIds = statusIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (parsedStatusIds.Any()) query = query.Where(r => parsedStatusIds.Contains(r.StatusId));
        }

        if (isAttention == true)
        {
            // O dashboard "Para Minha Ação" agora é puramente baseado em tarefas pendentes,
            // independentemente do prazo (vencimento), para garantir que processos "parados" no fluxo
            // fiquem visíveis para os responsáveis.
            query = query.Where(myTasksCriteria);
        }

        // 3a. Calculate Filtered Total (Monetary Total)
        // Opting for SelectMany / DefaultIfEmpty to compute sum on database-side without N+1 or local evaluation
        _sw.Restart();
        var filteredTotal = await query
            .SelectMany(
                r => r.Quotations.Where(q => q.Id == r.SelectedQuotationId).DefaultIfEmpty(),
                (r, q) => q != null ? q.TotalAmount : r.EstimatedTotalAmount
            )
            .SumAsync();

        // 3b. Retrieve Distinct Currency Codes for Multi-currency Protection
        // Running as a separate query ensures EF can safely translate the Distinct/ToList projection
        var filteredCurrencyCodes = await query
            .Select(r => r.SelectedQuotationId.HasValue 
                ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId).Select(q => q.Currency).FirstOrDefault() 
                : (r.Currency != null ? r.Currency.Code : null))
            .Where(c => c != null)
            .Select(c => c!)
            .Distinct()
            .ToListAsync();

        _logger.LogInformation("[PERF GetRequests] 4-FilteredTotalAndCurrencies: {Elapsed}ms", _sw.ElapsedMilliseconds);

        summary.FilteredTotal = filteredTotal;
        summary.FilteredCurrencyCodes = filteredCurrencyCodes ?? new List<string>();

        // 3c. Calculate Filtered Trend (MTD vs PMTD)
        _sw.Restart();
        var now = DateTime.UtcNow;
        var currentMtdStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        
        var prevMtdStart = currentMtdStart.AddMonths(-1);
        var prevMtdEnd = now.AddMonths(-1);

        // Current MTD Total & Currencies
        var currentMtdTotal = await query
            .Where(r => r.CreatedAtUtc >= currentMtdStart && r.CreatedAtUtc <= now)
            .SelectMany(
                r => r.Quotations.Where(q => q.Id == r.SelectedQuotationId).DefaultIfEmpty(),
                (r, q) => q != null ? q.TotalAmount : r.EstimatedTotalAmount
            )
            .SumAsync();
        
        var currentMtdCurrencies = await query
            .Where(r => r.CreatedAtUtc >= currentMtdStart && r.CreatedAtUtc <= now)
            .Select(r => r.SelectedQuotationId.HasValue 
                ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId).Select(q => q.Currency).FirstOrDefault() 
                : (r.Currency != null ? r.Currency.Code : null))
            .Where(c => c != null)
            .Distinct()
            .ToListAsync();

        // Previous MTD Total & Currencies
        var prevMtdTotal = await query
            .Where(r => r.CreatedAtUtc >= prevMtdStart && r.CreatedAtUtc <= prevMtdEnd)
            .SelectMany(
                r => r.Quotations.Where(q => q.Id == r.SelectedQuotationId).DefaultIfEmpty(),
                (r, q) => q != null ? q.TotalAmount : r.EstimatedTotalAmount
            )
            .SumAsync();

        var prevMtdCurrencies = await query
            .Where(r => r.CreatedAtUtc >= prevMtdStart && r.CreatedAtUtc <= prevMtdEnd)
            .Select(r => r.SelectedQuotationId.HasValue 
                ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId).Select(q => q.Currency).FirstOrDefault() 
                : (r.Currency != null ? r.Currency.Code : null))
            .Where(c => c != null)
            .Distinct()
            .ToListAsync();

        // Safe Trend Calculation (Multi-currency and Zero-baseline checks)
        bool isCurrentSafe = currentMtdCurrencies.Count == 1;
        bool isPrevSafe = prevMtdCurrencies.Count == 1;
        bool sameCurrency = isCurrentSafe && isPrevSafe && currentMtdCurrencies[0] == prevMtdCurrencies[0];

        if (sameCurrency && prevMtdTotal > 0)
        {
            summary.FilteredTotalTrend = ((currentMtdTotal - prevMtdTotal) / prevMtdTotal) * 100;
            summary.FilteredTotalTrendLabel = "vs mês anterior";
        }
        else
        {
            summary.FilteredTotalTrend = null;
            summary.FilteredTotalTrendLabel = "Sem comparativo";
        }

        _logger.LogInformation("[PERF GetRequests] 5-MTDTrend: {Elapsed}ms", _sw.ElapsedMilliseconds);

        // 4. Final Projection and Pagination
        _sw.Restart();
        var totalCount = await query.CountAsync();
        _logger.LogInformation("[PERF GetRequests] 6-PaginationCount: {Elapsed}ms", _sw.ElapsedMilliseconds);

        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            switch (sortBy.ToLower())
            {
                case "requestnumber":
                    // RequestNumber format is REQ-DD/MM/YYYY-NNN.
                    // Lexicographic string sort is incorrect (DD in position 4 breaks chronological order).
                    // Sort by CreatedAtUtc.Date for chronological order, then by RequestNumber
                    // as tiebreaker (the zero-padded sequence suffix sorts correctly within same date).
                    query = isDescending
                        ? query.OrderByDescending(r => r.CreatedAtUtc.Date).ThenByDescending(r => r.RequestNumber)
                        : query.OrderBy(r => r.CreatedAtUtc.Date).ThenBy(r => r.RequestNumber);
                    break;
                case "title":
                    query = isDescending ? query.OrderByDescending(r => r.Title) : query.OrderBy(r => r.Title);
                    break;
                case "statusname":
                    query = isDescending ? query.OrderByDescending(r => r.Status!.Name) : query.OrderBy(r => r.Status!.Name);
                    break;
                case "requestername":
                    query = isDescending ? query.OrderByDescending(r => r.Requester!.FullName) : query.OrderBy(r => r.Requester!.FullName);
                    break;
                case "departmentname":
                    query = isDescending ? query.OrderByDescending(r => r.Department!.Name) : query.OrderBy(r => r.Department!.Name);
                    break;
                case "statuscode":
                    query = isDescending ? query.OrderByDescending(r => r.Status!.DisplayOrder) : query.OrderBy(r => r.Status!.DisplayOrder);
                    break;
                case "requesttypecode":
                    query = isDescending ? query.OrderByDescending(r => r.RequestType!.Name) : query.OrderBy(r => r.RequestType!.Name);
                    break;
                case "companyname":
                    query = isDescending ? query.OrderByDescending(r => r.Company!.Name) : query.OrderBy(r => r.Company!.Name);
                    break;
                case "needbydateutc":
                    query = isDescending ? query.OrderByDescending(r => r.NeedByDateUtc) : query.OrderBy(r => r.NeedByDateUtc);
                    break;
                case "estimatedtotalamount":
                    query = isDescending
                        ? query.OrderByDescending(r => r.SelectedQuotationId.HasValue
                            ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId).Select(q => (decimal?)q.TotalAmount).FirstOrDefault()
                            : (decimal?)r.EstimatedTotalAmount)
                        : query.OrderBy(r => r.SelectedQuotationId.HasValue
                            ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId).Select(q => (decimal?)q.TotalAmount).FirstOrDefault()
                            : (decimal?)r.EstimatedTotalAmount);
                    break;
                case "createdatutc":
                    query = isDescending ? query.OrderByDescending(r => r.CreatedAtUtc) : query.OrderBy(r => r.CreatedAtUtc);
                    break;
                default:
                    query = isDescending ? query.OrderByDescending(r => r.CreatedAtUtc) : query.OrderBy(r => r.CreatedAtUtc);
                    break;
            }
        }
        else
        {
            query = query
                .OrderByDescending(r =>
                    // Finalized statuses always rank last (-1), below all active items including those with no deadline (0).
                    (r.Status!.Code == "REJECTED" || r.Status.Code == "CANCELLED" ||
                     r.Status.Code == "COMPLETED" || r.Status.Code == "QUOTATION_COMPLETED")
                        ? -1                                                                                        // finalized — always last
                        : (r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < today) ? 3                          // overdue
                        : (r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= today && r.NeedByDateUtc.Value < tomorrow) ? 2  // due today
                        : (r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= tomorrow && r.NeedByDateUtc.Value < in4Days) ? 1 // due soon (≤3 days)
                        : 0                                                                                         // active / no urgent deadline
                )
                .ThenByDescending(r => r.NeedLevelId ?? 0) // Crítico(4) > Urgente(3) > Normal(2) > Baixo(1) > sem nível(0)
                .ThenByDescending(r => r.CreatedAtUtc);
        }

        _sw.Restart();
        var items = await query
            .Select(r => new
            {
                r,
                SelectedQ = r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId),
                CompletedStatusHistory = r.StatusHistories.Where(sh => sh.NewStatus.Code == "COMPLETED" || sh.NewStatus.Code == "QUOTATION_COMPLETED" || sh.NewStatus.Code == "PAID" || sh.NewStatus.Code == "PAYMENT_COMPLETED").OrderByDescending(sh => sh.CreatedAtUtc).FirstOrDefault()
            })
            .Select(x => new RequestListItemDto
            {
                Id = x.r.Id,
                RequestNumber = x.r.RequestNumber,
                Title = x.r.Title,
                StatusId = x.r.Status!.Id,
                StatusName = x.r.Status.Name ?? string.Empty,
                StatusCode = x.r.Status.Code ?? string.Empty,
                StatusDisplayOrder = x.r.Status.DisplayOrder,
                StatusBadgeColor = x.r.Status.BadgeColor ?? string.Empty,
                RequestTypeId = x.r.RequestType!.Id,
                RequestTypeName = x.r.RequestType.Name ?? string.Empty,
                RequestTypeCode = x.r.RequestType.Code ?? string.Empty,
                NeedLevelId = x.r.NeedLevelId,
                NeedLevelName = x.r.NeedLevel != null ? x.r.NeedLevel.Name : null,
                RequesterId = x.r.Requester!.Id,
                RequesterName = x.r.Requester.FullName ?? string.Empty,
                BuyerId = x.r.BuyerId,
                BuyerName = x.r.Buyer != null ? x.r.Buyer.FullName : null,
                AreaApproverId = x.r.AreaApproverId,
                AreaApproverName = x.r.AreaApprover != null ? x.r.AreaApprover.FullName : null,
                FinalApproverId = x.r.FinalApproverId,
                FinalApproverName = x.r.FinalApprover != null ? x.r.FinalApprover.FullName : null,
                DepartmentId = x.r.DepartmentId,
                DepartmentName = x.r.Department != null ? x.r.Department.Name : null,
                CompanyId = x.r.CompanyId,
                CompanyName = x.r.Company != null ? x.r.Company.Name : string.Empty,
                PlantId = x.r.PlantId,
                PlantName = x.r.Plant != null ? x.r.Plant.Name : "---",
                SupplierId = x.r.SelectedQuotationId.HasValue 
                    ? (x.SelectedQ != null ? (int?)x.SelectedQ.SupplierId : null)
                    : x.r.SupplierId,
                SupplierName = x.r.SelectedQuotationId.HasValue 
                    ? (x.SelectedQ != null ? x.SelectedQ.SupplierNameSnapshot : null)
                    : (x.r.Supplier != null ? x.r.Supplier.Name : null),
                SupplierPortalCode = x.r.SelectedQuotationId.HasValue 
                    ? null 
                    : (x.r.Supplier != null ? x.r.Supplier.PortalCode : null),
                EstimatedTotalAmount = x.r.SelectedQuotationId.HasValue 
                    ? (x.SelectedQ != null ? (decimal?)x.SelectedQ.TotalAmount : 0) ?? 0
                    : x.r.EstimatedTotalAmount,
                CurrencyId = x.r.CurrencyId,
                CurrencyCode = x.r.SelectedQuotationId.HasValue 
                    ? (x.SelectedQ != null ? x.SelectedQ.Currency : null)
                    : (x.r.Currency != null ? x.r.Currency.Code : null),
                RequestedDateUtc = x.r.RequestedDateUtc,
                NeedByDateUtc = x.r.NeedByDateUtc,
                CreatedAtUtc = x.r.CreatedAtUtc,
                IsCancelled = x.r.IsCancelled,
                SelectedQuotationId = x.r.SelectedQuotationId,
                CapexOpexClassificationId = x.r.CapexOpexClassificationId,
                CompletedAtUtc = (x.r.Status.Code == "COMPLETED" || x.r.Status.Code == "QUOTATION_COMPLETED" || x.r.Status.Code == "PAID" || x.r.Status.Code == "PAYMENT_COMPLETED")
                    ? (x.CompletedStatusHistory != null ? (DateTime?)x.CompletedStatusHistory.CreatedAtUtc : null)
                    : null,
                PaymentCompletedAtUtc = x.r.ActualPaidAtUtc
            })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        _logger.LogInformation("[PERF GetRequests] 7-ItemsQuery: {Elapsed}ms (page={Page}, pageSize={PageSize})", _sw.ElapsedMilliseconds, page, pageSize);

        // ── Hydrate DisplayWorkflowState for QUOTATION requests ──
        var quotationRequestIds = items
            .Where(i => i.RequestTypeCode == RequestConstants.Types.Quotation)
            .Select(i => i.Id)
            .ToList();

        if (quotationRequestIds.Any())
        {
            var batchData = await _context.Requests
                .AsNoTracking()
                .Where(r => quotationRequestIds.Contains(r.Id))
                .Select(r => new
                {
                    r.Id,
                    LineItems = r.LineItems.Where(li => !li.IsDeleted).ToList(),
                    Batches = r.ApprovalBatches.ToList(),
                    PoGroups = r.PoGroups.ToList(),
                    StatusCode = r.Status!.Code
                })
                .AsSplitQuery()
                .ToListAsync();

            var lookup = batchData.ToDictionary(x => x.Id);
            foreach (var item in items)
            {
                if (lookup.TryGetValue(item.Id, out var data))
                {
                    item.DisplayWorkflowState = _statusSyncService.ComputeDisplayWorkflowState(
                        RequestConstants.Types.Quotation,
                        data.StatusCode,
                        data.LineItems,
                        data.Batches,
                        data.PoGroups);
                }
            }
        }

        // PAYMENT requests: mirror status code
        foreach (var item in items.Where(i => i.RequestTypeCode == RequestConstants.Types.Payment))
        {
            item.DisplayWorkflowState = item.StatusCode;
        }

        _swTotal.Stop();
        _logger.LogInformation("[PERF GetRequests] TOTAL: {Elapsed}ms | items={Count} totalCount={TotalCount}", _swTotal.ElapsedMilliseconds, items.Count, totalCount);

        return Ok(new RequestListResponseDto
        {
            PagedResult = new PagedResult<RequestListItemDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            },
            Summary = summary
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RequestDetailsDto>> GetRequest(Guid id)
    {
        var _sw = Stopwatch.StartNew();
        
        // To avoid massive cartesian product and improve performance, we project into the DTO directly.
        // However, the repeated FirstOrDefault calls for the selected quotation can be expensive.
        // We ensure Unit is projected for items.
        
        var request = await _context.Requests
            .AsNoTracking()
            .AsSplitQuery()
            .Where(r => r.Id == id)
            .Select(r => new RequestDetailsDto
            {
                Id = r.Id,
                RequestNumber = r.RequestNumber,
                Title = r.Title,
                Description = r.Description,
                StatusId = r.Status!.Id,
                StatusName = r.Status.Name ?? string.Empty,
                StatusCode = r.Status.Code ?? string.Empty,
                StatusDisplayOrder = r.Status.DisplayOrder,
                StatusBadgeColor = r.Status.BadgeColor ?? string.Empty,
                RequestTypeId = r.RequestType!.Id,
                RequestTypeName = r.RequestType.Name ?? string.Empty,
                RequestTypeCode = r.RequestType.Code ?? string.Empty,
                NeedLevelId = r.NeedLevelId,
                NeedLevelName = r.NeedLevel != null ? r.NeedLevel.Name : null,
                CurrencyId = r.CurrencyId,
                CapexOpexClassificationId = r.CapexOpexClassificationId,
                RequesterId = r.Requester!.Id,
                RequesterName = r.Requester.FullName ?? string.Empty,
                BuyerId = r.BuyerId,
                BuyerName = r.Buyer != null ? r.Buyer.FullName : null,
                AreaApproverId = r.AreaApproverId,
                AreaApproverName = r.AreaApprover != null ? r.AreaApprover.FullName : null,
                FinalApproverId = r.FinalApproverId,
                FinalApproverName = r.FinalApprover != null ? r.FinalApprover.FullName : null,
                DepartmentId = r.DepartmentId,
                DepartmentName = r.Department != null ? r.Department.Name : null,
                CompanyId = r.CompanyId,
                CompanyName = r.Company != null ? r.Company.Name : string.Empty,
                PlantId = r.PlantId,
                PlantName = r.Plant != null ? r.Plant.Name : null,
                
                // Optimized header fields: we fetch the Selected Quotation ID Once and use it
                SelectedQuotationId = r.SelectedQuotationId,
                
                // We'll populate Selected Quotation specific fields after the query to avoid redundant subqueries
                SupplierId = r.SupplierId,
                SupplierName = r.Supplier != null ? r.Supplier.Name : null,
                SupplierPortalCode = r.Supplier != null ? r.Supplier.PortalCode : null,

                EstimatedTotalAmount = r.EstimatedTotalAmount,
                DiscountAmount = r.DiscountAmount,
                CurrencyCode = r.Currency != null ? r.Currency.Code : null,

                // B2P: Payment Condition
                PaymentConditionCode = r.PaymentConditionCode,
                AdvancePaymentPercent = r.AdvancePaymentPercent,
                PaymentConditionSource = r.PaymentConditionSource,
                
                RequestedDateUtc = r.RequestedDateUtc,
                NeedByDateUtc = r.NeedByDateUtc,
                CreatedAtUtc = r.CreatedAtUtc,
                IsCancelled = r.IsCancelled,
                
                LineItems = r.LineItems.Where(li => !li.IsDeleted).Select(li => new RequestLineItemDto
                {
                    Id = li.Id,
                    LineNumber = li.LineNumber,
                    ItemPriority = li.ItemPriority,
                    Description = li.Description,
                    Quantity = li.Quantity,
                    Unit = li.Unit != null ? li.Unit.Code : null, 
                    UnitPrice = li.UnitPrice,
                    DiscountPercent = li.DiscountPercent,
                    DiscountAmount = li.DiscountAmount,
                    TotalAmount = li.TotalAmount,
                    SupplierName = li.SupplierName,
                    Notes = li.Notes,
                    LineItemStatusCode = li.LineItemStatus != null ? li.LineItemStatus.Code : null,
                    LineItemStatusName = li.LineItemStatus != null ? li.LineItemStatus.Name : null,
                    LineItemStatusBadgeColor = li.LineItemStatus != null ? li.LineItemStatus.BadgeColor : null,
                    ReceivedQuantity = li.ReceivedQuantity,
                    DivergenceNotes = li.DivergenceNotes,
                    PlantId = li.PlantId,
                    PlantName = li.Plant != null ? li.Plant.Name : null,
                    CostCenterId = li.CostCenterId,
                    CostCenterName = li.CostCenter != null ? li.CostCenter.Name : null,
                    CostCenterCode = li.CostCenter != null ? li.CostCenter.Code : null,
                    IvaRateId = li.IvaRateId,
                    IvaRateCode = li.IvaRate != null ? li.IvaRate.Code : null,
                    IvaRateName = li.IvaRate != null ? li.IvaRate.Name : null,
                    IvaRatePercent = li.IvaRate != null ? li.IvaRate.RatePercent : null,
                    SupplierId = li.SupplierId,
                    CurrencyId = li.CurrencyId,
                    CurrencyCode = li.Currency != null ? li.Currency.Code : null,
                    DueDate = li.DueDate,
                    ItemCatalogId = li.ItemCatalogId,
                    ItemCatalogCode = li.ItemCatalogItem != null ? li.ItemCatalogItem.Code : null,
                    QuotationLifecycleStatus = li.QuotationLifecycleStatus,
                    NotQuotedJustification = li.NotQuotedJustification,
                    NotQuotedProposedAtUtc = li.NotQuotedProposedAtUtc,
                    RequestPoGroupId = li.RequestPoGroupId,
                    SelectedQuotationItemId = li.SelectedQuotationItemId,
                    Allocations = li.Allocations != null ? li.Allocations.Select(a => new RequestLineItemAllocationDto
                    {
                        Id = a.Id,
                        PlantId = a.PlantId,
                        PlantName = a.Plant != null ? a.Plant.Name : null,
                        CostCenterId = a.CostCenterId,
                        CostCenterName = a.CostCenter != null ? a.CostCenter.Name : null,
                        CostCenterCode = a.CostCenter != null ? a.CostCenter.Code : null,
                        Percentage = a.Percentage,
                        AllocationOrder = a.AllocationOrder
                    }).ToList() : new List<RequestLineItemAllocationDto>()
                }).ToList(),

                PoGroups = r.PoGroups.Select(g => new RequestPoGroupDto
                {
                    Id = g.Id,
                    RequestId = g.RequestId,
                    SupplierId = g.SupplierId,
                    SupplierNameSnapshot = g.SupplierNameSnapshot,
                    SupplierNifSnapshot = g.SupplierNifSnapshot,
                    CurrencyId = g.CurrencyId,
                    CurrencyCode = g.CurrencyCode,
                    TotalAmount = g.TotalAmount,
                    PaymentConditionCode = g.PaymentConditionCode,
                    AdvancePaymentPercent = g.AdvancePaymentPercent,
                    Status = g.Status,
                    PurchaseOrderNumber = g.PurchaseOrderNumber,
                    CreatedAtUtc = g.CreatedAtUtc,
                    CreatedByUserId = g.CreatedByUserId,
                    LineItemCount = g.LineItems.Count,
                    AttachmentCount = g.PoAttachments.Count
                }).OrderBy(g => g.CreatedAtUtc).ToList(),

                Attachments = r.Attachments.Where(a => !a.IsDeleted).Select(a => new RequestAttachmentDto
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    FileExtension = a.FileExtension,
                    FileSizeMBytes = a.FileSizeMBytes,
                    AttachmentTypeCode = a.AttachmentTypeCode,
                    UploadedAtUtc = a.UploadedAtUtc,
                    UploadedByName = a.UploadedByUser!.FullName
                }).ToList(),

                StatusHistory = r.StatusHistories.Select(sh => new RequestStatusHistoryDto
                {
                    Id = sh.Id,
                    ActionTaken = sh.ActionTaken,
                    NewStatusName = sh.NewStatus!.Name,
                    Comment = sh.Comment,
                    CreatedAtUtc = sh.CreatedAtUtc,
                    ActorUserId = sh.ActorUserId,
                    ActorName = sh.ActorUser!.FullName
                }).OrderByDescending(sh => sh.CreatedAtUtc).ToList(),

                Quotations = r.Quotations.Select(q => new SavedQuotationDto
                {
                    Id = q.Id,
                    RequestId = q.RequestId,
                    SupplierId = q.SupplierId,
                    SupplierNameSnapshot = q.SupplierNameSnapshot,
                    SupplierPortalCode = q.Supplier != null ? q.Supplier.PortalCode : null,
                    SupplierPrimaveraCode = q.Supplier != null ? q.Supplier.PrimaveraCode : null,
                    SupplierRegistrationStatus = q.Supplier != null ? q.Supplier.RegistrationStatus : null,
                    DocumentNumber = q.DocumentNumber,
                    DocumentDate = q.DocumentDate,
                    Currency = q.Currency,
                    TotalAmount = q.TotalAmount,
                    SourceType = q.SourceType,
                    SourceFileName = q.SourceFileName,
                    IsSelected = q.IsSelected,
                    CreatedAtUtc = q.CreatedAtUtc,
                    ItemCount = q.Items.Count,
                    Items = q.Items.Select(qi => new SavedQuotationItemDto
                    {
                        Id = qi.Id,
                        LineNumber = qi.LineNumber,
                        Description = qi.Description,
                        Quantity = qi.Quantity,
                        UnitPrice = qi.UnitPrice,
                        GrossSubtotal = qi.GrossSubtotal,
                        TaxableBase = qi.GrossSubtotal, // In this domain, TaxableBase is usually the same as GrossSubtotal (after line discount)
                        IvaAmount = qi.IvaAmount,
                        LineTotal = qi.LineTotal,
                        ItemCatalogId = qi.ItemCatalogId,
                        ItemCatalogCode = qi.ItemCatalog != null ? qi.ItemCatalog.Code : null,
                        MappedRequestLineItemId = qi.MappedRequestLineItemId,
                        ReconciliationStatus = qi.ReconciliationStatus,
                        ReconciliationJustification = qi.ReconciliationJustification,
                        UnitId = qi.UnitId,
                        // Ensure Unit properties are projected; EF Core will join Units table
                        UnitName = qi.Unit != null ? qi.Unit.Name : null,
                        UnitCode = qi.Unit != null ? qi.Unit.Code : null,
                        ReceivedQuantity = qi.ReceivedQuantity,
                        DivergenceNotes = qi.DivergenceNotes,
                        LineItemStatusCode = qi.LineItemStatus != null ? qi.LineItemStatus.Code : null,
                        LineItemStatusName = qi.LineItemStatus != null ? qi.LineItemStatus.Name : null,
                        LineItemStatusBadgeColor = qi.LineItemStatus != null ? qi.LineItemStatus.BadgeColor : null
                    }).ToList()
                }).OrderByDescending(q => q.CreatedAtUtc).ToList(),

                ApprovalBatches = r.ApprovalBatches.Select(b => new RequestApprovalBatchDto
                {
                    Id = b.Id,
                    BatchNumber = b.BatchNumber,
                    Status = b.Status,
                    Comment = b.Comment,
                    CreatedAtUtc = b.CreatedAtUtc,
                    BudgetJustification = b.BudgetJustification,
                    ApprovedTotalAmount = b.ApprovedTotalAmount,
                    CreatedByUserId = b.CreatedByUserId,
                    UpdatedByUserId = b.UpdatedByUserId,
                    UpdatedAtUtc = b.UpdatedAtUtc,
                    Items = b.Items.Select(bi => new RequestApprovalBatchItemDto
                    {
                        Id = bi.Id,
                        RequestLineItemId = bi.RequestLineItemId,
                        SelectedQuotationItemId = bi.SelectedQuotationItemId
                    }).ToList()
                }).OrderByDescending(b => b.CreatedAtUtc).ToList()
            })
            .AsSplitQuery()
            .FirstOrDefaultAsync();
            
        _sw.Stop();
        _logger.LogInformation("[PERF] GetRequest(id) database query and projection took {Elapsed}ms for RequestId: {Id}", _sw.ElapsedMilliseconds, id);

        if (request == null) return NotFound();

        // Phase B: pending area approval with nobody decided yet → expose the eligible
        // managers (DepartmentManager routing) for the "Pendente — N responsáveis
        // elegíveis" display. Legacy nominated requests keep showing AreaApproverName.
        if (request.StatusCode == "WAITING_AREA_APPROVAL" && request.AreaApproverId == null)
        {
            var eligibleRouting = await _approvalRouting.ResolveAreaManagersAsync(request.DepartmentId, request.PlantId);
            request.EligibleAreaManagerNames = eligibleRouting.Managers.Select(m => m.FullName).ToList();
        }

        // Enrich not-quoted proposer display name — NotQuotedProposedByUserId has no
        // EF navigation property, so it isn't available inside the projection above.
        var notQuotedProposers = await _context.RequestLineItems
            .Where(li => li.RequestId == id && li.NotQuotedProposedByUserId != null)
            .Select(li => new { LineItemId = li.Id, li.NotQuotedProposedByUserId })
            .ToListAsync();

        if (notQuotedProposers.Any())
        {
            var proposerUserIds = notQuotedProposers.Select(x => x.NotQuotedProposedByUserId!.Value).Distinct().ToList();
            var proposerNamesById = await _context.Users
                .Where(u => proposerUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName);

            foreach (var proposer in notQuotedProposers)
            {
                var lineItemDto = request.LineItems.FirstOrDefault(li => li.Id == proposer.LineItemId);
                if (lineItemDto != null && proposerNamesById.TryGetValue(proposer.NotQuotedProposedByUserId!.Value, out var name))
                {
                    lineItemDto.NotQuotedProposedByName = name;
                }
            }
        }

        // Enrich batch actor display names — ApprovalBatch has no User navigation
        // properties, so names aren't available inside the projection above.
        if (request.ApprovalBatches.Any())
        {
            var batchUserIds = request.ApprovalBatches
                .SelectMany(b => new[] { (Guid?)b.CreatedByUserId, b.UpdatedByUserId })
                .Where(uid => uid.HasValue)
                .Select(uid => uid!.Value)
                .Distinct()
                .ToList();

            var batchUserNamesById = await _context.Users
                .Where(u => batchUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName);

            foreach (var batchDto in request.ApprovalBatches)
            {
                if (batchUserNamesById.TryGetValue(batchDto.CreatedByUserId, out var createdName))
                    batchDto.CreatedByUserName = createdName;
                if (batchDto.UpdatedByUserId.HasValue && batchUserNamesById.TryGetValue(batchDto.UpdatedByUserId.Value, out var updatedName))
                    batchDto.UpdatedByUserName = updatedName;
            }
        }

        // ── Cancelled-batch reuse annotations (Option C) — server-resolved eligibility so the
        // approver wizard hides blocked candidates and the buyer sees reuse state/provenance.
        if (request.Quotations != null && request.Quotations.Any(q => q.Items != null && q.Items.Count > 0))
        {
            var reuseEligibility = await _quotationEligibility.GetEligibilityMapAsync(id);
            foreach (var qItemDto in request.Quotations.SelectMany(q => q.Items))
            {
                if (reuseEligibility.TryGetValue(qItemDto.Id, out var elig))
                {
                    qItemDto.IsReuseBlocked = elig.IsReuseBlocked;
                    qItemDto.IsReuseAuthorized = elig.IsReuseAuthorized;
                    qItemDto.SourceCancelledBatchId = elig.SourceCancelledBatchId;
                    qItemDto.SourceCancelledBatchNumber = elig.SourceCancelledBatchNumber;
                    qItemDto.ReuseAuthorizationId = elig.ReuseAuthorizationId;
                    qItemDto.ReuseConsumedFromBatchId = elig.ReuseConsumedByBatchId;
                }
            }
        }

        // Compute DisplayWorkflowState (read-only, not persisted)
        request.DisplayWorkflowState = await _statusSyncService.ComputeDisplayWorkflowStateAsync(id);

        // Enrich with Selected Quotation data if applicable to avoid redundant subqueries in the projection
        if (request.SelectedQuotationId.HasValue)
        {
            var selectedQ = request.Quotations.FirstOrDefault(q => q.Id == request.SelectedQuotationId.Value);
            if (selectedQ != null)
            {
                request.SupplierId = selectedQ.SupplierId;
                request.SupplierName = selectedQ.SupplierNameSnapshot;
                request.EstimatedTotalAmount = selectedQ.TotalAmount;
                request.CurrencyCode = selectedQ.Currency;
                request.SupplierPortalCode = null; // Direct from snapshot
            }
        }

        // ── DEC: Purchase History Insight for Quotations (Step 3) ──
        if (request.Quotations != null && request.Quotations.Any())
        {
            var currentSupplierIds = request.Quotations.Where(q => q.SupplierId.HasValue).Select(q => q.SupplierId!.Value).Distinct().ToList();
            if (currentSupplierIds.Any())
            {
                var validStatusCodes = new[] { "PO_ISSUED", "PAYMENT_SCHEDULED", "PAID", "PAYMENT_COMPLETED", "COMPLETED" };
                
                var allQItems = request.Quotations.SelectMany(q => q.Items).ToList();
                var catalogIds = allQItems.Where(qi => qi.ItemCatalogId.HasValue).Select(qi => qi.ItemCatalogId!.Value).Distinct().ToList();
                var descriptions = allQItems.Where(qi => !qi.ItemCatalogId.HasValue && !string.IsNullOrWhiteSpace(qi.Description))
                                            .Select(qi => qi.Description.Trim().ToLower()).Distinct().ToList();

                if (catalogIds.Any() || descriptions.Any())
                {
                    // Query historical QuotationItems that were part of a selected quotation in a completed request
                    var historyQuery = _context.QuotationItems
                        .Include(qi => qi.Unit)
                        .Include(qi => qi.Quotation)
                        .Include(qi => qi.Quotation.Request)
                        .Include(qi => qi.Quotation.Request.Status)
                        .Where(qi => 
                            qi.Quotation.SupplierId.HasValue && currentSupplierIds.Contains(qi.Quotation.SupplierId.Value) &&
                            qi.Quotation.Request.Status != null && validStatusCodes.Contains(qi.Quotation.Request.Status.Code) &&
                            qi.Quotation.Request.SelectedQuotationId == qi.QuotationId &&
                            qi.Quotation.Request.Id != request.Id
                        );

                    var historyItems = await historyQuery.ToListAsync();

                    // Map by SupplierId
                    var historyBySupplier = historyItems
                        .Where(hi => 
                            (hi.ItemCatalogId.HasValue && catalogIds.Contains(hi.ItemCatalogId.Value)) ||
                            (!hi.ItemCatalogId.HasValue && !string.IsNullOrWhiteSpace(hi.Description) && descriptions.Contains(hi.Description.Trim().ToLower()))
                        )
                        .GroupBy(hi => hi.Quotation.SupplierId!.Value)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    foreach (var q in request.Quotations)
                    {
                        if (!q.SupplierId.HasValue || !historyBySupplier.ContainsKey(q.SupplierId.Value)) continue;
                        
                        var supplierHistory = historyBySupplier[q.SupplierId.Value];

                        foreach (var qi in q.Items)
                        {
                            var matches = supplierHistory.Where(hi => 
                                (qi.ItemCatalogId.HasValue && hi.ItemCatalogId == qi.ItemCatalogId) ||
                                (!qi.ItemCatalogId.HasValue && !hi.ItemCatalogId.HasValue && hi.Description?.Trim().ToLower() == qi.Description?.Trim().ToLower())
                            ).ToList();

                            if (matches.Any())
                            {
                                var bestMatch = matches.OrderByDescending(hi => 
                                    hi.Quotation.Request.ApprovedAtUtc ?? hi.Quotation.Request.ActualPaidAtUtc ?? hi.Quotation.Request.UpdatedAtUtc ?? hi.Quotation.Request.CreatedAtUtc
                                ).First();

                                var insight = new PurchaseHistoryInsightDto
                                {
                                    HasHistory = true,
                                    LastPurchaseDateUtc = bestMatch.Quotation.Request.ApprovedAtUtc ?? bestMatch.Quotation.Request.ActualPaidAtUtc ?? bestMatch.Quotation.Request.UpdatedAtUtc ?? bestMatch.Quotation.Request.CreatedAtUtc,
                                    LastUnitPrice = bestMatch.UnitPrice,
                                    LastCurrency = bestMatch.Quotation.Currency,
                                    LastUom = bestMatch.Unit?.Code,
                                    CurrentUnitPrice = qi.UnitPrice
                                };

                                if (q.Currency != insight.LastCurrency)
                                {
                                    insight.Status = "DIFFERENT_CURRENCY";
                                }
                                else if (qi.UnitCode != insight.LastUom)
                                {
                                    insight.Status = "DIFFERENT_UOM";
                                }
                                else
                                {
                                    if (bestMatch.UnitPrice > 0)
                                    {
                                        insight.DifferencePercent = Math.Round(((qi.UnitPrice - bestMatch.UnitPrice) / bestMatch.UnitPrice) * 100, 1);
                                    }
                                    else
                                    {
                                        insight.DifferencePercent = 0;
                                    }

                                    if (qi.UnitPrice < bestMatch.UnitPrice) insight.Status = "LOWER_THAN_LAST";
                                    else if (qi.UnitPrice > bestMatch.UnitPrice) insight.Status = "HIGHER_THAN_LAST";
                                    else insight.Status = "SAME_AS_LAST";
                                }

                                qi.HistoryInsight = insight;
                            }
                        }
                    }
                }
            }
        }

        // Fetch field changes for this request and associate with status history entries
        var fieldChanges = await _context.RequestFieldChangeHistories
            .AsNoTracking()
            .Where(fc => fc.RequestId == id)
            .OrderByDescending(fc => fc.CreatedAtUtc)
            .Select(fc => new RequestFieldChangeHistoryDto
            {
                Id = fc.Id,
                FieldName = fc.FieldName,
                FieldDisplayName = fc.FieldDisplayName,
                PreviousValue = fc.PreviousValue,
                NewValue = fc.NewValue,
                StatusCodeAtChange = fc.StatusCodeAtChange,
                LineItemId = fc.LineItemId,
                CreatedAtUtc = fc.CreatedAtUtc,
                ActorName = fc.ActorUser.FullName
            })
            .ToListAsync();

        foreach (var sh in request.StatusHistory)
        {
            sh.FieldChanges = fieldChanges
                .Where(fc => fc.CreatedAtUtc >= sh.CreatedAtUtc.AddSeconds(-2) && fc.CreatedAtUtc <= sh.CreatedAtUtc.AddSeconds(2))
                .ToList();
        }

        return Ok(request);
    }

    [HttpGet("{id:guid}/template")]
    public async Task<ActionResult<CreateRequestDraftDto>> GetRequestTemplate(Guid id)
    {
        var request = await _context.Requests
            .AsNoTracking()
            .Include(r => r.LineItems.Where(li => !li.IsDeleted))
                .ThenInclude(li => li.Unit)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        // Strict Strip: Return only business data for a new request draft
        var template = new CreateRequestDraftDto
        {
            Title = request.Title, // Frontend composes copy title: Cópia {SourceRequestNumber} {OriginalTitle}
            Description = request.Description,
            RequestTypeId = request.RequestTypeId,
            NeedLevelId = request.NeedLevelId,
            CurrencyId = null, // Not copied: quotation handled by buyer, payment handled in item step
            EstimatedTotalAmount = 0, // Reset: no items = no total
            DepartmentId = request.DepartmentId,
            CompanyId = request.CompanyId,
            PlantId = request.PlantId,
            CapexOpexClassificationId = null, // Not copied: downstream process data
            NeedByDateUtc = null, // Must be explicitly re-entered for the new request
            
            // Participants SHOULD remain copied as they represent the same business structure.
            // (Phase B: AreaApproverId is not copied — it is decided-by audit, resolved
            // via DepartmentManagers on the new request.)
            BuyerId = request.BuyerId,
            FinalApproverId = request.FinalApproverId,

            LineItems = new List<RequestLineItemDto>(), // Only header is copied at creation stage
            SourceRequestNumber = request.RequestNumber
        };

        return Ok(template);
    }

    [HttpGet("{id:guid}/timeline")]
    public async Task<ActionResult<RequestTimelineDto>> GetRequestTimeline(Guid id)
    {
        var request = await _context.Requests
            .AsNoTracking()
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.LineItems)
            .Include(r => r.StatusHistories)
                .ThenInclude(sh => sh.NewStatus)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        var typeCode = request.RequestType!.Code;
        var currentStatusCode = request.Status!.Code;
        var history = request.StatusHistories.OrderBy(sh => sh.CreatedAtUtc).ToList();

        var stages = typeCode == "QUOTATION"
            ? GetQuotationStages()
            : GetPaymentStages();

        var terminalStates = new[] { "REJECTED", "CANCELLED", "COMPLETED", "QUOTATION_COMPLETED" };
        bool isTerminal = terminalStates.Contains(currentStatusCode);
        bool isRejectionPath = currentStatusCode == "REJECTED" || currentStatusCode == "CANCELLED";

        // A request auto-closed because every item was closed without quotation
        // (Buyer's CLOSED_NOT_QUOTED, or the legacy accepted proposal) jumps
        // straight from the quotation stage to COMPLETED — the approval/PO/
        // payment/receiving stages never happened and must render as skipped,
        // not as implicitly completed.
        var activeLineItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
        bool completedWithoutQuotation =
            currentStatusCode == "COMPLETED" &&
            typeCode == "QUOTATION" &&
            activeLineItems.Count > 0 &&
            activeLineItems.All(li =>
                li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted ||
                li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedAccepted);

        // Identify the last stage that the request actually entered
        int lastStageWithHistoryIndex = -1;
        for (int i = 0; i < stages.Count; i++)
        {
            if (history.Any(h => stages[i].StatusCodes.Contains(h.NewStatus.Code)))
            {
                lastStageWithHistoryIndex = i;
            }
        }

        // Last NON-terminal stage the request actually entered — the boundary
        // between "implicitly passed" (e.g. Rascunho) and "skipped" stages when
        // the request auto-closed without quotation. The terminal stage itself
        // (Concluído) always has history in that flow, so it must be excluded.
        int lastNonTerminalStageWithHistoryIndex = -1;
        for (int i = 0; i < stages.Count - 1; i++)
        {
            if (history.Any(h => stages[i].StatusCodes.Contains(h.NewStatus.Code)))
            {
                lastNonTerminalStageWithHistoryIndex = i;
            }
        }

        var result = new RequestTimelineDto();

        for (int i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            var step = new TimelineStepDto
            {
                Label = stage.Label,
                State = "pending"
            };

            bool isInStage = stage.StatusCodes.Contains(currentStatusCode);
            var historyForStage = history.Where(h => stage.StatusCodes.Contains(h.NewStatus.Code)).ToList();
            var lastEntry = historyForStage.LastOrDefault();
            int currentStageIndex = stages.FindIndex(s => s.StatusCodes.Contains(currentStatusCode));

            if (isInStage)
            {
                step.State = "current";
                step.CompletedAt = lastEntry?.CreatedAtUtc ?? request.CreatedAtUtc;
            }
            else if (lastEntry != null)
            {
                // If we have history for this stage, it's either completed or the point where it was blocked
                if (isRejectionPath && i == lastStageWithHistoryIndex)
                {
                    step.State = "blocked";
                }
                else
                {
                    step.State = "completed";
                    step.CompletedAt = lastEntry.CreatedAtUtc;
                }
            }
            else
            {
                // No history for this stage. 
                if (isRejectionPath)
                {
                    // In a rejection flow, if we haven't reached this stage, it's blocked.
                    if (i > lastStageWithHistoryIndex)
                        step.State = "blocked";
                    else
                        step.State = "pending";
                }
                else if (isTerminal && !isRejectionPath)
                {
                    // For successful terminal states (COMPLETED), stages BEFORE the current stage
                    // should be considered completed even if they lack explicit history.
                    if (currentStageIndex != -1 && i < currentStageIndex)
                    {
                        if (completedWithoutQuotation && i > lastNonTerminalStageWithHistoryIndex)
                        {
                            // Closed without quotation: stages after the last stage the
                            // request actually reached (Cotação) were never executed.
                            step.State = "skipped";
                        }
                        else
                        {
                            step.State = "completed";
                            // Use a fallback date if no history exists (e.g. request creation or next available history)
                            step.CompletedAt = history.FirstOrDefault(h => h.CreatedAtUtc >= request.CreatedAtUtc)?.CreatedAtUtc ?? request.CreatedAtUtc;
                        }
                    }
                    else
                    {
                        step.State = "pending";
                    }
                }
                else
                {
                    step.State = "pending";
                }
            }

            result.Steps.Add(step);
        }

        // Post-process: Remove "Agendamento" step if the request completely bypassed it
        // (i.e. Finance paid directly without ever selecting a scheduling date).
        bool passedThroughScheduling = history.Any(h => h.NewStatus.Code == "PAYMENT_SCHEDULED") || currentStatusCode == "PAYMENT_SCHEDULED";
        bool isPastSchedulingStage = new[] { "PAYMENT_COMPLETED", "WAITING_RECEIPT", "IN_FOLLOWUP", "COMPLETED", "QUOTATION_COMPLETED" }.Contains(currentStatusCode);

        if (isPastSchedulingStage && !passedThroughScheduling)
        {
            var agendamentoStep = result.Steps.FirstOrDefault(s => s.Label == "Agendamento");
            if (agendamentoStep != null)
            {
                result.Steps.Remove(agendamentoStep);
            }
        }

        return Ok(result);
    }
    [HttpPost]
    public async Task<ActionResult<CreateRequestDraftResponseDto>> CreateRequest([FromBody] CreateRequestDraftDto dto)
    {
        // 1. Resolve Current Actor
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        // 1.1. Validate Plant Scope (Primary Authorization Check)
        // Rule: User must have the target plant assigned in UserPlantScopes.
        if (dto.PlantId.HasValue)
        {
            var isAuthorizedPlant = await _context.UserPlantScopes
                .AnyAsync(ups => ups.UserId == actorId && ups.PlantId == dto.PlantId.Value);
            
            if (!isAuthorizedPlant)
            {
                return StatusCode(403, new ProblemDetails 
                { 
                    Title = "Acesso Proibido", 
                    Detail = "A planta selecionada está fora do seu âmbito de acesso autorizado para criação de pedidos.", 
                    Status = 403 
                });
            }

            // 1.2. Consistency check for CompanyId
            var plant = await _context.Plants.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PlantId.Value);
            if (plant != null && dto.CompanyId != plant.CompanyId)
            {
                return BadRequest(new ProblemDetails 
                { 
                    Title = "Erro de Consistência", 
                    Detail = "A Empresa selecionada não corresponde à Planta informada.", 
                    Status = 400 
                });
            }
        }
        else
        {
             return BadRequest(new ProblemDetails 
             { 
                 Title = "Erro de Validação", 
                 Detail = "A Planta é obrigatória para a criação de um pedido.", 
                 Status = 400 
             });
        }
        // 1.3. Validate Department Scope
        if (dto.DepartmentId.HasValue)
        {
            var isAuthorizedDepartment = await _context.UserDepartmentScopes
                .AnyAsync(uds => uds.UserId == actorId && uds.DepartmentId == dto.DepartmentId.Value);
            
            if (!isAuthorizedDepartment)
            {
                return StatusCode(403, new ProblemDetails 
                { 
                    Title = "Acesso Proibido", 
                    Detail = "O departamento selecionado está fora do seu âmbito de acesso autorizado para criação de pedidos.", 
                    Status = 403 
                });
            }
        }

        // 2. Resolve Request Type and Statuses
        var requestTypeEntity = await _context.RequestTypes.FirstOrDefaultAsync(rt => rt.Id == dto.RequestTypeId);
        if (requestTypeEntity == null) return BadRequest("Tipo de pedido inválido.");

        if (requestTypeEntity.Code == "QUOTATION" && !dto.NeedByDateUtc.HasValue)
        {
            return BadRequest(new ProblemDetails 
            { 
                Title = "Erro de Validação", 
                Detail = "A Data de Necessidade é obrigatória para pedidos de Cotação.",
                Status = 400
            });
        }

        // 2.1. Need-level minimum lead time (Quotation only).
        // The "Necessário até" date may not precede the minimum implied by the Grau de Necessidade.
        // Payment requests are exempt: there the same field carries the supplier invoice due date,
        // which is legitimately allowed to be in the past.
        if (requestTypeEntity.Code == RequestConstants.Types.Quotation && dto.NeedByDateUtc.HasValue && dto.NeedLevelId.HasValue)
        {
            var needLevel = await _context.NeedLevels.AsNoTracking()
                .FirstOrDefaultAsync(nl => nl.Id == dto.NeedLevelId!.Value);

            var minNeedByDate = RequestConstants.NeedLevels.GetMinNeedByDate(needLevel?.Code, DateTime.UtcNow);
            if (minNeedByDate.HasValue && dto.NeedByDateUtc.Value.Date < minNeedByDate.Value)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Erro de Validação",
                    Detail = $"A data “Necessário até” não pode ser anterior ao prazo mínimo do grau {needLevel!.Name}. Data mínima: {minNeedByDate.Value:dd/MM/yyyy}.",
                    Status = 400
                });
            }
        }

        var initialStatusCode = requestTypeEntity.Code == "QUOTATION" ? "WAITING_QUOTATION" : "DRAFT";
        var initialStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == initialStatusCode);
        if (initialStatus == null) return StatusCode(500, $"{initialStatusCode} status code not found in database lookup.");

        // Phase 2 — Mandatory items (QUOTATION): a Quotation is created already-submitted
        // (WAITING_QUOTATION with SubmittedAtUtc set), so CreateRequest is the authoritative gate.
        // (PAYMENT is created as a DRAFT and is validated at Submit instead — do not block it here.)
        if (requestTypeEntity.Code == RequestConstants.Types.Quotation)
        {
            // Resolve active units once (code → id + valid-id set); no per-item queries.
            var activeUnits = await _context.Units.AsNoTracking()
                .Where(u => u.IsActive).Select(u => new { u.Id, u.Code }).ToListAsync();
            var validUnitIds = activeUnits.Select(u => u.Id).ToHashSet();
            var codeToId = activeUnits
                .GroupBy(u => u.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            var candidates = (dto.LineItems ?? new List<RequestLineItemDto>())
                .Select((li, idx) => new LineItemCandidate
                {
                    Index = idx,
                    Description = li.Description,
                    Quantity = li.Quantity,
                    UnitId = (!string.IsNullOrWhiteSpace(li.Unit) && codeToId.TryGetValue(li.Unit, out var uid)) ? uid : (int?)null
                })
                .ToList();

            var quotationValidation = _lineItemValidator.ValidateQuotation(candidates, validUnitIds);
            if (!quotationValidation.IsValid)
            {
                var problem = new ProblemDetails { Title = "Erro de Validação", Detail = quotationValidation.Summary, Status = 400 };
                problem.Extensions["lineItemErrors"] = quotationValidation.Errors;
                return BadRequest(problem);
            }
        }




        // 3. Generate Request Number using Persistent Global Counter
        // The counter is monotonic and never reused, even if requests are deleted.
        var today = DateTime.UtcNow.Date;
        var counterKey = "GLOBAL_REQUEST_COUNTER";
        var dateStr = today.ToString("dd/MM/yyyy");
        
        var counter = await _context.SystemCounters.FirstOrDefaultAsync(sc => sc.Id == counterKey);
        int seqNumber;

        if (counter == null)
        {
            // First time initialization
            seqNumber = 1;
            counter = new SystemCounter
            {
                Id = counterKey,
                CurrentValue = seqNumber,
                LastUpdatedUtc = DateTime.UtcNow
            };
            _context.SystemCounters.Add(counter);
        }
        else
        {
            counter.CurrentValue++;
            counter.LastUpdatedUtc = DateTime.UtcNow;
            seqNumber = counter.CurrentValue;
        }

        // Commit the counter increment FIRST (separate from request insert)
        await _context.SaveChangesAsync();

        var requestNumber = $"REQ-{dateStr}-{seqNumber:D3}";

        // 4. Construct the Request Entity
        var department = await _context.Departments.FirstOrDefaultAsync(d => d.Id == dto.DepartmentId!.Value);
        var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == dto.CompanyId!.Value);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = requestNumber,
            Title = dto.Title,
            Description = dto.Description,
            RequestTypeId = dto.RequestTypeId!.Value,
            NeedLevelId = dto.NeedLevelId,
            CurrencyId = dto.CurrencyId,
            EstimatedTotalAmount = dto.EstimatedTotalAmount,
            DiscountAmount = dto.DiscountAmount,
            DepartmentId = dto.DepartmentId!.Value,
            CompanyId = dto.CompanyId!.Value,
            PlantId = dto.PlantId,
            CapexOpexClassificationId = dto.CapexOpexClassificationId,
            NeedByDateUtc = dto.NeedByDateUtc,
            
            SupplierId = dto.SupplierId,
            BuyerId = dto.BuyerId, // Let it be null
            // Phase B: AreaApproverId is no longer nominated — it stays null until an
            // area manager actually decides (routing via DepartmentManagers at submit).
            AreaApproverId = null,
            FinalApproverId = company?.FinalApproverUserId, // Auto-resolved (final approval unchanged)
            
            StatusId = initialStatus.Id,
            RequesterId = actorId,
            CreatedByUserId = actorId,
            CreatedAtUtc = DateTime.UtcNow,
            SubmittedAtUtc = requestTypeEntity.Code == "QUOTATION" ? DateTime.UtcNow : null,
            IsCancelled = false
        };

        // 4.1. Bulk add Line Items if provided (Copy flow)
        if (dto.LineItems != null && dto.LineItems.Any())
        {
            var units = await _context.Units.AsNoTracking().ToListAsync();
            var allIvaRates = await _context.IvaRates.AsNoTracking().ToListAsync();
            var lineItems = new List<RequestLineItem>();
            decimal totalAmount = 0;

            int currentLine = 1;
            foreach (var itemDto in dto.LineItems)
            {
                var unit = units.FirstOrDefault(u => u.Code == itemDto.Unit);
                var netAmount = Round2((itemDto.Quantity * itemDto.UnitPrice) - (itemDto.DiscountAmount ?? 0));
                var ivaEntity = itemDto.IvaRateId.HasValue ? allIvaRates.FirstOrDefault(r => r.Id == itemDto.IvaRateId.Value) : null;
                var ivaAmount = ivaEntity != null ? Round2(netAmount * (ivaEntity.RatePercent / 100m)) : 0m;
                var item = new RequestLineItem
                {
                    Id = Guid.NewGuid(),
                    RequestId = request.Id,
                    LineNumber = itemDto.LineNumber > 0 ? itemDto.LineNumber : currentLine++,
                    ItemPriority = itemDto.ItemPriority,
                    Description = itemDto.Description,
                    Quantity = itemDto.Quantity,
                    UnitId = unit?.Id,
                    UnitPrice = itemDto.UnitPrice,
                    DiscountPercent = itemDto.DiscountPercent,
                    DiscountAmount = itemDto.DiscountAmount,
                    TotalAmount = netAmount + ivaAmount,
                    Notes = itemDto.Notes,
                    PlantId = itemDto.PlantId ?? request.PlantId,
                    CostCenterId = itemDto.CostCenterId,
                    IvaRateId = itemDto.IvaRateId,
                    CurrencyId = itemDto.CurrencyId ?? request.CurrencyId,
                    ItemCatalogId = itemDto.ItemCatalogId,
                    LineItemStatusId = null, // Initial state
                    IsDeleted = false,
                    CreatedAtUtc = DateTime.UtcNow
                };
                lineItems.Add(item);
                totalAmount += item.TotalAmount;
            }
            request.LineItems = lineItems;
            
            // For Payment requests (DRAFT with OCR), the frontend sends an
            // IVA+discount-inclusive total. Preserve it instead of overwriting.
            if (requestTypeEntity.Code != "PAYMENT")
            {
                request.EstimatedTotalAmount = totalAmount;
            }
        }

        _context.Requests.Add(request);

        // 4. Construct the audit trail entry
        var actionTaken = requestTypeEntity.Code == "QUOTATION" ? "SUBMIT" : "CREATED";
        var historyComment = requestTypeEntity.Code == "QUOTATION" 
            ? "Request created and submitted for quotation." 
            : "Request created as Draft.";

        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actorId,
            ActionTaken = actionTaken,
            PreviousStatusId = null,
            NewStatusId = initialStatus.Id,
            Comment = historyComment,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.RequestStatusHistories.Add(history);

        // 5. Persist transaction
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new { 
                Message = "An error occurred while saving the entity changes.", 
                Details = ex.InnerException?.Message ?? ex.Message 
            });
        }

        // 5.1 Workflow Notification Emission for Quotation (fire-and-forget, non-blocking)
        // Quotation requests skip DRAFT and are created directly in WAITING_QUOTATION,
        // so they never pass through SubmitRequest → ApplyStatusChangeAndSyncItemsAsync.
        // We must emit notifications here to replicate the dual-event pattern.
        if (requestTypeEntity.Code == "QUOTATION")
        {
            try
            {
                var actor = await _context.Users.FindAsync(actorId);
                var baseEvent = new WorkflowEvent
                {
                    EventCode = WorkflowEventCodes.QuotationAwaitingBuyer,
                    RequestId = request.Id,
                    RequestNumber = request.RequestNumber ?? "S/N",
                    RequestTitle = request.Title ?? "",
                    TargetStatusCode = initialStatusCode,
                    ActionTaken = actionTaken,
                    ActorUserId = actorId,
                    ActorName = actor?.FullName ?? "Sistema",
                    Comment = historyComment,
                    CorrelationId = history.Id,
                    RequesterId = request.RequesterId,
                    BuyerId = request.BuyerId,
                    AreaApproverId = request.AreaApproverId,
                    FinalApproverId = request.FinalApproverId,
                    DepartmentId = request.DepartmentId,
                    PlantId = request.PlantId
                };

                // Primary: notify plant-scoped buyers
                await _orchestrator.EmitAsync(baseEvent);

                // Secondary: submission confirmation to requester
                var confirmationEvent = new WorkflowEvent
                {
                    EventCode = WorkflowEventCodes.SubmissionConfirmed,
                    RequestId = request.Id,
                    RequestNumber = request.RequestNumber ?? "S/N",
                    RequestTitle = request.Title ?? "",
                    TargetStatusCode = initialStatusCode,
                    ActionTaken = actionTaken,
                    ActorUserId = actorId,
                    ActorName = actor?.FullName ?? "Sistema",
                    Comment = historyComment,
                    CorrelationId = Guid.NewGuid(),
                    RequesterId = request.RequesterId,
                    BuyerId = request.BuyerId,
                    AreaApproverId = request.AreaApproverId,
                    FinalApproverId = request.FinalApproverId,
                    PlantId = request.PlantId
                };
                await _orchestrator.EmitAsync(confirmationEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-critical: workflow notification emission failed for Quotation Request {RequestId} during creation", request.Id);
            }
        }
        // 6. Project response
        var responseDto = new CreateRequestDraftResponseDto
        {
            Id = request.Id,
            Title = request.Title,
            StatusCode = initialStatus.Code,
            CreatedAtUtc = request.CreatedAtUtc
        };

        return CreatedAtAction(nameof(GetRequest), new { id = request.Id }, responseDto);
    }

    [HttpPut("{id}/draft")]
    public async Task<IActionResult> UpdateRequestDraft(Guid id, [FromBody] UpdateRequestDraftDto dto)
    {
        // 1. Resolve Current Actor
        var actorId = CurrentUserId;

        // 1.1. Validate Plant Scope (Primary Authorization Check)
        // Rule: User must have the target plant assigned in UserPlantScopes.
        if (dto.PlantId.HasValue)
        {
            var isAuthorizedPlant = await _context.UserPlantScopes
                .AnyAsync(ups => ups.UserId == actorId && ups.PlantId == dto.PlantId.Value);
            
            if (!isAuthorizedPlant)
            {
                return StatusCode(403, new ProblemDetails 
                { 
                    Title = "Acesso Proibido", 
                    Detail = "A planta selecionada está fora do seu âmbito de acesso autorizado para alteração de pedidos.", 
                    Status = 403 
                });
            }

            // 1.2. Consistency check for CompanyId
            var plant = await _context.Plants.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PlantId.Value);
            if (plant != null && dto.CompanyId != plant.CompanyId)
            {
                return BadRequest(new ProblemDetails 
                { 
                    Title = "Erro de Consistência", 
                    Detail = "A Empresa selecionada não corresponde à Planta informada.", 
                    Status = 400 
                });
            }
        }

        // 1.3. Validate Department Scope
        if (dto.DepartmentId > 0)
        {
            var isAuthorizedDepartment = await _context.UserDepartmentScopes
                .AnyAsync(uds => uds.UserId == actorId && uds.DepartmentId == dto.DepartmentId);
            
            if (!isAuthorizedDepartment)
            {
                return StatusCode(403, new ProblemDetails 
                { 
                    Title = "Acesso Proibido", 
                    Detail = "O departamento selecionado está fora do seu âmbito de acesso autorizado para alteração de pedidos.", 
                    Status = 403 
                });
            }
        }

        // 2. Fetch tracking entity
        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .Include(r => r.Quotations)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null)
        {
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });
        }

        // 3. Status Rule: Only DRAFT, Adjustment or WAITING_QUOTATION statuses can be edited
        var statusCode = request.Status!.Code;
        if (statusCode != "DRAFT" && statusCode != "AREA_ADJUSTMENT" && statusCode != "FINAL_ADJUSTMENT" && statusCode != "WAITING_QUOTATION")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Este pedido não está em rascunho nem em fase de reajuste/cotação, por isso não pode ser alterado.", 
                Status = 409 
            });
        }

        // 3.1. Creator-only edit enforcement for non-DRAFT statuses
        // After a request enters the workflow, only the original creator may edit request data.
        // Buyer-specific actions (quotation management, assign, workflow) use separate endpoints.
        if (statusCode != "DRAFT" && request.RequesterId != actorId)
        {
            return StatusCode(403, new ProblemDetails 
            { 
                Title = "Acesso Proibido", 
                Detail = "Apenas o criador do pedido pode editar os dados do pedido nesta fase. Para solicitações de alteração, utilize os comentários ou o fluxo de comunicação.", 
                Status = 403 
            });
        }

        var requestTypeEntity = await _context.RequestTypes.FirstOrDefaultAsync(rt => rt.Id == dto.RequestTypeId);
        if (requestTypeEntity == null) return BadRequest("Tipo de pedido inválido.");

        if (requestTypeEntity.Code == "QUOTATION" && !dto.NeedByDateUtc.HasValue)
        {
            return BadRequest(new ProblemDetails 
            { 
                Title = "Erro de Validação", 
                Detail = "A Data de Necessidade é obrigatória para pedidos de Cotação.", 
                Status = 400 
            });
        }



        // 4. Update Header Fields and Track Changes
        bool isQuotationStage = statusCode == "WAITING_QUOTATION";
        bool hasQuotations = request.Quotations.Any();
        var changedFields = new List<string>();
        bool changed = false;

        // --- Field-level audit helper (Phase 3: Full traceability) ---
        // Records old → new value for every changed field into RequestFieldChangeHistory.
        var fieldChanges = new List<RequestFieldChangeHistory>();
        void TrackField(string fieldName, string displayName, string? oldVal, string? newVal, Guid? lineItemId = null)
        {
            if (oldVal != newVal)
            {
                fieldChanges.Add(new RequestFieldChangeHistory
                {
                    RequestId = request.Id,
                    ActorUserId = actorId,
                    FieldName = fieldName,
                    FieldDisplayName = displayName,
                    PreviousValue = oldVal,
                    NewValue = newVal,
                    StatusCodeAtChange = statusCode,
                    LineItemId = lineItemId,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        // --- Safe Fields (Always editable in allowed statuses) ---
        var newTitle = dto.Title?.Trim() ?? "";
        if ((request.Title?.Trim() ?? "") != newTitle)
        {
            TrackField("Title", "Título", request.Title?.Trim(), newTitle);
            request.Title = newTitle; changedFields.Add("Título"); changed = true;
        }
        
        var newDescription = dto.Description?.Trim() ?? "";
        if ((request.Description?.Trim() ?? "") != newDescription)
        {
            TrackField("Description", "Descrição", request.Description?.Trim(), newDescription);
            request.Description = newDescription; changedFields.Add("Descrição"); changed = true;
        }
        if (request.NeedLevelId != dto.NeedLevelId)
        {
            TrackField("NeedLevelId", "Urgência", request.NeedLevelId?.ToString(), dto.NeedLevelId?.ToString());
            request.NeedLevelId = dto.NeedLevelId; changedFields.Add("Urgência"); changed = true;
        }
        if (request.DepartmentId != dto.DepartmentId)
        {
            TrackField("DepartmentId", "Departamento", request.DepartmentId.ToString(), dto.DepartmentId.ToString());
            request.DepartmentId = dto.DepartmentId; changedFields.Add("Departamento"); changed = true;
        }
        if (request.NeedByDateUtc != dto.NeedByDateUtc)
        {
            TrackField("NeedByDateUtc", "Data Necessidade", request.NeedByDateUtc?.ToString("o"), dto.NeedByDateUtc?.ToString("o"));
            request.NeedByDateUtc = dto.NeedByDateUtc;
            changedFields.Add("Data Necessidade");
            changed = true;

            // Server-side due-date propagation: synchronize to all active line items
            // within the same SaveChangesAsync() transaction for atomicity.
            // This replaces the previous frontend-side Promise.all(updateLineItem) pattern
            // which caused the IVA partial save bug (header committed, item updates could fail).
            foreach (var lineItem in request.LineItems.Where(l => !l.IsDeleted))
            {
                lineItem.DueDate = dto.NeedByDateUtc;
            }
        }
        if (request.CapexOpexClassificationId != dto.CapexOpexClassificationId)
        {
            TrackField("CapexOpexClassificationId", "Classificação", request.CapexOpexClassificationId?.ToString(), dto.CapexOpexClassificationId?.ToString());
            request.CapexOpexClassificationId = dto.CapexOpexClassificationId; changedFields.Add("Classificação"); changed = true;
        }
        if (request.EstimatedTotalAmount != dto.EstimatedTotalAmount)
        {
            TrackField("EstimatedTotalAmount", "Valor Bruto Estimado", request.EstimatedTotalAmount.ToString("F2"), dto.EstimatedTotalAmount.ToString("F2"));
            request.EstimatedTotalAmount = dto.EstimatedTotalAmount; changedFields.Add("Valor Bruto Estimado"); changed = true;
        }
        if (request.DiscountAmount != dto.DiscountAmount)
        {
            TrackField("DiscountAmount", "Desconto Global", request.DiscountAmount.ToString("F2"), dto.DiscountAmount.ToString("F2"));
            request.DiscountAmount = dto.DiscountAmount; changedFields.Add("Desconto Global"); changed = true;
        }

        // --- Restricted Fields in Quotation Stage ---
        if (isQuotationStage)
        {
            // Block structural workflow changes.
            // (Phase B: AreaApproverId is decided-by audit, not a form participant —
            // removed from this comparison; the DTO no longer carries it.)
            if (request.RequestTypeId != dto.RequestTypeId ||
                request.BuyerId != dto.BuyerId ||
                request.FinalApproverId != dto.FinalApproverId ||
                request.PlantId != dto.PlantId ||
                request.CompanyId != dto.CompanyId)
            {
                return BadRequest(new ProblemDetails 
                { 
                    Title = "Ação Bloqueada", 
                    Detail = "Não é possível alterar o tipo, planta, empresa ou participantes do fluxo enquanto o pedido está em cotação.", 
                    Status = 400 
                });
            }
        }
        else
        {
            // Normal Draft/Adjustment rules
            if (request.RequestTypeId != dto.RequestTypeId) { request.RequestTypeId = dto.RequestTypeId; changedFields.Add("Tipo de Pedido"); changed = true; }
            if (request.BuyerId != dto.BuyerId) { request.BuyerId = dto.BuyerId; changedFields.Add("Comprador"); changed = true; }
            
            // Phase B: changing the department no longer nominates an AreaApprover —
            // routing is resolved from DepartmentManagers at submit/decision time.

            // Auto-resolve FinalApprover if Company changed
            if (request.CompanyId != dto.CompanyId)
            {
                var newComp = await _context.Companies.FirstOrDefaultAsync(c => c.Id == dto.CompanyId);
                request.FinalApproverId = newComp?.FinalApproverUserId;
            }
            if (request.PlantId != dto.PlantId) { request.PlantId = dto.PlantId; changedFields.Add("Planta"); changed = true; }
            
            if (request.CompanyId != dto.CompanyId)
            {
                if (request.LineItems.Any(l => !l.IsDeleted))
                {
                    return BadRequest(new ProblemDetails { Title = "Regra de Negócio Violada", Detail = "Não é possível alterar a empresa com itens presentes.", Status = 400 });
                }
                request.CompanyId = dto.CompanyId; changedFields.Add("Empresa"); changed = true;
            }
        }

        // --- Currency Logic ---
        if (request.CurrencyId != dto.CurrencyId)
        {
            if (isQuotationStage || request.LineItems.Any(l => !l.IsDeleted))
            {
                return Conflict(new ProblemDetails 
                { 
                    Title = "Regra de Negócio Violada", 
                    Detail = "Não é possível alterar a moeda de um pedido que já possui itens ou está em cotação.",
                    Status = 409
                });
            }
            request.CurrencyId = dto.CurrencyId;
            changedFields.Add("Moeda");
            changed = true;
        }

        // --- Supplier Logic ---
        if (request.SupplierId != dto.SupplierId)
        {
            if (isQuotationStage && hasQuotations)
            {
                return BadRequest(new ProblemDetails 
                { 
                    Title = "Ação Bloqueada", 
                    Detail = "O fornecedor não pode ser alterado pois já existem cotações salvas.", 
                    Status = 400 
                });
            }

            if (requestTypeEntity.Code == "QUOTATION" && statusCode != "DRAFT" && statusCode != "AREA_ADJUSTMENT" && statusCode != "FINAL_ADJUSTMENT")
            {
                 return BadRequest(new ProblemDetails 
                 { 
                     Title = "Ação Bloqueada", 
                     Detail = "Para pedidos de Cotação, o fornecedor não pode ser alterado manualmente nesta fase.", 
                     Status = 400 
                 });
            }

            request.SupplierId = dto.SupplierId; 
            changedFields.Add("Fornecedor");
            changed = true; 
        }

        if (changed)
        {
            request.UpdatedAtUtc = DateTime.UtcNow;
            request.UpdatedByUserId = actorId;

            var comment = isQuotationStage 
                ? $"Alteração parcial em fase de cotação. Campos modificados: {string.Join(", ", changedFields)}."
                : $"Dados básicos alterados. Campos modificados: {string.Join(", ", changedFields)}.";

            var history = new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = actorId,
                ActionTaken = "DADOS_ALTERADOS",
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = comment,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.RequestStatusHistories.Add(history);

            // Phase 3: Persist field-level audit records within the same transaction
            if (fieldChanges.Any())
            {
                _context.RequestFieldChangeHistories.AddRange(fieldChanges);
            }
        }

        await _context.SaveChangesAsync();

        // Notification: Notify assigned buyer in WAITING_QUOTATION status if changes occurred
        if (changed && statusCode == "WAITING_QUOTATION" && request.BuyerId.HasValue)
        {
            try
            {
                var fieldSummary = string.Join(", ", changedFields);
                await _notificationService.CreateNotificationAsync(
                    request.BuyerId.Value,
                    "Pedido alterado em Aguardando Cotação",
                    $"O pedido {request.RequestNumber} foi atualizado pelo solicitante. Campos alterados: {fieldSummary}.",
                    NotificationTypes.Info,
                    $"/requests/{request.Id}"
                );
            }
            catch (Exception ex)
            {
                // Non-critical failure for the main update flow, but should be logged
                _logger.LogWarning(ex, "Failed to send buyer notification for Request {RequestNumber} update.", request.RequestNumber);
            }
        }

        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> SubmitRequest(Guid id)
    {
        var actorId = CurrentUserId;

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.LineItems)
            .Include(r => r.Attachments)
            .Include(r => r.Department)
            .Include(r => r.Company)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        // 1. Validate Current Status (Allow DRAFT and Rework statuses)
        string currentStatusCode = request.Status!.Code;
        if (currentStatusCode != "DRAFT" && currentStatusCode != "AREA_ADJUSTMENT" && currentStatusCode != "FINAL_ADJUSTMENT")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Apenas pedidos em rascunho ou reajuste podem ser submetidos.",
                Status = 409 
            });
        }

        // 2. Perform Submission Validation
        var errors = new List<string>();
        var lineItemErrors = new List<LineItemValidationError>(); // structured, index-addressable

        // Phase B — area routing via DepartmentManagers (single source of truth).
        // The request is NOT nominated to a single approver anymore: AreaApproverId
        // stays null until a manager actually decides. Submission requires at least
        // one resolvable manager for (department, plant); Department.ResponsibleUserId
        // is no longer consulted.
        var areaRouting = await _approvalRouting.ResolveAreaManagersAsync(request.DepartmentId, request.PlantId);
        if (!areaRouting.HasManagers)
        {
            var deptName = request.Department?.Name ?? request.DepartmentId.ToString();
            var plantName = request.PlantId.HasValue
                ? (await _context.Plants.AsNoTracking().Where(p => p.Id == request.PlantId.Value).Select(p => p.Name).FirstOrDefaultAsync()) ?? request.PlantId.Value.ToString()
                : "—";

            await _adminLog.WriteAsync("Error", "RequestsController", "APPROVAL_ROUTING_NO_MANAGER",
                $"Submissão bloqueada: nenhum responsável de aprovação configurado para o departamento {deptName} (planta {plantName}).",
                payload: $"RequestId: {request.Id}. DepartmentId: {request.DepartmentId}. PlantId: {request.PlantId?.ToString() ?? "null"}. Ator: {actorId}.");

            return BadRequest(new ProblemDetails
            {
                Title = "Erro de Validação",
                Detail = $"Não existe responsável de aprovação configurado para o departamento {deptName} na planta {plantName}. Configure os managers em Dados Mestres → Departamentos.",
                Status = 400
            });
        }
        request.AreaApproverId = null; // decided-by semantics: filled only when a manager acts

        // Final approval unchanged (out of Phase B scope).
        if (request.Company != null && request.Company.FinalApproverUserId.HasValue)
        {
            request.FinalApproverId = request.Company.FinalApproverUserId;
        }

        // Always-required header fields (both request types)
        if (string.IsNullOrWhiteSpace(request.Title))
            errors.Add("O título do pedido é obrigatório.");
        if (string.IsNullOrWhiteSpace(request.Description))
            errors.Add("A descrição do pedido é obrigatória.");
        if (request.DepartmentId == 0)
            errors.Add("O departamento é obrigatório.");

        // BuyerId validation removed (now allows unassigned requests)

        if (request.FinalApproverId == null || request.FinalApproverId == Guid.Empty)
            errors.Add("Não foi possível determinar o Aprovador Final. Verifique se a Empresa correspondente possui um aprovador final definido no cadastro.");

        // QUOTATION: NeedByDateUtc is required at header level
        if (request.RequestType!.Code == "QUOTATION" && request.NeedByDateUtc == null)
            errors.Add("A Data de Necessidade (Necessário Até) é obrigatória para pedidos de Cotação.");

        // Conditional Item Validation
        // Phase 2 — a QUOTATION can only be submitted with at least one VALID item; an attachment no
        // longer substitutes items (closes the /duplicate DRAFT-quotation bypass). Reuses the same
        // validator as CreateRequest so the rule lives in one place.
        if (request.RequestType!.Code == "QUOTATION")
        {
            var activeQuotationItems = request.LineItems.Where(l => !l.IsDeleted).ToList();
            var distinctQUnitIds = activeQuotationItems.Where(l => l.UnitId.HasValue).Select(l => l.UnitId!.Value).Distinct().ToList();
            var validQUnitIds = distinctQUnitIds.Count == 0
                ? new HashSet<int>()
                : (await _context.Units.Where(u => distinctQUnitIds.Contains(u.Id) && u.IsActive)
                        .Select(u => u.Id).ToListAsync()).ToHashSet();

            var qCandidates = activeQuotationItems.Select(l => new LineItemCandidate
            {
                Index = l.LineNumber,
                Description = l.Description,
                Quantity = l.Quantity,
                UnitId = l.UnitId,
                IsDeleted = l.IsDeleted
            }).ToList();

            var quotationSubmitValidation = _lineItemValidator.ValidateQuotation(qCandidates, validQUnitIds);
            if (!quotationSubmitValidation.IsValid)
            {
                errors.Add("Adicione pelo menos um item válido antes de submeter a solicitação de Cotação.");
                lineItemErrors.AddRange(quotationSubmitValidation.Errors);
            }
        }

        if (request.RequestType!.Code == "PAYMENT")
        {
            var activePaymentItems = request.LineItems.Where(l => !l.IsDeleted).ToList();

            // Resolve the set of valid (active) unit ids referenced by the items in ONE query (no N+1).
            var distinctUnitIds = activePaymentItems.Where(l => l.UnitId.HasValue).Select(l => l.UnitId!.Value).Distinct().ToList();
            var validUnitIds = distinctUnitIds.Count == 0
                ? new HashSet<int>()
                : (await _context.Units.Where(u => distinctUnitIds.Contains(u.Id) && u.IsActive)
                        .Select(u => u.Id).ToListAsync()).ToHashSet();

            var candidates = activePaymentItems.Select(l => new LineItemCandidate
            {
                Index = l.LineNumber,
                Description = l.Description,
                Quantity = l.Quantity,
                UnitId = l.UnitId,
                LineTotal = l.TotalAmount, // derived authoritative value (always recomputed by the backend)
                IsDeleted = l.IsDeleted
            }).ToList();

            var paymentValidation = _lineItemValidator.ValidatePaymentSubmit(candidates, validUnitIds);
            foreach (var msg in paymentValidation.Errors.Select(e => e.Message).Distinct())
                errors.Add(msg);
            lineItemErrors.AddRange(paymentValidation.Errors);

            // Request-level sanity (kept in addition to the per-line checks above).
            if (activePaymentItems.Count > 0 && request.EstimatedTotalAmount <= 0)
                errors.Add("O pedido deve possuir valor total maior que zero.");

            // PAYMENT: DueDate, CostCenter and IvaRate are handled at later workflow stages (Area/Final Approver).
            // Supplier is no longer strictly mandatory at submission for PAYMENT requests (DEC-076).
        }

        // Mandatory Document Validation for Submission
        // QUOTATION: Proforma NOT mandatory on initial submission
        // PAYMENT: Proforma IS mandatory
        if (request.RequestType.Code == "PAYMENT" && !await HasAttachmentAsync(id, RequestAttachment.TYPE_PROFORMA))
        {
            errors.Add("É necessário anexar a Proforma antes de submeter o pedido.");
        }

        if (errors.Any())
        {
            var problem = new ProblemDetails
            {
                Title = "Validação de Submissão Falhou",
                Detail = string.Join(" ", errors.Distinct()),
                Status = 400
            };
            if (lineItemErrors.Count > 0)
                problem.Extensions["lineItemErrors"] = lineItemErrors;
            return BadRequest(problem);
        }

        // 3. Resolve Target Status based on Current Stage and Request Type
        bool isQuotation = request.RequestType!.Code == "QUOTATION";
        
        string targetStatusCode = currentStatusCode switch
        {
            "FINAL_ADJUSTMENT" => "WAITING_FINAL_APPROVAL",
            "AREA_ADJUSTMENT" => "WAITING_AREA_APPROVAL",
            _ => isQuotation ? "WAITING_QUOTATION" : "WAITING_AREA_APPROVAL"
        };

        if (request.SubmittedAtUtc == null)
            request.SubmittedAtUtc = DateTime.UtcNow;

        string actionTaken = currentStatusCode == "DRAFT" ? "SUBMIT" : "RESUBMIT";
        string historyComment = currentStatusCode switch
        {
            "FINAL_ADJUSTMENT" => "Pedido reenviado para aprovação final após reajuste.",
            "AREA_ADJUSTMENT" => "Pedido reenviado para aprovação da área após reajuste.",
            _ => isQuotation 
                ? "Pedido submetido pelo solicitante. Aguardando cotação." 
                : "Pedido submetido pelo solicitante. Aguardando aprovação da área."
        };

        string successMessage = currentStatusCode switch
        {
            "FINAL_ADJUSTMENT" => "Pedido reenviado para aprovação final com sucesso.",
            "AREA_ADJUSTMENT" => "Pedido reenviado para aprovação da área com sucesso.",
            _ => isQuotation 
                ? "Pedido enviado para cotação com sucesso." 
                : "Pedido enviado para aprovação da área com sucesso."
        };

        return await ApplyStatusChangeAndSyncItemsAsync(request, targetStatusCode, actionTaken, historyComment, successMessage, actorId);
    }

    [HttpPost("{id:guid}/assign-buyer")]
    public async Task<IActionResult> AssignBuyer(Guid id, [FromQuery] Guid? targetUserId = null)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
            .Include(r => r.Status)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound(new ProblemDetails { Title = "Pedido não encontrado", Status = 404 });

        // Only allow assignment if it's currently unassigned. Re-assignment should be a different flow/check if needed
        // but the prompt says: "If role/permissions already support it, allow reassignment by authorized users only"
        // Let's allow administrators to reassign, or buyers to self-assign if it's unassigned.
        var isSystemAdmin = CurrentUserRoles.Contains(RoleConstants.SystemAdministrator);
        var isLocalManager = CurrentUserRoles.Contains(RoleConstants.LocalManager);
        var canReassign = isSystemAdmin || isLocalManager;
        
        if (request.BuyerId.HasValue && !canReassign && request.BuyerId.Value != actorId)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Ação Bloqueada", 
                Detail = "Este pedido já está atribuído a outro comprador. O reencaminhamento só é permitido por coordenadores.", 
                Status = 409 
            });
        }

        var newBuyerId = targetUserId ?? actorId;
        
        if (request.BuyerId == newBuyerId)
        {
             return Ok(new { Message = "O comprador já está atribuído a este recurso." }); // Idempotent
        }

        request.BuyerId = newBuyerId;
        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;

        // History entry
        var targetUser = newBuyerId == actorId ? user : await _context.Users.FindAsync(newBuyerId);
        var historyComment = newBuyerId == actorId 
            ? "O comprador assumiu a responsabilidade pelo pedido." 
            : $"O pedido foi atribuído ao comprador {targetUser?.FullName}.";

        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actorId,
            ActionTaken = "COMPRADOR_ATRIBUIDO",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = historyComment,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(history);

        await _context.SaveChangesAsync();

        // Notifications (in-app)
        try
        {
            if (request.RequesterId != actorId)
            {
                var msgToRequester = newBuyerId == actorId 
                    ? $"O comprador {user.FullName} assumiu a responsabilidade pelo seu pedido {(request.RequestNumber ?? "S/N")}."
                    : $"O seu pedido {(request.RequestNumber ?? "S/N")} foi atribuído ao comprador {targetUser?.FullName}.";

                await _notificationService.CreateNotificationAsync(
                    request.RequesterId,
                    "Comprador Atribuído",
                    msgToRequester,
                    NotificationTypes.Info,
                    $"/requests/{request.Id}"
                );
            }

            if (newBuyerId != actorId)
            {
                await _notificationService.CreateNotificationAsync(
                    newBuyerId,
                    "Nova Atribuição",
                    $"O pedido {(request.RequestNumber ?? "S/N")} foi-lhe atribuído por {user.FullName}.",
                    NotificationTypes.Info,
                    $"/requests/{request.Id}"
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao enviar notificações in-app de atribuição para o Pedido {RequestNumber}.", request.RequestNumber);
        }

        // Email notification via orchestrator (Task 3 — BUYER_ASSIGNED)
        try
        {
            await _orchestrator.EmitAsync(new WorkflowEvent
            {
                EventCode = WorkflowEventCodes.BuyerAssigned,
                RequestId = request.Id,
                RequestNumber = request.RequestNumber ?? "S/N",
                RequestTitle = request.Title ?? "",
                TargetStatusCode = request.Status!.Code,
                ActionTaken = "BUYER_ASSIGNED",
                ActorUserId = actorId,
                ActorName = user.FullName ?? "Sistema",
                CorrelationId = history.Id,
                RequesterId = request.RequesterId,
                BuyerId = newBuyerId,
                DepartmentId = request.DepartmentId,
                PlantId = request.PlantId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-critical: email notification dispatch failed for BuyerAssigned on Request {RequestId}", request.Id);
        }

        return NoContent();
    }

    [HttpPost("{id:guid}/ocr-extract")]
    public async Task<ActionResult<OcrExtractionResultDto>> OcrExtract(Guid id, IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Nenhum arquivo enviado.");
        }

        var query = await GetScopedRequestsQuery();
        var request = await query
            .Include(r => r.Status)
            .Include(r => r.LineItems.Where(li => !li.IsDeleted))
                .ThenInclude(li => li.ItemCatalogItem)
            .Include(r => r.LineItems.Where(li => !li.IsDeleted))
                .ThenInclude(li => li.Unit)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id);
        
        if (request == null) return NotFound("Pedido não encontrado.");

        // Rule: OCR extraction is only allowed during the quotation phase
        if (!RequestWorkflowHelper.CanMutateQuotation(request.Status!.Code))
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Ação Bloqueada", 
                Detail = "Não é possível realizar extração OCR neste status do pedido.", 
                Status = 409 
            });
        }

        try
        {
            // Step 3: Trigger Extraction via provider-agnostic service
            using var stream = file.OpenReadStream();
            var internalResult = await _extractionService.ExtractAsync(stream, file.FileName, "REQUESTS");

            // Map back to legacy DTO to preserve frontend compatibility
            var legacyResult = ExtractionMapper.MapToLegacyOcrResult(internalResult);

            await LogOcrExecutionAsync(file.FileName, id, internalResult, null);

            // ═══════════════════════════════════════════════════════════════
            // Phase 2: Persist OCR extracted items + generate reconciliation
            // ═══════════════════════════════════════════════════════════════
            var extractionBatchId = Guid.NewGuid();
            var units = await _context.Units.Where(u => u.IsActive).ToListAsync();

            // Find the proforma attachment for this request (latest PROFORMA)
            var proformaAtt = await _context.RequestAttachments
                .Where(a => a.RequestId == id && a.AttachmentTypeCode == "PROFORMA" && !a.IsDeleted)
                .OrderByDescending(a => a.UploadedAtUtc)
                .FirstOrDefaultAsync();

            var ocrItems = new List<OcrExtractedItem>();
            var lineNumber = 1;
            foreach (var item in internalResult.Items ?? new())
            {
                // Resolve unit from raw string
                int? resolvedUnitId = null;
                if (!string.IsNullOrWhiteSpace(item.Unit))
                {
                    var normalized = item.Unit.Trim().ToUpperInvariant().TrimEnd('.');
                    var matched = units.FirstOrDefault(u => 
                        u.Code.ToUpperInvariant() == normalized || 
                        u.Name.ToUpperInvariant() == normalized);
                    resolvedUnitId = matched?.Id;
                }

                ocrItems.Add(new OcrExtractedItem
                {
                    RequestId = id,
                    ExtractionBatchId = extractionBatchId,
                    AttachmentId = proformaAtt?.Id,
                    LineNumber = item.LineNumber > 0 ? item.LineNumber : lineNumber,
                    RawDescription = item.Description ?? string.Empty,
                    Quantity = item.Quantity,
                    RawUnit = item.Unit,
                    ResolvedUnitId = resolvedUnitId,
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = item.DiscountAmount,
                    DiscountPercent = item.DiscountPercent,
                    TaxRate = item.TaxRate,
                    LineTotal = item.TotalPrice,
                    QualityScore = internalResult.QualityScore,
                    ProviderName = internalResult.ProviderName,
                    ExtractedAtUtc = DateTime.UtcNow
                });
                lineNumber++;
            }

            _context.OcrExtractedItems.AddRange(ocrItems);

            // Generate reconciliation records against requester items
            var requesterItems = request.LineItems
                .Where(li => !li.IsDeleted)
                .OrderBy(li => li.LineNumber)
                .ToList();

            var reconciliationRecords = ReconciliationService.GenerateReconciliation(
                id, extractionBatchId, requesterItems, ocrItems);

            _context.ReconciliationRecords.AddRange(reconciliationRecords);

            // Audit trail
            var reconHistory = new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = id,
                ActorUserId = CurrentUserId,
                ActionTaken = "OCR_EXTRACTION_RECONCILED",
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = $"OCR executado ({internalResult.ProviderName ?? "N/A"}). {ocrItems.Count} itens extraídos, {reconciliationRecords.Count} registros de reconciliação gerados. Batch: {extractionBatchId:N}",
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.RequestStatusHistories.Add(reconHistory);

            // ═══════════════════════════════════════════════════════════════
            // Phase 2b: Persist OCR header grand total as integrity baseline
            // ═══════════════════════════════════════════════════════════════
            // This value is used by the Financial Integrity Gate at quotation
            // completion to detect divergence between the supplier's original
            // document total and the system-calculated quotation total.
            var ocrGrandTotal = internalResult.Header?.GrandTotal ?? internalResult.Header?.TotalAmount;
            if (ocrGrandTotal.HasValue && ocrGrandTotal.Value > 0)
            {
                request.OcrOriginalGrandTotal = ocrGrandTotal.Value;
            }

            await _context.SaveChangesAsync();

            // Enrich legacy result with reconciliation metadata for frontend
            legacyResult.Metadata["extractionBatchId"] = extractionBatchId.ToString();
            legacyResult.Metadata["ocrItemsPersistedCount"] = ocrItems.Count;
            legacyResult.Metadata["reconciliationRecordCount"] = reconciliationRecords.Count;

            return Ok(legacyResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during document extraction for Request {RequestId}", id);
            
            await LogOcrExecutionAsync(file.FileName, id, null, ex);

            return StatusCode(500, new OcrExtractionResultDto
            {
                Success = false,
                Status = new OcrStatusDto
                {
                    Code = "PORTAL_ERROR",
                    QualityScore = 0
                }
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Phase 2: Reconciliation Endpoints
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Get the latest reconciliation batch for a request.</summary>
    [HttpGet("{id:guid}/reconciliation")]
    public async Task<ActionResult<ReconciliationBatchDto>> GetReconciliation(Guid id, [FromQuery] Guid? batchId = null)
    {
        var request = await _context.Requests.FindAsync(id);
        if (request == null) return NotFound("Pedido não encontrado.");

        // Find the target batch
        IQueryable<OcrExtractedItem> batchQuery = _context.OcrExtractedItems
            .Where(o => o.RequestId == id);

        if (batchId.HasValue)
            batchQuery = batchQuery.Where(o => o.ExtractionBatchId == batchId.Value);

        var latestBatchItem = await batchQuery
            .OrderByDescending(o => o.ExtractedAtUtc)
            .FirstOrDefaultAsync();

        if (latestBatchItem == null)
            return Ok(new ReconciliationBatchDto()); // No extractions yet

        var targetBatchId = latestBatchItem.ExtractionBatchId;

        // Load all OCR items for the batch
        var ocrItems = await _context.OcrExtractedItems
            .Include(o => o.ResolvedUnit)
            .Where(o => o.RequestId == id && o.ExtractionBatchId == targetBatchId)
            .OrderBy(o => o.LineNumber)
            .ToListAsync();

        // Load reconciliation records
        var records = await _context.ReconciliationRecords
            .Include(r => r.RequesterItem).ThenInclude(ri => ri!.Unit)
            .Include(r => r.RequesterItem).ThenInclude(ri => ri!.ItemCatalogItem)
            .Include(r => r.OcrExtractedItem)
            .Include(r => r.ReviewedByUser)
            .Where(r => r.RequestId == id && r.ExtractionBatchId == targetBatchId)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync();

        var recordDtos = records.Select(r => new ReconciliationRecordDto
        {
            Id = r.Id,
            MatchStatus = r.MatchStatus,
            MatchConfidence = r.MatchConfidence,
            MatchStrategy = r.MatchStrategy,
            QuantityDivergence = r.QuantityDivergence,
            UnitDivergence = r.UnitDivergence,
            BuyerReviewStatus = r.BuyerReviewStatus,
            BuyerJustification = r.BuyerJustification,
            ReviewedByName = r.ReviewedByUser?.FullName,
            ReviewedAtUtc = r.ReviewedAtUtc,
            RequesterItemId = r.RequesterItemId,
            RequesterDescription = r.RequesterItem?.Description,
            RequesterQuantity = r.RequesterItem?.Quantity,
            RequesterUnitCode = r.RequesterItem?.Unit?.Code,
            RequesterCatalogId = r.RequesterItem?.ItemCatalogId,
            RequesterCatalogCode = r.RequesterItem?.ItemCatalogItem?.Code,
            OcrExtractedItemId = r.OcrExtractedItemId,
            OcrDescription = r.OcrExtractedItem?.RawDescription,
            OcrQuantity = r.OcrExtractedItem?.Quantity,
            OcrRawUnit = r.OcrExtractedItem?.RawUnit,
            OcrUnitPrice = r.OcrExtractedItem?.UnitPrice,
            OcrLineTotal = r.OcrExtractedItem?.LineTotal
        }).ToList();

        var summary = BuildReconciliationSummary(recordDtos);

        return Ok(new ReconciliationBatchDto
        {
            ExtractionBatchId = targetBatchId,
            ExtractedAtUtc = latestBatchItem.ExtractedAtUtc,
            ProviderName = latestBatchItem.ProviderName,
            QualityScore = latestBatchItem.QualityScore,
            AttachmentId = latestBatchItem.AttachmentId,
            OcrItemCount = ocrItems.Count,
            Records = recordDtos,
            Summary = summary
        });
    }

    /// <summary>Submit buyer review decisions for reconciliation records.</summary>
    [HttpPut("{id:guid}/reconciliation/review")]
    public async Task<ActionResult<ReconciliationSummaryDto>> SubmitReconciliationReview(
        Guid id, [FromBody] ReconciliationReviewRequestDto dto)
    {
        var actorId = CurrentUserId;
        var request = await (await GetScopedRequestsQuery()).FirstOrDefaultAsync(r => r.Id == id);
        if (request == null) return NotFound("Pedido não encontrado.");

        var recordIds = dto.Reviews.Select(r => r.RecordId).ToList();
        var records = await _context.ReconciliationRecords
            .Where(r => r.RequestId == id && recordIds.Contains(r.Id))
            .ToListAsync();

        var validStatuses = new HashSet<string> { "CONFIRMED", "REJECTED", "ADJUSTED" };
        var now = DateTime.UtcNow;

        foreach (var review in dto.Reviews)
        {
            var record = records.FirstOrDefault(r => r.Id == review.RecordId);
            if (record == null) continue;
            if (!validStatuses.Contains(review.ReviewStatus)) continue;

            record.BuyerReviewStatus = review.ReviewStatus;
            record.BuyerJustification = review.Justification;
            record.ReviewedByUserId = actorId;
            record.ReviewedAtUtc = now;
        }

        // Audit trail
        var reviewHistory = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = actorId,
            ActionTaken = "RECONCILIATION_REVIEWED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Reconciliação revisada: {dto.Reviews.Count} registros atualizados.",
            CreatedAtUtc = now
        };
        _context.RequestStatusHistories.Add(reviewHistory);

        await _context.SaveChangesAsync();

        // Return updated summary
        var batchId = records.FirstOrDefault()?.ExtractionBatchId ?? Guid.Empty;
        var allRecords = await _context.ReconciliationRecords
            .Where(r => r.RequestId == id && r.ExtractionBatchId == batchId)
            .ToListAsync();

        var summaryDtos = allRecords.Select(r => new ReconciliationRecordDto
        {
            MatchStatus = r.MatchStatus,
            BuyerReviewStatus = r.BuyerReviewStatus
        }).ToList();

        return Ok(BuildReconciliationSummary(summaryDtos));
    }

    /// <summary>Get reconciliation summary for a request (lightweight).</summary>
    [HttpGet("{id:guid}/reconciliation/summary")]
    public async Task<ActionResult<ReconciliationSummaryDto>> GetReconciliationSummary(Guid id)
    {
        var latestBatch = await _context.OcrExtractedItems
            .Where(o => o.RequestId == id)
            .OrderByDescending(o => o.ExtractedAtUtc)
            .Select(o => o.ExtractionBatchId)
            .FirstOrDefaultAsync();

        if (latestBatch == Guid.Empty)
            return Ok(new ReconciliationSummaryDto());

        var records = await _context.ReconciliationRecords
            .Where(r => r.RequestId == id && r.ExtractionBatchId == latestBatch)
            .Select(r => new ReconciliationRecordDto
            {
                MatchStatus = r.MatchStatus,
                BuyerReviewStatus = r.BuyerReviewStatus
            })
            .ToListAsync();

        return Ok(BuildReconciliationSummary(records));
    }

    private static ReconciliationSummaryDto BuildReconciliationSummary(List<ReconciliationRecordDto> records)
    {
        return new ReconciliationSummaryDto
        {
            TotalRecords = records.Count,
            ExactMatches = records.Count(r => r.MatchStatus == "EXACT_MATCH"),
            ProbableMatches = records.Count(r => r.MatchStatus == "PROBABLE_MATCH"),
            ReviewRequired = records.Count(r => r.MatchStatus == "REVIEW_REQUIRED"),
            ExtraSupplierItems = records.Count(r => r.MatchStatus == "EXTRA_SUPPLIER_ITEM"),
            MissingRequestedItems = records.Count(r => r.MatchStatus == "MISSING_REQUESTED_ITEM"),
            BuyerConfirmed = records.Count(r => r.BuyerReviewStatus == "CONFIRMED"),
            BuyerPending = records.Count(r => r.BuyerReviewStatus == "PENDING"),
            BuyerRejected = records.Count(r => r.BuyerReviewStatus == "REJECTED")
        };
    }

    [AllowAnonymous]
    [HttpPost("direct-ocr")]
    public async Task<ActionResult<OcrExtractionResultDto>> DirectOcrExtract(IFormFile file, [FromQuery] string? sourceContext = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Nenhum arquivo enviado.");
        }

        try
        {
            // Trigger Extraction directly without Request ID check
            using var stream = file.OpenReadStream();
            var internalResult = await _extractionService.ExtractAsync(stream, file.FileName, sourceContext);

            // Map back to legacy DTO to preserve frontend compatibility
            var legacyResult = ExtractionMapper.MapToLegacyOcrResult(internalResult);

            await LogOcrExecutionAsync(file.FileName, null, internalResult, null);

            return Ok(legacyResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during direct document extraction");
            
            await LogOcrExecutionAsync(file.FileName, null, null, ex);

            return StatusCode(500, new OcrExtractionResultDto
            {
                Success = false,
                Status = new OcrStatusDto
                {
                    Code = "PORTAL_ERROR",
                    QualityScore = 0
                }
            });
        }
    }

    [HttpPost("{id:guid}/quotations")]
    
    /// <summary>
    /// Option C — explicit Buyer authorization to reuse quotation items previously used in a
    /// CANCELLED approval batch. One authorization record per item (partial reuse/revocation,
    /// exact audit). All-or-nothing: any invalid item aborts the whole call. The cancelled batch
    /// is never reopened or modified.
    /// </summary>
    [HttpPost("{id:guid}/quotations/{quotationId:guid}/authorize-reuse")]
    public async Task<IActionResult> AuthorizeQuotationReuse(Guid id, Guid quotationId, [FromBody] AuthorizeQuotationReuseDto dto)
    {
        var actorId = CurrentUserId;

        if (!CurrentUserRoles.Contains(RoleConstants.Buyer) && !CurrentUserRoles.Contains(RoleConstants.SystemAdministrator))
            return StatusCode(403, new ProblemDetails { Title = "Acesso Negado", Detail = "Apenas compradores podem autorizar o reuso de cotações.", Status = 403 });

        var reason = dto.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new ProblemDetails { Title = "Motivo Obrigatório", Detail = "Informe o motivo da autorização de reuso.", Status = 400 });

        var itemIds = (dto.QuotationItemIds ?? new List<Guid>()).Distinct().ToList();
        if (itemIds.Count == 0)
            return BadRequest(new ProblemDetails { Title = "Nenhum Item", Detail = "Selecione pelo menos um item de cotação para autorizar o reuso.", Status = 400 });

        var quotation = await _context.Quotations.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.RequestId == id);
        if (quotation == null)
            return NotFound(new ProblemDetails { Title = "Cotação não encontrada.", Status = 404 });

        var quotationItems = await _context.QuotationItems.AsNoTracking()
            .Where(qi => qi.QuotationId == quotationId && itemIds.Contains(qi.Id))
            .ToDictionaryAsync(qi => qi.Id);

        var eligibility = await _quotationEligibility.GetEligibilityMapAsync(id);

        // All-or-nothing validation: every requested item must be individually authorizable.
        var validationErrors = new List<string>();
        foreach (var itemId in itemIds)
        {
            if (!quotationItems.TryGetValue(itemId, out var qi))
            {
                validationErrors.Add($"Item {itemId} não pertence a esta cotação.");
                continue;
            }
            if (!eligibility.TryGetValue(itemId, out var elig))
            {
                validationErrors.Add($"Item {itemId} não encontrado no pedido.");
                continue;
            }
            switch (elig.ReasonCode)
            {
                case Application.Interfaces.Purchasing.QuotationItemEligibilityReasons.ReuseAuthorizationRequired:
                case Application.Interfaces.Purchasing.QuotationItemEligibilityReasons.AuthorizationConsumed:
                case Application.Interfaces.Purchasing.QuotationItemEligibilityReasons.AuthorizationRevoked:
                    break; // authorizable (consumed/revoked belong to a finished cycle — a new record is allowed)
                case Application.Interfaces.Purchasing.QuotationItemEligibilityReasons.ReuseAuthorized:
                    return Conflict(new ProblemDetails
                    {
                        Title = "Autorização Já Existente",
                        Detail = $"O item '{qi.Description}' já possui uma autorização de reuso ativa.",
                        Status = 409,
                        Extensions = { ["code"] = "REUSE_ALREADY_AUTHORIZED", ["quotationItemId"] = itemId }
                    });
                case Application.Interfaces.Purchasing.QuotationItemEligibilityReasons.ItemNotReconciled:
                    validationErrors.Add($"Item '{qi.Description}' não está reconciliado (MAPPED/SUBSTITUTE/EXTRA_ITEM) e não pode ser autorizado.");
                    break;
                default: // ELIGIBLE_NORMAL — never used in a cancelled batch
                    validationErrors.Add($"Item '{qi.Description}' não foi utilizado em nenhum lote cancelado — não requer autorização de reuso.");
                    break;
            }
        }

        if (validationErrors.Count > 0)
            return BadRequest(new ProblemDetails { Title = "Validação de Reuso Falhou", Detail = string.Join(" | ", validationErrors), Status = 400 });

        // Create one auditable record per item (all in the same transaction).
        var now = DateTime.UtcNow;
        var created = new List<QuotationReuseAuthorization>();
        foreach (var itemId in itemIds)
        {
            var elig = eligibility[itemId];
            created.Add(new QuotationReuseAuthorization
            {
                Id = Guid.NewGuid(),
                RequestId = id,
                QuotationId = quotationId,
                QuotationItemId = itemId,
                SourceApprovalBatchId = elig.SourceCancelledBatchId!.Value,
                AuthorizedByUserId = actorId,
                AuthorizedAtUtc = now,
                Reason = reason!,
                IsActive = true,
                CreatedAtUtc = now
            });
        }
        _context.QuotationReuseAuthorizations.AddRange(created);

        var sourceBatchNumbers = itemIds
            .Select(i => eligibility[i].SourceCancelledBatchNumber)
            .Where(n => n.HasValue).Select(n => n!.Value).Distinct().OrderBy(n => n).ToList();
        var itemDescriptions = itemIds.Select(i => quotationItems[i].Description).ToList();

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = actorId,
            ActionTaken = "QUOTATION_REUSE_AUTHORIZED",
            PreviousStatusId = (await _context.Requests.AsNoTracking().Where(r => r.Id == id).Select(r => r.StatusId).FirstAsync()),
            NewStatusId = (await _context.Requests.AsNoTracking().Where(r => r.Id == id).Select(r => r.StatusId).FirstAsync()),
            Comment = $"Reuso autorizado para {created.Count} item(ns) da cotação {quotation.DocumentNumber} ({quotation.SupplierNameSnapshot}), utilizados no(s) Lote(s) #{string.Join(", #", sourceBatchNumbers)} (cancelado). Itens: {string.Join("; ", itemDescriptions)}. Motivo: {reason}",
            CreatedAtUtc = now
        });

        await _context.SaveChangesAsync();

        return Ok(new
        {
            authorizationCount = created.Count,
            quotationId,
            quotationItemIds = itemIds,
            sourceCancelledBatchIds = created.Select(c => c.SourceApprovalBatchId).Distinct().ToList(),
            authorizedAtUtc = now
        });
    }

    /// <summary>Revokes an active, unconsumed reuse authorization (Buyer-only). Consumed authorizations cannot be revoked.</summary>
    [HttpPost("{id:guid}/quotation-reuse-authorizations/{authorizationId:guid}/revoke")]
    public async Task<IActionResult> RevokeQuotationReuse(Guid id, Guid authorizationId, [FromBody] RevokeQuotationReuseDto dto)
    {
        var actorId = CurrentUserId;

        if (!CurrentUserRoles.Contains(RoleConstants.Buyer) && !CurrentUserRoles.Contains(RoleConstants.SystemAdministrator))
            return StatusCode(403, new ProblemDetails { Title = "Acesso Negado", Detail = "Apenas compradores podem revogar autorizações de reuso.", Status = 403 });

        var reason = dto.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new ProblemDetails { Title = "Motivo Obrigatório", Detail = "Informe o motivo da revogação.", Status = 400 });

        var auth = await _context.QuotationReuseAuthorizations
            .Include(a => a.Quotation)
            .FirstOrDefaultAsync(a => a.Id == authorizationId && a.RequestId == id);
        if (auth == null)
            return NotFound(new ProblemDetails { Title = "Autorização não encontrada.", Status = 404 });

        if (auth.ConsumedAtUtc != null)
            return Conflict(new ProblemDetails
            {
                Title = "Autorização Já Consumida",
                Detail = "Esta autorização já foi consumida por um lote e não pode ser revogada.",
                Status = 409,
                Extensions = { ["code"] = "REUSE_AUTHORIZATION_CONSUMED" }
            });

        if (!auth.IsActive || auth.RevokedAtUtc != null)
            return Conflict(new ProblemDetails
            {
                Title = "Autorização Inativa",
                Detail = "Esta autorização já não está ativa.",
                Status = 409,
                Extensions = { ["code"] = "REUSE_AUTHORIZATION_INACTIVE" }
            });

        auth.IsActive = false;
        auth.RevokedByUserId = actorId;
        auth.RevokedAtUtc = DateTime.UtcNow;
        auth.RevocationReason = reason;

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = actorId,
            ActionTaken = "QUOTATION_REUSE_REVOKED",
            PreviousStatusId = (await _context.Requests.AsNoTracking().Where(r => r.Id == id).Select(r => r.StatusId).FirstAsync()),
            NewStatusId = (await _context.Requests.AsNoTracking().Where(r => r.Id == id).Select(r => r.StatusId).FirstAsync()),
            Comment = $"Autorização de reuso revogada (cotação {auth.Quotation?.DocumentNumber}, item {auth.QuotationItemId}). Motivo: {reason}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return Ok(new { revoked = true, authorizationId });
    }

    /// <summary>
    /// API-boundary normalization of reconciliation statuses (single canonical vocabulary —
    /// RequestConstants.ReconciliationStatuses). Trims and upper-cases so casing/whitespace
    /// variants from any client can never silently bypass status-based rules downstream.
    /// </summary>
    private static void NormalizeReconciliationStatuses(SaveQuotationRequestDto dto)
    {
        foreach (var item in dto.Items)
        {
            item.ReconciliationStatus = (item.ReconciliationStatus ?? string.Empty).Trim().ToUpperInvariant();
        }
    }

    private bool ValidateReconciliation(SaveQuotationRequestDto dto, out string errorMessage)
    {
        errorMessage = string.Empty;
        foreach (var item in dto.Items)
        {
            if (item.ReconciliationStatus == "LEGACY_UNMAPPED")
            {
                errorMessage = "O status LEGACY_UNMAPPED n\u00e3o pode ser criado por novas requisi\u00e7\u00f5es.";
                return false;
            }
            if (item.ReconciliationStatus == "MAPPED" && !item.MappedRequestLineItemId.HasValue)
            {
                errorMessage = "Itens MAPPED precisam ter um MappedRequestLineItemId associado.";
                return false;
            }
            if (item.ReconciliationStatus == "SUBSTITUTE")
            {
                if (!item.MappedRequestLineItemId.HasValue)
                {
                    errorMessage = "Itens SUBSTITUTE precisam ter um MappedRequestLineItemId associado.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(item.ReconciliationJustification))
                {
                    errorMessage = "Itens SUBSTITUTE precisam de uma justificativa.";
                    return false;
                }
            }
            if (item.ReconciliationStatus == "EXTRA_ITEM")
            {
                if (item.MappedRequestLineItemId.HasValue)
                {
                    errorMessage = "Itens EXTRA_ITEM n\u00e3o podem ter um MappedRequestLineItemId associado.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(item.ReconciliationJustification))
                {
                    errorMessage = "Itens EXTRA_ITEM precisam de uma justificativa.";
                    return false;
                }
            }
            if (item.ReconciliationStatus == "IGNORED" && item.MappedRequestLineItemId.HasValue)
            {
                errorMessage = "Itens IGNORED n\u00e3o podem ter um MappedRequestLineItemId associado.";
                return false;
            }
            if (item.ReconciliationStatus == "NOT_QUOTED")
            {
                if (!item.MappedRequestLineItemId.HasValue)
                {
                    errorMessage = "Itens NOT_QUOTED precisam ter um MappedRequestLineItemId associado.";
                    return false;
                }
                if (item.Quantity != 0 || item.UnitPrice != 0 || item.DiscountAmount != 0)
                {
                    errorMessage = "Itens NOT_QUOTED devem ter valores financeiros zerados.";
                    return false;
                }
            }
        }
        return true;
    }

    [HttpPost("{id:guid}/quotations")]
    public async Task<ActionResult<SavedQuotationDto>> SaveQuotation(Guid id, [FromQuery] Guid? replaceQuotationId, [FromBody] SaveQuotationRequestDto dto)
    {
        NormalizeReconciliationStatuses(dto);

        if (!ValidateReconciliation(dto, out string saveError))
        {
            return BadRequest(new ProblemDetails { Title = "Reconciliation Validation Failed", Detail = saveError, Status = 400 });
        }

        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var query = await GetScopedRequestsQuery();
        var request = await query
            .Include(r => r.Status)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
                    .ThenInclude(i => i.Unit)
            .Include(r => r.Attachments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id);
        
        if (request == null) return NotFound("Pedido não encontrado.");

        // Status Rule Check: Only explicitly editable statuses allow quotation persistence changes
        if (!RequestWorkflowHelper.CanMutateQuotation(request.Status!.Code))
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Ação Bloqueada", 
                Detail = "Não é possível adicionar cotações neste status do pedido.", 
                Status = 409 
            });
        }

        // Duplicate Supplier Protection
        var existingQuotations = request.Quotations.Where(q => q.SupplierId == dto.SupplierId && q.Id != replaceQuotationId).ToList();
        if (existingQuotations.Any())
        {
            var existingQuotationItemIds = existingQuotations.SelectMany(q => q.Items).Select(i => i.Id).ToList();
            var isAuditProtected = await _context.Set<ApprovalBatchItem>()
                .AnyAsync(abi => existingQuotationItemIds.Contains(abi.SelectedQuotationItemId));

            if (!isAuditProtected)
            {
                return Conflict(new ProblemDetails 
                { 
                    Title = "Regra de Negócio Violada", 
                    Detail = "Já existe uma cotação para este fornecedor. Confirme a substituição ou escolha outro fornecedor.",
                    Status = 409
                });
            }
        }

        // Basic Validation
        if (dto.SupplierId <= 0) return BadRequest(new ProblemDetails { Title = "Validação de Cotação", Detail = "O fornecedor é obrigatório.", Status = 400 });
        if (string.IsNullOrWhiteSpace(dto.Currency)) return BadRequest(new ProblemDetails { Title = "Validação de Cotação", Detail = "A moeda é obrigatória.", Status = 400 });
        if (dto.Items == null || !dto.Items.Any()) return BadRequest(new ProblemDetails { Title = "Validação de Cotação", Detail = "A cotação deve conter pelo menos um item.", Status = 400 });

        var supplier = await _context.Suppliers.FindAsync(dto.SupplierId);
        if (supplier == null) return BadRequest(new ProblemDetails { Title = "Validação de Cotação", Detail = "Fornecedor selecionado não existe.", Status = 400 });
        
        var ivaRates = await _context.IvaRates.ToDictionaryAsync(i => i.Id, i => i.RatePercent);

        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            SupplierId = dto.SupplierId,
            SupplierNameSnapshot = supplier.Name, // Explicit snapshot from the current record
            DocumentNumber = dto.DocumentNumber?.Trim(),
            DocumentDate = dto.DocumentDate,
            Currency = dto.Currency.ToUpper(),
            SourceType = dto.SourceType ?? "MANUAL",
            SourceFileName = dto.SourceFileName,
            ProformaAttachmentId = dto.ProformaAttachmentId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorId
        };

        if (replaceQuotationId.HasValue)
        {
            var oldQuotation = request.Quotations.FirstOrDefault(q => q.Id == replaceQuotationId.Value);
            if (oldQuotation != null)
            {
                if (oldQuotation.ProformaAttachmentId.HasValue)
                {
                    var oldProforma = request.Attachments.FirstOrDefault(a => a.Id == oldQuotation.ProformaAttachmentId.Value);
                    if (oldProforma != null) oldProforma.IsDeleted = true;
                }
                
                var itemsToDelete = await _context.QuotationItems.Where(qi => qi.QuotationId == oldQuotation.Id).ToListAsync();
                _context.QuotationItems.RemoveRange(itemsToDelete);
                _context.Quotations.Remove(oldQuotation);
            }
        }

        foreach (var item in dto.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Description)) return BadRequest(new ProblemDetails { Title = "Validação de Item", Detail = "Todos os itens devem ter uma descrição.", Status = 400 });
            
            if (item.Quantity <= 0 && item.ReconciliationStatus != "NOT_QUOTED" && item.ReconciliationStatus != "IGNORED") 
            {
                return BadRequest(new ProblemDetails { Title = "Validação de Item", Detail = $"Item {item.LineNumber}: A quantidade deve ser maior que zero.", Status = 400 });
            }
            
            if (item.UnitPrice < 0) return BadRequest(new ProblemDetails { Title = "Validação de Item", Detail = "O preço unitário não pode ser negativo.", Status = 400 });

            decimal grossSubtotal = Round2(item.Quantity * item.UnitPrice);
            decimal itemDiscount = Round2(item.DiscountAmount);
            if (itemDiscount < 0) return BadRequest(new ProblemDetails { Title = "Validação de Item", Detail = "O desconto do item não pode ser negativo.", Status = 400 });
            if (itemDiscount > grossSubtotal) return BadRequest(new ProblemDetails { Title = "Validação de Item", Detail = "O desconto não pode exceder o subtotal bruto no item " + item.LineNumber, Status = 400 });

            decimal netSubtotal = Math.Max(0, grossSubtotal - itemDiscount);
            decimal ivaPercent = item.IvaRateId.HasValue && ivaRates.TryGetValue(item.IvaRateId.Value, out var rate) ? rate : 0m;
            decimal ivaAmount = Round2(netSubtotal * (ivaPercent / 100m));
            decimal lineTotal = Round2(netSubtotal + ivaAmount);

            // Ignoring a document line with monetary value excludes it from the comparable
            // integrity baseline — that exclusion requires its own per-line justification.
            // Zero-value ignored lines need none.
            if (item.ReconciliationStatus == RequestConstants.ReconciliationStatuses.Ignored && lineTotal > 0
                && string.IsNullOrWhiteSpace(item.ReconciliationJustification))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Justificativa Obrigatória",
                    Detail = $"Item {item.LineNumber}: uma linha ignorada com valor ({lineTotal:N2}) exige justificativa própria na reconciliação.",
                    Status = 400
                });
            }

            quotation.Items.Add(new QuotationItem
            {
                Id = Guid.NewGuid(),
                QuotationId = quotation.Id,
                LineNumber = item.LineNumber,
                Description = item.Description,
                UnitId = item.UnitId,
                ItemCatalogId = item.ItemCatalogId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                DiscountAmount = itemDiscount,
                DiscountPercent = item.DiscountPercent,
                IvaRateId = item.IvaRateId,
                IvaRatePercent = ivaPercent,
                GrossSubtotal = grossSubtotal,
                IvaAmount = ivaAmount,
                LineTotal = lineTotal,
                MappedRequestLineItemId = item.MappedRequestLineItemId,
                ReconciliationStatus = item.ReconciliationStatus,
                ReconciliationJustification = item.ReconciliationJustification
            });
        }

        // Sum up line totals. Item-level discounts are already applied per-line.
        // Only CONSIDERED lines (MAPPED/SUBSTITUTE/EXTRA_ITEM) compose the quotation's financial
        // totals: IGNORED lines are persisted for audit (with their justification) but are
        // explicitly excluded from the quotation value — mirroring the wizard's displayed total.
        var totalConsideredItems = quotation.Items
            .Where(i => RequestConstants.ReconciliationStatuses.Considered.Contains(i.ReconciliationStatus))
            .ToList();
        decimal totalGross = totalConsideredItems.Sum(i => i.GrossSubtotal);
        decimal sumItemDiscounts = totalConsideredItems.Sum(i => i.DiscountAmount);
        decimal totalItemIva = totalConsideredItems.Sum(i => i.IvaAmount);
        decimal netAfterItemDiscounts = Round2(Math.Max(0, totalGross - sumItemDiscounts));

        // Global (commercial) discount from DTO — reduces the taxable base further
        decimal globalDiscount = Round2(Math.Max(0, dto.DiscountAmount));
        decimal taxableBase = Round2(Math.Max(0, netAfterItemDiscounts - globalDiscount));

        // Proportionally adjust IVA based on the global discount reduction
        decimal discountRatio = netAfterItemDiscounts > 0 ? (taxableBase / netAfterItemDiscounts) : 1m;
        decimal adjustedIva = Round2(totalItemIva * discountRatio);

        quotation.DiscountAmount = globalDiscount;
        quotation.TotalGrossAmount = Round2(totalGross);
        quotation.TotalDiscountAmount = Round2(sumItemDiscounts + globalDiscount);
        quotation.TotalTaxableBase = taxableBase;
        quotation.TotalIvaAmount = adjustedIva;
        quotation.TotalAmount = Round2(taxableBase + adjustedIva);

        // ═══════════════════════════════════════════════════════════════
        // Phase 1: Per-Quotation Financial Integrity Gate
        // The OCR baseline covers the WHOLE document, while the quotation total only covers
        // reconciled lines — so lines explicitly reconciled as IGNORED are subtracted from the
        // baseline (comparable scope). Real divergences on considered lines still trip the gate.
        // Math extracted to QuotationIntegrityCalculator for testability.
        // ═══════════════════════════════════════════════════════════════
        if (dto.OcrTotal.HasValue && dto.OcrTotal.Value > 0 && !dto.FinancialIntegrityOverride)
        {
            var integrity = AlplaPortal.Application.Validation.QuotationIntegrityCalculator.Compute(
                dto.OcrTotal.Value, quotation.Items, globalDiscount, netAfterItemDiscounts);

            if (integrity.VarianceAmount > AlplaPortal.Application.Validation.QuotationIntegrityCalculator.ToleranceAmount)
            {
                return StatusCode(409, new
                {
                    integrityCheckFailed = true,
                    ocrOriginalTotal = integrity.OcrOriginalTotal,
                    excludedIgnoredTotal = integrity.ExcludedIgnoredTotal,
                    comparableDocumentTotal = integrity.ComparableDocumentTotal,
                    quotationTotal = integrity.QuotationConsideredTotal,
                    varianceAmount = integrity.VarianceAmount,
                    variancePercent = integrity.VariancePercent,
                    toleranceApplied = AlplaPortal.Application.Validation.QuotationIntegrityCalculator.ToleranceAmount,
                    detail = $"Divergência detectada ({integrity.VarianceAmount:N2} {quotation.Currency}). A cotação não pode ser salva sem justificação explícita."
                });
            }
        }

        if (dto.FinancialIntegrityOverride && !string.IsNullOrWhiteSpace(dto.OverrideJustification))
        {
            var auditHistory = new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = actorId,
                ActionTaken = "FINANCIAL_INTEGRITY_OVERRIDEN",
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = $"Alerta de integridade financeira ignorado ao salvar cotação. Justificação: {dto.OverrideJustification}",
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.RequestStatusHistories.Add(auditHistory);
        }

        _context.Quotations.Add(quotation);

        // 4. Record Audit History for Quotation Management
        var qAction = replaceQuotationId.HasValue ? "COTACAO_SUBSTITUIDA" : "COTACAO_ADICIONADA";
        var qComment = replaceQuotationId.HasValue 
            ? $"Cotação do fornecedor {quotation.SupplierNameSnapshot} substituída via {quotation.SourceType}." 
            : $"Cotação do fornecedor {quotation.SupplierNameSnapshot} adicionada via {quotation.SourceType}.";

        var qHistory = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actorId,
            ActionTaken = qAction,
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = qComment,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(qHistory);

        await _context.SaveChangesAsync();
        
        // RE-QUERY items with Units to ensure the response projection is complete
        var savedItems = await _context.QuotationItems
            .Include(qi => qi.Unit)
            .Include(qi => qi.ItemCatalog)
            .Where(qi => qi.QuotationId == quotation.Id)
            .OrderBy(i => i.LineNumber)
            .ToListAsync();

        return Ok(new SavedQuotationDto
        {
            Id = quotation.Id,
            RequestId = quotation.RequestId,
            SupplierId = quotation.SupplierId,
            SupplierNameSnapshot = quotation.SupplierNameSnapshot,
            SupplierPortalCode = quotation.Supplier != null ? quotation.Supplier.PortalCode : null,
            SupplierPrimaveraCode = quotation.Supplier != null ? quotation.Supplier.PrimaveraCode : null,
            SupplierRegistrationStatus = quotation.Supplier != null ? quotation.Supplier.RegistrationStatus : null,
            DocumentNumber = quotation.DocumentNumber,
            DocumentDate = quotation.DocumentDate,
            Currency = quotation.Currency,
            TotalGrossAmount = quotation.TotalGrossAmount,
            TotalDiscountAmount = quotation.TotalDiscountAmount,
            TotalTaxableBase = quotation.TotalTaxableBase,
            DiscountAmount = quotation.DiscountAmount,
            TotalIvaAmount = quotation.TotalIvaAmount,
            TotalAmount = quotation.TotalAmount,
            SourceType = quotation.SourceType,
            SourceFileName = quotation.SourceFileName,
            ProformaAttachmentId = quotation.ProformaAttachmentId,
            CreatedAtUtc = quotation.CreatedAtUtc,
            ItemCount = savedItems.Count,
            Items = savedItems.Select(qi => new SavedQuotationItemDto
            {
                Id = qi.Id,
                LineNumber = qi.LineNumber,
                Description = qi.Description,
                Quantity = qi.Quantity,
                MappedRequestLineItemId = qi.MappedRequestLineItemId,
                ReconciliationStatus = qi.ReconciliationStatus,
                ReconciliationJustification = qi.ReconciliationJustification,
                UnitId = qi.UnitId,
                UnitName = qi.Unit?.Name,
                UnitCode = qi.Unit?.Code,
                UnitPrice = qi.UnitPrice,
                DiscountAmount = qi.DiscountAmount,
                DiscountPercent = qi.DiscountPercent,
                IvaRateId = qi.IvaRateId,
                IvaRatePercent = qi.IvaRatePercent,
                GrossSubtotal = qi.GrossSubtotal,
                IvaAmount = qi.IvaAmount,
                LineTotal = qi.LineTotal,
                ItemCatalogId = qi.ItemCatalogId,
                ItemCatalogCode = qi.ItemCatalog != null ? qi.ItemCatalog.Code : null
            }).ToList()
        });
    }

    [HttpPut("{requestId}/quotations/{quotationId}")]
    public async Task<ActionResult<SavedQuotationDto>> UpdateQuotation([FromRoute] Guid requestId, [FromRoute] Guid quotationId, [FromBody] SaveQuotationRequestDto dto)
    {
        NormalizeReconciliationStatuses(dto);

        if (!ValidateReconciliation(dto, out string saveError))
        {
            return BadRequest(new ProblemDetails { Title = "Reconciliation Validation Failed", Detail = saveError, Status = 400 });
        }

        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var query = await GetScopedRequestsQuery();
        var request = await query
            .Include(r => r.Status)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        
        if (request == null) return NotFound("Pedido não encontrado.");

        // Status Rule Check: Only explicitly editable statuses allow quotation persistence changes
        if (!RequestWorkflowHelper.CanMutateQuotation(request.Status!.Code))
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Ação Bloqueada", 
                Detail = "Não é possível alterar cotações neste status do pedido.", 
                Status = 409 
            });
        }

        var quotation = await _context.Quotations
            .Include(q => q.Items)
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.RequestId == requestId);

        if (quotation == null) return NotFound("Cotação não encontrada.");

        // Duplicate Supplier Protection
        var requestWithQuotations = await _context.Requests
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .FirstOrDefaultAsync(r => r.Id == requestId);
            
        if (requestWithQuotations != null)
        {
            var existingQuotations = requestWithQuotations.Quotations.Where(q => q.SupplierId == dto.SupplierId && q.Id != quotationId).ToList();
            if (existingQuotations.Any())
            {
                var existingQuotationItemIds = existingQuotations.SelectMany(q => q.Items).Select(i => i.Id).ToList();
                var isAuditProtected = await _context.Set<ApprovalBatchItem>()
                    .AnyAsync(abi => existingQuotationItemIds.Contains(abi.SelectedQuotationItemId));

                if (!isAuditProtected)
                {
                    return Conflict(new ProblemDetails 
                    { 
                        Title = "Regra de Negócio Violada", 
                        Detail = "Já existe uma cotação para este fornecedor. Confirme a substituição ou escolha outro fornecedor.",
                        Status = 409
                    });
                }
            }
        }

        // Validation (Explicitly including Currency as per user requirement)
        if (dto.SupplierId <= 0) return BadRequest(new ProblemDetails { Title = "Validação de Cotação", Detail = "O fornecedor é obrigatório.", Status = 400 });
        if (string.IsNullOrWhiteSpace(dto.Currency)) return BadRequest(new ProblemDetails { Title = "Validação de Cotação", Detail = "A moeda é obrigatória.", Status = 400 });
        if (dto.Items == null || !dto.Items.Any()) return BadRequest(new ProblemDetails { Title = "Validação de Cotação", Detail = "A cotação deve conter pelo menos um item.", Status = 400 });

        var supplier = await _context.Suppliers.FindAsync(dto.SupplierId);
        if (supplier == null) return BadRequest(new ProblemDetails { Title = "Validação de Cotação", Detail = "Fornecedor selecionado não existe.", Status = 400 });

        // Update Header
        if (quotation.ProformaAttachmentId != dto.ProformaAttachmentId)
        {
            if (quotation.ProformaAttachmentId.HasValue)
            {
                // Authoritative link: only delete the specific proforma linked to this quotation
                var oldProforma = await _context.RequestAttachments.FindAsync(quotation.ProformaAttachmentId.Value);
                if (oldProforma != null) oldProforma.IsDeleted = true;
            }
            quotation.ProformaAttachmentId = dto.ProformaAttachmentId;
        }
        
        quotation.SupplierId = dto.SupplierId;
        quotation.SupplierNameSnapshot = supplier.Name;
        quotation.DocumentNumber = dto.DocumentNumber?.Trim();
        quotation.DocumentDate = dto.DocumentDate;
        quotation.Currency = dto.Currency.ToUpper();

        // Replace Items (Merge/Upsert logic to preserve IDs and avoid FK constraints)
        var existingItemIds = quotation.Items.Select(i => i.Id).ToList();
        var referencedItemIds = await _context.Set<ApprovalBatchItem>()
            .Where(abi => existingItemIds.Contains(abi.SelectedQuotationItemId))
            .Select(abi => abi.SelectedQuotationItemId)
            .ToListAsync();

        var existingItemsList = quotation.Items.ToList();
        var itemsToRemove = new List<QuotationItem>();

        foreach (var existing in existingItemsList)
        {
            var dtoItem = dto.Items.FirstOrDefault(d => 
                (d.MappedRequestLineItemId.HasValue && existing.MappedRequestLineItemId == d.MappedRequestLineItemId) ||
                (!d.MappedRequestLineItemId.HasValue && !existing.MappedRequestLineItemId.HasValue && d.LineNumber == existing.LineNumber)
            );

            if (dtoItem == null)
            {
                if (referencedItemIds.Contains(existing.Id))
                {
                    return Conflict(new ProblemDetails 
                    { 
                        Title = "Ação Bloqueada", 
                        Detail = $"O item da cotação '{existing.Description}' está em um lote de aprovação e não pode ser removido. Faça o ajuste dos valores em vez de removê-lo.", 
                        Status = 409 
                    });
                }
                itemsToRemove.Add(existing);
            }
        }

        _context.QuotationItems.RemoveRange(itemsToRemove);
        foreach(var rm in itemsToRemove) { quotation.Items.Remove(rm); }

        var ivaRates = await _context.IvaRates.ToDictionaryAsync(i => i.Id, i => i.RatePercent);

        foreach (var item in dto.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Description)) return BadRequest(new ProblemDetails { Title = "Validação de Item", Detail = "Todos os itens devem ter uma descrição.", Status = 400 });
            
            if (item.Quantity <= 0 && item.ReconciliationStatus != "NOT_QUOTED" && item.ReconciliationStatus != "IGNORED") 
            {
                return BadRequest(new ProblemDetails { Title = "Validação de Item", Detail = $"Item {item.LineNumber}: A quantidade deve ser maior que zero.", Status = 400 });
            }
            
            if (item.UnitPrice < 0) return BadRequest(new ProblemDetails { Title = "Validação de Item", Detail = "O preço unitário não pode ser negativo.", Status = 400 });

            decimal grossSubtotal = Round2(item.Quantity * item.UnitPrice);
            decimal itemDiscount = Round2(item.DiscountAmount);
            if (itemDiscount < 0) return BadRequest(new ProblemDetails { Title = "Validação de Item", Detail = "O desconto do item não pode ser negativo.", Status = 400 });
            if (itemDiscount > grossSubtotal) return BadRequest(new ProblemDetails { Title = "Validação de Item", Detail = "O desconto não pode exceder o subtotal bruto no item " + item.LineNumber, Status = 400 });

            decimal netSubtotal = Math.Max(0, grossSubtotal - itemDiscount);
            decimal ivaPercent = item.IvaRateId.HasValue && ivaRates.TryGetValue(item.IvaRateId.Value, out var rate) ? rate : 0m;
            decimal ivaAmount = Round2(netSubtotal * (ivaPercent / 100m));
            decimal lineTotal = Round2(netSubtotal + ivaAmount);

            // Same rule as SaveQuotation: ignoring a document line WITH value excludes it from
            // the integrity baseline — that exclusion requires its own per-line justification.
            if (item.ReconciliationStatus == RequestConstants.ReconciliationStatuses.Ignored && lineTotal > 0
                && string.IsNullOrWhiteSpace(item.ReconciliationJustification))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Justificativa Obrigatória",
                    Detail = $"Item {item.LineNumber}: uma linha ignorada com valor ({lineTotal:N2}) exige justificativa própria na reconciliação.",
                    Status = 400
                });
            }

            var existing = quotation.Items.FirstOrDefault(e =>
                (item.MappedRequestLineItemId.HasValue && e.MappedRequestLineItemId == item.MappedRequestLineItemId) ||
                (!item.MappedRequestLineItemId.HasValue && !e.MappedRequestLineItemId.HasValue && e.LineNumber == item.LineNumber)
            );

            if (existing != null)
            {
                existing.LineNumber = item.LineNumber;
                existing.Description = item.Description;
                existing.UnitId = item.UnitId;
                existing.ItemCatalogId = item.ItemCatalogId;
                existing.Quantity = item.Quantity;
                existing.UnitPrice = item.UnitPrice;
                existing.DiscountAmount = itemDiscount;
                existing.DiscountPercent = item.DiscountPercent;
                existing.IvaRateId = item.IvaRateId;
                existing.IvaRatePercent = ivaPercent;
                existing.GrossSubtotal = grossSubtotal;
                existing.IvaAmount = ivaAmount;
                existing.LineTotal = lineTotal;
                existing.MappedRequestLineItemId = item.MappedRequestLineItemId;
                existing.ReconciliationStatus = item.ReconciliationStatus;
                existing.ReconciliationJustification = item.ReconciliationJustification;
            }
            else
            {
                var newItem = new QuotationItem
                {
                    Id = Guid.NewGuid(),
                    QuotationId = quotation.Id,
                    LineNumber = item.LineNumber,
                    Description = item.Description,
                    UnitId = item.UnitId,
                    ItemCatalogId = item.ItemCatalogId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = itemDiscount,
                    DiscountPercent = item.DiscountPercent,
                    IvaRateId = item.IvaRateId,
                    IvaRatePercent = ivaPercent,
                    GrossSubtotal = grossSubtotal,
                    IvaAmount = ivaAmount,
                    LineTotal = lineTotal,
                    MappedRequestLineItemId = item.MappedRequestLineItemId,
                    ReconciliationStatus = item.ReconciliationStatus,
                    ReconciliationJustification = item.ReconciliationJustification
                };
                _context.QuotationItems.Add(newItem);
                quotation.Items.Add(newItem);
            }
        }

        // Only CONSIDERED lines compose the quotation totals — IGNORED lines are kept for audit
        // (with justification) but never add to the quotation value (same rule as SaveQuotation).
        var totalConsideredItems = quotation.Items
            .Where(i => RequestConstants.ReconciliationStatuses.Considered.Contains(i.ReconciliationStatus))
            .ToList();
        decimal totalGross = totalConsideredItems.Sum(i => i.GrossSubtotal);
        decimal sumItemDiscounts = totalConsideredItems.Sum(i => i.DiscountAmount);
        decimal totalItemIva = totalConsideredItems.Sum(i => i.IvaAmount);
        decimal netAfterItemDiscounts = Round2(Math.Max(0, totalGross - sumItemDiscounts));

        // Global (commercial) discount from DTO — reduces the taxable base further
        decimal globalDiscount = Round2(Math.Max(0, dto.DiscountAmount));
        decimal taxableBase = Round2(Math.Max(0, netAfterItemDiscounts - globalDiscount));

        // Proportionally adjust IVA based on the global discount reduction
        decimal discountRatio = netAfterItemDiscounts > 0 ? (taxableBase / netAfterItemDiscounts) : 1m;
        decimal adjustedIva = Round2(totalItemIva * discountRatio);

        quotation.DiscountAmount = globalDiscount;
        quotation.TotalGrossAmount = Round2(totalGross);
        quotation.TotalDiscountAmount = Round2(sumItemDiscounts + globalDiscount);
        quotation.TotalTaxableBase = taxableBase;
        quotation.TotalIvaAmount = adjustedIva;
        quotation.TotalAmount = Round2(taxableBase + adjustedIva);


        // Audit Trail entry for traceability
        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "QUOTATION_UPDATED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Cotação do fornecedor '{quotation.SupplierNameSnapshot}' (Doc: {quotation.DocumentNumber}) foi atualizada por {user.FullName}.",
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(history);

        try 
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var errLog = $"[ERROR] Concurrency error: {ex.Message}\n";
            foreach (var entry in ex.Entries)
            {
                errLog += $"[ERROR] Entity: {entry.Entity.GetType().Name}, State: {entry.State}\n";
            }
            System.IO.File.WriteAllText("c:\\dev\\alpla-portal\\src\\backend\\error.txt", errLog);
            throw;
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("c:\\dev\\alpla-portal\\src\\backend\\error.txt", $"[ERROR] Update failed: {ex.Message}");
            throw;
        }

        // RE-QUERY items with Units to ensure the response projection is complete
        var updatedItems = await _context.QuotationItems
            .Include(qi => qi.Unit)
            .Include(qi => qi.ItemCatalog)
            .Where(qi => qi.QuotationId == quotation.Id)
            .OrderBy(i => i.LineNumber)
            .ToListAsync();

        return Ok(new SavedQuotationDto
        {
            Id = quotation.Id,
            RequestId = quotation.RequestId,
            SupplierId = quotation.SupplierId,
            SupplierNameSnapshot = quotation.SupplierNameSnapshot,
            SupplierPortalCode = quotation.Supplier != null ? quotation.Supplier.PortalCode : null,
            SupplierPrimaveraCode = quotation.Supplier != null ? quotation.Supplier.PrimaveraCode : null,
            SupplierRegistrationStatus = quotation.Supplier != null ? quotation.Supplier.RegistrationStatus : null,
            DocumentNumber = quotation.DocumentNumber,
            DocumentDate = quotation.DocumentDate,
            Currency = quotation.Currency,
            TotalGrossAmount = quotation.TotalGrossAmount,
            TotalDiscountAmount = quotation.TotalDiscountAmount,
            TotalTaxableBase = quotation.TotalTaxableBase,
            DiscountAmount = quotation.DiscountAmount,
            TotalIvaAmount = quotation.TotalIvaAmount,
            TotalAmount = quotation.TotalAmount,
            SourceType = quotation.SourceType,
            SourceFileName = quotation.SourceFileName,
            ProformaAttachmentId = quotation.ProformaAttachmentId,
            CreatedAtUtc = quotation.CreatedAtUtc,
            ItemCount = updatedItems.Count,
            Items = updatedItems.Select(qi => new SavedQuotationItemDto
            {
                Id = qi.Id,
                LineNumber = qi.LineNumber,
                Description = qi.Description,
                Quantity = qi.Quantity,
                MappedRequestLineItemId = qi.MappedRequestLineItemId,
                ReconciliationStatus = qi.ReconciliationStatus,
                ReconciliationJustification = qi.ReconciliationJustification,
                UnitId = qi.UnitId,
                UnitName = qi.Unit?.Name,
                UnitCode = qi.Unit?.Code,
                UnitPrice = qi.UnitPrice,
                IvaRateId = qi.IvaRateId,
                IvaRatePercent = qi.IvaRatePercent,
                GrossSubtotal = qi.GrossSubtotal,
                IvaAmount = qi.IvaAmount,
                LineTotal = qi.LineTotal,
                ItemCatalogId = qi.ItemCatalogId,
                ItemCatalogCode = qi.ItemCatalog != null ? qi.ItemCatalog.Code : null
            }).ToList()
        });
    }

    [HttpDelete("{id}/quotations/{quotationId}")]
    public async Task<IActionResult> DeleteQuotation(Guid id, Guid quotationId)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
            .Include(r => r.Status)
            .FirstOrDefaultAsync(r => r.Id == id);
        
        if (request == null) return NotFound("Pedido não encontrado.");

        // Status Rule Check
        if (!RequestWorkflowHelper.CanMutateQuotation(request.Status!.Code))
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Ação Bloqueada", 
                Detail = "Não é possível excluir cotações neste status do pedido.", 
                Status = 409 
            });
        }

        var quotation = await _context.Quotations
            .Include(q => q.Items)
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.RequestId == id);

        if (quotation == null) return NotFound("Cotação não encontrada.");

        // Check if any quotation item is used in an approval batch
        var quotationItemIds = quotation.Items.Select(i => i.Id).ToList();
        var isReferencedByBatch = await _context.Set<ApprovalBatchItem>()
            .AnyAsync(abi => quotationItemIds.Contains(abi.SelectedQuotationItemId));

        if (isReferencedByBatch)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Ação Bloqueada", 
                Detail = "Esta cotação já foi utilizada em um lote de aprovação e não pode ser excluída. Faça um reajuste ou adicione uma nova revisão/cotação.", 
                Status = 409 
            });
        }

        if (quotation.ProformaAttachmentId.HasValue)
        {
            var proforma = await _context.RequestAttachments.FindAsync(quotation.ProformaAttachmentId.Value);
            if (proforma != null)
            {
                proforma.IsDeleted = true;
                
                // Keep an audit history of the linked document deletion
                _context.RequestStatusHistories.Add(new RequestStatusHistory
                {
                    Id = Guid.NewGuid(),
                    RequestId = id,
                ActorUserId = actorId,
                    ActionTaken = "DOCUMENTO_REMOVIDO",
                    PreviousStatusId = request.StatusId,
                    NewStatusId = request.StatusId,
                    Comment = $"Proforma associada à cotação do fornecedor '{quotation.SupplierNameSnapshot}' removida automaticamente.",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        _context.QuotationItems.RemoveRange(quotation.Items);
        _context.Quotations.Remove(quotation);

        // Audit Trail
        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = actorId,
            ActionTaken = "QUOTATION_DELETED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Cotação do fornecedor '{quotation.SupplierNameSnapshot}' (Doc: {quotation.DocumentNumber}) foi excluída por {user.FullName}.",
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(history);

        await _context.SaveChangesAsync();

        return NoContent();
    }
    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> DuplicateRequest(Guid id)
    {
        var actorId = CurrentUserId;

        // 1. Get original request
        var originalRequest = await _context.Requests
            .Include(r => r.LineItems)
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .AsNoTracking() // Prevent EF conflicts when cloning
            .FirstOrDefaultAsync(r => r.Id == id);

        if (originalRequest == null) return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // 2. Prepare new DRAFT status and Number
        var draftStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == "DRAFT");
        if (draftStatus == null) return StatusCode(500, "DRAFT status not found.");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Fix #1: Use the same numbering system as CreateRequest (GLOBAL_REQUEST_COUNTER)
            var counterKey = "GLOBAL_REQUEST_COUNTER";
            var dateStr = DateTime.UtcNow.Date.ToString("dd/MM/yyyy");
            var counter = await _context.SystemCounters.FirstOrDefaultAsync(c => c.Id == counterKey);
            int seqNumber;

            if (counter == null)
            {
                seqNumber = 1;
                counter = new SystemCounter { Id = counterKey, CurrentValue = seqNumber, LastUpdatedUtc = DateTime.UtcNow };
                _context.SystemCounters.Add(counter);
            }
            else
            {
                counter.CurrentValue++;
                counter.LastUpdatedUtc = DateTime.UtcNow;
                seqNumber = counter.CurrentValue;
            }
            var newRequestNumber = $"REQ-{dateStr}-{seqNumber:D3}";

            // Determine request type for conditional field handling throughout duplication
            bool isQuotationRequest = originalRequest.RequestType!.Code == "QUOTATION";

            // 3. Create new Request (strictly copying structure only)
            var newRequest = new Request
            {
                Id = Guid.NewGuid(),
                RequestNumber = newRequestNumber,
                Title = $"{originalRequest.Title} (Cópia)",
                Description = originalRequest.Description,
                RequestTypeId = originalRequest.RequestTypeId,
                StatusId = draftStatus.Id,
                RequesterId = actorId, // The user performing the action is the new requester
                NeedLevelId = originalRequest.NeedLevelId,
                DepartmentId = originalRequest.DepartmentId,
                CompanyId = originalRequest.CompanyId,
                PlantId = null, // Phasing out request-level plant
                // Fix #2 (cancel visible): For QUOTATION, do NOT copy request-level Supplier.
                // canCancelRequest in RequestEdit checks formData.supplierId; BuyerItemsList checks group.requestSupplierId.
                // A copied QUOTATION request must start without a pre-assigned supplier.
                // For PAYMENT requests, supplier is structural and must be preserved.
                SupplierId = isQuotationRequest ? null : originalRequest.SupplierId,
                RequestedDateUtc = DateTime.UtcNow,
                // Fix #3: NeedByDate intentionally reset - user must review/re-enter it
                NeedByDateUtc = null,
                CurrencyId = originalRequest.CurrencyId,
                CapexOpexClassificationId = originalRequest.CapexOpexClassificationId,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actorId,
                
                // Explicit resets (DO NOT COPY WORKFLOW STATE)
                BuyerId = null,
                AreaApproverId = null,
                FinalApproverId = null,
                CurrentResponsibleRole = null,
                CurrentResponsibleUserId = null,
                EstimatedTotalAmount = 0, // Recalculated below
                IsCancelled = false,
                SubmittedAtUtc = null,
                UpdatedAtUtc = null,
                UpdatedByUserId = null
            };

            // 4. Copy Line Items
            var activeItems = originalRequest.LineItems.Where(li => !li.IsDeleted).ToList();
            decimal newTotalAmount = 0;
            var cloneIvaRates = await _context.IvaRates.AsNoTracking().ToListAsync();

            foreach (var item in activeItems)
            {
                var netClone = Round2((item.Quantity * item.UnitPrice) - (item.DiscountAmount ?? 0));
                var ivaClone = item.IvaRateId.HasValue ? cloneIvaRates.FirstOrDefault(r => r.Id == item.IvaRateId.Value) : null;
                var ivaAmountClone = ivaClone != null ? Round2(netClone * (ivaClone.RatePercent / 100m)) : 0m;
                var computedItemTotal = Round2(netClone + ivaAmountClone);
                newTotalAmount += computedItemTotal;

                var newItem = new RequestLineItem
                {
                    Id = Guid.NewGuid(),
                    RequestId = newRequest.Id,
                    LineNumber = item.LineNumber,
                    ItemPriority = item.ItemPriority,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitId = item.UnitId,
                    UnitPrice = item.UnitPrice,
                    DiscountPercent = item.DiscountPercent,
                    DiscountAmount = item.DiscountAmount,
                    TotalAmount = computedItemTotal,
                    CurrencyId = item.CurrencyId,
                    PlantId = item.PlantId,
                    // For QUOTATION: do NOT inherit supplier - items start fresh for buyer assignment
                    // For PAYMENT: preserve supplier inheritance (it's structural, not workflow state)
                    SupplierId = isQuotationRequest ? null : item.SupplierId,
                    SupplierName = isQuotationRequest ? null : item.SupplierName,
                    Notes = item.Notes,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedByUserId = actorId,
                    
                    // Fix #4 (items editable): QUOTATION items must start with LineItemStatusId=1 (WAITING_QUOTATION)
                    // so their status dropdown shows correct initial state in BuyerItemsList.
                    // Leaving it null causes the <select> to render blank/unusable.
                    // PAYMENT items can remain null as they don't use item-level status selects initially.
                    LineItemStatusId = isQuotationRequest ? 1 : (int?)null,
                    CostCenterId = null,
                    IsDeleted = false
                };
                newRequest.LineItems.Add(newItem);
            }

            newRequest.EstimatedTotalAmount = newTotalAmount;

            // 5. Add clean initial history
            newRequest.StatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = newRequest.Id,
                NewStatusId = draftStatus.Id,
                ActorUserId = actorId,
                ActionTaken = "DUPLICATE",
                CreatedAtUtc = DateTime.UtcNow,
                Comment = "Pedido criado a partir de cópia."
            });

            _context.Requests.Add(newRequest);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = new CreateRequestDraftResponseDto
            {
                Id = newRequest.Id,
                Title = newRequest.Title,
                StatusCode = draftStatus.Code,
                CreatedAtUtc = newRequest.CreatedAtUtc
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, $"Erro ao duplicar o pedido: {ex.Message}");
        }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelRequest(Guid id, [FromQuery] string? mode, [FromBody] CancelRequestDto dto)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.LineItems).ThenInclude(li => li.LineItemStatus)
            .Include(r => r.Attachments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        var currentStatusCode = request.Status!.Code;
        if (request.IsCancelled || currentStatusCode == "CANCELLED" || currentStatusCode == "COMPLETED" || currentStatusCode == "REJECTED")
        {
            return Conflict(new ProblemDetails { Title = "Regra de Negócio Violada", Detail = "O pedido já está num estado final e não pode ser cancelado.", Status = 409 });
        }

        bool isBuyer = mode?.ToUpper() == "BUYER";
        bool isQuotation = request.RequestType!.Code == "QUOTATION";
        bool isPayment = request.RequestType!.Code == "PAYMENT";

        if (isQuotation)
        {
            if (currentStatusCode != "DRAFT" && currentStatusCode != "WAITING_QUOTATION")
            {
                return Conflict(new ProblemDetails { Title = "Regra de Negócio Violada", Detail = "Apenas pedidos em rascunho ou aguardando cotação podem ser cancelados.", Status = 409 });
            }

            if (currentStatusCode == "WAITING_QUOTATION")
            {
                bool hasBuyerProcessing = request.SupplierId.HasValue || 
                    request.Attachments.Any(a => a.AttachmentTypeCode == "PROFORMA" && !a.IsDeleted) || 
                    request.LineItems.Any(li => !li.IsDeleted && (li.SupplierId.HasValue || !string.IsNullOrEmpty(li.SupplierName) || (li.LineItemStatus != null && li.LineItemStatus.Code != "WAITING_QUOTATION" && li.LineItemStatus.Code != "PENDING")));

                if (hasBuyerProcessing)
                {
                    return Conflict(new ProblemDetails { Title = "Regra de Negócio Violada", Detail = "O pedido já foi processado pelo comprador (fornecedor definido, proforma anexada ou itens atualizados) e não pode ser cancelado.", Status = 409 });
                }
            }
            if (isBuyer && currentStatusCode != "WAITING_QUOTATION")
            {
                return Conflict(new ProblemDetails { Title = "Regra de Negócio Violada", Detail = "O comprador só pode cancelar pedidos neste momento que estejam aguardando cotação.", Status = 409 });
            }
        }
        else if (isPayment)
        {
            if (isBuyer)
            {
                 return Conflict(new ProblemDetails { Title = "Regra de Negócio Violada", Detail = "O comprador não tem permissão para cancelar pedidos de pagamento.", Status = 409 });
            }

            var allowedStatuses = new[] { "DRAFT", "WAITING_AREA_APPROVAL", "AREA_ADJUSTMENT", "WAITING_FINAL_APPROVAL", "FINAL_ADJUSTMENT", "WAITING_COST_CENTER", "APPROVED" };
            if (!allowedStatuses.Contains(currentStatusCode))
            {
                return Conflict(new ProblemDetails { Title = "Regra de Negócio Violada", Detail = "O pedido de pagamento já avançou para processamento operacional e não pode ser cancelado.", Status = 409 });
            }

            if (request.Attachments.Any(a => (a.AttachmentTypeCode == "PO" || a.AttachmentTypeCode == "PAYMENT_SCHEDULE" || a.AttachmentTypeCode == "PAYMENT_PROOF") && !a.IsDeleted))
            {
                return Conflict(new ProblemDetails { Title = "Regra de Negócio Violada", Detail = "O pedido possui evidências de processamento operacional (documentos anexados) e não pode ser cancelado.", Status = 409 });
            }
        }

        // Apply IsCancelled boolean natively
        request.IsCancelled = true;

        var historyComment = $"Pedido cancelado por {user.FullName}. Motivo: {dto.Reason}";

        return await ApplyStatusChangeAndSyncItemsAsync(request, "CANCELLED", "CANCELLED", historyComment, "Pedido cancelado com sucesso.", actorId);
    }

    [HttpPost("validate-line")]
    public async Task<IActionResult> ValidateLine([FromBody] RequestLineValidationInputDto dto)
    {
        if (dto == null) return BadRequest("Missing request body");

        // 1) Translate CompanyId -> PrimaveraCompany code
        var company = await _context.Companies.FindAsync(dto.CompanyId);
        if (company == null) 
        {
            return Ok(new PrimaveraRequestValidationResultDto 
            {
                ValidationStatus = "WARNING",
                Messages = new List<string> { "Companhia não identificada. Selecione uma companhia válida para validação." },
                Source = "PORTAL"
            });
        }
        string primaveraCompany = company.Name.Contains("SOPRO", StringComparison.OrdinalIgnoreCase) ? "ALPLASOPRO" : "ALPLAPLASTICO";

        // 2) Translate SupplierId -> SupplierCode
        string? supplierCode = null;
        if (dto.SupplierId.HasValue)
        {
            var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId.Value && s.IsActive);
            if (supplier != null && !string.IsNullOrEmpty(supplier.PrimaveraCode))
            {
                supplierCode = supplier.PrimaveraCode;
            }
        }

        var input = new PrimaveraRequestValidationInputDto
        {
            Company = primaveraCompany,
            ArticleCode = dto.ItemCatalogCode,
            SupplierCode = supplierCode
        };

        try
        {
            var result = await _primaveraValidationService.ValidateRequestLineAsync(input);

            // Post-process logic for missing supplier if needed
            if (!dto.SupplierId.HasValue)
            {
                // The Primavera layer checks this and correctly sets "WARNING" with "Apenas verificação do artigo efetuada."
                // But let's append a more user-friendly help text.
                if (!result.Messages.Any(m => m.Contains("fornecedor não selecionado")))
                {
                    result.Messages.Add("Validação parcial (nenhum fornecedor selecionado ainda).");
                }
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Primavera validation for article code: {ArticleCode}", dto.ItemCatalogCode);
            return Ok(new PrimaveraRequestValidationResultDto
            {
                ValidationStatus = "ERROR",
                Messages = new List<string> { "Serviço do Primavera indisponível no momento. Não foi possível validar o artigo." },
                Source = "PORTAL"
            });
        }
    }

    [HttpPost("{requestId:guid}/line-items")]
    public async Task<IActionResult> AddLineItem(Guid requestId, [FromBody] CreateRequestLineItemDto dto)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.LineItems)
            .Include(r => r.Quotations)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        if (request.Status!.Code == "WAITING_QUOTATION" && request.Quotations.Any())
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Ação Bloqueada", 
                Detail = "Não é possível adicionar itens pois já existem cotações salvas para este pedido.", 
                Status = 409 
            });
        }

        if (request.Status!.Code != "DRAFT" && request.Status!.Code != "AREA_ADJUSTMENT" && request.Status!.Code != "FINAL_ADJUSTMENT" && request.Status!.Code != "WAITING_QUOTATION")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Operação bloqueada: este pedido não está em rascunho nem em fase de reajuste/cotação, por isso não é possível adicionar itens.", 
                Status = 409 
            });
        }

        // Creator-only edit enforcement for non-DRAFT statuses
        if (request.Status!.Code != "DRAFT" && request.RequesterId != actorId)
        {
            return StatusCode(403, new ProblemDetails 
            { 
                Title = "Acesso Proibido", 
                Detail = "Apenas o criador do pedido pode adicionar itens ao pedido nesta fase.", 
                Status = 403 
            });
        }
        var unit = await _context.Units.FindAsync(dto.UnitId);

        // Item Plant Validation: Mandatory for all types EXCEPT Payment (DEC-076)
        if (request.RequestType?.Code != "PAYMENT" && !dto.PlantId.HasValue)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Erro de Validação",
                Detail = "A planta de destino é obrigatória para este tipo de pedido.",
                Status = 400
            });
        }

        if (request.RequestType?.Code == "PAYMENT" && !dto.DueDate.HasValue)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Erro de Validação",
                Detail = "A Data de Vencimento é obrigatória para itens de pagamento.",
                Status = 400
            });
        }

        if (unit != null && !unit.AllowsDecimalQuantity && dto.Quantity.HasValue && dto.Quantity.Value % 1 != 0)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = $"A unidade '{unit.Code}' não permite quantidades fracionadas (decimais).",
                Status = 409
            });
        }

        // Cross-Company Plant Validation
        if (dto.PlantId.HasValue)
        {
            var plant = await _context.Plants.FindAsync(dto.PlantId.Value);
            if (plant == null) return BadRequest("Planta inválida.");
            if (plant.CompanyId != request.CompanyId)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Regra de Negócio Violada",
                    Detail = "A planta selecionada deve pertencer à mesma empresa do pedido.",
                    Status = 400
                });
            }
        }

        // Cost Center / Plant consistency check (per item — DEC-078)
        if (dto.CostCenterId.HasValue && dto.PlantId.HasValue)
        {
            var cc = await _context.CostCenters.FindAsync(dto.CostCenterId.Value);
            if (cc == null || cc.PlantId != dto.PlantId.Value)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Regra de Negócio Violada",
                    Detail = "O Centro de Custo selecionado não pertence à planta de destino do item.",
                    Status = 400
                });
            }
        }

        // Build + stage the item and its history through the shared factory
        // (same total/status/line-number math as before; single source of truth
        // shared with the buyer reconciliation workaround).
        var newItem = await _lineItemFactory.BuildAndStageAsync(request, new LineItemCreationSpec
        {
            Description = dto.Description,
            Quantity = dto.Quantity ?? 0,
            UnitId = dto.UnitId,
            UnitPrice = dto.UnitPrice ?? 0,
            CurrencyId = dto.CurrencyId,
            PlantId = dto.PlantId,
            CostCenterId = dto.CostCenterId,
            DiscountPercent = dto.DiscountPercent,
            DiscountAmount = dto.DiscountAmount,
            IvaRateId = dto.IvaRateId,
            ItemCatalogId = dto.ItemCatalogId,
            SupplierName = dto.SupplierName,
            Notes = dto.Notes,
            DueDate = dto.DueDate,
            ItemPriority = dto.ItemPriority ?? "MEDIUM"
        }, actorId, user.FullName);

        await _context.SaveChangesAsync();

        // Recalculate total from DB AFTER saving (accounts for global discount)
        await RecalculateEstimatedTotalAsync(request, requestId);
        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;

        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRequest), new { id = request.Id }, new { ItemId = newItem.Id });
    }

    /// <summary>
    /// Buyer reconciliation workaround: create a *requested* line item from a proforma line to cover
    /// an omitted item (or an old item-less request). Distinct from EXTRA_ITEM — the item is created
    /// immediately as QUOTATION_PENDING and does not depend on approver acceptance.
    ///
    /// Conservative scope (backend-enforced): QUOTATION requests in WAITING_QUOTATION only, no active
    /// approval batch, Buyer role. Two dedup layers: (1) same-operation idempotency via a client UUID
    /// persisted under a unique index; (2) cross-session probable-duplicate detection via persisted
    /// provenance (proforma attachment + normalized description/quantity/unit).
    /// </summary>
    [HttpPost("{requestId:guid}/line-items/from-proforma")]
    public async Task<IActionResult> AddLineItemFromProforma(Guid requestId, [FromBody] CreateLineItemFromProformaDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        // Authorization: this workaround is a Buyer action.
        if (!CurrentUserRoles.Contains(RoleConstants.Buyer))
            return StatusCode(403, new ProblemDetails { Title = "Acesso Proibido", Detail = "Apenas o papel de Comprador pode adicionar itens solicitados durante a conciliação.", Status = 403 });

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.LineItems)
            .Include(r => r.ApprovalBatches)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // State guards (conservative initial scope).
        if (request.RequestType?.Code != RequestConstants.Types.Quotation)
            return Conflict(new ProblemDetails { Title = "Ação Bloqueada", Detail = "Este recurso está disponível apenas para pedidos de Cotação.", Status = 409 });

        if (request.Status?.Code != RequestConstants.Statuses.WaitingQuotation)
            return Conflict(new ProblemDetails { Title = "Ação Bloqueada", Detail = "Só é possível adicionar itens solicitados enquanto o pedido está em cotação (Aguardando Cotação).", Status = 409 });

        // Block when an approval batch is still active (partial approval in flight) — do not risk
        // interfering with amounts already under review/approval.
        var activeBatchStatuses = new[]
        {
            RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
            RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
            RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
            RequestConstants.ApprovalBatchStatuses.FinalAdjustment
        };
        if (request.ApprovalBatches.Any(b => activeBatchStatuses.Contains(b.Status)))
            return Conflict(new ProblemDetails { Title = "Ação Bloqueada", Detail = "Existe um lote de aprovação ativo para este pedido. Conclua ou cancele o lote antes de adicionar novos itens solicitados.", Status = 409 });

        var quantity = dto.Quantity ?? 0;
        var normalizedDesc = NormalizeForDedup(dto.Description);

        // ── Dedup layer 1: same-operation idempotency (client UUID + unique index), SCOPED to this request ──
        // The lookup is scoped by RequestId so a key can never resolve to an item of another request.
        var byKey = await _context.RequestLineItems
            .FirstOrDefaultAsync(li => li.RequestId == requestId && li.CreationIdempotencyKey == dto.IdempotencyKey);
        if (byKey != null)
            return Ok(new { itemId = byKey.Id, deduplicated = true, reason = "IDEMPOTENT_RETRY", item = ProjectReconciliationItem(byKey) });

        // ── Dedup layer 2: cross-session probable-duplicate detection (persisted provenance) ──
        var siblings = await _context.RequestLineItems
            .Where(li => li.RequestId == requestId && !li.IsDeleted)
            .ToListAsync();

        // Unambiguous: same proforma attachment + normalized description + qty + unit → dedupe silently.
        if (dto.SourceProformaAttachmentId.HasValue)
        {
            var exact = siblings.FirstOrDefault(li =>
                li.SourceProformaAttachmentId == dto.SourceProformaAttachmentId &&
                li.UnitId == dto.UnitId &&
                li.Quantity == quantity &&
                NormalizeForDedup(li.Description) == normalizedDesc);
            if (exact != null)
                return Ok(new { itemId = exact.Id, deduplicated = true, reason = "UNAMBIGUOUS_DUPLICATE", item = ProjectReconciliationItem(exact) });
        }

        // Probable: same normalized description + qty + unit (regardless of proforma) → require confirmation.
        if (!dto.ConfirmCreateDespiteDuplicate)
        {
            var probable = siblings.FirstOrDefault(li =>
                li.UnitId == dto.UnitId &&
                li.Quantity == quantity &&
                NormalizeForDedup(li.Description) == normalizedDesc);
            if (probable != null)
            {
                return Conflict(new
                {
                    title = "Possível item duplicado",
                    detail = "Já existe um item semelhante neste pedido. Utilize o item existente ou confirme a criação de um novo.",
                    duplicateSuspected = true,
                    existingItemId = probable.Id,
                    existingDescription = probable.Description,
                    existingLineNumber = probable.LineNumber,
                    existingItem = ProjectReconciliationItem(probable)
                });
            }
        }

        // ── Create via shared factory ──
        var newItem = await _lineItemFactory.BuildAndStageAsync(request, new LineItemCreationSpec
        {
            Description = dto.Description.Trim(),
            Quantity = quantity,
            UnitId = dto.UnitId,
            UnitPrice = 0m, // conservative: never adopt the proforma price as the requested value
            PlantId = request.PlantId,
            ItemCatalogId = dto.ItemCatalogId,
            ItemPriority = "MEDIUM",
            QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationPending,
            CreationOrigin = LineItemCreationOrigins.BuyerReconciliation,
            SourceProformaAttachmentId = dto.SourceProformaAttachmentId,
            CreationIdempotencyKey = dto.IdempotencyKey,
            HistoryAction = LineItemHistoryActions.ItemAddedFromProforma,
            HistoryComment = $"Item solicitado \"{dto.Description.Trim()}\" (Qtd: {quantity}) criado a partir da proforma durante a conciliação por {user.FullName}."
        }, actorId, user.FullName);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message?.Contains("IX_RequestLineItems_RequestId_CreationIdempotencyKey", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Race: a concurrent operation with the same (RequestId, idempotency key) won. Return the winner.
            _context.Entry(newItem).State = EntityState.Detached;
            var winner = await _context.RequestLineItems.AsNoTracking()
                .FirstOrDefaultAsync(li => li.RequestId == requestId && li.CreationIdempotencyKey == dto.IdempotencyKey);
            if (winner != null)
                return Ok(new { itemId = winner.Id, deduplicated = true, reason = "IDEMPOTENT_RACE", item = ProjectReconciliationItem(winner) });
            throw;
        }

        // Recalculate total from DB AFTER saving (the new line has UnitPrice 0, so it does not inflate).
        await RecalculateEstimatedTotalAsync(request, requestId);
        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRequest), new { id = request.Id }, new { itemId = newItem.Id, deduplicated = false, item = ProjectReconciliationItem(newItem) });
    }

    /// <summary>Compact projection of a line item for the reconciliation wizard to update its local state.</summary>
    private static object ProjectReconciliationItem(RequestLineItem li) => new
    {
        id = li.Id,
        lineNumber = li.LineNumber,
        description = li.Description,
        quantity = li.Quantity,
        unitId = li.UnitId,
        unitPrice = li.UnitPrice,
        itemCatalogId = li.ItemCatalogId,
        quotationLifecycleStatus = li.QuotationLifecycleStatus
    };

    /// <summary>
    /// Normalizes a description for duplicate comparison: trim, lowercase, strip accents,
    /// collapse whitespace, drop trailing punctuation. Mirrors the catalog matcher's approach.
    /// </summary>
    private static string NormalizeForDedup(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var lowered = s.Trim().ToLowerInvariant();
        var decomposed = lowered.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        var noAccents = sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        return System.Text.RegularExpressions.Regex.Replace(noAccents, @"\s+", " ").TrimEnd('.', ',', ';', ':', '!');
    }

    [HttpPut("{requestId}/line-items/{itemId}")]
    public async Task<IActionResult> UpdateLineItem(Guid requestId, Guid itemId, [FromBody] UpdateRequestLineItemDto dto)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.LineItems)
            .Include(r => r.Quotations)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        if (request.Status!.Code == "WAITING_QUOTATION" && request.Quotations.Any())
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Ação Bloqueada", 
                Detail = "Não é possível editar itens pois já existem cotações salvas para este pedido.", 
                Status = 409 
            });
        }

        if (request.Status!.Code != "DRAFT" && request.Status!.Code != "AREA_ADJUSTMENT" && request.Status!.Code != "FINAL_ADJUSTMENT" && request.Status!.Code != "WAITING_QUOTATION")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Operação bloqueada: este pedido não está em rascunho nem em fase de reajuste/cotação, por isso não é possível editar itens.", 
                Status = 409 
            });
        }

        // Creator-only edit enforcement for non-DRAFT statuses
        if (request.Status!.Code != "DRAFT" && request.RequesterId != actorId)
        {
            return StatusCode(403, new ProblemDetails 
            { 
                Title = "Acesso Proibido", 
                Detail = "Apenas o criador do pedido pode editar itens do pedido nesta fase.", 
                Status = 403 
            });
        }

        var item = request.LineItems.FirstOrDefault(l => l.Id == itemId && !l.IsDeleted);
        if (item == null) return NotFound(new ProblemDetails { Title = "Item não encontrado no pedido.", Status = 404 });

        var unit = await _context.Units.FindAsync(dto.UnitId);

        // Item Plant Validation: Mandatory for all types EXCEPT Payment (DEC-076)
        if (request.RequestType?.Code != "PAYMENT" && !dto.PlantId.HasValue)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Erro de Validação",
                Detail = "A planta de destino é obrigatória para este tipo de pedido.",
                Status = 400
            });
        }

        if (request.RequestType?.Code == "PAYMENT" && !dto.DueDate.HasValue)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Erro de Validação",
                Detail = "A Data de Vencimento é obrigatória para itens de pagamento.",
                Status = 400
            });
        }

        if (unit != null && !unit.AllowsDecimalQuantity && dto.Quantity.HasValue && dto.Quantity.Value % 1 != 0)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = $"A unidade '{unit.Code}' não permite quantidades fracionadas (decimais).",
                Status = 409
            });
        }

        // Validate and normalize ItemPriority — backend enforces valid codes
        var validPriorities = new[] { "HIGH", "MEDIUM", "LOW" };
        var itemPriority = validPriorities.Contains(dto.ItemPriority?.ToUpper()) ? dto.ItemPriority!.ToUpper() : "MEDIUM";

        item.Description = dto.Description;
        item.ItemPriority = itemPriority;
        item.Quantity = dto.Quantity ?? item.Quantity;
        item.UnitId = dto.UnitId;
        item.UnitPrice = dto.UnitPrice ?? item.UnitPrice;
        item.DiscountPercent = dto.DiscountPercent ?? item.DiscountPercent;
        item.DiscountAmount = dto.DiscountAmount ?? item.DiscountAmount;
        var updateNet = Round2((item.Quantity * item.UnitPrice) - (item.DiscountAmount ?? 0));
        var updateIvaRate = item.IvaRateId.HasValue ? await _context.IvaRates.FindAsync(item.IvaRateId.Value) : null;
        var updateIvaAmount = updateIvaRate != null ? Round2(updateNet * (updateIvaRate.RatePercent / 100m)) : 0m;
        item.TotalAmount = Round2(updateNet + updateIvaAmount);
        item.CurrencyId = (request.RequestType?.Code == "QUOTATION" && dto.CurrencyId.HasValue) ? dto.CurrencyId : request.CurrencyId;
        
        // Cross-Company Plant Validation
        if (dto.PlantId.HasValue)
        {
            var plant = await _context.Plants.FindAsync(dto.PlantId.Value);
            if (plant == null) return BadRequest("Planta inválida.");
            if (plant.CompanyId != request.CompanyId)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Regra de Negócio Violada",
                    Detail = "A planta selecionada deve pertencer à mesma empresa do pedido.",
                    Status = 400
                });
            }
            item.PlantId = dto.PlantId;
        }

        // Cost Center / Plant consistency check (per item — DEC-078)
        if (dto.CostCenterId.HasValue && dto.PlantId.HasValue)
        {
            var cc = await _context.CostCenters.FindAsync(dto.CostCenterId.Value);
            if (cc == null || cc.PlantId != dto.PlantId.Value)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Regra de Negócio Violada",
                    Detail = "O Centro de Custo selecionado não pertence à planta de destino do item.",
                    Status = 400
                });
            }
        }

        item.CostCenterId = dto.CostCenterId;
        item.IvaRateId = dto.IvaRateId;
        item.DueDate = dto.DueDate;

        // LineItemStatusId is intentionally NOT updated here — status is backend/buyer-controlled only
        if (request.RequestType?.Code == "PAYMENT")
        {
            item.SupplierId = request.SupplierId;
            item.SupplierName = null;
        }
        else
        {
            item.SupplierName = dto.SupplierName;
        }
        item.Notes = dto.Notes;
        item.ItemCatalogId = dto.ItemCatalogId;
        
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedByUserId = actorId;

        // Record item update in history
        var itemHistory = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "ITEM_UPDATED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Item #{item.LineNumber} (\"{item.Description}\") alterado por {user.FullName}.",
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(itemHistory);

        await _context.SaveChangesAsync();

        // Recalculate total from DB AFTER saving (accounts for global discount)
        await RecalculateEstimatedTotalAsync(request, requestId);
        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{requestId}/line-items/{itemId}")]
    public async Task<IActionResult> DeleteLineItem(Guid requestId, Guid itemId)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .Include(r => r.Quotations)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return NotFound();

        if (request.Status!.Code == "WAITING_QUOTATION" && request.Quotations.Any())
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Ação Bloqueada", 
                Detail = "Não é possível excluir itens pois já existem cotações salvas para este pedido.", 
                Status = 409 
            });
        }

        if (request.Status!.Code != "DRAFT" && request.Status!.Code != "AREA_ADJUSTMENT" && request.Status!.Code != "FINAL_ADJUSTMENT" && request.Status!.Code != "WAITING_QUOTATION")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Operação bloqueada: este pedido não está em rascunho nem em fase de reajuste/cotação, por isso não é possível excluir itens.", 
                Status = 409 
            });
        }

        // Creator-only edit enforcement for non-DRAFT statuses
        if (request.Status!.Code != "DRAFT" && request.RequesterId != actorId)
        {
            return StatusCode(403, new ProblemDetails 
            { 
                Title = "Acesso Proibido", 
                Detail = "Apenas o criador do pedido pode excluir itens do pedido nesta fase.", 
                Status = 403 
            });
        }

        var item = request.LineItems.FirstOrDefault(l => l.Id == itemId && !l.IsDeleted);
        if (item == null) return NotFound();

        item.IsDeleted = true;
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedByUserId = actorId;

        // Record item deletion in history
        var itemHistory = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "ITEM_REMOVED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Item #{item.LineNumber} (\"{item.Description}\") removido do pedido por {user.FullName}.",
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(itemHistory);

        await _context.SaveChangesAsync();

        // Recalculate total from DB AFTER saving (accounts for global discount)
        await RecalculateEstimatedTotalAsync(request, requestId);
        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRequest(Guid id)
    {
        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .Include(r => r.Attachments)
            .Include(r => r.StatusHistories)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        // Safety: Only DRAFT requests can be hard-deleted in V1
        if (request.Status!.Code != "DRAFT")
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = "Não é possível excluir um pedido que já foi submetido. Apenas rascunhos (DRAFT) podem ser excluídos permanentemente.",
                Status = 409
            });
        }

        // Handle Restrict delete on StatusHistories
        if (request.StatusHistories.Any())
        {
            _context.RequestStatusHistories.RemoveRange(request.StatusHistories);
        }

        // If this request is linked to a contract payment obligation, we must reset the obligation status
        if (request.ContractPaymentObligationId.HasValue)
        {
            var obligation = await _context.ContractPaymentObligations
                .FirstOrDefaultAsync(o => o.Id == request.ContractPaymentObligationId.Value);

            if (obligation != null)
            {
                var oldStatus = obligation.StatusCode;
                obligation.StatusCode = ContractConstants.ObligationStatuses.Pending;
                
                _context.ContractHistories.Add(new ContractHistory
                {
                    ContractId = obligation.ContractId,
                    EventType = ContractConstants.HistoryEventTypes.StatusChanged,
                    FromStatusCode = oldStatus,
                    ToStatusCode = ContractConstants.ObligationStatuses.Pending,
                    Comment = $"Obrigação reaberta após obliteração/exclusão do pedido base rascunho ({request.RequestNumber}).",
                    OccurredAtUtc = DateTime.UtcNow,
                    ActorUserId = CurrentUserId
                });
            }
        }

        // LineItems and Attachments will cascade delete based on EntityConfigurations.cs
        _context.Requests.Remove(request);

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/area-approval/approve")]
    public async Task<IActionResult> ApproveArea(Guid id, [FromBody] ApprovalActionDto dto)
    {
        return await ProcessAreaApproval(id, "APPROVE", "WAITING_FINAL_APPROVAL", dto);
    }

    [HttpPost("{id}/area-approval/reject")]
    public async Task<IActionResult> RejectArea(Guid id, [FromBody] ApprovalActionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "Informe o motivo da rejeição.", Status = 400 });

        return await ProcessAreaApproval(id, "REJECT", "REJECTED", dto, WorkflowEventCodes.AreaRejected);
    }

    [HttpPost("{id}/area-approval/request-adjustment")]
    public async Task<IActionResult> RequestAdjustmentArea(Guid id, [FromBody] ApprovalActionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "Informe o motivo do reajuste.", Status = 400 });

        return await ProcessAreaApproval(id, "REQUEST_ADJUSTMENT", "AREA_ADJUSTMENT", dto, WorkflowEventCodes.AreaAdjustment);
    }

    [HttpPost("{id}/final-approval/approve")]
    public async Task<IActionResult> ApproveFinal(Guid id, [FromBody] ApprovalActionDto dto)
    {
        return await ProcessFinalApproval(id, "APPROVE", "APPROVED", dto.Comment);
    }

    [HttpPost("{id}/final-approval/reject")]
    public async Task<IActionResult> RejectFinal(Guid id, [FromBody] ApprovalActionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "Informe o motivo da rejeição.", Status = 400 });

        return await ProcessFinalApproval(id, "REJECT", "REJECTED", dto.Comment, WorkflowEventCodes.FinalRejected);
    }

    [HttpPost("{id}/final-approval/request-adjustment")]
    public async Task<IActionResult> RequestAdjustmentFinal(Guid id, [FromBody] ApprovalActionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "Informe o motivo do reajuste.", Status = 400 });

        // Semantic grounding: FINAL_ADJUSTMENT internal code strictly represents "REAJUSTE A.F" in this stage
        return await ProcessFinalApproval(id, "REQUEST_ADJUSTMENT", "FINAL_ADJUSTMENT", dto.Comment, WorkflowEventCodes.FinalAdjustment);
    }

    private async Task<IActionResult> ProcessFinalApproval(Guid id, string action, string targetStatusCode, string? comment, string? overrideEventCode = null)
    {
        var actorId = CurrentUserId;

        // Role-based Authorization: strictly enforce Final Approver role
        if (!CurrentUserRoles.Contains(RoleConstants.FinalApprover))
            return StatusCode(403, "Apenas o papel de Aprovador Final pode realizar aprovações nesta etapa.");

        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        // Business Rule: Final Approval applies to both PAYMENT and QUOTATION flows
        if (request.RequestType!.Code != "PAYMENT" && request.RequestType!.Code != "QUOTATION")
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Esta ação só é permitida para pedidos de Pagamento ou Cotação.", Status = 400 });

        if (request.Status!.Code != "WAITING_FINAL_APPROVAL")
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "O pedido não está em fase de aprovação final.", Status = 400 });

        if (request.RequestType!.Code == "PAYMENT" && action == "REQUEST_ADJUSTMENT")
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Pedidos de Pagamento não permitem reajuste. Apenas aprovação ou rejeição são permitidas.", Status = 400 });

        if (action == "APPROVE" && request.RequestType!.Code == "QUOTATION")
        {
            var hasGroups = await _context.RequestPoGroups.AnyAsync(g => g.RequestId == id);
            if (!hasGroups)
            {
                return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Não é possível aprovar um pedido de Cotação sem grupos de compras definidos.", Status = 400 });
            }
        }

        string historyComment = action switch
        {
            "APPROVE" => $"Aprovação Final realizada. {comment}".Trim(),
            "REJECT" => $"Pedido rejeitado na Aprovação Final. Motivo: {comment}",
            "REQUEST_ADJUSTMENT" => $"Solicitado reajuste na Aprovação Final. Motivo: {comment}",
            _ => comment ?? string.Empty
        };

        string successMessage = action switch
        {
            "APPROVE" => "Pedido aprovado com sucesso.",
            "REJECT" => "Pedido rejeitado com sucesso.",
            "REQUEST_ADJUSTMENT" => "Pedido devolvido para reajuste final com sucesso.",
            _ => "Operação realizada com sucesso."
        };

        // ── DEC-110: Financial Snapshot at Approval ──────────────────────────
        // Captures an immutable financial snapshot at the moment of final approval.
        // This snapshot serves as the baseline for divergence detection at payment.
        if (action == "APPROVE")
        {
            if (request.RequestType!.Code == RequestConstants.Types.Quotation)
            {
                var poGroups = await _context.RequestPoGroups
                    .Where(g => g.RequestId == id)
                    .ToListAsync();

                if (poGroups.Any())
                {
                    var groupTotal = poGroups.Sum(g => g.TotalAmount);
                    
                    // Defensive fallback: if PO group totals are zero (e.g. legacy data or builder
                    // ran before the QuotationItem.LineTotal fix), recalculate from awarded items.
                    if (groupTotal <= 0)
                    {
                        var awardedTotal = await _context.RequestLineItems
                            .Where(li => li.RequestId == id && !li.IsDeleted && li.SelectedQuotationItemId.HasValue)
                            .Join(_context.Set<QuotationItem>(),
                                li => li.SelectedQuotationItemId, qi => qi.Id,
                                (li, qi) => qi.LineTotal)
                            .SumAsync(t => t);
                        
                        if (awardedTotal > 0)
                        {
                            _logger.LogWarning(
                                "Request {RequestId}: PO group total was {GroupTotal}, recalculated from awarded items: {AwardedTotal}",
                                id, groupTotal, awardedTotal);
                            groupTotal = awardedTotal;
                        }
                    }
                    
                    request.ApprovedTotalAmount = groupTotal;
                    var distinctCurrencies = poGroups.Select(g => g.CurrencyCode).Distinct().ToList();
                    request.ApprovedCurrencyCode = distinctCurrencies.Count > 1 ? "MULTIPLE" : distinctCurrencies.FirstOrDefault();
                }
            }
            else
            {
                // PAYMENT flow: snapshot from EstimatedTotalAmount
                request.ApprovedTotalAmount = request.EstimatedTotalAmount;
                if (request.CurrencyId.HasValue)
                {
                    var currCode = await _context.Currencies
                        .Where(c => c.Id == request.CurrencyId.Value)
                        .Select(c => c.Code)
                        .FirstOrDefaultAsync();
                    request.ApprovedCurrencyCode = currCode;
                }
            }
            request.ApprovedAtUtc = DateTime.UtcNow;

            // ── Payment Request: Auto-create PO Group ──────────────────────
            // Payment Requests don't go through the quotation/award/GroupBuilder path.
            // Create a single PO group from the request's supplier, currency, and
            // estimated total so the Buyer can register the P.O. after final approval.
            if (request.RequestType!.Code == RequestConstants.Types.Payment)
            {
                var existingGroups = await _context.RequestPoGroups
                    .AnyAsync(g => g.RequestId == id);

                if (!existingGroups)
                {
                    var currencyObj = request.CurrencyId.HasValue
                        ? await _context.Currencies.FindAsync(request.CurrencyId.Value)
                        : null;
                    var supplier = request.SupplierId.HasValue
                        ? await _context.Suppliers.FindAsync(request.SupplierId.Value)
                        : null;

                    if (supplier == null && request.SupplierId.HasValue)
                    {
                        _logger.LogWarning(
                            "ProcessFinalApproval — Payment Request {RequestId} ({RequestNumber}) has SupplierId={SupplierId} but Supplier entity not found.",
                            id, request.RequestNumber, request.SupplierId);
                    }

                    var paymentGroup = new RequestPoGroup
                    {
                        RequestId = request.Id,
                        SupplierId = request.SupplierId,
                        SupplierNameSnapshot = supplier?.Name ?? "Fornecedor não definido",
                        SupplierNifSnapshot = supplier?.TaxId,
                        CurrencyId = currencyObj?.Id,
                        CurrencyCode = currencyObj?.Code ?? "AOA",
                        TotalAmount = request.EstimatedTotalAmount,
                        PaymentConditionCode = request.PaymentConditionCode,
                        AdvancePaymentPercent = request.AdvancePaymentPercent,
                        Status = RequestConstants.PoGroupStatuses.WaitingPo,
                        CreatedAtUtc = DateTime.UtcNow,
                        CreatedByUserId = actorId
                    };

                    _context.RequestPoGroups.Add(paymentGroup);

                    _logger.LogInformation(
                        "ProcessFinalApproval — Auto-created PO group for Payment Request {RequestId} ({RequestNumber}). " +
                        "Supplier: {Supplier}, Amount: {Amount:N2} {Currency}, GroupId: {GroupId}",
                        id, request.RequestNumber,
                        paymentGroup.SupplierNameSnapshot,
                        paymentGroup.TotalAmount,
                        paymentGroup.CurrencyCode,
                        paymentGroup.Id);
                }
            }

            // ── PO Group Activation: PENDING → WAITING_PO ──────────────────────
            // PO groups are created at Area Approval (via GroupBuilderService) with
            // status PENDING. At Final Approval, they must be activated to WAITING_PO
            // so the Buyer can register/upload the P.O.
            var pendingPoGroups = await _context.RequestPoGroups
                .Where(g => g.RequestId == id && g.Status == RequestConstants.PoGroupStatuses.Pending)
                .ToListAsync();

            foreach (var pg in pendingPoGroups)
            {
                pg.Status = RequestConstants.PoGroupStatuses.WaitingPo;
            }

            _logger.LogInformation(
                "ProcessFinalApproval — PO Group activation for Request {RequestId} ({RequestNumber}), " +
                "Type: {RequestType}. Total PO groups: {TotalGroups}, Transitioned PENDING→WAITING_PO: {TransitionedCount}. " +
                "Group details: [{GroupDetails}]",
                id,
                request.RequestNumber,
                request.RequestType?.Code,
                pendingPoGroups.Count + (await _context.RequestPoGroups.CountAsync(g => g.RequestId == id && g.Status != RequestConstants.PoGroupStatuses.Pending)),
                pendingPoGroups.Count,
                string.Join("; ", pendingPoGroups.Select(g => $"GroupId={g.Id}, Supplier={g.SupplierNameSnapshot}, Amount={g.TotalAmount:N2} {g.CurrencyCode}"))
            );

            if (!pendingPoGroups.Any())
            {
                _logger.LogWarning(
                    "ProcessFinalApproval — No PENDING PO groups found for Request {RequestId} ({RequestNumber}). " +
                    "This may indicate groups were not created at Area Approval or were already transitioned.",
                    id, request.RequestNumber);
            }

            await _context.SaveChangesAsync();
        }

        return await ApplyStatusChangeAndSyncItemsAsync(request, targetStatusCode, action, historyComment, successMessage, actorId, overrideEventCode);
    }

    private async Task<IActionResult> ProcessAreaApproval(Guid id, string action, string targetStatusCode, ApprovalActionDto dto, string? overrideEventCode = null)
    {
        var actorId = CurrentUserId;

        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
                .ThenInclude(li => li.Allocations)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        // Phase B authorization — DepartmentManager is the source of truth (D1):
        // admin, manager of the request's department/plant (specific or global), or the
        // legacy nominee on an old in-flight request. The manual role grants nothing.
        if (!await CanActAsAreaManagerAsync(actorId, request))
            return StatusCode(403, "Você não é responsável pelo departamento/planta deste pedido.");

        // Concurrency between multiple eligible managers: if another manager already
        // decided (status moved past area approval), answer 409 with the decider's name.
        if (request.Status!.Code != "WAITING_AREA_APPROVAL" && request.AreaApproverId != null
            && (request.Status.Code == "WAITING_FINAL_APPROVAL" || request.Status.Code == "REJECTED" || request.Status.Code == "AREA_ADJUSTMENT"))
        {
            var deciderName = await _context.Users.AsNoTracking()
                .Where(u => u.Id == request.AreaApproverId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync() ?? "outro aprovador";
            return Conflict(new ProblemDetails
            {
                Title = "Pedido Já Decidido",
                Detail = $"Este pedido já foi decidido por {deciderName}.",
                Status = 409
            });
        }

        // Winner Selection Validation (only for Quotation Approve)
        if (request.RequestType!.Code == "QUOTATION" && action == "APPROVE")
        {
            var activeItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
            
            // Process Extra Items before validation
            if (dto.ExtraItemDecisions != null && dto.ExtraItemDecisions.Any())
            {
                var extraIds = dto.ExtraItemDecisions.Keys.ToList();
                var extraQItems = await _context.QuotationItems
                    .Include(qi => qi.Quotation)
                    .Where(qi => extraIds.Contains(qi.Id) && qi.Quotation.RequestId == id)
                    .ToListAsync();

                foreach (var qi in extraQItems)
                {
                    var decision = dto.ExtraItemDecisions[qi.Id];
                    if (decision.Decision == "APPROVE")
                    {
                        int nextLineNumber = activeItems.Any() ? activeItems.Max(i => i.LineNumber) + 1 : 1;
                        var newLineItem = new RequestLineItem
                        {
                            Id = Guid.NewGuid(),
                            RequestId = id,
                            LineNumber = nextLineNumber,
                            Description = "[Item Adicional] " + qi.Description,
                            Quantity = qi.Quantity,
                            UnitId = qi.UnitId,
                            UnitPrice = qi.UnitPrice,
                            TotalAmount = qi.LineTotal,
                            IsDeleted = false,
                            CreatedAtUtc = DateTime.UtcNow,
                            CreatedByUserId = actorId
                        };
                        _context.RequestLineItems.Add(newLineItem);
                        activeItems.Add(newLineItem);

                        dto.ItemAwards ??= new Dictionary<Guid, Guid>();
                        dto.ItemAwards[newLineItem.Id] = qi.Id;

                        if (dto.ItemAssignments != null && dto.ItemAssignments.TryGetValue(qi.Id, out var assignment))
                        {
                            dto.ItemAssignments.Remove(qi.Id);
                            dto.ItemAssignments[newLineItem.Id] = assignment;
                        }

                        if (dto.ItemAllocations != null && dto.ItemAllocations.TryGetValue(qi.Id, out var allocations))
                        {
                            dto.ItemAllocations.Remove(qi.Id);
                            dto.ItemAllocations[newLineItem.Id] = allocations;
                        }
                    }
                    else if (decision.Decision == "REJECT")
                    {
                        _context.RequestStatusHistories.Add(new RequestStatusHistory
                        {
                            Id = Guid.NewGuid(),
                            RequestId = id,
                            ActorUserId = actorId,
                            ActionTaken = "EXTRA_ITEM_REJECTED",
                            PreviousStatusId = request.StatusId,
                            NewStatusId = request.StatusId, // Keep same status
                            Comment = $"Item Adicional Rejeitado ({qi.Quotation.SupplierNameSnapshot}): {qi.Description}. Motivo: {decision.Comment}",
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }
                }
            }

            if (dto.ItemAwards == null || !activeItems.All(i => dto.ItemAwards.ContainsKey(i.Id)))
            {
                return BadRequest(new ProblemDetails 
                { 
                    Title = "Vencedor Incompleto", 
                    Detail = "É necessário selecionar uma cotação vencedora para cada item do pedido.", 
                    Status = 400 
                });
            }

            // Verify if all selected quotation items belong to quotations of this request
            var selectedQuotationItemIds = dto.ItemAwards.Values.Distinct().ToList();
            var validQuotationItems = await _context.QuotationItems
                .Where(qi => selectedQuotationItemIds.Contains(qi.Id) && qi.Quotation.RequestId == id)
                .Select(qi => new { qi.Id, qi.QuotationId })
                .ToListAsync();

            var validQuotationItemIds = validQuotationItems.Select(x => x.Id).ToList();

            if (validQuotationItemIds.Count != selectedQuotationItemIds.Count)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Cotação Inválida",
                    Detail = "Um ou mais itens de cotação selecionados são inválidos ou não pertencem a este pedido.",
                    Status = 400
                });
            }

            // Cancelled-batch reuse rule (Option C): winners must not include items previously used
            // in a CANCELLED approval batch without an explicit, active Buyer reuse authorization.
            var (reuseBlocked, reuseAuthorized) = await _quotationEligibility.ValidateSelectionAsync(id, selectedQuotationItemIds);
            if (reuseBlocked.Count > 0)
            {
                return ApprovalBatchController.ReuseNotAuthorizedConflict(reuseBlocked);
            }

            // Consume authorizations used by this individual (non-batch) approval in the same
            // transaction (SaveChanges happens in ApplyStatusChangeAndSyncItemsAsync).
            if (reuseAuthorized.Count > 0)
            {
                var authIds = reuseAuthorized.Select(a => a.ReuseAuthorizationId!.Value).ToList();
                var authEntities = await _context.QuotationReuseAuthorizations
                    .Where(a => authIds.Contains(a.Id)).ToListAsync();
                foreach (var auth in authEntities)
                {
                    auth.IsActive = false;
                    auth.ConsumedAtUtc = DateTime.UtcNow; // no batch: individual approval consumption
                    var elig = reuseAuthorized.First(a => a.ReuseAuthorizationId == auth.Id);
                    _context.RequestStatusHistories.Add(new RequestStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        RequestId = id,
                        ActorUserId = actorId,
                        ActionTaken = "QUOTATION_REUSED_IN_NEW_BATCH",
                        PreviousStatusId = request.StatusId,
                        NewStatusId = request.StatusId,
                        Comment = $"Item de cotação {auth.QuotationItemId} reutilizado do Lote #{elig.SourceCancelledBatchNumber} (cancelado) na aprovação individual (sem lote). Motivo da autorização: {auth.Reason}",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }

            // Save the awards to the line items and record audit history
            var targetStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == targetStatusCode);
            foreach (var item in activeItems)
            {
                item.SelectedQuotationItemId = dto.ItemAwards[item.Id];
                
                if (targetStatus != null)
                {
                    _context.RequestStatusHistories.Add(new RequestStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        RequestId = request.Id,
                        ActorUserId = actorId,
                        ActionTaken = WorkflowEventCodes.QuotationItemAwarded,
                        PreviousStatusId = request.StatusId,
                        NewStatusId = targetStatus.Id,
                        Comment = $"Item #{item.LineNumber} - Vencedor selecionado: Cotação Item {item.SelectedQuotationItemId}",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }

            // Legacy compatibility: Set SelectedQuotationId if exactly one quotation won all items
            var distinctQuotationIds = validQuotationItems.Select(x => x.QuotationId).Distinct().ToList();
            if (distinctQuotationIds.Count == 1)
            {
                request.SelectedQuotationId = distinctQuotationIds.First();
            }
            else
            {
                request.SelectedQuotationId = null;
            }
        }

        // Multi-Allocation Propagation (Phase 1)
        if (action == "APPROVE")
        {
            var activeItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
            
            // 1. Process 3-tier fallback allocation logic
            foreach (var item in activeItems)
            {
                var allocsToSave = new List<RequestLineItemAllocation>();

                if (dto.ItemAllocations != null && dto.ItemAllocations.TryGetValue(item.Id, out var allocLines) && allocLines.Any())
                {
                    // Tier 1: ItemAllocations present
                    if (allocLines.Count > 10)
                    {
                        return BadRequest(new ProblemDetails { Title = "Excesso de Alocações", Detail = $"O item #{item.LineNumber} possui mais de 10 alocações.", Status = 400 });
                    }

                    if (allocLines.Any(a => a.Percentage <= 0))
                    {
                        return BadRequest(new ProblemDetails { Title = "Percentagem Inválida", Detail = $"O item #{item.LineNumber} possui alocações com percentagem igual ou inferior a zero.", Status = 400 });
                    }

                    var totalPercent = allocLines.Sum(a => a.Percentage);
                    if (totalPercent != 100m)
                    {
                        return BadRequest(new ProblemDetails { Title = "Percentagem Inválida", Detail = $"A soma das percentagens do item #{item.LineNumber} deve ser 100% (atual: {totalPercent}%).", Status = 400 });
                    }

                    int order = 0;
                    foreach (var al in allocLines)
                    {
                        allocsToSave.Add(new RequestLineItemAllocation
                        {
                            Id = Guid.NewGuid(),
                            RequestLineItemId = item.Id,
                            PlantId = al.PlantId,
                            CostCenterId = al.CostCenterId,
                            Percentage = al.Percentage,
                            AllocationOrder = order++,
                            Comment = al.Comment,
                            CreatedAtUtc = DateTime.UtcNow,
                            CreatedByUserId = actorId
                        });
                    }
                }
                else if (dto.ItemAssignments != null && dto.ItemAssignments.TryGetValue(item.Id, out var assignment) && assignment.PlantId.HasValue && assignment.PlantId > 0 && assignment.CostCenterId.HasValue && assignment.CostCenterId > 0)
                {
                    // Tier 2: ItemAssignments present
                    allocsToSave.Add(new RequestLineItemAllocation
                    {
                        Id = Guid.NewGuid(),
                        RequestLineItemId = item.Id,
                        PlantId = assignment.PlantId.Value,
                        CostCenterId = assignment.CostCenterId.Value,
                        Percentage = 100m,
                        AllocationOrder = 0,
                        CreatedAtUtc = DateTime.UtcNow,
                        CreatedByUserId = actorId
                    });
                }
                else
                {
                    // Tier 3: Reject missing allocation
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Atribuição de Item Incompleta",
                        Detail = $"O item #{item.LineNumber} está sem alocação financeira definida. A aprovação exige a definição de Planta e Centro de Custo para todos os itens.",
                        Status = 400
                    });
                }

                // Apply new allocations
                if (item.Allocations == null) item.Allocations = new List<RequestLineItemAllocation>();

                // We clear existing allocations and insert the new ones (since area approval overwrites drafts/previous)
                _context.RequestLineItemAllocations.RemoveRange(item.Allocations);
                item.Allocations.Clear();

                foreach (var a in allocsToSave)
                {
                    // Explicit DbSet.Add is required: the PK Guid is client-set, so an entity
                    // reached only through the navigation would be tracked as Modified
                    // (UPDATE on a row that never existed → DbUpdateConcurrencyException).
                    _context.RequestLineItemAllocations.Add(a);
                    item.Allocations.Add(a);
                }

                // Legacy fields sync
                var highestAlloc = allocsToSave.OrderByDescending(a => a.Percentage).ThenBy(a => a.AllocationOrder).ThenBy(a => a.CostCenterId).First();
                item.PlantId = highestAlloc.PlantId;
                item.CostCenterId = highestAlloc.CostCenterId;
            }

            // Budget Verification
            var overallStatus = await BudgetCalculationHelper.EvaluateOverallBudgetStatusAsync(_context, request, dto.ItemAwards, dto.ItemAssignments, dto.ItemAllocations);
            var criticalStatuses = new[] { "CRITICAL", "OVER_BUDGET", "NO_BUDGET", "CURRENCY_MISMATCH" };
            if (criticalStatuses.Contains(overallStatus))
            {
                if (string.IsNullOrWhiteSpace(dto.BudgetJustification) || dto.BudgetJustification.Trim().Length < 20)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Justificativa de Orçamento Obrigatória",
                        Detail = "O impacto orçamental deste pedido é Crítico ou Acima do Orçamento. É obrigatório fornecer uma justificativa orçamental com pelo menos 20 caracteres.",
                        Status = 400
                    });
                }
                
                // Save the justification in the comments/history
                dto.Comment = $"[Aprovação c/ Exceção Orçamental] Justificativa: {dto.BudgetJustification.Trim()}\n\nObservações: {dto.Comment}".Trim();
            }

            // Alternative Budget Reassignments Verification & Audit
            if (dto.Reassignments != null && dto.Reassignments.Any())
            {
                var allowedPlantIds = await _context.UserPlantScopes
                    .Where(s => s.UserId == CurrentUserId)
                    .Select(s => s.PlantId)
                    .ToListAsync();

                foreach (var reassignment in dto.Reassignments)
                {
                    // Validate Reason
                    if (string.IsNullOrWhiteSpace(reassignment.Reason) || reassignment.Reason.Trim().Length < 20)
                    {
                        return BadRequest(new ProblemDetails
                        {
                            Title = "Motivo de Alteração Obrigatório",
                            Detail = "É obrigatório fornecer um motivo válido (mínimo 20 caracteres) para a alteração do centro de custo.",
                            Status = 400
                        });
                    }

                    // Validate Plant Scope (skip if SystemAdmin)
                    if (!CurrentUserRoles.Contains(RoleConstants.SystemAdministrator) && !allowedPlantIds.Contains(reassignment.NewPlantId))
                    {
                        return StatusCode(403, "Você não tem permissão para realocar itens para a planta selecionada.");
                    }

                    // Validate Cost Center belongs to Plant and is valid
                    var cc = await _context.CostCenters.FirstOrDefaultAsync(c => c.Id == reassignment.NewCostCenterId && c.PlantId == reassignment.NewPlantId);
                    if (cc == null && reassignment.NewCostCenterId.HasValue)
                    {
                        return BadRequest(new ProblemDetails { Title = "Centro de Custo Inválido", Detail = "O centro de custo selecionado não pertence à planta informada ou não existe.", Status = 400 });
                    }

                    // Validate Budget Line active
                    var budgetLine = await _context.AnnualBudgets.FirstOrDefaultAsync(b => b.DepartmentId == request.DepartmentId 
                                                                                   && b.CompanyId == request.CompanyId
                                                                                   && b.CostCenterId == reassignment.NewCostCenterId 
                                                                                   && b.Year == request.CreatedAtUtc.Year 
                                                                                   && b.CurrencyId == request.CurrencyId
                                                                                   && b.IsActive);
                    if (budgetLine == null)
                    {
                        return BadRequest(new ProblemDetails { Title = "Orçamento Inválido", Detail = "A rubrica orçamental selecionada não está ativa ou não pertence ao contexto deste pedido.", Status = 400 });
                    }

                    // Validate items
                    var invalidItems = reassignment.AffectedItemIds.Where(id => !request.LineItems.Any(li => li.Id == id && !li.IsDeleted)).ToList();
                    if (invalidItems.Any())
                    {
                        return BadRequest(new ProblemDetails { Title = "Itens Inválidos", Detail = "Um ou mais itens afetados pela realocação são inválidos ou não pertencem a este pedido.", Status = 400 });
                    }

                    // Log History
                    var oldPlant = await _context.Plants.FindAsync(reassignment.OldPlantId);
                    var oldCc = reassignment.OldCostCenterId.HasValue ? await _context.CostCenters.FindAsync(reassignment.OldCostCenterId) : null;
                    var oldPlantName = oldPlant?.Name ?? "Desconhecida";
                    var oldCcName = oldCc?.Name ?? "Orçamento Geral";

                    var newPlant = await _context.Plants.FindAsync(reassignment.NewPlantId);
                    var newCcName = cc?.Name ?? "Orçamento Geral";

                    string auditMsg = $"[Alteração de Centro de Custo] De: {oldPlantName} / {oldCcName} Para: {newPlant?.Name} / {newCcName}. Itens afetados: {reassignment.AffectedItemIds.Count}. Motivo: {reassignment.Reason.Trim()}";
                    
                    _context.RequestStatusHistories.Add(new RequestStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        RequestId = request.Id,
                        ActorUserId = actorId,
                        ActionTaken = "REALLOCATION",
                        PreviousStatusId = request.StatusId,
                        NewStatusId = request.StatusId,
                        Comment = auditMsg,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }
        }

        if (request == null) return NotFound();

        // Business Rule: Area Approval applies to both PAYMENT and QUOTATION flows
        if (request.RequestType!.Code != "PAYMENT" && request.RequestType!.Code != "QUOTATION")
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Esta ação só é permitida para pedidos de Pagamento ou Cotação.", Status = 400 });

        if (request.Status!.Code != "WAITING_AREA_APPROVAL")
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "O pedido não está em fase de aprovação da área.", Status = 400 });

        if (request.RequestType!.Code == "PAYMENT" && action == "REQUEST_ADJUSTMENT")
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Pedidos de Pagamento não permitem reajuste. Apenas aprovação ou rejeição são permitidas.", Status = 400 });

        string historyComment = action switch
        {
            "APPROVE" => $"Aprovação da Área realizada. {dto.Comment}".Trim(),
            "REJECT" => $"Pedido rejeitado na Aprovação da Área. Motivo: {dto.Comment}",
            "REQUEST_ADJUSTMENT" => $"Solicitado reajuste (Rework) na Aprovação da Área. Motivo: {dto.Comment}",
            _ => dto.Comment ?? string.Empty
        };

        string successMessage = action switch
        {
            "APPROVE" => "Pedido enviado para aprovação final com sucesso.",
            "REJECT" => "Pedido rejeitado com sucesso.",
            "REQUEST_ADJUSTMENT" => "Pedido devolvido para reajuste com sucesso.",
            _ => "Operação realizada com sucesso."
        };

        // Phase B — decided-by semantics: AreaApproverId records who actually took the
        // area decision. Written only after authorization and status checks passed.
        request.AreaApproverId = actorId;

        var result = await ApplyStatusChangeAndSyncItemsAsync(request, targetStatusCode, action, historyComment, successMessage, actorId, overrideEventCode);

        // Stage 4: Build request PO groups after Area Approval if it's an approved Quotation
        if (request.RequestType!.Code == "QUOTATION" && action == "APPROVE" && result is OkObjectResult)
        {
            await _groupBuilderService.BuildGroupsForRequestAsync(request.Id);
        }

        return result;
    }

    [HttpPost("{id}/operational/register-po")]
    public async Task<IActionResult> RegisterPo(Guid id, [FromBody] RegisterPoActionDto dto)
    {
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Buyer))
            return StatusCode(403, "Apenas o Comprador pode registrar a P.O.");

        if (!dto.PoGroupId.HasValue)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Bloqueada",
                Detail = "O Grupo de P.O é obrigatório.",
                Status = 400
            });
        }

        if (!await HasGroupAttachmentAsync(dto.PoGroupId.Value, RequestAttachment.TYPE_PO))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Bloqueada",
                Detail = "É necessário anexar a P.O antes de registrar.",
                Status = 400
            });
        }

        // ── Backend Duplicate PO Validation ──
        if (!string.IsNullOrWhiteSpace(dto.PurchaseOrderNumber))
        {
            var duplicateGroup = await _context.RequestPoGroups
                .Include(g => g.Request)
                .Include(g => g.Supplier)
                .Where(g => g.PurchaseOrderNumber == dto.PurchaseOrderNumber && g.Id != dto.PoGroupId.Value)
                .FirstOrDefaultAsync();

            if (duplicateGroup != null)
            {
                if (!dto.OverrideDuplicateConfirmed)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "DUPLICATE_PO",
                        Detail = $"O número de P.O {dto.PurchaseOrderNumber} já está registrado no Pedido {duplicateGroup.Request?.RequestNumber} (Fornecedor: {duplicateGroup.Supplier?.Name}, Status: {duplicateGroup.Status}). Confirme se deseja prosseguir.",
                        Status = 400
                    });
                }
                
                if (string.IsNullOrWhiteSpace(dto.DuplicateOverrideComment))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Ação Bloqueada",
                        Detail = "Quando um número de P.O duplicado é confirmado, uma justificativa é obrigatória.",
                        Status = 400
                    });
                }
            }
        }

        // ── B2P: Validate payment condition (required — no silent default) ──
        if (string.IsNullOrWhiteSpace(dto.PaymentConditionCode))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Condição de Pagamento Obrigatória",
                Detail = "É obrigatório selecionar a condição de pagamento antes de registrar a P.O.",
                Status = 400
            });
        }
        var paymentCondition = dto.PaymentConditionCode;
        var validConditions = new[] { RequestConstants.PaymentConditions.PostPaid, RequestConstants.PaymentConditions.AdvanceFull, RequestConstants.PaymentConditions.AdvancePartial };
        if (!validConditions.Contains(paymentCondition))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Condição de Pagamento Inválida",
                Detail = $"Condição '{paymentCondition}' não reconhecida. Valores aceitos: POST_PAID, ADVANCE_FULL, ADVANCE_PARTIAL.",
                Status = 400
            });
        }

        decimal? advancePercent = null;
        if (paymentCondition == RequestConstants.PaymentConditions.AdvanceFull)
        {
            advancePercent = 100m;
        }
        else if (paymentCondition == RequestConstants.PaymentConditions.AdvancePartial)
        {
            if (!dto.AdvancePaymentPercent.HasValue || dto.AdvancePaymentPercent.Value < 1 || dto.AdvancePaymentPercent.Value > 99)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Percentual Inválido",
                    Detail = "Para pagamento antecipado parcial, informe o percentual entre 1 e 99.",
                    Status = 400
                });
            }
            advancePercent = dto.AdvancePaymentPercent.Value;
        }

        bool isAdvancePayment = paymentCondition != RequestConstants.PaymentConditions.PostPaid;

        // ── Find Request and the specific PO Group ──
        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.Currency)
            .Include(r => r.PoGroups)
                .ThenInclude(g => g.Supplier)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound("Pedido não encontrado.");

        var poGroup = request.PoGroups.FirstOrDefault(g => g.Id == dto.PoGroupId.Value);
        if (poGroup == null) return NotFound("Grupo P.O não encontrado.");

        // ── Block PO for DRAFT suppliers ──
        if (poGroup.Supplier != null && poGroup.Supplier.RegistrationStatus == "DRAFT")
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Fornecedor em Rascunho",
                Detail = "Não é possível registrar P.O. para um fornecedor em rascunho (Cadastro de fornecedor necessário). O fornecedor deve ser validado primeiro.",
                Status = 400
            });
        }

        // ── Backend OCR Validation against Group ──
        var backendMismatches = new List<string>();
        
        if (dto.ExtractedTotalAmount.HasValue && Math.Abs(dto.ExtractedTotalAmount.Value - poGroup.TotalAmount) > 1.0m)
        {
            backendMismatches.Add($"Total divergente: Identificado {dto.ExtractedTotalAmount.Value:N2} (Esperado: {poGroup.TotalAmount:N2})");
        }

        if (!string.IsNullOrWhiteSpace(dto.ExtractedCurrencyCode) && !dto.ExtractedCurrencyCode.Equals(poGroup.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            backendMismatches.Add($"Moeda divergente: Identificada {dto.ExtractedCurrencyCode} (Esperada: {poGroup.CurrencyCode})");
        }

        if (!string.IsNullOrWhiteSpace(dto.ExtractedSupplierName))
        {
            var extractedSupplier = dto.ExtractedSupplierName.ToLowerInvariant();
            var expectedSupplier = poGroup.Supplier?.Name?.ToLowerInvariant() ?? "";
            
            var tokens1 = extractedSupplier.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var tokens2 = expectedSupplier.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var intersection = tokens1.Intersect(tokens2).Count();
            var maxLen = Math.Max(tokens1.Length, tokens2.Length);
            
            if (maxLen > 0 && ((double)intersection / maxLen) < 0.6)
            {
                backendMismatches.Add($"Fornecedor divergente: Identificado \"{dto.ExtractedSupplierName}\" (Esperado: \"{poGroup.Supplier?.Name}\")");
            }
        }

        if (backendMismatches.Any())
        {
            if (!dto.OverrideConfirmed)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "OCR_MISMATCH",
                    Detail = string.Join(" | ", backendMismatches),
                    Status = 400
                });
            }

            if (string.IsNullOrWhiteSpace(dto.Comment))
            {
                 return BadRequest(new ProblemDetails
                {
                    Title = "Ação Bloqueada",
                    Detail = "Quando há divergências confirmadas, um comentário justificativo é obrigatório.",
                    Status = 400
                });
            }
        }

        string finalComment = $"[Grupo P.O.: {poGroup.Supplier?.Name} | Total: {poGroup.TotalAmount:N2} | GroupId: {poGroup.Id.ToString().Substring(0,8)}] ";

        if (backendMismatches.Any())
        {
            finalComment += $"Divergência OCR confirmada pelo comprador. Divergências: {string.Join(", ", backendMismatches)}. Justificativa: {dto.Comment}. ";
        }
        else if (!string.IsNullOrWhiteSpace(dto.Comment))
        {
            finalComment += $"Justificativa: {dto.Comment}. ";
        }

        if (dto.OverrideDuplicateConfirmed)
        {
            finalComment += $"Número de P.O. duplicado confirmado pelo comprador. P.O.: {dto.PurchaseOrderNumber}. Justificativa: {dto.DuplicateOverrideComment}. ";
        }

        // ── Status Guard: group-first for QUOTATION, request-level for PAYMENT ──
        if (request.RequestType?.Code == RequestConstants.Types.Quotation)
        {
            // QUOTATION: validate by PO group status, not parent request status
            var allowedGroupStatuses = new[] { 
                RequestConstants.PoGroupStatuses.WaitingPo, 
                RequestConstants.PoGroupStatuses.WaitingPoCorrection 
            };
            if (!allowedGroupStatuses.Contains(poGroup.Status))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Ação Inválida",
                    Detail = $"O Grupo P.O não está em status que permita registro de P.O. " +
                             $"Status atual do grupo: {poGroup.Status}. " +
                             $"Status permitidos: {string.Join(", ", allowedGroupStatuses)}.",
                    Status = 400
                });
            }
        }
        else
        {
            // PAYMENT: preserve existing request-level status guard
            var allowedRequestStatuses = new[] { "APPROVED", RequestConstants.Statuses.WaitingPoCorrection, RequestConstants.Statuses.PoPartiallyUploaded };
            if (request.Status == null || !allowedRequestStatuses.Contains(request.Status.Code))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Ação Inválida",
                    Detail = $"Não é possível registrar P.O no status atual do pedido ({request.Status?.Code}).",
                    Status = 400
                });
            }
        }

        var isCorrection = poGroup.Status == RequestConstants.Statuses.WaitingPoCorrection;
        var actionCode = isCorrection ? "REREGISTER_PO" : "REGISTER_PO";

        if (isCorrection && string.IsNullOrWhiteSpace(finalComment))
        {
            finalComment = "P.O corrigida após devolução por Finanças.";
        }

        // ── B2P: Persist payment condition on RequestPoGroup ──
        poGroup.PaymentConditionCode = paymentCondition;
        poGroup.AdvancePaymentPercent = advancePercent;
        if (!string.IsNullOrWhiteSpace(dto.PurchaseOrderNumber)) 
        {
            poGroup.PurchaseOrderNumber = dto.PurchaseOrderNumber;
        }
        poGroup.UpdatedAtUtc = DateTime.UtcNow;
        poGroup.UpdatedByUserId = CurrentUserId;

        string targetGroupStatusCode;
        string successMsg;

        if (isAdvancePayment)
        {
            targetGroupStatusCode = RequestConstants.Statuses.AdvancePaymentRequired;
            successMsg = isCorrection
                ? "P.O corrigida e re-registrada com sucesso. Adiantamento necessário."
                : $"P.O registrada com sucesso. Adiantamento de {advancePercent}% necessário.";

            finalComment += $"\n[Condição de Pagamento: {paymentCondition} — {advancePercent}%]";

            var advancePayment = new RequestPayment
            {
                RequestId = request.Id,
                RequestPoGroupId = poGroup.Id,
                PaymentType = RequestPayment.PaymentTypes.Advance,
                PaymentSequence = 1,
                PlannedPercent = advancePercent,
                PlannedAmount = Math.Round(poGroup.TotalAmount * (advancePercent!.Value / 100m), 2),
                CurrencyCode = poGroup.CurrencyCode ?? "AOA",
                PaymentStatus = RequestPayment.PaymentStatuses.Planned,
                CreatedByUserId = CurrentUserId,
                CreatedAtUtc = DateTime.UtcNow,
                Notes = $"Adiantamento de {advancePercent}% criado automaticamente no registro da P.O."
            };
            _context.RequestPayments.Add(advancePayment);
        }
        else
        {
            // POST_PAID
            targetGroupStatusCode = RequestConstants.Statuses.PoIssued;
            successMsg = isCorrection ? "P.O corrigida e re-registrada com sucesso." : "P.O registrada com sucesso.";
        }

        finalComment += successMsg;

        // Change group status
        poGroup.Status = targetGroupStatusCode;

        // Add history log
        var history = new RequestStatusHistory
        {
            RequestId = request.Id,
            ActorUserId = CurrentUserId, // Ensure the field is ActorUserId or ActorId depending on model
            ActionTaken = actionCode,
            CreatedAtUtc = DateTime.UtcNow,
            Comment = finalComment
            // Target status might need ID, we'll see
        };
        
        var newStatusForHistory = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == targetGroupStatusCode);
        if (newStatusForHistory != null)
        {
            history.NewStatusId = newStatusForHistory.Id;
        }

        _context.RequestStatusHistories.Add(history);

        // Evaluate parent request status
        var allGroups = request.PoGroups.ToList();
        var pendingGroups = allGroups.Count(g => 
            g.Status == RequestConstants.PoGroupStatuses.Pending || 
            g.Status == RequestConstants.PoGroupStatuses.WaitingPo || 
            g.Status == RequestConstants.Statuses.WaitingPoCorrection);

        // For QUOTATION: also check for unresolved quotation items without PO groups
        bool hasPendingQuotationItems = false;
        if (request.RequestType?.Code == RequestConstants.Types.Quotation)
        {
            var lineItemStatuses = await _context.RequestLineItems
                .Where(li => li.RequestId == request.Id && !li.IsDeleted)
                .Select(li => li.QuotationLifecycleStatus)
                .ToListAsync();

            hasPendingQuotationItems = lineItemStatuses.Any(status =>
                status == null ||
                status == RequestConstants.QuotationLifecycleStatuses.QuotationPending ||
                status == RequestConstants.QuotationLifecycleStatuses.BatchAssigned ||
                status == RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed);
        }

        string newRequestStatusCode;
        if (pendingGroups == 0 && !hasPendingQuotationItems)
        {
            // All POs uploaded AND no pending quotation items
            if (allGroups.Any(g => g.Status == RequestConstants.Statuses.AdvancePaymentRequired))
            {
                newRequestStatusCode = RequestConstants.Statuses.AdvancePaymentRequired;
            }
            else
            {
                newRequestStatusCode = RequestConstants.Statuses.PoIssued;
            }
        }
        else if (pendingGroups < allGroups.Count || hasPendingQuotationItems)
        {
            // Some groups still pending OR unresolved quotation items exist
            newRequestStatusCode = RequestConstants.Statuses.PoPartiallyUploaded;
        }
        else
        {
            newRequestStatusCode = "APPROVED";
        }

        if (request.Status.Code != newRequestStatusCode)
        {
            var newStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == newRequestStatusCode);
            if (newStatus != null)
            {
                request.StatusId = newStatus.Id;
            }
        }

        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = CurrentUserId;

        await _context.SaveChangesAsync();

        // ── Notifications ──
        try {
            await _orchestrator.EmitAsync(new WorkflowEvent
            {
                EventCode = "PO_REGISTERED",
                RequestId = request.Id,
                ActionTaken = "REGISTER_PO",
                TargetStatusCode = request.Status!.Code,
                ActorUserId = CurrentUserId,
                CorrelationId = Guid.NewGuid()
            });
        } catch { }

        return Ok(new { message = successMsg });
    }

    [HttpPost("{id}/operational/schedule-payment")]
    public async Task<IActionResult> SchedulePayment(Guid id, [FromBody] ApprovalActionDto dto)
    {
        _logger.LogWarning("[DEPRECATED] Legacy schedule-payment called for Request {RequestId}. Use FinanceController.SchedulePayment instead.", id);
        if (!await HasAttachmentAsync(id, RequestAttachment.TYPE_PAYMENT_SCHEDULE))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Bloqueada",
                Detail = "É necessário anexar o Cronograma de Pagamento antes de agendar.",
                Status = 400
            });
        }
        // Unified post-PO operational flow
    return await ProcessCommonOperationalTransition(id, "SCHEDULE_PAYMENT", "PAYMENT_SCHEDULED", new[] { "PO_ISSUED" }, dto.Comment, "Pagamento agendado com sucesso.");
    }

    [HttpPost("{id}/operational/complete-payment")]
    public async Task<IActionResult> CompletePayment(Guid id, [FromBody] ApprovalActionDto dto)
    {
        _logger.LogWarning("[DEPRECATED] Legacy complete-payment called for Request {RequestId}. Use FinanceController.MarkAsPaid instead.", id);
        if (!await HasAttachmentAsync(id, RequestAttachment.TYPE_PAYMENT_PROOF))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Bloqueada",
                Detail = "É necessário anexar o Comprovante de Pagamento antes de concluir.",
                Status = 400
            });
        }
        // Unified post-PO operational flow
    return await ProcessCommonOperationalTransition(id, "COMPLETE_PAYMENT", "PAYMENT_COMPLETED", new[] { "PO_ISSUED", "PAYMENT_SCHEDULED" }, dto.Comment, "Pagamento realizado com sucesso.");
    }

    // ── Buy-to-Pay: Advance Payment Lifecycle Endpoints ──

    [HttpPost("{id}/b2p/schedule-advance")]
    public async Task<IActionResult> ScheduleAdvancePayment(Guid id, [FromBody] ScheduleAdvancePaymentDto dto)
    {
        var actorId = CurrentUserId;
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Finance))
            return StatusCode(403, "Apenas o Financeiro pode agendar o adiantamento.");

        var request = await _context.Requests
            .Include(r => r.PoGroups)
            .Include(r => r.Status)
            .Include(r => r.Payments)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        var group = request.PoGroups.FirstOrDefault(g => g.Id == dto.RequestPoGroupId);
        if (group == null) return BadRequest("Grupo P.O não encontrado no request.");

        if (group.Status != RequestConstants.Statuses.AdvancePaymentRequired)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = $"O grupo não está em status de adiantamento necessário. Status atual: {group.Status}", Status = 400 });

        // Find the planned advance payment for this group
        var advancePayment = request.Payments
            .FirstOrDefault(p => p.RequestPoGroupId == group.Id && p.PaymentType == RequestPayment.PaymentTypes.Advance && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned);

        if (advancePayment == null)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Não existe pagamento adiantado planejado para este grupo.", Status = 400 });

        // Update payment status
        advancePayment.PaymentStatus = RequestPayment.PaymentStatuses.Scheduled;
        advancePayment.ScheduledDateUtc = dto.ScheduledDate;
        advancePayment.ScheduledByUserId = actorId;
        advancePayment.UpdatedByUserId = actorId;
        advancePayment.UpdatedAtUtc = DateTime.UtcNow;

        // Group transitions to ADVANCE_PAYMENT_SCHEDULED
        group.Status = RequestConstants.Statuses.AdvancePaymentScheduled;
        group.UpdatedAtUtc = DateTime.UtcNow;

        var currentStatus = await _context.RequestStatuses.FirstAsync(s => s.Code == request.Status!.Code);
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actorId,
            ActionTaken = "SCHEDULE_ADVANCE",
            PreviousStatusId = currentStatus.Id,
            NewStatusId = currentStatus.Id,
            Comment = $"[Grupo P.O.: {group.SupplierNameSnapshot}] " + (dto.Comment ?? "Adiantamento agendado."),
            CreatedAtUtc = DateTime.UtcNow
        });
        
        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;
        await _context.SaveChangesAsync();
        
        // Aggregate to update parent status
        var _statusAggregationService = HttpContext.RequestServices.GetRequiredService<IStatusAggregationService>();
        await _statusAggregationService.AggregateRequestStatusAsync(id);

        return Ok(new { message = "Adiantamento agendado com sucesso.", paymentId = advancePayment.Id });
    }


    [HttpPost("{id}/b2p/confirm-advance")]
    public async Task<IActionResult> ConfirmAdvancePayment(Guid id, [FromBody] ConfirmAdvancePaymentDto dto)
    {
        var actorId = CurrentUserId;
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Finance))
            return StatusCode(403, "Apenas o Financeiro pode confirmar o adiantamento.");

        var request = await _context.Requests
            .Include(r => r.PoGroups)
            .Include(r => r.Status)
            .Include(r => r.Payments)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        var group = request.PoGroups.FirstOrDefault(g => g.Id == dto.RequestPoGroupId);
        if (group == null) return BadRequest("Grupo P.O não encontrado no request.");

        if (group.Status != RequestConstants.Statuses.AdvancePaymentRequired && group.Status != RequestConstants.Statuses.AdvancePaymentScheduled)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = $"O grupo não está em status de adiantamento. Status atual: {group.Status}", Status = 400 });

        // Find the scheduled or planned advance payment for this group
        var advancePayment = request.Payments
            .Where(p => p.RequestPoGroupId == group.Id && p.PaymentType == RequestPayment.PaymentTypes.Advance)
            .Where(p => p.PaymentStatus == RequestPayment.PaymentStatuses.Scheduled || p.PaymentStatus == RequestPayment.PaymentStatuses.Planned)
            .OrderByDescending(p => p.PaymentSequence)
            .FirstOrDefault();

        if (advancePayment == null)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Não existe pagamento adiantado pendente para este grupo.", Status = 400 });

        // Validate actual paid amount
        if (dto.ActualPaidAmount <= 0)
            return BadRequest(new ProblemDetails { Title = "Valor Inválido", Detail = "O valor pago deve ser maior que zero.", Status = 400 });

        // Validate and link payment proof attachment if provided
        var attachment = await _context.RequestAttachments
            .FirstOrDefaultAsync(a => a.Id == dto.PaymentProofAttachmentId && a.RequestId == id && !a.IsDeleted);
            
        if (attachment == null)
            return BadRequest(new ProblemDetails { Title = "Anexo Inválido", Detail = "O comprovativo de pagamento não foi encontrado ou não pertence a este pedido.", Status = 400 });

        if (attachment.AttachmentTypeCode != AttachmentConstants.Types.PaymentProof)
            return BadRequest(new ProblemDetails { Title = "Anexo Inválido", Detail = "O ficheiro enviado não é um comprovativo de pagamento válido.", Status = 400 });

        advancePayment.PaymentProofAttachmentId = dto.PaymentProofAttachmentId;
        attachment.RequestPoGroupId = group.Id; // link attachment to group

        // Update payment
        advancePayment.PaymentStatus = RequestPayment.PaymentStatuses.Completed;
        advancePayment.ActualPaidAmount = dto.ActualPaidAmount;
        advancePayment.PaidDateUtc = dto.PaidDate;
        advancePayment.PaidByUserId = actorId;
        advancePayment.UpdatedByUserId = actorId;
        advancePayment.UpdatedAtUtc = DateTime.UtcNow;

        // Divergence detection
        var divergence = dto.ActualPaidAmount - advancePayment.PlannedAmount;
        if (Math.Abs(divergence) > 0.01m)
        {
            advancePayment.HasDivergence = true;
            advancePayment.DivergenceAmount = divergence;
            advancePayment.DivergenceNotes = dto.Comment;
        }

        group.Status = RequestConstants.Statuses.AdvancePaymentCompleted;
        group.UpdatedAtUtc = DateTime.UtcNow;

        var prevStatusId = request.StatusId;

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actorId,
            ActionTaken = "CONFIRM_ADVANCE",
            PreviousStatusId = prevStatusId,
            NewStatusId = prevStatusId, // Keep parent request status id context
            Comment = $"[Grupo P.O.: {group.SupplierNameSnapshot}] Adiantamento de {advancePayment.ActualPaidAmount} confirmado. {dto.Comment ?? ""}",
            CreatedAtUtc = DateTime.UtcNow
        });
        
        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;

        await _context.SaveChangesAsync();

        // Aggregate to update parent status
        var _statusAggregationService = HttpContext.RequestServices.GetRequiredService<IStatusAggregationService>();
        await _statusAggregationService.AggregateRequestStatusAsync(id);

        return Ok(new { message = "Adiantamento confirmado.", paymentId = advancePayment.Id });
    }


    [HttpPost("{id}/b2p/reconcile")]
    public async Task<IActionResult> ReconcileRequest(Guid id, [FromBody] SubmitReconciliationDto dto)
    {
        var actorId = CurrentUserId;
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Finance))
            return StatusCode(403, "Apenas o Financeiro pode realizar a reconciliação.");

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.Reconciliations)
            .Include(r => r.Payments)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null)
            return NotFound("Pedido não encontrado.");

        if (request.Status!.Code != "WAITING_RECONCILIATION")
            return StatusCode(400, "O pedido não está aguardando reconciliação.");

        var activeReconciliation = request.Reconciliations
            .Where(r => r.ReconciliationStatus == RequestReconciliation.ReconciliationStatuses.Draft || r.ReconciliationStatus == RequestReconciliation.ReconciliationStatuses.InProgress)
            .OrderByDescending(r => r.ReconciliationSequence)
            .FirstOrDefault();

        if (activeReconciliation == null)
        {
            var nextSequence = request.Reconciliations.Any() ? request.Reconciliations.Max(r => r.ReconciliationSequence) + 1 : 1;
            activeReconciliation = new RequestReconciliation
            {
                RequestId = request.Id,
                ReconciliationSequence = nextSequence,
                StartedByUserId = actorId,
                StartedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.RequestReconciliations.Add(activeReconciliation);
        }

        activeReconciliation.FinalInvoiceAmount = dto.FinalInvoiceAmount;
        activeReconciliation.FinalAcceptedAmount = dto.FinalAcceptedAmount;
        activeReconciliation.DeliveredAcceptedAmount = dto.DeliveredAcceptedAmount;
        activeReconciliation.ReconciliationDecision = dto.ReconciliationDecision;
        activeReconciliation.ReconciliationNotes = dto.ReconciliationNotes;
        activeReconciliation.CreditNoteRequired = dto.CreditNoteRequired;
        activeReconciliation.CreditNoteNumber = dto.CreditNoteNumber;
        activeReconciliation.CreditNoteAttachmentId = dto.CreditNoteAttachmentId;
        activeReconciliation.DebitNoteRequired = dto.DebitNoteRequired;
        activeReconciliation.DebitNoteNumber = dto.DebitNoteNumber;
        activeReconciliation.DebitNoteAttachmentId = dto.DebitNoteAttachmentId;
        activeReconciliation.RefundRequired = dto.RefundRequired;
        activeReconciliation.RefundAmount = dto.RefundAmount;
        activeReconciliation.CompensationFuturePayment = dto.CompensationFuturePayment;
        activeReconciliation.CompensationNotes = dto.CompensationNotes;

        var actualPaidSum = request.Payments.Where(p => p.PaymentStatus == RequestPayment.PaymentStatuses.Completed).Sum(p => p.ActualPaidAmount ?? 0);
        activeReconciliation.DifferenceAmount = dto.FinalInvoiceAmount - actualPaidSum;

        if (dto.ReconciliationDecision == RequestReconciliation.ReconciliationDecisions.NoDifference)
        {
            activeReconciliation.ReconciliationStatus = RequestReconciliation.ReconciliationStatuses.Completed;
            activeReconciliation.CompletedByUserId = actorId;
            activeReconciliation.CompletedAtUtc = DateTime.UtcNow;

            if (activeReconciliation.DifferenceAmount > 0)
            {
                var finalBalancePayment = new RequestPayment
                {
                    RequestId = request.Id,
                    PaymentType = RequestPayment.PaymentTypes.FinalBalance,
                    PaymentStatus = RequestPayment.PaymentStatuses.Planned,
                    PlannedAmount = activeReconciliation.DifferenceAmount.Value,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.RequestPayments.Add(finalBalancePayment);

                var waitingPaymentStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.PaymentRequestSent);
                
                request.StatusHistories.Add(new RequestStatusHistory
                {
                    RequestId = request.Id,
                    PreviousStatusId = request.StatusId,
                    NewStatusId = waitingPaymentStatus!.Id,
                    ActorUserId = actorId,
                    ActionTaken = "Reconciled_Balance_Created",
                    CreatedAtUtc = DateTime.UtcNow,
                    Comment = $"Reconciliação finalizada. Saldo remanescente de {activeReconciliation.DifferenceAmount} enviado para pagamento."
                });

                request.StatusId = waitingPaymentStatus.Id;
            }
            else
            {
                var finalizedStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.PaymentCompleted);
                
                request.StatusHistories.Add(new RequestStatusHistory
                {
                    RequestId = request.Id,
                    PreviousStatusId = request.StatusId,
                    NewStatusId = finalizedStatus!.Id,
                    ActorUserId = actorId,
                    ActionTaken = "Reconciled",
                    CreatedAtUtc = DateTime.UtcNow,
                    Comment = "Reconciliação finalizada (Sem saldo remanescente)."
                });

                request.StatusId = finalizedStatus.Id;
            }
        }
        else
        {
            activeReconciliation.ReconciliationStatus = RequestReconciliation.ReconciliationStatuses.InProgress;
        }

        activeReconciliation.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Reconciliação registrada com sucesso.", statusCode = request.Status!.Code });
    }

    [HttpPost("{id}/b2p/confirm-delivery")]
    public async Task<IActionResult> ConfirmDelivery(Guid id, [FromBody] ApprovalActionDto dto)
    {
        var actorId = CurrentUserId;
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Receiving) && !roles.Contains(RoleConstants.Buyer))
            return StatusCode(403, "Apenas o Almoxarifado ou Comprador pode confirmar a entrega.");

        return await ProcessCommonOperationalTransition(id, "CONFIRM_DELIVERY", RequestConstants.Statuses.WaitingReconciliation,
            new[] { RequestConstants.Statuses.WaitingSupplierDelivery }, dto.Comment, "Entrega/serviço confirmado. Pedido em reconciliação.");
    }

    [HttpPost("{id}/operational/move-to-receipt")]
    public async Task<IActionResult> MoveToReceipt(Guid id, [FromBody] ConfirmReceivingDto dto)
    {
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Receiving))
            return StatusCode(403, "Apenas o Almoxarifado/Recebimento pode acessar esta função.");

        var _statusAggregationService = HttpContext.RequestServices.GetRequiredService<IStatusAggregationService>();

        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.PoGroups)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        var poGroup = request.PoGroups.FirstOrDefault(g => g.Id == dto.RequestPoGroupId);
        if (poGroup == null) return BadRequest(new { message = "Grupo P.O. não encontrado." });

        // Unified post-PO operational flow: strictly from PAYMENT_COMPLETED for all types
        string[] requiredStatuses = new[] { "PAYMENT_COMPLETED" };

        if (!requiredStatuses.Contains(poGroup.Status))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Inválida",
                Detail = $"O grupo não está em um status válido para mover para recebimento. Status atual: {poGroup.Status}.",
                Status = 400
            });
        }

        var oldStatusId = request.StatusId;
        poGroup.Status = "WAITING_RECEIPT";
        poGroup.UpdatedAtUtc = DateTime.UtcNow;

        var targetStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == "WAITING_RECEIPT");
        if (targetStatus == null) return StatusCode(500, "Status 'WAITING_RECEIPT' não configurado.");

        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = CurrentUserId,
            ActionTaken = "MOVE_TO_RECEIPT",
            PreviousStatusId = oldStatusId, // Keep parent status id for tracking
            NewStatusId = targetStatus.Id,
            Comment = $"[Grupo P.O.: {poGroup.SupplierNameSnapshot ?? "N/A"} | GroupId: {poGroup.Id.ToString().Substring(0, 8)}] " + (dto.Comment ?? "Pedido movido para aguardando recibo."),
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(history);

        await _context.SaveChangesAsync();
        await _statusAggregationService.AggregateRequestStatusAsync(request.Id);

        return Ok(new { Message = "Grupo movido para aguardando recibo.", StatusCode = "WAITING_RECEIPT" });
    }

    [HttpPost("{id}/operational/confirm-receiving")]
    public async Task<IActionResult> ConfirmReceiving(Guid id, [FromBody] ConfirmReceivingDto dto)
    {
        var actorId = CurrentUserId;
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Receiving))
            return StatusCode(403, "Apenas o Almoxarifado/Recebimento pode confirmar o recebimento.");

        var _statusAggregationService = HttpContext.RequestServices.GetRequiredService<IStatusAggregationService>();

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var request = await _context.Requests
                .Include(r => r.RequestType)
                .Include(r => r.Status)
                .Include(r => r.PoGroups)
                    .ThenInclude(g => g.LineItems)
                        .ThenInclude(li => li.SelectedQuotationItem)
                            .ThenInclude(qi => qi.LineItemStatus)
                .Include(r => r.PoGroups)
                    .ThenInclude(g => g.LineItems)
                        .ThenInclude(li => li.LineItemStatus)
                .Include(r => r.LineItems)
                    .ThenInclude(li => li.LineItemStatus)
                .Include(r => r.Quotations)
                    .ThenInclude(q => q.Items)
                        .ThenInclude(qi => qi.LineItemStatus)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null) return NotFound();

            var poGroup = request.PoGroups.FirstOrDefault(g => g.Id == dto.RequestPoGroupId);
            if (poGroup == null) return BadRequest(new { message = "Grupo P.O. não encontrado." });

            // Status Rule: Must be in WAITING_RECEIPT, IN_FOLLOWUP, PAYMENT_COMPLETED, or WAITING_SUPPLIER_DELIVERY to confirm receiving
            var allowedStatuses = new[] { "WAITING_RECEIPT", "IN_FOLLOWUP", RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.WaitingSupplierDelivery };
            if (!allowedStatuses.Contains(poGroup.Status))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Ação Inválida",
                    Detail = $"O grupo não está em um status válido para confirmação de recebimento. Status atual: {poGroup.Status}.",
                    Status = 400
                });
            }

            // Determine next status: WAITING_RECEIPT (all received) or IN_FOLLOWUP (partial)
            // Business rule: Receiving NEVER moves to COMPLETED
            string nextStatusCode = RequestWorkflowHelper.DetermineGroupPostConfirmReceivingStatus(poGroup);
            var targetStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == nextStatusCode);
            if (targetStatus == null) return StatusCode(500, $"Status '{nextStatusCode}' não configurado.");

            var oldStatusId = request.StatusId;
            poGroup.Status = targetStatus.Code;
            poGroup.UpdatedAtUtc = DateTime.UtcNow;
            
            request.UpdatedAtUtc = DateTime.UtcNow;
            request.UpdatedByUserId = actorId;

            // Create Status History entry
            var history = new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = actorId,
                ActionTaken = "CONFIRM_RECEIVING",
                PreviousStatusId = oldStatusId,
                NewStatusId = targetStatus.Id,
                Comment = $"[Grupo P.O.: {poGroup.SupplierNameSnapshot ?? "N/A"} | GroupId: {poGroup.Id.ToString().Substring(0, 8)}] " + 
                          (dto.Comment ?? (nextStatusCode == "WAITING_RECEIPT"
                              ? "Recebimento de itens confirmado com sucesso. Aguardando recibo do fornecedor."
                              : "Recebimento parcial confirmado. Itens pendentes movidos para acompanhamento.")),
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.RequestStatusHistories.Add(history);

            await _context.SaveChangesAsync();
            
            // Re-aggregate the parent status
            await _statusAggregationService.AggregateRequestStatusAsync(request.Id);
            
            await transaction.CommitAsync();

            // Notification dispatch
            try
            {
                var actor = await _context.Users.FindAsync(actorId);
                await _orchestrator.EmitAsync(new WorkflowEvent
                {
                    EventCode = WorkflowEventCodes.RequestFinalized, // Reuse existing event code for receiving confirmation
                    RequestId = request.Id,
                    RequestNumber = request.RequestNumber ?? "S/N",
                    RequestTitle = request.Title ?? "",
                    TargetStatusCode = nextStatusCode,
                    ActionTaken = "CONFIRM_RECEIVING",
                    ActorUserId = actorId,
                    ActorName = actor?.FullName ?? "Sistema",
                    CorrelationId = history.Id,
                    RequesterId = request.RequesterId,
                    BuyerId = request.BuyerId,
                    AreaApproverId = request.AreaApproverId,
                    FinalApproverId = request.FinalApproverId,
                    PlantId = request.PlantId
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-critical: notification dispatch failed for ConfirmReceiving on Request {RequestId}", request.Id);
            }

            return Ok(new { Message = "Recebimento confirmado com sucesso.", StatusCode = nextStatusCode });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new ProblemDetails
            {
                Title = "Erro na Confirmação de Recebimento",
                Detail = ex.Message,
                Status = 500
            });
        }
    }

    [HttpPost("{id}/operational/finalize")]
    public async Task<IActionResult> FinalizeRequest(Guid id, [FromBody] ApprovalActionDto dto)
    {
        var actorId = CurrentUserId;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var request = await _context.Requests
                .Include(r => r.RequestType)
                .Include(r => r.Status)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null) return NotFound();

            // Idempotency check: If already completed, just return success without history/timestamp changes
            if (request.Status!.Code == "COMPLETED")
            {
                return Ok(new { Message = "Pedido já finalizado.", StatusCode = "COMPLETED" });
            }

            // Status Rule: Finance finalization ONLY from WAITING_RECEIPT
            if (request.Status!.Code != "WAITING_RECEIPT")
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Ação Inválida",
                    Detail = $"O pedido deve estar em 'Aguardando Recibo do Fornecedor' para ser finalizado. Status atual: {request.Status.Code}.",
                    Status = 400
                });
            }

            // ── Phase 8: QUOTATION-specific finalization guards ──
            if (request.RequestType!.Code == RequestConstants.Types.Quotation)
            {
                // Reload with full relationships for guard checks
                await _context.Entry(request).Collection(r => r.LineItems).LoadAsync();
                await _context.Entry(request).Collection(r => r.ApprovalBatches).LoadAsync();
                await _context.Entry(request).Collection(r => r.PoGroups).LoadAsync();

                var activeItems = request.LineItems.Where(li => !li.IsDeleted).ToList();

                // Guard A: All items must be quotation-terminal
                var nonTerminalItems = activeItems.Where(li =>
                    li.QuotationLifecycleStatus == null ||
                    li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending ||
                    li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.BatchAssigned ||
                    li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed
                ).ToList();

                if (nonTerminalItems.Any())
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Finalização Bloqueada",
                        Detail = $"Existem {nonTerminalItems.Count} item(ns) com ciclo de cotação pendente. " +
                                 "Todos os itens devem estar aprovados ou aceitos como não cotados.",
                        Status = 400
                    });
                }

                // Guard B: No active approval batches
                var activeBatchStatuses = new[]
                {
                    RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
                    RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
                    RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
                    RequestConstants.ApprovalBatchStatuses.FinalAdjustment
                };
                var activeBatchesInApproval = request.ApprovalBatches
                    .Where(b => activeBatchStatuses.Contains(b.Status)).ToList();

                if (activeBatchesInApproval.Any())
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Finalização Bloqueada",
                        Detail = $"Existem {activeBatchesInApproval.Count} lote(s) de aprovação em andamento.",
                        Status = 400
                    });
                }

                // Guard C: All active PO groups must be ready to finalize
                var activePoGroups = request.PoGroups
                    .Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled).ToList();

                var blockedGroups = activePoGroups
                    .Where(g => !RequestConstants.PoGroupStatuses.ReadyToFinalize.Contains(g.Status)).ToList();

                if (blockedGroups.Any())
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Finalização Bloqueada",
                        Detail = $"Existem {blockedGroups.Count} grupo(s) P.O. que ainda não estão prontos para " +
                                 $"finalização: {string.Join(", ", blockedGroups.Select(g => $"{g.SupplierNameSnapshot} ({g.Status})"))}.",
                        Status = 400
                    });
                }
            }

            // Receipt validation: Supplier financial receipt is mandatory
            if (!await HasAttachmentAsync(id, RequestAttachment.TYPE_RECEIPT))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Ação Bloqueada",
                    Detail = "É necessário anexar o Recibo Fiscal do Fornecedor (Fatura, Recibo, ou VD) antes de finalizar o pedido.",
                    Status = 400
                });
            }

            // Target status is always COMPLETED — Finance finalization is the terminal action
            var targetStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == "COMPLETED");
            if (targetStatus == null) return StatusCode(500, "Status 'COMPLETED' não configurado.");

            var oldStatusId = request.StatusId;
            request.StatusId = targetStatus.Id;
            request.UpdatedAtUtc = DateTime.UtcNow;
            request.UpdatedByUserId = actorId;

            // Phase 8: Mark all active PO groups as COMPLETED for QUOTATION requests
            if (request.RequestType!.Code == RequestConstants.Types.Quotation)
            {
                // PoGroups already loaded by guard block above
                var poGroupsToComplete = request.PoGroups
                    .Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled &&
                                g.Status != RequestConstants.PoGroupStatuses.Completed).ToList();
                foreach (var group in poGroupsToComplete)
                {
                    group.Status = RequestConstants.PoGroupStatuses.Completed;
                    group.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            // Create Status History entry
            var history = new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = actorId,
                ActionTaken = "FINALIZE",
                PreviousStatusId = oldStatusId,
                NewStatusId = targetStatus.Id,
                Comment = dto.Comment ?? "Pedido finalizado pelo Financeiro. Recibo do fornecedor anexado.",
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.RequestStatusHistories.Add(history);

            // Persistence
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // [TEMPORARY NON-CENTRAL HOOK] FinalizeRequest has its own inline transaction
            // and does not go through ApplyStatusChangeAndSyncItemsAsync.
            // This hook is a temporary architecture exception — see DEC-XXX for future consolidation.
            try
            {
                var actor = await _context.Users.FindAsync(actorId);
                await _orchestrator.EmitAsync(new WorkflowEvent
                {
                    EventCode = WorkflowEventCodes.RequestFinalized,
                    RequestId = request.Id,
                    RequestNumber = request.RequestNumber ?? "S/N",
                    RequestTitle = request.Title ?? "",
                    TargetStatusCode = "COMPLETED",
                    ActionTaken = "FINALIZE",
                    ActorUserId = actorId,
                    ActorName = actor?.FullName ?? "Sistema",
                    CorrelationId = history.Id,
                    RequesterId = request.RequesterId,
                    BuyerId = request.BuyerId,
                    AreaApproverId = request.AreaApproverId,
                    FinalApproverId = request.FinalApproverId,
                    PlantId = request.PlantId
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-critical: notification dispatch failed for FinalizeRequest on Request {RequestId}", request.Id);
            }

            return Ok(new { Message = "Pedido finalizado com sucesso.", StatusCode = "COMPLETED" });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new ProblemDetails 
            { 
                Title = "Erro na Finalização", 
                Detail = ex.Message, 
                Status = 500 
            });
        }
    }
    
    [HttpPatch("{id}/supplier")]
    public async Task<IActionResult> UpdateRequestSupplier(Guid id, [FromBody] UpdateLineItemSupplierDto dto)
    {
        var actorId = CurrentUserId;

        var request = await _context.Requests
            .Include(r => r.Status)
            .FirstOrDefaultAsync(r => r.Id == id);
            
        if (request == null) return NotFound();

        // Status Rule: Only DRAFT, Adjustment or WAITING_QUOTATION statuses can be edited
        if (request.Status!.Code != "DRAFT" && request.Status!.Code != "AREA_ADJUSTMENT" && request.Status!.Code != "FINAL_ADJUSTMENT" && request.Status!.Code != "WAITING_QUOTATION")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Este pedido não permite alteração de fornecedor neste status.", 
                Status = 409 
            });
        }

        if (dto.SupplierId.HasValue)
        {
            var supplier = await _context.Suppliers.FindAsync(dto.SupplierId.Value);
            if (supplier == null) return BadRequest("Fornecedor inválido.");
            
            request.SupplierId = supplier.Id;
        }
        else
        {
            request.SupplierId = null;
        }

        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/quotation/complete")]
    public async Task<IActionResult> CompleteQuotation(Guid id, [FromBody] ApprovalActionDto dto)
    {
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .Include(r => r.Attachments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        // Phase 5.5 Compatibility Audit Fix: Block request-level CompleteQuotation for QUOTATION type
        if (request.RequestType?.Code == RequestConstants.Types.Quotation)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Obsoleta",
                Detail = "Fluxo de cotação por pedido foi substituído pelo envio de lotes de aprovação. Use o wizard de cotação para criar/submeter um ApprovalBatch.",
                Status = 400
            });
        }

        // 1. Validation Logic: Prioritize Quotation-based model if quotations exist
        bool hasSavedQuotations = request.Quotations.Any();
        bool hasLegacyItems = request.LineItems.Any(l => !l.IsDeleted);

        if (hasSavedQuotations)
        {
            // At least one quotation must be structurally complete for the workflow to proceed
            bool anyCompleteQuotation = request.Quotations.Any(q => 
                q.SupplierId > 0 && 
                q.Items.Any() && 
                (q.ProformaAttachmentId.HasValue || request.Attachments.Any(a => a.AttachmentTypeCode == RequestAttachment.TYPE_PROFORMA && !a.IsDeleted))
            );

            if (!anyCompleteQuotation)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Ação Bloqueada",
                    Detail = "É necessário que pelo menos uma cotação salva esteja completa (com fornecedor, itens e documento anexo) antes de concluir.",
                    Status = 400
                });
            }

            // ═══════════════════════════════════════════════════════════════
            // MAPPING COVERAGE VALIDATION
            // Every active request line item must be mapped by at least one
            // quotation item. Without this, the Area Approval matrix shows
            // all cells as "— não cotado —", creating a dead-end.
            // ═══════════════════════════════════════════════════════════════
            var activeRequestLineItemIds = request.LineItems
                .Where(l => !l.IsDeleted)
                .Select(l => l.Id)
                .ToHashSet();

            if (activeRequestLineItemIds.Any())
            {
                var mappedLineItemIds = request.Quotations
                    .SelectMany(q => q.Items)
                    .Where(qi => qi.MappedRequestLineItemId.HasValue && 
                                 (qi.ReconciliationStatus == "MAPPED" || qi.ReconciliationStatus == "SUBSTITUTE"))
                    .Select(qi => qi.MappedRequestLineItemId!.Value)
                    .ToHashSet();

                var unmappedCount = activeRequestLineItemIds.Except(mappedLineItemIds).Count();

                if (unmappedCount > 0)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Mapeamento Incompleto",
                        Detail = "Não é possível concluir a etapa de cotação. Ainda existem itens solicitados sem cotação.",
                        Status = 400,
                        Extensions = { ["unmappedItemCount"] = unmappedCount }
                    });
                }
            }
        }
        else if (hasLegacyItems)
        {
            // Legacy/Payment Fallback: check request-level line items and metadata
            var allItemsHaveSupplier = request.LineItems.Where(l => !l.IsDeleted).All(l => l.SupplierId.HasValue || !string.IsNullOrWhiteSpace(l.SupplierName));
            
            if (request.SupplierId == null && !allItemsHaveSupplier)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Fornecedor Ausente",
                    Detail = "É necessário selecionar um fornecedor (no cabeçalho ou em todos os itens) antes de concluir a cotação.",
                    Status = 400
                });
            }

            if (!await HasAttachmentAsync(id, RequestAttachment.TYPE_PROFORMA))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Ação Bloqueada",
                    Detail = "É necessário anexar a Proforma antes de concluir a cotação.",
                    Status = 400
                });
            }
        }
        else
        {
            // No items and no quotations
            return BadRequest(new ProblemDetails
            {
                Title = "Pedido sem Itens",
                Detail = "O pedido deve conter pelo menos uma cotação com itens ou itens diretos no pedido para ser concluído.",
                Status = 400
            });
        }

        // 1. Role-based Authorization: strictly enforce Buyer role for concluding quotation
        if (!CurrentUserRoles.Contains(RoleConstants.Buyer))
            return StatusCode(403, "Apenas o Comprador pode concluir a etapa de cotação.");


        // ═══════════════════════════════════════════════════════════════
        // REWORK-AWARE TRANSITION
        // Detect if this is a rework resubmission (from AREA_ADJUSTMENT or
        // FINAL_ADJUSTMENT) and use a distinct action + audit comment.
        // ═══════════════════════════════════════════════════════════════
        var isRework = request.Status!.Code is "AREA_ADJUSTMENT" or "FINAL_ADJUSTMENT";
        var transitionAction = isRework ? "QUOTATION_RESUBMITTED" : "COMPLETE_QUOTATION";
        var transitionComment = isRework
            ? "Cotação reajustada pelo comprador e reenviada para aprovação da área."
            : "Cotação concluída e enviada para aprovação da área.";

        var result = await ProcessQuotationTransition(id, transitionAction, "WAITING_AREA_APPROVAL", new[] { "WAITING_QUOTATION", "AREA_ADJUSTMENT", "FINAL_ADJUSTMENT" }, dto.Comment ?? transitionComment, transitionComment);
        
        // R1 FIX: Wrap notification dispatch in try-catch so notification failures
        // do not propagate and mask the successful quotation transition.
        if (result is OkObjectResult)
        {
            try
            {
                await _orchestrator.EmitAsync(new WorkflowEvent
                {
                    EventCode = isRework ? WorkflowEventCodes.QuotationResubmitted : WorkflowEventCodes.QuotationCompleted,
                    RequestId = request.Id,
                    RequestNumber = request.RequestNumber ?? "S/N",
                    RequestTitle = request.Title ?? "",
                    TargetStatusCode = "WAITING_AREA_APPROVAL",
                    ActionTaken = transitionAction,
                    ActorUserId = CurrentUserId,
                    ActorName = (await _context.Users.FindAsync(CurrentUserId))?.FullName ?? "Sistema",
                    CorrelationId = Guid.NewGuid(), // No history entry for this path; generate unique ID
                    RequesterId = request.RequesterId,
                    BuyerId = request.BuyerId,
                    AreaApproverId = request.AreaApproverId,
                    FinalApproverId = request.FinalApproverId,
                    DepartmentId = request.DepartmentId,
                    PlantId = request.PlantId
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-critical: notification dispatch failed for {EventType} on Request {RequestId}", transitionAction, request.Id);
            }
        }

        return result;
    }

    [HttpPost("{requestId}/quotations/{quotationId}/select")]
    public async Task<IActionResult> SelectQuotation(Guid requestId, Guid quotationId)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // Phase B: winner selection is part of the area review — requires area-manager
        // titularity for this request's department/plant (or admin / legacy nominee).
        if (!await CanActAsAreaManagerAsync(actorId, request))
        {
            if (CurrentUserRoles.Contains(RoleConstants.FinalApprover))
                return StatusCode(403, "O Aprovador Final não pode alterar o vencedor selecionado pela área.");

            return StatusCode(403, "Você não é responsável pelo departamento/planta deste pedido.");
        }

        // Status Rule: Only WAITING_AREA_APPROVAL allows winning selection
        if (request.Status!.Code != "WAITING_AREA_APPROVAL")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Operação bloqueada: a seleção de vencedor só é permitida no status Aguardando Aprovação Final.", 
                Status = 409 
            });
        }

        var targetQuotation = request.Quotations.FirstOrDefault(q => q.Id == quotationId);
        if (targetQuotation == null) return NotFound(new ProblemDetails { Title = "Cotação não encontrada no pedido.", Status = 404 });

        // Enforce single selection
        foreach (var q in request.Quotations)
        {
            q.IsSelected = (q.Id == quotationId);
        }

        request.SelectedQuotationId = quotationId;

        // Synchronize header fields from winning quotation
        request.SupplierId = targetQuotation.SupplierId;
        request.EstimatedTotalAmount = targetQuotation.TotalAmount;

        var matchedCurrency = await _context.Currencies
            .FirstOrDefaultAsync(c => c.Code.ToUpper() == targetQuotation.Currency.ToUpper());
            
        bool currencySynced = false;
        if (matchedCurrency != null)
        {
            request.CurrencyId = matchedCurrency.Id;
            currencySynced = true;
        }
        else
        {
            await _adminLog.WriteAsync("Warning", "RequestsController", "REQUEST_SYNC_WINNER", 
                $"Currency synchronization restricted: Code '{targetQuotation.Currency}' not found in master data for Request {request.RequestNumber}. Header CurrencyId remains unchanged.");
        }

        await _adminLog.WriteAsync("Info", "RequestsController", "REQUEST_SYNC_WINNER", 
            $"Header synchronized with winning quotation {targetQuotation.SupplierNameSnapshot}. SupplierId: {request.SupplierId}, Amount: {request.EstimatedTotalAmount}, CurrencySynced: {currencySynced}.");

        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;

        // Synchronize Quotation Items to Request Line Items
        // 1. Soft-delete any existing active items to ensure a clean slate
        foreach (var lineItem in request.LineItems.Where(li => !li.IsDeleted))
        {
            lineItem.IsDeleted = true;
            lineItem.UpdatedAtUtc = DateTime.UtcNow;
            lineItem.UpdatedByUserId = actorId;
        }

        // 2. Clone the winning quotation items into the request
        int nextLineNumber = 1;
        var orderedQuoteItems = targetQuotation.Items.OrderBy(i => i.LineNumber == 0 ? int.MaxValue : i.LineNumber).ToList();
        
        foreach (var quotationItem in orderedQuoteItems)
        {
            var newRequestLineItem = new RequestLineItem
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                LineNumber = nextLineNumber++,
                ItemPriority = "MEDIUM",
                Description = quotationItem.Description,
                Quantity = quotationItem.Quantity,
                UnitId = quotationItem.UnitId,
                UnitPrice = quotationItem.UnitPrice,
                TotalAmount = quotationItem.LineTotal,
                CurrencyId = matchedCurrency?.Id ?? request.CurrencyId,
                IvaRateId = quotationItem.IvaRateId,
                LineItemStatusId = 1, // WAITING_QUOTATION status default
                SupplierName = targetQuotation.SupplierNameSnapshot,
                IsDeleted = false,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actorId
            };
            
            _context.RequestLineItems.Add(newRequestLineItem);
        }

        // Record selection update in history for audit
        var itemHistory = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "COTACAO_SELECIONADA",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId, // Preserve status
            Comment = $"Cotação {targetQuotation.SupplierNameSnapshot} ({(targetQuotation.DocumentNumber ?? "S/N")}) selecionada como vencedora por {user.FullName}.",
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(itemHistory);

        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<IActionResult> ProcessCommonOperationalTransition(Guid id, string action, string targetStatusCode, string[] requiredCurrentStatusCodes, string? comment, string successMessage)
    {
        return await ProcessTransition(id, action, targetStatusCode, requiredCurrentStatusCodes, comment, successMessage, new[] { "PAYMENT", "QUOTATION" });
    }

    private async Task<IActionResult> ProcessOperationalTransition(Guid id, string action, string targetStatusCode, string[] requiredCurrentStatusCodes, string? comment, string successMessage)
    {
        return await ProcessTransition(id, action, targetStatusCode, requiredCurrentStatusCodes, comment, successMessage, new[] { "PAYMENT" });
    }

    private async Task<IActionResult> ProcessQuotationTransition(Guid id, string action, string targetStatusCode, string[] requiredCurrentStatusCodes, string? comment, string successMessage)
    {
        return await ProcessTransition(id, action, targetStatusCode, requiredCurrentStatusCodes, comment, successMessage, new[] { "QUOTATION" });
    }

    private async Task<IActionResult> ProcessTransition(Guid id, string action, string targetStatusCode, string[] requiredCurrentStatusCodes, string? comment, string successMessage, string[] allowedTypeCodes)
    {
        var actorId = CurrentUserId;

        // Role-based Authorization fallback for operational actions
        var roles = CurrentUserRoles;
        if (action == "REGISTER_PO" && !roles.Contains(RoleConstants.Buyer))
            return StatusCode(403, "Apenas o Comprador pode registrar a P.O.");
        if ((action == "SCHEDULE_PAYMENT" || action == "COMPLETE_PAYMENT" || action == "FINALIZE") && !roles.Contains(RoleConstants.Finance))
            return StatusCode(403, "Apenas o Financeiro pode gerir o fluxo de pagamento e finalização.");
        if ((action == "MOVE_TO_RECEIPT" || action == "CONFIRM_RECEIVING") && !roles.Contains(RoleConstants.Receiving))
            return StatusCode(403, "Apenas o Almoxarifado/Recebimento pode confirmar o recebimento.");
        if (action == "CONFIRM_DELIVERY" && !roles.Contains(RoleConstants.Receiving) && !roles.Contains(RoleConstants.Buyer))
            return StatusCode(403, "Apenas o Almoxarifado ou Comprador pode confirmar a entrega.");
        if (action == "REREGISTER_PO" && !roles.Contains(RoleConstants.Buyer))
            return StatusCode(403, "Apenas o Comprador pode re-registrar a P.O.");

        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        if (!allowedTypeCodes.Contains(request.RequestType!.Code))
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = $"Esta ação só é permitida para pedidos de: {string.Join(", ", allowedTypeCodes)}.", Status = 400 });

        if (!requiredCurrentStatusCodes.Contains(request.Status!.Code))
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = $"O pedido não está em um status válido para esta ação. Status permitidos: {string.Join(", ", requiredCurrentStatusCodes)}.", Status = 400 });

        return await ApplyStatusChangeAndSyncItemsAsync(request, targetStatusCode, action, comment ?? string.Empty, successMessage, actorId);
    }

    /// <summary>
    /// Recalculates EstimatedTotalAmount from active line items, applying the global
    /// DiscountAmount proportionally across the taxable base and IVA.
    /// </summary>
    private async Task RecalculateEstimatedTotalAsync(Request request, Guid requestId)
    {
        var activeItems = await _context.RequestLineItems
            .Where(l => l.RequestId == requestId && !l.IsDeleted)
            .ToListAsync();

        var grossTotal = activeItems.Sum(l => l.TotalAmount);
        var globalDiscount = request.DiscountAmount;

        if (globalDiscount > 0 && grossTotal > 0)
        {
            var allIvaRates = await _context.IvaRates.AsNoTracking().ToListAsync();
            decimal grossBase = 0;
            decimal ivaTotal = 0;
            foreach (var li in activeItems)
            {
                var netItem = Round2((li.Quantity * li.UnitPrice) - (li.DiscountAmount ?? 0));
                grossBase += netItem;
                var liIva = li.IvaRateId.HasValue ? allIvaRates.FirstOrDefault(r => r.Id == li.IvaRateId.Value) : null;
                if (liIva != null) ivaTotal += Round2(netItem * (liIva.RatePercent / 100m));
            }
            var taxableBase = Math.Max(0, grossBase - globalDiscount);
            var discountRatio = grossBase > 0 ? (taxableBase / grossBase) : 1m;
            var adjustedIva = Round2(ivaTotal * discountRatio);
            request.EstimatedTotalAmount = Math.Max(0, Round2(taxableBase + adjustedIva));
        }
        else
        {
            request.EstimatedTotalAmount = grossTotal;
        }
    }

    /// <summary>
    /// Central hub for all major workflow status transitions.
    /// After persisting the transition, emits a WorkflowEvent to the notification orchestrator.
    /// </summary>
    /// <param name="overrideEventCode">
    /// Optional event code override for ambiguous transitions (e.g., REJECT → REJECTED
    /// could be either area or final rejection). When null, the event code is auto-resolved
    /// from the (actionTaken, targetStatusCode) tuple.
    /// </param>
    private async Task<IActionResult> ApplyStatusChangeAndSyncItemsAsync(
        Request request, 
        string targetStatusCode, 
        string actionTaken, 
        string historyComment, 
        string successMessage, 
        Guid actorUserId,
        string? overrideEventCode = null)
    {
        var targetStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == targetStatusCode);
        if (targetStatus == null) return StatusCode(500, $"Status '{targetStatusCode}' não configurado no sistema.");

        // If the request is being cancelled or rejected and was linked to a Contract Obligation, we must revert it
        if ((targetStatusCode == "CANCELLED" || targetStatusCode == "REJECTED") && request.ContractPaymentObligationId.HasValue)
        {
            var obligation = await _context.ContractPaymentObligations.FindAsync(request.ContractPaymentObligationId.Value);
            if (obligation != null && obligation.StatusCode != "PAID")
            {
                obligation.StatusCode = "PENDING";
                
                _context.ContractHistories.Add(new ContractHistory
                {
                    ContractId = obligation.ContractId,
                    EventType = "OBLIGATION_RESET",
                    Comment = $"Obrigação {obligation.SequenceNumber} revertida para PENDENTE porque o pedido gerado foi {targetStatusCode.ToLower()}.",
                    OccurredAtUtc = DateTime.UtcNow,
                    ActorUserId = actorUserId
                });
            }
        }

        var oldStatusId = request.StatusId;

        request.StatusId = targetStatus.Id;
        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorUserId;

        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actorUserId,
            ActionTaken = actionTaken,
            PreviousStatusId = oldStatusId,
            NewStatusId = targetStatus.Id,
            Comment = historyComment,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(history);

        // Auto-sync Line Items (Centralized logic)
        await SyncLineItemStatusesAsync(request, targetRequestStatusCode: targetStatusCode, actorUserId: actorUserId);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // A row this approval expected to update/delete no longer matches the database
            // (e.g., two eligible approvers acting at once). The transaction has already been
            // rolled back by EF; surface a structured 409 instead of a 500 with a stack trace.
            // Other DbUpdateException kinds are intentionally NOT treated as concurrency.
            _logger.LogError(ex,
                "Approval concurrency conflict on Request {RequestId} (Action: {ActionTaken}, Target: {TargetStatus}, TraceId: {TraceId})",
                request.Id, actionTaken, targetStatusCode, HttpContext.TraceIdentifier);

            return Conflict(new ProblemDetails
            {
                Title = "Conflito de concorrência",
                Detail = "O pedido foi alterado por outra operação. Atualize os dados e tente novamente.",
                Status = 409,
                Extensions =
                {
                    ["code"] = "APPROVAL_CONCURRENCY_CONFLICT",
                    ["traceId"] = HttpContext.TraceIdentifier
                }
            });
        }

        // --- Workflow Notification Emission (fire-and-forget, non-blocking) ---
        try
        {
            var eventCode = overrideEventCode ?? ResolveEventCode(actionTaken, targetStatusCode);
            if (eventCode != null)
            {
                var actor = await _context.Users.FindAsync(actorUserId);
                var workflowEvent = new WorkflowEvent
                {
                    EventCode = eventCode,
                    RequestId = request.Id,
                    RequestNumber = request.RequestNumber ?? "S/N",
                    RequestTitle = request.Title ?? "",
                    TargetStatusCode = targetStatusCode,
                    ActionTaken = actionTaken,
                    ActorUserId = actorUserId,
                    ActorName = actor?.FullName ?? "Sistema",
                    Comment = historyComment,
                    CorrelationId = history.Id,
                    RequesterId = request.RequesterId,
                    BuyerId = request.BuyerId,
                    AreaApproverId = request.AreaApproverId,
                    FinalApproverId = request.FinalApproverId,
                    DepartmentId = request.DepartmentId,
                    PlantId = request.PlantId
                };
                await _orchestrator.EmitAsync(workflowEvent);

                // Auto-confirm for the requester (this event bypasses the "no-self-notify" rule)
                if (actionTaken is "SUBMIT" or "RESUBMIT")
                {
                    var confirmationEvent = new WorkflowEvent
                    {
                        EventCode = WorkflowEventCodes.SubmissionConfirmed,
                        RequestId = request.Id,
                        RequestNumber = request.RequestNumber ?? "S/N",
                        RequestTitle = request.Title ?? "",
                        TargetStatusCode = targetStatusCode,
                        ActionTaken = actionTaken,
                        ActorUserId = actorUserId,
                        ActorName = actor?.FullName ?? "Sistema",
                        Comment = historyComment,
                        // Use a slightly different CorrelationId to avoid deduplicating against the primary event if it happens to target the same user somehow
                        CorrelationId = Guid.NewGuid(), 
                        RequesterId = request.RequesterId,
                        BuyerId = request.BuyerId,
                        AreaApproverId = request.AreaApproverId,
                        FinalApproverId = request.FinalApproverId,
                        PlantId = request.PlantId
                    };
                    await _orchestrator.EmitAsync(confirmationEvent);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-critical: workflow notification emission failed for Request {RequestId} (Action: {ActionTaken})", request.Id, actionTaken);
            successMessage = $"{successMessage} Aviso técnica: Falha ao expedir e-mail/notificação no motor de regras.";
            
            // Fire-and-forget sync write to admin log
            _ = _adminLog.WriteAsync(
                "Error",
                "RequestsController",
                "WORKFLOW_NOTIFICATION_FAILED",
                $"Falha na emissão de notificações para Pedido {request.RequestNumber} (Action: {actionTaken})",
                exceptionDetail: ex.Message
            );
        }

        return Ok(new { Message = successMessage, StatusCode = targetStatusCode });
    }

    /// <summary>
    /// Resolves the canonical WorkflowEventCode from the (actionTaken, targetStatusCode) tuple.
    /// Returns null for unmapped transitions (no notification will be sent).
    /// For ambiguous transitions (e.g., REJECT → REJECTED), callers should pass overrideEventCode instead.
    /// </summary>
    private static string? ResolveEventCode(string actionTaken, string targetStatusCode)
    {
        return (actionTaken, targetStatusCode) switch
        {
            ("SUBMIT", "WAITING_AREA_APPROVAL") => WorkflowEventCodes.RequestSubmitted,
            ("SUBMIT", "WAITING_QUOTATION") => WorkflowEventCodes.QuotationAwaitingBuyer,
            ("RESUBMIT", "WAITING_AREA_APPROVAL") => WorkflowEventCodes.RequestSubmitted,
            ("RESUBMIT", "WAITING_FINAL_APPROVAL") => WorkflowEventCodes.AreaApproved,
            ("APPROVE", "WAITING_FINAL_APPROVAL") => WorkflowEventCodes.AreaApproved,
            ("APPROVE", "APPROVED") => WorkflowEventCodes.FinalApproved,
            ("REGISTER_PO", "PO_ISSUED") => WorkflowEventCodes.PoRegistered,
            ("REREGISTER_PO", "PO_ISSUED") => WorkflowEventCodes.PoCorrectionCompleted,
            ("SCHEDULE_PAYMENT", "PAYMENT_SCHEDULED") => WorkflowEventCodes.PaymentScheduled,
            ("COMPLETE_PAYMENT", "PAYMENT_COMPLETED") => WorkflowEventCodes.PaymentCompleted,
            ("CANCELLED", "CANCELLED") => WorkflowEventCodes.RequestCancelled,
            // Rework resubmission (buyer corrected quotation after area/final return)
            ("QUOTATION_RESUBMITTED", "WAITING_AREA_APPROVAL") => WorkflowEventCodes.QuotationResubmitted,
            // REJECT and REQUEST_ADJUSTMENT are ambiguous (area vs. final) — handled via overrideEventCode
            _ => null
        };
    }

    private async Task SyncLineItemStatusesAsync(Request request, string targetRequestStatusCode, Guid actorUserId)
    {
        if (request.LineItems == null)
        {
            await _context.Entry(request).Collection(r => r.LineItems).LoadAsync();
        }

        string? targetItemStatus = null;
        switch (targetRequestStatusCode)
        {
            case "WAITING_QUOTATION":
                targetItemStatus = "WAITING_QUOTATION";
                break;
            case "APPROVED":
            case "WAITING_COST_CENTER":
                targetItemStatus = "PENDING";
                break;
            case "PO_ISSUED":
            case "WAITING_PO_CORRECTION":
            case "PAYMENT_SCHEDULED":
            case "PAYMENT_COMPLETED":
                targetItemStatus = "WAITING_ORDER";
                break;
            case "WAITING_RECEIPT":
            case "IN_FOLLOWUP":
                targetItemStatus = "ORDERED";
                break;
            case "CANCELLED":
            case "REJECTED":
                targetItemStatus = "CANCELLED";
                break;
        }

        if (targetItemStatus != null)
        {
            _logger.LogInformation("Syncing line item statuses for Request {RequestId} to {TargetItemStatus} (Trigger: Request status set to {RequestStatusCode})", 
                request.Id, targetItemStatus, targetRequestStatusCode);

            var statusEntity = await _context.LineItemStatuses.FirstOrDefaultAsync(s => s.Code == targetItemStatus);
            if (statusEntity != null)
            {
                foreach (var item in (request.LineItems ?? new List<RequestLineItem>()).Where(l => !l.IsDeleted))
                {
                    // Look up current status of the item
                    var currentStatusEntity = await _context.LineItemStatuses.FirstOrDefaultAsync(s => s.Id == item.LineItemStatusId);
                    var currentCode = currentStatusEntity?.Code;

                    // Preserve manually or functionally advanced statuses
                    if (currentCode == "RECEIVED" || currentCode == "PARTIALLY_RECEIVED" || currentCode == "ORDERED" || currentCode == "CANCELLED")
                    {
                        continue;
                    }

                    item.LineItemStatusId = statusEntity.Id;
                    item.UpdatedAtUtc = DateTime.UtcNow;
                    item.UpdatedByUserId = actorUserId;
                }
            }

            // Also sync QuotationItems if it's the authoritative source
            if (request.SelectedQuotationId.HasValue)
            {
                var winningQuotation = await _context.Quotations
                    .Include(q => q.Items)
                    .FirstOrDefaultAsync(q => q.Id == request.SelectedQuotationId.Value);

                if (winningQuotation != null)
                {
                    foreach (var qi in winningQuotation.Items)
                    {
                        if (statusEntity != null)
                        {
                            // Preservation logic for QuotationItems
                            var qiCurrentStatus = await _context.LineItemStatuses.FirstOrDefaultAsync(s => s.Id == qi.LineItemStatusId);
                            var qiCode = qiCurrentStatus?.Code;

                            if (qiCode == "RECEIVED" || qiCode == "PARTIALLY_RECEIVED") continue;

                            qi.LineItemStatusId = statusEntity.Id;
                        }
                    }
                }
            }
        }
    }

    private async Task<bool> HasAttachmentAsync(Guid requestId, string typeCode)
    {
        return await _context.RequestAttachments.AnyAsync(a => a.RequestId == requestId && a.AttachmentTypeCode == typeCode && !a.IsDeleted);
    }

    private async Task<bool> HasGroupAttachmentAsync(Guid poGroupId, string typeCode)
    {
        return await _context.RequestAttachments.AnyAsync(a => a.RequestPoGroupId == poGroupId && a.AttachmentTypeCode == typeCode && !a.IsDeleted);
    }

    private class StageDef
    {
        public string Label { get; set; } = string.Empty;
        public string[] StatusCodes { get; set; } = Array.Empty<string>();
    }

    private List<StageDef> GetQuotationStages() => new()
    {
        new StageDef { Label = "Rascunho", StatusCodes = new[] { "DRAFT", "SUBMITTED" } },
        new StageDef { Label = "Cotação", StatusCodes = new[] { "WAITING_QUOTATION" } },
        new StageDef { Label = "Aprovações", StatusCodes = new[] { "WAITING_AREA_APPROVAL", "AREA_ADJUSTMENT", "WAITING_FINAL_APPROVAL", "FINAL_ADJUSTMENT", "WAITING_COST_CENTER" } },
        new StageDef { Label = "P.O / Contratação", StatusCodes = new[] { "APPROVED", "PO_ISSUED" } },
        new StageDef { Label = "Agendamento", StatusCodes = new[] { "PO_ISSUED" } },
        new StageDef { Label = "Pagamento", StatusCodes = new[] { "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED" } },
        new StageDef { Label = "Recebimento", StatusCodes = new[] { "WAITING_RECEIPT", "IN_FOLLOWUP" } },
        new StageDef { Label = "Concluído", StatusCodes = new[] { "COMPLETED", "QUOTATION_COMPLETED" } }
    };

    private List<StageDef> GetPaymentStages() => new()
    {
        new StageDef { Label = "Rascunho", StatusCodes = new[] { "DRAFT", "SUBMITTED" } },
        new StageDef { Label = "Aprovação Área", StatusCodes = new[] { "WAITING_AREA_APPROVAL", "AREA_ADJUSTMENT" } },
        new StageDef { Label = "Aprovação Final", StatusCodes = new[] { "WAITING_FINAL_APPROVAL", "FINAL_ADJUSTMENT", "WAITING_COST_CENTER" } },
        new StageDef { Label = "Agendamento", StatusCodes = new[] { "APPROVED", "PO_ISSUED" } },
        new StageDef { Label = "Pagamento", StatusCodes = new[] { "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED" } },
        new StageDef { Label = "Recebimento", StatusCodes = new[] { "WAITING_RECEIPT", "IN_FOLLOWUP" } },
        new StageDef { Label = "Concluído", StatusCodes = new[] { "COMPLETED" } }
    };

    private async Task LogOcrExecutionAsync(string fileName, Guid? requestId, ExtractionResultDto? result, Exception? ex)
    {
        try
        {
            bool success = result?.Success ?? false;
            bool isPartial = result?.Metadata?.IsPartial ?? false;
            string status = success ? (isPartial ? "Partial" : "Success") : "Failed";

            string routingStrategy = result?.Metadata?.RoutingStrategy ?? "Unknown";
            string detectedStrategy = routingStrategy.StartsWith("Contract") ? "Contract" : (routingStrategy == "Unknown" ? "Unknown" : "Invoice");
            string routingPath = routingStrategy.Replace("Contract", "");
            if (string.IsNullOrEmpty(routingPath)) routingPath = "Unknown";
            
            string documentType = detectedStrategy == "Contract" ? "contract" : (detectedStrategy == "Invoice" ? "invoice/proforma" : "unknown");

            string? shortError = ex?.Message ?? (!success && result != null ? "Extraction provider returned failure" : null);

            var payloadObj = new
            {
                FileName = fileName,
                RequestId = requestId,
                DocumentType = documentType,
                DetectedStrategy = detectedStrategy,
                RoutingPath = routingPath,
                Provider = result?.ProviderName ?? "OPENAI",
                Model = "gpt-4o-mini",
                NativeTextDetected = result?.Metadata?.NativeTextDetected ?? false,
                DetailMode = result?.Metadata?.DetailMode,
                PagesProcessed = result?.Metadata?.PagesProcessed ?? 0,
                PromptTokens = result?.Metadata?.PromptTokens ?? 0,
                CompletionTokens = result?.Metadata?.CompletionTokens ?? 0,
                TotalTokens = result?.Metadata?.TotalTokens ?? 0,
                ChunkCount = result?.Metadata?.ChunkCount ?? 0,
                ExecutionStatus = status,
                BilledCompany = result?.Header?.BilledCompanyName,
                ErrorSummary = shortError
            };

            var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payloadObj, options);
            
            string message = $"OCR Execution {status}: {detectedStrategy} via {routingPath}";
            string level = status == "Failed" ? "Warning" : "Information";

            await _adminLog.WriteAsync(level, "OCR_Pipeline", "OCR_EXECUTION", message, ex?.ToString(), payloadJson);
        }
        catch (Exception logEx)
        {
            _logger.LogWarning(logEx, "Failed to write OCR audit log for file {FileName}", fileName);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Phase 7 — Not-Quoted Workflow Endpoints
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Buyer proposes a line item as not-quoted.
    /// Sets QuotationLifecycleStatus = NOT_QUOTED_PROPOSED with justification.
    /// </summary>
    [HttpPost("{requestId}/line-items/{lineItemId}/not-quoted/propose")]
    public async Task<IActionResult> ProposeNotQuoted(Guid requestId, Guid lineItemId, [FromBody] NotQuotedProposeDto dto)
    {
        var actorId = CurrentUserId;

        // 1. Role check: Buyer only
        if (!CurrentUserRoles.Contains(RoleConstants.Buyer))
            return StatusCode(403, new ProblemDetails { Title = "Acesso Negado", Detail = "Apenas compradores podem propor item como não cotado.", Status = 403 });

        // 2. Load request with line items
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // 3. Scope check via GetScopedRequestsQuery (plant/department)
        var scopedQuery = await GetScopedRequestsQuery();
        if (!await scopedQuery.AnyAsync(r => r.Id == requestId))
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // 4. QUOTATION only
        if (request.RequestType?.Code != RequestConstants.Types.Quotation)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Esta ação é permitida apenas para pedidos de Cotação.", Status = 400 });

        // 5. Buyer assignment check
        if (request.BuyerId != null && request.BuyerId != actorId)
            return StatusCode(403, new ProblemDetails { Title = "Acesso Negado", Detail = "Você não é o comprador atribuído a este pedido.", Status = 403 });

        // 6. Find the line item
        var lineItem = request.LineItems.FirstOrDefault(li => li.Id == lineItemId && !li.IsDeleted);
        if (lineItem == null)
            return NotFound(new ProblemDetails { Title = "Item não encontrado", Detail = "O item não pertence a este pedido ou foi excluído.", Status = 404 });

        // 7. Lifecycle guard: must be null or QUOTATION_PENDING
        var lifecycle = lineItem.QuotationLifecycleStatus;
        if (lifecycle != null && lifecycle != RequestConstants.QuotationLifecycleStatuses.QuotationPending)
            return BadRequest(new ProblemDetails
            {
                Title = "Item Indisponível",
                Detail = $"O item #{lineItem.LineNumber} não pode ser proposto como não cotado. Status atual: {lifecycle}.",
                Status = 400
            });

        // 8. Active batch guard: item must not be in an active (non-rejected) batch
        var activeBatchStatuses = new[]
        {
            RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
            RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
            RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
            RequestConstants.ApprovalBatchStatuses.FinalAdjustment,
            RequestConstants.ApprovalBatchStatuses.Approved
        };

        var isInActiveBatch = await _context.Set<ApprovalBatchItem>()
            .AnyAsync(bi => bi.RequestLineItemId == lineItemId
                            && bi.ApprovalBatch.RequestId == requestId
                            && activeBatchStatuses.Contains(bi.ApprovalBatch.Status));

        if (isInActiveBatch)
            return BadRequest(new ProblemDetails
            {
                Title = "Item em Lote Ativo",
                Detail = $"O item #{lineItem.LineNumber} está em um lote de aprovação ativo e não pode ser proposto como não cotado.",
                Status = 400
            });

        // 9. Justification validation: required, min 20 chars
        if (string.IsNullOrWhiteSpace(dto.Justification))
            return BadRequest(new ProblemDetails { Title = "Justificação Obrigatória", Detail = "Informe a justificação para propor o item como não cotado.", Status = 400 });

        if (dto.Justification.Trim().Length < 20)
            return BadRequest(new ProblemDetails { Title = "Justificação Insuficiente", Detail = "A justificação deve ter pelo menos 20 caracteres.", Status = 400 });

        // 10. Mutations
        lineItem.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed;
        lineItem.NotQuotedJustification = dto.Justification.Trim();
        lineItem.NotQuotedProposedByUserId = actorId;
        lineItem.NotQuotedProposedAtUtc = DateTime.UtcNow;

        // Clear stale decision fields from a previous rejected proposal
        lineItem.NotQuotedDecisionByUserId = null;
        lineItem.NotQuotedDecisionAtUtc = null;
        lineItem.NotQuotedDecisionComment = null;

        lineItem.UpdatedAtUtc = DateTime.UtcNow;
        lineItem.UpdatedByUserId = actorId;

        // 11. Status history
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "NOT_QUOTED_PROPOSED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Item #{lineItem.LineNumber} (\"{lineItem.Description}\") proposto como não cotado. Justificação: {dto.Justification.Trim()}",
            CreatedAtUtc = DateTime.UtcNow
        });

        // 12. Sync status (before SaveChanges — SyncStatusAsync mutates but does not save)
        await _statusSyncService.SyncStatusAsync(requestId, actorId);

        await _context.SaveChangesAsync();

        return Ok(new { message = $"Item #{lineItem.LineNumber} proposto como não cotado com sucesso.", lineItemId, status = "NOT_QUOTED_PROPOSED" });
    }

    /// <summary>
    /// Requester or scoped Area Approver accepts a not-quoted proposal.
    /// Sets QuotationLifecycleStatus = NOT_QUOTED_ACCEPTED (terminal).
    /// </summary>
    [HttpPost("{requestId}/line-items/{lineItemId}/not-quoted/accept")]
    public async Task<IActionResult> AcceptNotQuoted(Guid requestId, Guid lineItemId, [FromBody] NotQuotedDecisionDto dto)
    {
        var actorId = CurrentUserId;

        // 1. Load request
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // 2. QUOTATION only
        if (request.RequestType?.Code != RequestConstants.Types.Quotation)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Esta ação é permitida apenas para pedidos de Cotação.", Status = 400 });

        // 3. Authorization (Phase B): Requester OR area manager of this request's
        // department/plant (DepartmentManager routing — the manual role grants nothing).
        var isRequester = request.RequesterId == actorId;
        if (!isRequester && !await CanActAsAreaManagerAsync(actorId, request))
            return StatusCode(403, new ProblemDetails { Title = "Acesso Negado", Detail = "Apenas o solicitante ou o responsável de área do departamento/planta podem aceitar propostas de item não cotado.", Status = 403 });

        // 4. Find the line item
        var lineItem = request.LineItems.FirstOrDefault(li => li.Id == lineItemId && !li.IsDeleted);
        if (lineItem == null)
            return NotFound(new ProblemDetails { Title = "Item não encontrado", Detail = "O item não pertence a este pedido ou foi excluído.", Status = 404 });

        // 5. Lifecycle guard: must be NOT_QUOTED_PROPOSED
        if (lineItem.QuotationLifecycleStatus != RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed)
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Inválida",
                Detail = $"O item #{lineItem.LineNumber} não está com proposta de não cotado pendente. Status atual: {lineItem.QuotationLifecycleStatus ?? "null"}.",
                Status = 400
            });

        // 6. Comment validation
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "Informe o comentário para aceitar a proposta de item não cotado.", Status = 400 });

        // 7. Mutations — terminal status
        lineItem.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.NotQuotedAccepted;
        lineItem.NotQuotedDecisionByUserId = actorId;
        lineItem.NotQuotedDecisionAtUtc = DateTime.UtcNow;
        lineItem.NotQuotedDecisionComment = dto.Comment.Trim();

        lineItem.UpdatedAtUtc = DateTime.UtcNow;
        lineItem.UpdatedByUserId = actorId;

        // 8. Status history
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "NOT_QUOTED_ACCEPTED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Proposta de item não cotado aceita para item #{lineItem.LineNumber} (\"{lineItem.Description}\"). Comentário: {dto.Comment.Trim()}",
            CreatedAtUtc = DateTime.UtcNow
        });

        // 9. Sync status
        await _statusSyncService.SyncStatusAsync(requestId, actorId);

        // 10. Phase 8: Auto-close check — all items NOT_QUOTED_ACCEPTED, no PO groups, no active batches
        var allActiveItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
        var allNotQuotedAccepted = allActiveItems.All(li =>
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedAccepted);

        if (allNotQuotedAccepted)
        {
            var activeBatchStatuses = new[]
            {
                RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
                RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
                RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
                RequestConstants.ApprovalBatchStatuses.FinalAdjustment
            };
            var hasActiveBatches = await _context.ApprovalBatches
                .AnyAsync(b => b.RequestId == requestId && activeBatchStatuses.Contains(b.Status));

            var hasActivePoGroups = await _context.RequestPoGroups
                .AnyAsync(g => g.RequestId == requestId &&
                               g.Status != RequestConstants.PoGroupStatuses.Cancelled);

            if (!hasActiveBatches && !hasActivePoGroups)
            {
                var completedStatus = await _context.RequestStatuses
                    .FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.Completed);
                if (completedStatus != null)
                {
                    var previousStatusId = request.StatusId;
                    request.StatusId = completedStatus.Id;
                    request.UpdatedAtUtc = DateTime.UtcNow;
                    request.UpdatedByUserId = actorId;

                    _context.RequestStatusHistories.Add(new RequestStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        RequestId = requestId,
                        ActorUserId = actorId,
                        ActionTaken = "AUTO_CLOSE_ALL_NOT_QUOTED",
                        PreviousStatusId = previousStatusId,
                        NewStatusId = completedStatus.Id,
                        Comment = "Pedido encerrado automaticamente — todos os itens foram aceitos como não cotados, " +
                                  "sem lotes de aprovação ou grupos P.O. ativos.",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }
        }
        // NOTE: Do NOT auto-close if any items are QUOTATION_APPROVED without PO groups.
        // That is an inconsistent/incomplete state, not a valid closure.

        await _context.SaveChangesAsync();

        return Ok(new { message = $"Proposta de item não cotado aceita para item #{lineItem.LineNumber}.", lineItemId, status = "NOT_QUOTED_ACCEPTED" });
    }

    /// <summary>
    /// Requester or scoped Area Approver rejects a not-quoted proposal.
    /// Returns item to QuotationLifecycleStatus = null (pending pool).
    /// </summary>
    [HttpPost("{requestId}/line-items/{lineItemId}/not-quoted/reject")]
    public async Task<IActionResult> RejectNotQuoted(Guid requestId, Guid lineItemId, [FromBody] NotQuotedDecisionDto dto)
    {
        var actorId = CurrentUserId;

        // 1. Load request
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // 2. QUOTATION only
        if (request.RequestType?.Code != RequestConstants.Types.Quotation)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Esta ação é permitida apenas para pedidos de Cotação.", Status = 400 });

        // 3. Authorization (Phase B): Requester OR area manager of this request's
        // department/plant (DepartmentManager routing — the manual role grants nothing).
        var isRequester = request.RequesterId == actorId;
        if (!isRequester && !await CanActAsAreaManagerAsync(actorId, request))
            return StatusCode(403, new ProblemDetails { Title = "Acesso Negado", Detail = "Apenas o solicitante ou o responsável de área do departamento/planta podem rejeitar propostas de item não cotado.", Status = 403 });

        // 4. Find the line item
        var lineItem = request.LineItems.FirstOrDefault(li => li.Id == lineItemId && !li.IsDeleted);
        if (lineItem == null)
            return NotFound(new ProblemDetails { Title = "Item não encontrado", Detail = "O item não pertence a este pedido ou foi excluído.", Status = 404 });

        // 5. Lifecycle guard: must be NOT_QUOTED_PROPOSED
        if (lineItem.QuotationLifecycleStatus != RequestConstants.QuotationLifecycleStatuses.NotQuotedProposed)
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Inválida",
                Detail = $"O item #{lineItem.LineNumber} não está com proposta de não cotado pendente. Status atual: {lineItem.QuotationLifecycleStatus ?? "null"}.",
                Status = 400
            });

        // 6. Comment validation
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "Informe o motivo da rejeição da proposta de item não cotado.", Status = 400 });

        // 7. Mutations — return to pending pool
        lineItem.QuotationLifecycleStatus = null; // Back to pending pool (consistent with batch rejection pattern)
        lineItem.NotQuotedDecisionByUserId = actorId;
        lineItem.NotQuotedDecisionAtUtc = DateTime.UtcNow;
        lineItem.NotQuotedDecisionComment = dto.Comment.Trim();
        // Preserve NotQuotedJustification for audit trail

        lineItem.UpdatedAtUtc = DateTime.UtcNow;
        lineItem.UpdatedByUserId = actorId;

        // 8. Status history
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "NOT_QUOTED_REJECTED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Proposta de item não cotado rejeitada para item #{lineItem.LineNumber} (\"{lineItem.Description}\"). Motivo: {dto.Comment.Trim()}",
            CreatedAtUtc = DateTime.UtcNow
        });

        // 9. Sync status
        await _statusSyncService.SyncStatusAsync(requestId, actorId);

        await _context.SaveChangesAsync();

        return Ok(new { message = $"Proposta de item não cotado rejeitada para item #{lineItem.LineNumber}. Item retornado ao pool de cotação.", lineItemId, status = "QUOTATION_PENDING" });
    }

    /// <summary>
    /// Buyer closes a line item without quotation — a final Buyer decision with
    /// mandatory reason + justification. Sets QuotationLifecycleStatus = CLOSED_NOT_QUOTED.
    /// Replaces the legacy propose/accept/reject not-quoted flow: no Requester or
    /// Area Approver acceptance is involved.
    /// </summary>
    [HttpPost("{requestId}/line-items/{lineItemId}/close-not-quoted")]
    public async Task<IActionResult> CloseNotQuoted(Guid requestId, Guid lineItemId, [FromBody] CloseNotQuotedDto dto)
    {
        var actorId = CurrentUserId;

        // 1. Role check: Buyer only
        if (!CurrentUserRoles.Contains(RoleConstants.Buyer))
            return StatusCode(403, new ProblemDetails { Title = "Acesso Negado", Detail = "Apenas compradores podem encerrar itens sem cotação.", Status = 403 });

        // 2. Load request with line items
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // 3. Scope check via GetScopedRequestsQuery (plant/department)
        var scopedQuery = await GetScopedRequestsQuery();
        if (!await scopedQuery.AnyAsync(r => r.Id == requestId))
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // 4. QUOTATION only
        if (request.RequestType?.Code != RequestConstants.Types.Quotation)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Esta ação é permitida apenas para pedidos de Cotação.", Status = 400 });

        // 5. Buyer assignment check
        if (request.BuyerId != null && request.BuyerId != actorId)
            return StatusCode(403, new ProblemDetails { Title = "Acesso Negado", Detail = "Você não é o comprador atribuído a este pedido.", Status = 403 });

        // 6. Find the line item
        var lineItem = request.LineItems.FirstOrDefault(li => li.Id == lineItemId && !li.IsDeleted);
        if (lineItem == null)
            return NotFound(new ProblemDetails { Title = "Item não encontrado", Detail = "O item não pertence a este pedido ou foi excluído.", Status = 404 });

        // 7. Lifecycle guard: must be null or QUOTATION_PENDING
        var lifecycle = lineItem.QuotationLifecycleStatus;
        if (lifecycle != null && lifecycle != RequestConstants.QuotationLifecycleStatuses.QuotationPending)
            return BadRequest(new ProblemDetails
            {
                Title = "Item Indisponível",
                Detail = $"O item #{lineItem.LineNumber} não pode ser encerrado sem cotação. Status atual: {lifecycle}.",
                Status = 400
            });

        // 8. Active batch guard: item must not be in an active/approved batch
        var activeBatchStatuses = new[]
        {
            RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
            RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
            RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
            RequestConstants.ApprovalBatchStatuses.FinalAdjustment,
            RequestConstants.ApprovalBatchStatuses.Approved
        };

        var isInActiveBatch = await _context.Set<ApprovalBatchItem>()
            .AnyAsync(bi => bi.RequestLineItemId == lineItemId
                            && bi.ApprovalBatch.RequestId == requestId
                            && activeBatchStatuses.Contains(bi.ApprovalBatch.Status));

        if (isInActiveBatch)
            return BadRequest(new ProblemDetails
            {
                Title = "Item em Lote Ativo",
                Detail = $"O item #{lineItem.LineNumber} está em um lote de aprovação ativo e não pode ser encerrado sem cotação.",
                Status = 400
            });

        // 9. Reason + justification validation
        if (string.IsNullOrWhiteSpace(dto.ReasonCode))
            return BadRequest(new ProblemDetails { Title = "Motivo Obrigatório", Detail = "Selecione o motivo do encerramento sem cotação.", Status = 400 });

        if (string.IsNullOrWhiteSpace(dto.Justification) || dto.Justification.Trim().Length < 20)
            return BadRequest(new ProblemDetails { Title = "Justificação Insuficiente", Detail = "A justificação deve ter pelo menos 20 caracteres.", Status = 400 });

        var reason = dto.ReasonCode.Trim();
        var justification = dto.Justification.Trim();

        // 10. Mutations — terminal Buyer decision. Reuses the existing not-quoted
        // audit columns (no migration): justification holds the composed text,
        // proposer fields hold the closing actor/time.
        lineItem.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted;
        lineItem.NotQuotedJustification = $"Motivo: {reason}\nJustificativa: {justification}";
        lineItem.NotQuotedProposedByUserId = actorId;
        lineItem.NotQuotedProposedAtUtc = DateTime.UtcNow;
        lineItem.NotQuotedDecisionByUserId = null;
        lineItem.NotQuotedDecisionAtUtc = null;
        lineItem.NotQuotedDecisionComment = null;

        lineItem.UpdatedAtUtc = DateTime.UtcNow;
        lineItem.UpdatedByUserId = actorId;

        // 11. Status history
        var actorUser = await _context.Users.FindAsync(actorId);
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "ITEM_CLOSED_NOT_QUOTED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"{actorUser?.FullName ?? "Comprador"} encerrou o item \"{lineItem.Description}\" (Linha {lineItem.LineNumber}) sem cotação.\n" +
                      $"Motivo: {reason}.\nJustificativa: {justification}",
            CreatedAtUtc = DateTime.UtcNow
        });

        // 12. Sync status (before SaveChanges — SyncStatusAsync mutates but does not save)
        await _statusSyncService.SyncStatusAsync(requestId, actorId);

        // 13. Auto-close check — all items terminally closed as not-quoted
        // (new CLOSED_NOT_QUOTED or legacy NOT_QUOTED_ACCEPTED), no active
        // batches, no active PO groups. Mirrors the legacy accept endpoint.
        var allActiveItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
        var allClosedNotQuoted = allActiveItems.All(li =>
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted ||
            li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.NotQuotedAccepted);

        if (allClosedNotQuoted)
        {
            var hasActiveBatches = await _context.ApprovalBatches
                .AnyAsync(b => b.RequestId == requestId && activeBatchStatuses.Contains(b.Status));

            var hasActivePoGroups = await _context.RequestPoGroups
                .AnyAsync(g => g.RequestId == requestId &&
                               g.Status != RequestConstants.PoGroupStatuses.Cancelled);

            if (!hasActiveBatches && !hasActivePoGroups)
            {
                var completedStatus = await _context.RequestStatuses
                    .FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.Completed);
                if (completedStatus != null)
                {
                    var previousStatusId = request.StatusId;
                    request.StatusId = completedStatus.Id;
                    request.UpdatedAtUtc = DateTime.UtcNow;
                    request.UpdatedByUserId = actorId;

                    _context.RequestStatusHistories.Add(new RequestStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        RequestId = requestId,
                        ActorUserId = actorId,
                        ActionTaken = "AUTO_CLOSE_ALL_NOT_QUOTED",
                        PreviousStatusId = previousStatusId,
                        NewStatusId = completedStatus.Id,
                        Comment = "Pedido encerrado automaticamente — todos os itens foram encerrados sem cotação, " +
                                  "sem lotes de aprovação ou grupos P.O. ativos.",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }
        }
        // NOTE: Do NOT auto-close if any items are QUOTATION_APPROVED without PO groups.
        // That is an inconsistent/incomplete state, not a valid closure.

        await _context.SaveChangesAsync();

        return Ok(new { message = $"Item #{lineItem.LineNumber} encerrado sem cotação.", lineItemId, status = RequestConstants.QuotationLifecycleStatuses.ClosedNotQuoted });
    }
}
