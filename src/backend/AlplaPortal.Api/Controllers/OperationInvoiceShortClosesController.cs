using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Validation;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Short-close of one PO group's operation-invoice obligation (Release 4 Phase 3A): the audited,
/// two-person decision that the group will legitimately never reach its expected total, closing
/// the remaining amount instead of waiting for an invoice that will not come.
///
/// <para>Separation of duties is structural: Buyer, Finance or SysAdmin may PROPOSE; only Finance
/// or SysAdmin DECIDE; and the proposer can never approve their own proposal — but may reject it,
/// which is how a proposal is withdrawn (the model has no separate cancellation state; a
/// self-rejection with its mandatory reason is the recorded withdrawal).</para>
///
/// <para>One ACTIVE (PROPOSED or APPROVED) short-close per group, guarded twice: a precheck here
/// and the filtered unique index <c>UX_OperationInvoiceShortClose_ActivePerGroup</c> as the
/// authoritative backstop under concurrency.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/requests/{requestId:guid}/po-groups/{groupId:guid}/operation-invoice-short-close")]
public class OperationInvoiceShortClosesController : BaseController
{
    private readonly ILogger<OperationInvoiceShortClosesController> _logger;
    private readonly IOperationInvoiceCoverageService _coverage;

    /// <summary>
    /// Phase 4C: optional-by-default so existing direct constructions (tests) keep compiling;
    /// DI always supplies the registered service in production. Self-gates on the completion
    /// flags with zero queries while disabled. Only APPROVE is wired — a rejection/withdrawal
    /// never changes effective coverage (a PROPOSED short-close contributes nothing), so it
    /// cannot change completion readiness and takes no completion evaluation.
    /// </summary>
    private readonly AlplaPortal.Application.Interfaces.Requests.IRequestCompletionService? _completionService;

    public OperationInvoiceShortClosesController(
        ApplicationDbContext context,
        ILogger<OperationInvoiceShortClosesController> logger,
        IOperationInvoiceCoverageService coverage,
        AlplaPortal.Application.Interfaces.Requests.IRequestCompletionService? completionService = null) : base(context)
    {
        _logger = logger;
        _coverage = coverage;
        _completionService = completionService;
    }

    /// <summary>Typed business codes — the UI branches on codes, never on Portuguese.</summary>
    public const string GroupInvalidCode = "OI_SHORTCLOSE_GROUP_INVALID";
    public const string NotEligibleCode = "OI_SHORTCLOSE_NOT_ELIGIBLE";
    public const string NothingRemainingCode = "OI_SHORTCLOSE_NOTHING_REMAINING";
    public const string ActiveExistsCode = "OI_SHORTCLOSE_ACTIVE_EXISTS";
    public const string NotDecidableCode = "OI_SHORTCLOSE_NOT_DECIDABLE";
    public const string SelfApprovalCode = "OI_SHORTCLOSE_SELF_APPROVAL";
    public const string ConcurrencyCode = "OI_SHORTCLOSE_CONCURRENCY";

