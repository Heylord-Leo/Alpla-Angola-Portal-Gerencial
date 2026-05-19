using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace AlplaPortal.Infrastructure.Services.Approvals;

public class ApprovalIntelligenceService : IApprovalIntelligenceService
{
    private readonly ApplicationDbContext _context;

    public ApprovalIntelligenceService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApprovalIntelligenceDto> GetIntelligenceAsync(Guid requestId)
    {
        var request = await _context.Requests
            .Include(r => r.LineItems)
                .ThenInclude(li => li.CostCenter)
            .Include(r => r.Department)
            .Include(r => r.Currency)
            .Include(r => r.Plant)
            .Include(r => r.Company)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return new ApprovalIntelligenceDto { RequestId = requestId };

        var result = new ApprovalIntelligenceDto
        {
            RequestId = requestId
        };

        // 1. Department Context
        result.DepartmentContext = await GetDepartmentIntelligenceAsync(request);

        // 2. Item Intelligence
        foreach (var item in request.LineItems.Where(li => !li.IsDeleted))
        {
            var itemIntel = await GetItemIntelligenceAsync(item, request.Currency?.Code);
            result.Items.Add(itemIntel);
        }

        // 3. Budget Availability
        result.BudgetAvailability = await GetBudgetAvailabilityAsync(request);

        // 4. Overall Alerts (Aggregated from items + global rules)
        result.OverallAlerts = GenerateOverallAlerts(result, request);

        return result;
    }

    private async Task<DepartmentIntelligenceDto> GetDepartmentIntelligenceAsync(Domain.Entities.Request request)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearStart = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var validStatuses = new[] 
        { 
            RequestConstants.Statuses.FinalApproved,
            RequestConstants.Statuses.WaitingCostCenter,
            RequestConstants.Statuses.QuotationCompleted,
            RequestConstants.Statuses.PoIssued,
            RequestConstants.Statuses.PaymentRequestSent,
            RequestConstants.Statuses.PaymentScheduled,
            RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed,
            RequestConstants.Statuses.WaitingPoCorrection
        };

