namespace AlplaPortal.Api.Controllers;

using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

[Authorize]
[ApiController]
[Route("api/v1/finance/budget")]
public class FinanceBudgetController : BaseController
{
    private readonly ILogger<FinanceBudgetController> _logger;

    public FinanceBudgetController(
        ApplicationDbContext context,
        ILogger<FinanceBudgetController> logger) : base(context)
    {
        _logger = logger;
    }

    private static readonly string[] CommittedStatuses = new[] 
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

    private void CalculateRequestLineConsumption(Request req, out Dictionary<int, decimal> ccCommitted, out Dictionary<int, decimal> ccPaid)
    {
        ccCommitted = new Dictionary<int, decimal>();
        ccPaid = new Dictionary<int, decimal>();

        if (!CommittedStatuses.Contains(req.Status?.Code)) return;

        bool isPaid = req.Status?.Code == RequestConstants.Statuses.Paid || 
                      req.Status?.Code == RequestConstants.Statuses.PaymentCompleted ||
                      req.Status?.Code == RequestConstants.Statuses.InFollowup ||
                      req.Status?.Code == RequestConstants.Statuses.Completed ||
                      req.ActualPaidAtUtc.HasValue;

        var selectedQuotation = req.SelectedQuotationId.HasValue 
            ? req.Quotations.FirstOrDefault(q => q.Id == req.SelectedQuotationId.Value) 
            : null;

        decimal reqLineItemsTotal = req.LineItems.Sum(li => li.TotalAmount);

        // Calculate amount per line item
        foreach (var line in req.LineItems)
        {
            decimal amountToAttribute = line.TotalAmount;

            if (selectedQuotation != null)
            {
                var qItem = selectedQuotation.Items.FirstOrDefault(qi => qi.LineNumber == line.LineNumber);
                if (qItem != null)
                {
                    amountToAttribute = qItem.LineTotal;
                }
                else if (reqLineItemsTotal > 0)
                {
                    // Proportional fallback
                    amountToAttribute = (line.TotalAmount / reqLineItemsTotal) * selectedQuotation.TotalAmount;
                }
                else
                {
                    // Edge case fallback
                    amountToAttribute = selectedQuotation.TotalAmount / req.LineItems.Count;
                }
            }

            int ccId = line.CostCenterId ?? 0;
            
            if (!ccCommitted.ContainsKey(ccId)) ccCommitted[ccId] = 0;
            ccCommitted[ccId] += amountToAttribute;

            if (isPaid)
            {
                if (!ccPaid.ContainsKey(ccId)) ccPaid[ccId] = 0;
                ccPaid[ccId] += amountToAttribute;
            }
        }
    }

    [HttpGet("overview/{year}")]
    public async Task<ActionResult<BudgetOverviewDto>> GetBudgetOverview(int year)
    {
        var scopedRequestsQuery = await GetScopedRequestsQuery();
        
        var annualBudgets = await _context.AnnualBudgets
            .Include(ab => ab.Department)
            .Include(ab => ab.Currency)
            .Where(ab => ab.Year == year && ab.IsActive && ab.Department!.IsActive)
            .ToListAsync();

        var requestsYearly = await scopedRequestsQuery
            .Include(r => r.Status)
            .Include(r => r.Currency)
            .Include(r => r.LineItems)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .Include(r => r.Department)
            .Where(r => r.CreatedAtUtc.Year == year && !r.IsCancelled && r.Department!.IsActive)
            .ToListAsync();

        var overview = new BudgetOverviewDto { Year = year };
        var budgetGrouped = annualBudgets.GroupBy(b => new { b.DepartmentId, b.CurrencyId }).ToList();

        foreach (var depGroup in budgetGrouped)
        {
            var dept = depGroup.First().Department!;
            var budgetRecord = depGroup.First();
            
            var deptRequests = requestsYearly.Where(r => r.DepartmentId == dept.Id).ToList();

            decimal totalCommitted = 0;
            decimal totalPaid = 0;

            foreach (var req in deptRequests)
            {
                var reqCurrencyId = req.CurrencyId ?? budgetRecord.CurrencyId;
                if (reqCurrencyId != budgetRecord.CurrencyId) continue;

                CalculateRequestLineConsumption(req, out var ccCommitted, out var ccPaid);
                totalCommitted += ccCommitted.Values.Sum();
                totalPaid += ccPaid.Values.Sum();
            }

            overview.Departments.Add(new BudgetDepartmentOverviewDto
            {
                DepartmentId = dept.Id,
                DepartmentName = dept.Name,
                TotalBudget = depGroup.Sum(b => b.TotalAmount),
                CommittedSpend = totalCommitted,
                PaidSpend = totalPaid,
                CurrencyId = budgetRecord.CurrencyId,
                CurrencyCode = budgetRecord.Currency?.Code ?? "AOA"
            });
        }

        return Ok(overview);
    }