    // ── Read ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Full short-close history of the group, newest first — decided rows included.</summary>
    [HttpGet]
    public async Task<ActionResult<List<OperationInvoiceShortCloseDto>>> List(
        Guid requestId, Guid groupId)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var group = await _context.RequestPoGroups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.RequestId == requestId);
        if (group == null) return NotFound(Problem404("Grupo não encontrado."));

        var rows = await _context.OperationInvoiceShortCloses.AsNoTracking()
            .Where(c => c.RequestPoGroupId == groupId)
            .OrderByDescending(c => c.ProposedAtUtc)
            .ToListAsync();

        return Ok(await ProjectAsync(rows));
    }

    // ── Propose ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Proposes closing the group's remaining obligation. The remaining amount is FROZEN into the
    /// proposal so the decision audit records what was actually being written off, even if
    /// coverage moves afterwards (in which case the decider sees both numbers).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Propose(
        Guid requestId, Guid groupId, [FromBody] ProposeOperationInvoiceShortCloseDto dto)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var roleProblem = GuardProposeRole();
        if (roleProblem != null) return roleProblem;

        var statusProblem = GuardMutableRequestStatus(request);
        if (statusProblem != null) return statusProblem;

        var group = await _context.RequestPoGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.RequestId == requestId);
        if (group == null) return NotFound(Problem404("Grupo não encontrado."));

        // Same obligation-eligibility rule as allocations: classified, owing an invoice, in an
        // accepting state, and not parked in PO correction.
        if (!group.RequiresOperationInvoice ||
            !RequestConstants.OperationInvoiceStatuses.AcceptsUploadIn(group.OperationInvoiceStatus) ||
            string.Equals(group.Status, RequestConstants.PoGroupStatuses.WaitingPoCorrection,
                StringComparison.OrdinalIgnoreCase))
        {
            var problem = new ProblemDetails
            {
                Title = "Grupo não elegível",
                Detail = "Só um grupo com obrigação de fatura final ativa pode ser encerrado por " +
                         "valor inferior ao esperado.",
                Status = 409
            };
            problem.Extensions["code"] = NotEligibleCode;
            return Conflict(problem);
        }

        var expected = group.ExpectedOperationInvoiceTotal;
        if (expected is not > 0)
        {
            var problem = new ProblemDetails
            {
                Title = "Grupo sem total esperado",
                Detail = "O grupo não tem total esperado definido — sem baseline não existe " +
                         "restante a encerrar.",
                Status = 409
            };
            problem.Extensions["code"] = NotEligibleCode;
            return Conflict(problem);
        }

        // Remaining = expected − validated EFFECTIVE coverage. Within tolerance there is nothing
        // to short-close — the group satisfies on its own.
        var validated = await ValidatedCoverageAsync(groupId);
        var tolerance = OperationInvoiceTolerance.For(expected.Value);
        var remaining = expected.Value - validated;
        if (remaining <= tolerance)
        {
            var problem = new ProblemDetails
            {
                Title = "Nada a encerrar",
                Detail = "A cobertura validada já satisfaz o total esperado dentro da tolerância — " +
                         "não existe restante para encerrar.",
                Status = 409
            };
            problem.Extensions["code"] = NothingRemainingCode;
            problem.Extensions["expectedTotal"] = expected.Value;
            problem.Extensions["validatedTotal"] = validated;
            problem.Extensions["tolerance"] = tolerance;
            return Conflict(problem);
        }

        var justification = dto.Justification?.Trim();
        if (!ReconciliationJustificationValidator.IsValid(justification, out var justificationError))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Justification"] = new[] { justificationError }
            }));
        }

        if (dto.EvidenceAttachmentId.HasValue)
        {
            var evidenceBelongs = await _context.RequestAttachments.AsNoTracking()
                .AnyAsync(a => a.Id == dto.EvidenceAttachmentId.Value && a.RequestId == requestId);
            if (!evidenceBelongs)
            {
                return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    ["EvidenceAttachmentId"] = new[] { "O anexo de evidência não pertence a este pedido." }
                }));
            }
        }

        // One active proposal per group. Idempotent retry: the SAME user re-proposing while their
        // own PROPOSED row exists gets that row back; anything else active is a real conflict.
        var active = await _context.OperationInvoiceShortCloses.AsNoTracking()
            .Where(c => c.RequestPoGroupId == groupId &&
                        RequestConstants.ShortCloseStatuses.Active.Contains(c.Status))
            .FirstOrDefaultAsync();
        if (active != null)
        {
            if (string.Equals(active.Status, RequestConstants.ShortCloseStatuses.Proposed,
                    StringComparison.OrdinalIgnoreCase) &&
                active.ProposedByUserId == CurrentUserId)
            {
                return Ok(await ProjectOneAsync(active));
            }

            var problem = new ProblemDetails
            {
                Title = "Encerramento já ativo",
                Detail = string.Equals(active.Status, RequestConstants.ShortCloseStatuses.Approved,
                    StringComparison.OrdinalIgnoreCase)
                    ? "Este grupo já foi encerrado por valor inferior ao esperado."
                    : "Já existe uma proposta de encerramento pendente de decisão para este grupo.",
                Status = 409
            };
            problem.Extensions["code"] = ActiveExistsCode;
            problem.Extensions["shortCloseId"] = active.Id;
            return Conflict(problem);
        }

        var shortClose = new OperationInvoiceShortClose
        {
            Id = Guid.NewGuid(),
            RequestPoGroupId = groupId,
            Status = RequestConstants.ShortCloseStatuses.Proposed,
            ProposedByUserId = CurrentUserId,
            ProposedAtUtc = DateTime.UtcNow,
            ProposalJustification = justification!,
            EvidenceAttachmentId = dto.EvidenceAttachmentId,
            RemainingAmountAtProposal = remaining
        };
        _context.OperationInvoiceShortCloses.Add(shortClose);

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = "OI_SHORTCLOSE_PROPOSED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Proposta de encerramento por valor inferior no grupo " +
                      $"{group.SupplierNameSnapshot ?? group.Id.ToString()}: restante " +
                      $"{remaining:N2} de {expected.Value:N2} {group.CurrencyCode}. " +
                      $"Justificativa: {justification}",
            IdempotencyKey = PostPaymentIdempotencyKeys.ShortCloseProposed(shortClose.Id),
            CreatedAtUtc = DateTime.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The filtered unique active-per-group index caught a racing proposal the precheck
            // could not see. Same business answer as the precheck: already active.
            _context.ChangeTracker.Clear();
            var problem = new ProblemDetails
            {
                Title = "Encerramento já ativo",
                Detail = "Já existe uma proposta de encerramento ativa para este grupo.",
                Status = 409
            };
            problem.Extensions["code"] = ActiveExistsCode;
            return Conflict(problem);
        }

        return Ok(await ProjectOneAsync(shortClose));
    }

    // ── Decide ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Approves the proposal — Finance or SysAdmin, NEVER the proposer. Approval is what makes
    /// the aggregate close short: the group re-derives to SATISFIED with <c>ClosedShort</c> in
    /// the same transaction.
    /// </summary>
    [HttpPost("{shortCloseId:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid requestId, Guid groupId, Guid shortCloseId,
        [FromBody] DecideOperationInvoiceShortCloseDto dto)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var roleProblem = GuardDecisionRole();
        if (roleProblem != null) return roleProblem;

        var statusProblem = GuardMutableRequestStatus(request);
        if (statusProblem != null) return statusProblem;

        var (shortClose, group, loadProblem) = await LoadShortCloseAsync(requestId, groupId, shortCloseId);
        if (loadProblem != null) return loadProblem;

        // Idempotent retry: approving what is already APPROVED is the same decision arriving twice.
        if (string.Equals(shortClose!.Status, RequestConstants.ShortCloseStatuses.Approved,
                StringComparison.OrdinalIgnoreCase))
        {
            return Ok(await ProjectOneAsync(shortClose));
        }

        if (!string.Equals(shortClose.Status, RequestConstants.ShortCloseStatuses.Proposed,
                StringComparison.OrdinalIgnoreCase))
        {
            return NotDecidable();
        }

        // Separation of duties — structural, role-independent: a SysAdmin who proposed still
        // cannot approve their own proposal.
        if (shortClose.ProposedByUserId == CurrentUserId)
        {
            var problem = new ProblemDetails
            {
                Title = "Aprovação própria não permitida",
                Detail = "Quem propôs o encerramento não pode aprová-lo — a decisão exige uma " +
                         "segunda pessoa do Financeiro ou Administração.",
                Status = 403
            };
            problem.Extensions["code"] = SelfApprovalCode;
            return StatusCode(403, problem);
        }

        var staleToken = ApplyConcurrencyToken(shortClose, dto.RowVersion);
        if (staleToken != null) return staleToken;

        shortClose.Status = RequestConstants.ShortCloseStatuses.Approved;
        shortClose.DecidedByUserId = CurrentUserId;
        shortClose.DecidedAtUtc = DateTime.UtcNow;
        shortClose.DecisionReason = Trimmed(dto.DecisionReason);

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = "OI_SHORTCLOSE_APPROVED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Encerramento por valor inferior APROVADO no grupo " +
                      $"{group!.SupplierNameSnapshot ?? group.Id.ToString()}: restante de " +
                      $"{shortClose.RemainingAmountAtProposal:N2} {group.CurrencyCode} encerrado." +
                      (shortClose.DecisionReason != null ? $" Motivo: {shortClose.DecisionReason}" : ""),
            IdempotencyKey = PostPaymentIdempotencyKeys.ShortCloseApproved(shortClose.Id),
            CreatedAtUtc = DateTime.UtcNow
        });

        // Approval is an effective-coverage write — forced touch so a concurrent coverage writer
        // conflicts instead of committing against the pre-approval reading.
        var coverageChanges = await _coverage.RederiveAsync(new[] { groupId }, forceGroupTouch: true);
        AddGroupStatusHistories(request, coverageChanges);

        // Phase 4C: an approved short-close satisfies the invoice obligation (SATISFIED via
        // ClosedShort) — the group may advance in this same transaction.
        if (_completionService != null)
            await _completionService.EvaluateGroupCompletionAsync(requestId, groupId, CurrentUserId);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        // Phase 2 strictly post-save; never fails the approval.
        if (_completionService != null)
        {
            try
            {
                await _completionService.EvaluateParentCompletionAsync(requestId, CurrentUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Non-critical: parent completion evaluation failed after short-close approval on Request {RequestId}.",
                    requestId);
            }
        }

        return Ok(await ProjectOneAsync(shortClose));
    }

    /// <summary>
    /// Rejects the proposal, with a mandatory reason. Two legitimate actors: a Finance/SysAdmin
    /// decider, or THE PROPOSER THEMSELF — the model's withdrawal path (no separate cancellation
    /// state exists; a self-rejection is the recorded withdrawal).
    /// </summary>
    [HttpPost("{shortCloseId:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid requestId, Guid groupId, Guid shortCloseId,
        [FromBody] DecideOperationInvoiceShortCloseDto dto)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var statusProblem = GuardMutableRequestStatus(request);
        if (statusProblem != null) return statusProblem;

        var (shortClose, group, loadProblem) = await LoadShortCloseAsync(requestId, groupId, shortCloseId);
        if (loadProblem != null) return loadProblem;

        // Decider roles OR the proposer withdrawing — checked against the loaded row, so the
        // role guard cannot run before the row exists.
        var isDecider = CurrentUserRoles.Contains(RoleConstants.Finance) ||
                        CurrentUserRoles.Contains(RoleConstants.SystemAdministrator);
        var isProposerWithdrawal = shortClose!.ProposedByUserId == CurrentUserId;
        if (!isDecider && !isProposerWithdrawal)
        {
            return StatusCode(403, new ProblemDetails
            {
                Title = "Sem permissão",
                Detail = "Apenas o Financeiro, o Administrador de Sistema ou o próprio proponente " +
                         "(retirada) podem rejeitar uma proposta de encerramento.",
                Status = 403
            });
        }

        // Idempotent retry: rejecting what is already REJECTED returns the persisted decision.
        if (string.Equals(shortClose.Status, RequestConstants.ShortCloseStatuses.Rejected,
                StringComparison.OrdinalIgnoreCase))
        {
            return Ok(await ProjectOneAsync(shortClose));
        }

        if (!string.Equals(shortClose.Status, RequestConstants.ShortCloseStatuses.Proposed,
                StringComparison.OrdinalIgnoreCase))
        {
            return NotDecidable();
        }

        var reason = Trimmed(dto.DecisionReason);
        if (reason == null)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["DecisionReason"] = new[] { "Indique o motivo da rejeição." }
            }));
        }

        var staleToken = ApplyConcurrencyToken(shortClose, dto.RowVersion);
        if (staleToken != null) return staleToken;

        shortClose.Status = RequestConstants.ShortCloseStatuses.Rejected;
        shortClose.DecidedByUserId = CurrentUserId;
        shortClose.DecidedAtUtc = DateTime.UtcNow;
        shortClose.DecisionReason = reason;

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = "OI_SHORTCLOSE_REJECTED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = (isProposerWithdrawal && !isDecider
                          ? "Proposta de encerramento RETIRADA pelo proponente no grupo "
                          : "Encerramento por valor inferior REJEITADO no grupo ") +
                      $"{group!.SupplierNameSnapshot ?? group.Id.ToString()}. Motivo: {reason}",
            IdempotencyKey = PostPaymentIdempotencyKeys.ShortCloseRejected(shortClose.Id),
            CreatedAtUtc = DateTime.UtcNow
        });

        // A rejection frees the active slot but moves no coverage — no forced touch; the
        // re-derivation simply confirms the unchanged aggregate.
        var coverageChanges = await _coverage.RederiveAsync(new[] { groupId }, forceGroupTouch: false);
        AddGroupStatusHistories(request, coverageChanges);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return Ok(await ProjectOneAsync(shortClose));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Validated EFFECTIVE coverage of the group: allocations on VALIDATED, non-superseded invoices.</summary>
    private async Task<decimal> ValidatedCoverageAsync(Guid groupId)
    {
        return await _context.OperationInvoiceAllocations.AsNoTracking()
            .Where(a => a.RequestPoGroupId == groupId)
            .Join(_context.OperationInvoices.Where(i =>
                    i.Status == RequestConstants.OperationInvoiceDocumentStatuses.Validated &&
                    i.SupersededByOperationInvoiceId == null),
                a => a.OperationInvoiceId,
                i => i.Id,
                (a, i) => a.AllocatedGrossAmount)
            .SumAsync();
    }

    private async Task<(OperationInvoiceShortClose?, RequestPoGroup?, IActionResult?)> LoadShortCloseAsync(
        Guid requestId, Guid groupId, Guid shortCloseId)
    {
        var group = await _context.RequestPoGroups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.RequestId == requestId);
        if (group == null) return (null, null, NotFound(Problem404("Grupo não encontrado.")));

        var shortClose = await _context.OperationInvoiceShortCloses
            .FirstOrDefaultAsync(c => c.Id == shortCloseId && c.RequestPoGroupId == groupId);
        if (shortClose == null)
            return (null, null, NotFound(Problem404("Proposta de encerramento não encontrada.")));

        return (shortClose, group, null);
    }

    private IActionResult NotDecidable()
    {
        var problem = new ProblemDetails
        {
            Title = "Proposta já decidida",
            Detail = "Esta proposta de encerramento já foi decidida com um resultado diferente.",
            Status = 409
        };
        problem.Extensions["code"] = NotDecidableCode;
        return Conflict(problem);
    }

    private IActionResult? GuardProposeRole()
    {
        var roles = CurrentUserRoles;
        if (roles.Contains(RoleConstants.Buyer) ||
            roles.Contains(RoleConstants.Finance) ||
            roles.Contains(RoleConstants.SystemAdministrator))
        {
            return null;
        }

        return StatusCode(403, new ProblemDetails
        {
            Title = "Sem permissão",
            Detail = "Apenas o Comprador, o Financeiro ou o Administrador de Sistema podem propor " +
                     "um encerramento por valor inferior.",
            Status = 403
        });
    }

    private IActionResult? GuardDecisionRole()
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
            Detail = "Apenas o Financeiro ou o Administrador de Sistema podem decidir um " +
                     "encerramento por valor inferior.",
            Status = 403
        });
    }

    /// <summary>Same mutation window as every operation-invoice write.</summary>
    private IActionResult? GuardMutableRequestStatus(Request request)
    {
        if (OperationInvoiceLifecyclePolicy.CanMutateInRequestStatus(request.Status?.Code)) return null;

        return Conflict(new ProblemDetails
        {
            Title = "Estado do pedido não permite esta operação",
            Detail = "O encerramento por valor inferior só é possível enquanto o pedido está no " +
                     "período pós-aprovação e não em correção de PO, concluído, rejeitado ou cancelado.",
            Status = 409
        });
    }

    private IActionResult? ApplyConcurrencyToken(OperationInvoiceShortClose shortClose, byte[]? rowVersion)
    {
        if (rowVersion == null || rowVersion.Length == 0) return null;

        if (!rowVersion.SequenceEqual(shortClose.RowVersion ?? Array.Empty<byte>()))
            return ConcurrencyConflict();

        _context.Entry(shortClose).Property(c => c.RowVersion).OriginalValue = rowVersion;
        return null;
    }

    private IActionResult ConcurrencyConflict()
    {
        _context.ChangeTracker.Clear();
        var problem = new ProblemDetails
        {
            Title = "Proposta alterada entretanto",
            Detail = "Esta proposta foi alterada por outra pessoa desde que a abriu. Recarregue " +
                     "para ver o estado atual antes de repetir a decisão.",
            Status = 409
        };
        problem.Extensions["code"] = ConcurrencyCode;
        return Conflict(problem);
    }

    /// <summary>GROUP_OI_STATUS audit rows — same shape as the invoice controller writes.</summary>
    private void AddGroupStatusHistories(Request request, List<GroupCoverageChange> changes)
    {
        foreach (var change in changes.Where(c => c.StatusChanged))
        {
            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = CurrentUserId,
                ActionTaken = "GROUP_OI_STATUS",
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = $"Obrigação de fatura final do grupo {change.RequestPoGroupId}: " +
                          $"{change.PreviousStatus} → {change.NewStatus} " +
                          $"(validado {change.Coverage.ValidatedTotal:N2}; pendente {change.Coverage.PendingValidationTotal:N2}; " +
                          $"restante {change.Coverage.RemainingAmount:N2} de {change.Coverage.ExpectedTotal:N2}).",
                CreatedAtUtc = DateTime.UtcNow
            });
        }
    }

    private async Task<OperationInvoiceShortCloseDto> ProjectOneAsync(OperationInvoiceShortClose row) =>
        (await ProjectAsync(new List<OperationInvoiceShortClose> { row }))[0];

    private async Task<List<OperationInvoiceShortCloseDto>> ProjectAsync(
        List<OperationInvoiceShortClose> rows)
    {
        var userIds = rows.Select(r => r.ProposedByUserId)
            .Concat(rows.Where(r => r.DecidedByUserId.HasValue).Select(r => r.DecidedByUserId!.Value))
            .Distinct()
            .ToList();

        var names = await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        return rows.Select(r => new OperationInvoiceShortCloseDto
        {
            Id = r.Id,
            RequestPoGroupId = r.RequestPoGroupId,
            Status = r.Status,
            ProposedByUserId = r.ProposedByUserId,
            ProposedByName = names.TryGetValue(r.ProposedByUserId, out var pn) ? pn : null,
            ProposedAtUtc = r.ProposedAtUtc,
            ProposalJustification = r.ProposalJustification,
            EvidenceAttachmentId = r.EvidenceAttachmentId,
            RemainingAmountAtProposal = r.RemainingAmountAtProposal,
            DecidedByUserId = r.DecidedByUserId,
            DecidedByName = r.DecidedByUserId.HasValue &&
                            names.TryGetValue(r.DecidedByUserId.Value, out var dn) ? dn : null,
            DecidedAtUtc = r.DecidedAtUtc,
            DecisionReason = r.DecisionReason,
            RowVersion = r.RowVersion
        }).ToList();
    }

    private async Task<Request?> LoadScopedRequestAsync(Guid requestId) =>
        await (await GetScopedRequestsQuery())
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .FirstOrDefaultAsync(r => r.Id == requestId);

    private static ProblemDetails Problem404(string detail = "Pedido não encontrado.") =>
        new() { Title = "Não encontrado", Detail = detail, Status = 404 };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
