using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Validation;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Release 4 activation tooling — the audited, explicit backfill of
/// <c>RequestPoGroup.ExpectedOperationInvoiceTotal</c> for groups created before the expected
/// total existed. NEVER automatic and NEVER an overwrite: only groups whose expected total is
/// still null are candidates, and the write freezes the group's own <c>TotalAmount</c> as the
/// baseline with the operator's reason in the manual-set audit trio.
///
/// <para>Preview is Finance + SysAdmin (read-only); Apply is SysAdmin only. Both work Portal-wide,
/// which is why this lives on an admin route instead of a request-scoped one.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/admin/release4/expected-operation-invoice-totals")]
public class Release4ActivationController : BaseController
{
    private readonly ILogger<Release4ActivationController> _logger;
    private readonly IOperationInvoiceCoverageService _coverage;

    public Release4ActivationController(
        ApplicationDbContext context,
        ILogger<Release4ActivationController> logger,
        IOperationInvoiceCoverageService coverage) : base(context)
    {
        _logger = logger;
        _coverage = coverage;
    }

    /// <summary>Skip reasons — the preview explains every non-candidate instead of hiding it.</summary>
    public const string SkipNotClassified = "NOT_CLASSIFIED";
    public const string SkipNotRequired = "NOT_REQUIRED";
    public const string SkipNoTotal = "NO_TOTAL";

    /// <summary>
    /// Dry run: every group still missing an expected total, split into eligible and skipped with
    /// the proposed value per group. Changes nothing.
    /// </summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview()
    {
        var roleProblem = GuardPreviewRole();
        if (roleProblem != null) return roleProblem;

        var groups = await LoadCandidatesAsync();

        var result = new ExpectedTotalBackfillPreviewDto();
        foreach (var row in groups)
        {
            var dto = ToDto(row);
            result.Groups.Add(dto);
            if (dto.Eligible) result.EligibleCount++; else result.SkippedCount++;
        }

        return Ok(result);
    }

