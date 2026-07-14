using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Api.Controllers;

[Route("api/v1/requests")]
[ApiController]
[Authorize(Roles = RoleConstants.AreaApprover + "," + RoleConstants.FinalApprover + "," + RoleConstants.SystemAdministrator)]
public class BudgetPreviewController : BaseController
{
    private readonly ILogger<BudgetPreviewController> _logger;

    public BudgetPreviewController(ApplicationDbContext context, ILogger<BudgetPreviewController> logger) : base(context)
    {
        _logger = logger;
    }

    [HttpPost("{id:guid}/budget-preview")]
    public async Task<ActionResult<BudgetPreviewResponseDto>> PreviewBudget(Guid id, [FromBody] BudgetPreviewRequestDto dto)
    {
        // 1. Fetch Request with scoped access check
        var request = await (await GetScopedRequestsQuery())
            .Include(r => r.LineItems)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .Include(r => r.Department)
            .Include(r => r.Company)
            .Include(r => r.Plant)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null)
        {
            var globalRequest = await _context.Requests
                .Include(r => r.Department)
                .Include(r => r.Plant)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (globalRequest != null)
            {
                _logger.LogWarning("BudgetPreview: 403 Forbidden. Scope validation failed. UserId: {UserId}, UserRoles: {UserRoles}, RequestId: {RequestId}, Status: {Status}, Dept: {Dept}, Plant: {Plant}", 
                    CurrentUserId, string.Join(",", CurrentUserRoles), id, globalRequest.Status, globalRequest.Department?.Name, globalRequest.Plant?.Name);
                return StatusCode(403, "Você não tem permissão para realizar esta ação neste escopo.");
            }

            return NotFound("Solicitação não encontrada.");
        }

        int fiscalYear = request.CreatedAtUtc.Year;
        int currencyId = request.CurrencyId ?? 0;
        string currencyCode = "AOA";
        var curr = await _context.Currencies.FirstOrDefaultAsync(c => c.Id == currencyId);
        if (curr != null) currencyCode = curr.Code;

        var response = new BudgetPreviewResponseDto();

        // 2. Aggregate preview data grouped by Plant & CostCenter (fallback to null)
        var allocations = new Dictionary<(int PlantId, int? CostCenterId), BudgetAllocationPreviewDto>();
        decimal totalRequestAmount = 0;
        
        var previewLineItems = request.LineItems.Where(li => !li.IsDeleted).ToList();

        // ── Batch-scoped path ──
        // When BatchId is set, scope to items in that batch only.
        // Winners come from ApprovalBatchItem.SelectedQuotationItemId (dto.ItemAwards is ignored).
        decimal approvedBatchesConsumedAdjustment = 0;

        if (dto.BatchId.HasValue)
        {
            var batch = await _context.ApprovalBatches
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.Id == dto.BatchId.Value && b.RequestId == id);

            if (batch == null)
                return NotFound("Lote não encontrado.");

            // Scope previewLineItems to batch items only
            var batchRliIds = batch.Items.Select(bi => bi.RequestLineItemId).ToHashSet();
            previewLineItems = previewLineItems.Where(li => batchRliIds.Contains(li.Id)).ToList();

            // Override ItemAwards from batch items
            dto.ItemAwards = batch.Items.ToDictionary(bi => bi.RequestLineItemId, bi => bi.SelectedQuotationItemId);

            // Compute consumed adjustment: sum approved amounts from other approved batches of the same request
            var otherApprovedBatches = await _context.ApprovalBatches
                .Include(b => b.Items)
                .Where(b => b.RequestId == id
                    && b.Id != dto.BatchId.Value
                    && b.Status == RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval)
                .ToListAsync();

