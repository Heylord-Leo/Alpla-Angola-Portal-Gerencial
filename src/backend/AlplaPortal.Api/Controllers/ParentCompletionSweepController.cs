using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Application.Validation;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Release 4 Phase 4C recovery sweep — the SysAdmin mechanism that recovers a PARENT transition
/// which failed after every group had already completed (a Phase-2 race lost twice, a process
/// crash between commit and evaluation, and nothing has re-triggered since).
///
/// <para>It recovers, it never repairs: the apply path only invokes the authoritative
/// <see cref="IRequestCompletionService.EvaluateParentCompletionAsync"/> — it NEVER writes
/// COMPLETED directly, never classifies, never fabricates receipt/payment/invoice facts, never
/// bypasses a reconciliation and never touches groupless requests. Anything the completion
/// service would refuse, the sweep refuses identically, because the service IS the rulebook.</para>
///
/// <para>Preview (Finance/SysAdmin) is an ungated dry run — consistent with the expected-total
/// activation tool, planning reads stay available in every flag state. Apply (SysAdmin only,
/// mandatory meaningful reason) fails closed while <c>Enabled &amp;&amp; CompletionEnabled</c> is
/// not true: the sweep must never be the thing that implicitly activates Phase 4.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/admin/release4/parent-completion-sweep")]
public class ParentCompletionSweepController : BaseController
{
    public const string SkipUnclassified = "UNCLASSIFIED_GROUP";
    public const string SkipActiveReconciliation = "ACTIVE_RECONCILIATION";
    public const string CompletionDisabledCode = "COMPLETION_DISABLED";

    private readonly ILogger<ParentCompletionSweepController> _logger;
    private readonly IRequestCompletionService _completion;
    private readonly PostPaymentCompletionOptions _postPaymentOptions;

    public ParentCompletionSweepController(
        ApplicationDbContext context,
        ILogger<ParentCompletionSweepController> logger,
        IRequestCompletionService completion,
        IOptions<PostPaymentCompletionOptions> postPaymentOptions) : base(context)
    {
        _logger = logger;
        _completion = completion;
        _postPaymentOptions = postPaymentOptions?.Value ?? new PostPaymentCompletionOptions();
    }

    /// <summary>
    /// Dry run: every open, grouped, classified request whose relevant groups are all COMPLETED,
    /// with the blockers that would still stop the parent transition. Changes nothing.
    /// </summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview()
    {
        var roleProblem = GuardPreviewRole();
        if (roleProblem != null) return roleProblem;

        var rows = await LoadCandidatesAsync();

        return Ok(new
        {
            CompletionEnabled = !PostPaymentCompletionPolicy.IsCompletionDisabled(_postPaymentOptions),
            EligibleCount = rows.Count(r => r.Eligible),
            SkippedCount = rows.Count(r => !r.Eligible),
            Requests = rows
        });
    }

    public class ApplySweepDto
    {
        public string? Reason { get; set; }
    }