    [HttpGet("department/{departmentId}/details/{year}")]
    public async Task<ActionResult<List<BudgetCostCenterDetailDto>>> GetCostCenterDetails(int departmentId, int year)
    {
        var scopedRequestsQuery = await GetScopedRequestsQuery();

        var requestsYearly = await scopedRequestsQuery
            .Include(r => r.Status)
            .Include(r => r.LineItems)
                .ThenInclude(li => li.CostCenter)
            .Include(r => r.Currency)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .Where(r => r.CreatedAtUtc.Year == year && r.DepartmentId == departmentId && !r.IsCancelled)
            .ToListAsync();

        var details = new List<BudgetCostCenterDetailDto>();

        var ccCommittedTotals = new Dictionary<int, decimal>();
        var ccPaidTotals = new Dictionary<int, decimal>();
        var ccNameLookup = new Dictionary<int, string>();
        string defaultCurrencyCode = "AOA";

        foreach (var req in requestsYearly)
        {
            if (!string.IsNullOrEmpty(req.Currency?.Code)) defaultCurrencyCode = req.Currency.Code;
            
            CalculateRequestLineConsumption(req, out var reqCommitted, out var reqPaid);
            
            foreach (var ccId in reqCommitted.Keys)
            {
                if (!ccCommittedTotals.ContainsKey(ccId)) ccCommittedTotals[ccId] = 0;
                if (!ccPaidTotals.ContainsKey(ccId)) ccPaidTotals[ccId] = 0;

                ccCommittedTotals[ccId] += reqCommitted[ccId];
                ccPaidTotals[ccId] += reqPaid.GetValueOrDefault(ccId, 0);

                if (!ccNameLookup.ContainsKey(ccId))
                {
                    if (ccId == 0) ccNameLookup[ccId] = "Não Alocado / Departamento Geral";
                    else
                    {
                        var ccEntity = req.LineItems.FirstOrDefault(li => li.CostCenterId == ccId)?.CostCenter;
                        ccNameLookup[ccId] = ccEntity?.Name ?? $"CC {ccId}";
                    }
                }
            }
        }

        foreach (var ccId in ccCommittedTotals.Keys)
        {
            details.Add(new BudgetCostCenterDetailDto
            {
                CostCenterId = ccId,
                CostCenterName = ccNameLookup[ccId],
                CommittedSpend = ccCommittedTotals[ccId],
                PaidSpend = ccPaidTotals[ccId],
                CurrencyCode = defaultCurrencyCode
            });
        }

        return Ok(details.OrderByDescending(d => d.CommittedSpend).ToList());
    }

    [HttpGet("department/{departmentId}/monthly/{year}")]
    public async Task<ActionResult<List<BudgetMonthlyDataDto>>> GetMonthlyBreakdown(int departmentId, int year)
    {
        var scopedRequestsQuery = await GetScopedRequestsQuery();

        var requestsYearly = await scopedRequestsQuery
            .Include(r => r.Status)
            .Include(r => r.LineItems)
                .ThenInclude(li => li.CostCenter)
            .Include(r => r.Currency)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .Where(r => r.CreatedAtUtc.Year == year && r.DepartmentId == departmentId && !r.IsCancelled)
            .ToListAsync();

        var monthLabels = new[] { "Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez" };
        var result = new List<BudgetMonthlyDataDto>();

        var ccNameLookup = new Dictionary<int, string>();
        foreach (var req in requestsYearly)
        {
            foreach (var li in req.LineItems)
            {
                int ccId = li.CostCenterId ?? 0;
                if (!ccNameLookup.ContainsKey(ccId))
                {
                    if (ccId == 0) ccNameLookup[ccId] = "Não Alocado / Departamento Geral";
                    else ccNameLookup[ccId] = li.CostCenter?.Name ?? $"CC {ccId}";
                }
            }
        }

        for (int month = 1; month <= 12; month++)
        {
            var monthEntry = new BudgetMonthlyDataDto
            {
                Month = month,
                MonthLabel = monthLabels[month - 1],
                CostCenters = new List<BudgetMonthlyCostCenterDto>()
            };

            var monthCcCommitted = new Dictionary<int, decimal>();
            var monthCcPaid = new Dictionary<int, decimal>();

            foreach (var req in requestsYearly)
            {
                bool reqCreatedInMonth = req.CreatedAtUtc.Month == month;
                
                int paidMonth = req.ActualPaidAtUtc?.Month ?? req.CreatedAtUtc.Month;
                bool reqPaidInMonth = paidMonth == month;

                if (!reqCreatedInMonth && !reqPaidInMonth) continue;

                CalculateRequestLineConsumption(req, out var ccCommitted, out var ccPaid);

                if (reqCreatedInMonth)
                {
                    foreach (var kvp in ccCommitted)
                    {
                        if (!monthCcCommitted.ContainsKey(kvp.Key)) monthCcCommitted[kvp.Key] = 0;
                        monthCcCommitted[kvp.Key] += kvp.Value;
                    }
                }

                if (reqPaidInMonth)
                {
                    foreach (var kvp in ccPaid)
                    {
                        if (!monthCcPaid.ContainsKey(kvp.Key)) monthCcPaid[kvp.Key] = 0;
                        monthCcPaid[kvp.Key] += kvp.Value;
                    }
                }
            }

            var allCcKeys = monthCcCommitted.Keys.Union(monthCcPaid.Keys).Distinct();

            foreach (var ccId in allCcKeys)
            {
                decimal committedForMonth = monthCcCommitted.GetValueOrDefault(ccId, 0);
                decimal paidForMonth = monthCcPaid.GetValueOrDefault(ccId, 0);

                if (committedForMonth > 0 || paidForMonth > 0)
                {
                    monthEntry.CostCenters.Add(new BudgetMonthlyCostCenterDto
                    {
                        CostCenterId = ccId,
                        CostCenterName = ccNameLookup.GetValueOrDefault(ccId, "Não Alocado"),
                        CommittedAmount = committedForMonth,
                        PaidAmount = paidForMonth
                    });
                }
            }

            result.Add(monthEntry);
        }

        return Ok(result);
    }