            if (otherApprovedBatches.Any())
            {
                var approvedBatchItemQiIds = otherApprovedBatches
                    .SelectMany(b => b.Items)
                    .Select(bi => bi.SelectedQuotationItemId)
                    .Distinct()
                    .ToList();

                var approvedQiAmounts = await _context.QuotationItems
                    .Where(qi => approvedBatchItemQiIds.Contains(qi.Id))
                    .SumAsync(qi => qi.LineTotal);

                approvedBatchesConsumedAdjustment = approvedQiAmounts;
            }
        }

        // ── Process ExtraItemDecisions (both batch-scoped and full-request paths) ──
        // Extras are decided by the Area Approver during Area Approval.
        // APPROVE → include as virtual preview item; REJECT → exclude.
        // No persistence: these are preview-only and not saved until Area Approval submit.
        if (dto.ExtraItemDecisions != null && dto.ExtraItemDecisions.Any())
        {
            var extraIds = dto.ExtraItemDecisions.Keys.ToList();
            var extraQItems = request.Quotations.SelectMany(q => q.Items).Where(qi => extraIds.Contains(qi.Id)).ToList();
            foreach (var qi in extraQItems)
            {
                if (dto.ExtraItemDecisions[qi.Id].Decision == "APPROVE")
                {
                    var fakeId = qi.Id; // Use QuotationItemId as ID to match frontend's logic
                    previewLineItems.Add(new Domain.Entities.RequestLineItem
                    {
                        Id = fakeId,
                        LineNumber = 900 + previewLineItems.Count,
                        Description = "[Item Adicional] " + qi.Description,
                        TotalAmount = qi.LineTotal,
                        CostCenterId = null,
                        PlantId = null
                    });
                    
                    dto.ItemAwards ??= new Dictionary<Guid, Guid>();
                    dto.ItemAwards[fakeId] = qi.Id;
                }
            }
        }

        foreach (var li in previewLineItems)
        {
            // Determine award amount
            decimal amount = li.TotalAmount;
            if (dto.ItemAwards != null && dto.ItemAwards.TryGetValue(li.Id, out var qItemId))
            {
                var qItem = request.Quotations.SelectMany(q => q.Items).FirstOrDefault(qi => qi.Id == qItemId);
                if (qItem != null) amount = qItem.LineTotal;
            }
            else if (request.SelectedQuotationId.HasValue)
            {
                var selectedQ = request.Quotations.FirstOrDefault(q => q.Id == request.SelectedQuotationId.Value);
                if (selectedQ != null)
                {
                    var qItem = selectedQ.Items.FirstOrDefault(qi => qi.LineNumber == li.LineNumber);
                    if (qItem != null) amount = qItem.LineTotal;
                }
            }

            // Determine Assignments & Allocations (Plant & CostCenter)
            if (dto.ItemAllocations != null && dto.ItemAllocations.TryGetValue(li.Id, out var allocLines) && allocLines.Any())
            {
                var allocs = allocLines.Select(a => (Guid.NewGuid(), a.Percentage, 0)).ToList();
                var distributed = AllocationHelper.DistributeAmount(amount, allocs);

                for (int i = 0; i < allocLines.Count; i++)
                {
                    var a = allocLines[i];
                    var key = (a.PlantId, (int?)a.CostCenterId);
                    
                    if (!allocations.ContainsKey(key))
                    {
                        var plant = await _context.Plants.FirstOrDefaultAsync(p => p.Id == a.PlantId);
                        var costCenter = a.CostCenterId > 0 ? await _context.CostCenters.FirstOrDefaultAsync(c => c.Id == a.CostCenterId) : null;

                        allocations[key] = new BudgetAllocationPreviewDto
                        {
                            PlantId = a.PlantId,
                            PlantName = plant?.Name ?? "Desconhecida",
                            CompanyId = plant?.CompanyId ?? request.CompanyId,
                            DepartmentId = request.DepartmentId,
                            DepartmentName = request.Department?.Name ?? "",
                            CostCenterId = a.CostCenterId > 0 ? a.CostCenterId : null,
                            CostCenterName = costCenter?.Name ?? "Orçamento Geral do Departamento",
                            CostCenterCode = costCenter?.Code ?? "",
                            CurrencyCode = currencyCode,
                            FiscalYear = fiscalYear
                        };
                    }

                    var distAmount = distributed.FirstOrDefault(d => d.AllocationId == allocs[i].Item1).Amount;
                    allocations[key].ThisRequestAmount += distAmount;
                    allocations[key].Items.Add(new BudgetAllocationItemDto
                    {
                        RequestLineItemId = li.Id,
                        LineNumber = li.LineNumber,
                        Description = li.Description,
                        SupplierName = "", // Could enrich from quotation
                        Amount = distAmount
                    });
                }
            }
            else
            {
                int plantId = request.PlantId ?? 0;
                int? costCenterId = li.CostCenterId;

                if (dto.ItemAssignments != null && dto.ItemAssignments.TryGetValue(li.Id, out var assign))
                {
                    plantId = assign.PlantId ?? plantId;
                    costCenterId = assign.CostCenterId;
                }
                else if (li.PlantId.HasValue)
                {
                    plantId = li.PlantId.Value;
                }

                var key = (plantId, costCenterId);
                if (!allocations.ContainsKey(key))
                {
                    var plant = await _context.Plants.FirstOrDefaultAsync(p => p.Id == plantId);
                    var costCenter = costCenterId.HasValue ? await _context.CostCenters.FirstOrDefaultAsync(c => c.Id == costCenterId.Value) : null;

                    allocations[key] = new BudgetAllocationPreviewDto
                    {
                        PlantId = plantId,
                        PlantName = plant?.Name ?? "Desconhecida",
                        CompanyId = plant?.CompanyId ?? request.CompanyId,
                        DepartmentId = request.DepartmentId,
                        DepartmentName = request.Department?.Name ?? "",
                        CostCenterId = costCenterId,
                        CostCenterName = costCenter?.Name ?? "Orçamento Geral do Departamento",
                        CostCenterCode = costCenter?.Code ?? "",
                        CurrencyCode = currencyCode,
                        FiscalYear = fiscalYear
                    };
                }

                allocations[key].ThisRequestAmount += amount;
                allocations[key].Items.Add(new BudgetAllocationItemDto
                {
                    RequestLineItemId = li.Id,
                    LineNumber = li.LineNumber,
                    Description = li.Description,
                    SupplierName = "",
                    Amount = amount
                });
            }

            totalRequestAmount += amount;
        }

        // 3. Process each allocation vs existing Budget config
        decimal overallBudget = 0;
        decimal overallConsumed = 0;

        foreach (var kvp in allocations)
        {
            var alloc = kvp.Value;
            
            // Check budget line
            var budgetLine = await _context.AnnualBudgets
                .FirstOrDefaultAsync(b => b.DepartmentId == alloc.DepartmentId 
                                       && b.CostCenterId == alloc.CostCenterId 
                                       && b.Year == fiscalYear 
                                       && b.CurrencyId == currencyId);

            if (budgetLine == null)
            {
                alloc.Status = "NO_BUDGET";
                alloc.Warnings.Add("Não há orçamento configurado para esta combinação.");
            }
            else
            {
                alloc.AnnualBudget = budgetLine.TotalAmount;
                overallBudget += budgetLine.TotalAmount;

                alloc.AlreadyConsumed = await BudgetCalculationHelper.CalculateCommittedForBudgetLineAsync(
                    _context, alloc.DepartmentId, alloc.CostCenterId, currencyId, fiscalYear, request.Id);

                // When batch-scoped: CalculateCommittedForBudgetLineAsync excludes the current request entirely.
                // We need to add back consumed amounts from other approved batches of this same request.
                alloc.AlreadyConsumed += approvedBatchesConsumedAdjustment;

                overallConsumed += alloc.AlreadyConsumed;

                alloc.ProjectedConsumed = alloc.AlreadyConsumed + alloc.ThisRequestAmount;
                alloc.ProjectedBalance = alloc.AnnualBudget - alloc.ProjectedConsumed;
                alloc.ProjectedUsagePercent = alloc.AnnualBudget > 0 
                    ? (alloc.ProjectedConsumed / alloc.AnnualBudget) * 100 
                    : 0;

                alloc.Status = BudgetCalculationHelper.MapToWizardStatus(
                    BudgetCalculationHelper.DeriveStatus(alloc.ProjectedUsagePercent)
                );
            }

            response.Allocations.Add(alloc);
        }

        // 4. Summarize Overall Status
        response.Summary.ThisRequestAmount = totalRequestAmount;
        response.Summary.TotalBudget = overallBudget;
        response.Summary.AlreadyConsumed = overallConsumed;
        response.Summary.ProjectedBalance = overallBudget - (overallConsumed + totalRequestAmount);
        response.Summary.ProjectedUsagePercent = overallBudget > 0 
            ? ((overallConsumed + totalRequestAmount) / overallBudget) * 100 
            : 0;
        response.Summary.CurrencyCode = currencyCode;
        response.Summary.FiscalYear = fiscalYear;

        bool hasNoBudget = response.Allocations.Any(a => a.Status == "NO_BUDGET");
        bool hasCritical = response.Allocations.Any(a => a.Status == "CRITICAL" || a.Status == "OVER_BUDGET");

        if (hasNoBudget)
        {
            response.Summary.OverallStatus = "NO_BUDGET";
            response.Summary.ExecutiveSummary = "Algumas rubricas não possuem orçamento configurado. Uma justificativa será exigida.";
            response.RequiresJustification = true;
        }
        else if (hasCritical)
        {
            string worstStatus = response.Allocations.Any(a => a.Status == "OVER_BUDGET") ? "OVER_BUDGET" : "CRITICAL";
            response.Summary.OverallStatus = worstStatus;
            response.Summary.ExecutiveSummary = "O orçamento ficará em estado crítico ou estourado. Uma justificativa será exigida.";
            response.RequiresJustification = true;
        }
        else
        {
            bool hasWarning = response.Allocations.Any(a => a.Status == "WARNING");
            response.Summary.OverallStatus = hasWarning ? "WARNING" : "SAFE";
            response.Summary.ExecutiveSummary = hasWarning 
                ? "Atenção: O orçamento de algumas rubricas ultrapassará 75%."
                : "Orçamento disponível e dentro dos limites seguros.";
            response.RequiresJustification = false;
        }

        // 5. Calculate Alternative Budget Lines
        var isSystemAdmin = CurrentUserRoles.Contains(RoleConstants.SystemAdministrator);
        var allowedPlantIds = new List<int>();
        
        if (!isSystemAdmin)
        {
            allowedPlantIds = await _context.UserPlantScopes
                .Where(s => s.UserId == CurrentUserId)
                .Select(s => s.PlantId)
                .ToListAsync();
        }

        var alternativeQuery = _context.AnnualBudgets
            .Include(b => b.Company)
            .Include(b => b.Plant)
            .Include(b => b.Department)
            .Include(b => b.CostCenter)
            .Where(b => b.IsActive 
                     && b.CompanyId == request.CompanyId 
                     && b.DepartmentId == request.DepartmentId 
                     && b.Year == fiscalYear 
                     && b.CurrencyId == currencyId);

        if (!isSystemAdmin && allowedPlantIds.Any())
        {
            alternativeQuery = alternativeQuery.Where(b => allowedPlantIds.Contains(b.PlantId));
        }

        var alternativeBudgets = await alternativeQuery.ToListAsync();

        foreach (var budgetLine in alternativeBudgets)
        {
            // Skip if this CostCenter is already part of the primary allocations
            var isAlreadyAllocated = response.Allocations.Any(a => a.PlantId == budgetLine.PlantId && a.CostCenterId == budgetLine.CostCenterId);
            if (isAlreadyAllocated) continue;

            var alt = new AlternativeBudgetLineDto
            {
                CompanyId = budgetLine.CompanyId,
                CompanyName = budgetLine.Company?.Name ?? "",
                PlantId = budgetLine.PlantId,
                PlantName = budgetLine.Plant?.Name ?? "",
                DepartmentId = budgetLine.DepartmentId,
                DepartmentName = budgetLine.Department?.Name ?? "",
                CostCenterId = budgetLine.CostCenterId,
                CostCenterCode = budgetLine.CostCenter?.Code ?? "",
                CostCenterName = budgetLine.CostCenter?.Name ?? "Orçamento Geral do Departamento",
                CurrencyCode = currencyCode,
                FiscalYear = fiscalYear,
                AnnualBudget = budgetLine.TotalAmount
            };

            alt.AlreadyConsumed = await BudgetCalculationHelper.CalculateCommittedForBudgetLineAsync(
                _context, alt.DepartmentId, alt.CostCenterId, currencyId, fiscalYear, request.Id);

            alt.AvailableBefore = alt.AnnualBudget - alt.AlreadyConsumed;
            
            // Note: Since this is a flat list, we assume the total request amount for projection.
            // The frontend may recalculate this based on the specific allocation group if desired.
            alt.ProjectedBalanceIfApplied = alt.AvailableBefore - totalRequestAmount;
            alt.ProjectedUsagePercentIfApplied = alt.AnnualBudget > 0 
                ? ((alt.AlreadyConsumed + totalRequestAmount) / alt.AnnualBudget) * 100 
                : 0;

            alt.Status = BudgetCalculationHelper.MapToWizardStatus(
                BudgetCalculationHelper.DeriveStatus(alt.ProjectedUsagePercentIfApplied)
            );

            response.AlternativeBudgetLines.Add(alt);
        }

        response.AlternativeBudgetLines = response.AlternativeBudgetLines
            .OrderBy(a => a.Status == "SAFE" ? 0 : a.Status == "WARNING" ? 1 : a.Status == "CRITICAL" ? 2 : 3)
            .ThenByDescending(a => a.ProjectedBalanceIfApplied)
            .ToList();

        return Ok(response);
    }
}
