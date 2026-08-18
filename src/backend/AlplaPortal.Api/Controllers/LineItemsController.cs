using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Api.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/line-items")]
public class LineItemsController : BaseController
{
    private readonly ILogger<LineItemsController> _logger;
    private readonly AlplaPortal.Application.Interfaces.IApprovalRoutingService _approvalRouting;
    private readonly PostPaymentCompletionOptions? _postPaymentOptions;
    private readonly IRequestCompletionService? _completionService;

    /// <summary>
    /// The Phase 4 dependencies are optional-by-default so existing direct constructions (tests)
    /// keep compiling with the exact legacy behaviour; DI always supplies both in production.
    /// </summary>
    public LineItemsController(ApplicationDbContext context, ILogger<LineItemsController> logger,
        AlplaPortal.Application.Interfaces.IApprovalRoutingService approvalRouting,
        IOptions<PostPaymentCompletionOptions>? postPaymentOptions = null,
        IRequestCompletionService? completionService = null) : base(context)
    {
        _logger = logger;
        _approvalRouting = approvalRouting;
        _postPaymentOptions = postPaymentOptions?.Value;
        _completionService = completionService;
    }

    [HttpGet]
    public async Task<ActionResult> GetLineItems(
        [FromQuery] string? query = null,
        [FromQuery] string? itemStatus = null,
        [FromQuery] string? requestStatus = null,
        [FromQuery] int? plant = null,
        [FromQuery] int? department = null,
        [FromQuery] string? owner = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var swOverall = System.Diagnostics.Stopwatch.StartNew();
        var swCount = new System.Diagnostics.Stopwatch();
        var swPage = new System.Diagnostics.Stopwatch();
        var swRelated = new System.Diagnostics.Stopwatch();

        var requestsBaseQuery = await GetScopedRequestsQuery();
        
        // Phase 0: Build the filtered query (No Includes needed yet)
        var filteredRequests = requestsBaseQuery
            .AsNoTracking()
            .Where(r => !r.IsCancelled && r.RequestType!.Code == RequestConstants.Types.Quotation);

        // Security: Filter to only show items for requests in specific allowed statuses.
        // For QUOTATION requests with partial approval, the request must also appear if:
        //   (b) it has at least one active item with QuotationLifecycleStatus null or QUOTATION_PENDING; OR
        //   (c) it has a batch in AREA_ADJUSTMENT or FINAL_ADJUSTMENT (rework).
        // Conditions (b) and (c) are scoped to QUOTATION requests only so PAYMENT requests
        // are never included just because their line items have QuotationLifecycleStatus = null.
        var allowedStatuses = new[] 
        { 
            RequestConstants.Statuses.WaitingQuotation, 
            RequestConstants.Statuses.AreaAdjustment, 
            RequestConstants.Statuses.FinalAdjustment, 
            RequestConstants.Statuses.PaymentCompleted, 
            RequestConstants.Statuses.InFollowup, 
            RequestConstants.Statuses.WaitingAreaApproval, 
            RequestConstants.Statuses.WaitingFinalApproval, 
            RequestConstants.Statuses.WaitingCostCenter 
        };

        var reworkBatchStatuses = new[]
        {
            RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
            RequestConstants.ApprovalBatchStatuses.FinalAdjustment
        };

        filteredRequests = filteredRequests.Where(r =>
            // (a) Legacy status in allowed list — applies to both QUOTATION and PAYMENT
            allowedStatuses.Contains(r.Status!.Code)
            // (b+c) QUOTATION-only: pending items or rework batches
            || (
                r.RequestType!.Code == RequestConstants.Types.Quotation
                && (
                    r.LineItems.Any(li => !li.IsDeleted
                        && (li.QuotationLifecycleStatus == null
                            || li.QuotationLifecycleStatus == RequestConstants.QuotationLifecycleStatuses.QuotationPending))
                    || r.ApprovalBatches.Any(b => reworkBatchStatuses.Contains(b.Status))
                )
            )
        );

        if (department.HasValue)
        {
            filteredRequests = filteredRequests.Where(r => r.DepartmentId == department.Value);
        }

        if (!string.IsNullOrWhiteSpace(requestStatus))
        {
            filteredRequests = filteredRequests.Where(r => r.Status!.Code == requestStatus);
        }

        if (owner == "unassigned")
        {
            filteredRequests = filteredRequests.Where(r => r.BuyerId == null);
        }
        else if (owner == "me")
        {
            var currentUserId = CurrentUserId;
            filteredRequests = filteredRequests.Where(r => r.BuyerId == currentUserId);
        }
        else if (!string.IsNullOrWhiteSpace(owner) && Guid.TryParse(owner, out var ownerId))
        {
            filteredRequests = filteredRequests.Where(r => r.BuyerId == ownerId);
        }

        // Project to a combined structure for filtering
        var dbQuery = filteredRequests.SelectMany(
            r => r.LineItems.Where(li => !li.IsDeleted).DefaultIfEmpty(),
            (r, li) => new { Request = r, LineItem = li }
        );

        if (plant.HasValue)
        {
            dbQuery = dbQuery.Where(x => x.LineItem != null && x.LineItem.PlantId == plant.Value);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var upperQuery = query.ToUpper();
            dbQuery = dbQuery.Where(x => 
                (x.Request.RequestNumber != null && x.Request.RequestNumber.Contains(upperQuery)) ||
                x.Request.Title.ToUpper().Contains(upperQuery) ||
                (x.LineItem != null && x.LineItem.Description.ToUpper().Contains(upperQuery)) ||
                (x.LineItem != null && x.LineItem.Supplier != null && x.LineItem.Supplier.PrimaveraCode != null && x.LineItem.Supplier.PrimaveraCode.Contains(upperQuery))
            );
        }

        if (!string.IsNullOrWhiteSpace(itemStatus))
        {
            dbQuery = dbQuery.Where(x => x.LineItem != null && x.LineItem.LineItemStatus!.Code == itemStatus);
        }

        // Phase 1: Count
        swCount.Start();
        var totalCount = await dbQuery.CountAsync();
        swCount.Stop();

        // Phase 2: Page Fetch (Only Flat Data)
        swPage.Start();
        var pageItemsRaw = await dbQuery
            .OrderByDescending(x => x.Request.CreatedAtUtc)
            .ThenBy(x => x.Request.Id)
            .ThenBy(x => x.LineItem != null ? x.LineItem.LineNumber : 0)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                LineItem = x.LineItem,
                RequestId = x.Request.Id,
                RequestNumber = x.Request.RequestNumber,
                RequestTitle = x.Request.Title,
                RequestDescription = x.Request.Description,
                RequestStatusName = x.Request.Status!.Name,
                RequestStatusCode = x.Request.Status!.Code,
                RequestStatusBadgeColor = x.Request.Status!.BadgeColor,
                RequestTypeCode = x.Request.RequestType!.Code,
                RequestTypeName = x.Request.RequestType!.Name,
                RequestPlantId = x.Request.PlantId,
                RequestPlantName = x.Request.Plant != null ? x.Request.Plant.Name : null,
                RequesterName = x.Request.Requester!.FullName,
                RequesterEmail = x.Request.Requester!.Email,
                NeedByDateUtc = x.Request.NeedByDateUtc,
                DepartmentName = x.Request.Department!.Name,
                CompanyId = x.Request.CompanyId,
                RequestSupplierId = x.Request.SupplierId,
                RequestSupplierName = x.Request.Supplier != null ? x.Request.Supplier.Name : null,
                RequestSupplierCode = x.Request.Supplier != null ? x.Request.Supplier.PortalCode : null,
                RequestCurrencyId = x.Request.CurrencyId,
                RequestCurrencyCode = x.Request.Currency != null ? x.Request.Currency.Code : null,
                RequestUpdatedAtUtc = x.Request.UpdatedAtUtc,
                RequestCreatedAtUtc = x.Request.CreatedAtUtc,
                RequestBuyerId = x.Request.BuyerId,
                RequestBuyerName = x.Request.Buyer != null ? x.Request.Buyer.FullName : null,
                RequestBuyerEmail = x.Request.Buyer != null ? x.Request.Buyer.Email : null,
                RequestAreaApproverId = x.Request.AreaApproverId,
                RequestAreaApproverName = x.Request.AreaApprover != null ? x.Request.AreaApprover.FullName : null,
                RequestAreaApproverEmail = x.Request.AreaApprover != null ? x.Request.AreaApprover.Email : null,
                RequestFinalApproverId = x.Request.FinalApproverId,
                RequestFinalApproverName = x.Request.FinalApprover != null ? x.Request.FinalApprover.FullName : null,
                RequestFinalApproverEmail = x.Request.FinalApprover != null ? x.Request.FinalApprover.Email : null,
                
                // Line Item specific flat fields
                ItemPlantName = x.LineItem != null && x.LineItem.Plant != null ? x.LineItem.Plant.Name : null,
                ItemUnitCode = x.LineItem != null && x.LineItem.Unit != null ? x.LineItem.Unit.Code : null,
                ItemCurrencyCode = x.LineItem != null && x.LineItem.Currency != null ? x.LineItem.Currency.Code : null,
                ItemStatusName = x.LineItem != null && x.LineItem.LineItemStatus != null ? x.LineItem.LineItemStatus.Name : null,
                ItemStatusCode = x.LineItem != null && x.LineItem.LineItemStatus != null ? x.LineItem.LineItemStatus.Code : null,
                ItemStatusBadgeColor = x.LineItem != null && x.LineItem.LineItemStatus != null ? x.LineItem.LineItemStatus.BadgeColor : null,
                ItemSupplierName = x.LineItem != null && x.LineItem.Supplier != null ? x.LineItem.Supplier.Name : (x.LineItem != null ? x.LineItem.SupplierName : null),
                ItemSupplierCode = x.LineItem != null && x.LineItem.Supplier != null ? x.LineItem.Supplier.PortalCode : null,
                ItemPrimaveraCode = x.LineItem != null && x.LineItem.Supplier != null ? x.LineItem.Supplier.PrimaveraCode : null,
                ItemCostCenterName = x.LineItem != null && x.LineItem.CostCenter != null ? x.LineItem.CostCenter.Name : null,
                ItemCostCenterCode = x.LineItem != null && x.LineItem.CostCenter != null ? x.LineItem.CostCenter.Code : null,
                ItemCatalogId = x.LineItem != null ? x.LineItem.ItemCatalogId : null
            })
            .ToListAsync();
        swPage.Stop();

        // Phase 3: Related Data Hydration (Quotations, Attachments, Histories)
        swRelated.Start();
        var uniqueRequestIds = pageItemsRaw.Select(x => x.RequestId).Distinct().ToList();
        
        var requestDetails = await _context.Requests
            .AsNoTracking()
            .AsSplitQuery()
            .Where(r => uniqueRequestIds.Contains(r.Id))
            .Select(r => new
            {
                r.Id,
                ProformaAttachments = r.Attachments
                    .Where(a => (a.AttachmentTypeCode == "PROFORMA" || a.AttachmentTypeCode == "QUOTATION") && !a.IsDeleted)
                    .OrderByDescending(a => a.UploadedAtUtc)
                    .Select(a => new ProformaAttachmentDto { Id = a.Id, FileName = a.FileName })
                    .ToList(),
                SupportingAttachments = r.Attachments
                    .Where(a => a.AttachmentTypeCode == "SUPPORTING" && !a.IsDeleted)
                    .OrderByDescending(a => a.UploadedAtUtc)
                    .Select(a => new ProformaAttachmentDto { Id = a.Id, FileName = a.FileName })
                    .ToList(),
                StatusHistories = r.StatusHistories
                    .Where(sh => sh.ActionTaken == "REQUEST_ADJUSTMENT")
                    .OrderByDescending(sh => sh.CreatedAtUtc)
                    .Select(sh => new 
                    { 
                        sh.Comment, 
                        ActorName = sh.ActorUser!.FullName, 
                        NewStatusCode = sh.NewStatus!.Code,
                        sh.CreatedAtUtc 
                    })
                    .Take(1)
                    .ToList(),
                Quotations = r.Quotations
                    .OrderByDescending(q => q.CreatedAtUtc)
                    .Select(q => new SavedQuotationDto
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
                        TotalGrossAmount = q.TotalGrossAmount,
                        TotalIvaAmount = q.TotalIvaAmount,
                        TotalAmount = q.TotalAmount,
                        DiscountAmount = q.DiscountAmount,
                        TotalTaxableBase = q.TotalTaxableBase,
                        SourceType = q.SourceType,
                        SourceFileName = q.SourceFileName,
                        ProformaAttachmentId = q.ProformaAttachmentId,
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
                            UnitId = qi.UnitId,
                            UnitName = qi.Unit != null ? qi.Unit.Name : null,
                            UnitCode = qi.Unit != null ? qi.Unit.Code : null,
                            IvaRateId = qi.IvaRateId,
                            IvaRatePercent = qi.IvaRatePercent,
                            DiscountAmount = qi.DiscountAmount,
                            DiscountPercent = qi.DiscountPercent,
                            GrossSubtotal = qi.GrossSubtotal,
                            TaxableBase = qi.GrossSubtotal - qi.DiscountAmount, // Net taxable base — the defect fix for the buyer batch modals (see SavedQuotationItemDto)
                            IvaAmount = qi.IvaAmount,
                            LineTotal = qi.LineTotal,
                            ItemCatalogId = qi.ItemCatalogId,
                            ItemCatalogCode = qi.ItemCatalog != null ? qi.ItemCatalog.Code : null,
                            MappedRequestLineItemId = qi.MappedRequestLineItemId,
                            ReconciliationStatus = qi.ReconciliationStatus,
                            ReconciliationJustification = qi.ReconciliationJustification
                        }).ToList()
                    })
                    .ToList(),
                ApprovalBatches = r.ApprovalBatches.Select(b => new
                {
                    b.Id,
                    b.BatchNumber,
                    b.Status,
                    b.CreatedAtUtc,
                    Items = b.Items.Select(bi => new
                    {
                        bi.Id,
                        bi.RequestLineItemId,
                        bi.SelectedQuotationItemId
                    }).ToList()
                }).ToList()
            })
            .ToListAsync();
        
        var requestDetailsLookup = requestDetails.ToDictionary(x => x.Id);
        swRelated.Stop();

        // Phase 4: Final Merge
        var finalItems = pageItemsRaw.Select(x => 
        {
            var rd = requestDetailsLookup.TryGetValue(x.RequestId, out var val) ? val : null;
            var latestAdj = rd?.StatusHistories.FirstOrDefault();
            
            return new LineItemDetailsDto
            {
                LineItemId = x.LineItem != null ? (Guid?)x.LineItem.Id : null,
                LineNumber = x.LineItem != null ? x.LineItem.LineNumber : 0,
                RequestId = x.RequestId,
                RequestNumber = x.RequestNumber ?? "",
                RequestTitle = x.RequestTitle,
                RequestDescription = x.RequestDescription,
                RequestStatusName = x.RequestStatusName,
                RequestStatusCode = x.RequestStatusCode,
                RequestStatusBadgeColor = x.RequestStatusBadgeColor,
                RequestTypeCode = x.RequestTypeCode,
                RequestTypeName = x.RequestTypeName,
                CompanyId = x.CompanyId,
                
                ProformaId = rd?.ProformaAttachments.FirstOrDefault()?.Id,
                ProformaFileName = rd?.ProformaAttachments.FirstOrDefault()?.FileName,
                ProformaAttachments = rd?.ProformaAttachments,
                SupportingAttachments = rd?.SupportingAttachments,
                
                DepartmentName = x.DepartmentName,
                PlantId = x.LineItem != null ? x.LineItem.PlantId : null,
                PlantName = x.ItemPlantName,
                RequestPlantId = x.RequestPlantId,
                RequestPlantName = x.RequestPlantName,
                RequesterName = x.RequesterName,
                RequesterEmail = x.RequesterEmail,
                NeedByDateUtc = x.NeedByDateUtc,
                
                ItemDescription = x.LineItem != null ? x.LineItem.Description : string.Empty,
                ItemPriority = x.LineItem != null ? x.LineItem.ItemPriority : null,
                PrimaveraCode = x.ItemPrimaveraCode,
                
                Quantity = x.LineItem != null ? x.LineItem.Quantity : 0,
                UnitPrice = x.LineItem != null ? x.LineItem.UnitPrice : 0,
                Total = x.LineItem != null ? x.LineItem.Quantity * x.LineItem.UnitPrice : 0,
                UnitCode = x.ItemUnitCode,
                
                CurrencyId = x.LineItem != null ? x.LineItem.CurrencyId : null,
                CurrencyCode = x.ItemCurrencyCode,
                RequestCurrencyId = x.RequestCurrencyId,
                RequestCurrencyCode = x.RequestCurrencyCode,
                
                LineItemStatusCode = x.ItemStatusCode,
                QuotationLifecycleStatus = x.LineItem != null ? x.LineItem.QuotationLifecycleStatus : null,
                LineItemStatusName = x.ItemStatusName,
                LineItemStatusBadgeColor = x.ItemStatusBadgeColor,
                
                SupplierId = x.RequestTypeCode == "PAYMENT" ? x.RequestSupplierId : (x.LineItem != null ? x.LineItem.SupplierId : null),
                SupplierName = x.RequestTypeCode == "PAYMENT" ? x.RequestSupplierName : x.ItemSupplierName,
                SupplierCode = x.RequestTypeCode == "PAYMENT" ? x.RequestSupplierCode : x.ItemSupplierCode,
                
                RequestSupplierId = x.RequestSupplierId,
                RequestSupplierName = x.RequestSupplierName,
                RequestSupplierCode = x.RequestSupplierCode,
                
                BuyerId = x.RequestBuyerId,
                BuyerName = x.RequestBuyerName,
                BuyerEmail = x.RequestBuyerEmail,
                AreaApproverId = x.RequestAreaApproverId,
                AreaApproverName = x.RequestAreaApproverName,
                AreaApproverEmail = x.RequestAreaApproverEmail,
                FinalApproverId = x.RequestFinalApproverId,
                FinalApproverName = x.RequestFinalApproverName,
                FinalApproverEmail = x.RequestFinalApproverEmail,

                CostCenterId = x.LineItem != null ? x.LineItem.CostCenterId : null,
                CostCenterName = x.ItemCostCenterName,
                CostCenterCode = x.ItemCostCenterCode,
                
                Notes = x.LineItem != null ? x.LineItem.Notes : null,
                ItemCatalogId = x.ItemCatalogId,
                UpdatedAtUtc = x.LineItem != null ? (x.LineItem.UpdatedAtUtc ?? x.LineItem.CreatedAtUtc) : x.RequestUpdatedAtUtc ?? x.RequestCreatedAtUtc,
                
                LatestAdjustmentMessage = latestAdj?.Comment,
                LatestAdjustmentActor = latestAdj?.ActorName,
                LatestAdjustmentRole = latestAdj != null ? (latestAdj.NewStatusCode == "AREA_ADJUSTMENT" ? "Aprovador de Área" : (latestAdj.NewStatusCode == "FINAL_ADJUSTMENT" ? "Aprovador Final" : "Aprovador")) : null,
                LatestAdjustmentDateUtc = latestAdj?.CreatedAtUtc,
                
                Quotations = rd?.Quotations ?? new List<SavedQuotationDto>(),
                ApprovalBatches = rd?.ApprovalBatches?.Cast<object>().ToList() ?? new List<object>()
            };
        }).ToList();

        swOverall.Stop();
        
        // ═══ TEMPORARY DIAGNOSTIC: BUG-REWORK-030 ═══
        // Log exact payload for requests in Adjustment status to identify failed button conditions
        // TODO: Remove after root cause confirmed
        var adjustmentItems = finalItems.Where(f => f.RequestStatusCode == "AREA_ADJUSTMENT" || f.RequestStatusCode == "FINAL_ADJUSTMENT").ToList();
        if (adjustmentItems.Any())
        {
            var currentUserId = CurrentUserId;
            var groupedByRequest = adjustmentItems.GroupBy(f => f.RequestId).ToList();
            foreach (var reqGroup in groupedByRequest)
            {
                var first = reqGroup.First();
                _logger.LogWarning(
                    "[DIAG:REWORK-BUTTON] ─── Backend Payload for {RequestNumber} ───\n" +
                    "  RequestId:        {RequestId}\n" +
                    "  StatusCode:       {StatusCode}\n" +
                    "  RequestTypeCode:  {RequestTypeCode}\n" +
                    "  BuyerId:          {BuyerId}\n" +
                    "  BuyerName:        {BuyerName}\n" +
                    "  BuyerEmail:       {BuyerEmail}\n" +
                    "  CurrentUserId:    {CurrentUserId}\n" +
                    "  isAssignedToMe:   {IsAssigned}\n" +
                    "  QuotationCount:   {QuotationCount}\n" +
                    "  QuotationIds:     {QuotationIds}\n" +
                    "  LineItemCount:    {LineItemCount}\n" +
                    "  AreaApproverId:   {AreaApproverId}\n" +
                    "  FinalApproverId:  {FinalApproverId}",
                    first.RequestNumber,
                    first.RequestId,
                    first.RequestStatusCode,
                    first.RequestTypeCode,
                    first.BuyerId?.ToString() ?? "NULL",
                    first.BuyerName ?? "NULL",
                    first.BuyerEmail ?? "NULL",
                    currentUserId,
                    first.BuyerId.HasValue && first.BuyerId.Value == currentUserId,
                    first.Quotations?.Count ?? 0,
                    first.Quotations != null ? string.Join(", ", first.Quotations.Select(q => q.Id)) : "NONE",
                    reqGroup.Count(f => f.LineItemId.HasValue),
                    first.AreaApproverId?.ToString() ?? "NULL",
                    first.FinalApproverId?.ToString() ?? "NULL"
                );
            }
        }
        // ═══ END TEMPORARY DIAGNOSTIC ═══

        // Log performance metrics
        _logger.LogInformation($"Perf: GetLineItems total {swOverall.ElapsedMilliseconds}ms [Count: {swCount.ElapsedMilliseconds}ms, Page: {swPage.ElapsedMilliseconds}ms, Related: {swRelated.ElapsedMilliseconds}ms]. Rows: {finalItems.Count}");

        return Ok(new { Data = finalItems, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id}/check-last-pending")]
    public async Task<IActionResult> CheckLastPending(Guid id)
    {
        var item = await _context.RequestLineItems
            .AsNoTracking()
            .FirstOrDefaultAsync(li => li.Id == id);

        if (item == null) return NotFound();

        // Check if there are any OTHER items in the same request that are not RECEIVED and not CANCELLED
        var otherPendingItemsCount = await _context.RequestLineItems
            .CountAsync(li => li.RequestId == item.RequestId && 
                             li.Id != id && 
                             !li.IsDeleted &&
                             (li.LineItemStatus == null || (li.LineItemStatus.Code != "RECEIVED" && li.LineItemStatus.Code != "CANCELLED")));

        return Ok(new { isLastPending = otherPendingItemsCount == 0 });
    }


    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateLineItemStatusDto dto)
    {
        if (!CurrentUserRoles.Contains(RoleConstants.Buyer)) return Forbid("Apenas o papel de Comprador pode alterar o status.");

        var item = await _context.RequestLineItems
            .Include(li => li.Request)
                .ThenInclude(r => r.Status)
            .Include(li => li.LineItemStatus)
            .FirstOrDefaultAsync(li => li.Id == id);

        if (item == null) return NotFound();

        var newStatus = await _context.LineItemStatuses.FirstOrDefaultAsync(s => s.Code == dto.StatusCode);
        if (newStatus == null) return BadRequest("Status inválido.");

        // Mandatory comment validation for specific statuses
        var mandatoryCommentStatuses = new[] { "ORDERED", "PARTIALLY_RECEIVED", "RECEIVED" };
        if (mandatoryCommentStatuses.Contains(dto.StatusCode) && string.IsNullOrWhiteSpace(dto.Comment))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Observação Obrigatória",
                Detail = $"A observação é obrigatória para o status '{newStatus.Name}'.",
                Status = 400
            });
        }

        var oldStatusName = item.LineItemStatus?.Name ?? "Desconhecido";
        var request = item.Request;

        // Transition logic: PAYMENT_COMPLETED -> IN_FOLLOWUP
        if (request.Status!.Code == "PAYMENT_COMPLETED" || request.Status!.Code == "WAITING_RECEIPT")
        {
            var inFollowupStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == "IN_FOLLOWUP");
            if (inFollowupStatus != null)
            {
                request.StatusId = inFollowupStatus.Id;
                request.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        item.LineItemStatusId = newStatus.Id;
        item.UpdatedAtUtc = DateTime.UtcNow;

        // Authoritative Check for Request Completion
        bool shouldCompleteRequest = false;
        if (dto.StatusCode == "RECEIVED")
        {
            var otherPendingItemsCount = await _context.RequestLineItems
                .CountAsync(li => li.RequestId == request.Id && 
                                 li.Id != id && 
                                 !li.IsDeleted &&
                                 (li.LineItemStatus == null || (li.LineItemStatus.Code != "RECEIVED" && li.LineItemStatus.Code != "CANCELLED")));
            
            if (otherPendingItemsCount == 0)
            {
                shouldCompleteRequest = true;
            }
        }

        // Resolve Actor
        var actorId = CurrentUserId;

        // Record Item history in human-readable format
        var history = new AlplaPortal.Domain.Entities.RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = actorId,
            ActionTaken = "ITEM_STATUS_CHANGE",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"[Item #{item.LineNumber} - {item.Description}] Status alterado de \"{oldStatusName}\" para \"{newStatus.Name}\". Observação: {dto.Comment ?? "N/A"}",
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(history);

        // ── Phase 4C competing-writer consolidation ──
        // While the Phase 4 completion lifecycle is active, a GROUPED request's parent
        // completion belongs exclusively to RequestCompletionService — this legacy shortcut
        // (last item RECEIVED → request COMPLETED, bypassing invoice/fiscal-receipt
        // obligations) is suppressed and replaced by a delegation to the authoritative
        // engine: Phase 1 evaluates the item's group with the freshly tracked item state
        // (stamping the operational receipt through the lazy-derivation rule when the item
        // records prove full receipt), and Phase 2 runs after the save. Groupless legacy
        // requests, and every request while CompletionEnabled=false, keep the exact
        // pre-Phase-4 behaviour.
        var delegatedToPhase4 = false;
        if (shouldCompleteRequest &&
            _postPaymentOptions != null &&
            !PostPaymentCompletionPolicy.IsCompletionDisabled(_postPaymentOptions) &&
            await _context.RequestPoGroups.AnyAsync(g => g.RequestId == request.Id))
        {
            shouldCompleteRequest = false;
            delegatedToPhase4 = true;

            if (_completionService != null)
            {
                await _completionService.EvaluateGroupCompletionAsync(
                    request.Id, item.RequestPoGroupId, actorId);
            }
        }

        if (shouldCompleteRequest)
        {
            var completedStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == "COMPLETED");
            if (completedStatus != null)
            {
                var previousStatusId = request.StatusId;
                request.StatusId = completedStatus.Id;
                request.UpdatedAtUtc = DateTime.UtcNow;

                // Record Request Completion history
                var completionHistory = new AlplaPortal.Domain.Entities.RequestStatusHistory
                {
                    Id = Guid.NewGuid(),
                    RequestId = request.Id,
                    ActorUserId = actorId,
                    ActionTaken = "REQUEST_COMPLETED",
                    PreviousStatusId = previousStatusId,
                    NewStatusId = completedStatus.Id,
                    Comment = "Pedido finalizado automaticamente após recebimento do último item.",
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.RequestStatusHistories.Add(completionHistory);
            }
        }

        await _context.SaveChangesAsync();

        // Phase 2 strictly after the save (the caller-transaction rule): the parent completes
        // through the authoritative service or not at all. A failure here never fails the item
        // action — the next trigger or the recovery sweep re-evaluates.
        if (delegatedToPhase4 && _completionService != null)
        {
            try
            {
                await _completionService.EvaluateParentCompletionAsync(request.Id, actorId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Non-critical: parent completion evaluation failed after item status change on Request {RequestId}.",
                    request.Id);
            }
        }

        return NoContent();
    }

    [HttpPatch("{id}/supplier")]
    public async Task<IActionResult> UpdateSupplier(Guid id, [FromBody] UpdateLineItemSupplierDto dto)
    {
        if (!CurrentUserRoles.Contains(RoleConstants.Buyer)) return StatusCode(403, "Apenas o papel de Comprador pode alterar o fornecedor.");

        var item = await _context.RequestLineItems
            .Include(l => l.Request)
            .ThenInclude(r => r.RequestType)
            .FirstOrDefaultAsync(l => l.Id == id);
            
        if (item == null) return NotFound();

        if (item.Request?.RequestType?.Code == "PAYMENT")
            return Conflict(new ProblemDetails
            {
                Title = "Ação Inválida",
                Detail = "Itens de pedidos de pagamento herdam o fornecedor do pedido e não podem ter o fornecedor alterado individualmente.",
                Status = 409
            });

        if (dto.SupplierId.HasValue)
        {
            var supplier = await _context.Suppliers.FindAsync(dto.SupplierId.Value);
            if (supplier == null) return BadRequest("Fornecedor inválido.");
            
            item.SupplierId = supplier.Id;
            item.SupplierName = supplier.Name;
        }
        else
        {
            item.SupplierId = null;
            item.SupplierName = null;
        }

        item.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/currency")]
    public async Task<IActionResult> UpdateCurrency(Guid id, [FromBody] UpdateLineItemCurrencyDto dto)
    {
        var item = await _context.RequestLineItems.FindAsync(id);
        if (item == null) return NotFound();

        var request = await _context.Requests.Include(r => r.RequestType).FirstOrDefaultAsync(r => r.Id == item.RequestId);
        if (request?.RequestType?.Code == "PAYMENT")
            return StatusCode(403, "Moeda de itens de pagamento não pode ser editada individualmente.");

        if (dto.CurrencyId.HasValue)
        {
            var currency = await _context.Currencies.FindAsync(dto.CurrencyId.Value);
            if (currency == null) return BadRequest("Moeda inválida.");
            
            item.CurrencyId = currency.Id;
        }
        else
        {
            item.CurrencyId = null;
        }

        item.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/cost-center")]
    public async Task<IActionResult> UpdateCostCenter(Guid id, [FromBody] UpdateLineItemCostCenterDto dto)
    {
        var item = await _context.RequestLineItems.Include(li => li.Request).FirstOrDefaultAsync(li => li.Id == id);
        if (item == null) return NotFound();

        // Phase B: cost-center assignment is an area-review action — requires manager
        // titularity for the request's department/plant (DepartmentManager routing),
        // admin, or the legacy nominee. The manual role grants nothing.
        var costCenterActorId = CurrentUserId;
        var isAuthorized = CurrentUserRoles.Contains(RoleConstants.SystemAdministrator)
            || item.Request!.AreaApproverId == costCenterActorId
            || await _approvalRouting.IsAreaManagerAsync(costCenterActorId, item.Request!.DepartmentId, item.Request!.PlantId);
        if (!isAuthorized)
            return StatusCode(403, "Você não é responsável pelo departamento/planta deste pedido.");

        if (dto.CostCenterId.HasValue)
        {
            var cc = await _context.CostCenters.FindAsync(dto.CostCenterId.Value);
            if (cc == null) return BadRequest("Centro de Custo inválido.");
            
            item.CostCenterId = cc.Id;
        }
        else
        {
            item.CostCenterId = null;
        }

        item.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/receiving")]
    public async Task<IActionResult> UpdateReceiving(Guid id, [FromBody] UpdateItemReceivingDto dto)
    {
        // Real Role Check: allow both RECEIVING and BUYER
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Receiving) && !roles.Contains(RoleConstants.Buyer)) 
             return Forbid("Apenas o papel de Recebimento ou Comprador pode registrar recebimento.");

        var item = await _context.RequestLineItems
            .Include(li => li.RequestPoGroup)
            .Include(li => li.Request)
                .ThenInclude(r => r.Status)
            .Include(li => li.LineItemStatus)
            .FirstOrDefaultAsync(li => li.Id == id);

        if (item == null) return NotFound();

        // Validation: Group must be in a valid receiving status
        var allowedRequestStatuses = new[] { "PAYMENT_COMPLETED", "WAITING_RECEIPT", "IN_FOLLOWUP", RequestConstants.Statuses.WaitingSupplierDelivery };
        if (item.RequestPoGroup != null && !allowedRequestStatuses.Contains(item.RequestPoGroup.Status))
        {
            return Conflict(new ProblemDetails { Title = "Ação Bloqueada", Detail = $"O grupo P.O. não está em fase de recebimento. Status atual: {item.RequestPoGroup.Status}", Status = 409 });
        }
        else if (item.RequestPoGroup == null && !allowedRequestStatuses.Contains(item.Request.Status!.Code))
        {
            return Conflict(new ProblemDetails { Title = "Ação Bloqueada", Detail = "O pedido não está em fase de recebimento.", Status = 409 });
        }

        return await ProcessItemReceivingAsync(item, dto.ReceivedQuantity, dto.DivergenceNotes);
    }

    [HttpPatch("~/api/v1/quotation-items/{id}/receiving")]
    public async Task<IActionResult> UpdateQuotationItemReceiving(Guid id, [FromBody] UpdateItemReceivingDto dto)
    {
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Receiving) && !roles.Contains(RoleConstants.Buyer)) 
             return Forbid("Apenas o papel de Recebimento ou Comprador pode registrar recebimento.");

        var qi = await _context.QuotationItems
            .Include(q => q.Quotation)
                .ThenInclude(q => q.Request)
                    .ThenInclude(r => r.Status)
            .Include(q => q.LineItemStatus)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (qi == null) return NotFound();

        // Find the matching RequestLineItem to resolve the group
        var rli = await _context.RequestLineItems
            .Include(li => li.RequestPoGroup)
            .FirstOrDefaultAsync(li => li.SelectedQuotationItemId == qi.Id);

        var allowedRequestStatuses = new[] { "PAYMENT_COMPLETED", "WAITING_RECEIPT", "IN_FOLLOWUP", RequestConstants.Statuses.WaitingSupplierDelivery };
        if (rli != null && rli.RequestPoGroup != null && !allowedRequestStatuses.Contains(rli.RequestPoGroup.Status))
        {
            return Conflict(new ProblemDetails { Title = "Ação Bloqueada", Detail = $"O grupo P.O. não está em fase de recebimento. Status atual: {rli.RequestPoGroup.Status}", Status = 409 });
        }
        else if ((rli == null || rli.RequestPoGroup == null) && !allowedRequestStatuses.Contains(qi.Quotation.Request.Status!.Code))
        {
            return Conflict(new ProblemDetails { Title = "Ação Bloqueada", Detail = "O pedido não está em fase de recebimento.", Status = 409 });
        }

        return await ProcessQuotationItemReceivingAsync(qi, dto.ReceivedQuantity, dto.DivergenceNotes);
    }

    private async Task<IActionResult> ProcessItemReceivingAsync(AlplaPortal.Domain.Entities.RequestLineItem item, decimal receivedQty, string? notes)
    {
        var oldReceivedQty = item.ReceivedQuantity;
        item.ReceivedQuantity = receivedQty;
        item.DivergenceNotes = notes;
        item.UpdatedAtUtc = DateTime.UtcNow;

        // Determine Status
        string statusCode = "PENDING";
        if (receivedQty >= item.Quantity) statusCode = "RECEIVED";
        else if (receivedQty > 0) statusCode = "PARTIALLY_RECEIVED";

        var status = await _context.LineItemStatuses.FirstOrDefaultAsync(s => s.Code == statusCode);
        if (status != null) item.LineItemStatusId = status.Id;

        var actorId = CurrentUserId;
        
        // Granular History Registration
        var actionQty = receivedQty - oldReceivedQty;
        var unit = await _context.Units.FindAsync(item.UnitId);
        var unitCode = unit?.Code ?? "UN";
        var isTotalStr = receivedQty >= item.Quantity ? "TOTAL" : "PARCIAL";
        
        var formattedActionQty = QuantityFormatter.FormatQuantity(actionQty, unit);
        var formattedReceivedQty = QuantityFormatter.FormatQuantity(receivedQty, unit);
        var formattedAuthorizedQty = QuantityFormatter.FormatQuantity(item.Quantity, unit);

        var request = await _context.Requests.Include(r => r.Status).FirstOrDefaultAsync(r => r.Id == item.RequestId);
        if (request != null)
        {
            _context.RequestStatusHistories.Add(new AlplaPortal.Domain.Entities.RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = item.RequestId,
                ActorUserId = actorId,
                ActionTaken = "ITEM_RECEIVING_REGISTRATION",
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = $"[Recebimento Item #{item.LineNumber}] {item.Description}: {formattedActionQty} {unitCode} recebidos (Acumulado: {formattedReceivedQty} de {formattedAuthorizedQty} {unitCode}). [{isTotalStr}]. Obs: {notes ?? "N/A"}",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await SyncRequestStatusAfterReceivingAsync(item.RequestId, actorId);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<IActionResult> ProcessQuotationItemReceivingAsync(AlplaPortal.Domain.Entities.QuotationItem qi, decimal receivedQty, string? notes)
    {
        var oldReceivedQty = qi.ReceivedQuantity;
        qi.ReceivedQuantity = receivedQty;
        qi.DivergenceNotes = notes;

        // Determine Status
        string statusCode = "PENDING";
        if (receivedQty >= qi.Quantity) statusCode = "RECEIVED";
        else if (receivedQty > 0) statusCode = "PARTIALLY_RECEIVED";

        var status = await _context.LineItemStatuses.FirstOrDefaultAsync(s => s.Code == statusCode);
        if (status != null) qi.LineItemStatusId = status.Id;

        var actorId = CurrentUserId;
        // Granular History Registration
        var actionQty = receivedQty - oldReceivedQty;
        
        // Resolve unit from related RequestLineItem
        var relatedItem = await _context.RequestLineItems
            .Include(li => li.Unit)
            .FirstOrDefaultAsync(li => li.RequestId == qi.Quotation.RequestId && li.LineNumber == qi.LineNumber && !li.IsDeleted);
        
        var unit = relatedItem?.Unit;
        var unitCode = unit?.Code ?? "UN"; 
        var isTotalStr = receivedQty >= qi.Quantity ? "TOTAL" : "PARCIAL";
        
        var formattedActionQty = QuantityFormatter.FormatQuantity(actionQty, unit);
        var formattedReceivedQty = QuantityFormatter.FormatQuantity(receivedQty, unit);
        var formattedAuthorizedQty = QuantityFormatter.FormatQuantity(qi.Quantity, unit);

        var request = await _context.Requests.Include(r => r.Status).FirstOrDefaultAsync(r => r.Id == qi.Quotation.RequestId);
        if (request != null)
        {
            _context.RequestStatusHistories.Add(new AlplaPortal.Domain.Entities.RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = actorId,
                ActionTaken = "ITEM_RECEIVING_REGISTRATION",
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = $"[Recebimento Item #{qi.LineNumber}] {qi.Description}: {formattedActionQty} {unitCode} recebidos (Acumulado: {formattedReceivedQty} de {formattedAuthorizedQty} {unitCode}). [{isTotalStr}]. Obs: {notes ?? "N/A"}",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await SyncRequestStatusAfterReceivingAsync(qi.Quotation.RequestId, actorId);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task SyncRequestStatusAfterReceivingAsync(Guid requestId, Guid actorId)
    {
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
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null) return;

            // 1. Authoritative Status Determination
            string nextStatusCode = RequestWorkflowHelper.DeterminePostReceivingStatus(request);

            if (nextStatusCode != request.Status!.Code)
            {
                var targetStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == nextStatusCode);
                if (targetStatus != null)
                {
                    var oldStatusId = request.StatusId;
                    request.StatusId = targetStatus.Id;
                    request.UpdatedAtUtc = DateTime.UtcNow;
                    request.UpdatedByUserId = actorId;
                    
                    _context.RequestStatusHistories.Add(new AlplaPortal.Domain.Entities.RequestStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        RequestId = request.Id,
                        ActionTaken = "RECEIVING_PROGRESS",
                        PreviousStatusId = oldStatusId,
                        NewStatusId = targetStatus.Id,
                        Comment = nextStatusCode == "COMPLETED" ? "Pedido finalizado após recebimento total dos itens." : "Pedido em acompanhamento de recebimento.",
                        CreatedAtUtc = DateTime.UtcNow,
                        ActorUserId = actorId
                    });

                    await _context.SaveChangesAsync();
                }
            }
            
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw; // Re-throw to ensure the original operation also fails
        }
    }
}
