namespace AlplaPortal.Api.Controllers;

using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.DTOs.Common;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Api.Helpers;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlplaPortal.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;


[Authorize]
[ApiController]
[Route("api/v1/requests")]
public class RequestsController : BaseController
{
    private readonly IDocumentExtractionService _extractionService;
    private readonly AdminLogWriter _adminLog;
    private readonly ILogger<RequestsController> _logger;

    public RequestsController(
        ApplicationDbContext context, 
        IDocumentExtractionService extractionService, 
        AdminLogWriter adminLog,
        ILogger<RequestsController> logger) : base(context)
    {
        _extractionService = extractionService;
        _adminLog = adminLog;
        _logger = logger;
    }


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
                InAdjustment = g.Count(r => r.Status!.Code == RequestConstants.Statuses.AreaAdjustment || r.Status!.Code == RequestConstants.Statuses.FinalAdjustment),
                InAttention = g.Count(r => !terminalStates.Contains(r.Status!.Code) && r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < in4Days)
            })
            .FirstOrDefaultAsync();

        return Ok(new DashboardSummaryDto
        {
            TotalRequests = stats?.Total ?? 0,
            WaitingQuotation = stats?.WaitingQuotation ?? 0,
            WaitingAreaApproval = stats?.WaitingAreaApproval ?? 0,
            WaitingFinalApproval = stats?.WaitingFinalApproval ?? 0,
            InAdjustment = stats?.InAdjustment ?? 0,
            InAttention = stats?.InAttention ?? 0
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


    [HttpGet]
    public async Task<ActionResult<RequestListResponseDto>> GetRequests(
        [FromQuery] string? search = null, 
        [FromQuery] string? statusIds = null,
        [FromQuery] string? typeIds = null,
        [FromQuery] string? plantIds = null,
        [FromQuery] string? companyIds = null,
        [FromQuery] string? departmentIds = null,
        [FromQuery] bool? isAttention = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = await GetScopedRequestsQuery();
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var in4Days = today.AddDays(4);

        // 1. Apply Base Filters (those that affect KPI counts too)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim().ToLower();
            query = query.Where(r => (r.RequestNumber != null && r.RequestNumber.ToLower().Contains(searchTerm)) || 
                                     r.Title.ToLower().Contains(searchTerm));
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

        // 2. Calculate Dashboard Summary (Aware of base filters, but before status filters)
        var counts = await query
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                WaitingQuotation = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingQuotation && r.RequestType!.Code == RequestConstants.Types.Quotation),
                AwaitingApproval = g.Count(r => r.Status!.Code == RequestConstants.Statuses.WaitingAreaApproval || r.Status!.Code == RequestConstants.Statuses.WaitingFinalApproval || r.Status!.Code == RequestConstants.Statuses.WaitingCostCenter),
                AwaitingPayment = g.Count(r => r.Status!.Code == RequestConstants.Statuses.PoIssued || r.Status!.Code == RequestConstants.Statuses.PaymentRequestSent || r.Status!.Code == RequestConstants.Statuses.PaymentScheduled),
                Completed = g.Count(r => r.Status!.Code == "COMPLETED") // COMPLETED status not yet in constants
            })
            .FirstOrDefaultAsync();

        var summary = new DashboardSummaryDto
        {
            TotalRequests = counts?.Total ?? 0,
            WaitingQuotation = counts?.WaitingQuotation ?? 0,
            AwaitingApproval = counts?.AwaitingApproval ?? 0,
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
            var terminalStates = new[] { "APPROVED", "REJECTED", "CANCELLED", "COMPLETED", "QUOTATION_COMPLETED" };
            query = query.Where(r => 
                !terminalStates.Contains(r.Status!.Code) && 
                r.NeedByDateUtc.HasValue && 
                r.NeedByDateUtc.Value < in4Days);
        }

        // 4. Final Projection and Pagination
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(r =>
                // Finalized statuses always rank last (-1), below all active items including those with no deadline (0).
                // APPROVED is intentionally excluded here: it is an active operational status (Buyer registers P.O).
                (r.Status!.Code == "REJECTED" || r.Status.Code == "CANCELLED" ||
                 r.Status.Code == "COMPLETED" || r.Status.Code == "QUOTATION_COMPLETED")
                    ? -1                                                                                        // finalized — always last
                    : (r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value < today) ? 3                          // overdue
                    : (r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= today && r.NeedByDateUtc.Value < tomorrow) ? 2  // due today
                    : (r.NeedByDateUtc.HasValue && r.NeedByDateUtc.Value >= tomorrow && r.NeedByDateUtc.Value < in4Days) ? 1 // due soon (≤3 days)
                    : 0                                                                                         // active / no urgent deadline
            )
            .ThenByDescending(r => r.NeedLevelId ?? 0) // Crítico(4) > Urgente(3) > Normal(2) > Baixo(1) > sem nível(0)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Select(r => new RequestListItemDto
            {
                Id = r.Id,
                RequestNumber = r.RequestNumber,
                Title = r.Title,
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
                PlantName = r.Plant != null ? r.Plant.Name : "---",
                SupplierId = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId.Value).Select(q => (int?)q.SupplierId).FirstOrDefault() 
                    : r.SupplierId,
                SupplierName = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId.Value).Select(q => q.SupplierNameSnapshot).FirstOrDefault() 
                    : (r.Supplier != null ? r.Supplier.Name : null),
                SupplierPortalCode = r.SelectedQuotationId.HasValue 
                    ? null 
                    : (r.Supplier != null ? r.Supplier.PortalCode : null),
                EstimatedTotalAmount = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId.Value).Select(q => (decimal?)q.TotalAmount).FirstOrDefault() ?? 0
                    : r.EstimatedTotalAmount,
                CurrencyId = r.CurrencyId,
                CurrencyCode = r.SelectedQuotationId.HasValue 
                    ? r.Quotations.Where(q => q.Id == r.SelectedQuotationId.Value).Select(q => q.Currency).FirstOrDefault() 
                    : (r.Currency != null ? r.Currency.Code : null),
                CapexOpexClassificationId = r.CapexOpexClassificationId,
                RequestedDateUtc = r.RequestedDateUtc,
                NeedByDateUtc = r.NeedByDateUtc,
                CreatedAtUtc = r.CreatedAtUtc,
                IsCancelled = r.IsCancelled,
                SelectedQuotationId = r.SelectedQuotationId
            })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

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

    [HttpGet("{id}")]
    public async Task<ActionResult<RequestDetailsDto>> GetRequest(Guid id)
    {
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
                CurrencyCode = r.Currency != null ? r.Currency.Code : null,
                
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
                    Unit = li.Unit != null ? li.Unit.Code : "EA", 
                    UnitPrice = li.UnitPrice,
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
                    SupplierId = li.SupplierId,
                    CurrencyId = li.CurrencyId,
                    CurrencyCode = li.Currency != null ? li.Currency.Code : null
                }).ToList(),

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
                    ActorName = sh.ActorUser!.FullName
                }).OrderByDescending(sh => sh.CreatedAtUtc).ToList(),

                Quotations = r.Quotations.Select(q => new SavedQuotationDto
                {
                    Id = q.Id,
                    RequestId = q.RequestId,
                    SupplierId = q.SupplierId,
                    SupplierNameSnapshot = q.SupplierNameSnapshot,
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
                        Description = qi.Description,
                        Quantity = qi.Quantity,
                        UnitPrice = qi.UnitPrice,
                        LineTotal = qi.LineTotal,
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
                }).OrderByDescending(q => q.CreatedAtUtc).ToList()
            })
            .FirstOrDefaultAsync();

        if (request == null) return NotFound();

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

        return Ok(request);
    }

    [HttpGet("{id}/timeline")]
    public async Task<ActionResult<RequestTimelineDto>> GetRequestTimeline(Guid id)
    {
        var request = await _context.Requests
            .AsNoTracking()
            .Include(r => r.Status)
            .Include(r => r.RequestType)
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

        // Identify the last stage that the request actually entered
        int lastStageWithHistoryIndex = -1;
        for (int i = 0; i < stages.Count; i++)
        {
            if (history.Any(h => stages[i].StatusCodes.Contains(h.NewStatus.Code)))
            {
                lastStageWithHistoryIndex = i;
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
                        step.State = "completed";
                        // Use a fallback date if no history exists (e.g. request creation or next available history)
                        step.CompletedAt = history.FirstOrDefault(h => h.CreatedAtUtc >= request.CreatedAtUtc)?.CreatedAtUtc ?? request.CreatedAtUtc;
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

        var initialStatusCode = requestTypeEntity.Code == "QUOTATION" ? "WAITING_QUOTATION" : "DRAFT";
        var initialStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == initialStatusCode);
        if (initialStatus == null) return StatusCode(500, $"{initialStatusCode} status code not found in database lookup.");


        if (dto.NeedByDateUtc.HasValue && dto.NeedByDateUtc.Value.Date < DateTime.UtcNow.Date)
        {
            return BadRequest("A data Necessário Até não pode ser no passado.");
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
            DepartmentId = dto.DepartmentId!.Value,
            CompanyId = dto.CompanyId!.Value,
            PlantId = dto.PlantId,
            CapexOpexClassificationId = dto.CapexOpexClassificationId,
            NeedByDateUtc = dto.NeedByDateUtc,
            
            SupplierId = dto.SupplierId,
            BuyerId = dto.BuyerId,
            AreaApproverId = dto.AreaApproverId,
            FinalApproverId = dto.FinalApproverId,
            
            StatusId = initialStatus.Id,
            RequesterId = actorId,
            CreatedByUserId = actorId,
            CreatedAtUtc = DateTime.UtcNow,
            SubmittedAtUtc = requestTypeEntity.Code == "QUOTATION" ? DateTime.UtcNow : null,
            IsCancelled = false
        };

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

        // 2. Fetch tracking entity
        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null)
        {
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });
        }

        // 3. Status Rule: Only DRAFT, Adjustment or WAITING_QUOTATION statuses can be edited
        if (request.Status!.Code != "DRAFT" && request.Status!.Code != "AREA_ADJUSTMENT" && request.Status!.Code != "FINAL_ADJUSTMENT" && request.Status!.Code != "WAITING_QUOTATION")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Este pedido não está em rascunho nem em fase de reajuste/cotação, por isso não pode ser alterado.", 
                Status = 409 
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

        if (requestTypeEntity.Code == "PAYMENT" && !dto.SupplierId.HasValue)
        {
            return BadRequest(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "O fornecedor é obrigatório para pedidos de pagamento.", 
                Status = 400 
            });
        }

        if (dto.NeedByDateUtc.HasValue && dto.NeedByDateUtc.Value.Date < DateTime.UtcNow.Date)
        {
            return BadRequest(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "A data Necessário Até não pode ser no passado.", 
                Status = 400 
            });
        }

        // 4. Update Header Fields and Track Changes
        bool changed = false;
        bool isQuotationStage = request.Status!.Code == "WAITING_QUOTATION";

        if (!isQuotationStage)
        {
            if (request.Title != dto.Title) { request.Title = dto.Title; changed = true; }
            if (request.Description != dto.Description) { request.Description = dto.Description; changed = true; }
            if (request.RequestTypeId != dto.RequestTypeId) { request.RequestTypeId = dto.RequestTypeId; changed = true; }
            if (request.NeedLevelId != dto.NeedLevelId) { request.NeedLevelId = dto.NeedLevelId; changed = true; }
            
            if (request.CurrencyId != dto.CurrencyId)
            {
                if (request.LineItems.Any(l => !l.IsDeleted))
                {
                    return Conflict(new ProblemDetails 
                    { 
                        Title = "Regra de Negócio Violada", 
                        Detail = "Não é possível alterar a moeda de um pedido que já possui itens. Exclua os itens primeiro se desejar alterar a moeda.",
                        Status = 409
                    });
                }
                request.CurrencyId = dto.CurrencyId;
                changed = true;
            }

            if (request.DepartmentId != dto.DepartmentId) { request.DepartmentId = dto.DepartmentId; changed = true; }
            
            if (request.CompanyId != dto.CompanyId)
            {
                if (request.LineItems.Any(l => !l.IsDeleted))
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Regra de Negócio Violada",
                        Detail = "Não é possível alterar a empresa de um pedido que já possui itens. Exclua os itens primeiro.",
                        Status = 400
                    });
                }
                request.CompanyId = dto.CompanyId;
                changed = true;
            }
            if (request.PlantId != dto.PlantId) { request.PlantId = dto.PlantId; changed = true; }
            if (request.CapexOpexClassificationId != dto.CapexOpexClassificationId) { request.CapexOpexClassificationId = dto.CapexOpexClassificationId; changed = true; }
            if (request.NeedByDateUtc != dto.NeedByDateUtc) { request.NeedByDateUtc = dto.NeedByDateUtc; changed = true; }
            if (request.BuyerId != dto.BuyerId) { request.BuyerId = dto.BuyerId; changed = true; }
            if (request.AreaApproverId != dto.AreaApproverId) { request.AreaApproverId = dto.AreaApproverId; changed = true; }
            if (request.FinalApproverId != dto.FinalApproverId) { request.FinalApproverId = dto.FinalApproverId; changed = true; }
        }

        if (request.SupplierId != dto.SupplierId)
        {
            // Hardening rule: For QUOTATION type, SupplierId must not be editable through this general endpoint once no longer in DRAFT
            if (requestTypeEntity.Code == "QUOTATION" && request.Status!.Code != "DRAFT")
            {
                return BadRequest(new ProblemDetails 
                { 
                    Title = "Ação Bloqueada", 
                    Detail = "Para pedidos de Cotação, o fornecedor não pode ser alterado manualmente após o rascunho. Ele será definido pela seleção da cotação vencedora no fluxo de cotações.", 
                    Status = 400 
                });
            }
            request.SupplierId = dto.SupplierId; 
            changed = true; 
        }

        if (changed)
        {
            // 5. Update Audit
            request.UpdatedAtUtc = DateTime.UtcNow;
            request.UpdatedByUserId = actorId;

            // 6. Record history entry for substantive changes
            var history = new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = actorId,
                ActionTaken = "DADOS_ALTERADOS",
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = "Dados básicos ou parâmetros do pedido alterados pelo usuário.",
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.RequestStatusHistories.Add(history);
        }

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> SubmitRequest(Guid id)
    {
        var actorId = CurrentUserId;

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.LineItems)
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

        // Submission Validation: Rely on mandatory field checks below

        if (string.IsNullOrWhiteSpace(request.Title) || 
            string.IsNullOrWhiteSpace(request.Description) ||
            request.NeedLevelId == null || 
            request.NeedByDateUtc == null || 
            request.DepartmentId == 0 || 
            request.BuyerId == null || request.BuyerId == Guid.Empty || 
            request.AreaApproverId == null || request.AreaApproverId == Guid.Empty ||
            request.FinalApproverId == null || request.FinalApproverId == Guid.Empty)
        {
            errors.Add("Preencha os campos obrigatórios antes de submeter o pedido.");
        }

        // Conditional Item Validation
        // PAYMENT requests strictly require at least one item to submit.
        // New Hardening Rule: QUOTATION requests must have at least one line item OR one attachment
        if (request.RequestType!.Code == "QUOTATION" && !request.LineItems.Any(l => !l.IsDeleted) && !request.Attachments.Any(a => !a.IsDeleted))
        {
            errors.Add("O pedido de cotação deve conter pelo menos itens ou um anexo descritivo antes de ser submetido.");
        }

        if (request.RequestType!.Code == "PAYMENT" && !request.LineItems.Any(l => !l.IsDeleted))
        {
            errors.Add("Para submeter, o pedido deve conter pelo menos um item.");
        }

        if (request.RequestType!.Code == "PAYMENT")
        {
            if (request.EstimatedTotalAmount <= 0 && request.LineItems.Any(l => !l.IsDeleted))
                errors.Add("O pedido deve possuir valor total maior que zero.");
            
            if (!request.SupplierId.HasValue)
                errors.Add("O fornecedor é obrigatório para pedidos de pagamento.");
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
            return BadRequest(new ProblemDetails
            {
                Title = "Validação de Submissão Falhou",
                Detail = string.Join(" ", errors.Distinct()),
                Status = 400
            });
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

    [HttpPost("{id}/ocr-extract")]
    public async Task<ActionResult<OcrExtractionResultDto>> OcrExtract(Guid id, IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Nenhum arquivo enviado.");
        }

        var request = await _context.Requests
            .Include(r => r.Status)
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
            var internalResult = await _extractionService.ExtractAsync(stream, file.FileName);

            // Map back to legacy DTO to preserve frontend compatibility
            var legacyResult = ExtractionMapper.MapToLegacyOcrResult(internalResult);

            return Ok(legacyResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during document extraction for Request {RequestId}", id);
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

    [HttpPost("{id}/quotations")]
    public async Task<ActionResult<SavedQuotationDto>> SaveQuotation(Guid id, [FromQuery] Guid? replaceQuotationId, [FromBody] SaveQuotationRequestDto dto)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
                    .ThenInclude(i => i.Unit)
            .Include(r => r.Attachments)
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
        if (request.Quotations.Any(q => q.SupplierId == dto.SupplierId && q.Id != replaceQuotationId))
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Já existe uma cotação para este fornecedor. Confirme a substituição ou escolha outro fornecedor.",
                Status = 409
            });
        }

        // Basic Validation
        if (dto.SupplierId <= 0) return BadRequest("O fornecedor é obrigatório.");
        if (string.IsNullOrWhiteSpace(dto.Currency)) return BadRequest("A moeda é obrigatória.");
        if (dto.Items == null || !dto.Items.Any()) return BadRequest("A cotação deve conter pelo menos um item.");

        var supplier = await _context.Suppliers.FindAsync(dto.SupplierId);
        if (supplier == null) return BadRequest("Fornecedor selecionado não existe.");
        
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
            if (string.IsNullOrWhiteSpace(item.Description)) return BadRequest("Todos os itens devem ter uma descrição.");
            if (item.Quantity <= 0) return BadRequest("A quantidade deve ser maior que zero.");
            if (item.UnitPrice < 0) return BadRequest("O preço unitário não pode ser negativo.");

            decimal grossSubtotal = Math.Round(item.Quantity * item.UnitPrice, 2);

            decimal ivaPercent = item.IvaRateId.HasValue && ivaRates.TryGetValue(item.IvaRateId.Value, out var rate) ? rate : 0m;
            decimal ivaAmount = Math.Round(grossSubtotal * (ivaPercent / 100m), 2);
            decimal lineTotal = Math.Round(grossSubtotal + ivaAmount, 2);

            quotation.Items.Add(new QuotationItem
            {
                Id = Guid.NewGuid(),
                QuotationId = quotation.Id,
                LineNumber = item.LineNumber,
                Description = item.Description,
                UnitId = item.UnitId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                IvaRateId = item.IvaRateId,
                IvaRatePercent = ivaPercent,
                GrossSubtotal = grossSubtotal,
                IvaAmount = ivaAmount,
                LineTotal = lineTotal
            });
        }

        quotation.TotalGrossAmount = quotation.Items.Sum(i => i.GrossSubtotal);
        quotation.TotalIvaAmount = quotation.Items.Sum(i => i.IvaAmount);
        
        // Apply quotation-level discount
        if (dto.DiscountAmount < 0) return BadRequest("O valor do desconto não pode ser negativo.");
        if (dto.DiscountAmount > quotation.TotalGrossAmount + quotation.TotalIvaAmount) return BadRequest("O desconto não pode exceder o valor bruto total e IVA.");
        
        quotation.DiscountAmount = Math.Round(dto.DiscountAmount, 2);
        quotation.TotalAmount = Math.Round(quotation.TotalGrossAmount + quotation.TotalIvaAmount - quotation.DiscountAmount, 2);

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
            .Where(qi => qi.QuotationId == quotation.Id)
            .OrderBy(i => i.LineNumber)
            .ToListAsync();

        return Ok(new SavedQuotationDto
        {
            Id = quotation.Id,
            RequestId = quotation.RequestId,
            SupplierId = quotation.SupplierId,
            SupplierNameSnapshot = quotation.SupplierNameSnapshot,
            DocumentNumber = quotation.DocumentNumber,
            DocumentDate = quotation.DocumentDate,
            Currency = quotation.Currency,
            TotalGrossAmount = quotation.TotalGrossAmount,
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
                UnitId = qi.UnitId,
                UnitName = qi.Unit?.Name,
                UnitCode = qi.Unit?.Code,
                UnitPrice = qi.UnitPrice,
                IvaRateId = qi.IvaRateId,
                IvaRatePercent = qi.IvaRatePercent,
                GrossSubtotal = qi.GrossSubtotal,
                IvaAmount = qi.IvaAmount,
                LineTotal = qi.LineTotal
            }).ToList()
        });
    }

    [HttpPut("{requestId}/quotations/{quotationId}")]
    public async Task<ActionResult<SavedQuotationDto>> UpdateQuotation([FromRoute] Guid requestId, [FromRoute] Guid quotationId, [FromBody] SaveQuotationRequestDto dto)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
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
        var requestWithQuotations = await _context.Requests.Include(r => r.Quotations).FirstOrDefaultAsync(r => r.Id == requestId);
        if (requestWithQuotations != null && requestWithQuotations.Quotations.Any(q => q.SupplierId == dto.SupplierId && q.Id != quotationId))
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Já existe uma cotação para este fornecedor. Confirme a substituição ou escolha outro fornecedor.",
                Status = 409
            });
        }

        // Validation (Explicitly including Currency as per user requirement)
        if (dto.SupplierId <= 0) return BadRequest("O fornecedor é obrigatório.");
        if (string.IsNullOrWhiteSpace(dto.Currency)) return BadRequest("A moeda é obrigatória.");
        if (dto.Items == null || !dto.Items.Any()) return BadRequest("A cotação deve conter pelo menos um item.");

        var supplier = await _context.Suppliers.FindAsync(dto.SupplierId);
        if (supplier == null) return BadRequest("Fornecedor selecionado não existe.");

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

        // Replace Items (Explicitly tracking via DbContext directly to avoid navigation confusion)
        if (quotation.Items.Any())
        {
            _context.QuotationItems.RemoveRange(quotation.Items);
        }

        var ivaRates = await _context.IvaRates.ToDictionaryAsync(i => i.Id, i => i.RatePercent);

        foreach (var item in dto.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Description)) return BadRequest("Todos os itens devem ter uma descrição.");
            if (item.Quantity <= 0) return BadRequest("A quantidade deve ser maior que zero.");
            if (item.UnitPrice < 0) return BadRequest("O preço unitário não pode ser negativo.");

            decimal grossSubtotal = Math.Round(item.Quantity * item.UnitPrice, 2);
            decimal ivaPercent = item.IvaRateId.HasValue && ivaRates.TryGetValue(item.IvaRateId.Value, out var rate) ? rate : 0m;
            decimal ivaAmount = Math.Round(grossSubtotal * (ivaPercent / 100m), 2);
            decimal lineTotal = Math.Round(grossSubtotal + ivaAmount, 2);

            _context.QuotationItems.Add(new QuotationItem
            {
                Id = Guid.NewGuid(),
                QuotationId = quotation.Id,
                LineNumber = item.LineNumber,
                Description = item.Description,
                UnitId = item.UnitId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                IvaRateId = item.IvaRateId,
                IvaRatePercent = ivaPercent,
                GrossSubtotal = grossSubtotal,
                IvaAmount = ivaAmount,
                LineTotal = lineTotal
            });
        }

        quotation.TotalGrossAmount = _context.QuotationItems.Local.Where(qi => qi.QuotationId == quotation.Id).Sum(i => i.GrossSubtotal);
        quotation.TotalIvaAmount = _context.QuotationItems.Local.Where(qi => qi.QuotationId == quotation.Id).Sum(i => i.IvaAmount);
        
        // Apply quotation-level discount safely
        if (dto.DiscountAmount < 0) return BadRequest("O valor do desconto não pode ser negativo.");
        if (dto.DiscountAmount > quotation.TotalGrossAmount + quotation.TotalIvaAmount) return BadRequest("O desconto não pode exceder o valor bruto total e IVA.");
        
        quotation.DiscountAmount = Math.Round(dto.DiscountAmount, 2);
        quotation.TotalAmount = Math.Round(quotation.TotalGrossAmount + quotation.TotalIvaAmount - quotation.DiscountAmount, 2);


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
            .Where(qi => qi.QuotationId == quotation.Id)
            .OrderBy(i => i.LineNumber)
            .ToListAsync();

        return Ok(new SavedQuotationDto
        {
            Id = quotation.Id,
            RequestId = quotation.RequestId,
            SupplierId = quotation.SupplierId,
            SupplierNameSnapshot = quotation.SupplierNameSnapshot,
            DocumentNumber = quotation.DocumentNumber,
            DocumentDate = quotation.DocumentDate,
            Currency = quotation.Currency,
            TotalGrossAmount = quotation.TotalGrossAmount,
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
                UnitId = qi.UnitId,
                UnitName = qi.Unit?.Name,
                UnitCode = qi.Unit?.Code,
                UnitPrice = qi.UnitPrice,
                IvaRateId = qi.IvaRateId,
                IvaRatePercent = qi.IvaRatePercent,
                GrossSubtotal = qi.GrossSubtotal,
                IvaAmount = qi.IvaAmount,
                LineTotal = qi.LineTotal
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
    [HttpPost("{id}/duplicate")]
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

            foreach (var item in activeItems)
            {
                var computedItemTotal = item.Quantity * item.UnitPrice;
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

    [HttpPost("{id}/cancel")]
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

    [HttpPost("{requestId}/line-items")]
    public async Task<IActionResult> AddLineItem(Guid requestId, [FromBody] CreateRequestLineItemDto dto)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .Include(r => r.LineItems)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        if (request.Status!.Code != "DRAFT" && request.Status!.Code != "AREA_ADJUSTMENT" && request.Status!.Code != "FINAL_ADJUSTMENT" && request.Status!.Code != "WAITING_QUOTATION")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Operação bloqueada: este pedido não está em rascunho nem em fase de reajuste/cotação, por isso não é possível adicionar itens.", 
                Status = 409 
            });
        }

        var unit = await _context.Units.FindAsync(dto.UnitId);
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

        // Auto-assign initial item status based on parent request type (backend-controlled, not from client)
        int? lineItemStatusId = request.RequestType?.Code switch
        {
            "QUOTATION" => 1, // WAITING_QUOTATION
            "PAYMENT"   => 2, // PENDING
            _           => null
        };

        var nextLineNumber = request.LineItems.Any() ? request.LineItems.Max(l => l.LineNumber) + 1 : 1;
        var quantity = dto.Quantity ?? 0;
        var unitPrice = dto.UnitPrice ?? 0;
        var computedTotal = quantity * unitPrice;

        var newItem = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            LineNumber = nextLineNumber,
            ItemPriority = itemPriority,
            Description = dto.Description,
            Quantity = quantity,
            UnitId = dto.UnitId, // Mapped from Frontend Select
            UnitPrice = unitPrice,
            TotalAmount = computedTotal,
            CurrencyId = request.RequestType?.Code == "QUOTATION" && dto.CurrencyId.HasValue ? dto.CurrencyId : request.CurrencyId,
            PlantId = dto.PlantId,
            LineItemStatusId = lineItemStatusId, // Auto-assigned, never from client
            SupplierId = request.RequestType?.Code == "PAYMENT" ? request.SupplierId : null,
            SupplierName = request.RequestType?.Code == "PAYMENT" ? null : dto.SupplierName,
            Notes = dto.Notes,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorId
        };

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

        _context.RequestLineItems.Add(newItem);

        // Record item addition in history
        var itemHistory = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "ITEM_ADDED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Item #{newItem.LineNumber} (\"{newItem.Description}\") adicionado ao pedido por {user.FullName}.",
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(itemHistory);

        await _context.SaveChangesAsync();

        // Recalculate total from DB AFTER saving, to avoid in-memory collection double-counting
        request.EstimatedTotalAmount = await _context.RequestLineItems
            .Where(l => l.RequestId == requestId && !l.IsDeleted)
            .SumAsync(l => l.TotalAmount);
        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = actorId;

        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRequest), new { id = request.Id }, new { ItemId = newItem.Id });
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
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        if (request.Status!.Code != "DRAFT" && request.Status!.Code != "AREA_ADJUSTMENT" && request.Status!.Code != "FINAL_ADJUSTMENT" && request.Status!.Code != "WAITING_QUOTATION")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Operação bloqueada: este pedido não está em rascunho nem em fase de reajuste/cotação, por isso não é possível editar itens.", 
                Status = 409 
            });
        }

        var item = request.LineItems.FirstOrDefault(l => l.Id == itemId && !l.IsDeleted);
        if (item == null) return NotFound(new ProblemDetails { Title = "Item não encontrado no pedido.", Status = 404 });

        var unit = await _context.Units.FindAsync(dto.UnitId);
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
        item.TotalAmount = item.Quantity * item.UnitPrice;
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

        // Recalculate total from DB AFTER saving, to avoid in-memory collection double-counting
        request.EstimatedTotalAmount = await _context.RequestLineItems
            .Where(l => l.RequestId == requestId && !l.IsDeleted)
            .SumAsync(l => l.TotalAmount);
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
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return NotFound();

        if (request.Status!.Code != "DRAFT" && request.Status!.Code != "AREA_ADJUSTMENT" && request.Status!.Code != "FINAL_ADJUSTMENT" && request.Status!.Code != "WAITING_QUOTATION")
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = "Operação bloqueada: este pedido não está em rascunho nem em fase de reajuste/cotação, por isso não é possível excluir itens.", 
                Status = 409 
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

        // Recalculate total from DB AFTER saving, to avoid in-memory collection double-counting
        request.EstimatedTotalAmount = await _context.RequestLineItems
            .Where(l => l.RequestId == requestId && !l.IsDeleted)
            .SumAsync(l => l.TotalAmount);
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

        // LineItems and Attachments will cascade delete based on EntityConfigurations.cs
        _context.Requests.Remove(request);

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/area-approval/approve")]
    public async Task<IActionResult> ApproveArea(Guid id, [FromBody] ApprovalActionDto dto)
    {
        return await ProcessAreaApproval(id, "APPROVE", "WAITING_FINAL_APPROVAL", dto.Comment, dto.SelectedQuotationId);
    }

    [HttpPost("{id}/area-approval/reject")]
    public async Task<IActionResult> RejectArea(Guid id, [FromBody] ApprovalActionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "Informe o motivo da rejeição.", Status = 400 });

        return await ProcessAreaApproval(id, "REJECT", "REJECTED", dto.Comment, dto.SelectedQuotationId);
    }

    [HttpPost("{id}/area-approval/request-adjustment")]
    public async Task<IActionResult> RequestAdjustmentArea(Guid id, [FromBody] ApprovalActionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "Informe o motivo do reajuste.", Status = 400 });

        return await ProcessAreaApproval(id, "REQUEST_ADJUSTMENT", "AREA_ADJUSTMENT", dto.Comment, dto.SelectedQuotationId);
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

        return await ProcessFinalApproval(id, "REJECT", "REJECTED", dto.Comment);
    }

    [HttpPost("{id}/final-approval/request-adjustment")]
    public async Task<IActionResult> RequestAdjustmentFinal(Guid id, [FromBody] ApprovalActionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "Informe o motivo do reajuste.", Status = 400 });

        // Semantic grounding: FINAL_ADJUSTMENT internal code strictly represents "REAJUSTE A.F" in this stage
        return await ProcessFinalApproval(id, "REQUEST_ADJUSTMENT", "FINAL_ADJUSTMENT", dto.Comment);
    }

    private async Task<IActionResult> ProcessFinalApproval(Guid id, string action, string targetStatusCode, string? comment)
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

        if (action == "APPROVE" && request.RequestType!.Code == "QUOTATION" && !request.SelectedQuotationId.HasValue)
        {
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Não é possível aprovar um pedido de Cotação sem selecionar a cotação vencedora primeiro.", Status = 400 });
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

        return await ApplyStatusChangeAndSyncItemsAsync(request, targetStatusCode, action, historyComment, successMessage, actorId);
    }

    private async Task<IActionResult> ProcessAreaApproval(Guid id, string action, string targetStatusCode, string? comment, Guid? selectedQuotationId)
    {
        var actorId = CurrentUserId;

        // Role-based Authorization: strictly enforce Area Approver role
        if (!CurrentUserRoles.Contains(RoleConstants.AreaApprover))
            return StatusCode(403, "Apenas o papel de Aprovador de Área pode realizar aprovações nesta etapa.");

        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .FirstOrDefaultAsync(r => r.Id == id);

        // Winner Selection Validation (only for Quotation Approve)
        if (request != null && request.RequestType!.Code == "QUOTATION" && action == "APPROVE")
        {
            if (!selectedQuotationId.HasValue)
            {
                return BadRequest(new ProblemDetails 
                { 
                    Title = "Vencedor não Selecionado", 
                    Detail = "É necessário selecionar um fornecedor vencedor para aprovar um pedido de cotação.", 
                    Status = 400 
                });
            }

            // Verify if the quotation belongs to this request
            var quotationExists = await _context.Quotations.AnyAsync(q => q.Id == selectedQuotationId && q.RequestId == id);
            if (!quotationExists)
            {
                return BadRequest(new ProblemDetails 
                { 
                    Title = "Cotação Inválida", 
                    Detail = "A cotação selecionada não pertence a este pedido.", 
                    Status = 400 
                });
            }

            request.SelectedQuotationId = selectedQuotationId;
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
            "APPROVE" => $"Aprovação da Área realizada. {comment}".Trim(),
            "REJECT" => $"Pedido rejeitado na Aprovação da Área. Motivo: {comment}",
            "REQUEST_ADJUSTMENT" => $"Solicitado reajuste (Rework) na Aprovação da Área. Motivo: {comment}",
            _ => comment ?? string.Empty
        };

        string successMessage = action switch
        {
            "APPROVE" => "Pedido enviado para aprovação final com sucesso.",
            "REJECT" => "Pedido rejeitado com sucesso.",
            "REQUEST_ADJUSTMENT" => "Pedido devolvido para reajuste com sucesso.",
            _ => "Operação realizada com sucesso."
        };

        return await ApplyStatusChangeAndSyncItemsAsync(request, targetStatusCode, action, historyComment, successMessage, actorId);
    }

    [HttpPost("{id}/operational/register-po")]
    public async Task<IActionResult> RegisterPo(Guid id, [FromBody] ApprovalActionDto dto)
    {
        if (!await HasAttachmentAsync(id, RequestAttachment.TYPE_PO))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ação Bloqueada",
                Detail = "É necessário anexar a P.O antes de registrar.",
                Status = 400
            });
        }
        return await ProcessCommonOperationalTransition(id, "REGISTER_PO", "PO_ISSUED", new[] { "APPROVED" }, dto.Comment, "P.O registrada com sucesso.");
    }

    [HttpPost("{id}/operational/schedule-payment")]
    public async Task<IActionResult> SchedulePayment(Guid id, [FromBody] ApprovalActionDto dto)
    {
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

    [HttpPost("{id}/operational/move-to-receipt")]
    public async Task<IActionResult> MoveToReceipt(Guid id, [FromBody] ApprovalActionDto dto)
    {
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        // Unified post-PO operational flow: strictly from PAYMENT_COMPLETED for all types
    string[] requiredStatuses = new[] { "PAYMENT_COMPLETED" };

    return await ProcessTransition(id, "MOVE_TO_RECEIPT", "WAITING_RECEIPT", requiredStatuses, dto.Comment, "Pedido movido para aguardando recibo.", new[] { "PAYMENT", "QUOTATION" });
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
                .Include(r => r.LineItems)
                    .ThenInclude(li => li.LineItemStatus)
                .Include(r => r.Quotations)
                    .ThenInclude(q => q.Items)
                        .ThenInclude(qi => qi.LineItemStatus)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null) return NotFound();

            // Idempotency check: If already completed, just return success without history/timestamp changes
            if (request.Status!.Code == "COMPLETED")
            {
                return Ok(new { Message = "Pedido já finalizado.", StatusCode = "COMPLETED" });
            }

            // Status Rule: Must be in WAITING_RECEIPT or IN_FOLLOWUP to be finalized
            var allowedStatuses = new[] { "WAITING_RECEIPT", "IN_FOLLOWUP" };
            if (!allowedStatuses.Contains(request.Status!.Code))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Ação Inválida",
                    Detail = $"O pedido não está em um status válido para finalização. Status atual: {request.Status.Code}.",
                    Status = 400
                });
            }

            // 2. Authoritative Status Determination
            string nextStatusCode = RequestWorkflowHelper.DeterminePostReceivingStatus(request);
            var targetStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == nextStatusCode);
            if (targetStatus == null) return StatusCode(500, $"Status '{nextStatusCode}' não configurado.");

            var oldStatusId = request.StatusId;
            request.StatusId = targetStatus.Id;
            request.UpdatedAtUtc = DateTime.UtcNow;
            request.UpdatedByUserId = actorId;

            // 3. Create Status History entry
            var history = new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = actorId,
                ActionTaken = "FINALIZE",
                PreviousStatusId = oldStatusId,
                NewStatusId = targetStatus.Id,
                Comment = dto.Comment ?? (nextStatusCode == "COMPLETED" 
                    ? "Recebimento finalizado com sucesso." 
                    : "Recebimento finalizado com itens pendentes (Movido para Acompanhamento)."),
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.RequestStatusHistories.Add(history);

            // 4. Persistence
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { Message = "Recebimento finalizado com sucesso.", StatusCode = nextStatusCode });
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
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .Include(r => r.Attachments)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

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

        return await ProcessQuotationTransition(id, "COMPLETE_QUOTATION", "WAITING_AREA_APPROVAL", new[] { "WAITING_QUOTATION", "AREA_ADJUSTMENT", "FINAL_ADJUSTMENT" }, dto.Comment, "Cotação concluída e enviada para aprovação da área.");
    }

    [HttpPost("{requestId}/quotations/{quotationId}/select")]
    public async Task<IActionResult> SelectQuotation(Guid requestId, Guid quotationId)
    {
        var actorId = CurrentUserId;
        var user = await _context.Users.FindAsync(actorId);
        if (user == null) return Unauthorized();

        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.Quotations)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // Role check: Only Area Approver can select a winner (Final Approver can only review)
        if (!CurrentUserRoles.Contains(RoleConstants.AreaApprover))
        {
            if (CurrentUserRoles.Contains(RoleConstants.FinalApprover))
                return StatusCode(403, "O Aprovador Final não pode alterar o vencedor selecionado pela área.");
            
            return StatusCode(403, "Apenas o papel de Aprovação de Área pode selecionar a cotação vencedora.");
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
        if ((action == "SCHEDULE_PAYMENT" || action == "COMPLETE_PAYMENT") && !roles.Contains(RoleConstants.Finance))
            return StatusCode(403, "Apenas o Financeiro pode gerir o fluxo de pagamento.");
        if ((action == "MOVE_TO_RECEIPT" || action == "FINALIZE") && !roles.Contains(RoleConstants.Receiving))
            return StatusCode(403, "Apenas o Almoxarifado/Recebimento pode finalizar o pedido.");

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

    private async Task<IActionResult> ApplyStatusChangeAndSyncItemsAsync(
        Request request, 
        string targetStatusCode, 
        string actionTaken, 
        string historyComment, 
        string successMessage, 
        Guid actorUserId)
    {
        var targetStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == targetStatusCode);
        if (targetStatus == null) return StatusCode(500, $"Status '{targetStatusCode}' não configurado no sistema.");

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

        await _context.SaveChangesAsync();

        return Ok(new { Message = successMessage, StatusCode = targetStatusCode });
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
}
