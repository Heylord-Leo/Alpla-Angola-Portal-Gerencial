namespace AlplaPortal.Api.Controllers;

using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Application.DTOs.Common;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;

[Authorize]
[ApiController]
[Route("api/v1/finance")]
public class FinanceController : BaseController
{
    private readonly IWorkflowNotificationOrchestrator _orchestrator;
    private readonly ILogger<FinanceController> _logger;

    public FinanceController(
        ApplicationDbContext context,
        IWorkflowNotificationOrchestrator orchestrator,
        ILogger<FinanceController> logger) : base(context)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

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
        
        // We only care about requests that have reached the PO_ISSUED state or beyond
        var financeStatuses = new[] 
        { 
            RequestConstants.Statuses.PoIssued, 
            RequestConstants.Statuses.PaymentRequestSent, 
            RequestConstants.Statuses.PaymentScheduled, 
            RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.InFollowup
        };

        var query = scopedQuery.Where(r => 
            (financeStatuses.Contains(r.Status!.Code) || (r.RequestType!.Code == RequestConstants.Types.Payment && r.Status!.Code == RequestConstants.Statuses.FinalApproved))
            && r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PurchaseOrder)
        );
        
        var today = DateTime.UtcNow.Date;
        var in4Days = today.AddDays(4);
        var firstDayOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var stats = await query
            .Include(r => r.Quotations) // For amounts
            .Include(r => r.Currency)
            .Include(r => r.Attachments)
            .Select(r => new
            {
                Id = r.Id,
                StatusCode = r.Status!.Code,
                NeedByDateUtc = r.NeedByDateUtc,
                ScheduledDateUtc = r.ScheduledDateUtc,
                RequestedDateUtc = r.RequestedDateUtc,
                RequestTypeCode = r.RequestType!.Code,
                Amount = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)!.TotalAmount
                    : r.EstimatedTotalAmount,
                CurrencyCode = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)!.Currency
                    : r.Currency != null ? r.Currency.Code : "---",
                SupplierName = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)!.SupplierNameSnapshot
                    : r.Supplier != null ? r.Supplier.Name : "---",
                CompletedAtUtc = r.StatusHistories
                    .Where(sh => sh.NewStatus!.Code == RequestConstants.Statuses.Paid || sh.NewStatus!.Code == RequestConstants.Statuses.PaymentCompleted)
                    .OrderByDescending(sh => sh.CreatedAtUtc)
                    .Select(sh => (DateTime?)sh.CreatedAtUtc)
                    .FirstOrDefault(),
                HasProforma = r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.Proforma),
                HasPO = r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PurchaseOrder),
                HasProof = r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PaymentProof)
            })
            .ToListAsync();

        var waitingActions = new[] { RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentRequestSent };
        int waitingFinance = stats.Count(s => waitingActions.Contains(s.StatusCode) || (s.RequestTypeCode == RequestConstants.Types.Payment && s.StatusCode == RequestConstants.Statuses.FinalApproved));
        
        // Refine waiting finance (needs payment scheduling/execution)
        var scheduled = stats.Count(s => s.StatusCode == RequestConstants.Statuses.PaymentScheduled);
        
        // Active and overdue
        var activeOverdue = stats.Count(s => 
            !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(s.StatusCode) 
            && s.NeedByDateUtc.HasValue && s.NeedByDateUtc.Value < today);

        // Completed this month
        var completedThisMonth = stats.Count(s => 
            new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(s.StatusCode) 
            && s.CompletedAtUtc >= firstDayOfMonth);

        var pendingList = stats.Where(s => !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(s.StatusCode)).ToList();
        var pendingValue = pendingList.Sum(s => s.Amount); // Aggregation
        var currencyCodes = pendingList.Select(s => s.CurrencyCode).Distinct().ToList();

        var paidList = stats.Where(s => new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(s.StatusCode) && s.CompletedAtUtc >= firstDayOfMonth).ToList();
        var paidValue = paidList.Sum(s => s.Amount);

        // Missing Documents
        int missingDocs = stats.Count(s => 
            !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(s.StatusCode) &&
            ((s.RequestTypeCode == RequestConstants.Types.Quotation && !s.HasProforma) ||
            (!s.HasPO))
        );

        var dueSoon = stats.Count(s => 
            !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(s.StatusCode) 
            && s.NeedByDateUtc.HasValue && s.NeedByDateUtc.Value >= today && s.NeedByDateUtc.Value <= in4Days);

        var attentionPoints = new List<FinanceAttentionPointDto>();
        
        if (activeOverdue > 0)
        {
            attentionPoints.Add(new FinanceAttentionPointDto
            {
                Id = "overdue", Title = "Pagamentos Vencidos", Description = "Requer ação imediata de tesouraria.", Count = activeOverdue, TargetPath = "/finance/payments?filter=overdue", Type = "DANGER"
            });
        }
        if (dueSoon > 0)
        {
            attentionPoints.Add(new FinanceAttentionPointDto
            {
                Id = "due-soon", Title = "Vencendo em breve", Description = "Pagamentos vencendo nos próximos 4 dias.", Count = dueSoon, TargetPath = "/finance/payments?filter=dueSoon", Type = "WARNING"
            });
        }
        if (missingDocs > 0)
        {
            attentionPoints.Add(new FinanceAttentionPointDto
            {
                Id = "missing-docs", Title = "Falta Documento", Description = "Pedidos sem Proforma ou P.O pendentes de ação.", Count = missingDocs, TargetPath = "/finance/payments?filter=missingDocs", Type = "INFO"
            });
        }

        var scheduledValue = stats.Where(s => s.StatusCode == RequestConstants.Statuses.PaymentScheduled).Sum(s => s.Amount);
        var overdueValue = stats.Where(s => !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(s.StatusCode) && s.NeedByDateUtc.HasValue && s.NeedByDateUtc.Value < today).Sum(s => s.Amount);

        // --- NEW DATA SCIENCE METRICS ---

        // 1. Projeção de Fluxo de Caixa (Próximos 15 dias)
        var maxProjectionDate = today.AddDays(15);
        var uncompletedRequests = stats.Where(s => !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(s.StatusCode)).ToList();
        
        var projections = uncompletedRequests
            .Where(s => (s.ScheduledDateUtc ?? s.NeedByDateUtc) >= today && (s.ScheduledDateUtc ?? s.NeedByDateUtc) <= maxProjectionDate)
            .GroupBy(s => new { Date = (s.ScheduledDateUtc ?? s.NeedByDateUtc).Value.Date, Currency = s.CurrencyCode })
            .Select(g => new FinanceCashFlowProjectionDto
            {
                Date = g.Key.Date.ToString("yyyy-MM-dd"),
                CurrencyCode = g.Key.Currency,
                TotalAmount = g.Sum(x => x.Amount)
            })
            .OrderBy(p => p.Date)
            .ToList();

        // 2. Exposição Cambial
        var currencyExposures = uncompletedRequests
            .GroupBy(s => s.CurrencyCode)
            .Select(g => new FinanceCurrencyExposureDto
            {
                CurrencyCode = g.Key ?? "N/A",
                Amount = g.Sum(x => x.Amount),
                Count = g.Count()
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        // 3. Top 5 Fornecedores (Concentração financeira pendente)
        var topSuppliers = uncompletedRequests
            .GroupBy(s => new { s.SupplierName, s.CurrencyCode })
            .Select(g => new FinanceTopSupplierDto
            {
                SupplierName = string.IsNullOrWhiteSpace(g.Key.SupplierName) ? "Fornecedor Não Declarado" : g.Key.SupplierName,
                CurrencyCode = g.Key.CurrencyCode ?? "---",
                TotalPendingAmount = g.Sum(x => x.Amount),
                RequestCount = g.Count()
            })
            .OrderByDescending(t => t.TotalPendingAmount)
            .Take(5)
            .ToList();

        // 4. Aging Operational (Idade dos processos esperando finanças)
        var waitingFinanceAging = uncompletedRequests.Where(s => waitingActions.Contains(s.StatusCode) || (s.RequestTypeCode == RequestConstants.Types.Payment && s.StatusCode == RequestConstants.Statuses.FinalApproved)).ToList();
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
            ScheduledPayments = scheduled,
            OverduePayments = activeOverdue,
            CompletedThisMonth = completedThisMonth,
            PendingValue = pendingValue,
            ScheduledValue = scheduledValue,
            OverdueValue = overdueValue,
            PaidThisMonthValue = paidValue,
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
            RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup
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
                Amount = r.SelectedQuotationId.HasValue 
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
            RequestConstants.Statuses.PaymentScheduled, 
            RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.InFollowup
        };

        var query = scopedQuery.Where(r => 
            (financeStatuses.Contains(r.Status!.Code) || (r.RequestType!.Code == RequestConstants.Types.Payment && r.Status!.Code == RequestConstants.Statuses.FinalApproved))
            && r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PurchaseOrder)
        );

        if (!string.IsNullOrWhiteSpace(statusIds))
        {
            var parsedStatusIds = statusIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (parsedStatusIds.Any()) query = query.Where(r => parsedStatusIds.Contains(r.StatusId));
        }

        switch(filter)
        {
            case "overdue":
                query = query.Where(r => !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(r.Status!.Code) && r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < today);
                break;
            case "dueSoon":
                query = query.Where(r => !new[] { RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted }.Contains(r.Status!.Code) && r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= today && r.NeedByDateUtc.Value <= in4Days);
                break;
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.Supplier)
            .Include(r => r.Requester)
            .Include(r => r.Plant)
            .Include(r => r.Quotations)
            .Include(r => r.Currency)
            .OrderByDescending(r => r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < today ? 1 : 0) // Overdue first
            .ThenByDescending(r => r.NeedLevelId ?? 0)
            .ThenBy(r => r.NeedByDateUtc)
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
            var isPaid = r.Status!.Code == RequestConstants.Statuses.Paid || r.Status.Code == RequestConstants.Statuses.PaymentCompleted;
            var isQuotation = r.RequestType!.Code == RequestConstants.Types.Quotation;
            
            var missingDocs = new List<string>();
            if (isQuotation && !item.HasProforma) missingDocs.Add("PROFORMA");
            if (!item.HasPO) missingDocs.Add("PO");
            if (isPaid && !item.HasProof) missingDocs.Add("PAYMENT_PROOF");

            bool isMissingDocuments = (filter == "missingDocs") ? missingDocs.Count > 0 : missingDocs.Count > 0;

            if (filter == "missingDocs" && !isMissingDocuments) continue; // Manual filter application for missing docs

            var actions = new List<string>();
            if (!isPaid)
            {
                if (r.Status.Code != RequestConstants.Statuses.PaymentScheduled) actions.Add("SCHEDULE");
                actions.Add("PAY");
                actions.Add("RETURN");
            }
            actions.Add("ADD_NOTE");
            if (!item.HasProof && (actions.Contains("PAY") || isPaid)) actions.Add("ADD_PROOF");

            dtoList.Add(new FinanceListItemDto
            {
                Id = r.Id,
                RequestNumber = r.RequestNumber,
                Title = r.Title,
                SupplierName = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.SupplierNameSnapshot ?? "---"
                    : r.Supplier != null ? r.Supplier.Name : "---",
                RequesterName = r.Requester!.FullName ?? "---",
                PlantName = r.Plant != null ? r.Plant.Name : "---",
                Amount = r.SelectedQuotationId.HasValue 
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
                AvailableFinanceActions = actions
            });
        }

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

    [HttpGet("history")]
    public async Task<ActionResult<PagedResult<FinanceHistoryItemDto>>> GetHistory(
        [FromQuery] string? search = null,
        [FromQuery] string? actionType = null,
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 20)
    {
        var scopedQuery = await GetScopedRequestsQuery();
        var requestIds = await scopedQuery.Select(r => r.Id).ToListAsync();

        var financeActionCodes = new[] { "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "DOCUMENTO ADICIONADO", "NOTA_FINANCEIRA", "FINANCE_RETURN_ADJUSTMENT" };
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
            query = query.Where(sh => sh.ActionTaken == actionType);
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
        var items = await query
            .OrderByDescending(sh => sh.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(sh => new FinanceHistoryItemDto
            {
                Id = sh.Id,
                RequestId = sh.RequestId,
                RequestNumber = sh.Request!.RequestNumber ?? "---",
                RequestTitle = sh.Request.Title ?? "---",
                Amount = sh.Request.SelectedQuotationId.HasValue 
                    ? sh.Request.Quotations.FirstOrDefault(q => q.Id == sh.Request.SelectedQuotationId.Value)!.TotalAmount 
                    : sh.Request.EstimatedTotalAmount,
                CurrencyCode = sh.Request.SelectedQuotationId.HasValue 
                    ? sh.Request.Quotations.FirstOrDefault(q => q.Id == sh.Request.SelectedQuotationId.Value)!.Currency 
                    : (sh.Request.Currency != null ? sh.Request.Currency.Code : null),
                ActionTaken = sh.ActionTaken,
                Comment = sh.Comment,
                CreatedAtUtc = sh.CreatedAtUtc,
                ActorName = sh.ActorUser!.FullName ?? "Unknown",
                NewStatusCode = sh.NewStatus!.Code,
                NewStatusName = sh.NewStatus.Name
            })
            .ToListAsync();

        return Ok(new PagedResult<FinanceHistoryItemDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("history/export")]
    public async Task<IActionResult> ExportHistory(
        [FromQuery] string? search = null,
        [FromQuery] string? actionType = null)
    {
        var scopedQuery = await GetScopedRequestsQuery();
        var requestIds = await scopedQuery.Select(r => r.Id).ToListAsync();

        var financeActionCodes = new[] { "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "DOCUMENTO ADICIONADO", "NOTA_FINANCEIRA", "FINANCE_RETURN_ADJUSTMENT" };
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

        if (!string.IsNullOrWhiteSpace(actionType)) query = query.Where(sh => sh.ActionTaken == actionType);
        
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

        var items = await query
            .OrderByDescending(sh => sh.CreatedAtUtc)
            .Take(5000)
            .Select(sh => new FinanceHistoryItemDto
            {
                Id = sh.Id,
                RequestId = sh.RequestId,
                RequestNumber = sh.Request!.RequestNumber ?? "---",
                RequestTitle = sh.Request.Title ?? "---",
                Amount = sh.Request.SelectedQuotationId.HasValue 
                    ? sh.Request.Quotations.FirstOrDefault(q => q.Id == sh.Request.SelectedQuotationId.Value)!.TotalAmount 
                    : sh.Request.EstimatedTotalAmount,
                CurrencyCode = sh.Request.SelectedQuotationId.HasValue 
                    ? sh.Request.Quotations.FirstOrDefault(q => q.Id == sh.Request.SelectedQuotationId.Value)!.Currency 
                    : (sh.Request.Currency != null ? sh.Request.Currency.Code : null),
                ActionTaken = sh.ActionTaken,
                Comment = sh.Comment,
                CreatedAtUtc = sh.CreatedAtUtc,
                ActorName = sh.ActorUser!.FullName ?? "Unknown",
                NewStatusCode = sh.NewStatus!.Code,
                NewStatusName = sh.NewStatus.Name
            })
            .ToListAsync();

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Data/Hora;Acao;Responsavel;Ref. Pedido;Titulo;Moeda;Montante;Detalhes");

        foreach (var item in items)
        {
            var comment = item.Comment?.Replace(";", ",").Replace("\r", "").Replace("\n", " ") ?? "";
            var actionStr = item.ActionTaken switch {
                "PAYMENT_SCHEDULED" => "Agendado",
                "PAYMENT_COMPLETED" => "Pago",
                "DOCUMENTO ADICIONADO" => "Comprovativo",
                "NOTA_FINANCEIRA" => "Observação",
                "FINANCE_RETURN_ADJUSTMENT" => "Ajuste",
                _ => item.ActionTaken
            };
            csv.AppendLine($"{item.CreatedAtUtc:yyyy-MM-dd HH:mm:ss};{actionStr};{item.ActorName};{item.RequestNumber};{item.RequestTitle};{item.CurrencyCode};{item.Amount};{comment}");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv", $"auditoria_financas_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    [HttpPost("{id:guid}/schedule")]
    public async Task<IActionResult> SchedulePayment(Guid id, [FromBody] FinanceActionRequestDto requestDto)
    {
        var r = await _context.Requests.Include(req => req.Status).FirstOrDefaultAsync(req => req.Id == id);
        if (r == null || !await (await GetScopedRequestsQuery()).AnyAsync(req => req.Id == id)) return NotFound();

        var scheduledStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.PaymentScheduled);
        if (scheduledStatus == null) return BadRequest("Status SCHEDULED não configurado.");

        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = CurrentUserId,
            ActionTaken = "PAYMENT_SCHEDULED",
            PreviousStatusId = r.StatusId,
            NewStatusId = scheduledStatus.Id,
            Comment = $"Pagamento agendado. " + (requestDto.Notes ?? ""),
            CreatedAtUtc = DateTime.UtcNow
        };

        r.StatusId = scheduledStatus.Id;
        r.ScheduledDateUtc = requestDto.ActionDateUtc;
        r.UpdatedAtUtc = DateTime.UtcNow;

        _context.RequestStatusHistories.Add(history);
        await _context.SaveChangesAsync();

        // [TEMPORARY NON-CENTRAL HOOK] FinanceController has its own inline status transitions.
        // This is a temporary architecture exception — see DEC-XXX for future consolidation.
        try
        {
            var actor = await _context.Users.FindAsync(CurrentUserId);
            await _orchestrator.EmitAsync(new WorkflowEvent
            {
                EventCode = WorkflowEventCodes.PaymentScheduled,
                RequestId = id,
                RequestNumber = r.RequestNumber ?? "S/N",
                RequestTitle = r.Title ?? "",
                TargetStatusCode = "PAYMENT_SCHEDULED",
                ActionTaken = "PAYMENT_SCHEDULED",
                ActorUserId = CurrentUserId,
                ActorName = actor?.FullName ?? "Sistema",
                Comment = requestDto.Notes,
                CorrelationId = history.Id,
                RequesterId = r.RequesterId,
                BuyerId = r.BuyerId,
                AreaApproverId = r.AreaApproverId,
                FinalApproverId = r.FinalApproverId,
                PlantId = r.PlantId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-critical: notification dispatch failed for SchedulePayment on Request {RequestId}", id);
        }

        return Ok();
    }

    [HttpPost("{id:guid}/pay")]
    public async Task<IActionResult> MarkAsPaid(Guid id, [FromBody] FinanceActionRequestDto requestDto)
    {
        var r = await _context.Requests.Include(req => req.Status).FirstOrDefaultAsync(req => req.Id == id);
        if (r == null || !await (await GetScopedRequestsQuery()).AnyAsync(req => req.Id == id)) return NotFound();

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
            Comment = $"Pagamento realizado na totalidade. " + (requestDto.Notes ?? ""),
            CreatedAtUtc = DateTime.UtcNow
        };

        r.StatusId = paidStatus.Id;
        r.UpdatedAtUtc = DateTime.UtcNow;

        _context.RequestStatusHistories.Add(history);
        await _context.SaveChangesAsync();

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
                Comment = requestDto.Notes,
                CorrelationId = history.Id,
                RequesterId = r.RequesterId,
                BuyerId = r.BuyerId,
                AreaApproverId = r.AreaApproverId,
                FinalApproverId = r.FinalApproverId,
                PlantId = r.PlantId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-critical: notification dispatch failed for MarkAsPaid on Request {RequestId}", id);
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
        var r = await _context.Requests.Include(req => req.Status).FirstOrDefaultAsync(req => req.Id == id);
        if (r == null || !await (await GetScopedRequestsQuery()).AnyAsync(req => req.Id == id)) return NotFound();

        // Source-status guard: only allow return from operational statuses where PO has been registered
        // Note: Returning from PAYMENT_SCHEDULED intentionally invalidates the prior scheduling.
        //       After correction, Finance must re-evaluate from PO_ISSUED.
        var allowedReturnStatuses = new[] { "PO_ISSUED", "PAYMENT_SCHEDULED" };
        if (r.Status == null || !allowedReturnStatuses.Contains(r.Status.Code))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Inválida",
                Detail = $"A devolução para correção de P.O só é permitida quando o pedido está nos status: {string.Join(", ", allowedReturnStatuses)}. Status atual: {r.Status?.Code ?? "desconhecido"}.",
                Status = 400
            });
        }

        var returnStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.WaitingPoCorrection);
        if (returnStatus == null) return StatusCode(500, "Status de destino não configurado no sistema.");

        var oldStatus = r.Status;
        r.StatusId = returnStatus.Id;
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            ActorUserId = CurrentUserId,
            ActionTaken = "FINANCE_RETURN_ADJUSTMENT",
            PreviousStatusId = oldStatus?.Id,
            NewStatusId = returnStatus.Id,
            Comment = $"Devolvido por Finanças para ajuste: {requestDto.Notes}",
            CreatedAtUtc = DateTime.UtcNow
        });

        r.StatusId = returnStatus.Id;
        r.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

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
}