    /// <summary>
    /// The activation write, SysAdmin only. Sets each eligible group's expected total to its own
    /// TotalAmount, stamps the manual-set audit trio with the mandatory reason, re-derives the
    /// aggregate status and writes one history event per touched request — all in one transaction.
    ///
    /// <para>Idempotent structurally: a written group stops being a candidate (its expected total
    /// is no longer null), so the exact same apply retried finds nothing to write and produces no
    /// duplicate history.</para>
    /// </summary>
    [HttpPost("apply")]
    public async Task<ActionResult<ExpectedTotalBackfillResultDto>> Apply(
        [FromBody] ApplyExpectedTotalBackfillDto dto)
    {
        if (!CurrentUserRoles.Contains(RoleConstants.SystemAdministrator))
        {
            return StatusCode(403, new ProblemDetails
            {
                Title = "Sem permissão",
                Detail = "Apenas o Administrador de Sistema pode aplicar a ativação dos totais esperados.",
                Status = 403
            });
        }

        var reason = dto.Reason?.Trim();
        if (!ReconciliationJustificationValidator.IsValid(reason, out var reasonError))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Reason"] = new[] { reasonError }
            }));
        }

        var candidates = await LoadCandidatesAsync(track: true);

        var result = new ExpectedTotalBackfillResultDto();
        var nowUtc = DateTime.UtcNow;
        var writtenGroups = new List<RequestPoGroup>();

        foreach (var row in candidates)
        {
            var dto2 = ToDto(row);
            if (!dto2.Eligible)
            {
                result.SkippedCount++;
                continue;
            }

            var group = row.Group;
            group.ExpectedOperationInvoiceTotal = group.TotalAmount;
            group.ExpectedOperationInvoiceCurrency = group.CurrencyCode;
            group.ExpectedTotalSetByUserId = CurrentUserId;
            group.ExpectedTotalSetAtUtc = nowUtc;
            group.ExpectedTotalJustification = $"[ATIVAÇÃO R4] {reason}";

            writtenGroups.Add(group);
            result.WrittenGroups.Add(dto2);
            result.WrittenCount++;
        }

        if (writtenGroups.Count == 0)
        {
            return Ok(result);
        }

        // One audit event per touched request, naming its written groups. No idempotency key on
        // purpose: a retry after commit finds zero candidates and never reaches this point, and
        // two DIFFERENT apply runs (new groups became eligible in between) are distinct events.
        var byRequest = writtenGroups.GroupBy(g => g.RequestId).ToList();
        var requestStatusIds = await _context.Requests.AsNoTracking()
            .Where(r => byRequest.Select(x => x.Key).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.StatusId);

        foreach (var requestGroups in byRequest)
        {
            requestStatusIds.TryGetValue(requestGroups.Key, out var statusId);
            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = requestGroups.Key,
                ActorUserId = CurrentUserId,
                ActionTaken = "OI_EXPECTED_TOTAL_BACKFILLED",
                PreviousStatusId = statusId,
                NewStatusId = statusId,
                Comment = $"Ativação Release 4: total esperado da fatura final definido a partir do " +
                          $"total do grupo em {requestGroups.Count()} grupo(s) " +
                          $"({string.Join(", ", requestGroups.Select(g => g.SupplierNameSnapshot ?? g.Id.ToString()))}). " +
                          $"Motivo: {reason}",
                CreatedAtUtc = nowUtc
            });
        }

        // The expected total is an input of the aggregate — re-derive in the same transaction so
        // the cached status can never disagree with the value that was just activated.
        var coverageChanges = await _coverage.RederiveAsync(
            writtenGroups.Select(g => g.Id).ToList(), forceGroupTouch: false);
        foreach (var change in coverageChanges.Where(c => c.StatusChanged))
        {
            var group = writtenGroups.First(g => g.Id == change.RequestPoGroupId);
            requestStatusIds.TryGetValue(group.RequestId, out var statusId);
            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = group.RequestId,
                ActorUserId = CurrentUserId,
                ActionTaken = "GROUP_OI_STATUS",
                PreviousStatusId = statusId,
                NewStatusId = statusId,
                Comment = $"Obrigação de fatura final do grupo {change.RequestPoGroupId}: " +
                          $"{change.PreviousStatus} → {change.NewStatus} " +
                          $"(validado {change.Coverage.ValidatedTotal:N2}; pendente {change.Coverage.PendingValidationTotal:N2}; " +
                          $"restante {change.Coverage.RemainingAmount:N2} de {change.Coverage.ExpectedTotal:N2}).",
                CreatedAtUtc = nowUtc
            });
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Release 4 expected-total activation applied by {UserId}: {Written} written, {Skipped} skipped.",
            CurrentUserId, result.WrittenCount, result.SkippedCount);

        return Ok(result);
    }

    // ── Shared candidate pipeline — preview and apply must agree by construction ────────────

    private sealed record CandidateRow(RequestPoGroup Group, string? RequestNumber);

    private async Task<List<CandidateRow>> LoadCandidatesAsync(bool track = false)
    {
        var query = _context.RequestPoGroups.AsQueryable();
        if (!track) query = query.AsNoTracking();

        var rows = await query
            .Where(g => g.ExpectedOperationInvoiceTotal == null)
            .Join(_context.Requests,
                g => g.RequestId,
                r => r.Id,
                (g, r) => new { Group = g, r.RequestNumber })
            .OrderBy(x => x.RequestNumber).ThenBy(x => x.Group.SupplierNameSnapshot)
            .ToListAsync();

        return rows.Select(x => new CandidateRow(x.Group, x.RequestNumber)).ToList();
    }

    private static ExpectedTotalBackfillGroupDto ToDto(CandidateRow row)
    {
        var group = row.Group;
        var skipReason =
            group.SourceDocumentType == null ? SkipNotClassified
            : !group.RequiresOperationInvoice ? SkipNotRequired
            : group.TotalAmount <= 0 ? SkipNoTotal
            : null;

        return new ExpectedTotalBackfillGroupDto
        {
            RequestId = group.RequestId,
            RequestNumber = row.RequestNumber,
            RequestPoGroupId = group.Id,
            SupplierName = group.SupplierNameSnapshot,
            CurrencyCode = group.CurrencyCode,
            GroupStatus = group.Status,
            OperationInvoiceStatus = group.OperationInvoiceStatus,
            CurrentExpectedTotal = group.ExpectedOperationInvoiceTotal,
            ProposedExpectedTotal = skipReason == null ? group.TotalAmount : null,
            Eligible = skipReason == null,
            SkipReason = skipReason
        };
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
            Detail = "Apenas o Financeiro ou o Administrador de Sistema podem consultar a pré-visualização da ativação.",
            Status = 403
        });
    }
}
