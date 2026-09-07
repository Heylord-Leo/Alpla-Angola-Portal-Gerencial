using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Receiving;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Canonical group-level Receiving queue (B4). Additive, READ-ONLY. Returns one row per
/// Receiving-actionable <c>RequestPoGroup</c> (actionability from <see cref="ReceivingActionEvaluator"/>)
/// plus the same count summary the Dashboard uses — so the Dashboard Receiving section and this list
/// reconcile exactly at the group level (which the request-scalar Receiving workspace could not do).
/// No mutation here; the existing MoveToReceipt/ConfirmReceiving endpoints remain the only writers.
/// Scoped by RequestAccessScope (plant/dept; SysAdmin bypass).
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/receiving/queue")]
public class ReceivingQueueController : BaseController
{
    public ReceivingQueueController(ApplicationDbContext context) : base(context) { }

    /// <param name="actionableOnly">Kept for a stable filter contract; the queue is actionable by construction.</param>
    /// <param name="bucket">Optional single bucket: READY_FOR_RECEIPT | WAITING_RECEIPT | IN_FOLLOWUP | WAITING_SUPPLIER_DELIVERY.</param>
    [HttpGet]
    public async Task<ActionResult<ReceivingQueueResponseDto>> GetQueue(
        [FromQuery] bool actionableOnly = true,
        [FromQuery] string? bucket = null)
    {
        var scoped = await GetScopedRequestsQuery();
        var projection = new ReceivingQueueProjection();
        var built = await projection.BuildAsync(scoped, actionableOnly, bucket);

        // Summary reflects the (optionally bucket-filtered) rows so counts always match the returned list.
        return Ok(new ReceivingQueueResponseDto { Rows = built.Rows, Summary = built.Summary });
    }
}
