namespace AlplaPortal.Api.Controllers;

using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Application.DTOs.Common;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Constants;
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
    private readonly INotificationService _notificationService;

    public FinanceController(ApplicationDbContext context, INotificationService notificationService) : base(context)
    {
        _notificationService = notificationService;
    }

    private IQueryable<Request> GetFinanceQuery()
    {
        // Scope restrictions can be applied here based on company/plant
        // The BaseController.GetScopedRequestsQuery() already aligns with the user's role and scopes (Plant/Dept limits)
        // Since FINANCE uses this controller, we just use the base scoped query.
        return _context.Requests.AsQueryable(); // Note: we'll call GetScopedRequestsQuery() inside the endpoints
    }

    [HttpGet("summary")]
    public async Task<ActionResult<FinanceSummaryDto>> GetSummary()
    {
        // Finance is restricted to their scoped plants
        var scopedQuery = await GetScopedRequestsQuery();
        
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
                RequestTypeCode = r.RequestType!.Code,
                Amount = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)!.TotalAmount
                    : r.EstimatedTotalAmount,
                CurrencyCode = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)!.Currency
                    : r.Currency != null ? r.Currency.Code : "---",
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
            AttentionPoints = attentionPoints
        });
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
    public async Task<ActionResult<PagedResult<FinanceHistoryItemDto>>> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var scopedQuery = await GetScopedRequestsQuery();
        var requestIds = await scopedQuery.Select(r => r.Id).ToListAsync();

        var financeActionCodes = new[] { "PAYMENT_SCHEDULED", "PAYMENT_COMPLETED", "DOCUMENTO ADICIONADO", "NOTA_FINANCEIRA", "FINANCE_RETURN_ADJUSTMENT" };
        var financeStatusCodes = new[] { RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.Paid, RequestConstants.Statuses.PaymentCompleted };

        // Query status histories for finance related transitions or generic notes from finance users
        var query = _context.RequestStatusHistories
            .Include(sh => sh.NewStatus)
            .Include(sh => sh.ActorUser)
            .Where(sh => requestIds.Contains(sh.RequestId) && 
                (financeStatusCodes.Contains(sh.NewStatus!.Code) || financeActionCodes.Contains(sh.ActionTaken)));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(sh => sh.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(sh => new FinanceHistoryItemDto
            {
                Id = sh.Id,
                RequestId = sh.RequestId,
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
        return Ok();
    }
}
