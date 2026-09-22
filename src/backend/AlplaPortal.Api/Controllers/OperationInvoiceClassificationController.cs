using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Validation;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// v2.245.8 — Finance classification of an UNCLASSIFIED P.O. group ("Classificar Documento de Origem").
///
/// <para>Groups created before the post-payment feature was active (or from a header that never carried a
/// document type) persist the schema defaults — <c>SourceDocumentType = null</c>,
/// <c>OperationInvoiceStatus = UNCLASSIFIED</c>, no obligation flags, no expected total. Nothing
/// downstream can decide anything about such a group (rule R15 fails closed), and until this release no
/// endpoint could give it an identity after creation. This is the "Release 5 Finance classification"
/// the completion design deferred to, in its minimal, group-scoped form.</para>
///
/// <para>The decision records IDENTITY only; every obligation is re-derived from it by
/// <see cref="DocumentObligationResolver"/> exactly as group creation does, the expected total is
/// captured with the same convention as group creation (the group's own <c>TotalAmount</c>, once,
/// only when an invoice is owed), and the cached aggregate is recomputed by the single coverage
/// service. Receiving facts (operational-receipt stamp, item quantities, supplier receipt) are
/// evidence of delivery, not of the invoice obligation, and never block this first classification;
/// genuine operation-invoice activity (allocations, short-closes, reconciliation snapshots, a bound
/// fiscal receipt) does — those facts were judged against an obligation that must not change
/// underneath them.</para>
///
/// <para>Finance or System Administrator only (the owner of <c>CLASSIFICATION_PENDING</c> in
/// <see cref="GroupCompletionOwnership"/>). One transaction; one <c>GRUPO_CLASSIFICADO</c> audit row;
/// a repeated call finds the group classified and is refused, so no duplicate audit can exist. Under
/// SQL Server the group's RowVersion is the concurrency backstop for two simultaneous decisions.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/requests/{requestId:guid}/po-groups/{groupId:guid}/operation-invoice-classification")]
public class OperationInvoiceClassificationController : BaseController
{
    private readonly ILogger<OperationInvoiceClassificationController> _logger;
    private readonly IOperationInvoiceCoverageService _coverage;
    private readonly PostPaymentCompletionOptions _options;
    private readonly AlplaPortal.Application.Interfaces.Requests.IRequestCompletionService? _completionService;

    public OperationInvoiceClassificationController(
        ApplicationDbContext context,
        ILogger<OperationInvoiceClassificationController> logger,
        IOperationInvoiceCoverageService coverage,
        IOptions<PostPaymentCompletionOptions> options,
        AlplaPortal.Application.Interfaces.Requests.IRequestCompletionService? completionService = null) : base(context)
    {
        _logger = logger;
        _coverage = coverage;
        _options = options.Value;
        _completionService = completionService;
    }

    /// <summary>Typed business codes — the UI branches on codes, never on Portuguese.</summary>
    public const string AlreadyClassifiedCode = "OI_CLASSIFICATION_ALREADY_SET";
    public const string ActivityExistsCode = "OI_CLASSIFICATION_ACTIVITY_EXISTS";
    public const string NotEligibleCode = "OI_CLASSIFICATION_NOT_ELIGIBLE";
    public const string ConcurrencyCode = "OI_CLASSIFICATION_CONCURRENCY";

    public const string HistoryAction = "GRUPO_CLASSIFICADO";

    [HttpPost]
    public async Task<IActionResult> Classify(
        Guid requestId, Guid groupId, [FromBody] ClassifyOperationInvoiceDto? dto)
    {
        // Gated-endpoint contract (Release 1): while the feature is off the route does not exist.
        if (PostPaymentCompletionPolicy.IsFeatureDisabled(_options))
            return NotFound(Problem404());

        // Visibility before anything else: an out-of-scope request is indistinguishable from a
        // nonexistent one.
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var roleProblem = GuardDecisionRole();
        if (roleProblem != null) return roleProblem;

        // ── Field validation → the standard errors dictionary the frontend already renders ──
        var errors = new Dictionary<string, string[]>();

        var justification = dto?.Justification?.Trim();
        if (!ReconciliationJustificationValidator.IsValid(justification, out var justificationError))
            errors["Justification"] = new[] { justificationError };

        var normalizedType = RequestConstants.SourceDocumentTypes.Normalize(dto?.SourceDocumentType);
        DocumentObligations? obligations = null;
        if (!RequestConstants.SourceDocumentTypes.IsValid(normalizedType))
        {
            errors["SourceDocumentType"] = new[] { "O tipo de documento de origem é inválido." };
        }
        else
        {
            // The same acceptance rule as the origin screens: what a document IS decides whether it
            // may originate this request's process (a Factura-Recibo can never originate a payment).
            var usage = string.Equals(request.RequestType?.Code, RequestConstants.Types.Payment, StringComparison.OrdinalIgnoreCase)
                ? DocumentUsageContext.PaymentRequest
                : DocumentUsageContext.QuotationManagement;
            obligations = DocumentObligationResolver.Resolve(normalizedType, usage);
            if (obligations.BlocksProgression)
            {
                errors["SourceDocumentType"] = new[]
                {
                    $"{RequestConstants.SourceDocumentTypes.DisplayName(normalizedType)}: " +
                    (obligations.BlockingReason ?? "este documento não pode classificar este grupo.")
                };
            }
        }

        if (errors.Count > 0) return BadRequest(new ValidationProblemDetails(errors));

        // Same mutation window as every operation-invoice write: post-approval, not in P.O.
        // correction, not concluded / rejected / cancelled.
        if (!OperationInvoiceLifecyclePolicy.CanMutateInRequestStatus(request.Status?.Code))
        {
            var problem = new ProblemDetails
            {
                Title = "Estado do pedido não permite esta operação",
                Detail = "A classificação do documento de origem só é possível enquanto o pedido está no " +
                         "período pós-aprovação e não em correção de P.O., concluído, rejeitado ou cancelado.",
                Status = 409
            };
            problem.Extensions["code"] = NotEligibleCode;
            return Conflict(problem);
        }

        using var transaction = await _context.Database.BeginTransactionAsync();

        var group = await _context.RequestPoGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.RequestId == requestId);
        if (group == null) return NotFound(Problem404("Grupo não encontrado."));

