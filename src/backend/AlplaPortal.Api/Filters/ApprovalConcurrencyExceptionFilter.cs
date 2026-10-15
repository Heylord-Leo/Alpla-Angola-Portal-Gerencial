using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Api.Filters;

/// <summary>
/// Maps an optimistic-concurrency failure (two alternative approvers deciding the same request or
/// batch at the same time) to a structured 409 instead of an unhandled 500. The loser's transaction
/// was never committed: no second transition, PO-group activation or notification happened. This is
/// the same contract the legacy request-level approval already returns
/// (APPROVAL_CONCURRENCY_CONFLICT in RequestsController).
/// </summary>
public sealed class ApprovalConcurrencyExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ApprovalConcurrencyExceptionFilter> _logger;

    public ApprovalConcurrencyExceptionFilter(ILogger<ApprovalConcurrencyExceptionFilter> logger) => _logger = logger;

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DbUpdateConcurrencyException ex) return;

        _logger.LogWarning(ex,
            "Approval concurrency conflict on {Path}: another approver completed this stage first. Nothing was committed by this request.",
            context.HttpContext.Request.Path);

        context.Result = new ConflictObjectResult(new ProblemDetails
        {
            Title = "Conflito de Concorrência",
            Detail = "Outro aprovador concluiu esta etapa primeiro. A sua ação não foi aplicada. Atualize os dados para ver o estado atual.",
            Status = StatusCodes.Status409Conflict,
            Extensions =
            {
                ["code"] = "APPROVAL_CONCURRENCY_CONFLICT",
                ["traceId"] = context.HttpContext.TraceIdentifier
            }
        });
        context.ExceptionHandled = true;
    }
}