    /// <summary>
    /// SysAdmin only. Re-runs the authoritative parent evaluation for every eligible candidate.
    /// Idempotent: a recovered request stops being a candidate; AlreadyCompleted and blocked
    /// results are reported, never retried in a loop.
    /// </summary>
    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] ApplySweepDto dto)
    {
        if (!CurrentUserRoles.Contains(RoleConstants.SystemAdministrator))
        {
            return StatusCode(403, new ProblemDetails
            {
                Title = "Sem permissão",
                Detail = "Apenas o Administrador de Sistema pode executar a recuperação de conclusão.",
                Status = 403
            });
        }

        // Fail closed: the sweep recovers an ACTIVE lifecycle — it must never be the write that
        // implicitly turns Phase 4 on.
        if (PostPaymentCompletionPolicy.IsCompletionDisabled(_postPaymentOptions))
        {
            var problem = new ProblemDetails
            {
                Title = "Ciclo de conclusão desativado",
                Detail = "A recuperação de conclusão só pode ser executada com o ciclo de conclusão " +
                         "pós-pagamento ativo (PostPaymentCompletion.Enabled e CompletionEnabled).",
                Status = 409
            };
            problem.Extensions["code"] = CompletionDisabledCode;
            return Conflict(problem);
        }

        var reason = dto.Reason?.Trim();
        if (!ReconciliationJustificationValidator.IsValid(reason, out var reasonError))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Reason"] = new[] { reasonError }
            }));
        }

        var candidates = (await LoadCandidatesAsync()).Where(r => r.Eligible).ToList();

        var completed = new List<object>();
        var alreadyCompleted = 0;
        var blocked = 0;
        var conflicts = 0;

        foreach (var candidate in candidates)
        {
            // The authoritative path — never a direct COMPLETED write. Every guard the service
            // applies (classification, reconciliation, group states, concurrency) applies here.
            var result = await _completion.EvaluateParentCompletionAsync(candidate.RequestId, CurrentUserId);

            if (result.RequestCompleted)
                completed.Add(new { candidate.RequestId, candidate.RequestNumber, result.CompletionCycleId });
            else if (result.AlreadyCompleted) alreadyCompleted++;
            else if (result.ConflictUnresolved) conflicts++;
            else blocked++;
        }

        _logger.LogInformation(
            "Parent-completion sweep applied by {UserId}: {Completed} completed, {Already} already completed, " +
            "{Blocked} blocked, {Conflicts} unresolved conflicts. Reason: {Reason}",
            CurrentUserId, completed.Count, alreadyCompleted, blocked, conflicts, reason);

        return Ok(new
        {
            CompletedCount = completed.Count,
            AlreadyCompletedCount = alreadyCompleted,
            BlockedCount = blocked,
            ConflictCount = conflicts,
            Completed = completed,
            Reason = reason
        });
    }

    private sealed record CandidateRow(
        Guid RequestId,
        string? RequestNumber,
        string? CurrentStatus,
        int RelevantGroupCount,
        bool Eligible,
        string? SkipReason);

    /// <summary>
    /// Candidate pipeline — preview and apply agree by construction. A candidate is an open,
    /// non-cancelled request owning at least one non-cancelled group, with EVERY non-cancelled
    /// group already COMPLETED (the Phase-1 commitment); classification and reconciliation
    /// blockers are surfaced as skip reasons instead of being hidden.
    /// </summary>
    private async Task<List<CandidateRow>> LoadCandidatesAsync()
    {
        var terminal = new[]
        {
            RequestConstants.Statuses.Completed,
            RequestConstants.Statuses.Rejected,
            RequestConstants.Statuses.Cancelled
        };

        var rows = await _context.Requests.AsNoTracking()
            .Where(r => !r.IsCancelled && !terminal.Contains(r.Status!.Code))
            .Where(r => r.PoGroups.Any(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled))
            .Where(r => r.PoGroups
                .Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled)
                .All(g => g.Status == RequestConstants.PoGroupStatuses.Completed))
            .Select(r => new
            {
                r.Id,
                r.RequestNumber,
                StatusCode = r.Status!.Code,
                RelevantGroupCount = r.PoGroups
                    .Count(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled),
                AnyUnclassified = r.PoGroups
                    .Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled)
                    .Any(g => g.SourceDocumentType == null ||
                              g.OperationInvoiceStatus == RequestConstants.OperationInvoiceStatuses.Unclassified),
                ActiveReconciliation = _context.RequestReconciliations.Any(rec =>
                    rec.RequestId == r.Id &&
                    (rec.ReconciliationStatus == "DRAFT" || rec.ReconciliationStatus == "IN_PROGRESS"))
            })
            .OrderBy(r => r.RequestNumber)
            .ToListAsync();

        return rows.Select(r =>
        {
            var skip = r.AnyUnclassified ? SkipUnclassified
                : r.ActiveReconciliation ? SkipActiveReconciliation
                : null;

            return new CandidateRow(
                r.Id, r.RequestNumber, r.StatusCode, r.RelevantGroupCount,
                Eligible: skip == null, SkipReason: skip);
        }).ToList();
    }

    private IActionResult? GuardPreviewRole()
    {
        var roles = CurrentUserRoles;
        if (roles.Contains(RoleConstants.Finance) ||
            roles.Contains(RoleConstants.SystemAdministrator))
        {
            return null;
        }

        return StatusCode(403, new ProblemDetails
        {
            Title = "Sem permissão",
            Detail = "Apenas o Financeiro ou o Administrador de Sistema podem consultar a " +
                     "pré-visualização da recuperação de conclusão.",
            Status = 403
        });
    }
}
