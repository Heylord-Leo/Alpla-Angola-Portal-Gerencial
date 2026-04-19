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
        RequestConstants.Statuses.WaitingPoCorrection
    };

    [HttpGet("overview/{year}")]
    public async Task<ActionResult<BudgetOverviewDto>> GetBudgetOverview(int year)
    {
        var scopedRequestsQuery = await GetScopedRequestsQuery();
        
        // Obter orçamentos anuais e departamentos ativos
        var annualBudgets = await _context.AnnualBudgets
            .Include(ab => ab.Department)
            .Include(ab => ab.Currency)
            .Where(ab => ab.Year == year && ab.Department!.IsActive)
            .ToListAsync();

        // Limita a busca das requests no mesmo ano
        var requestsYearly = await scopedRequestsQuery
            .Include(r => r.Currency)
            .Include(r => r.Quotations)
            .Include(r => r.Department)
            .Where(r => r.CreatedAtUtc.Year == year && !r.IsCancelled && r.Department!.IsActive)
            .ToListAsync();

        var overview = new BudgetOverviewDto { Year = year };

        // Processa departamentos que possuem budget cadastrado
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
                // Only aggregate if the request's native currency matches the budget line's currency, 
                // OR if it's the fallback default to avoid double counting across multiple currency budgets
                var reqCurrencyId = req.CurrencyId ?? budgetRecord.CurrencyId;
                if (reqCurrencyId != budgetRecord.CurrencyId) continue;

                var amount = req.SelectedQuotationId.HasValue 
                    ? req.Quotations.FirstOrDefault(q => q.Id == req.SelectedQuotationId.Value)?.TotalAmount ?? req.EstimatedTotalAmount
                    : req.EstimatedTotalAmount;

                // Em MVP: soma tudo. Ideamente aqui teria mock conversão se a moeda da req != moeda do budget
                if (CommittedStatuses.Contains(req.Status?.Code))
                {
                    totalCommitted += amount;

                    if (req.Status?.Code == RequestConstants.Statuses.Paid || 
                        req.Status?.Code == RequestConstants.Statuses.PaymentCompleted ||
                        req.Status?.Code == RequestConstants.Statuses.InFollowup ||
                        req.ActualPaidAtUtc.HasValue)
                    {
                        totalPaid += amount;
                    }
                }
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
            .Include(r => r.LineItems)
                .ThenInclude(li => li.CostCenter)
            .Include(r => r.Currency)
            .Include(r => r.Quotations)
            .Where(r => r.CreatedAtUtc.Year == year && r.DepartmentId == departmentId && !r.IsCancelled)
            .ToListAsync();

        var details = new List<BudgetCostCenterDetailDto>();

        // Agrupa requests: se houver CC mapeado no primeiro item usa-o, caso contrário usa 0 ("Não Alocado")
        var costCenters = requestsYearly
            .GroupBy(r => r.LineItems.FirstOrDefault(li => li.CostCenterId.HasValue)?.CostCenterId ?? 0)
            .ToList();

        foreach (var ccGroup in costCenters)
        {
            var ccId = ccGroup.Key;
            string ccName = "Não Alocado / Múltiplos";
            
            if (ccId != 0)
            {
                var firstLineItemWithCc = ccGroup.First().LineItems.FirstOrDefault(li => li.CostCenterId == ccId);
                if (firstLineItemWithCc != null && firstLineItemWithCc.CostCenter != null)
                {
                    ccName = firstLineItemWithCc.CostCenter.Name;
                }
            }

            decimal ccCommitted = 0;
            decimal ccPaid = 0;

            foreach(var req in ccGroup)
            {
                var amount = req.SelectedQuotationId.HasValue 
                    ? req.Quotations.FirstOrDefault(q => q.Id == req.SelectedQuotationId.Value)?.TotalAmount ?? req.EstimatedTotalAmount
                    : req.EstimatedTotalAmount;

                if (CommittedStatuses.Contains(req.Status?.Code))
                {
                    ccCommitted += amount;

                    if (req.Status?.Code == RequestConstants.Statuses.Paid || 
                        req.Status?.Code == RequestConstants.Statuses.PaymentCompleted ||
                        req.Status?.Code == RequestConstants.Statuses.InFollowup ||
                        req.ActualPaidAtUtc.HasValue)
                    {
                        ccPaid += amount;
                    }
                }
            }

            details.Add(new BudgetCostCenterDetailDto
            {
                CostCenterId = ccId,
                CostCenterName = ccName,
                CommittedSpend = ccCommitted,
                PaidSpend = ccPaid,
                CurrencyCode = ccGroup.First().Currency?.Code ?? "AOA"
            });
        }

        return Ok(details.OrderByDescending(d => d.CommittedSpend).ToList());
    }

    [HttpGet("config/{year}")]
    public async Task<ActionResult<List<AnnualBudgetConfigDto>>> GetBudgetConfig(int year)
    {
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.SystemAdministrator) && !roles.Contains(RoleConstants.Finance))
        {
            return Forbid();
        }

        var departments = await _context.Departments.Where(d => d.IsActive).ToListAsync();
        var budgets = await _context.AnnualBudgets
            .Include(b => b.Currency)
            .Where(b => b.Year == year)
            .ToListAsync();

        var configList = new List<AnnualBudgetConfigDto>();
        // Default currency if none, pick first or id=1
        var defaultCurrency = await _context.Currencies.FirstOrDefaultAsync();
        
        foreach (var dept in departments)
        {
            var budget = budgets.FirstOrDefault(b => b.DepartmentId == dept.Id);
            if (budget != null)
            {
                configList.Add(new AnnualBudgetConfigDto
                {
                    Id = budget.Id,
                    Year = budget.Year,
                    DepartmentId = dept.Id,
                    DepartmentName = dept.Name,
                    CurrencyId = budget.CurrencyId,
                    CurrencyCode = budget.Currency?.Code,
                    TotalAmount = budget.TotalAmount
                });
            }
            else
            {
                configList.Add(new AnnualBudgetConfigDto
                {
                    Id = 0,
                    Year = year,
                    DepartmentId = dept.Id,
                    DepartmentName = dept.Name,
                    CurrencyId = defaultCurrency?.Id ?? 1,
                    CurrencyCode = defaultCurrency?.Code ?? "AOA",
                    TotalAmount = 0
                });
            }
        }

        return Ok(configList.OrderBy(c => c.DepartmentName).ToList());
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
            var existing = await _context.AnnualBudgets
                .FirstOrDefaultAsync(b => b.Year == dto.Year && b.DepartmentId == dto.DepartmentId && b.CurrencyId == dto.CurrencyId);
            
            if (existing != null)
            {
                existing.TotalAmount = dto.TotalAmount;
            }
            else if (dto.TotalAmount > 0)
            {
                _context.AnnualBudgets.Add(new AnnualBudget
                {
                    Year = dto.Year,
                    DepartmentId = dto.DepartmentId,
                    CurrencyId = dto.CurrencyId,
                    TotalAmount = dto.TotalAmount
                });
            }
        }

        await _context.SaveChangesAsync();
        return Ok();
    }
}