        if (StatusIs(group.Status, RequestConstants.PoGroupStatuses.Cancelled) ||
            StatusIs(group.Status, RequestConstants.PoGroupStatuses.Completed))
        {
            var problem = new ProblemDetails
            {
                Title = "Grupo não elegível",
                Detail = $"Um grupo {group.Status} não pode ser classificado.",
                Status = 409
            };
            problem.Extensions["code"] = NotEligibleCode;
            return Conflict(problem);
        }

        // Classification pending = the R15 fail-closed reading (GroupCompletionProjector): either the
        // identity is missing or the cached aggregate still says UNCLASSIFIED. Anything else is a
        // classified group — a correction of a classified group is the source-document edit path
        // (PoGroupReclassificationPlanner), never this first-decision endpoint.
        var isPending = group.SourceDocumentType == null ||
                        StatusIs(group.OperationInvoiceStatus, RequestConstants.OperationInvoiceStatuses.Unclassified);
        if (!isPending)
        {
            var problem = new ProblemDetails
            {
                Title = "Grupo já classificado",
                Detail = $"Este grupo já está classificado como " +
                         $"{RequestConstants.SourceDocumentTypes.DisplayName(group.SourceDocumentType)}.",
                Status = 409
            };
            problem.Extensions["code"] = AlreadyClassifiedCode;
            problem.Extensions["sourceDocumentType"] = group.SourceDocumentType;
            return Conflict(problem);
        }

        // Genuine operation-invoice activity was judged against the current (absent) obligation; a
        // new identity underneath it would discard what Finance already decided. The operational
        // receipt stamp and the supplier receipt are deliberately NOT in this list.
        var hasActivity =
            await _context.OperationInvoiceAllocations.AnyAsync(a => a.RequestPoGroupId == groupId) ||
            await _context.OperationInvoiceShortCloses.AnyAsync(c =>
                c.RequestPoGroupId == groupId &&
                (c.Status == RequestConstants.ShortCloseStatuses.Proposed ||
                 c.Status == RequestConstants.ShortCloseStatuses.Approved)) ||
            await _context.OperationInvoiceReconciliations.AnyAsync(r => r.RequestPoGroupId == groupId) ||
            group.FiscalReceiptAttachmentId != null;
        if (hasActivity)
        {
            var problem = new ProblemDetails
            {
                Title = "Grupo com atividade de Fatura Final",
                Detail = "Este grupo já tem faturas finais distribuídas, encerramento com saldo, reconciliação " +
                         "ou recibo fiscal registados. A classificação não pode ser definida sob essa evidência.",
                Status = 409
            };
            problem.Extensions["code"] = ActivityExistsCode;
            return Conflict(problem);
        }

        // ── The decision: identity + derived obligations, the group-creation convention verbatim ──
        var previousType = group.SourceDocumentType;
        var previousStatus = group.OperationInvoiceStatus;
        var nowUtc = DateTime.UtcNow;
        var resolved = obligations!;

        group.SourceDocumentType = normalizedType;
        group.OperationInvoiceStatus = resolved.OperationInvoiceStatus;
        group.RequiresOperationInvoice = resolved.RequiresOperationInvoice;
        group.RequiresSeparateFiscalReceipt = resolved.RequiresSeparateFiscalReceipt;
        group.RequiresAdvanceRegularization = resolved.RequiresAdvanceRegularization;
        group.RequiresFinanceClassificationReview = resolved.RequiresFinanceClassificationReview;

