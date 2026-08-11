namespace AlplaPortal.Api.Controllers;

using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Validation;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Manages ApprovalBatch operations for partial quotation approval.
/// QUOTATION requests only — PAYMENT requests are rejected with 400.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/requests/{requestId:guid}/batches")]
public class ApprovalBatchController : BaseController
{
    private readonly ILogger<ApprovalBatchController> _logger;
    private readonly IRequestStatusSyncService _statusSyncService;
    private readonly IGroupBuilderService _groupBuilderService;
    private readonly IApprovalRoutingService _approvalRouting;
    private readonly IQuotationItemEligibilityService _quotationEligibility;
    private readonly IBatchExtraItemDecisionService _extraItemDecisionService;

    public ApprovalBatchController(
        ApplicationDbContext context,
        ILogger<ApprovalBatchController> logger,
        IRequestStatusSyncService statusSyncService,
        IGroupBuilderService groupBuilderService,
        IApprovalRoutingService approvalRouting,
        IQuotationItemEligibilityService quotationEligibility,
        IBatchExtraItemDecisionService extraItemDecisionService) : base(context)
    {
        _logger = logger;
        _statusSyncService = statusSyncService;
        _groupBuilderService = groupBuilderService;
        _approvalRouting = approvalRouting;
        _quotationEligibility = quotationEligibility;
        _extraItemDecisionService = extraItemDecisionService;
    }

    /// <summary>
    /// Translates an unsuccessful IBatchExtraItemDecisionService result into the matching
    /// structured conflict/validation response — shared by CreateBatch and UpdateBatch so the
    /// two call sites can never present this differently.
    /// </summary>
    private static IActionResult ExtraItemDecisionErrorResult(ExtraItemDecisionApplyResult result)
    {
        if (result.PendingItems.Count > 0)
        {
            var pd = new ProblemDetails
            {
                Title = "Itens Adicionais Pendentes de Decisão",
                Detail = "Existem linhas da cotação classificadas como Item Adicional que ainda não foram incluídas ou excluídas deste lote.",
                Status = 409
            };
            pd.Extensions["code"] = "EXTRA_ITEMS_PENDING_DECISION";
            pd.Extensions["pendingItems"] = result.PendingItems.Select(p => new
            {
                quotationItemId = p.QuotationItemId,
                description = p.Description,
                quotationDocumentNumber = p.QuotationDocumentNumber,
                supplierName = p.SupplierName
            }).ToList();
            return new ConflictObjectResult(pd);
        }

        if (result.ReversalLocked)
        {
            var pd = new ProblemDetails
            {
                Title = "Reversão de Item Adicional Bloqueada",
                Detail = result.ReversalLockedReason,
                Status = 409
            };
            pd.Extensions["code"] = "EXTRA_ITEM_INCLUSION_LOCKED";
            pd.Extensions["reason"] = result.ReversalLockedReason;
            pd.Extensions["quotationItemId"] = result.ReversalLockedQuotationItemId;
            return new ConflictObjectResult(pd);
        }

        return new BadRequestObjectResult(new ProblemDetails
        {
            Title = "Validação de Decisão de Item Adicional Falhou",
            Detail = result.ValidationErrorMessage,
            Status = 400
        });
    }

    /// <summary>
    /// Structured 409 for Option C (no silent reuse): a quotation item used in a CANCELLED batch
    /// cannot be selected again without an explicit, active Buyer reuse authorization.
    /// </summary>
    internal static ObjectResult ReuseNotAuthorizedConflict(IReadOnlyList<QuotationItemEligibility> blocked)
    {
        var pd = new ProblemDetails
        {
            Title = "Reuso de cotação não autorizado",
            Detail = "Este item de cotação foi utilizado num lote de aprovação cancelado e exige autorização explícita de reuso antes de poder ser selecionado novamente.",
            Status = 409
        };
        pd.Extensions["code"] = "QUOTATION_REUSE_NOT_AUTHORIZED";
        pd.Extensions["blockedItems"] = blocked.Select(b => new
        {
            quotationItemId = b.QuotationItemId,
            quotationId = b.QuotationId,
            sourceCancelledBatchId = b.SourceCancelledBatchId,
            sourceCancelledBatchNumber = b.SourceCancelledBatchNumber,
            reasonCode = b.ReasonCode
        }).ToList();
        return new ConflictObjectResult(pd);
    }

    /// <summary>
    /// Phase B area-approval authorization for batch actions: admin, active manager of
    /// the request's department/plant (specific or global — D1), or the legacy nominee
    /// on an old in-flight request. The manual "Area Approver" role grants nothing.
    /// A batch always belongs to a single request, so the per-request check covers the
    /// whole batch.
    /// </summary>
    private async Task<bool> CanActAsAreaManagerAsync(Guid actorId, Request request)
    {
        if (CurrentUserRoles.Contains(RoleConstants.SystemAdministrator)) return true;
        // Phase C — TEMPORARY legacy-nominee compatibility (pre-Phase-B requests only:
        // post-cut requests reach the area stage with AreaApproverId == null, so no new
        // request ever satisfies this). The area-stage scoping is guaranteed by the
        // callers (batch loaded/validated in WAITING_AREA_APPROVAL). Remove when PROD
        // has no pending nominated requests (redesign plan §16.1 audit query).
        if (request.AreaApproverId == actorId) return true;
        return await _approvalRouting.IsAreaManagerAsync(actorId, request.DepartmentId, request.PlantId);
    }

    /// <summary>One validated winner decision: the batch item plus either its Area-selected
    /// candidate (candidate model) or nothing (legacy item whose buyer-selected winner stands).</summary>
    private sealed record ResolvedWinner(ApprovalBatchItem Item, ApprovalBatchItemCandidate? Candidate, string? Justification)
    {
        public Guid WinningQuotationItemId => Candidate?.QuotationItemId ?? Item.SelectedQuotationItemId!.Value;
    }

