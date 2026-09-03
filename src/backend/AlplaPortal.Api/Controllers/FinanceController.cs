namespace AlplaPortal.Api.Controllers;

using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Application.DTOs.Common;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Events;
using AlplaPortal.Domain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;

[Authorize(Roles = RoleConstants.SystemAdministrator + "," + RoleConstants.Finance)]
[ApiController]
[Route("api/v1/finance")]
public class FinanceController : BaseController
{
    private readonly IWorkflowNotificationOrchestrator _orchestrator;
    private readonly ILogger<FinanceController> _logger;
    private readonly IStatusAggregationService _statusAggregationService;
    private readonly IFinancePaymentEligibilityService _eligibility;

    public FinanceController(
        ApplicationDbContext context,
        IWorkflowNotificationOrchestrator orchestrator,
        ILogger<FinanceController> logger,
        IStatusAggregationService statusAggregationService,
        IFinancePaymentEligibilityService eligibility,
        AlplaPortal.Application.Interfaces.Requests.IRequestCompletionService? completionService = null) : base(context)
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _statusAggregationService = statusAggregationService;
        _eligibility = eligibility;
        _completionService = completionService;
    }

    /// <summary>
    /// Phase 4C: optional-by-default so existing direct constructions (tests) keep compiling;
    /// DI always supplies the registered service in production. Self-gates on the completion
    /// flags with zero queries while disabled.
    /// </summary>
    private readonly AlplaPortal.Application.Interfaces.Requests.IRequestCompletionService? _completionService;

    private IQueryable<Request> GetFinanceQuery()
    {
        // Scope restrictions can be applied here based on company/plant
        // The BaseController.GetScopedRequestsQuery() already aligns with the user's role and scopes (Plant/Dept limits)
        // Since FINANCE uses this controller, we just use the base scoped query.
        return _context.Requests.AsQueryable(); // Note: we'll call GetScopedRequestsQuery() inside the endpoints
    }

    [HttpGet("summary")]
    public async Task<ActionResult<FinanceSummaryDto>> GetSummary([FromQuery] int? companyId = null)
    {
        // Finance is restricted to their scoped plants
        var scopedQuery = await GetScopedRequestsQuery();
        
        if (companyId.HasValue)
        {
            scopedQuery = scopedQuery.Where(r => r.CompanyId == companyId.Value);
        }
        
        // ── Finance-eligible statuses ──
        var financeStatuses = new[] 
        { 
            RequestConstants.Statuses.PoIssued, 
            RequestConstants.Statuses.PaymentRequestSent, 
            RequestConstants.Statuses.PaymentScheduled, 
            RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed
        };

        var financeGroupStatuses = new[] {
            RequestConstants.Statuses.PoIssued,
            RequestConstants.Statuses.AdvancePaymentRequired,
            RequestConstants.Statuses.AdvancePaymentScheduled,
            RequestConstants.Statuses.AdvancePaymentCompleted,
            RequestConstants.Statuses.WaitingSupplierDelivery,
            RequestConstants.Statuses.PaymentRequestSent,
            RequestConstants.Statuses.PaymentScheduled,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed
        };

        var today = DateTime.UtcNow.Date;
        var in4Days = today.AddDays(4);
        var firstDayOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // ── Stream A: PAYMENT requests — existing request-level logic ──
        var paymentQuery = scopedQuery.Where(r => 
            r.RequestType!.Code == RequestConstants.Types.Payment
            && financeStatuses.Contains(r.Status!.Code)
            && r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PurchaseOrder)
        );

        var paymentStats = await paymentQuery
            .Select(r => new
            {
                Id = r.Id,
                StatusCode = r.Status!.Code,
                NeedByDateUtc = r.NeedByDateUtc,
                ScheduledDateUtc = r.ScheduledDateUtc,
                RequestedDateUtc = r.RequestedDateUtc,
                RequestTypeCode = r.RequestType!.Code,
                Amount = (r.ApprovedTotalAmount.HasValue && r.ApprovedTotalAmount.Value > 0)
                    ? r.ApprovedTotalAmount.Value
                    : r.SelectedQuotationId.HasValue
                        ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)!.TotalAmount
                        : r.EstimatedTotalAmount,
                ActualPaidAmount = r.ActualPaidAmount,
                ActualPaidAtUtc = r.ActualPaidAtUtc,
                CurrencyCode = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)!.Currency
                    : r.Currency != null ? r.Currency.Code : "---",
                SupplierName = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)!.SupplierNameSnapshot
                    : r.Supplier != null ? r.Supplier.Name : "---",
                HistoryPaidAtUtc = r.StatusHistories
                    .Where(sh => sh.NewStatus!.Code == RequestConstants.Statuses.Paid || sh.NewStatus!.Code == RequestConstants.Statuses.PaymentCompleted)
                    .OrderByDescending(sh => sh.CreatedAtUtc)
                    .Select(sh => (DateTime?)sh.CreatedAtUtc)
                    .FirstOrDefault(),
                HasProforma = r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.Proforma),
                HasPO = r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PurchaseOrder),
                HasProof = r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PaymentProof)
            })
            .ToListAsync();

        var paidStatuses = new[] {
            RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed
        };

        var paymentProcessed = paymentStats.Select(s => new SummaryItem {
            StatusCode = s.StatusCode,
            NeedByDateUtc = s.NeedByDateUtc,
            ScheduledDateUtc = s.ScheduledDateUtc,
            RequestedDateUtc = s.RequestedDateUtc,
            RequestTypeCode = s.RequestTypeCode,
            CurrencyCode = s.CurrencyCode ?? "---",
            SupplierName = s.SupplierName ?? "---",
            HasProforma = s.HasProforma,
            HasPO = s.HasPO,
            HasProof = s.HasProof,
            IsPaid = paidStatuses.Contains(s.StatusCode) || s.ActualPaidAtUtc.HasValue || s.HistoryPaidAtUtc.HasValue,
            PaidAtUtc = s.ActualPaidAtUtc ?? s.HistoryPaidAtUtc,
            Amount = s.ActualPaidAmount ?? s.Amount
        }).ToList();

        // ── Stream B: QUOTATION requests — group-based ──
        var quotGroupStats = await scopedQuery
            .Where(r => r.RequestType!.Code == RequestConstants.Types.Quotation)
            .SelectMany(r => r.PoGroups
                .Where(g => financeGroupStatuses.Contains(g.Status)
                    && r.Attachments.Any(a => !a.IsDeleted 
                        && a.AttachmentTypeCode == AttachmentConstants.Types.PurchaseOrder
                        && a.RequestPoGroupId == g.Id)),
                (r, g) => new
                {
                    GroupId = g.Id,
                    GroupStatusCode = g.Status,
                    Amount = g.TotalAmount,
                    CurrencyCode = g.CurrencyCode ?? "---",
                    SupplierName = g.SupplierNameSnapshot ?? "---",
                    NeedByDateUtc = r.NeedByDateUtc,
                    ScheduledDateUtc = r.ScheduledDateUtc,
                    RequestedDateUtc = r.RequestedDateUtc,
                    RequestTypeCode = r.RequestType!.Code,
                    HasProforma = r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.Proforma),
                })
            .ToListAsync();

        var quotProcessed = quotGroupStats.Select(g => new SummaryItem {
            StatusCode = g.GroupStatusCode,
            NeedByDateUtc = g.NeedByDateUtc,
            ScheduledDateUtc = g.ScheduledDateUtc,
            RequestedDateUtc = g.RequestedDateUtc,
            RequestTypeCode = g.RequestTypeCode,
            CurrencyCode = g.CurrencyCode,
            SupplierName = g.SupplierName,
            HasProforma = g.HasProforma,
            HasPO = true, // already filtered by PO attachment
            HasProof = false,
            IsPaid = paidStatuses.Contains(g.GroupStatusCode),
            PaidAtUtc = null,
            Amount = g.Amount
        }).ToList();

        // ── Merge both streams ──
        var processedStats = paymentProcessed.Concat(quotProcessed).ToList();

        var waitingActions = new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentRequestSent, RequestConstants.Statuses.AdvancePaymentRequired };
        
        // Metrics excluding paid ones
        int waitingFinance = processedStats.Count(s => !s.IsPaid && waitingActions.Contains(s.StatusCode));
        var scheduledCount = processedStats.Count(s => !s.IsPaid && s.StatusCode == RequestConstants.Statuses.PaymentScheduled);
        var overdueCount = processedStats.Count(s => !s.IsPaid && s.NeedByDateUtc.HasValue && s.NeedByDateUtc.Value < today);
        var completedCountThisMonth = processedStats.Count(s => s.IsPaid && s.PaidAtUtc >= firstDayOfMonth);

        // Values grouped by currency
        var pendingValues = processedStats
            .Where(s => !s.IsPaid)
            .GroupBy(s => s.CurrencyCode)
            .Select(g => new FinanceCurrencyValueDto { CurrencyCode = g.Key, TotalAmount = g.Sum(x => x.Amount) })
            .ToList();

        var scheduledValues = processedStats
            .Where(s => !s.IsPaid && s.StatusCode == RequestConstants.Statuses.PaymentScheduled)
            .GroupBy(s => s.CurrencyCode)
            .Select(g => new FinanceCurrencyValueDto { CurrencyCode = g.Key, TotalAmount = g.Sum(x => x.Amount) })
            .ToList();

        var overdueValues = processedStats
            .Where(s => !s.IsPaid && s.NeedByDateUtc.HasValue && s.NeedByDateUtc.Value < today)
            .GroupBy(s => s.CurrencyCode)
            .Select(g => new FinanceCurrencyValueDto { CurrencyCode = g.Key, TotalAmount = g.Sum(x => x.Amount) })
            .ToList();

        var paidThisMonthValues = processedStats
            .Where(s => s.IsPaid && s.PaidAtUtc >= firstDayOfMonth)
            .GroupBy(s => s.CurrencyCode)
            .Select(g => new FinanceCurrencyValueDto { CurrencyCode = g.Key, TotalAmount = g.Sum(x => x.Amount) })
            .ToList();

        var currencyCodes = processedStats.Where(s => !s.IsPaid).Select(s => s.CurrencyCode).Distinct().ToList();

        // Warning points
        int missingDocs = processedStats.Count(s => 
            !s.IsPaid &&
            ((s.RequestTypeCode == RequestConstants.Types.Quotation && !s.HasProforma) || (!s.HasPO))
        );

        var dueSoonCount = processedStats.Count(s => 
            !s.IsPaid && s.NeedByDateUtc.HasValue && s.NeedByDateUtc.Value >= today && s.NeedByDateUtc.Value <= in4Days);

        var attentionPoints = new List<FinanceAttentionPointDto>();
        if (overdueCount > 0)
        {
            attentionPoints.Add(new FinanceAttentionPointDto { Id = "overdue", Title = "Pagamentos Vencidos", Description = "Requer ação imediata de tesouraria.", Count = overdueCount, TargetPath = "/finance/payments?filter=overdue", Type = "DANGER" });
        }
        if (dueSoonCount > 0)
        {
            attentionPoints.Add(new FinanceAttentionPointDto { Id = "due-soon", Title = "Vencendo em breve", Description = "Pagamentos vencendo nos próximos 4 dias.", Count = dueSoonCount, TargetPath = "/finance/payments?filter=dueSoon", Type = "WARNING" });
        }
        if (missingDocs > 0)
        {
            attentionPoints.Add(new FinanceAttentionPointDto { Id = "missing-docs", Title = "Falta Documento", Description = "Pedidos sem Proforma ou P.O pendentes de ação.", Count = missingDocs, TargetPath = "/finance/payments?filter=missingDocs", Type = "INFO" });
        }

        // --- DATA SCIENCE METRICS ---
        var maxProjectionDate = today.AddDays(15);
        var uncompletedRequests = processedStats.Where(s => !s.IsPaid).ToList();
        
        var projections = uncompletedRequests
            .Where(s => (s.ScheduledDateUtc ?? s.NeedByDateUtc).HasValue && (s.ScheduledDateUtc ?? s.NeedByDateUtc) >= today && (s.ScheduledDateUtc ?? s.NeedByDateUtc) <= maxProjectionDate)
            .GroupBy(s => new { Date = (s.ScheduledDateUtc ?? s.NeedByDateUtc)!.Value.Date, Currency = s.CurrencyCode })
            .Select(g => new FinanceCashFlowProjectionDto {
                Date = g.Key.Date.ToString("yyyy-MM-dd"),
                CurrencyCode = g.Key.Currency,
                TotalAmount = g.Sum(x => x.Amount)
            })
            .OrderBy(p => p.Date)
            .ToList();

        var currencyExposures = uncompletedRequests
            .GroupBy(s => s.CurrencyCode)
            .Select(g => new FinanceCurrencyExposureDto {
                CurrencyCode = g.Key ?? "N/A",
                Amount = g.Sum(x => x.Amount),
                Count = g.Count()
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var topSuppliers = uncompletedRequests
            .GroupBy(s => new { s.SupplierName, s.CurrencyCode })
            .Select(g => new FinanceTopSupplierDto {
                SupplierName = string.IsNullOrWhiteSpace(g.Key.SupplierName) ? "Fornecedor Não Declarado" : g.Key.SupplierName,
                CurrencyCode = g.Key.CurrencyCode ?? "---",
                TotalPendingAmount = g.Sum(x => x.Amount),
                RequestCount = g.Count()
            })
            .OrderByDescending(t => t.TotalPendingAmount)
            .Take(5)
            .ToList();

        var waitingFinanceAging = uncompletedRequests.Where(s => waitingActions.Contains(s.StatusCode)).ToList();
        var agingAnalysis = new FinanceAgingAnalysisDto();
        foreach (var req in waitingFinanceAging)
        {
            var diffDays = (today - req.RequestedDateUtc.Date).TotalDays;
            if (diffDays <= 2) agingAnalysis.ZeroToTwoDays++;
            else if (diffDays <= 5) agingAnalysis.ThreeToFiveDays++;
            else agingAnalysis.MoreThanFiveDays++;
        }

        return Ok(new FinanceSummaryDto
        {
            WaitingFinanceAction = waitingFinance,
            ScheduledPayments = scheduledCount,
            OverduePayments = overdueCount,
            CompletedThisMonth = completedCountThisMonth,
            PendingValues = pendingValues,
            ScheduledValues = scheduledValues,
            OverdueValues = overdueValues,
            PaidThisMonthValues = paidThisMonthValues,
            CurrencyCodes = currencyCodes,
            AttentionPoints = attentionPoints,
            CashFlowProjections = projections,
            CurrencyExposures = currencyExposures,
            TopSuppliers = topSuppliers,
            AgingAnalysis = agingAnalysis
        });
    }

    [HttpGet("cashflow-projections")]
    public async Task<ActionResult<List<FinanceCashFlowProjectionDto>>> GetCashFlowProjections([FromQuery] int? companyId = null, [FromQuery] string interval = "15days")
    {
        var scopedQuery = await GetScopedRequestsQuery();
        
        if (companyId.HasValue)
        {
            scopedQuery = scopedQuery.Where(r => r.CompanyId == companyId.Value);
        }
        
        var financeStatuses = new[] 
        { 
            RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentRequestSent, 
            RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed
        };

        var query = scopedQuery.Where(r => 
            (financeStatuses.Contains(r.Status!.Code) || (r.RequestType!.Code == RequestConstants.Types.Payment && r.Status!.Code == RequestConstants.Statuses.FinalApproved))
            && r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PurchaseOrder)
            && !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(r.Status!.Code)
        );

        var today = DateTime.UtcNow.Date;
        DateTime maxProjectionDate;

        switch (interval.ToLowerInvariant())
        {
            case "weeks": maxProjectionDate = today.AddDays(7 * 12); break;
            case "months": maxProjectionDate = today.AddMonths(12); break;
            case "years": maxProjectionDate = today.AddYears(5); break;
            case "15days":
            default: maxProjectionDate = today.AddDays(15); break;
        }

        var uncompletedRequests = await query
            .Include(r => r.Quotations)
            .Include(r => r.Currency)
            .Select(r => new
            {
                ScheduledDateUtc = r.ScheduledDateUtc,
                NeedByDateUtc = r.NeedByDateUtc,
                CurrencyCode = r.SelectedQuotationId.HasValue
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)!.Currency
                    : r.Currency != null ? r.Currency.Code : "---",
                Amount = (r.ApprovedTotalAmount.HasValue && r.ApprovedTotalAmount.Value > 0)
                    ? r.ApprovedTotalAmount.Value
                    : r.SelectedQuotationId.HasValue
                        ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)!.TotalAmount
                        : r.EstimatedTotalAmount
            })
            .Where(s => (s.ScheduledDateUtc ?? s.NeedByDateUtc) >= today && (s.ScheduledDateUtc ?? s.NeedByDateUtc) <= maxProjectionDate)
            .ToListAsync();

        var projectionsQuery = uncompletedRequests.Select(s => new {
            Date = (s.ScheduledDateUtc ?? s.NeedByDateUtc)!.Value.Date,
            Currency = s.CurrencyCode,
            Amount = s.Amount
        });

        IEnumerable<FinanceCashFlowProjectionDto> projections;

        if (interval == "weeks")
        {
            projections = projectionsQuery
                .GroupBy(s => new { 
                    YearWeek = System.Globalization.ISOWeek.GetYear(s.Date).ToString() + "-W" + System.Globalization.ISOWeek.GetWeekOfYear(s.Date).ToString("D2"),
                    Currency = s.Currency 
                })
                .Select(g => new FinanceCashFlowProjectionDto { Date = g.Key.YearWeek, CurrencyCode = g.Key.Currency, TotalAmount = g.Sum(x => x.Amount) });
        }
        else if (interval == "months")
        {
            projections = projectionsQuery
                .GroupBy(s => new { YearMonth = s.Date.ToString("yyyy-MM"), Currency = s.Currency })
                .Select(g => new FinanceCashFlowProjectionDto { Date = g.Key.YearMonth, CurrencyCode = g.Key.Currency, TotalAmount = g.Sum(x => x.Amount) });
        }
        else if (interval == "years")
        {
            projections = projectionsQuery
                .GroupBy(s => new { Year = s.Date.ToString("yyyy"), Currency = s.Currency })
                .Select(g => new FinanceCashFlowProjectionDto { Date = g.Key.Year, CurrencyCode = g.Key.Currency, TotalAmount = g.Sum(x => x.Amount) });
        }
        else 
        {
            projections = projectionsQuery
                .GroupBy(s => new { Date = s.Date.ToString("yyyy-MM-dd"), Currency = s.Currency })
                .Select(g => new FinanceCashFlowProjectionDto { Date = g.Key.Date, CurrencyCode = g.Key.Currency, TotalAmount = g.Sum(x => x.Amount) });
        }

        return Ok(projections.OrderBy(p => p.Date).ToList());
    }

    [HttpGet("payments")]
    public async Task<ActionResult<FinanceListResponseDto>> GetPayments(
        [FromQuery] string? filter = null,
        [FromQuery] string? statusIds = null,
        [FromQuery] string? statusCodes = null,
        [FromQuery] string? currencyCode = null,
        [FromQuery] string? search = null,
        [FromQuery] string? searchSupplier = null, // legacy fallback — kept for bookmarked URLs/saved preferences; 'search' takes precedence when both are present
        [FromQuery] string? sortBy = null,
        [FromQuery] bool isDescending = false,
        [FromQuery] int? plantId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var scopedQuery = await GetScopedRequestsQuery();
        var today = DateTime.UtcNow.Date;
        var in4Days = today.AddDays(4);

        var financeStatuses = new[] 
        { 
            RequestConstants.Statuses.PoIssued, 
            RequestConstants.Statuses.PaymentRequestSent, 
            RequestConstants.Statuses.AdvancePaymentRequired,
            RequestConstants.Statuses.AdvancePaymentCompleted,
            RequestConstants.Statuses.PaymentScheduled, 
            RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed,
            RequestConstants.Statuses.PoPartiallyUploaded
        };

        // Groups that have entered the finance pipeline (PO_ISSUED and beyond)
        var financeGroupStatuses = new[] {
            RequestConstants.Statuses.PoIssued,
            RequestConstants.Statuses.AdvancePaymentRequired,
            RequestConstants.Statuses.AdvancePaymentScheduled,
            RequestConstants.Statuses.AdvancePaymentCompleted,
            RequestConstants.Statuses.WaitingSupplierDelivery,
            RequestConstants.Statuses.PaymentRequestSent,
            RequestConstants.Statuses.PaymentScheduled,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed
        };

        var query = scopedQuery.Where(r => 
            // PAYMENT / legacy: parent status is finance-relevant + has PO attachment
            // Also covers QUOTATION PO_PARTIALLY_UPLOADED (parent status after partial batch PO registration)
            (financeStatuses.Contains(r.Status!.Code)
                && r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PurchaseOrder))
            // QUOTATION group-first: include requests where any PO group entered the finance pipeline
            || (r.RequestType!.Code == RequestConstants.Types.Quotation 
                && r.PoGroups.Any(g => financeGroupStatuses.Contains(g.Status)))
        );

        if (plantId.HasValue)
        {
            query = query.Where(r => r.PlantId == plantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(statusIds))
        {
            var parsedStatusIds = statusIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (parsedStatusIds.Any()) query = query.Where(r => parsedStatusIds.Contains(r.StatusId));
        }

        if (!string.IsNullOrWhiteSpace(statusCodes))
        {
            var parsedCodes = statusCodes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToUpper()).ToList();
            if (parsedCodes.Any()) query = query.Where(r => r.Status != null && parsedCodes.Contains(r.Status.Code));
        }

        if (!string.IsNullOrWhiteSpace(currencyCode))
        {
            var ccUpper = currencyCode.ToUpper();
            query = query.Where(r => 
                (r.SelectedQuotationId.HasValue && r.Quotations.Any(q => q.Id == r.SelectedQuotationId.Value && q.Currency != null && q.Currency.ToUpper() == ccUpper))
                || (!r.SelectedQuotationId.HasValue && r.Currency != null && r.Currency.Code.ToUpper() == ccUpper));
        }

        // General search: request number OR the effective supplier name (Quotation.SupplierNameSnapshot
        // when a quotation is selected, otherwise Request.Supplier.Name) — same OR-search shape as
        // RequestsController.GetRequests. 'search' takes precedence; 'searchSupplier' is a temporary
        // fallback for old bookmarked URLs / saved table preferences that predate this change.
        var effectiveSearch = !string.IsNullOrWhiteSpace(search) ? search : searchSupplier;
        if (!string.IsNullOrWhiteSpace(effectiveSearch))
        {
            var searchLower = effectiveSearch.Trim().ToLower();
            query = query.Where(r =>
                (r.RequestNumber != null && r.RequestNumber.ToLower().Contains(searchLower))
                || (r.SelectedQuotationId.HasValue && r.Quotations.Any(q => q.Id == r.SelectedQuotationId.Value && q.SupplierNameSnapshot != null && q.SupplierNameSnapshot.ToLower().Contains(searchLower)))
                || (!r.SelectedQuotationId.HasValue && r.Supplier != null && r.Supplier.Name != null && r.Supplier.Name.ToLower().Contains(searchLower)));
        }

        switch(filter)
        {
            case "action":
                var waitingActions = new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentRequestSent, RequestConstants.Statuses.AdvancePaymentRequired };
                query = query.Where(r => waitingActions.Contains(r.Status!.Code));
                break;
            case "scheduled":
                query = query.Where(r => r.Status!.Code == RequestConstants.Statuses.PaymentScheduled);
                break;
            case "completedThisMonth":
                var firstDayOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                query = query.Where(r => new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.Completed }.Contains(r.Status!.Code) && r.StatusHistories.Any(sh => (sh.NewStatus!.Code == RequestConstants.Statuses.Paid || sh.NewStatus!.Code == RequestConstants.Statuses.PaymentCompleted || sh.NewStatus!.Code == RequestConstants.Statuses.Completed) && sh.CreatedAtUtc >= firstDayOfMonth));
                break;
            case "overdue":
                query = query.Where(r => !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup }.Contains(r.Status!.Code) && !r.ActualPaidAtUtc.HasValue && r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < today);
                break;
            case "dueSoon":
                query = query.Where(r => !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup }.Contains(r.Status!.Code) && !r.ActualPaidAtUtc.HasValue && r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= today && r.NeedByDateUtc.Value <= in4Days);
                break;
        }

        var totalCount = await query.CountAsync();

        var includedQuery = query
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.Supplier)
            .Include(r => r.Requester)
            .Include(r => r.Plant)
            .Include(r => r.Quotations)
            .Include(r => r.Currency)
            .Include(r => r.PoGroups)
                .ThenInclude(g => g.Payments);

        // Default order (no sortBy): overdue first, then need-level, then need-by-date — unchanged
        // from prior behavior. Explicit sortBy switches to one of the supported columns below.
        IOrderedQueryable<Request> orderedQuery;
        switch (sortBy?.ToLower())
        {
            case "requestnumber":
                // RequestNumber format is REQ-DD/MM/YYYY-NNN — lexicographic string sort would be
                // wrong (day precedes year). Sort by CreatedAtUtc.Date, same as RequestsController.
                orderedQuery = isDescending
                    ? includedQuery.OrderByDescending(r => r.CreatedAtUtc.Date).ThenByDescending(r => r.RequestNumber)
                    : includedQuery.OrderBy(r => r.CreatedAtUtc.Date).ThenBy(r => r.RequestNumber);
                break;
            case "suppliername":
                // Effective supplier name: Quotation.SupplierNameSnapshot when a quotation is
                // selected, otherwise Request.Supplier.Name — same source used to populate the row.
                orderedQuery = isDescending
                    ? includedQuery.OrderByDescending(r => r.SelectedQuotationId.HasValue
                        ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId.Value).Select(q => q.SupplierNameSnapshot).FirstOrDefault()
                        : (r.Supplier != null ? r.Supplier.Name : null))
                    : includedQuery.OrderBy(r => r.SelectedQuotationId.HasValue
                        ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId.Value).Select(q => q.SupplierNameSnapshot).FirstOrDefault()
                        : (r.Supplier != null ? r.Supplier.Name : null));
                break;
            case "needbydateutc":
                // Raw due date. The row may separately display ScheduledDateUtc/PaidDateUtc
                // depending on state, but sorting is intentionally against NeedByDateUtc only —
                // same single-date-field approach RequestsController uses.
                orderedQuery = isDescending ? includedQuery.OrderByDescending(r => r.NeedByDateUtc) : includedQuery.OrderBy(r => r.NeedByDateUtc);
                break;
            case "statuscode":
                // Workflow position, not alphabetical label — same as RequestsController.
                orderedQuery = isDescending ? includedQuery.OrderByDescending(r => r.Status!.DisplayOrder) : includedQuery.OrderBy(r => r.Status!.DisplayOrder);
                break;
            case "amount":
                // Matches the Amount fallback below exactly, so the sort order never disagrees
                // with what's displayed.
                orderedQuery = isDescending
                    ? includedQuery.OrderByDescending(r => (r.ApprovedTotalAmount.HasValue && r.ApprovedTotalAmount.Value > 0)
                        ? r.ApprovedTotalAmount
                        : r.SelectedQuotationId.HasValue
                            ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId.Value).Select(q => (decimal?)q.TotalAmount).FirstOrDefault()
                            : (decimal?)r.EstimatedTotalAmount)
                    : includedQuery.OrderBy(r => (r.ApprovedTotalAmount.HasValue && r.ApprovedTotalAmount.Value > 0)
                        ? r.ApprovedTotalAmount
                        : r.SelectedQuotationId.HasValue
                            ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId.Value).Select(q => (decimal?)q.TotalAmount).FirstOrDefault()
                            : (decimal?)r.EstimatedTotalAmount);
                break;
            default:
                orderedQuery = includedQuery
                    .OrderByDescending(r => r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < today ? 1 : 0) // Overdue first
                    .ThenByDescending(r => r.NeedLevelId ?? 0)
                    .ThenBy(r => r.NeedByDateUtc);
                break;
        }

        var items = await orderedQuery
            .ThenBy(r => r.Id) // deterministic tie-breaker — keeps pagination order stable
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                Request = r,
                HasProforma = r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.Proforma),
                HasPO = r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PurchaseOrder),
                HasProof = r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PaymentProof),
                ScheduledHistory = r.StatusHistories.OrderByDescending(sh => sh.CreatedAtUtc).FirstOrDefault(sh => sh.NewStatus!.Code == RequestConstants.Statuses.PaymentScheduled),
                PaidHistory = r.StatusHistories.OrderByDescending(sh => sh.CreatedAtUtc).FirstOrDefault(sh => sh.NewStatus!.Code == RequestConstants.Statuses.Paid || sh.NewStatus!.Code == RequestConstants.Statuses.PaymentCompleted)
            })
            .ToListAsync();

        var dtoList = new List<FinanceListItemDto>();

        foreach (var item in items)
        {
            var r = item.Request;
            var isPaid = r.Status!.Code == RequestConstants.Statuses.Paid || r.Status.Code == RequestConstants.Statuses.PaymentCompleted || r.Status.Code == RequestConstants.Statuses.InFollowup || r.Status.Code == RequestConstants.Statuses.Completed || r.Status.Code == RequestConstants.Statuses.AdvancePaymentCompleted || r.ActualPaidAtUtc.HasValue || item.PaidHistory != null;
            var isQuotation = r.RequestType!.Code == RequestConstants.Types.Quotation;
            
            var missingDocs = new List<string>();
            if (isQuotation && !item.HasProforma) missingDocs.Add("PROFORMA");
            if (!item.HasPO) missingDocs.Add("PO");
            if (isPaid && !item.HasProof) missingDocs.Add("PAYMENT_PROOF");

            bool isMissingDocuments = (filter == "missingDocs") ? missingDocs.Count > 0 : missingDocs.Count > 0;

            if (filter == "missingDocs" && !isMissingDocuments) continue; // Manual filter application for missing docs

            // Single source of truth for action eligibility — shared with SchedulePayment/MarkAsPaid/
            // ReturnForAdjustment via IFinancePaymentEligibilityService, so listing can never advertise
            // an action the corresponding endpoint would reject (or hide one it would accept).
            var eligibilityInput = new FinanceEligibilityInput
            {
                RequestTypeCode = r.RequestType!.Code,
                RequestStatusCode = r.Status.Code,
                IsPaid = isPaid,
                HasProof = item.HasProof,
                PoGroups = r.PoGroups.Select(g => new FinancePoGroupEligibilityInput
                {
                    GroupId = g.Id,
                    GroupStatus = g.Status
                }).ToList()
            };
            var actions = _eligibility.Evaluate(eligibilityInput).Actions.ToList();

            dtoList.Add(new FinanceListItemDto
            {
                Id = r.Id,
                RequestNumber = r.RequestNumber ?? string.Empty,
                Title = r.Title ?? "---",
                SupplierName = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.SupplierNameSnapshot ?? "---"
                    : r.Supplier?.Name ?? "---",
                RequesterName = r.Requester!.FullName ?? "---",
                PlantName = r.Plant != null ? r.Plant.Name : "---",
                Amount = (r.ApprovedTotalAmount.HasValue && r.ApprovedTotalAmount.Value > 0)
                    ? r.ApprovedTotalAmount.Value
                    : r.SelectedQuotationId.HasValue
                        ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.TotalAmount ?? 0
                        : r.EstimatedTotalAmount,
                CurrencyCode = r.SelectedQuotationId.HasValue
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.Currency
                    : r.Currency != null ? r.Currency.Code : null,
                NeedByDateUtc = r.NeedByDateUtc,
                ScheduledDateUtc = r.ScheduledDateUtc,
                PaidDateUtc = item.PaidHistory?.CreatedAtUtc,
                StatusCode = r.Status.Code ?? "UNKNOWN",
                StatusName = r.Status.Name ?? "UNKNOWN",
                StatusBadgeColor = r.Status.BadgeColor ?? "gray",
                IsOverdue = !isPaid && (r.ScheduledDateUtc ?? r.NeedByDateUtc) < today,
                IsDueSoon = !isPaid && (r.ScheduledDateUtc ?? r.NeedByDateUtc) >= today && (r.ScheduledDateUtc ?? r.NeedByDateUtc) <= in4Days,
                IsMissingDocuments = isMissingDocuments,
                MissingDocumentTypes = missingDocs,
                AvailableFinanceActions = actions,
                // DEC-110: Financial snapshot & payment divergence
                ApprovedTotalAmount = r.ApprovedTotalAmount,
                ApprovedCurrencyCode = r.ApprovedCurrencyCode,
                ApprovedAtUtc = r.ApprovedAtUtc,
                ActualPaidAmount = r.ActualPaidAmount,
                ActualPaidAtUtc = r.ActualPaidAtUtc,
                HasPaymentDivergence = r.ApprovedTotalAmount.HasValue && r.ActualPaidAmount.HasValue
                    && Math.Round(r.ActualPaidAmount.Value, 2) != Math.Round(r.ApprovedTotalAmount.Value, 2),
                PaymentCondition = r.PaymentConditionCode,
                AdvancePaymentPercent = r.AdvancePaymentPercent,
                // Deliberately NOT filtered by financeGroupStatuses: the frontend needs every
                // group's real id/status to act on it (including a legacy group stuck at a
                // pre-finance status like PENDING, which is otherwise still the request's only
                // group). Row inclusion for QUOTATION is separately guarded below, against the
                // original (unfiltered-here) items collection, so that guard's intent is preserved.
                PoGroups = r.PoGroups
                    .Select(g => new RequestPoGroupDto
                {
                    Id = g.Id,
                    RequestId = g.RequestId,
                    // Legacy PAYMENT-type auto-created groups can carry a null snapshot (never
                    // actively synced) even though the parent request always knows its own
                    // supplier/currency — resolve a safe display value without writing back to
                    // the DB. See FinanceGroupDisplayResolver.
                    SupplierNameSnapshot = FinanceGroupDisplayResolver.ResolveSupplierName(
                        g.SupplierNameSnapshot,
                        r.SelectedQuotationId.HasValue,
                        r.SelectedQuotationId.HasValue ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.SupplierNameSnapshot : null,
                        r.Supplier?.Name),
                    CurrencyCode = FinanceGroupDisplayResolver.ResolveCurrencyCode(
                        g.CurrencyCode,
                        r.SelectedQuotationId.HasValue,
                        r.SelectedQuotationId.HasValue ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.Currency : null,
                        r.Currency?.Code),
                    TotalAmount = g.TotalAmount,
                    PaymentConditionCode = g.PaymentConditionCode,
                    AdvancePaymentPercent = g.AdvancePaymentPercent,
                    Status = g.Status,
                    // Per-group Finance actions — derived from THIS group's own status only, so the
                    // multi-group UI gates each card's buttons here (never on the request-level
                    // AvailableFinanceActions). A paid sibling can no longer suppress this group.
                    FinanceActions = _eligibility.EvaluateGroupActions(r.RequestType!.Code, r.Status.Code, g.Status).ToList(),
                    Payments = g.Payments.Select(p => new RequestPaymentDto
                    {
                        Id = p.Id,
                        PaymentType = p.PaymentType,
                        PaymentStatus = p.PaymentStatus,
                        PlannedAmount = p.PlannedAmount,
                        ActualPaidAmount = p.ActualPaidAmount,
                        ScheduledDateUtc = p.ScheduledDateUtc,
                        PaidDateUtc = p.PaidDateUtc,
                        CurrencyCode = p.CurrencyCode,
                        HasDivergence = p.HasDivergence
                    }).ToList()
                }).ToList()
            });
        }

        // ── Guard: exclude QUOTATION items with no finance-eligible group ──
        // This prevents QUOTATION requests that entered via the parent-status path (e.g. with a
        // request-level PO attachment but no group-linked PO) from appearing with zero finance groups.
        // Checked against the original (unfiltered) items collection — dto.PoGroups itself is no
        // longer status-filtered (see above), so it can't be used for this decision anymore.
        dtoList.RemoveAll(dto =>
        {
            var sourceItem = items.FirstOrDefault(i => i.Request.Id == dto.Id);
            if (sourceItem == null) return false;

            if (dto.StatusCode == RequestConstants.Statuses.PoPartiallyUploaded
                || sourceItem.Request.RequestType?.Code == RequestConstants.Types.Quotation)
            {
                // For QUOTATION, require at least one finance-eligible group
                return !sourceItem.Request.PoGroups.Any(g => financeGroupStatuses.Contains(g.Status));
            }
            return false;
        });

        if (filter == "missingDocs") totalCount = dtoList.Count; // Adjust count if filtered in memory

        return Ok(new FinanceListResponseDto 
        {
            PagedResult = new PagedResult<FinanceListItemDto>
            {
                Items = dtoList,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            },
            // Note: We leave Summary empty here to avoid recalculating; frontend calls /summary anyway
            Summary = new FinanceSummaryDto()
        });
    }

    /// <summary>
    /// Phase 3/4: Finance obligations projection — one row per RequestPoGroup (the independent
    /// financial unit), grouped under its Request container (Option C). Answers "what to do now,
    /// for which supplier, for how much, by when". Reuses the same finance scope/inclusion and the
    /// per-group eligibility as GetPayments; adds no workflow state. The legacy /payments endpoint
    /// is untouched for backward compatibility.
    /// </summary>
    [HttpGet("obligations")]
    public async Task<ActionResult<FinanceObligationsResponseDto>> GetObligations(
        [FromQuery] string? search = null,
        [FromQuery] string? currencyCode = null,
        [FromQuery] string? actionClass = null,
        [FromQuery] bool overdueOnly = false,
        [FromQuery] bool dueTodayOnly = false,
        [FromQuery] bool actionableOnly = false,
        [FromQuery] int? plantId = null,
        [FromQuery] int? departmentId = null,
        [FromQuery] int? companyId = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var scopedQuery = await GetScopedRequestsQuery();
        var today = DateTime.UtcNow.Date;

        // Build obligations + containers via the shared projection (same computation the Dashboard uses).
        var built = await new FinanceObligationSummaryProjection(_eligibility)
            .BuildAsync(scopedQuery, plantId, departmentId, companyId, today);
        var containersById = built.Containers;

        // ── Search / currency narrowing (applies to BOTH summary and list) ──
        // Matches request number, supplier name, P.O., title, AND the supplier NIF — both the group's
        // SupplierNifSnapshot and the canonical Supplier.TaxId — with digits-only normalization so a
        // pasted NIF ("5401126913" or "5401 126 913") matches regardless of punctuation.
        static string DigitsOnly(string? v) => v == null ? "" : new string(v.Where(char.IsLetterOrDigit).ToArray());
        var searchDigits = DigitsOnly(search);
        bool MatchesSearch(FinanceObligationDto o)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            var s = search.Trim().ToLowerInvariant();
            if ((o.RequestNumber?.ToLowerInvariant().Contains(s) ?? false)
                || (o.SupplierName?.ToLowerInvariant().Contains(s) ?? false)
                || (o.PurchaseOrderNumber?.ToLowerInvariant().Contains(s) ?? false)
                || (o.RequestTitle?.ToLowerInvariant().Contains(s) ?? false)) return true;
            // NIF / TaxId (only when the term carries digits, to avoid matching everything on empty)
            return searchDigits.Length > 0 &&
                (DigitsOnly(o.SupplierNif).Contains(searchDigits) || DigitsOnly(o.SupplierTaxId).Contains(searchDigits));
        }
        bool MatchesCurrency(FinanceObligationDto o) =>
            string.IsNullOrWhiteSpace(currencyCode) || string.Equals(o.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase);

        var scoped = built.Obligations.Where(o => MatchesSearch(o) && MatchesCurrency(o)).ToList();

        // ── Summary: over the search/currency-scoped set, ALL classes (cards are the filter entry points) ──
        var summary = FinanceObligationSummaryProjection.BuildSummary(scoped);

        // ── List filters (actionClass / overdue / dueToday / actionableOnly) ──
        bool MatchesListFilter(FinanceObligationDto o)
        {
            if (!string.IsNullOrWhiteSpace(actionClass) && !string.Equals(o.ActionClass, actionClass, StringComparison.OrdinalIgnoreCase)) return false;
            if (overdueOnly && !o.IsOverdue) return false;
            if (dueTodayOnly && !o.IsDueToday) return false;
            if (actionableOnly && !(o.ActionClass == FinanceActionClasses.NeedsScheduling || o.ActionClass == FinanceActionClasses.NeedsPayment || o.ActionClass == FinanceActionClasses.FiscalDocumentPending)) return false;
            return true;
        }

        // Requests that have ≥1 obligation matching the list filter are included; the container keeps
        // ALL its (search/currency-scoped) obligations for reconciliation context (paid siblings stay visible).
        var matchingRequestIds = scoped.Where(MatchesListFilter).Select(o => o.RequestId).ToHashSet();

        var containers = containersById.Values
            .Where(c => matchingRequestIds.Contains(c.RequestId))
            .Select(c =>
            {
                // Keep only obligations that passed search/currency; order actionable-first inside the container.
                var kept = c.Obligations.Where(o => MatchesSearch(o) && MatchesCurrency(o))
                    .OrderBy(ObligationUrgencyRank).ThenBy(o => o.DueDate ?? DateTime.MaxValue).ToList();
                c.Obligations = kept;
                c.TotalsByCurrency = FinanceObligationSummaryProjection.SumByCurrency(kept.Select(x => (x.CurrencyCode, x.ObligationAmount)));
                return c;
            })
            .ToList();

        // ── Sort containers. Default = newest first (Request creation date); FE exposes newest/oldest.
        //    The other keys stay available for existing callers. Intra-container obligations already
        //    order actionable-first (above), independent of the container sort. ──
        IEnumerable<FinanceObligationContainerDto> ordered = (sortBy?.ToLowerInvariant()) switch
        {
            "oldest" => containers.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.RequestNumber),
            "amount" => containers.OrderByDescending(c => c.Obligations.Sum(o => o.ObligationAmount)),
            "supplier" => containers.OrderBy(c => c.Obligations.FirstOrDefault()?.SupplierName ?? ""),
            "duedate" => containers.OrderBy(c => c.Obligations.Min(o => o.DueDate ?? DateTime.MaxValue)),
            "urgency" => containers
                .OrderBy(c => c.Obligations.Min(ObligationUrgencyRank))
                .ThenBy(c => c.Obligations.Min(o => o.DueDate ?? DateTime.MaxValue))
                .ThenBy(c => c.RequestNumber),
            // default + "newest"
            _ => containers.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.RequestNumber)
        };
        var orderedList = ordered.ToList();

        var totalContainers = orderedList.Count;
        var pageItems = orderedList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // ── Attach request-level Finance-note metadata (minimal: count + latest) — ONLY for the
        //    current page, batched, never loading full histories into rows. Same visibility model as
        //    the request itself (inclusion already gated by GetScopedRequestsQuery). ──
        var pageRequestIds = pageItems.Select(c => c.RequestId).ToList();
        if (pageRequestIds.Count > 0)
        {
            var noteRows = await _context.RequestStatusHistories
                .AsNoTracking()
                .Where(h => pageRequestIds.Contains(h.RequestId) && h.ActionTaken == "NOTA_FINANCEIRA")
                .Select(h => new { h.RequestId, h.Comment, h.CreatedAtUtc, ActorName = h.ActorUser != null ? h.ActorUser.FullName : null })
                .ToListAsync();
            foreach (var grp in noteRows.GroupBy(n => n.RequestId))
            {
                var c = pageItems.FirstOrDefault(x => x.RequestId == grp.Key);
                if (c == null) continue;
                var latest = grp.OrderByDescending(n => n.CreatedAtUtc).First();
                var text = latest.Comment ?? string.Empty;
                const string prefix = "Nota de Finanças: ";
                if (text.StartsWith(prefix)) text = text.Substring(prefix.Length);
                if (text.Length > 280) text = text.Substring(0, 280) + "…";
                c.HasNotes = true;
                c.NoteCount = grp.Count();
                c.LatestNoteText = text;
                c.LatestNoteAtUtc = latest.CreatedAtUtc;
                c.LatestNoteActorName = latest.ActorName;
            }
        }

        return Ok(new FinanceObligationsResponseDto
        {
            PagedResult = new PagedResult<FinanceObligationContainerDto>
            {
                Items = pageItems,
                TotalCount = totalContainers,
                Page = page,
                PageSize = pageSize
            },
            Summary = summary
        });
    }

    // Lower = more urgent. Drives default container/obligation ordering.
    private static int ObligationUrgencyRank(FinanceObligationDto o)
    {
        if (o.IsOverdue) return 0;
        if (o.IsDueToday) return 1;
        return o.ActionClass switch
        {
            FinanceActionClasses.NeedsPayment => 2,
            FinanceActionClasses.NeedsScheduling => 3,
            FinanceActionClasses.FiscalDocumentPending => 4,
            FinanceActionClasses.InReceivingFollowup => 5,
            FinanceActionClasses.PaidWaitingReceiving => 6,
            FinanceActionClasses.Completed => 7,
            _ => 8
        };
    }

    [HttpGet("history")]
    public async Task<ActionResult<PagedResult<FinanceHistoryItemDto>>> GetHistory(
        [FromQuery] string? search = null,
        [FromQuery] string? actionType = null,
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 20)
    {
        var scopedQuery = await GetScopedRequestsQuery();
        var requestIds = await scopedQuery.Select(r => r.Id).ToListAsync();

        var financeActionCodes = new[] { "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "ADVANCE_PAYMENT_COMPLETED", "DOCUMENTO ADICIONADO", "NOTA_FINANCEIRA", "FINANCE_RETURN_ADJUSTMENT", "PAYMENT_DIVERGENCE_DETECTED", "PAYMENT_SCHEDULE_CANCELLED", "ADVANCE_PAYMENT_SCHEDULE_CANCELLED" };
        var financeStatusCodes = new[] { RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted };

        var query = _context.RequestStatusHistories
            .Include(sh => sh.NewStatus)
            .Include(sh => sh.ActorUser)
            .Include(sh => sh.Request!)
                .ThenInclude(r => r.Quotations)
            .Include(sh => sh.Request!)
                .ThenInclude(r => r.Currency)
            .Where(sh => requestIds.Contains(sh.RequestId) && 
                (financeStatusCodes.Contains(sh.NewStatus!.Code) || financeActionCodes.Contains(sh.ActionTaken)));

        if (!string.IsNullOrWhiteSpace(actionType))
        {
            // "Pagos" (PAYMENT_COMPLETED) must also surface advance-payment completions — same
            // financial category from the user's perspective, just a different group track. Every
            // other tab keeps its exact single-code match.
            var actionTypeCodes = actionType == "PAYMENT_COMPLETED"
                ? new[] { "PAYMENT_COMPLETED", "ADVANCE_PAYMENT_COMPLETED" }
                : actionType == "PAYMENT_SCHEDULE_CANCELLED"
                    ? new[] { "PAYMENT_SCHEDULE_CANCELLED", "ADVANCE_PAYMENT_SCHEDULE_CANCELLED" }
                    : new[] { actionType };
            query = query.Where(sh => actionTypeCodes.Contains(sh.ActionTaken));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(sh => 
                (sh.ActorUser != null && sh.ActorUser.FullName != null && sh.ActorUser.FullName.ToLower().Contains(searchLower)) ||
                (sh.Comment != null && sh.Comment.ToLower().Contains(searchLower)) ||
                (sh.Request != null && sh.Request.RequestNumber != null && sh.Request.RequestNumber.ToLower().Contains(searchLower)) ||
                (sh.Request != null && sh.Request.Title != null && sh.Request.Title.ToLower().Contains(searchLower))
            );
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(sh => sh.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(sh => new HistoryProjectionRow(
                sh.Id,
                sh.RequestId,
                sh.Request!.RequestNumber ?? "---",
                sh.Request.Title ?? "---",
                sh.Comment,
                sh.ActionTaken ?? "Unknown",
                sh.CreatedAtUtc,
                sh.ActorUser!.FullName ?? "Unknown",
                sh.NewStatus!.Code,
                sh.NewStatus.Name,
                sh.Request.PaymentConditionCode,
                sh.Request.AdvancePaymentPercent,
                sh.Request.SelectedQuotationId.HasValue
                    ? sh.Request.Quotations.FirstOrDefault(q => q.Id == sh.Request.SelectedQuotationId.Value)!.Currency
                    : (sh.Request.Currency != null ? sh.Request.Currency.Code : "---"),
                sh.Request.ApprovedTotalAmount,
                sh.Request.SelectedQuotationId.HasValue
                    ? sh.Request.Quotations.Where(q => q.Id == sh.Request.SelectedQuotationId.Value).Select(q => (decimal?)q.TotalAmount).FirstOrDefault()
                    : null,
                sh.Request.EstimatedTotalAmount))
            .ToListAsync();

        var attachmentVoidLookup = await BuildScheduleAttachmentVoidLookupAsync(rows);
        var items = ProjectHistoryItems(rows, attachmentVoidLookup);

        return Ok(new PagedResult<FinanceHistoryItemDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    /// <summary>
    /// RequestStatusHistory has no RequestAttachment FK, so a DOCUMENTO ADICIONADO event's void
    /// status can only be found via a best-effort match on the filename embedded in its own Comment
    /// (see AttachmentsController.Upload's exact "Documento &quot;{FileName}&quot; (...)" shape) —
    /// scoped to PAYMENT_SCHEDULE-typed attachments only, one small batched query per page/export,
    /// never a per-row query. Display-only: never used for any eligibility/validation decision.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex UploadedFileNamePattern =
        new(@"Documento ""(?<fileName>.*?)""", System.Text.RegularExpressions.RegexOptions.Compiled);

    private async Task<Dictionary<(Guid RequestId, string FileName), (DateTime? VoidedAtUtc, string? VoidReason)>> BuildScheduleAttachmentVoidLookupAsync(
        List<HistoryProjectionRow> rows)
    {
        var candidateRequestIds = rows
            .Where(r => r.ActionTaken == "DOCUMENTO ADICIONADO" && r.Comment != null && r.Comment.Contains("Cronograma de Pagamento"))
            .Select(r => r.RequestId)
            .Distinct()
            .ToList();

        if (candidateRequestIds.Count == 0)
        {
            return new Dictionary<(Guid, string), (DateTime?, string?)>();
        }

        var scheduleAttachments = await _context.RequestAttachments
            .Where(a => candidateRequestIds.Contains(a.RequestId) && a.AttachmentTypeCode == AttachmentConstants.Types.PaymentSchedule)
            .Select(a => new { a.RequestId, a.FileName, a.VoidedAtUtc, a.VoidReason })
            .ToListAsync();

        var lookup = new Dictionary<(Guid, string), (DateTime?, string?)>();
        foreach (var a in scheduleAttachments)
        {
            // Last-write-wins on a duplicate filename for the same request — display-only best
            // effort, same tolerance CancelSchedule itself accepts for the ambiguous case.
            lookup[(a.RequestId, a.FileName)] = (a.VoidedAtUtc, a.VoidReason);
        }
        return lookup;
    }

    /// <summary>
    /// Raw, EF-translatable projection of a RequestStatusHistory row plus everything
    /// FinanceHistoryAmountResolver needs to compute its display Amount. Shared by GetHistory and
    /// ExportHistory so both use the exact same event-aware resolution — see ProjectHistoryItems.
    /// </summary>
    private sealed record HistoryProjectionRow(
        Guid Id,
        Guid RequestId,
        string RequestNumber,
        string RequestTitle,
        string? Comment,
        string ActionTaken,
        DateTime CreatedAtUtc,
        string ActorName,
        string? NewStatusCode,
        string? NewStatusName,
        string? PaymentCondition,
        decimal? AdvancePaymentPercent,
        string? CurrencyCode,
        decimal? ApprovedTotalAmount,
        decimal? SelectedQuotationTotalAmount,
        decimal EstimatedTotalAmount);

    /// <summary>
    /// Maps raw rows to FinanceHistoryItemDto, resolving each event's Amount via
    /// FinanceHistoryAmountResolver (regex parsing can't run inside the EF-translated query
    /// above, hence the two-step projection). The single point where GetHistory and ExportHistory
    /// share amount-resolution logic — do not reintroduce a second inline copy.
    /// attachmentVoidLookup is optional: only DOCUMENTO ADICIONADO rows for a PAYMENT_SCHEDULE
    /// upload are ever looked up, and a missing/empty lookup simply leaves IsVoided false.
    /// </summary>
    private static List<FinanceHistoryItemDto> ProjectHistoryItems(
        List<HistoryProjectionRow> rows,
        IReadOnlyDictionary<(Guid RequestId, string FileName), (DateTime? VoidedAtUtc, string? VoidReason)>? attachmentVoidLookup = null) =>
        rows.Select(row =>
        {
            var resolution = FinanceHistoryAmountResolver.Resolve(
                row.Comment,
                row.ActionTaken,
                row.ApprovedTotalAmount,
                row.SelectedQuotationTotalAmount,
                row.EstimatedTotalAmount);

            bool isVoided = false;
            string? voidReason = null;
            if (attachmentVoidLookup != null && row.ActionTaken == "DOCUMENTO ADICIONADO" && row.Comment != null)
            {
                var fileNameMatch = UploadedFileNamePattern.Match(row.Comment);
                if (fileNameMatch.Success
                    && attachmentVoidLookup.TryGetValue((row.RequestId, fileNameMatch.Groups["fileName"].Value), out var voidInfo)
                    && voidInfo.VoidedAtUtc.HasValue)
                {
                    isVoided = true;
                    voidReason = voidInfo.VoidReason;
                }
            }

            return new FinanceHistoryItemDto
            {
                Id = row.Id,
                RequestId = row.RequestId,
                RequestNumber = row.RequestNumber,
                RequestTitle = row.RequestTitle,
                Amount = resolution.Amount,
                CurrencyCode = row.CurrencyCode,
                ActionTaken = row.ActionTaken,
                Comment = row.Comment ?? string.Empty,
                CreatedAtUtc = row.CreatedAtUtc,
                ActorName = row.ActorName,
                NewStatusCode = row.NewStatusCode ?? string.Empty,
                NewStatusName = row.NewStatusName ?? string.Empty,
                PaymentCondition = row.PaymentCondition,
                AdvancePaymentPercent = row.AdvancePaymentPercent,
                IsVoided = isVoided,
                VoidReason = voidReason
            };
        }).ToList();

    [HttpGet("history/export")]
    public async Task<IActionResult> ExportHistory(
        [FromQuery] string? search = null,
        [FromQuery] string? actionType = null)
    {
        var scopedQuery = await GetScopedRequestsQuery();
        var requestIds = await scopedQuery.Select(r => r.Id).ToListAsync();

        var financeActionCodes = new[] { "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "ADVANCE_PAYMENT_COMPLETED", "DOCUMENTO ADICIONADO", "NOTA_FINANCEIRA", "FINANCE_RETURN_ADJUSTMENT", "PAYMENT_DIVERGENCE_DETECTED", "PAYMENT_SCHEDULE_CANCELLED", "ADVANCE_PAYMENT_SCHEDULE_CANCELLED" };
        var financeStatusCodes = new[] { RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted };

        var query = _context.RequestStatusHistories
            .Include(sh => sh.NewStatus)
            .Include(sh => sh.ActorUser)
            .Include(sh => sh.Request!)
                .ThenInclude(r => r.Quotations)
            .Include(sh => sh.Request!)
                .ThenInclude(r => r.Currency)
            .Where(sh => requestIds.Contains(sh.RequestId) && 
                (financeStatusCodes.Contains(sh.NewStatus!.Code) || financeActionCodes.Contains(sh.ActionTaken)));

        if (!string.IsNullOrWhiteSpace(actionType))
        {
            var actionTypeCodes = actionType == "PAYMENT_COMPLETED"
                ? new[] { "PAYMENT_COMPLETED", "ADVANCE_PAYMENT_COMPLETED" }
                : actionType == "PAYMENT_SCHEDULE_CANCELLED"
                    ? new[] { "PAYMENT_SCHEDULE_CANCELLED", "ADVANCE_PAYMENT_SCHEDULE_CANCELLED" }
                    : new[] { actionType };
            query = query.Where(sh => actionTypeCodes.Contains(sh.ActionTaken));
        }
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(sh => 
                (sh.ActorUser != null && sh.ActorUser.FullName != null && sh.ActorUser.FullName.ToLower().Contains(searchLower)) ||
                (sh.Comment != null && sh.Comment.ToLower().Contains(searchLower)) ||
                (sh.Request != null && sh.Request.RequestNumber != null && sh.Request.RequestNumber.ToLower().Contains(searchLower)) ||
                (sh.Request != null && sh.Request.Title != null && sh.Request.Title.ToLower().Contains(searchLower))
            );
        }

        var rows = await query
            .OrderByDescending(sh => sh.CreatedAtUtc)
            .Take(5000)
            .Select(sh => new HistoryProjectionRow(
                sh.Id,
                sh.RequestId,
                sh.Request!.RequestNumber ?? "---",
                sh.Request.Title ?? "---",
                sh.Comment,
                sh.ActionTaken ?? "Unknown",
                sh.CreatedAtUtc,
                sh.ActorUser!.FullName ?? "Unknown",
                sh.NewStatus!.Code,
                sh.NewStatus.Name,
                sh.Request.PaymentConditionCode,
                sh.Request.AdvancePaymentPercent,
                sh.Request.SelectedQuotationId.HasValue
                    ? sh.Request.Quotations.FirstOrDefault(q => q.Id == sh.Request.SelectedQuotationId.Value)!.Currency
                    : (sh.Request.Currency != null ? sh.Request.Currency.Code : "---"),
                sh.Request.ApprovedTotalAmount,
                sh.Request.SelectedQuotationId.HasValue
                    ? sh.Request.Quotations.Where(q => q.Id == sh.Request.SelectedQuotationId.Value).Select(q => (decimal?)q.TotalAmount).FirstOrDefault()
                    : null,
                sh.Request.EstimatedTotalAmount))
            .ToListAsync();

        var attachmentVoidLookup = await BuildScheduleAttachmentVoidLookupAsync(rows);
        var items = ProjectHistoryItems(rows, attachmentVoidLookup);

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Data/Hora;Acao;Responsavel;Ref. Pedido;Titulo;Moeda;Montante;Cond. Pagamento;% Adiant.;Detalhes");

        foreach (var item in items)
        {
            var comment = item.Comment?.Replace(";", ",").Replace("\r", "").Replace("\n", " ") ?? "";
            var actionStr = item.ActionTaken switch {
                "PAYMENT_SCHEDULED" => "Agendado",
                "PAYMENT_COMPLETED" => "Pago",
                "ADVANCE_PAYMENT_COMPLETED" => "Adiantamento Realizado",
                "DOCUMENTO ADICIONADO" => "Comprovativo",
                "NOTA_FINANCEIRA" => "Observação",
                "FINANCE_RETURN_ADJUSTMENT" => "Ajuste",
                "PAYMENT_DIVERGENCE_DETECTED" => "Divergência",
                "PAYMENT_SCHEDULE_CANCELLED" => "Agendamento Cancelado",
                "ADVANCE_PAYMENT_SCHEDULE_CANCELLED" => "Adiantamento Cancelado",
                _ => item.ActionTaken
            };
            csv.AppendLine($"{item.CreatedAtUtc:yyyy-MM-dd HH:mm:ss};{actionStr};{item.ActorName};{item.RequestNumber};{item.RequestTitle};{item.CurrencyCode};{item.Amount};{item.PaymentCondition};{item.AdvancePaymentPercent};{comment}");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv", $"auditoria_financas_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    [HttpPost("{id:guid}/schedule")]
    public async Task<IActionResult> SchedulePayment(Guid id, [FromBody] SchedulePaymentDto requestDto)
    {
        var r = await _context.Requests
            .Include(req => req.PoGroups)
                .ThenInclude(g => g.ApprovalBatch)
            .Include(req => req.PoGroups)
                .ThenInclude(g => g.Payments)
            .Include(req => req.Status)
            .Include(req => req.RequestType)
            .Include(req => req.Supplier)
            .Include(req => req.Currency)
            .Include(req => req.Quotations)
            .FirstOrDefaultAsync(req => req.Id == id);
        if (r == null || !await (await GetScopedRequestsQuery()).AnyAsync(req => req.Id == id)) return NotFound();

        var group = r.PoGroups.FirstOrDefault(g => g.Id == requestDto.RequestPoGroupId);
        if (group == null) return BadRequest("Grupo P.O não encontrado no request.");

        // ── DEC-110: Status Guard — via IFinancePaymentEligibilityService, same rule GetPayments uses ──
        var allowedScheduleStatuses = new[] {
            RequestConstants.Statuses.PoIssued,
            RequestConstants.Statuses.PaymentRequestSent,
            RequestConstants.Statuses.AdvancePaymentRequired
        };
        if (!_eligibility.CanSchedule(r.RequestType!.Code, r.Status!.Code, group.Status))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Inválida",
                Detail = $"O agendamento de pagamento só é permitido nos status: {string.Join(", ", allowedScheduleStatuses)}. Status atual do grupo: {group.Status}.",
                Status = 400
            });
        }

        var newStatus = group.Status == RequestConstants.Statuses.AdvancePaymentRequired
            ? RequestConstants.Statuses.AdvancePaymentScheduled
            : RequestConstants.Statuses.PaymentScheduled;

        var paymentType = group.Status == RequestConstants.Statuses.AdvancePaymentRequired ? RequestPayment.PaymentTypes.Advance : RequestPayment.PaymentTypes.FinalBalance;
        // REQUEST-scoped sequence via the canonical allocator (the unique index is
        // (RequestId, PaymentType, PaymentSequence)). A per-group Max() would restart at 1 for each
        // sibling group and collide; the allocator also counts group-less payments (e.g. a
        // reconciliation remaining-balance row) and preserves CANCELLED sequences without reuse.
        var nextSequence = await AlplaPortal.Api.Services.PaymentSequenceAllocator
            .NextSequenceAsync(_context, r.Id, paymentType);

        var payment = new RequestPayment
        {
            RequestId = r.Id,
            RequestPoGroupId = group.Id,
            PlannedAmount = group.TotalAmount,
            PaymentType = paymentType,
            PaymentSequence = nextSequence,
            ScheduledDateUtc = requestDto.ScheduledDate,
            ScheduledByUserId = CurrentUserId,
            PaymentStatus = RequestPayment.PaymentStatuses.Scheduled,
            CurrencyCode = group.CurrencyCode ?? "---",
            Notes = requestDto.Comment,
            CreatedByUserId = CurrentUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestPayments.Add(payment);

        // Legacy PAYMENT-type auto-created groups can carry a null SupplierNameSnapshot/CurrencyCode
        // (never actively synced) even though the parent request always knows its own supplier/
        // currency — resolve a safe display value for the audit comment only; never written back
        // to the group/payment record. See FinanceGroupDisplayResolver.
        var displaySupplierName = FinanceGroupDisplayResolver.ResolveSupplierName(
            group.SupplierNameSnapshot,
            r.SelectedQuotationId.HasValue,
            r.SelectedQuotationId.HasValue ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.SupplierNameSnapshot : null,
            r.Supplier?.Name);
        var displayCurrencyCode = FinanceGroupDisplayResolver.ResolveCurrencyCode(
            group.CurrencyCode,
            r.SelectedQuotationId.HasValue,
            r.SelectedQuotationId.HasValue ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.Currency : null,
            r.Currency?.Code);

        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = CurrentUserId,
            ActionTaken = newStatus,
            PreviousStatusId = r.StatusId,
            NewStatusId = r.StatusId,
            Comment = FinanceHistoryCommentFormatter.FormatGroupPrefix(group.ApprovalBatch?.BatchNumber, displaySupplierName, displayCurrencyCode, "Total", group.TotalAmount)
                + " Pagamento agendado. " + (requestDto.Comment ?? ""),
            CreatedAtUtc = DateTime.UtcNow
        };

        group.Status = newStatus;
        group.UpdatedAtUtc = DateTime.UtcNow;
        r.UpdatedAtUtc = DateTime.UtcNow;

        _context.RequestStatusHistories.Add(history);
        await _context.SaveChangesAsync();

        await _statusAggregationService.AggregateRequestStatusAsync(id, CurrentUserId);

        try
        {
            var actor = await _context.Users.FindAsync(CurrentUserId);
            await _orchestrator.EmitAsync(new WorkflowEvent
            {
                EventCode = WorkflowEventCodes.PaymentScheduled,
                RequestId = id,
                RequestNumber = r.RequestNumber ?? "S/N",
                RequestTitle = r.Title ?? "",
                TargetStatusCode = newStatus,
                ActionTaken = newStatus,
                ActorUserId = CurrentUserId,
                ActorName = actor?.FullName ?? "Sistema",
                Comment = requestDto.Comment,
                CorrelationId = history.Id,
                RequesterId = r.RequesterId,
                BuyerId = r.BuyerId,
                AreaApproverId = r.AreaApproverId,
                FinalApproverId = r.FinalApproverId,
                DepartmentId = r.DepartmentId,
                PlantId = r.PlantId,
                CompanyId = r.CompanyId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-critical: notification dispatch failed for SchedulePayment on Request {RequestId}", id);
        }

        return Ok();
    }


    [HttpPost("{id:guid}/pay")]
    public async Task<IActionResult> MarkAsPaid(Guid id, [FromBody] ConfirmPaymentDto requestDto)
    {
        var r = await _context.Requests
            .Include(req => req.PoGroups)
                .ThenInclude(g => g.ApprovalBatch)
            .Include(req => req.Status)
            .Include(req => req.RequestType)
            .FirstOrDefaultAsync(req => req.Id == id);
        if (r == null || !await (await GetScopedRequestsQuery()).AnyAsync(req => req.Id == id)) return NotFound();

        var group = r.PoGroups.FirstOrDefault(g => g.Id == requestDto.RequestPoGroupId);
        if (group == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Grupo P.O Não Encontrado",
                Detail = "Grupo da P.O não encontrado para este pagamento. Verifique se o grupo P.O foi emitido corretamente.",
                Status = 400
            });
        }

        // ── Status Guard: group-first for QUOTATION, request-level for PAYMENT ──
        // Both branches now delegate to IFinancePaymentEligibilityService.CanPay — the same
        // predicate GetPayments uses to compute AvailableFinanceActions.
        if (r.RequestType?.Code == RequestConstants.Types.Quotation)
        {
            // QUOTATION: guard on group status, including advance payment statuses
            var allowedGroupPayStatuses = new[] {
                RequestConstants.Statuses.PoIssued,
                RequestConstants.Statuses.PaymentRequestSent,
                RequestConstants.Statuses.PaymentScheduled,
                RequestConstants.Statuses.AdvancePaymentRequired,
                RequestConstants.Statuses.AdvancePaymentScheduled
            };
            if (!_eligibility.CanPay(r.RequestType.Code, r.Status?.Code ?? string.Empty, group.Status))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Status Atual Não Permite Liquidação",
                    Detail = $"A confirmação de pagamento só é permitida para grupos nos status: " +
                             $"{string.Join(", ", allowedGroupPayStatuses)}. Status atual do grupo: {group.Status}.",
                    Status = 400
                });
            }
        }
        else
        {
            // PAYMENT: preserve existing request-level guard
            var allowedPayStatuses = new[] {
                RequestConstants.Statuses.PoIssued,
                RequestConstants.Statuses.PaymentRequestSent,
                RequestConstants.Statuses.PaymentScheduled
            };
            if (r.Status == null || !_eligibility.CanPay(r.RequestType?.Code ?? string.Empty, r.Status.Code, group.Status))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Status Atual Não Permite Liquidação",
                    Detail = $"A confirmação de pagamento só é permitida nos status: " +
                             $"{string.Join(", ", allowedPayStatuses)}. Status atual: {r.Status?.Code ?? "desconhecido"}.",
                    Status = 400
                });
            }
        }

        // ── DEC-110: Mandatory ActualPaidAmount ──────────────────────────────
        if (requestDto.ActualPaidAmount <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Montante Obrigatório",
                Detail = "O montante efetivamente pago é obrigatório e deve ser superior a zero.",
                Status = 400
            });
        }

        // ── Mandatory Payment Proof ──────────────────────────────────────────
        if (requestDto.PaymentProofAttachmentId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Comprovativo Obrigatório",
                Detail = "Comprovativo de pagamento é obrigatório. Por favor, anexe o comprovante antes de confirmar.",
                Status = 400
            });
        }

        // ── Minimum-Amount Guard: partial payments are not supported by this action — a group
        // must never be silently closed as fully paid for less than the required amount. This
        // check runs before any mutation. Overpayment remains allowed (existing divergence
        // detection further below still applies to it). ──
        if (r.RequestType?.Code == RequestConstants.Types.Quotation)
        {
            var requiredAmount = group.TotalAmount;
            if (requestDto.ActualPaidAmount < requiredAmount)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Montante Insuficiente",
                    Detail = $"O montante pago ({requestDto.ActualPaidAmount:N2}) é inferior ao valor total do grupo P.O. ({requiredAmount:N2}). Pagamentos parciais não são suportados por esta ação.",
                    Status = 400
                });
            }
        }

        var paidStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.PaymentCompleted || s.Code == RequestConstants.Statuses.Paid);
        if (paidStatus == null) return BadRequest("Status PAID não configurado.");

        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = CurrentUserId,
            ActionTaken = "PAYMENT_COMPLETED",
            PreviousStatusId = r.StatusId,
            NewStatusId = paidStatus.Id,
            Comment = FinanceHistoryCommentFormatter.FormatGroupPrefix(group.ApprovalBatch?.BatchNumber, group.SupplierNameSnapshot, group.CurrencyCode, "Montante", requestDto.ActualPaidAmount)
                + $" Pagamento realizado. Pago em {requestDto.PaidDate:dd/MM/yyyy}. " + (requestDto.Comment ?? ""),
            CreatedAtUtc = DateTime.UtcNow
        };

        // QUOTATION: the parent status is decided below by the shared StatusAggregationService,
        // which reflects the furthest-behind sibling group — never unconditionally set here, or a
        // sibling still at PO_ISSUED/ADVANCE_PAYMENT_REQUIRED would be masked by this one group's
        // completion. PAYMENT requests are single-group by design, so the direct assignment (matching
        // every other PAYMENT-type transition in this method) remains correct and unchanged.
        if (r.RequestType?.Code != RequestConstants.Types.Quotation)
        {
            r.StatusId = paidStatus.Id;
        }
        group.Status = paidStatus.Code;
        group.UpdatedAtUtc = DateTime.UtcNow;

        // Ledger completeness. Complete the group's ACTIVE FINAL_BALANCE row when one exists (the
        // scheduled path — unchanged). Otherwise CREATE the row: a direct pay from PO_ISSUED previously
        // left NO RequestPayment, which undercounts reconciliation's actualPaidSum and starves the
        // obligations projection of the paid amount/proof. A CANCELLED or already-COMPLETED row is never
        // revived — its sequence is preserved and the new payment lands on the next request-scoped
        // sequence via the canonical allocator.
        // MAINTENANCE TRIGGER: Finance payment mutations MUST be validated against the Finance DEV
        // Regression Harness (ZZTEST-FIN-*) — docs/FINANCE_DEV_REGRESSION_HARNESS.md.
        var payment = await _context.RequestPayments.FirstOrDefaultAsync(p =>
            p.RequestPoGroupId == group.Id
            && p.PaymentType == RequestPayment.PaymentTypes.FinalBalance
            && p.PaymentStatus != RequestPayment.PaymentStatuses.Cancelled
            && p.PaymentStatus != RequestPayment.PaymentStatuses.Completed);
        if (payment != null) {
            payment.ActualPaidAmount = requestDto.ActualPaidAmount;
            payment.PaidDateUtc = requestDto.PaidDate;
            payment.PaymentStatus = RequestPayment.PaymentStatuses.Completed;
            payment.PaymentProofAttachmentId = requestDto.PaymentProofAttachmentId;
        }
        else {
            var finalBalanceSequence = await AlplaPortal.Api.Services.PaymentSequenceAllocator
                .NextSequenceAsync(_context, r.Id, RequestPayment.PaymentTypes.FinalBalance);
            payment = new RequestPayment
            {
                RequestId = r.Id,
                RequestPoGroupId = group.Id,
                PaymentType = RequestPayment.PaymentTypes.FinalBalance,
                PaymentSequence = finalBalanceSequence,
                PlannedAmount = group.TotalAmount,
                ActualPaidAmount = requestDto.ActualPaidAmount,
                PaidDateUtc = requestDto.PaidDate,
                PaidByUserId = CurrentUserId,
                PaymentStatus = RequestPayment.PaymentStatuses.Completed,
                PaymentProofAttachmentId = requestDto.PaymentProofAttachmentId,
                CurrencyCode = group.CurrencyCode ?? "---",
                CreatedByUserId = CurrentUserId,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.RequestPayments.Add(payment);
        }

        var attachment = await _context.RequestAttachments.FindAsync(requestDto.PaymentProofAttachmentId);
        if (attachment != null) attachment.RequestPoGroupId = group.Id;

        r.UpdatedAtUtc = DateTime.UtcNow;

        // ── DEC-110: Actual Payment Capture ──────────────────────────────────
        r.ActualPaidAmount = requestDto.ActualPaidAmount;
        r.ActualPaidAtUtc = requestDto.PaidDate;

        _context.RequestStatusHistories.Add(history);

        // Phase 4C: the actual-payment fact (never SCHEDULED) may satisfy the group's payment
        // dimension — Phase 1 runs on the freshly tracked paid state, in this same save.
        if (_completionService != null)
            await _completionService.EvaluateGroupCompletionAsync(id, group.Id, CurrentUserId);

        await _context.SaveChangesAsync();

        // QUOTATION: now that this group's paid status is persisted, let the shared aggregator
        // (same one SchedulePayment/RegisterPo/ConfirmAdvancePayment already use) decide the
        // parent's status from every group's current state — a sibling still pending must keep
        // the parent from reading as globally PAYMENT_COMPLETED.
        if (r.RequestType?.Code == RequestConstants.Types.Quotation)
        {
            await _statusAggregationService.AggregateRequestStatusAsync(id, CurrentUserId);
        }

        // ── Divergence Detection: group-first for QUOTATION, request-level for PAYMENT ──
        decimal? comparisonAmount;
        string comparisonLabel;
        if (r.RequestType?.Code == RequestConstants.Types.Quotation)
        {
            // QUOTATION: compare against group amount (operational source of truth)
            comparisonAmount = group.TotalAmount;
            comparisonLabel = "Grupo P.O.";
        }
        else
        {
            // PAYMENT: compare against approved request total
            comparisonAmount = r.ApprovedTotalAmount;
            comparisonLabel = "Pedido";
        }

        if (comparisonAmount.HasValue && comparisonAmount.Value > 0)
        {
            var roundedPaid = Math.Round(r.ActualPaidAmount.Value, 2);
            var roundedExpected = Math.Round(comparisonAmount.Value, 2);
            var diff = roundedPaid - roundedExpected;
            var absDiff = Math.Abs(diff);

            if (absDiff > 0)
            {
                var pctDiff = roundedExpected != 0
                    ? (absDiff / Math.Abs(roundedExpected) * 100).ToString("F2")
                    : "N/A";

                var direction = diff < 0 ? "abaixo" : "acima";
                var divergenceComment = $"[SISTEMA] Pagamento realizado {direction} do valor aprovado ({comparisonLabel}). " +
                    $"Montante Esperado={roundedExpected:N2}, " +
                    $"Montante Pago={roundedPaid:N2}, " +
                    $"Diferença={absDiff:N2} ({pctDiff}%).";

                _context.RequestStatusHistories.Add(new RequestStatusHistory
                {
                    Id = Guid.NewGuid(),
                    RequestId = r.Id,
                    ActorUserId = CurrentUserId,
                    ActionTaken = "PAYMENT_DIVERGENCE_DETECTED",
                    PreviousStatusId = paidStatus.Id,
                    NewStatusId = paidStatus.Id,
                    Comment = divergenceComment,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
        }

        // [TEMPORARY NON-CENTRAL HOOK]
        try
        {
            var actor = await _context.Users.FindAsync(CurrentUserId);
            await _orchestrator.EmitAsync(new WorkflowEvent
            {
                EventCode = WorkflowEventCodes.PaymentCompleted,
                RequestId = id,
                RequestNumber = r.RequestNumber ?? "S/N",
                RequestTitle = r.Title ?? "",
                TargetStatusCode = "PAYMENT_COMPLETED",
                ActionTaken = "PAYMENT_COMPLETED",
                ActorUserId = CurrentUserId,
                ActorName = actor?.FullName ?? "Sistema",
                Comment = requestDto.Comment,
                CorrelationId = history.Id,
                RequesterId = r.RequesterId,
                BuyerId = r.BuyerId,
                AreaApproverId = r.AreaApproverId,
                FinalApproverId = r.FinalApproverId,
                DepartmentId = r.DepartmentId,
                PlantId = r.PlantId,
                CompanyId = r.CompanyId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-critical: notification dispatch failed for MarkAsPaid on Request {RequestId}", id);
        }

        // Phase 2 strictly after every save of this action; never fails the payment.
        if (_completionService != null)
        {
            try
            {
                await _completionService.EvaluateParentCompletionAsync(id, CurrentUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Non-critical: parent completion evaluation failed after MarkAsPaid on Request {RequestId}.", id);
            }
        }

        return Ok();
    }

    [HttpPost("{id:guid}/note")]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] FinanceActionRequestDto requestDto)
    {
        if (string.IsNullOrWhiteSpace(requestDto.Notes)) return BadRequest();
        var r = await _context.Requests.Include(req => req.Status).FirstOrDefaultAsync(req => req.Id == id);
        if (r == null || !await (await GetScopedRequestsQuery()).AnyAsync(req => req.Id == id)) return NotFound();

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = CurrentUserId,
            ActionTaken = "NOTA_FINANCEIRA",
            PreviousStatusId = r.StatusId,
            NewStatusId = r.StatusId,
            Comment = $"Nota de Finanças: {requestDto.Notes}",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id:guid}/return")]
    public async Task<IActionResult> ReturnForAdjustment(Guid id, [FromBody] FinanceActionRequestDto requestDto)
    {
        // v2.230.0: Return for Adjustment is now GROUP-scoped. It moves ONE RequestPoGroup back to
        // WAITING_PO_CORRECTION and lets StatusAggregationService derive the request scalar — a
        // further-ahead sibling group (e.g. already PAYMENT_COMPLETED) is never touched or regressed.
        var r = await _context.Requests
            .Include(req => req.PoGroups)
                .ThenInclude(g => g.ApprovalBatch)
            .Include(req => req.Status)
            .Include(req => req.RequestType)
            .Include(req => req.Supplier)
            .Include(req => req.Currency)
            .Include(req => req.Quotations)
            .FirstOrDefaultAsync(req => req.Id == id);
        if (r == null || !await (await GetScopedRequestsQuery()).AnyAsync(req => req.Id == id)) return NotFound();

        // Resolve the target group: explicit RequestPoGroupId, or the sole active group for
        // backward-compatible single-group callers. A multi-group request MUST name the group so a
        // sibling can never be regressed implicitly.
        var activeGroups = r.PoGroups.Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled).ToList();
        RequestPoGroup? group;
        if (requestDto.RequestPoGroupId.HasValue)
        {
            group = r.PoGroups.FirstOrDefault(g => g.Id == requestDto.RequestPoGroupId.Value);
            if (group == null)
                return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Grupo P.O não encontrado neste pedido.", Status = 400 });
        }
        else if (activeGroups.Count == 1)
        {
            group = activeGroups[0];
        }
        else
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Grupo Obrigatório",
                Detail = "Este pedido possui múltiplos grupos operacionais. Informe o grupo específico a devolver para correção da P.O.",
                Status = 400
            });
        }

        // Source-status guard: group-scoped. Only PO_ISSUED / PAYMENT_SCHEDULED groups may be returned.
        // Returning from PAYMENT_SCHEDULED intentionally invalidates the prior scheduling; after
        // correction Finance re-evaluates from PO_ISSUED.
        var allowedReturnStatuses = new[] { "PO_ISSUED", "PAYMENT_SCHEDULED" };
        if (!_eligibility.CanReturnGroup(group.Status))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Inválida",
                Detail = $"A devolução para correção de P.O só é permitida quando o grupo está nos status: {string.Join(", ", allowedReturnStatuses)}. Status atual do grupo: {group.Status}.",
                Status = 400
            });
        }

        var displaySupplierName = FinanceGroupDisplayResolver.ResolveSupplierName(
            group.SupplierNameSnapshot,
            r.SelectedQuotationId.HasValue,
            r.SelectedQuotationId.HasValue ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.SupplierNameSnapshot : null,
            r.Supplier?.Name);
        var displayCurrencyCode = FinanceGroupDisplayResolver.ResolveCurrencyCode(
            group.CurrencyCode,
            r.SelectedQuotationId.HasValue,
            r.SelectedQuotationId.HasValue ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.Currency : null,
            r.Currency?.Code);

        // Change ONLY the target group; the request scalar is derived by aggregation afterwards
        // (no scalar-only manual override, so siblings are never masked or regressed).
        group.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection;
        group.UpdatedAtUtc = DateTime.UtcNow;
        r.UpdatedAtUtc = DateTime.UtcNow;

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = CurrentUserId,
            ActionTaken = "FINANCE_RETURN_ADJUSTMENT",
            PreviousStatusId = r.StatusId,
            NewStatusId = r.StatusId, // scalar is recomputed by aggregation below, not set here
            Comment = FinanceHistoryCommentFormatter.FormatGroupPrefix(group.ApprovalBatch?.BatchNumber, displaySupplierName, displayCurrencyCode, "Total", group.TotalAmount)
                + $" Grupo devolvido por Finanças para correção da P.O (GroupId: {group.Id}). Motivo: {requestDto.Notes}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        // Derive the request scalar from the current group set — sibling groups are respected.
        await _statusAggregationService.AggregateRequestStatusAsync(id, CurrentUserId);

        // [TEMPORARY NON-CENTRAL HOOK]
        try
        {
            var actor = await _context.Users.FindAsync(CurrentUserId);
            var historyEntry = _context.RequestStatusHistories.Local.OrderByDescending(h => h.CreatedAtUtc).FirstOrDefault(h => h.RequestId == id);
            await _orchestrator.EmitAsync(new WorkflowEvent
            {
                EventCode = WorkflowEventCodes.FinanceReturned,
                RequestId = id,
                RequestNumber = r.RequestNumber ?? "S/N",
                RequestTitle = r.Title ?? "",
                TargetStatusCode = "WAITING_PO_CORRECTION",
                ActionTaken = "FINANCE_RETURN_ADJUSTMENT",
                ActorUserId = CurrentUserId,
                ActorName = actor?.FullName ?? "Sistema",
                Comment = requestDto.Notes,
                CorrelationId = historyEntry?.Id ?? Guid.NewGuid(),
                RequesterId = r.RequesterId,
                BuyerId = r.BuyerId,
                AreaApproverId = r.AreaApproverId,
                FinalApproverId = r.FinalApproverId,
                PlantId = r.PlantId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-critical: notification dispatch failed for ReturnForAdjustment on Request {RequestId}", id);
        }

        return Ok();
    }

    [HttpPost("{id:guid}/cancel-schedule")]
    public async Task<IActionResult> CancelSchedule(Guid id, [FromBody] CancelSchedulePaymentDto requestDto)
    {
        var r = await _context.Requests
            .Include(req => req.Status)
            .Include(req => req.RequestType)
            .Include(req => req.PoGroups)
                .ThenInclude(g => g.ApprovalBatch)
            .Include(req => req.PoGroups)
                .ThenInclude(g => g.Payments)
            .Include(req => req.Attachments)
            .Include(req => req.Supplier)
            .Include(req => req.Currency)
            .Include(req => req.Quotations)
            .FirstOrDefaultAsync(req => req.Id == id);
        if (r == null || !await (await GetScopedRequestsQuery()).AnyAsync(req => req.Id == id)) return NotFound();

        var group = r.PoGroups.FirstOrDefault(g => g.Id == requestDto.RequestPoGroupId);
        if (group == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Grupo P.O Não Encontrado",
                Detail = "Grupo da P.O não encontrado para este pedido.",
                Status = 400
            });
        }

        // ── Status Guard: eligible only for a group whose OWN status is currently scheduled ──
        if (!_eligibility.CanCancelSchedule(group.Status))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Inválida",
                Detail = $"O cancelamento de agendamento só é permitido para grupos nos status: " +
                         $"{RequestConstants.Statuses.PaymentScheduled}, {RequestConstants.Statuses.AdvancePaymentScheduled}. " +
                         $"Status atual do grupo: {group.Status}.",
                Status = 400
            });
        }

        // ── Reason: required, validated after Trim() ──
        var trimmedReason = requestDto.Reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length < 20)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Motivo Obrigatório",
                Detail = "O motivo do cancelamento é obrigatório e deve ter no mínimo 20 caracteres.",
                Status = 400
            });
        }

        var isAdvance = group.Status == RequestConstants.Statuses.AdvancePaymentScheduled;
        var targetStatus = isAdvance ? RequestConstants.Statuses.AdvancePaymentRequired : RequestConstants.Statuses.PoIssued;
        var expectedPaymentType = isAdvance ? RequestPayment.PaymentTypes.Advance : RequestPayment.PaymentTypes.FinalBalance;

        // ── Locate the live Scheduled RequestPayment row for this group/type ──
        var payment = group.Payments
            .Where(p => p.PaymentType == expectedPaymentType && p.PaymentStatus == RequestPayment.PaymentStatuses.Scheduled)
            .OrderByDescending(p => p.PaymentSequence)
            .FirstOrDefault();
        if (payment == null)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Inconsistência de Dados",
                Detail = "O grupo está marcado como agendado, mas nenhum registo de pagamento agendado foi encontrado para este grupo. Contacte o suporte técnico.",
                Status = 409
            });
        }

        var previousScheduledDateUtc = payment.ScheduledDateUtc;

        // ── Mutate the RequestPayment: normal vs. advance diverge here — never identical ──
        if (isAdvance)
        {
            // This row originated as the pre-existing PLANNED advance record created at PO
            // registration (RegisterPo). Returning it to PLANNED (not CANCELLED) — rather than
            // creating a replacement row — lets ScheduleAdvancePayment's own lookup
            // (PaymentType == Advance && PaymentStatus == Planned) find and reschedule the SAME
            // row again, exactly as it would for a group that was never scheduled in the first place.
            payment.PaymentStatus = RequestPayment.PaymentStatuses.Planned;
            payment.ScheduledDateUtc = null;
            payment.ScheduledByUserId = null;
        }
        else
        {
            payment.PaymentStatus = RequestPayment.PaymentStatuses.Cancelled;
        }
        payment.UpdatedByUserId = CurrentUserId;
        payment.UpdatedAtUtc = DateTime.UtcNow;

        // ── Update only the selected RequestPoGroup — siblings are never touched ──
        group.Status = targetStatus;
        group.UpdatedAtUtc = DateTime.UtcNow;

        // ── Void only the single most recent eligible schedule attachment for THIS group ──
        var candidates = r.Attachments
            .Where(a => a.RequestPoGroupId == group.Id
                && a.AttachmentTypeCode == RequestAttachment.TYPE_PAYMENT_SCHEDULE
                && !a.IsDeleted
                && a.VoidedAtUtc == null)
            .OrderByDescending(a => a.UploadedAtUtc)
            .ToList();
        if (candidates.Count > 1)
        {
            _logger.LogWarning(
                "CancelSchedule found {Count} non-voided PAYMENT_SCHEDULE attachments for Request {RequestId} Group {GroupId} — voiding only the most recent, older ones left untouched.",
                candidates.Count, id, group.Id);
        }
        var attachmentToVoid = candidates.FirstOrDefault();
        if (attachmentToVoid != null)
        {
            attachmentToVoid.VoidedAtUtc = DateTime.UtcNow;
            attachmentToVoid.VoidedByUserId = CurrentUserId;
            attachmentToVoid.VoidReason = trimmedReason;
        }

        // ── One RequestStatusHistory event — the original scheduling event is never touched ──
        var actionTaken = isAdvance ? "ADVANCE_PAYMENT_SCHEDULE_CANCELLED" : "PAYMENT_SCHEDULE_CANCELLED";
        var cancelledLabel = isAdvance ? "Agendamento de adiantamento cancelado." : "Agendamento de pagamento cancelado.";
        var scheduledDateLabel = previousScheduledDateUtc.HasValue ? previousScheduledDateUtc.Value.ToString("dd/MM/yyyy") : "desconhecida";
        // Same legacy-snapshot fallback as SchedulePayment — resolve a safe display value for the
        // audit comment only; never written back to the group record. See FinanceGroupDisplayResolver.
        var displaySupplierName = FinanceGroupDisplayResolver.ResolveSupplierName(
            group.SupplierNameSnapshot,
            r.SelectedQuotationId.HasValue,
            r.SelectedQuotationId.HasValue ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.SupplierNameSnapshot : null,
            r.Supplier?.Name);
        var displayCurrencyCode = FinanceGroupDisplayResolver.ResolveCurrencyCode(
            group.CurrencyCode,
            r.SelectedQuotationId.HasValue,
            r.SelectedQuotationId.HasValue ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.Currency : null,
            r.Currency?.Code);
        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = CurrentUserId,
            ActionTaken = actionTaken,
            PreviousStatusId = r.StatusId,
            NewStatusId = r.StatusId,
            Comment = FinanceHistoryCommentFormatter.FormatGroupPrefix(group.ApprovalBatch?.BatchNumber, displaySupplierName, displayCurrencyCode, "Total", group.TotalAmount)
                + $"\n{cancelledLabel}\nData anteriormente agendada: {scheduledDateLabel}.\nMotivo: {trimmedReason}.",
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(history);

        r.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Same pattern as SchedulePayment/MarkAsPaid: aggregation is a separate round-trip after
        // the group mutation is persisted, never assigned inline here.
        await _statusAggregationService.AggregateRequestStatusAsync(id, CurrentUserId);

        try
        {
            var actor = await _context.Users.FindAsync(CurrentUserId);
            await _orchestrator.EmitAsync(new WorkflowEvent
            {
                EventCode = WorkflowEventCodes.PaymentScheduleCancelled,
                RequestId = id,
                RequestNumber = r.RequestNumber ?? "S/N",
                RequestTitle = r.Title ?? "",
                TargetStatusCode = targetStatus,
                ActionTaken = actionTaken,
                ActorUserId = CurrentUserId,
                ActorName = actor?.FullName ?? "Sistema",
                Comment = trimmedReason,
                CorrelationId = history.Id,
                RequesterId = r.RequesterId,
                BuyerId = r.BuyerId,
                AreaApproverId = r.AreaApproverId,
                FinalApproverId = r.FinalApproverId,
                DepartmentId = r.DepartmentId,
                PlantId = r.PlantId,
                CompanyId = r.CompanyId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-critical: notification dispatch failed for CancelSchedule on Request {RequestId}", id);
        }

        return Ok();
    }

    // ─── Contract-Driven Cash Flow Projection (Phase 1) ──────────────────────────

    /// <summary>
    /// Returns aggregated KPI totals and monthly series for contractual payment obligations.
    /// Source: ACTIVE contracts → ContractPaymentObligation. No monetary calculations.
    /// Forecast buckets and risk levels are derived at query time.
    /// </summary>
    [HttpGet("contract-projections/summary")]
    public async Task<ActionResult<ContractProjectionSummaryDto>> GetContractProjectionSummary(
        [FromQuery] int? companyId = null,
        [FromQuery] int? plantId = null,
        [FromQuery] int? departmentId = null)
    {
        var today = DateTime.UtcNow.Date;
        var currentMonthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);
        var next90Days = today.AddDays(90);
        var currentYearStart = new DateTime(today.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Build base query: ACTIVE contracts only
        var baseQuery = _context.ContractPaymentObligations
            .Include(o => o.Contract)
                .ThenInclude(c => c.Supplier)
            .Include(o => o.Contract)
                .ThenInclude(c => c.Company)
            .Include(o => o.Contract)
                .ThenInclude(c => c.Department)
            .Include(o => o.Currency)
            .Where(o =>
                o.Contract.StatusCode == ContractConstants.Statuses.Active &&
                o.StatusCode != ContractConstants.ObligationStatuses.Cancelled);

        if (companyId.HasValue)
            baseQuery = baseQuery.Where(o => o.Contract.CompanyId == companyId.Value);
        if (plantId.HasValue)
            baseQuery = baseQuery.Where(o => o.Contract.PlantId == plantId.Value);
        if (departmentId.HasValue)
            baseQuery = baseQuery.Where(o => o.Contract.DepartmentId == departmentId.Value);

        // Load minimal projection with linked request info
        var obligations = await baseQuery
            .Select(o => new
            {
                ObligationId = o.Id,
                ObligationStatusCode = o.StatusCode,
                Amount = o.ExpectedAmount,
                CurrencyCode = o.Currency != null ? o.Currency.Code : o.Contract.Currency != null ? o.Contract.Currency.Code : "AOA",
                DueDateUtc = o.DueDateUtc,
                GraceDateUtc = o.GraceDateUtc,
                PenaltyStartDateUtc = o.PenaltyStartDateUtc,
                ContractHasLatePenalty = o.Contract.HasLatePenalty,
                // Linked request info (navigated via Requests table FK)
                LinkedRequest = _context.Requests
                    .Where(r => r.ContractPaymentObligationId == o.Id)
                    .OrderByDescending(r => r.RequestedDateUtc)
                    .Select(r => new { r.Status!.Code, r.RequestNumber })
                    .FirstOrDefault()
            })
            .ToListAsync();

        // Derive forecast bucket and risk for each obligation
        var projectionData = obligations.Select(o =>
        {
            DateTime? dueDateNullable = o.DueDateUtc;
            var bucket = DeriveContractForecastBucket(o.ObligationStatusCode, o.LinkedRequest?.Code, dueDateNullable, today);
            var risk = DeriveContractRiskLevel(bucket, dueDateNullable, o.GraceDateUtc, o.PenaltyStartDateUtc, o.ContractHasLatePenalty, today);
            return new
            {
                o.Amount,
                o.CurrencyCode,
                DueDateUtc = dueDateNullable,
                Bucket = bucket,
                Risk = risk
            };
        }).ToList();

        // KPI 1: Projected obligations due this calendar month
        var currentMonth = projectionData
            .Where(p => p.Bucket is "PROJECTED" or "OVERDUE_NO_REQUEST" && p.DueDateUtc.HasValue && p.DueDateUtc.Value >= currentMonthStart && p.DueDateUtc.Value <= currentMonthEnd)
            .GroupBy(p => p.CurrencyCode)
            .Select(g => new ContractProjectionCurrencyTotalDto { CurrencyCode = g.Key, TotalAmount = g.Sum(x => x.Amount) })
            .ToList();

        // KPI 2: PROJECTED + OVERDUE in next 90 days
        var next3Months = projectionData
            .Where(p => p.Bucket is "PROJECTED" or "OVERDUE_NO_REQUEST" && p.DueDateUtc.HasValue && p.DueDateUtc.Value >= today && p.DueDateUtc.Value <= next90Days)
            .GroupBy(p => p.CurrencyCode)
            .Select(g => new ContractProjectionCurrencyTotalDto { CurrencyCode = g.Key, TotalAmount = g.Sum(x => x.Amount) })
            .ToList();

        // KPI 3: Pipeline
        var pipeline = projectionData
            .Where(p => p.Bucket == "PIPELINE")
            .GroupBy(p => p.CurrencyCode)
            .Select(g => new ContractProjectionCurrencyTotalDto { CurrencyCode = g.Key, TotalAmount = g.Sum(x => x.Amount) })
            .ToList();

        // KPI 4: Confirmed (APPROVED + SCHEDULED)
        var confirmed = projectionData
            .Where(p => p.Bucket == "CONFIRMED")
            .GroupBy(p => p.CurrencyCode)
            .Select(g => new ContractProjectionCurrencyTotalDto { CurrencyCode = g.Key, TotalAmount = g.Sum(x => x.Amount) })
            .ToList();

        // KPI 5: Realized this year
        var realized = projectionData
            .Where(p => p.Bucket == "REALIZED")
            .GroupBy(p => p.CurrencyCode)
            .Select(g => new ContractProjectionCurrencyTotalDto { CurrencyCode = g.Key, TotalAmount = g.Sum(x => x.Amount) })
            .ToList();

        // KPI 6: Risk counts
        int overdueNoRequestCount = projectionData.Count(p => p.Bucket == "OVERDUE_NO_REQUEST");
        int penaltyRiskCount = projectionData.Count(p => p.Risk == "HIGH");

        // Monthly series: next 6 months
        var monthlySeries = new List<ContractProjectionMonthlySeriesDto>();
        for (int m = 0; m < 6; m++)
        {
            var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(m);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var yearMonth = monthStart.ToString("yyyy-MM");

            var monthItems = projectionData.Where(p =>
                p.DueDateUtc.HasValue &&
                p.DueDateUtc!.Value >= monthStart &&
                p.DueDateUtc!.Value <= monthEnd &&
                p.Bucket != "REALIZED")
                .ToList();

            var currencies = monthItems.Select(x => x.CurrencyCode).Distinct();
            foreach (var curr in currencies)
            {
                var currItems = monthItems.Where(x => x.CurrencyCode == curr).ToList();
                monthlySeries.Add(new ContractProjectionMonthlySeriesDto
                {
                    YearMonth = yearMonth,
                    CurrencyCode = curr,
                    ProjectedAmount = currItems.Where(x => x.Bucket is "PROJECTED" or "OVERDUE_NO_REQUEST").Sum(x => x.Amount),
                    PipelineAmount = currItems.Where(x => x.Bucket == "PIPELINE").Sum(x => x.Amount),
                    ConfirmedAmount = currItems.Where(x => x.Bucket == "CONFIRMED").Sum(x => x.Amount)
                });
            }
        }

        return Ok(new ContractProjectionSummaryDto
        {
            CurrentMonthByCurrency = currentMonth,
            NextThreeMonthsByCurrency = next3Months,
            PipelineByCurrency = pipeline,
            ConfirmedByCurrency = confirmed,
            RealizedByCurrency = realized,
            OverdueNoRequestCount = overdueNoRequestCount,
            PenaltyRiskCount = penaltyRiskCount,
            MonthlySeries = monthlySeries
        });
    }

    /// <summary>
    /// Returns a paged list of contractual projection items for the detail table.
    /// Source: ACTIVE contracts → ContractPaymentObligation.
    /// </summary>
    [HttpGet("contract-projections")]
    public async Task<ActionResult<ContractProjectionPagedResultDto>> GetContractProjections(
        [FromQuery] int? companyId = null,
        [FromQuery] int? plantId = null,
        [FromQuery] int? departmentId = null,
        [FromQuery] string? bucket = null,
        [FromQuery] bool onlyAtRisk = false,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var today = DateTime.UtcNow.Date;

        var baseQuery = _context.ContractPaymentObligations
            .Include(o => o.Contract)
                .ThenInclude(c => c.Supplier)
            .Include(o => o.Contract)
                .ThenInclude(c => c.Company)
            .Include(o => o.Contract)
                .ThenInclude(c => c.Department)
            .Include(o => o.Currency)
            .Where(o =>
                o.Contract.StatusCode == ContractConstants.Statuses.Active &&
                o.StatusCode != ContractConstants.ObligationStatuses.Cancelled);

        if (companyId.HasValue)
            baseQuery = baseQuery.Where(o => o.Contract.CompanyId == companyId.Value);
        if (plantId.HasValue)
            baseQuery = baseQuery.Where(o => o.Contract.PlantId == plantId.Value);
        if (departmentId.HasValue)
            baseQuery = baseQuery.Where(o => o.Contract.DepartmentId == departmentId.Value);
        if (dateFrom.HasValue)
            baseQuery = baseQuery.Where(o => o.DueDateUtc >= dateFrom.Value);
        if (dateTo.HasValue)
            baseQuery = baseQuery.Where(o => o.DueDateUtc <= dateTo.Value);

        var raw = await baseQuery
            .OrderBy(o => o.DueDateUtc)
            .Select(o => new
            {
                ObligationId = o.Id,
                ContractId = o.ContractId,
                ContractNumber = o.Contract.ContractNumber,
                ContractTitle = o.Contract.Title,
                SupplierName = o.Contract.Supplier != null ? o.Contract.Supplier.Name : o.Contract.CounterpartyName ?? "---",
                CompanyName = o.Contract.Company.Name,
                DepartmentName = o.Contract.Department != null ? o.Contract.Department.Name : (string?)null,
                DepartmentId = (int?)o.Contract.DepartmentId,
                ObligationLabel = o.Description,
                Amount = o.ExpectedAmount,
                CurrencyCode = o.Currency != null ? o.Currency.Code : o.Contract.Currency != null ? o.Contract.Currency.Code : "AOA",
                DueDateUtc = (DateTime?)o.DueDateUtc,
                GraceDateUtc = o.GraceDateUtc,
                PenaltyStartDateUtc = o.PenaltyStartDateUtc,
                ObligationStatusCode = o.StatusCode,
                ContractHasLatePenalty = o.Contract.HasLatePenalty,
                LinkedRequest = _context.Requests
                    .Where(r => r.ContractPaymentObligationId == o.Id)
                    .OrderByDescending(r => r.RequestedDateUtc)
                    .Select(r => new { StatusCode = r.Status!.Code, r.RequestNumber })
                    .FirstOrDefault()
            })
            .ToListAsync();

        // Derive and filter in memory
        var items = raw
            .Select(o =>
            {
                var forecastBucket = DeriveContractForecastBucket(o.ObligationStatusCode, o.LinkedRequest?.StatusCode, o.DueDateUtc, today);
                var riskLevel = DeriveContractRiskLevel(forecastBucket, o.DueDateUtc, o.GraceDateUtc, o.PenaltyStartDateUtc, o.ContractHasLatePenalty, today);
                return new ContractProjectionItemDto
                {
                    ObligationId = o.ObligationId.ToString(),
                    ContractId = o.ContractId.ToString(),
                    ContractNumber = o.ContractNumber,
                    ContractTitle = o.ContractTitle,
                    SupplierName = o.SupplierName,
                    CompanyName = o.CompanyName,
                    DepartmentName = o.DepartmentName,
                    DepartmentId = o.DepartmentId,
                    ObligationLabel = o.ObligationLabel,
                    Amount = o.Amount,
                    CurrencyCode = o.CurrencyCode,
                    DueDateUtc = o.DueDateUtc,
                    GraceDateUtc = o.GraceDateUtc,
                    PenaltyStartDateUtc = o.PenaltyStartDateUtc,
                    ForecastBucket = forecastBucket,
                    RiskLevelCode = riskLevel,
                    LinkedRequestNumber = o.LinkedRequest?.RequestNumber,
                    LinkedRequestStatus = o.LinkedRequest?.StatusCode
                };
            })
            .Where(item => bucket == null || item.ForecastBucket == bucket)
            .Where(item => !onlyAtRisk || item.RiskLevelCode is "HIGH" or "MEDIUM")
            .ToList();

        var totalCount = items.Count;
        var paged = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new ContractProjectionPagedResultDto
        {
            Items = paged,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    // ─── Private helpers ─────────────────────────────────────────────────────────

    private static string DeriveContractForecastBucket(
        string obligationStatusCode,
        string? linkedRequestStatusCode,
        DateTime? dueDateUtc,
        DateTime today)
    {
        if (obligationStatusCode == ContractConstants.ObligationStatuses.Paid)
            return "REALIZED";

        if (obligationStatusCode == ContractConstants.ObligationStatuses.RequestCreated && linkedRequestStatusCode != null)
        {
            // Terminal / cancelled request → revert to PROJECTED
            if (linkedRequestStatusCode is RequestConstants.Statuses.Cancelled or RequestConstants.Statuses.Rejected or RequestConstants.Statuses.AreaAdjustment or RequestConstants.Statuses.FinalAdjustment)
                return "PROJECTED";

            // Realized
            if (linkedRequestStatusCode is RequestConstants.Statuses.Paid or RequestConstants.Statuses.PaymentCompleted or RequestConstants.Statuses.InFollowup)
                return "REALIZED";

            // Confirmed
            if (linkedRequestStatusCode is RequestConstants.Statuses.FinalApproved or RequestConstants.Statuses.PoIssued or RequestConstants.Statuses.PaymentRequestSent or RequestConstants.Statuses.PaymentScheduled
                or RequestConstants.Statuses.AdvancePaymentRequired or RequestConstants.Statuses.AdvancePaymentCompleted or RequestConstants.Statuses.WaitingSupplierDelivery or RequestConstants.Statuses.WaitingReconciliation)
                return "CONFIRMED";

            // Pipeline — all other in-flight statuses
            return "PIPELINE";
        }

        // No request: PENDING obligation
        if (dueDateUtc.HasValue && dueDateUtc.Value.Date < today)
            return "OVERDUE_NO_REQUEST";

        return "PROJECTED";
    }

    private static string DeriveContractRiskLevel(
        string bucket,
        DateTime? dueDateUtc,
        DateTime? graceDateUtc,
        DateTime? penaltyStartDateUtc,
        bool hasLatePenalty,
        DateTime today)
    {
        // Already paid or confirmed — no risk signal needed
        if (bucket is "REALIZED" or "CONFIRMED" or "PIPELINE")
            return "LOW";

        // Penalty already accruing
        if (hasLatePenalty && penaltyStartDateUtc.HasValue && penaltyStartDateUtc.Value.Date <= today)
            return "HIGH";

        // Overdue with no request
        if (bucket == "OVERDUE_NO_REQUEST")
            return "HIGH";

        // In grace period (overdue but not yet penalizing)
        if (graceDateUtc.HasValue && dueDateUtc.HasValue && dueDateUtc.Value.Date < today && today <= graceDateUtc.Value.Date)
            return "MEDIUM";

        // Close to due date (within 7 days)
        if (dueDateUtc.HasValue && dueDateUtc.Value.Date > today && (dueDateUtc.Value.Date - today).TotalDays <= 7)
            return "MEDIUM";

        return "LOW";
    }

    /// <summary>
    /// Shared shape for Finance Summary items (PAYMENT request-level and QUOTATION group-level).
    /// Allows merging both streams into a single list for metric computation.
    /// </summary>
    private record SummaryItem
    {
        public string StatusCode { get; init; } = string.Empty;
        public DateTime? NeedByDateUtc { get; init; }
        public DateTime? ScheduledDateUtc { get; init; }
        public DateTime RequestedDateUtc { get; init; }
        public string RequestTypeCode { get; init; } = string.Empty;
        public string CurrencyCode { get; init; } = "---";
        public string SupplierName { get; init; } = "---";
        public bool HasProforma { get; init; }
        public bool HasPO { get; init; }
        public bool HasProof { get; init; }
        public bool IsPaid { get; init; }
        public DateTime? PaidAtUtc { get; init; }
        public decimal Amount { get; init; }
    }
}
