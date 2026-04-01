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
}
