using System;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/approvals")]
public class ApprovalIntelligenceController : BaseController
{
    private readonly IApprovalIntelligenceService _intelligenceService;

    public ApprovalIntelligenceController(
        ApplicationDbContext context,
        IApprovalIntelligenceService intelligenceService) : base(context)
    {
        _intelligenceService = intelligenceService;
    }

    [HttpGet("{id}/intelligence")]
    public async Task<ActionResult<ApprovalIntelligenceDto>> GetIntelligence(Guid id)
    {
        var intelligence = await _intelligenceService.GetIntelligenceAsync(id);
        
        if (intelligence == null)
        {
            return NotFound();
        }
        
        return Ok(intelligence);
    }

    [HttpGet("{id}/items/{lineItemId}/history")]
    public async Task<ActionResult<List<HistoricalPurchaseRecordDto>>> GetItemHistory(Guid id, Guid lineItemId)
    {
        var history = await _intelligenceService.GetItemHistoryAsync(id, lineItemId);
        return Ok(history);
    }

    [HttpGet("{id}/finance-trend")]
    public async Task<ActionResult<ApprovalFinancialTrendDto>> GetFinanceTrend(Guid id, [FromQuery] string resolution = "MONTH", [FromQuery] string scope = "PLANT")
    {
        var trend = await _intelligenceService.GetFinancialTrendAsync(id, resolution, scope);
        return Ok(trend);
    }
}
