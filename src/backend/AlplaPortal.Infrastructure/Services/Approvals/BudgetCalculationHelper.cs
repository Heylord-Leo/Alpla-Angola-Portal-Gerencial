using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AlplaPortal.Infrastructure.Services.Approvals;

/// <summary>
/// Shared helper for budget availability calculations across the approval intelligence service
/// and the wizard budget preview endpoint.
/// </summary>
public static class BudgetCalculationHelper
{
    /// <summary>
    /// Committed statuses aligned with FinanceBudgetController.CommittedStatuses.
    /// </summary>
    public static readonly string[] BudgetCommittedStatuses = new[]
    {
        RequestConstants.Statuses.FinalApproved,
        RequestConstants.Statuses.WaitingCostCenter,
        RequestConstants.Statuses.QuotationCompleted,
        RequestConstants.Statuses.PoRequested,
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentRequestSent,
        RequestConstants.Statuses.PaymentScheduled,
        RequestConstants.Statuses.Paid,
        RequestConstants.Statuses.PaymentCompleted,
        RequestConstants.Statuses.InFollowup,
        RequestConstants.Statuses.Completed,
        RequestConstants.Statuses.WaitingPoCorrection
    };

    /// <summary>
    /// Calculates the committed amount for a given department (and optionally cost center),
    /// excluding the current request to prevent double-counting.
    /// </summary>
    public static async Task<decimal> CalculateCommittedForBudgetLineAsync(
        ApplicationDbContext context,
        int departmentId, 
        int? costCenterId, 
        int currencyId, 
        int fiscalYear, 
        Guid? excludeRequestId)
    {
        var query = context.Requests
            .Include(r => r.Status)
            .Include(r => r.LineItems)
                .ThenInclude(li => li.Allocations)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .Where(r => r.DepartmentId == departmentId
                     && r.CurrencyId == currencyId
                     && r.CreatedAtUtc.Year == fiscalYear
                     && !r.IsCancelled
                     && BudgetCommittedStatuses.Contains(r.Status!.Code));

        if (excludeRequestId.HasValue)
        {
            query = query.Where(r => r.Id != excludeRequestId.Value);
        }

        var committedRequests = await query.ToListAsync();

        decimal total = 0;
        foreach (var req in committedRequests)
        {
            if (costCenterId.HasValue)
            {
                // Only sum line items attributed to this specific cost center
                var selectedQ = req.SelectedQuotationId.HasValue
                    ? req.Quotations.FirstOrDefault(q => q.Id == req.SelectedQuotationId.Value)
                    : null;

                foreach (var li in req.LineItems.Where(li => li.CostCenterId == costCenterId.Value))
                {
                    decimal amount = li.TotalAmount;
                    if (selectedQ != null)
                    {
                        var qItem = selectedQ.Items.FirstOrDefault(qi => qi.LineNumber == li.LineNumber);
                        if (qItem != null) amount = qItem.LineTotal;
                    }

                    if (li.Allocations != null && li.Allocations.Any())
                    {
                        var allocs = li.Allocations.Select(a => (a.Id, a.Percentage, a.AllocationOrder)).ToList();
                        var distributed = AllocationHelper.DistributeAmount(amount, allocs);
                        var targetAlloc = li.Allocations.FirstOrDefault(a => a.CostCenterId == costCenterId.Value);
                        if (targetAlloc != null)
                        {
                            var distAmount = distributed.FirstOrDefault(d => d.AllocationId == targetAlloc.Id).Amount;
                            total += distAmount;
                        }
                    }
                    else if (li.CostCenterId == costCenterId.Value)
                    {
                        total += amount;
                    }
                }
            }
            else
            {
                // Department-level: sum all line items
                total += GetRequestEffectiveAmount(req);
            }
        }
        return total;
    }

    /// <summary>
    /// Gets the effective total amount for a request, preferring the selected quotation total.
    /// </summary>
    public static decimal GetRequestEffectiveAmount(Request request)
    {
        if (request.SelectedQuotationId.HasValue)
        {
            var selectedQ = request.Quotations.FirstOrDefault(q => q.Id == request.SelectedQuotationId.Value);
            if (selectedQ != null) return selectedQ.TotalAmount;
        }
        return request.EstimatedTotalAmount;
    }

    /// <summary>
    /// Derives the budget status label based on the utilization percentage.
    /// Thresholds match the existing Finance intelligence dashboard logic.
    /// </summary>
    public static string DeriveStatus(decimal utilizationPercent)
    {
        return utilizationPercent > 100 ? "EXCEEDED"
             : utilizationPercent > 90  ? "CRITICAL"
             : utilizationPercent > 75  ? "WARNING"
             : "OK";
    }

    /// <summary>
    /// Maps the backend budget status to the wizard UI status vocabulary.
    /// </summary>
    public static string MapToWizardStatus(string intelligenceStatus)
    {
        return intelligenceStatus switch
        {
            "OK" => "SAFE",
            "WARNING" => "WARNING",
            "CRITICAL" => "CRITICAL",
            "EXCEEDED" => "OVER_BUDGET",
            _ => "SAFE"
        };
    }