    /// <summary>
    /// Validates the Area Approver's winner selections against the batch (candidate model).
    /// Pure validation — mutates nothing. Enforces, per batch item with candidates:
    /// exactly one selection, candidate belongs to that item, and a meaningful justification when
    /// the choice is more expensive than the cheapest same-currency candidate beyond the
    /// FinancialIntegrity tolerance. Legacy items (zero candidates) must carry their historical
    /// buyer-selected winner and accept no selection. ALL-OR-RETURN: any gap fails the whole call.
    /// </summary>
    private static IActionResult? ResolveWinnerSelections(
        ApprovalBatch batch,
        List<BatchWinnerSelectionDto>? selections,
        out List<ResolvedWinner> resolvedWinners)
    {
        resolvedWinners = new List<ResolvedWinner>();
        var provided = selections ?? new List<BatchWinnerSelectionDto>();

        var duplicated = provided.GroupBy(s => s.ApprovalBatchItemId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicated.Any())
        {
            return new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Seleção de Vencedores Inválida",
                Detail = "Existe mais de uma seleção de vencedor para o mesmo item do lote.",
                Status = 400
            });
        }

        var itemIds = batch.Items.Select(i => i.Id).ToHashSet();
        var unknown = provided.Where(s => !itemIds.Contains(s.ApprovalBatchItemId)).ToList();
        if (unknown.Any())
        {
            return new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Seleção de Vencedores Inválida",
                Detail = "Uma ou mais seleções referem itens que não pertencem a este lote.",
                Status = 400
            });
        }

        var selectionByItem = provided.ToDictionary(s => s.ApprovalBatchItemId);
        var missing = new List<int>();

        foreach (var item in batch.Items)
        {
            selectionByItem.TryGetValue(item.Id, out var selection);

            if (item.Candidates.Count == 0)
            {
                // Legacy buyer-selected item — its winner already exists and cannot be re-decided.
                if (selection != null)
                {
                    return new BadRequestObjectResult(new ProblemDetails
                    {
                        Title = "Seleção de Vencedores Inválida",
                        Detail = $"O item #{item.RequestLineItem?.LineNumber ?? 0} pertence a um lote do modelo anterior (vencedor definido pelo comprador) e não aceita seleção de vencedor.",
                        Status = 400
                    });
                }

                if (!item.SelectedQuotationItemId.HasValue)
                {
                    return new BadRequestObjectResult(new ProblemDetails
                    {
                        Title = "Estado do Lote Inválido",
                        Detail = $"O item #{item.RequestLineItem?.LineNumber ?? 0} não possui opções de cotação nem vencedor definido. Devolva o lote ao comprador para correção.",
                        Status = 400
                    });
                }

                resolvedWinners.Add(new ResolvedWinner(item, null, null));
                continue;
            }

            // Candidate-based item: ALL-OR-RETURN — a selection is mandatory.
            if (selection == null)
            {
                missing.Add(item.RequestLineItem?.LineNumber ?? 0);
                continue;
            }

            var candidate = item.Candidates.FirstOrDefault(c => c.Id == selection.SelectedCandidateId);
            if (candidate == null)
            {
                return new BadRequestObjectResult(new ProblemDetails
                {
                    Title = "Seleção de Vencedores Inválida",
                    Detail = $"A opção selecionada para o item #{item.RequestLineItem?.LineNumber ?? 0} não pertence a este item do lote.",
                    Status = 400
                });
            }

            // Non-cheapest rule: compare against the cheapest SAME-CURRENCY candidate of this
            // item using the existing FinancialIntegrity tolerance. Ties within tolerance need no
            // justification; a supplied justification is persisted either way.
            var justification = string.IsNullOrWhiteSpace(selection.WinnerSelectionJustification)
                ? null
                : selection.WinnerSelectionJustification.Trim();

            var cheapestSameCurrency = item.Candidates
                .Where(c => c.Currency == candidate.Currency)
                .Min(c => c.LineTotal);
            var tolerance = RequestConstants.FinancialIntegrity.CalculateTolerance(cheapestSameCurrency);
            var isMoreExpensive = candidate.LineTotal > cheapestSameCurrency + tolerance;

            if (isMoreExpensive && !ReconciliationJustificationValidator.IsValid(justification, out var justError))
            {
                return new BadRequestObjectResult(new ProblemDetails
                {
                    Title = "Justificativa de Escolha Obrigatória",
                    Detail = $"A opção selecionada para o item #{item.RequestLineItem?.LineNumber ?? 0} ({candidate.SupplierNameSnapshot} — {candidate.LineTotal:N2} {candidate.Currency}) não é a de menor valor ({cheapestSameCurrency:N2} {candidate.Currency}). {justError}",
                    Status = 400
                });
            }

            resolvedWinners.Add(new ResolvedWinner(item, candidate, justification));
        }

        if (missing.Any())
        {
            return new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Seleção de Vencedores Incompleta",
                Detail = $"Todos os itens do lote devem ter exatamente um vencedor selecionado. Itens sem seleção: #{string.Join(", #", missing.OrderBy(n => n))}. Se algum item não puder ser decidido, devolva o lote para reajuste.",
                Status = 400
            });
        }

        return null;
    }

    /// <summary>
    /// Creates a new ApprovalBatch with buyer-selected winners for the specified request.
    /// Sets included items to BATCH_ASSIGNED and creates a RequestStatusHistory entry.
    /// Does NOT change Request.StatusId (deferred to Phase 3).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateBatch(Guid requestId, [FromBody] CreateApprovalBatchDto dto)
    {
        var actorId = CurrentUserId;

        // ── 1. Load request with required navigations ──
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .Include(r => r.ApprovalBatches)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // ── 2. Scope check ──
        var scopedQuery = await GetScopedRequestsQuery();
        var hasScope = await scopedQuery.AnyAsync(r => r.Id == requestId);
        if (!hasScope)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // ── 3. QUOTATION only — reject PAYMENT ──
        if (request.RequestType!.Code != RequestConstants.Types.Quotation)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tipo de pedido inválido.",
                Detail = "Lotes de aprovação parcial são suportados apenas para pedidos de Cotação. Pedidos de Pagamento utilizam o fluxo de aprovação padrão.",
                Status = 400
            });
        }

        // ── 4. At least 1 item required ──
        if (dto.Items == null || dto.Items.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Nenhum item informado.",
                Detail = "O lote deve conter pelo menos um item.",
                Status = 400
            });
        }

        // ── 5. Validate each item ──
        var errors = new List<string>();
        var lineItemIds = dto.Items.Select(i => i.RequestLineItemId).ToList();
        var quotationItemIds = dto.Items
            .SelectMany(i => i.Candidates ?? new List<BatchCandidateInputDto>())
            .Select(c => c.QuotationItemId)
            .Distinct()
            .ToList();

        // Check for duplicates within the request
        var duplicateLineItems = lineItemIds.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateLineItems.Any())
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Itens duplicados no lote.",
                Detail = $"Os seguintes itens aparecem mais de uma vez: {string.Join(", ", duplicateLineItems)}",
                Status = 400
            });
        }

        // Load active line items for the request
        var requestLineItems = request.LineItems
            .Where(li => !li.IsDeleted)
            .ToDictionary(li => li.Id);

        // Load quotation items that belong to quotations of this request (Unit included so the
        // candidate snapshot can freeze the unit text server-side)
        var quotationItems = await _context.Set<QuotationItem>()
            .Include(qi => qi.Quotation)
                .ThenInclude(q => q.Supplier)
            .Include(qi => qi.Unit)
            .Where(qi => qi.Quotation.RequestId == requestId
                         && quotationItemIds.Contains(qi.Id))
            .ToListAsync();

        var quotationItemMap = quotationItems.ToDictionary(qi => qi.Id);

        // Determine which items are already in an active batch
        var activeBatchStatuses = new[]
        {
            RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
            RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
            RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
            RequestConstants.ApprovalBatchStatuses.FinalAdjustment,
            RequestConstants.ApprovalBatchStatuses.Approved
        };

        var itemsInActiveBatches = await _context.Set<ApprovalBatchItem>()
            .Where(bi => bi.ApprovalBatch.RequestId == requestId
                         && activeBatchStatuses.Contains(bi.ApprovalBatch.Status)
                         && lineItemIds.Contains(bi.RequestLineItemId))
            .Select(bi => bi.RequestLineItemId)
            .ToListAsync();

        var itemsInActiveBatchSet = new HashSet<Guid>(itemsInActiveBatches);

        // Validate each item and its candidate options
        foreach (var item in dto.Items)
        {
            // 5a. RequestLineItemId must belong to request and not be deleted
            if (!requestLineItems.TryGetValue(item.RequestLineItemId, out var lineItem))
            {
                errors.Add($"Item {item.RequestLineItemId} não pertence a este pedido ou foi excluído.");
                continue;
            }

            // 5b. QuotationLifecycleStatus must be null or QUOTATION_PENDING
            var lifecycle = lineItem.QuotationLifecycleStatus;
            if (lifecycle != null && lifecycle != RequestConstants.QuotationLifecycleStatuses.QuotationPending)
            {
                errors.Add($"Item '{lineItem.Description}' (Linha {lineItem.LineNumber}) não está disponível para inclusão em lote. Status atual: {lifecycle}.");
                continue;
            }

            // 5c. Item not already in another active batch
            if (itemsInActiveBatchSet.Contains(item.RequestLineItemId))
            {
                errors.Add($"Item '{lineItem.Description}' (Linha {lineItem.LineNumber}) já está em outro lote ativo.");
                continue;
            }

            // 5d. At least one candidate option per included item (candidate model — the Buyer
            // submits options; there is no winner at creation time)
            if (item.Candidates == null || item.Candidates.Count == 0)
            {
                errors.Add($"Item '{lineItem.Description}' (Linha {lineItem.LineNumber}) não possui nenhuma opção de cotação para envio à aprovação.");
                continue;
            }

            // 5e. No duplicate candidate within the same item
            var duplicateCandidates = item.Candidates
                .GroupBy(c => c.QuotationItemId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicateCandidates.Any())
            {
                errors.Add($"Item '{lineItem.Description}' (Linha {lineItem.LineNumber}) possui opções de cotação duplicadas.");
                continue;
            }

            foreach (var candidate in item.Candidates)
            {
                // 5f. Candidate must belong to a quotation of this request
                if (!quotationItemMap.TryGetValue(candidate.QuotationItemId, out var quotationItem))
                {
                    errors.Add($"Item de cotação {candidate.QuotationItemId} não pertence a uma cotação deste pedido.");
                    continue;
                }

                // 5g. Candidate must be MAPPED or SUBSTITUTE
                var allowedStatuses = new[] { "MAPPED", "SUBSTITUTE" };
                if (!allowedStatuses.Contains(quotationItem.ReconciliationStatus))
                {
                    errors.Add($"Item de cotação '{quotationItem.Description}' tem status '{quotationItem.ReconciliationStatus}' e não pode ser enviado como opção. Apenas itens MAPPED ou SUBSTITUTE são permitidos.");
                    continue;
                }

                // 5h. Candidate must be mapped to this specific RequestLineItem
                if (quotationItem.MappedRequestLineItemId != item.RequestLineItemId)
                {
                    errors.Add($"Item de cotação '{quotationItem.Description}' não está mapeado para o item '{lineItem.Description}' (Linha {lineItem.LineNumber}).");
                }
            }
        }

        if (errors.Any())
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Erros de validação do lote.",
                Detail = string.Join(" | ", errors),
                Status = 400
            });
        }

        // ── 5g. Cancelled-batch reuse rule (Option C): items used in a CANCELLED batch need an
        // explicit, active Buyer authorization; authorized ones are consumed atomically below.
        var (reuseBlocked, reuseAuthorized) = await _quotationEligibility.ValidateSelectionAsync(requestId, quotationItemIds);
        if (reuseBlocked.Count > 0)
        {
            return ReuseNotAuthorizedConflict(reuseBlocked);
        }

        // ── 5.1. Phase B routing guard: a batch enters WAITING_AREA_APPROVAL, so at
        // least one area manager must be resolvable for the request's department/plant.
        // Department.ResponsibleUserId is no longer consulted.
        var areaRouting = await _approvalRouting.ResolveAreaManagersAsync(request.DepartmentId, request.PlantId);
        if (!areaRouting.HasManagers)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Erro de Validação",
                Detail = "Não existe responsável de aprovação configurado para o departamento/planta deste pedido. Configure os managers em Dados Mestres → Departamentos.",
                Status = 400
            });
        }

        // ── 6. Create ApprovalBatch ──
        var maxBatchNumber = request.ApprovalBatches.Any()
            ? request.ApprovalBatches.Max(b => b.BatchNumber)
            : 0;

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            BatchNumber = maxBatchNumber + 1,
            Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval,
            Comment = dto.Comment,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorId
        };

        _context.ApprovalBatches.Add(batch);

        // ── 7. Create ApprovalBatchItems with frozen candidate snapshots. The winner fields stay
        // NULL by construction — the Buyer endpoint carries no winner-selection input at all
        // (structural authorization guarantee: only area approval writes them). ──
        foreach (var item in dto.Items)
        {
            var batchItem = new ApprovalBatchItem
            {
                Id = Guid.NewGuid(),
                ApprovalBatchId = batch.Id,
                RequestLineItemId = item.RequestLineItemId,
                SelectedQuotationItemId = null,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.ApprovalBatchItems.Add(batchItem);

            foreach (var candidate in item.Candidates)
            {
                _context.ApprovalBatchItemCandidates.Add(ApprovalBatchCandidateSnapshotFactory.Create(
                    quotationItemMap[candidate.QuotationItemId], batchItem.Id, candidate.BuyerNote,
                    actorId, DateTime.UtcNow));
            }

            // Update lifecycle status to BATCH_ASSIGNED
            var lineItem = requestLineItems[item.RequestLineItemId];
            lineItem.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.BatchAssigned;
            lineItem.UpdatedAtUtc = DateTime.UtcNow;
            lineItem.UpdatedByUserId = actorId;
            _context.Entry(lineItem).State = EntityState.Modified;

            // Candidate-submission audit: which options the Buyer put forward for this item.
            var candidateSummary = string.Join("; ", item.Candidates.Select(c =>
            {
                var qi = quotationItemMap[c.QuotationItemId];
                return $"'{qi.Description}' ({qi.Quotation.SupplierNameSnapshot} — {qi.LineTotal:N2} {qi.Quotation.Currency})";
            }));
            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = requestId,
                ActorUserId = actorId,
                ActionTaken = "BATCH_CANDIDATES_SUBMITTED",
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = $"[Lote #{batch.BatchNumber}] Item #{lineItem.LineNumber} — {item.Candidates.Count} opção(ões) enviadas para aprovação: {candidateSummary}",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // ── 7.05. Buyer batch-composition decision for genuine EXTRA_ITEM lines belonging to the
        // quotation(s) contributing at least one CANDIDATE to this batch (shared service —
        // identical rule for CreateBatch and UpdateBatch). Nothing has been SaveChanges'd yet, so
        // a failure here aborts cleanly with no persistence. ──
        var extraItemResult = await _extraItemDecisionService.ApplyAsync(
            request, batch, quotationItemIds, dto.ExtraItemDecisions, actorId);
        if (!extraItemResult.Success)
        {
            return ExtraItemDecisionErrorResult(extraItemResult);
        }

        // ── 7.1. Consume reuse authorizations in the SAME transaction as the batch (Option C):
        // each authorized reused item flips its authorization to consumed and records provenance.
        // If SaveChanges fails, everything (batch + consumption + history) rolls back together.
        if (reuseAuthorized.Count > 0)
        {
            var authIds = reuseAuthorized.Select(a => a.ReuseAuthorizationId!.Value).ToList();
            var authEntities = await _context.QuotationReuseAuthorizations
                .Where(a => authIds.Contains(a.Id))
                .ToListAsync();
            var authorizerIds = authEntities.Select(a => a.AuthorizedByUserId).Distinct().ToList();
            var authorizerNames = await _context.Users.AsNoTracking()
                .Where(u => authorizerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName);

            foreach (var auth in authEntities)
            {
                auth.IsActive = false;
                auth.ConsumedByApprovalBatchId = batch.Id;
                auth.ConsumedAtUtc = DateTime.UtcNow;

                var elig = reuseAuthorized.First(a => a.ReuseAuthorizationId == auth.Id);
                quotationItemMap.TryGetValue(auth.QuotationItemId, out var qi);
                var lineItemId = dto.Items
                    .First(i => i.Candidates.Any(c => c.QuotationItemId == auth.QuotationItemId))
                    .RequestLineItemId;
                var authorizer = authorizerNames.TryGetValue(auth.AuthorizedByUserId, out var n) ? n : auth.AuthorizedByUserId.ToString();

                _context.RequestStatusHistories.Add(new RequestStatusHistory
                {
                    Id = Guid.NewGuid(),
                    RequestId = requestId,
                    ActorUserId = actorId,
                    ActionTaken = "QUOTATION_REUSED_IN_NEW_BATCH",
                    PreviousStatusId = request.StatusId,
                    NewStatusId = request.StatusId,
                    Comment = $"Item de cotação '{qi?.Description}' ({qi?.Quotation?.DocumentNumber} — {qi?.Quotation?.SupplierNameSnapshot}) reutilizado do Lote #{elig.SourceCancelledBatchNumber} (cancelado) no Lote #{batch.BatchNumber}. Item do pedido: {lineItemId}. Autorizado por {authorizer} em {auth.AuthorizedAtUtc:dd/MM/yyyy HH:mm}. Motivo da autorização: {auth.Reason}",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        // ── 8. Sync Request.StatusId (legacy compatibility) ──
        await _statusSyncService.SyncStatusAsync(requestId, actorId);

        // ── 9. Create RequestStatusHistory entry (BATCH_CREATED) ──
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "BATCH_CREATED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Lote #{batch.BatchNumber} criado com {dto.Items.Count} item(ns) e {dto.Items.Sum(i => i.Candidates.Count)} opção(ões) de cotação. Vencedores a definir pelo Aprovador de Área.",
            CreatedAtUtc = DateTime.UtcNow
        });

        // ── 10. Persist ──
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create approval batch for request {RequestId}", requestId);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Erro ao criar lote de aprovação.",
                Detail = "Ocorreu um erro interno. Por favor, tente novamente.",
                Status = 500
            });
        }

        _logger.LogInformation(
            "Approval batch {BatchId} (#{BatchNumber}) created for request {RequestId} with {ItemCount} items by user {ActorId}",
            batch.Id, batch.BatchNumber, requestId, dto.Items.Count, actorId);

        // ── 10. Return batch detail ──
        return Ok(await BuildBatchDto(batch.Id));
    }

    /// <summary>
    /// Lists all ApprovalBatches for a request.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBatches(Guid requestId)
    {
        // Scope check
        var scopedQuery = await GetScopedRequestsQuery();
        var hasScope = await scopedQuery.AnyAsync(r => r.Id == requestId);
        if (!hasScope)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        var batches = await _context.ApprovalBatches
            .AsNoTracking()
            .Where(b => b.RequestId == requestId)
            .OrderBy(b => b.BatchNumber)
            .Select(b => new ApprovalBatchListItemDto
            {
                Id = b.Id,
                BatchNumber = b.BatchNumber,
                Status = b.Status,
                Comment = b.Comment,
                ItemCount = b.Items.Count,
                ApprovedTotalAmount = b.ApprovedTotalAmount,
                CreatedAtUtc = b.CreatedAtUtc,
                CreatedByUserId = b.CreatedByUserId,
                CreatedByUserName = _context.Users
                    .Where(u => u.Id == b.CreatedByUserId)
                    .Select(u => u.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(batches);
    }

    /// <summary>
    /// Gets detailed information about a single ApprovalBatch including its items.
    /// </summary>
    [HttpGet("{batchId:guid}")]
    public async Task<IActionResult> GetBatchDetail(Guid requestId, Guid batchId)
    {
        // Scope check
        var scopedQuery = await GetScopedRequestsQuery();
        var hasScope = await scopedQuery.AnyAsync(r => r.Id == requestId);
        if (!hasScope)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        var batchDto = await BuildBatchDto(batchId, requestId);
        if (batchDto == null)
            return NotFound(new ProblemDetails { Title = "Lote não encontrado.", Status = 404 });

        return Ok(batchDto);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Phase 4: Batch-Aware Area Approval Endpoints
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Area-approves a batch: applies allocations, processes extras, builds PO groups,
    /// sets batch to WAITING_FINAL_APPROVAL. Winners from ApprovalBatchItem.
    /// </summary>
    [HttpPost("{batchId:guid}/area-approval/approve")]
    public async Task<IActionResult> BatchAreaApprove(Guid requestId, Guid batchId, [FromBody] BatchApprovalActionDto dto)
    {
        var actorId = CurrentUserId;

        // ── 1. Load request ──
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
                .ThenInclude(li => li.Allocations)
            .Include(r => r.Quotations)
                .ThenInclude(q => q.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // ── 2. Authorization (Phase B — DepartmentManager titularity, D1) ──
        if (!await CanActAsAreaManagerAsync(actorId, request))
            return StatusCode(403, "Você não é responsável pelo departamento/planta deste pedido.");

        // ── 3. Scope check ──
        var scopedQuery = await GetScopedRequestsQuery();
        if (!await scopedQuery.AnyAsync(r => r.Id == requestId))
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // ── 4. QUOTATION only ──
        if (request.RequestType?.Code != RequestConstants.Types.Quotation)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Aprovação por lote é permitida apenas para pedidos de Cotação.", Status = 400 });

        // ── 5. Load batch ──
        var batch = await _context.ApprovalBatches
            .Include(b => b.Items)
                .ThenInclude(bi => bi.RequestLineItem)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.Candidates)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == batchId && b.RequestId == requestId);

        if (batch == null)
            return NotFound(new ProblemDetails { Title = "Lote não encontrado.", Status = 404 });

        if (batch.Status != RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = $"O lote não está em fase de aprovação da área (status atual: {batch.Status}).", Status = 400 });

        // ── 5.1. Resolve the winner for every batch item (candidate model). ALL-OR-RETURN: a
        // candidate-based item without a valid selection fails the whole call before anything is
        // mutated. Legacy items (zero candidates, buyer-selected winner already populated) need —
        // and accept — no selection. The client contributes only candidate IDENTITY and an
        // optional justification; every commercial value comes from the frozen snapshot. ──
        var selectionError = ResolveWinnerSelections(batch, dto.Selections, out var resolvedWinners);
        if (selectionError != null)
            return selectionError;

        // ── 6. Extra-item decisions are no longer made here. By the time a batch reaches
        // WAITING_AREA_APPROVAL, every genuine EXTRA_ITEM line's INCLUDE/EXCLUDE decision was
        // already finalized by the Buyer at CreateBatch/UpdateBatch time (IBatchExtraItemDecisionService)
        // — the Area Approver reviews the resulting, already-resolved batch, never decides scope.
        //
        // Server-side safety net (mirrors the wizard's client-side block): a batch created before
        // that rule existed can still carry genuine EXTRA_ITEM lines with no recorded decision at
        // all. Approving over them would silently drop supplier-document lines from the process,
        // so the batch must go back to the Buyer for rework instead. Runs before any allocation
        // processing or other mutation — nothing is persisted when this returns.
        var contributingQuotationItemIds = batch.Items
            .SelectMany(bi => bi.Candidates.Select(c => c.QuotationItemId))
            .Concat(batch.Items.Where(bi => bi.SelectedQuotationItemId.HasValue)
                .Select(bi => bi.SelectedQuotationItemId!.Value))
            .Distinct()
            .ToList();
        var unresolvedCheck = await _extraItemDecisionService.GetInformationalLinesAsync(
            batchId, contributingQuotationItemIds);
        if (unresolvedCheck.UnresolvedLegacyLines.Count > 0)
        {
            var pd = new ProblemDetails
            {
                Title = "Lote com Itens Adicionais Não Resolvidos",
                Detail = "Este lote foi criado antes da regra de decisão de itens adicionais e possui linhas de cotação sem decisão registrada do comprador. Solicite reajuste para que o comprador resolva estas linhas antes da aprovação.",
                Status = 409
            };
            pd.Extensions["code"] = "BATCH_HAS_UNRESOLVED_LEGACY_LINES";
            pd.Extensions["pendingItems"] = unresolvedCheck.UnresolvedLegacyLines.Select(l => new
            {
                quotationItemId = l.QuotationItemId,
                description = l.Description,
                supplierName = l.SupplierName,
                quotationDocumentNumber = l.QuotationDocumentNumber
            }).ToList();
            return Conflict(pd);
        }

        var activeItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
        var batchItemRliIds = batch.Items.Select(bi => bi.RequestLineItemId).ToHashSet();

        // ── 7. Build itemAwards from the RESOLVED winners (Area selections for candidate items,
        // the historical buyer-selected pointer for legacy items) ──
        var itemAwards = new Dictionary<Guid, Guid>();
        foreach (var rw in resolvedWinners)
        {
            itemAwards[rw.Item.RequestLineItemId] = rw.WinningQuotationItemId;
        }

        // ── 8. Apply 3-tier allocation fallback (batch items only) ──
        var batchLineItems = activeItems.Where(li => batchItemRliIds.Contains(li.Id)).ToList();

        foreach (var item in batchLineItems)
        {
            var allocsToSave = new List<RequestLineItemAllocation>();

            if (dto.ItemAllocations != null && dto.ItemAllocations.TryGetValue(item.Id, out var allocLines) && allocLines.Any())
            {
                // Tier 1: ItemAllocations present
                if (allocLines.Count > 10)
                    return BadRequest(new ProblemDetails { Title = "Excesso de Alocações", Detail = $"O item #{item.LineNumber} possui mais de 10 alocações.", Status = 400 });

                if (allocLines.Any(a => a.Percentage <= 0))
                    return BadRequest(new ProblemDetails { Title = "Percentagem Inválida", Detail = $"O item #{item.LineNumber} possui alocações com percentagem igual ou inferior a zero.", Status = 400 });

                var totalPercent = allocLines.Sum(a => a.Percentage);
                if (totalPercent != 100m)
                    return BadRequest(new ProblemDetails { Title = "Percentagem Inválida", Detail = $"A soma das percentagens do item #{item.LineNumber} deve ser 100% (atual: {totalPercent}%).", Status = 400 });

                int order = 0;
                foreach (var al in allocLines)
                {
                    allocsToSave.Add(new RequestLineItemAllocation
                    {
                        Id = Guid.NewGuid(),
                        RequestLineItemId = item.Id,
                        PlantId = al.PlantId,
                        CostCenterId = al.CostCenterId,
                        Percentage = al.Percentage,
                        AllocationOrder = order++,
                        Comment = al.Comment,
                        CreatedAtUtc = DateTime.UtcNow,
                        CreatedByUserId = actorId
                    });
                }
            }
            else if (dto.ItemAssignments != null && dto.ItemAssignments.TryGetValue(item.Id, out var assignment) && assignment.PlantId.HasValue && assignment.PlantId > 0 && assignment.CostCenterId.HasValue && assignment.CostCenterId > 0)
            {
                // Tier 2: ItemAssignments present
                allocsToSave.Add(new RequestLineItemAllocation
                {
                    Id = Guid.NewGuid(),
                    RequestLineItemId = item.Id,
                    PlantId = assignment.PlantId.Value,
                    CostCenterId = assignment.CostCenterId.Value,
                    Percentage = 100m,
                    AllocationOrder = 0,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedByUserId = actorId
                });
            }
            else
            {
                // Tier 3: Reject missing allocation
                return BadRequest(new ProblemDetails
                {
                    Title = "Atribuição de Item Incompleta",
                    Detail = $"O item #{item.LineNumber} está sem alocação financeira definida. A aprovação exige a definição de Planta e Centro de Custo para todos os itens do lote.",
                    Status = 400
                });
            }

            // Clear existing allocations and insert new ones
            if (item.Allocations == null) item.Allocations = new List<RequestLineItemAllocation>();
            _context.RequestLineItemAllocations.RemoveRange(item.Allocations);
            item.Allocations.Clear();

            foreach (var a in allocsToSave)
            {
                _context.RequestLineItemAllocations.Add(a);
                item.Allocations.Add(a);
            }

            // Legacy fields sync — highest allocation determines PlantId/CostCenterId
            var highestAlloc = allocsToSave.OrderByDescending(a => a.Percentage).ThenBy(a => a.AllocationOrder).ThenBy(a => a.CostCenterId).First();
            item.PlantId = highestAlloc.PlantId;
            item.CostCenterId = highestAlloc.CostCenterId;
        }

        // ── 9. Budget verification (batch-scoped) ──
        var overallStatus = await BudgetCalculationHelper.EvaluateOverallBudgetStatusAsync(_context, request, itemAwards, dto.ItemAssignments, dto.ItemAllocations);
        var criticalStatuses = new[] { "CRITICAL", "OVER_BUDGET", "NO_BUDGET", "CURRENCY_MISMATCH" };
        if (criticalStatuses.Contains(overallStatus))
        {
            if (string.IsNullOrWhiteSpace(dto.BudgetJustification) || dto.BudgetJustification.Trim().Length < 20)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Justificativa de Orçamento Obrigatória",
                    Detail = "O impacto orçamental deste lote é Crítico ou Acima do Orçamento. É obrigatório fornecer uma justificativa orçamental com pelo menos 20 caracteres.",
                    Status = 400
                });
            }
        }

        // ── 10. Reassignment validation ──
        if (dto.Reassignments != null && dto.Reassignments.Any())
        {
            var allowedPlantIds = await _context.UserPlantScopes
                .Where(s => s.UserId == CurrentUserId)
                .Select(s => s.PlantId)
                .ToListAsync();

            foreach (var reassignment in dto.Reassignments)
            {
                if (string.IsNullOrWhiteSpace(reassignment.Reason) || reassignment.Reason.Trim().Length < 20)
                    return BadRequest(new ProblemDetails { Title = "Motivo de Alteração Obrigatório", Detail = "É obrigatório fornecer um motivo válido (mínimo 20 caracteres) para a alteração do centro de custo.", Status = 400 });

                if (!CurrentUserRoles.Contains(RoleConstants.SystemAdministrator) && !allowedPlantIds.Contains(reassignment.NewPlantId))
                    return StatusCode(403, "Você não tem permissão para realocar itens para a planta selecionada.");

                var cc = await _context.CostCenters.FirstOrDefaultAsync(c => c.Id == reassignment.NewCostCenterId && c.PlantId == reassignment.NewPlantId);
                if (cc == null && reassignment.NewCostCenterId.HasValue)
                    return BadRequest(new ProblemDetails { Title = "Centro de Custo Inválido", Detail = "O centro de custo selecionado não pertence à planta informada ou não existe.", Status = 400 });

                // Validate items belong to this batch
                var invalidItems = reassignment.AffectedItemIds.Where(id => !batchItemRliIds.Contains(id)).ToList();
                if (invalidItems.Any())
                    return BadRequest(new ProblemDetails { Title = "Itens Inválidos", Detail = "Um ou mais itens afetados pela realocação não pertencem a este lote.", Status = 400 });

                var oldPlant = await _context.Plants.FindAsync(reassignment.OldPlantId);
                var oldCc = reassignment.OldCostCenterId.HasValue ? await _context.CostCenters.FindAsync(reassignment.OldCostCenterId) : null;
                var newPlant = await _context.Plants.FindAsync(reassignment.NewPlantId);

                _context.RequestStatusHistories.Add(new RequestStatusHistory
                {
                    Id = Guid.NewGuid(),
                    RequestId = requestId,
                    ActorUserId = actorId,
                    ActionTaken = "REALLOCATION",
                    PreviousStatusId = request.StatusId,
                    NewStatusId = request.StatusId,
                    Comment = $"[Lote #{batch.BatchNumber}] Alteração de Centro de Custo: De {oldPlant?.Name ?? "?"} / {oldCc?.Name ?? "Geral"} Para {newPlant?.Name ?? "?"} / {cc?.Name ?? "Geral"}. Itens: {reassignment.AffectedItemIds.Count}. Motivo: {reassignment.Reason.Trim()}",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        // ── 11. Stamp the winner decision on each batch item and the line-level compatibility
        // pointer — all in this same transaction (single SaveChanges below, §atomicity) ──
        var winnerStampUtc = DateTime.UtcNow;
        foreach (var rw in resolvedWinners)
        {
            if (rw.Candidate != null)
            {
                rw.Item.SelectedCandidateId = rw.Candidate.Id;
                rw.Item.SelectedQuotationItemId = rw.Candidate.QuotationItemId;
                rw.Item.WinnerSelectedByUserId = actorId;
                rw.Item.WinnerSelectedAtUtc = winnerStampUtc;
                rw.Item.WinnerSelectionJustification = rw.Justification;
            }

            var lineItem = activeItems.FirstOrDefault(li => li.Id == rw.Item.RequestLineItemId);
            if (lineItem != null)
                lineItem.SelectedQuotationItemId = rw.WinningQuotationItemId;
        }

        // ── 12. Update batch state ──
        batch.Status = RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval;
        batch.BudgetJustification = dto.BudgetJustification?.Trim();
        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedByUserId = actorId;
        request.AreaApproverId = actorId; // Phase B: records who actually decided the area stage

        // ── 13. History entry ──
        string historyComment = $"Aprovação da Área do Lote #{batch.BatchNumber} realizada. {dto.Comment}".Trim();
        // Keep the budget justification in the audit trail, mirroring the legacy
        // (non-batch) area-approval path which embeds it into the history comment.
        if (!string.IsNullOrWhiteSpace(dto.BudgetJustification))
        {
            historyComment += $"\nJustificativa orçamental: {dto.BudgetJustification.Trim()}";
        }
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "BATCH_AREA_APPROVED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = historyComment,
            CreatedAtUtc = DateTime.UtcNow
        });

        // Individual award history per batch item — the Area Approver is the deciding actor.
        foreach (var rw in resolvedWinners)
        {
            var lineNumber = rw.Item.RequestLineItem?.LineNumber ?? 0;
            var comment = rw.Candidate != null
                ? $"[Lote #{batch.BatchNumber}] Item #{lineNumber} - Vencedor selecionado pelo Aprovador de Área: '{rw.Candidate.QuotedDescription}' ({rw.Candidate.SupplierNameSnapshot} — {rw.Candidate.LineTotal:N2} {rw.Candidate.Currency})"
                  + (string.IsNullOrWhiteSpace(rw.Justification) ? string.Empty : $". Justificativa da escolha: {rw.Justification}")
                : $"[Lote #{batch.BatchNumber}] Item #{lineNumber} - Vencedor: {rw.WinningQuotationItemId} (lote legado — seleção do comprador)";

            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = requestId,
                ActorUserId = actorId,
                ActionTaken = WorkflowEventCodes.QuotationItemAwarded,
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = comment,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // ── 14. Build PO groups for this batch. Deliberately NO SaveChanges before this call:
        // the group builder shares this scoped DbContext, sees the stamped (tracked) winners, and
        // its single SaveChanges persists winner decisions + line pointers + history + status +
        // groups together — a selection failure or group-build failure persists nothing. ──
        await _groupBuilderService.BuildGroupsForBatchAsync(batchId);

        // ── 15. Sync request status ──
        await _statusSyncService.SyncStatusAsync(requestId, actorId);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Batch {BatchId} of Request {RequestId} area-approved by {ActorId}", batchId, requestId, actorId);

        return Ok(new { Message = $"Lote #{batch.BatchNumber} aprovado pela área com sucesso.", BatchId = batchId, Status = batch.Status });
    }

    /// <summary>
    /// Area-rejects a batch: returns items to QUOTATION_PENDING, clears allocations,
    /// defensively removes PO groups for this batch.
    /// </summary>
    [HttpPost("{batchId:guid}/area-approval/reject")]
    public async Task<IActionResult> BatchAreaReject(Guid requestId, Guid batchId, [FromBody] BatchApprovalActionDto dto)
    {
        var actorId = CurrentUserId;

        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "É necessário informar o motivo da rejeição.", Status = 400 });

        var (request, batch, error) = await LoadAndValidateBatch(requestId, batchId, RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval);
        if (error != null) return error;

        // Phase B authorization — DepartmentManager titularity (D1)
        if (!await CanActAsAreaManagerAsync(actorId, request!))
            return StatusCode(403, "Você não é responsável pelo departamento/planta deste pedido.");

        // ── Return batch items to QUOTATION_PENDING ──
        var batchItemRliIds = batch!.Items.Select(bi => bi.RequestLineItemId).ToHashSet();
        var batchLineItems = request!.LineItems.Where(li => batchItemRliIds.Contains(li.Id)).ToList();

        foreach (var item in batchLineItems)
        {
            item.QuotationLifecycleStatus = null;
            // Do NOT clear SelectedQuotationItemId — winner audit preserved in ApprovalBatchItem

            // Clear allocations (defensive)
            if (item.Allocations != null && item.Allocations.Any())
            {
                _context.RequestLineItemAllocations.RemoveRange(item.Allocations);
                item.Allocations.Clear();
            }

            // Clear PO group assignment (defensive)
            item.RequestPoGroupId = null;
        }

        // ── Defensive PO group cleanup ──
        var batchPoGroups = await _context.RequestPoGroups
            .Where(g => g.ApprovalBatchId == batchId)
            .ToListAsync();
        if (batchPoGroups.Any())
        {
            _context.RequestPoGroups.RemoveRange(batchPoGroups);
            _logger.LogWarning("BatchAreaReject: Removed {Count} PO groups for batch {BatchId} (defensive cleanup)", batchPoGroups.Count, batchId);
        }

        // ── Update batch state ──
        batch.Status = RequestConstants.ApprovalBatchStatuses.Rejected;
        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedByUserId = actorId;
        request!.AreaApproverId = actorId; // Phase B: records who actually decided the area stage

        // ── History entry ──
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "BATCH_AREA_REJECTED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Lote #{batch.BatchNumber} rejeitado na Aprovação da Área. Motivo: {dto.Comment}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        await _statusSyncService.SyncStatusAsync(requestId, actorId);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Batch {BatchId} of Request {RequestId} area-rejected by {ActorId}", batchId, requestId, actorId);

        return Ok(new { Message = $"Lote #{batch.BatchNumber} rejeitado com sucesso.", BatchId = batchId, Status = batch.Status });
    }

    /// <summary>
    /// Requests area adjustment (rework) for a batch: keeps items as BATCH_ASSIGNED,
    /// clears allocations, sets batch to AREA_ADJUSTMENT.
    /// </summary>
    [HttpPost("{batchId:guid}/area-approval/request-adjustment")]
    public async Task<IActionResult> BatchAreaRequestAdjustment(Guid requestId, Guid batchId, [FromBody] BatchApprovalActionDto dto)
    {
        var actorId = CurrentUserId;

        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "É necessário informar o motivo do reajuste.", Status = 400 });

        var (request, batch, error) = await LoadAndValidateBatch(requestId, batchId, RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval);
        if (error != null) return error;

        // Phase B authorization — DepartmentManager titularity (D1)
        if (!await CanActAsAreaManagerAsync(actorId, request!))
            return StatusCode(403, "Você não é responsável pelo departamento/planta deste pedido.");

        // ── Clear allocations for batch items ──
        var batchItemRliIds = batch!.Items.Select(bi => bi.RequestLineItemId).ToHashSet();
        var batchLineItems = request!.LineItems.Where(li => batchItemRliIds.Contains(li.Id)).ToList();

        foreach (var item in batchLineItems)
        {
            // Items stay BATCH_ASSIGNED
            if (item.Allocations != null && item.Allocations.Any())
            {
                _context.RequestLineItemAllocations.RemoveRange(item.Allocations);
                item.Allocations.Clear();
            }
            item.RequestPoGroupId = null;
        }

        // ── Defensive PO group cleanup ──
        var batchPoGroups = await _context.RequestPoGroups
            .Where(g => g.ApprovalBatchId == batchId)
            .ToListAsync();
        if (batchPoGroups.Any())
        {
            _context.RequestPoGroups.RemoveRange(batchPoGroups);
        }

        // ── Update batch state ──
        batch.Status = RequestConstants.ApprovalBatchStatuses.AreaAdjustment;
        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedByUserId = actorId;
        request!.AreaApproverId = actorId; // Phase B: records who actually decided the area stage

        // ── History entry ──
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "BATCH_AREA_ADJUSTMENT",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Solicitado reajuste no Lote #{batch.BatchNumber} na Aprovação da Área. Motivo: {dto.Comment}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        await _statusSyncService.SyncStatusAsync(requestId, actorId);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Batch {BatchId} of Request {RequestId} area-adjustment requested by {ActorId}", batchId, requestId, actorId);

        return Ok(new { Message = $"Lote #{batch.BatchNumber} devolvido para reajuste com sucesso.", BatchId = batchId, Status = batch.Status });
    }

    /// <summary>
    /// Edits a batch during rework (AREA_ADJUSTMENT or FINAL_ADJUSTMENT).
    /// Allows changing items, winners, and comment.
    /// </summary>
    [HttpPut("{batchId:guid}")]
    public async Task<IActionResult> UpdateBatch(Guid requestId, Guid batchId, [FromBody] UpdateApprovalBatchDto dto)
    {
        var actorId = CurrentUserId;

        // ── 1. Load request ──
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .Include(r => r.ApprovalBatches)
                .ThenInclude(b => b.Items)
                    .ThenInclude(i => i.Candidates)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        var scopedQuery = await GetScopedRequestsQuery();
        if (!await scopedQuery.AnyAsync(r => r.Id == requestId))
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        if (request.RequestType?.Code != RequestConstants.Types.Quotation)
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Edição de lote é permitida apenas para pedidos de Cotação.", Status = 400 });

        // ── 2. Validate batch ──
        var batch = request.ApprovalBatches.FirstOrDefault(b => b.Id == batchId);
        if (batch == null)
            return NotFound(new ProblemDetails { Title = "Lote não encontrado.", Status = 404 });

        var allowedStatuses = new[] { RequestConstants.ApprovalBatchStatuses.AreaAdjustment, RequestConstants.ApprovalBatchStatuses.FinalAdjustment };
        if (!allowedStatuses.Contains(batch.Status))
            return BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = $"O lote só pode ser editado em AREA_ADJUSTMENT ou FINAL_ADJUSTMENT (status atual: {batch.Status}).", Status = 400 });

        // ── 3. Validate items and candidate options ──
        if (dto.Items == null || !dto.Items.Any())
            return BadRequest(new ProblemDetails { Title = "Lote Vazio", Detail = "O lote deve conter pelo menos um item.", Status = 400 });

        // Load quotation items for validation and snapshot creation (Unit for the frozen unit text)
        var allQuotationItemIds = dto.Items
            .SelectMany(i => i.Candidates ?? new List<BatchCandidateInputDto>())
            .Select(c => c.QuotationItemId)
            .Distinct()
            .ToList();
        var quotationItemMap = await _context.QuotationItems
            .Include(qi => qi.Quotation)
                .ThenInclude(q => q.Supplier)
            .Include(qi => qi.Unit)
            .Where(qi => allQuotationItemIds.Contains(qi.Id) && qi.Quotation.RequestId == requestId)
            .ToDictionaryAsync(qi => qi.Id);

        var requestLineItemIds = dto.Items.Select(i => i.RequestLineItemId).ToHashSet();
        var errors = new List<string>();

        foreach (var item in dto.Items)
        {
            var lineItem = request.LineItems.FirstOrDefault(li => li.Id == item.RequestLineItemId && !li.IsDeleted);
            if (lineItem == null)
            {
                errors.Add($"Item {item.RequestLineItemId} não pertence a este pedido ou está removido.");
                continue;
            }

            if (item.Candidates == null || item.Candidates.Count == 0)
            {
                errors.Add($"Item {item.RequestLineItemId} não possui nenhuma opção de cotação para envio à aprovação.");
                continue;
            }

            if (item.Candidates.GroupBy(c => c.QuotationItemId).Any(g => g.Count() > 1))
            {
                errors.Add($"Item {item.RequestLineItemId} possui opções de cotação duplicadas.");
                continue;
            }

            // A buyer-included EXTRA_ITEM line was GENERATED from its quotation line, so that
            // line legitimately has no MappedRequestLineItemId and carries EXTRA_ITEM status —
            // it re-enters a rework payload as its own single fixed candidate.
            var isGeneratedExtraLine = lineItem.CreationOrigin == LineItemCreationOrigins.BuyerExtraItemIncluded;
            if (isGeneratedExtraLine && item.Candidates.Count > 1)
            {
                errors.Add($"O item adicional '{lineItem.Description}' aceita apenas a própria linha da cotação como opção.");
                continue;
            }

            foreach (var candidate in item.Candidates)
            {
                if (!quotationItemMap.TryGetValue(candidate.QuotationItemId, out var quotationItem))
                {
                    errors.Add($"Item de cotação {candidate.QuotationItemId} não pertence a uma cotação deste pedido.");
                    continue;
                }

                if (isGeneratedExtraLine)
                {
                    if (quotationItem.ReconciliationStatus != RequestConstants.ReconciliationStatuses.ExtraItem)
                    {
                        errors.Add($"O item adicional '{lineItem.Description}' só pode referenciar a linha EXTRA_ITEM que o originou.");
                    }
                    continue;
                }

                if (quotationItem.ReconciliationStatus != "MAPPED" && quotationItem.ReconciliationStatus != "SUBSTITUTE")
                {
                    errors.Add($"Item de cotação '{quotationItem.Description}' tem status '{quotationItem.ReconciliationStatus}' e não pode ser enviado como opção.");
                    continue;
                }

                if (quotationItem.MappedRequestLineItemId != item.RequestLineItemId)
                {
                    errors.Add($"Item de cotação '{quotationItem.Description}' não está mapeado para o item {item.RequestLineItemId}.");
                }
            }
        }

        if (errors.Any())
            return BadRequest(new ProblemDetails { Title = "Validação Falhou", Detail = string.Join(" | ", errors), Status = 400 });

        // Check for items in other active batches
        var currentBatchRliIds = batch.Items.Select(bi => bi.RequestLineItemId).ToHashSet();
        var newItemIds = requestLineItemIds.Except(currentBatchRliIds).ToHashSet();

        if (newItemIds.Any())
        {
            var otherActiveBatches = request.ApprovalBatches
                .Where(b => b.Id != batchId && b.Status != RequestConstants.ApprovalBatchStatuses.Rejected);

            foreach (var otherBatch in otherActiveBatches)
            {
                var conflicts = otherBatch.Items.Where(bi => newItemIds.Contains(bi.RequestLineItemId)).ToList();
                if (conflicts.Any())
                    return BadRequest(new ProblemDetails { Title = "Item em Outro Lote", Detail = $"Um ou mais itens já pertencem ao lote #{otherBatch.BatchNumber}.", Status = 400 });
            }

            // Validate new items are QUOTATION_PENDING or null
            foreach (var newId in newItemIds)
            {
                var li = request.LineItems.First(l => l.Id == newId);
                if (li.QuotationLifecycleStatus != null && li.QuotationLifecycleStatus != RequestConstants.QuotationLifecycleStatuses.QuotationPending)
                    return BadRequest(new ProblemDetails { Title = "Item Indisponível", Detail = $"O item #{li.LineNumber} não está disponível para inclusão no lote (status: {li.QuotationLifecycleStatus}).", Status = 400 });
            }
        }

        // ── 3.1. Cancelled-batch reuse rule (Option C): the edited winner set must not include
        // items from a cancelled batch without an active authorization. Items whose authorization
        // was already consumed by THIS batch stay legitimate (consumingBatchId).
        var (reuseBlocked, reuseAuthorized) = await _quotationEligibility.ValidateSelectionAsync(
            requestId, allQuotationItemIds, consumingBatchId: batchId);
        if (reuseBlocked.Count > 0)
        {
            return ReuseNotAuthorizedConflict(reuseBlocked);
        }

        // ── 4. Apply changes ──
        // Remove items no longer in the batch (their candidate snapshots cascade away with them)
        var removedIds = currentBatchRliIds.Except(requestLineItemIds).ToHashSet();
        foreach (var removedId in removedIds)
        {
            var batchItem = batch.Items.First(bi => bi.RequestLineItemId == removedId);
            _context.Set<ApprovalBatchItem>().Remove(batchItem);

            var lineItem = request.LineItems.First(li => li.Id == removedId);
            lineItem.QuotationLifecycleStatus = null; // Return to pending pool
        }

        // Reconcile candidate membership per item. Retained candidates keep their FROZEN snapshot
        // (only the BuyerNote may change in place); removed ones are deleted; newly added ones are
        // snapshotted NOW. Winner-decision fields are cleared on every edited item — a returned
        // batch re-enters area approval with no pre-decided winner (a legacy buyer-selected item
        // edited through this contract is thereby converted to the candidate model).
        foreach (var item in dto.Items)
        {
            var existingBatchItem = batch.Items.FirstOrDefault(bi => bi.RequestLineItemId == item.RequestLineItemId);
            if (existingBatchItem == null)
            {
                existingBatchItem = new ApprovalBatchItem
                {
                    Id = Guid.NewGuid(),
                    ApprovalBatchId = batchId,
                    RequestLineItemId = item.RequestLineItemId,
                    SelectedQuotationItemId = null,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.Set<ApprovalBatchItem>().Add(existingBatchItem);

                var lineItem = request.LineItems.First(li => li.Id == item.RequestLineItemId);
                lineItem.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.BatchAssigned;
            }

            // Clear any prior winner decision (returned-batch semantics; also strips the legacy
            // buyer-selected pointer when an old batch is edited through the candidate contract).
            existingBatchItem.SelectedQuotationItemId = null;
            existingBatchItem.SelectedCandidateId = null;
            existingBatchItem.WinnerSelectedByUserId = null;
            existingBatchItem.WinnerSelectedAtUtc = null;
            existingBatchItem.WinnerSelectionJustification = null;

            var wantedByQuotationItem = item.Candidates.ToDictionary(c => c.QuotationItemId);

            var toRemove = existingBatchItem.Candidates
                .Where(c => !wantedByQuotationItem.ContainsKey(c.QuotationItemId))
                .ToList();
            foreach (var stale in toRemove)
            {
                existingBatchItem.Candidates.Remove(stale);
                _context.ApprovalBatchItemCandidates.Remove(stale);
            }

            foreach (var candidate in item.Candidates)
            {
                var existingCandidate = existingBatchItem.Candidates
                    .FirstOrDefault(c => c.QuotationItemId == candidate.QuotationItemId);
                if (existingCandidate != null)
                {
                    // Snapshot stays frozen; only the informational note follows the edit.
                    var trimmedNote = string.IsNullOrWhiteSpace(candidate.BuyerNote) ? null : candidate.BuyerNote.Trim();
                    if (existingCandidate.BuyerNote != trimmedNote)
                        existingCandidate.BuyerNote = trimmedNote;
                }
                else
                {
                    _context.ApprovalBatchItemCandidates.Add(ApprovalBatchCandidateSnapshotFactory.Create(
                        quotationItemMap[candidate.QuotationItemId], existingBatchItem.Id, candidate.BuyerNote,
                        actorId, DateTime.UtcNow));
                }
            }
        }

        // ── 4.05. Buyer batch-composition decision for genuine EXTRA_ITEM lines belonging to the
        // quotation(s) contributing at least one CANDIDATE to this batch (shared service — identical
        // rule as CreateBatch, including safe INCLUDE→EXCLUDE reversal during this rework).
        var extraItemResult = await _extraItemDecisionService.ApplyAsync(
            request, batch, allQuotationItemIds, dto.ExtraItemDecisions, actorId);
        if (!extraItemResult.Success)
        {
            return ExtraItemDecisionErrorResult(extraItemResult);
        }

        // ── 4.1. Consume reuse authorizations for newly assigned reused items — same transaction
        // as the batch edit (rolls back together on failure).
        if (reuseAuthorized.Count > 0)
        {
            var authIds = reuseAuthorized.Select(a => a.ReuseAuthorizationId!.Value).ToList();
            var authEntities = await _context.QuotationReuseAuthorizations
                .Where(a => authIds.Contains(a.Id))
                .ToListAsync();
            var authorizerIds = authEntities.Select(a => a.AuthorizedByUserId).Distinct().ToList();
            var authorizerNames = await _context.Users.AsNoTracking()
                .Where(u => authorizerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName);

            foreach (var auth in authEntities)
            {
                auth.IsActive = false;
                auth.ConsumedByApprovalBatchId = batch.Id;
                auth.ConsumedAtUtc = DateTime.UtcNow;

                var elig = reuseAuthorized.First(a => a.ReuseAuthorizationId == auth.Id);
                quotationItemMap.TryGetValue(auth.QuotationItemId, out var qi);
                var authorizer = authorizerNames.TryGetValue(auth.AuthorizedByUserId, out var n) ? n : auth.AuthorizedByUserId.ToString();

                _context.RequestStatusHistories.Add(new RequestStatusHistory
                {
                    Id = Guid.NewGuid(),
                    RequestId = requestId,
                    ActorUserId = actorId,
                    ActionTaken = "QUOTATION_REUSED_IN_NEW_BATCH",
                    PreviousStatusId = request.StatusId,
                    NewStatusId = request.StatusId,
                    Comment = $"Item de cotação '{qi?.Description}' reutilizado do Lote #{elig.SourceCancelledBatchNumber} (cancelado) no Lote #{batch.BatchNumber} (edição em reajuste). Autorizado por {authorizer} em {auth.AuthorizedAtUtc:dd/MM/yyyy HH:mm}. Motivo da autorização: {auth.Reason}",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        if (dto.Comment != null)
            batch.Comment = dto.Comment;

        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedByUserId = actorId;

        // ── 5. History entry ──
        var changesDesc = new List<string>();
        if (removedIds.Any()) changesDesc.Add($"removidos: {removedIds.Count}");
        if (newItemIds.Any()) changesDesc.Add($"adicionados: {newItemIds.Count}");
        changesDesc.Add($"total: {dto.Items.Count}");

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "BATCH_EDITED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Lote #{batch.BatchNumber} editado durante reajuste ({string.Join(", ", changesDesc)}).",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        _logger.LogInformation("Batch {BatchId} of Request {RequestId} edited by {ActorId}", batchId, requestId, actorId);

        var batchDto = await BuildBatchDto(batchId, requestId);
        return Ok(batchDto);
    }

    /// <summary>
    /// Resubmits a batch after rework: sets batch back to WAITING_AREA_APPROVAL.
    /// </summary>
    [HttpPost("{batchId:guid}/resubmit")]
    public async Task<IActionResult> ResubmitBatch(Guid requestId, Guid batchId, [FromBody] BatchApprovalActionDto? dto)
    {
        var actorId = CurrentUserId;

        var (request, batch, error) = await LoadAndValidateBatchForRework(requestId, batchId);
        if (error != null) return error;

        // Defensive Option C check: everything the batch puts forward must still be legitimate at
        // resubmission — every candidate option plus any legacy buyer-selected winner (its own
        // consumed authorizations pass via consumingBatchId; anything else blocked → 409).
        var resubmitQuotationItemIds = batch!.Items
            .SelectMany(bi => bi.Candidates.Select(c => c.QuotationItemId))
            .Concat(batch.Items.Where(bi => bi.SelectedQuotationItemId.HasValue)
                .Select(bi => bi.SelectedQuotationItemId!.Value))
            .Distinct()
            .ToList();
        var (resubmitBlocked, _) = await _quotationEligibility.ValidateSelectionAsync(
            requestId, resubmitQuotationItemIds, consumingBatchId: batchId);
        if (resubmitBlocked.Count > 0)
        {
            return ReuseNotAuthorizedConflict(resubmitBlocked);
        }

        // ── Update batch state ──
        batch.Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval;
        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedByUserId = actorId;

        // ── History entry ──
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "BATCH_RESUBMITTED",
            PreviousStatusId = request!.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Lote #{batch.BatchNumber} reenviado para aprovação da área. {dto?.Comment}".Trim(),
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        await _statusSyncService.SyncStatusAsync(requestId, actorId);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Batch {BatchId} of Request {RequestId} resubmitted by {ActorId}", batchId, requestId, actorId);

        return Ok(new { Message = $"Lote #{batch.BatchNumber} reenviado para aprovação com sucesso.", BatchId = batchId, Status = batch.Status });
    }

    // ══════════════════════════════════════════════════════════════════════
    // Phase 5: Batch-Aware Final Approval Endpoints
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Final-approves a batch: activates PO groups (PENDING → WAITING_PO),
    /// snapshots batch and request amounts, sets items to QUOTATION_APPROVED.
    /// </summary>
    [HttpPost("{batchId:guid}/final-approval/approve")]
    public async Task<IActionResult> BatchFinalApprove(Guid requestId, Guid batchId, [FromBody] BatchApprovalActionDto dto)
    {
        var actorId = CurrentUserId;

        if (!CurrentUserRoles.Contains(RoleConstants.FinalApprover))
            return StatusCode(403, "Apenas o papel de Aprovador Final pode realizar aprovações nesta etapa.");

        var (request, batch, error) = await LoadAndValidateBatch(requestId, batchId, RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval);
        if (error != null) return error;

        // ── Validate batch has items ──
        if (!batch!.Items.Any())
            return BadRequest(new ProblemDetails { Title = "Lote Vazio", Detail = "O lote não possui itens para aprovação.", Status = 400 });

        // ── Validate batch has PENDING PO groups ──
        var batchPoGroups = await _context.RequestPoGroups
            .Where(g => g.ApprovalBatchId == batchId)
            .ToListAsync();

        var pendingGroups = batchPoGroups.Where(g => g.Status == RequestConstants.PoGroupStatuses.Pending).ToList();
        if (!pendingGroups.Any())
            return BadRequest(new ProblemDetails { Title = "Sem Grupos de PO", Detail = "O lote não possui grupos de PO pendentes para ativação. Verifique se a Aprovação da Área foi concluída corretamente.", Status = 400 });

        // ── Step 1: Compute batch approved amount ──
        // Primary: sum of PO group TotalAmount
        var batchApprovedAmount = pendingGroups.Sum(g => g.TotalAmount);

        // Defensive fallback: if PO group totals are zero/negative, recalculate from the winners —
        // frozen candidate snapshots first (candidate model), live quotation items only for legacy
        // buyer-selected items that have no snapshot.
        if (batchApprovedAmount <= 0)
        {
            var candidateSum = batch.Items
                .Where(bi => bi.SelectedCandidateId.HasValue)
                .Select(bi => bi.Candidates.FirstOrDefault(c => c.Id == bi.SelectedCandidateId!.Value))
                .Where(c => c != null)
                .Sum(c => c!.LineTotal);

            var legacyQiIds = batch.Items
                .Where(bi => !bi.SelectedCandidateId.HasValue && bi.SelectedQuotationItemId.HasValue)
                .Select(bi => bi.SelectedQuotationItemId!.Value)
                .Distinct()
                .ToList();
            var legacySum = legacyQiIds.Count == 0
                ? 0m
                : await _context.QuotationItems
                    .Where(qi => legacyQiIds.Contains(qi.Id))
                    .SumAsync(qi => qi.LineTotal);

            batchApprovedAmount = candidateSum + legacySum;

            _logger.LogWarning(
                "BatchFinalApprove: PO group total was {GroupTotal}, recalculated from batch items: {AwardedTotal} for batch {BatchId}",
                pendingGroups.Sum(g => g.TotalAmount), batchApprovedAmount, batchId);
        }

        // ── Step 2: Snapshot batch amount ──
        batch.ApprovedTotalAmount = batchApprovedAmount;

        // ── Step 3: Set batch APPROVED (BEFORE cumulative query) ──
        batch.Status = RequestConstants.ApprovalBatchStatuses.Approved;
        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedByUserId = actorId;

        // ── Step 4: Compute cumulative Request.ApprovedTotalAmount ──
        // Query all APPROVED batches (including this one, which is now APPROVED)
        // We need to flush the status change first via the tracked entity
        var allApprovedBatches = await _context.ApprovalBatches
            .Where(b => b.RequestId == requestId && b.Status == RequestConstants.ApprovalBatchStatuses.Approved && b.Id != batchId)
            .ToListAsync();

        var cumulativeAmount = batchApprovedAmount + allApprovedBatches.Sum(b => b.ApprovedTotalAmount ?? 0);
        request!.ApprovedTotalAmount = cumulativeAmount;

        // ── Activate this batch's PO groups: PENDING → WAITING_PO ──
        foreach (var group in pendingGroups)
        {
            group.Status = RequestConstants.PoGroupStatuses.WaitingPo;
        }

        // ── Set batch items: BATCH_ASSIGNED → QUOTATION_APPROVED ──
        var batchItemRliIds = batch.Items.Select(bi => bi.RequestLineItemId).ToHashSet();
        var batchLineItems = request.LineItems.Where(li => batchItemRliIds.Contains(li.Id)).ToList();

        foreach (var item in batchLineItems)
        {
            item.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved;
            // SelectedQuotationItemId stays set (was set at Area Approval)
        }

        // ── History entries ──
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "BATCH_FINAL_APPROVED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Aprovação Final do Lote #{batch.BatchNumber} realizada. Montante: {batchApprovedAmount:N2}. {dto.Comment}".Trim(),
            CreatedAtUtc = DateTime.UtcNow
        });

        // Per-group activation history
        foreach (var group in pendingGroups)
        {
            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = requestId,
                ActorUserId = actorId,
                ActionTaken = "PO_GROUP_ACTIVATED",
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = $"[Lote #{batch.BatchNumber}] Grupo de PO ativado: {group.SupplierNameSnapshot} ({group.CurrencyCode}). Total: {group.TotalAmount:N2}.",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        // ── Sync request status ──
        await _statusSyncService.SyncStatusAsync(requestId, actorId);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Batch {BatchId} of Request {RequestId} final-approved by {ActorId}. Amount: {Amount}", batchId, requestId, actorId, batchApprovedAmount);

        return Ok(new { Message = $"Lote #{batch.BatchNumber} aprovado na Aprovação Final com sucesso.", BatchId = batchId, Status = batch.Status, ApprovedTotalAmount = batchApprovedAmount });
    }

    /// <summary>
    /// Final-rejects a batch: returns items to QUOTATION_PENDING, clears SelectedQuotationItemId
    /// on RequestLineItem (preserved on ApprovalBatchItem for audit), clears allocations,
    /// removes PENDING PO groups for this batch.
    /// </summary>
    [HttpPost("{batchId:guid}/final-approval/reject")]
    public async Task<IActionResult> BatchFinalReject(Guid requestId, Guid batchId, [FromBody] BatchApprovalActionDto dto)
    {
        var actorId = CurrentUserId;

        if (!CurrentUserRoles.Contains(RoleConstants.FinalApprover))
            return StatusCode(403, "Apenas o papel de Aprovador Final pode realizar rejeições nesta etapa.");

        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "É necessário informar o motivo da rejeição.", Status = 400 });

        var (request, batch, error) = await LoadAndValidateBatch(requestId, batchId, RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval);
        if (error != null) return error;

        // ── Validate PO groups are all PENDING (safety check) ──
        var batchPoGroups = await _context.RequestPoGroups
            .Where(g => g.ApprovalBatchId == batchId)
            .ToListAsync();

        var nonPendingGroups = batchPoGroups.Where(g => g.Status != RequestConstants.PoGroupStatuses.Pending).ToList();
        if (nonPendingGroups.Any())
        {
            var statuses = string.Join(", ", nonPendingGroups.Select(g => $"{g.SupplierNameSnapshot}: {g.Status}"));
            return BadRequest(new ProblemDetails
            {
                Title = "Grupos de PO Não Pendentes",
                Detail = $"O lote possui grupos de PO que não estão em PENDING e não podem ser removidos: {statuses}. Contacte o administrador.",
                Status = 400
            });
        }

        // ── Return batch items to QUOTATION_PENDING ──
        var batchItemRliIds = batch!.Items.Select(bi => bi.RequestLineItemId).ToHashSet();
        var batchLineItems = request!.LineItems.Where(li => batchItemRliIds.Contains(li.Id)).ToList();

        foreach (var item in batchLineItems)
        {
            item.QuotationLifecycleStatus = null;
            item.SelectedQuotationItemId = null; // Clear legacy field (audit preserved in ApprovalBatchItem)

            // Clear allocations
            if (item.Allocations != null && item.Allocations.Any())
            {
                _context.RequestLineItemAllocations.RemoveRange(item.Allocations);
                item.Allocations.Clear();
            }

            // Clear PO group assignment
            item.RequestPoGroupId = null;
        }

        // ── Remove PENDING PO groups for this batch ──
        if (batchPoGroups.Any())
        {
            _context.RequestPoGroups.RemoveRange(batchPoGroups);
            _logger.LogInformation("BatchFinalReject: Removed {Count} PENDING PO groups for batch {BatchId}", batchPoGroups.Count, batchId);
        }

        // ── Update batch state ──
        batch.Status = RequestConstants.ApprovalBatchStatuses.Rejected;
        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedByUserId = actorId;

        // ── History entry ──
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "BATCH_FINAL_REJECTED",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Lote #{batch.BatchNumber} rejeitado na Aprovação Final. Motivo: {dto.Comment}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        await _statusSyncService.SyncStatusAsync(requestId, actorId);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Batch {BatchId} of Request {RequestId} final-rejected by {ActorId}", batchId, requestId, actorId);

        return Ok(new { Message = $"Lote #{batch.BatchNumber} rejeitado na Aprovação Final.", BatchId = batchId, Status = batch.Status });
    }

    /// <summary>
    /// Requests final adjustment (rework) for a batch: sets status to FINAL_ADJUSTMENT,
    /// clears SelectedQuotationItemId on RequestLineItem (preserved on ApprovalBatchItem),
    /// keeps items as BATCH_ASSIGNED, clears allocations, removes PENDING PO groups.
    /// Rework path: FINAL_ADJUSTMENT → PUT edit → POST resubmit → WAITING_AREA_APPROVAL.
    /// </summary>
    [HttpPost("{batchId:guid}/final-approval/request-adjustment")]
    public async Task<IActionResult> BatchFinalRequestAdjustment(Guid requestId, Guid batchId, [FromBody] BatchApprovalActionDto dto)
    {
        var actorId = CurrentUserId;

        if (!CurrentUserRoles.Contains(RoleConstants.FinalApprover))
            return StatusCode(403, "Apenas o papel de Aprovador Final pode solicitar reajuste nesta etapa.");

        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "É necessário informar o motivo do reajuste.", Status = 400 });

        var (request, batch, error) = await LoadAndValidateBatch(requestId, batchId, RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval);
        if (error != null) return error;

        // ── Validate PO groups are all PENDING (safety check) ──
        var batchPoGroups = await _context.RequestPoGroups
            .Where(g => g.ApprovalBatchId == batchId)
            .ToListAsync();

        var nonPendingGroups = batchPoGroups.Where(g => g.Status != RequestConstants.PoGroupStatuses.Pending).ToList();
        if (nonPendingGroups.Any())
        {
            var statuses = string.Join(", ", nonPendingGroups.Select(g => $"{g.SupplierNameSnapshot}: {g.Status}"));
            return BadRequest(new ProblemDetails
            {
                Title = "Grupos de PO Não Pendentes",
                Detail = $"O lote possui grupos de PO que não estão em PENDING e não podem ser removidos: {statuses}. Contacte o administrador.",
                Status = 400
            });
        }

        // ── Clear allocations and legacy fields for batch items ──
        var batchItemRliIds = batch!.Items.Select(bi => bi.RequestLineItemId).ToHashSet();
        var batchLineItems = request!.LineItems.Where(li => batchItemRliIds.Contains(li.Id)).ToList();

        foreach (var item in batchLineItems)
        {
            // Items stay BATCH_ASSIGNED
            item.SelectedQuotationItemId = null; // Clear legacy field (audit preserved in history)

            if (item.Allocations != null && item.Allocations.Any())
            {
                _context.RequestLineItemAllocations.RemoveRange(item.Allocations);
                item.Allocations.Clear();
            }

            item.RequestPoGroupId = null;
        }

        // ── Revoke the Area winner decision on candidate-based items (returned-batch semantics:
        // candidates persist; the decision itself survives only in history). Legacy items keep
        // their buyer-selected pointer — it is their only winner information. ──
        foreach (var bi in batch.Items.Where(bi => bi.Candidates.Count > 0))
        {
            bi.SelectedCandidateId = null;
            bi.SelectedQuotationItemId = null;
            bi.WinnerSelectedByUserId = null;
            bi.WinnerSelectedAtUtc = null;
            bi.WinnerSelectionJustification = null;
        }

        // ── Remove PENDING PO groups for this batch ──
        if (batchPoGroups.Any())
        {
            _context.RequestPoGroups.RemoveRange(batchPoGroups);
            _logger.LogInformation("BatchFinalRequestAdjustment: Removed {Count} PENDING PO groups for batch {BatchId}", batchPoGroups.Count, batchId);
        }

        // ── Update batch state ──
        batch.Status = RequestConstants.ApprovalBatchStatuses.FinalAdjustment;
        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedByUserId = actorId;

        // ── History entry ──
        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "BATCH_FINAL_ADJUSTMENT",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Solicitado reajuste final no Lote #{batch.BatchNumber}. Motivo: {dto.Comment}",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        await _statusSyncService.SyncStatusAsync(requestId, actorId);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Batch {BatchId} of Request {RequestId} final-adjustment requested by {ActorId}", batchId, requestId, actorId);

        return Ok(new { Message = $"Lote #{batch.BatchNumber} devolvido para reajuste final.", BatchId = batchId, Status = batch.Status });
    }

    // ── Private helpers ──


    private async Task<ApprovalBatchDto?> BuildBatchDto(Guid batchId, Guid? expectedRequestId = null)
    {
        var batch = await _context.ApprovalBatches
            .AsNoTracking()
            .Include(b => b.Items)
                .ThenInclude(bi => bi.RequestLineItem)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.SelectedQuotationItem)
                    .ThenInclude(qi => qi!.Quotation)
                        .ThenInclude(q => q!.Supplier)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.Candidates)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == batchId);

        if (batch == null) return null;
        if (expectedRequestId.HasValue && batch.RequestId != expectedRequestId.Value) return null;

        var creatorName = await _context.Users
            .Where(u => u.Id == batch.CreatedByUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync();

        var itemDtos = batch.Items.Select(bi =>
        {
            var winnerCandidate = bi.SelectedCandidateId.HasValue
                ? bi.Candidates.FirstOrDefault(c => c.Id == bi.SelectedCandidateId.Value)
                : null;

            // "MENOR VALOR" badge per currency within this item (informational only).
            var lowestByCurrency = bi.Candidates
                .GroupBy(c => c.Currency)
                .ToDictionary(g => g.Key, g => g.Min(c => c.LineTotal));

            return new ApprovalBatchItemDto
            {
                Id = bi.Id,
                RequestLineItemId = bi.RequestLineItemId,
                RequestLineItemDescription = bi.RequestLineItem?.Description,
                RequestLineItemLineNumber = bi.RequestLineItem?.LineNumber,
                RequestLineItemQuantity = bi.RequestLineItem?.Quantity,

                SelectedQuotationItemId = bi.SelectedQuotationItemId,
                // Winner display: frozen snapshot when Area-decided; live lookup only for legacy items.
                SelectedQuotationItemDescription = winnerCandidate?.QuotedDescription ?? bi.SelectedQuotationItem?.Description,
                SelectedQuotationItemUnitPrice = winnerCandidate?.UnitPrice ?? bi.SelectedQuotationItem?.UnitPrice,
                SelectedQuotationItemLineTotal = winnerCandidate?.LineTotal ?? bi.SelectedQuotationItem?.LineTotal,
                SupplierName = winnerCandidate?.SupplierNameSnapshot
                    ?? bi.SelectedQuotationItem?.Quotation?.Supplier?.Name
                    ?? bi.SelectedQuotationItem?.Quotation?.SupplierNameSnapshot,

                SelectedCandidateId = bi.SelectedCandidateId,
                WinnerSelectedByUserId = bi.WinnerSelectedByUserId,
                WinnerSelectedAtUtc = bi.WinnerSelectedAtUtc,
                WinnerSelectionJustification = bi.WinnerSelectionJustification,
                IsLegacyBuyerSelectedWinner = bi.Candidates.Count == 0 && bi.SelectedQuotationItemId.HasValue,

                Candidates = bi.Candidates
                    .OrderBy(c => c.LineTotal)
                    .Select(c => new ApprovalBatchItemCandidateDto
                    {
                        Id = c.Id,
                        QuotationItemId = c.QuotationItemId,
                        QuotationId = c.QuotationId,
                        SupplierId = c.SupplierId,
                        SupplierName = c.SupplierNameSnapshot,
                        SupplierNif = c.SupplierNifSnapshot,
                        Description = c.QuotedDescription,
                        Quantity = c.QuotedQuantity,
                        UnitText = c.UnitTextSnapshot,
                        UnitPrice = c.UnitPrice,
                        DiscountAmount = c.DiscountAmount,
                        IvaRatePercent = c.IvaRatePercent,
                        IvaAmount = c.IvaAmount,
                        GrossSubtotal = c.GrossSubtotal,
                        LineTotal = c.LineTotal,
                        Currency = c.Currency,
                        QuotationDocumentNumber = c.QuotationDocumentNumber,
                        QuotationDocumentDate = c.QuotationDocumentDate,
                        HasReconciliationWarnings = c.HasReconciliationWarnings,
                        ReconciliationStatus = c.ReconciliationStatusSnapshot,
                        ReconciliationJustification = c.ReconciliationJustificationSnapshot,
                        LineAdjustmentJustification = c.LineAdjustmentJustificationSnapshot,
                        BuyerNote = c.BuyerNote,
                        IsWinner = bi.SelectedCandidateId.HasValue && c.Id == bi.SelectedCandidateId.Value,
                        IsLowestTotal = lowestByCurrency.TryGetValue(c.Currency, out var lowest) && c.LineTotal == lowest
                    }).ToList()
            };
        }).ToList();

        // Pre-decision candidate summary (decision 5): combination bounds only when every item
        // carries candidates in one shared currency — otherwise null, never a guess.
        var allCandidates = batch.Items.SelectMany(bi => bi.Candidates).ToList();
        decimal? minCombination = null;
        decimal? maxCombination = null;
        var currencies = allCandidates.Select(c => c.Currency).Distinct().ToList();
        if (batch.Items.Any() && batch.Items.All(bi => bi.Candidates.Count > 0) && currencies.Count == 1)
        {
            minCombination = batch.Items.Sum(bi => bi.Candidates.Min(c => c.LineTotal));
            maxCombination = batch.Items.Sum(bi => bi.Candidates.Max(c => c.LineTotal));
        }

        return new ApprovalBatchDto
        {
            Id = batch.Id,
            RequestId = batch.RequestId,
            BatchNumber = batch.BatchNumber,
            Status = batch.Status,
            Comment = batch.Comment,
            ApprovedTotalAmount = batch.ApprovedTotalAmount,
            BudgetJustification = batch.BudgetJustification,
            CreatedAtUtc = batch.CreatedAtUtc,
            CreatedByUserId = batch.CreatedByUserId,
            CreatedByUserName = creatorName,
            UpdatedAtUtc = batch.UpdatedAtUtc,
            UpdatedByUserId = batch.UpdatedByUserId,
            Items = itemDtos,
            CandidateOptionCount = allCandidates.Count,
            CandidateSupplierCount = allCandidates
                .Select(c => c.SupplierId?.ToString() ?? c.SupplierNameSnapshot)
                .Distinct()
                .Count(),
            MinCandidateCombinationTotal = minCombination,
            MaxCandidateCombinationTotal = maxCombination
        };
    }

    /// <summary>
    /// Cancels or voids an approval batch, preserving the quotation items but resetting the request line items for a new submission.
    /// Available to Buyers and System Administrators.
    /// </summary>
    [HttpPost("{batchId}/cancel")]
    public async Task<ActionResult> CancelBatch([FromRoute] Guid requestId, [FromRoute] Guid batchId, [FromBody] CancelApprovalBatchRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Justification))
            return BadRequest(new ProblemDetails { Title = "Validação", Detail = "A justificativa é obrigatória.", Status = 400 });

        var actorId = CurrentUserId;
        var userRoles = CurrentUserRoles;

        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 });

        // Authorization: Only Buyer (owner) or SysAdmin
        if (request.BuyerId != actorId && !userRoles.Contains(RoleConstants.SystemAdministrator))
        {
            return Forbid();
        }

        var batch = await _context.ApprovalBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.RequestId == requestId);

        if (batch == null)
            return NotFound(new ProblemDetails { Title = "Lote não encontrado.", Status = 404 });

        var reversibleStatuses = new[] 
        { 
            RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, 
            RequestConstants.ApprovalBatchStatuses.AreaAdjustment,
            RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval,
            RequestConstants.ApprovalBatchStatuses.FinalAdjustment
        };

        if (!reversibleStatuses.Contains(batch.Status))
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Ação Inválida", 
                Detail = $"Não é possível cancelar um lote no status {batch.Status}. Apenas lotes pendentes ou em ajuste podem ser cancelados.", 
                Status = 409 
            });
        }

        // Get all items in this batch
        var batchRliIds = batch.Items.Select(bi => bi.RequestLineItemId).ToHashSet();

        // Check if any item is already in another active batch (should not happen normally, but a safeguard)
        var otherActiveBatchItems = await _context.ApprovalBatchItems
            .Include(abi => abi.ApprovalBatch)
            .Where(abi => abi.ApprovalBatchId != batchId 
                       && abi.ApprovalBatch.RequestId == requestId 
                       && reversibleStatuses.Contains(abi.ApprovalBatch.Status)
                       && batchRliIds.Contains(abi.RequestLineItemId))
            .Select(abi => abi.RequestLineItemId)
            .ToListAsync();

        var safeToResetRliIds = batchRliIds.Except(otherActiveBatchItems).ToHashSet();

        var lineItemsToReset = request.LineItems.Where(li => safeToResetRliIds.Contains(li.Id)).ToList();
        
        foreach (var li in lineItemsToReset)
        {
            li.QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationPending;
            // Also clear legacy field just in case
            li.SelectedQuotationItemId = null;
        }

        string previousStatus = batch.Status;
        batch.Status = RequestConstants.ApprovalBatchStatuses.Cancelled;
        batch.UpdatedAtUtc = DateTime.UtcNow;
        batch.UpdatedByUserId = actorId;

        // Audit History
        string affectedLines = string.Join(", ", lineItemsToReset.Select(li => li.LineNumber).OrderBy(n => n));
        
        var history = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = actorId,
            ActionTaken = "Lote de aprovação cancelado",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId, // Status doesn't change directly, sync service handles it
            Comment = $"Motivo: {dto.Justification} (Lote {batch.BatchNumber}, Status Anterior: {previousStatus}, Itens Afetados: {affectedLines})",
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.RequestStatusHistories.Add(history);

        await _context.SaveChangesAsync();

        // 7. Sync parent request status
        await _statusSyncService.SyncStatusAsync(requestId, CurrentUserId);

        return Ok();
    }

    /// <summary>
    /// Shared validation for reject and request-adjustment: loads request + batch with required navigations.
    /// </summary>
    private async Task<(Request? request, ApprovalBatch? batch, IActionResult? error)> LoadAndValidateBatch(Guid requestId, Guid batchId, string requiredBatchStatus)
    {
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .Include(r => r.LineItems)
                .ThenInclude(li => li.Allocations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return (null, null, NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 }));

        var scopedQuery = await GetScopedRequestsQuery();
        if (!await scopedQuery.AnyAsync(r => r.Id == requestId))
            return (null, null, NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 }));

        if (request.RequestType?.Code != RequestConstants.Types.Quotation)
            return (null, null, BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Operação permitida apenas para pedidos de Cotação.", Status = 400 }));

        var batch = await _context.ApprovalBatches
            .Include(b => b.Items)
                .ThenInclude(bi => bi.RequestLineItem)
            .Include(b => b.Items)
                .ThenInclude(bi => bi.Candidates)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == batchId && b.RequestId == requestId);

        if (batch == null)
            return (null, null, NotFound(new ProblemDetails { Title = "Lote não encontrado.", Status = 404 }));

        if (batch.Status != requiredBatchStatus)
            return (null, null, BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = $"O lote não está no status esperado ({requiredBatchStatus}). Status atual: {batch.Status}.", Status = 400 }));

        return (request, batch, null);
    }

    /// <summary>
    /// Shared validation for resubmit: batch must be in AREA_ADJUSTMENT or FINAL_ADJUSTMENT.
    /// </summary>
    private async Task<(Request? request, ApprovalBatch? batch, IActionResult? error)> LoadAndValidateBatchForRework(Guid requestId, Guid batchId)
    {
        var request = await _context.Requests
            .Include(r => r.RequestType)
            .Include(r => r.Status)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return (null, null, NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 }));

        var scopedQuery = await GetScopedRequestsQuery();
        if (!await scopedQuery.AnyAsync(r => r.Id == requestId))
            return (null, null, NotFound(new ProblemDetails { Title = "Pedido não encontrado.", Status = 404 }));

        if (request.RequestType?.Code != RequestConstants.Types.Quotation)
            return (null, null, BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = "Operação permitida apenas para pedidos de Cotação.", Status = 400 }));

        var batch = await _context.ApprovalBatches
            .Include(b => b.Items)
                .ThenInclude(bi => bi.Candidates)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.RequestId == requestId);

        if (batch == null)
            return (null, null, NotFound(new ProblemDetails { Title = "Lote não encontrado.", Status = 404 }));

        var allowedStatuses = new[] { RequestConstants.ApprovalBatchStatuses.AreaAdjustment, RequestConstants.ApprovalBatchStatuses.FinalAdjustment };
        if (!allowedStatuses.Contains(batch.Status))
            return (null, null, BadRequest(new ProblemDetails { Title = "Ação Inválida", Detail = $"O lote deve estar em AREA_ADJUSTMENT ou FINAL_ADJUSTMENT para reenvio (status atual: {batch.Status}).", Status = 400 }));

        return (request, batch, null);
    }
}