        var deptApprovedRequests = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.Currency)
            .Where(r => r.DepartmentId == request.DepartmentId && 
                        validStatuses.Contains(r.Status!.Code) &&
                        r.CreatedAtUtc >= yearStart)
            .Select(r => new { r.CreatedAtUtc, r.EstimatedTotalAmount, CurrencyCode = r.Currency!.Code })
            .ToListAsync();

        var monthTotal = deptApprovedRequests
            .Where(r => r.CreatedAtUtc >= monthStart && r.CurrencyCode == request.Currency?.Code)
            .Sum(r => r.EstimatedTotalAmount);

        var yearTotal = deptApprovedRequests
            .Where(r => r.CurrencyCode == request.Currency?.Code)
            .Sum(r => r.EstimatedTotalAmount);

        var monthCount = deptApprovedRequests
            .Count(r => r.CreatedAtUtc >= monthStart);

        return new DepartmentIntelligenceDto
        {
            MonthAccumulatedTotal = monthTotal,
            YearAccumulatedTotal = yearTotal,
            MonthApprovedCount = monthCount,
            CurrentRequestSharePercentage = (monthTotal + request.EstimatedTotalAmount) > 0 
                ? (request.EstimatedTotalAmount / (monthTotal + request.EstimatedTotalAmount)) * 100 
                : 100,
            Currency = request.Currency?.Code ?? string.Empty
        };
    }

    private async Task<ItemIntelligenceDto> GetItemIntelligenceAsync(Domain.Entities.RequestLineItem item, string? currentCurrency)
    {
        var normalizedDescription = (item.Description ?? string.Empty).Trim().ToLower();
        
        var validStatuses = new[] 
        { 
            RequestConstants.Statuses.FinalApproved,
            RequestConstants.Statuses.WaitingCostCenter,
            RequestConstants.Statuses.QuotationCompleted,
            RequestConstants.Statuses.PoIssued,
            RequestConstants.Statuses.PaymentRequestSent,
            RequestConstants.Statuses.PaymentScheduled,
            RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed,
            RequestConstants.Statuses.WaitingPoCorrection
        };

        // Lookup historical items with the same normalized description, same currency, and from approved requests
        var historicalItems = await _context.RequestLineItems
            .Include(li => li.Request)
                .ThenInclude(r => r.Status)
            .Include(li => li.Request.Currency)
            .Where(li => !li.IsDeleted && 
                         li.Description.Trim().ToLower() == normalizedDescription &&
                         validStatuses.Contains(li.Request.Status!.Code) &&
                         li.Request.Currency!.Code == currentCurrency &&
                         li.RequestId != item.RequestId)
            .OrderByDescending(li => li.Request.CreatedAtUtc)
            .Select(li => new { li.UnitPrice, li.SupplierName, CreatedAt = li.Request.CreatedAtUtc })
            .ToListAsync();

        if (!historicalItems.Any())
        {
            return new ItemIntelligenceDto
            {
                LineItemId = item.Id,
                Description = item.Description ?? string.Empty,
                CurrentUnitPrice = item.UnitPrice,
                Currency = currentCurrency ?? string.Empty,
                HasHistory = false
            };
        }

        var lastPurchase = historicalItems.First();
        var avgPrice = historicalItems.Average(li => li.UnitPrice);

        return new ItemIntelligenceDto
        {
            LineItemId = item.Id,
            Description = item.Description ?? string.Empty,
            CurrentUnitPrice = item.UnitPrice,
            Currency = currentCurrency ?? string.Empty,
            HasHistory = true,
            LastPaidPrice = lastPurchase.UnitPrice,
            AverageHistoricalPrice = avgPrice,
            LastSupplierName = lastPurchase.SupplierName,
            TotalPurchaseCount = historicalItems.Count,
            VariationVsLastPercentage = lastPurchase.UnitPrice > 0 ? ((item.UnitPrice - lastPurchase.UnitPrice) / lastPurchase.UnitPrice) * 100 : null,
            VariationVsAvgPercentage = avgPrice > 0 ? ((item.UnitPrice - avgPrice) / avgPrice) * 100 : null
        };
    }

    private List<DecisionAlertDto> GenerateOverallAlerts(ApprovalIntelligenceDto intel, Domain.Entities.Request request)
    {
        var alerts = new List<DecisionAlertDto>();

        // 1. Price Alerts (Thresholds as per Phase 3A: >10% vs Avg, >5% vs Last)
        foreach (var item in intel.Items.Where(i => i.HasHistory))
        {
            if (item.VariationVsAvgPercentage > 10)
            {
                alerts.Add(new DecisionAlertDto
                {
                    Type = "PRC_HIGH_AVG",
                    Level = "WARNING",
                    Message = $"Item '{item.Description}': Preço {item.VariationVsAvgPercentage:F1}% acima da média histórica.",
                    RelatedItemId = item.LineItemId
                });
            }

            if (item.VariationVsLastPercentage > 5)
            {
                alerts.Add(new DecisionAlertDto
                {
                    Type = "PRC_HIGH_LAST",
                    Level = "WARNING",
                    Message = $"Item '{item.Description}': Preço subiu {item.VariationVsLastPercentage:F1}% vs última compra.",
                    RelatedItemId = item.LineItemId
                });
            }
        }

        // 2. Duplicate Check heuristic 
        // For 3A: duplicated description by same department in last 30 days
        // We'll skip complex date sub-filtering here to maintain focus, but metrics are available.

        // 3. CC Missing 
        if (request.LineItems.Any(li => li.CostCenterId == null))
        {
            alerts.Add(new DecisionAlertDto
            {
                Type = "CC_MISSING",
                Level = "CRITICAL",
                Message = "Pendente de alocação de Centro de Custo em itens.",
            });
        }

        return alerts;
    }

    public async Task<List<HistoricalPurchaseRecordDto>> GetItemHistoryAsync(Guid requestId, Guid lineItemId)
    {
        var item = await _context.RequestLineItems
            .Include(li => li.Request)
                .ThenInclude(r => r.Currency)
            .FirstOrDefaultAsync(li => li.Id == lineItemId);

        if (item == null) return new List<HistoricalPurchaseRecordDto>();

        var normalizedDescription = (item.Description ?? string.Empty).Trim().ToLower();
        var currentCurrency = item.Request.Currency?.Code;

        var validStatuses = new[] 
        { 
            RequestConstants.Statuses.FinalApproved,
            RequestConstants.Statuses.WaitingCostCenter,
            RequestConstants.Statuses.QuotationCompleted,
            RequestConstants.Statuses.PoIssued,
            RequestConstants.Statuses.PaymentRequestSent,
            RequestConstants.Statuses.PaymentScheduled,
            RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed,
            RequestConstants.Statuses.WaitingPoCorrection
        };

        var historicalItems = await _context.RequestLineItems
            .Include(li => li.Request)
                .ThenInclude(r => r.Status)
            .Include(li => li.Request)
                .ThenInclude(r => r.Currency)
            .Include(li => li.Request)
                .ThenInclude(r => r.Plant)
            .Include(li => li.Request)
                .ThenInclude(r => r.Department)
            .Where(li => !li.IsDeleted && 
                         li.Description.Trim().ToLower() == normalizedDescription &&
                         validStatuses.Contains(li.Request.Status!.Code) &&
                         li.Request.Currency!.Code == currentCurrency &&
                         li.RequestId != requestId)
            .OrderByDescending(li => li.Request.CreatedAtUtc)
            .Take(15)
            .ToListAsync();

        if (!historicalItems.Any()) return new List<HistoricalPurchaseRecordDto>();

        var lastPurchaseId = historicalItems.First().Id;

        return historicalItems.Select(li => new HistoricalPurchaseRecordDto
        {
            RequestId = li.RequestId,
            RequestNumber = li.Request.RequestNumber ?? string.Empty,
            PurchaseDate = li.Request.CreatedAtUtc,
            SupplierName = li.SupplierName ?? "N/D",
            UnitPrice = li.UnitPrice,
            Currency = li.Request.Currency!.Code,
            IsLastPurchase = li.Id == lastPurchaseId,
            IsUsedInAverage = true, // Currently all same-description items are used in avg
            MatchType = "APPROX", // Since it's description based
            PlantName = li.Request.Plant?.Name,
            DepartmentName = li.Request.Department?.Name
        }).ToList();
    }

    public async Task<ApprovalFinancialTrendDto> GetFinancialTrendAsync(Guid requestId, string resolution, string scope)
    {
        var request = await _context.Requests
            .Include(r => r.Currency)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return new ApprovalFinancialTrendDto();

        var query = _context.Requests
            .Include(r => r.Status)
            .Include(r => r.StatusHistories)
                .ThenInclude(sh => sh.NewStatus)
            .Where(r => r.DepartmentId == request.DepartmentId && r.CurrencyId == request.CurrencyId && !r.IsCancelled);

        if (scope == "PLANT" && request.PlantId.HasValue)
        {
            query = query.Where(r => r.PlantId == request.PlantId);
        }

        var validStatuses = new[] 
        { 
            RequestConstants.Statuses.FinalApproved,
            RequestConstants.Statuses.PoIssued,
            RequestConstants.Statuses.PaymentRequestSent,
            RequestConstants.Statuses.PaymentScheduled,
            RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.AreaAdjustment,
            RequestConstants.Statuses.FinalAdjustment,
            RequestConstants.Statuses.InFollowup,
            RequestConstants.Statuses.Completed,
            RequestConstants.Statuses.WaitingCostCenter,
            RequestConstants.Statuses.WaitingPoCorrection
        };

        var paidStatuses = new[] 
        { 
            RequestConstants.Statuses.Paid,
            RequestConstants.Statuses.PaymentCompleted,
            RequestConstants.Statuses.Completed
        };

        // Needs to have been approved
        query = query.Where(r => validStatuses.Contains(r.Status!.Code));

        var historicalData = await query
            .Select(r => new 
            {
                r.Id,
                r.EstimatedTotalAmount,
                CurrentStatusCode = r.Status!.Code,
                ApprovedDate = r.StatusHistories
                    .Where(sh => sh.NewStatus.Code == RequestConstants.Statuses.FinalApproved)
                    .Select(sh => (DateTime?)sh.CreatedAtUtc)
                    .OrderBy(d => d)
                    .FirstOrDefault() ?? r.CreatedAtUtc
            })
            .ToListAsync();

        var points = new List<FinancialTrendPointDto>();
        var now = DateTime.UtcNow;

        if (resolution == "WEEK")
        {
            var cutoff = now.AddDays(-7 * 12); // Last 12 weeks
            var filtered = historicalData.Where(d => d.ApprovedDate >= cutoff);
            
            var grouped = filtered.GroupBy(d => {
                int diff = (7 + (d.ApprovedDate.DayOfWeek - DayOfWeek.Monday)) % 7;
                return d.ApprovedDate.AddDays(-1 * diff).Date;
            });

            foreach (var g in grouped.OrderBy(g => g.Key))
            {
                points.Add(new FinancialTrendPointDto
                {
                    PeriodLabel = $"Semana {ISOWeek.GetWeekOfYear(g.Key)} do ano",
                    SortDate = g.Key,
                    ApprovedAmount = g.Where(x => !paidStatuses.Contains(x.CurrentStatusCode)).Sum(x => x.EstimatedTotalAmount),
                    ApprovedCount = g.Count(x => !paidStatuses.Contains(x.CurrentStatusCode)),
                    PaidAmount = g.Where(x => paidStatuses.Contains(x.CurrentStatusCode)).Sum(x => x.EstimatedTotalAmount),
                    PaidCount = g.Count(x => paidStatuses.Contains(x.CurrentStatusCode))
                });
            }
        }
        else // MONTH
        {
            var cutoff = now.AddMonths(-12); // Last 12 months
            var filtered = historicalData.Where(d => d.ApprovedDate >= new DateTime(cutoff.Year, cutoff.Month, 1));

            var grouped = filtered.GroupBy(d => new { d.ApprovedDate.Year, d.ApprovedDate.Month });

            foreach (var g in grouped.OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
            {
                points.Add(new FinancialTrendPointDto
                {
                    PeriodLabel = $"{g.Key.Month:D2}/{g.Key.Year}",
                    SortDate = new DateTime(g.Key.Year, g.Key.Month, 1),
                    ApprovedAmount = g.Where(x => !paidStatuses.Contains(x.CurrentStatusCode)).Sum(x => x.EstimatedTotalAmount),
                    ApprovedCount = g.Count(x => !paidStatuses.Contains(x.CurrentStatusCode)),
                    PaidAmount = g.Where(x => paidStatuses.Contains(x.CurrentStatusCode)).Sum(x => x.EstimatedTotalAmount),
                    PaidCount = g.Count(x => paidStatuses.Contains(x.CurrentStatusCode))
                });
            }
        }

        return new ApprovalFinancialTrendDto
        {
            RequestId = requestId,
            Currency = request.Currency?.Code ?? string.Empty,
            Resolution = resolution,
            Scope = scope,
            DataPoints = points
        };
    }

    // ─── Budget Availability Calculation ─────────────────────────────────────

    /// <summary>
    /// Committed statuses aligned with FinanceBudgetController.CommittedStatuses.
    /// </summary>
    private static readonly string[] BudgetCommittedStatuses = new[]
    {
        RequestConstants.Statuses.FinalApproved,
        RequestConstants.Statuses.WaitingCostCenter,
        RequestConstants.Statuses.QuotationCompleted,
        RequestConstants.Statuses.PoIssued,
        RequestConstants.Statuses.PaymentRequestSent,
        RequestConstants.Statuses.PaymentScheduled,
        RequestConstants.Statuses.Paid,
        RequestConstants.Statuses.PaymentCompleted,
        RequestConstants.Statuses.InFollowup,
        RequestConstants.Statuses.Completed,
        RequestConstants.Statuses.WaitingPoCorrection
    };

    private async Task<BudgetAvailabilityDto> GetBudgetAvailabilityAsync(Domain.Entities.Request request)
    {
        var fiscalYear = DateTime.UtcNow.Year;
        var currencyCode = request.Currency?.Code ?? "AOA";

        // Determine distinct cost centers from request line items
        var lineItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
        var distinctCCIds = lineItems.Select(li => li.CostCenterId).Distinct().ToList();

        // Load all budget lines for this department/company/plant/currency/year
        var budgetLines = await _context.AnnualBudgets
            .Include(ab => ab.CostCenter)
            .Include(ab => ab.Department)
            .Include(ab => ab.Plant)
            .Include(ab => ab.Company)
            .Include(ab => ab.Currency)
            .Where(ab => ab.Year == fiscalYear
                      && ab.IsActive
                      && ab.DepartmentId == request.DepartmentId
                      && ab.CompanyId == request.CompanyId)
            .ToListAsync();

        // No budget configured at all
        if (!budgetLines.Any())
        {
            return new BudgetAvailabilityDto
            {
                HasBudgetConfig = false,
                MatchLevel = "NONE",
                FiscalYear = fiscalYear,
                CurrencyCode = currencyCode,
                DepartmentName = request.Department?.Name,
                PlantName = request.Plant?.Name,
                CompanyName = request.Company?.Name,
                InfoMessage = "Orçamento não configurado para esta combinação."
            };
        }

        // Check currency match — budget may be in a different currency
        var matchingCurrencyBudgets = budgetLines
            .Where(ab => ab.Currency?.Code == currencyCode)
            .ToList();

        if (!matchingCurrencyBudgets.Any())
        {
            var budgetCurrency = budgetLines.First().Currency?.Code ?? "N/D";
            return new BudgetAvailabilityDto
            {
                HasBudgetConfig = true,
                MatchLevel = "NONE",
                FiscalYear = fiscalYear,
                CurrencyCode = currencyCode,
                DepartmentName = request.Department?.Name,
                PlantName = request.Plant?.Name,
                CompanyName = request.Company?.Name,
                Status = "WARNING",
                InfoMessage = $"Moeda do pedido ({currencyCode}) diferente da moeda do orçamento ({budgetCurrency})."
            };
        }

        // Optionally narrow by plant
        var plantBudgets = request.PlantId.HasValue
            ? matchingCurrencyBudgets.Where(ab => ab.PlantId == request.PlantId.Value).ToList()
            : matchingCurrencyBudgets;

        if (!plantBudgets.Any()) plantBudgets = matchingCurrencyBudgets;

        // Determine the current request's total amount
        decimal currentRequestAmount = GetRequestEffectiveAmount(request);

        // Route to the appropriate calculation strategy
        if (distinctCCIds.Count == 1)
        {
            // Single cost center (or all items with no CC)
            return await CalculateSingleCCBudget(request, plantBudgets, distinctCCIds[0], currentRequestAmount, fiscalYear, currencyCode);
        }
        else
        {
            // Multiple cost centers — provide summary + per-CC breakdown
            return await CalculateMultiCCBudget(request, plantBudgets, distinctCCIds, lineItems, currentRequestAmount, fiscalYear, currencyCode);
        }
    }

    private async Task<BudgetAvailabilityDto> CalculateSingleCCBudget(
        Domain.Entities.Request request,
        List<Domain.Entities.AnnualBudget> plantBudgets,
        int? costCenterId,
        decimal currentRequestAmount,
        int fiscalYear,
        string currencyCode)
    {
        // Try to find budget line for the specific cost center
        Domain.Entities.AnnualBudget? budgetLine = null;
        string matchLevel = "NONE";

        if (costCenterId.HasValue)
        {
            budgetLine = plantBudgets.FirstOrDefault(ab => ab.CostCenterId == costCenterId.Value);
            if (budgetLine != null) matchLevel = "COST_CENTER";
        }

        // Fallback: department general (CostCenterId = null)
        if (budgetLine == null)
        {
            budgetLine = plantBudgets.FirstOrDefault(ab => ab.CostCenterId == null);
            if (budgetLine != null) matchLevel = "DEPARTMENT_GENERAL";
        }

        if (budgetLine == null)
        {
            return new BudgetAvailabilityDto
            {
                HasBudgetConfig = false,
                MatchLevel = "NONE",
                FiscalYear = fiscalYear,
                CurrencyCode = currencyCode,
                CurrentRequestAmount = currentRequestAmount,
                DepartmentName = request.Department?.Name,
                PlantName = request.Plant?.Name,
                CompanyName = request.Company?.Name,
                InfoMessage = "Orçamento não configurado para esta combinação."
            };
        }

        // Calculate committed amount (excluding current request)
        decimal committed = await CalculateCommittedForBudgetLine(
            budgetLine.DepartmentId,
            matchLevel == "COST_CENTER" ? costCenterId : null,
            request.CurrencyId ?? budgetLine.CurrencyId,
            fiscalYear,
            request.Id);

        return BuildResult(budgetLine, committed, currentRequestAmount, matchLevel, fiscalYear, currencyCode, request);
    }

    private async Task<BudgetAvailabilityDto> CalculateMultiCCBudget(
        Domain.Entities.Request request,
        List<Domain.Entities.AnnualBudget> plantBudgets,
        List<int?> distinctCCIds,
        List<Domain.Entities.RequestLineItem> lineItems,
        decimal currentRequestAmount,
        int fiscalYear,
        string currencyCode)
    {
        // Aggregate: use department-general budget as the summary level
        var generalBudget = plantBudgets.FirstOrDefault(ab => ab.CostCenterId == null);
        var totalBudget = generalBudget?.TotalAmount ?? plantBudgets.Sum(ab => ab.TotalAmount);

        // Calculate total committed across all CCs for this department (excluding current request)
        decimal totalCommitted = await CalculateCommittedForBudgetLine(
            request.DepartmentId, null, request.CurrencyId ?? 0, fiscalYear, request.Id);

        // Build per-CC breakdown
        var breakdown = new List<BudgetCostCenterBreakdownDto>();
        foreach (var ccId in distinctCCIds)
        {
            var ccItems = lineItems.Where(li => li.CostCenterId == ccId).ToList();
            decimal ccRequestAmount = ccItems.Sum(li => li.TotalAmount);
            var ccBudgetLine = ccId.HasValue
                ? plantBudgets.FirstOrDefault(ab => ab.CostCenterId == ccId.Value)
                : generalBudget;

            decimal ccCommitted = ccBudgetLine != null
                ? await CalculateCommittedForBudgetLine(request.DepartmentId, ccId, request.CurrencyId ?? 0, fiscalYear, request.Id)
                : 0;

            decimal ccAnnual = ccBudgetLine?.TotalAmount ?? 0;
            decimal ccAvailAfter = ccAnnual - ccCommitted - ccRequestAmount;
            decimal ccUtil = ccAnnual > 0 ? ((ccCommitted + ccRequestAmount) / ccAnnual) * 100 : 0;

            breakdown.Add(new BudgetCostCenterBreakdownDto
            {
                CostCenterId = ccId,
                CostCenterName = ccId.HasValue
                    ? (ccItems.FirstOrDefault()?.CostCenter?.Name ?? $"CC {ccId}")
                    : "Não Alocado",
                HasBudgetLine = ccBudgetLine != null,
                AnnualBudget = ccAnnual,
                CommittedAmount = ccCommitted,
                RequestAmountInCC = ccRequestAmount,
                AvailableAfter = ccAvailAfter,
                Status = DeriveStatus(ccUtil),
                UtilizationPercent = Math.Round(ccUtil, 1)
            });
        }

        decimal availBefore = totalBudget - totalCommitted;
        decimal availAfter = availBefore - currentRequestAmount;
        decimal utilPct = totalBudget > 0 ? ((totalCommitted + currentRequestAmount) / totalBudget) * 100 : 0;

        return new BudgetAvailabilityDto
        {
            HasBudgetConfig = true,
            MatchLevel = "MULTI_CC",
            AnnualBudget = totalBudget,
            CommittedAmount = totalCommitted,
            AvailableBefore = availBefore,
            CurrentRequestAmount = currentRequestAmount,
            AvailableAfter = availAfter,
            CurrencyCode = currencyCode,
            DepartmentName = request.Department?.Name,
            CostCenterName = "Múltiplos CCs",
            PlantName = request.Plant?.Name,
            CompanyName = request.Company?.Name,
            FiscalYear = fiscalYear,
            Status = DeriveStatus(utilPct),
            UtilizationPercent = Math.Round(utilPct, 1),
            CostCenterBreakdown = breakdown
        };
    }

    /// <summary>
    /// Calculates the committed amount for a given department (and optionally cost center),
    /// excluding the current request to prevent double-counting.
    /// </summary>
    private async Task<decimal> CalculateCommittedForBudgetLine(
        int departmentId, int? costCenterId, int currencyId, int fiscalYear, Guid excludeRequestId)
    {
        var committedRequests = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .Where(r => r.DepartmentId == departmentId
                     && r.CurrencyId == currencyId
                     && r.CreatedAtUtc.Year == fiscalYear
                     && !r.IsCancelled
                     && r.Id != excludeRequestId
                     && BudgetCommittedStatuses.Contains(r.Status!.Code))
            .ToListAsync();

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
                    total += amount;
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
    private static decimal GetRequestEffectiveAmount(Domain.Entities.Request request)
    {
        if (request.SelectedQuotationId.HasValue)
        {
            var selectedQ = request.Quotations.FirstOrDefault(q => q.Id == request.SelectedQuotationId.Value);
            if (selectedQ != null) return selectedQ.TotalAmount;
        }
        return request.EstimatedTotalAmount;
    }

    private static BudgetAvailabilityDto BuildResult(
        Domain.Entities.AnnualBudget budgetLine,
        decimal committed,
        decimal currentRequestAmount,
        string matchLevel,
        int fiscalYear,
        string currencyCode,
        Domain.Entities.Request request)
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

    private static string DeriveStatus(decimal utilizationPercent)
    {
        return utilizationPercent > 100 ? "EXCEEDED"
             : utilizationPercent > 90  ? "CRITICAL"
             : utilizationPercent > 75  ? "WARNING"
             : "OK";
    }
}