    public static BudgetAvailabilityDto BuildResult(
        AnnualBudget budgetLine,
        decimal committed,
        decimal currentRequestAmount,
        string matchLevel,
        int fiscalYear,
        string currencyCode,
        Request request)
    {
        decimal availBefore = budgetLine.TotalAmount - committed;
        decimal availAfter = availBefore - currentRequestAmount;
        decimal utilPct = budgetLine.TotalAmount > 0
            ? ((committed + currentRequestAmount) / budgetLine.TotalAmount) * 100
            : 0;

        return new BudgetAvailabilityDto
        {
            HasBudgetConfig = true,
            MatchLevel = matchLevel,
            AnnualBudget = budgetLine.TotalAmount,
            CommittedAmount = committed,
            AvailableBefore = availBefore,
            CurrentRequestAmount = currentRequestAmount,
            AvailableAfter = availAfter,
            CurrencyCode = currencyCode,
            DepartmentName = request.Department?.Name,
            CostCenterName = budgetLine.CostCenter?.Name ?? "Orçamento Geral",
            PlantName = request.Plant?.Name ?? budgetLine.Plant?.Name,
            CompanyName = request.Company?.Name ?? budgetLine.Company?.Name,
            FiscalYear = fiscalYear,
            Status = DeriveStatus(utilPct),
            UtilizationPercent = Math.Round(utilPct, 1)
        };
    }

    public static async Task<string> EvaluateOverallBudgetStatusAsync(
        ApplicationDbContext context, 
        Request request, 
        Dictionary<Guid, Guid>? itemAwards, 
        Dictionary<Guid, ItemApprovalAssignmentDto>? itemAssignments,
        Dictionary<Guid, List<ItemAllocationLineDto>>? itemAllocations)
    {
        int fiscalYear = request.CreatedAtUtc.Year;
        int currencyId = request.CurrencyId ?? 0;

        var allocations = new Dictionary<(int PlantId, int? CostCenterId), decimal>();

        foreach (var li in request.LineItems.Where(l => !l.IsDeleted))
        {
            decimal amount = li.TotalAmount;
            if (itemAwards != null && itemAwards.TryGetValue(li.Id, out var qItemId))
            {
                var qItem = request.Quotations.SelectMany(q => q.Items).FirstOrDefault(qi => qi.Id == qItemId);
                if (qItem != null) amount = qItem.LineTotal;
            }

            if (itemAllocations != null && itemAllocations.TryGetValue(li.Id, out var allocLines) && allocLines.Any())
            {
                // Multi-allocation 
                var allocs = allocLines.Select(a => (Guid.NewGuid(), a.Percentage, 0)).ToList();
                var distributed = AllocationHelper.DistributeAmount(amount, allocs);
                
                for (int i = 0; i < allocLines.Count; i++)
                {
                    var a = allocLines[i];
                    var key = (a.PlantId, (int?)a.CostCenterId);
                    if (!allocations.ContainsKey(key)) allocations[key] = 0;
                    
                    var distAmount = distributed.FirstOrDefault(d => d.AllocationId == allocs[i].Item1).Amount;
                    allocations[key] += distAmount;
                }
            }
            else
            {
                // Legacy / Single assignment
                int plantId = request.PlantId ?? 0;
                int? costCenterId = li.CostCenterId;

                if (itemAssignments != null && itemAssignments.TryGetValue(li.Id, out var assign))
                {
                    plantId = assign.PlantId ?? plantId;
                    costCenterId = assign.CostCenterId;
                }
                else if (li.PlantId.HasValue)
                {
                    plantId = li.PlantId.Value;
                }

                var key = (plantId, costCenterId);
                if (!allocations.ContainsKey(key)) allocations[key] = 0;
                allocations[key] += amount;
            }
        }

        bool hasNoBudget = false;
        bool hasCritical = false;
        bool hasOverBudget = false;
        bool hasWarning = false;

        foreach (var kvp in allocations)
        {
            var budgetLine = await context.AnnualBudgets
                .FirstOrDefaultAsync(b => b.DepartmentId == request.DepartmentId 
                                       && b.CostCenterId == kvp.Key.CostCenterId 
                                       && b.Year == fiscalYear 
                                       && b.CurrencyId == currencyId);

            if (budgetLine == null)
            {
                hasNoBudget = true;
                continue;
            }

            decimal alreadyConsumed = await CalculateCommittedForBudgetLineAsync(
                context, request.DepartmentId, kvp.Key.CostCenterId, currencyId, fiscalYear, request.Id);

            decimal projectedConsumed = alreadyConsumed + kvp.Value;
            decimal projectedUsagePercent = budgetLine.TotalAmount > 0 
                ? (projectedConsumed / budgetLine.TotalAmount) * 100 
                : 0;

            string status = MapToWizardStatus(DeriveStatus(projectedUsagePercent));

            if (status == "OVER_BUDGET") hasOverBudget = true;
            else if (status == "CRITICAL") hasCritical = true;
            else if (status == "WARNING") hasWarning = true;
        }

        if (hasNoBudget) return "NO_BUDGET";
        if (hasOverBudget) return "OVER_BUDGET";
        if (hasCritical) return "CRITICAL";
        if (hasWarning) return "WARNING";
        return "SAFE";
    }
}