    [HttpGet("config/{year}")]
    public async Task<ActionResult<List<AnnualBudgetConfigDto>>> GetBudgetConfig(int year)
    {
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.SystemAdministrator) && !roles.Contains(RoleConstants.Finance))
        {
            return Forbid();
        }

        var budgets = await _context.AnnualBudgets
            .Include(b => b.Company)
            .Include(b => b.Plant)
            .Include(b => b.Department)
            .Include(b => b.CostCenter)
            .Include(b => b.Currency)
            .Where(b => b.Year == year)
            .ToListAsync();

        var configList = budgets.Select(budget => new AnnualBudgetConfigDto
        {
            Id = budget.Id,
            Year = budget.Year,
            CompanyId = budget.CompanyId,
            CompanyName = budget.Company?.Name,
            PlantId = budget.PlantId,
            PlantName = budget.Plant?.Name,
            DepartmentId = budget.DepartmentId,
            DepartmentName = budget.Department?.Name,
            CostCenterId = budget.CostCenterId,
            CostCenterName = budget.CostCenter?.Name,
            CurrencyId = budget.CurrencyId,
            CurrencyCode = budget.Currency?.Code,
            TotalAmount = budget.TotalAmount,
            IsActive = budget.IsActive
        }).OrderBy(c => c.CompanyName).ThenBy(c => c.PlantName).ThenBy(c => c.DepartmentName).ToList();

        return Ok(configList);
    }

    [HttpPost("config")]
    public async Task<ActionResult> SaveBudgetConfig([FromBody] List<AnnualBudgetConfigDto> configs)
    {
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.SystemAdministrator) && !roles.Contains(RoleConstants.Finance))
        {
            return Forbid();
        }

        foreach (var dto in configs)
        {
            // Explicit duplicate validation based on logical unique key
            var existing = await _context.AnnualBudgets
                .FirstOrDefaultAsync(b => 
                    b.Year == dto.Year && 
                    b.CompanyId == dto.CompanyId && 
                    b.PlantId == dto.PlantId && 
                    b.DepartmentId == dto.DepartmentId && 
                    b.CostCenterId == dto.CostCenterId && 
                    b.CurrencyId == dto.CurrencyId);
            
            if (existing != null)
            {
                existing.TotalAmount = dto.TotalAmount;
                existing.IsActive = dto.IsActive;
            }
            else
            {
                _context.AnnualBudgets.Add(new AnnualBudget
                {
                    Year = dto.Year,
                    CompanyId = dto.CompanyId,
                    PlantId = dto.PlantId,
                    DepartmentId = dto.DepartmentId,
                    CostCenterId = dto.CostCenterId,
                    CurrencyId = dto.CurrencyId,
                    TotalAmount = dto.TotalAmount,
                    IsActive = dto.IsActive
                });
            }
        }

        await _context.SaveChangesAsync();
        return Ok();
    }
}