        // The commercial baseline the operation invoices must eventually cover — the group's own
        // ordered total, captured once, only when an invoice is owed, never recalculated later.
        var expectedCaptured = false;
        if (resolved.RequiresOperationInvoice && group.ExpectedOperationInvoiceTotal == null && group.TotalAmount > 0m)
        {
            group.ExpectedOperationInvoiceTotal = group.TotalAmount;
            group.ExpectedOperationInvoiceCurrency = group.CurrencyCode;
            group.ExpectedTotalSetByUserId = CurrentUserId;
            group.ExpectedTotalSetAtUtc = nowUtc;
            group.ExpectedTotalJustification = $"[CLASSIFICAÇÃO] {justification}";
            expectedCaptured = true;
        }

        group.UpdatedAtUtc = nowUtc;
        group.UpdatedByUserId = CurrentUserId;

        // The expected total and the identity are inputs of the aggregate — re-derive in the same
        // transaction so the cached status can never disagree with the decision just taken.
        await _coverage.RederiveAsync(new[] { group.Id }, forceGroupTouch: false);

        // Phase 1 completion evaluation over the fresh in-transaction state; exact no-op while the
        // completion lifecycle is disabled (the service self-gates), a no-op transition otherwise
        // unless every dimension is already satisfied.
        if (_completionService != null)
            await _completionService.EvaluateGroupCompletionAsync(requestId, group.Id, CurrentUserId);

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = HistoryAction,
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"[Grupo P.O.: {group.SupplierNameSnapshot ?? "N/A"} | GroupId: {group.Id.ToString().Substring(0, 8)}] " +
                      $"Documento de origem classificado: " +
                      $"{RequestConstants.SourceDocumentTypes.DisplayName(previousType)} → " +
                      $"{RequestConstants.SourceDocumentTypes.DisplayName(normalizedType)}. " +
                      $"Obrigação de fatura final: {previousStatus} → {group.OperationInvoiceStatus} " +
                      $"(fatura final {(resolved.RequiresOperationInvoice ? "exigida" : "não exigida")}; " +
                      $"recibo fiscal separado {(resolved.RequiresSeparateFiscalReceipt ? "exigido" : "não exigido")}" +
                      (expectedCaptured
                          ? $"; total esperado {group.ExpectedOperationInvoiceCurrency} {group.ExpectedOperationInvoiceTotal:N2}"
                          : string.Empty) +
                      $"). Motivo: {justification}",
            CreatedAtUtc = nowUtc
        });

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two simultaneous decisions on the same group: RowVersion kept the truth. Nothing was
            // written by this call.
            _context.ChangeTracker.Clear();
            var problem = new ProblemDetails
            {
                Title = "Grupo alterado entretanto",
                Detail = "Este grupo foi alterado por outra pessoa desde que o abriu. Recarregue para ver o " +
                         "estado atual antes de repetir a classificação.",
                Status = 409
            };
            problem.Extensions["code"] = ConcurrencyCode;
            return Conflict(problem);
        }

        await transaction.CommitAsync();

        _logger.LogInformation(
            "Operation-invoice classification of group {GroupId} on request {RequestId}: {Previous} → {New} ({Status}) by {UserId}.",
            group.Id, requestId, previousType ?? "null", normalizedType, group.OperationInvoiceStatus, CurrentUserId);

        // Phase 2 (parent completion) strictly after the commit — never fails the user's decision.
        if (_completionService != null)
        {
            try
            {
                await _completionService.EvaluateParentCompletionAsync(requestId, CurrentUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Non-critical: parent completion evaluation failed after group classification on Request {RequestId}.",
                    requestId);
            }
        }

        return Ok(new OperationInvoiceClassificationResultDto
        {
            RequestId = requestId,
            GroupId = group.Id,
            PreviousSourceDocumentType = previousType,
            SourceDocumentType = normalizedType!,
            OperationInvoiceStatus = group.OperationInvoiceStatus,
            RequiresOperationInvoice = group.RequiresOperationInvoice,
            RequiresSeparateFiscalReceipt = group.RequiresSeparateFiscalReceipt,
            RequiresAdvanceRegularization = group.RequiresAdvanceRegularization,
            RequiresFinanceClassificationReview = group.RequiresFinanceClassificationReview,
            ExpectedAmount = group.ExpectedOperationInvoiceTotal,
            ExpectedCurrency = group.ExpectedOperationInvoiceCurrency,
            ClassifiedAtUtc = nowUtc
        });
    }

    // ── Helpers (the same conventions as the sibling operation-invoice controllers) ─────────

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
            Detail = "Apenas o Financeiro ou o Administrador de Sistema podem classificar o documento " +
                     "de origem de um grupo.",
            Status = 403
        });
    }

    private async Task<Request?> LoadScopedRequestAsync(Guid requestId) =>
        await (await GetScopedRequestsQuery())
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .FirstOrDefaultAsync(r => r.Id == requestId);

    private static ProblemDetails Problem404(string detail = "Pedido não encontrado.") =>
        new() { Title = "Não encontrado", Detail = detail, Status = 404 };

    private static bool StatusIs(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
