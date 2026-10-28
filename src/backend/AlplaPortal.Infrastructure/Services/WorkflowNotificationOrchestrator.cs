using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Common;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// Central notification orchestrator for workflow events.
/// Resolves recipients per event type, builds contextual messages, and dispatches
/// through both in-app notification persistence and transactional email channels.
///
/// <para><b>Architecture Note:</b> This class is the single authority for "who gets notified, 
/// what they see, and through which channel" for all workflow transitions. Controllers should 
/// never build notification logic themselves — they emit a <see cref="WorkflowEvent"/> and 
/// this orchestrator handles the rest.</para>
/// </summary>
public class WorkflowNotificationOrchestrator : IWorkflowNotificationOrchestrator
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<WorkflowNotificationOrchestrator> _logger;
    private readonly AdminLogWriter _adminLog;

    private readonly IApprovalRoutingService _approvalRouting;

    public WorkflowNotificationOrchestrator(
        ApplicationDbContext context,
        INotificationService notificationService,
        IEmailService emailService,
        IConfiguration config,
        ILogger<WorkflowNotificationOrchestrator> logger,
        AdminLogWriter adminLog,
        IApprovalRoutingService approvalRouting)
    {
        _context = context;
        _notificationService = notificationService;
        _emailService = emailService;
        _config = config;
        _logger = logger;
        _adminLog = adminLog;
        _approvalRouting = approvalRouting;
    }

    public async Task EmitAsync(WorkflowEvent evt)
    {
        try
        {
            _logger.LogInformation(
                "Workflow notification event: {EventCode} for Request {RequestId} (Action: {ActionTaken}, CorrelationId: {CorrelationId})",
                evt.EventCode, evt.RequestId, evt.ActionTaken, evt.CorrelationId);

            var eventConfig = await ResolveEventConfigAsync(evt);
            if (eventConfig == null)
            {
                _logger.LogDebug("No notification config for event {EventCode} — skipping", evt.EventCode);
                return;
            }

            var recipients = await ResolveRecipientsAsync(evt);
            if (!recipients.Any())
            {
                // No per-user recipient: skip the in-app/outbox dispatch only. The Accounts Payable group notification
                // below is independent of per-user routing (e.g. a company whose plant has no Finance user must still
                // reach its configured AP address when NotifyOnPoRegistered is on), so it is NOT short-circuited here.
                _logger.LogWarning("No recipients resolved for event {EventCode} on Request {RequestId}", evt.EventCode, evt.RequestId);
            }

            foreach (var recipient in recipients)
            {
                // Self-notify restriction has been lifted based on business requirements.
                // Every actor gets notified of their own actions to preserve email history.

                await DispatchToRecipientAsync(evt, eventConfig, recipient);
            }

            // ── Accounts Payable external notification (independent from in-app dispatch) ──
            // Payment scheduled/completed (unchanged) plus P.O. registered, the latter only when the company's
            // configuration opts in (NotifyOnPoRegistered) — see SendAccountsPayableNotificationAsync.
            if (evt.EventCode == WorkflowEventCodes.PoGroupCancelled)
            {
                // Phase 1 P.O. cancellation: the Accounts Payable notice is an OUTBOX row written on the caller's context (inside
                // its transaction) — never a direct SMTP send. Durable: the intent survives a crash after commit; the outbox
                // worker delivers with its retry budget; dedup per (correlation, AP address).
                try { await QueueAccountsPayableOutboxAsync(evt); }
                catch (Exception apEx) { _logger.LogWarning(apEx, "Non-critical: Accounts Payable outbox queueing failed for {EventCode} on Request {RequestId}", evt.EventCode, evt.RequestId); }
            }
            else if (evt.EventCode == WorkflowEventCodes.PaymentScheduled || evt.EventCode == WorkflowEventCodes.PaymentCompleted
                || evt.EventCode == WorkflowEventCodes.PoRegistered)
            {
                try
                {
                    await SendAccountsPayableNotificationAsync(evt);
                }
                catch (Exception apEx)
                {
                    // AP notification failure must NEVER propagate — payment workflow is unaffected
                    _logger.LogWarning(apEx, "Non-critical: Accounts Payable notification failed for {EventCode} on Request {RequestId}", evt.EventCode, evt.RequestId);
                }
            }

            // ── Buyer P.O. Creation Instruction (independent from generic approval dispatch) ──
            if (evt.EventCode == WorkflowEventCodes.FinalApproved)
            {
                try
                {
                    await SendBuyerPoCreationInstructionAsync(evt);
                }
                catch (Exception buyerEx)
                {
                    // Buyer PO instruction failure must NEVER block the approval workflow
                    _logger.LogWarning(buyerEx, "Non-critical: Buyer P.O. creation instruction email failed for Request {RequestId}", evt.RequestId);
                }
            }
        }
        catch (Exception ex)
        {
            // Bubble up the exception so the Controller knows that an orchestration path failed.
            _logger.LogError(ex, "Workflow notification orchestration failed for event {EventCode} on Request {RequestId}. Bubbling up.",
                evt.EventCode, evt.RequestId);
            throw;
        }
    }

    // =====================================================================
    // RECIPIENT RESOLUTION
    // =====================================================================

    private async Task<List<NotificationRecipient>> ResolveRecipientsAsync(WorkflowEvent evt)
    {
        var recipients = new List<NotificationRecipient>();
        
        // Context variables for specialized override logic
        var reqRef = FormatRequestRef(evt);
        var commentHtml = !string.IsNullOrWhiteSpace(evt.Comment)
            ? $"<br/><br/><b>Justificativa:</b><blockquote style='border-left: 4px solid #dc3545; margin: 10px 0; padding-left: 10px; color: #555;'>{System.Net.WebUtility.HtmlEncode(evt.Comment)}</blockquote>"
            : "";

        switch (evt.EventCode)
        {
            // --- Approval Flow: notify the next decision-maker ---
            case WorkflowEventCodes.RequestSubmitted:
                await HandlePendingAreaApprovalFanningAsync(recipients, evt, reqRef, isQuotation: false);
                break;

            case WorkflowEventCodes.SubmissionConfirmed:
                await AddUserRecipientAsync(recipients, evt.RequesterId);
                break;

            case WorkflowEventCodes.AreaApproved:
                await AddUserRecipientAsync(recipients, evt.FinalApproverId); // Send default to Final Approver
                await HandleAreaFanningOverridesAsync(recipients, evt, reqRef, commentHtml, isApproval: true);
                break;

            case WorkflowEventCodes.FinalApproved:
                await AddUserRecipientAsync(recipients, evt.FinalApproverId);
                await AddUserRecipientAsync(recipients, evt.RequesterId);
                if (evt.BuyerId.HasValue)
                    await AddUserRecipientAsync(recipients, evt.BuyerId);
                break;

            case WorkflowEventCodes.QuotationCompleted:
                await HandlePendingAreaApprovalFanningAsync(recipients, evt, reqRef, isQuotation: true);
                break;

            // --- Quotation Awaiting Buyer: notify plant-scoped Buyers ---
            case WorkflowEventCodes.QuotationAwaitingBuyer:
                await AddPlantScopedBuyerRecipientsAsync(recipients, evt);
                break;

            // --- Buyer Assigned: notify Buyer + Requester ---
            case WorkflowEventCodes.BuyerTransferred:
                await AddBuyerTransferRecipientsAsync(recipients, evt, reqRef);
                break;
            case WorkflowEventCodes.BuyerAssigned:
            {
                var buyerUser = evt.BuyerId.HasValue
                    ? await _context.Users.AsNoTracking()
                        .Where(u => u.Id == evt.BuyerId.Value)
                        .Select(u => new { u.FullName }).FirstOrDefaultAsync()
                    : null;
                var buyerName = buyerUser?.FullName ?? evt.ActorName;
                var assignedByCoordinator = evt.BuyerId.HasValue && evt.BuyerId.Value != evt.ActorUserId;
                var assignedScope = evt.PoGroupId.HasValue ? "a correção da P.O do seu pedido" : "o seu pedido de cotação";

                // Buyer: confirmation that the request was assigned to them
                await AddUserRecipientAsync(recipients, evt.BuyerId,
                    emailSubjectOverride: $"Pedido de Cotação atribuído a você — {reqRef}",
                    emailBodyOverride: assignedByCoordinator
                        ? $"{(evt.PoGroupId.HasValue ? "A correção da P.O do pedido" : "O pedido de cotação")} <b>{reqRef}</b> foi-lhe atribuído(a) por <b>{System.Net.WebUtility.HtmlEncode(evt.ActorName)}</b>. Por favor, prossiga."
                        : $"{(evt.PoGroupId.HasValue ? "A correção da P.O do pedido" : "O pedido de cotação")} <b>{reqRef}</b> foi-lhe atribuído(a). Por favor, prossiga com o processo.",
                    bypassSelfNotifyRule: true);

                // Requester: informing who the buyer is
                await AddUserRecipientAsync(recipients, evt.RequesterId,
                    emailSubjectOverride: $"Comprador atribuído ao seu pedido — {reqRef}",
                    emailBodyOverride: assignedByCoordinator
                        ? $"{(evt.PoGroupId.HasValue ? "A correção da P.O do seu pedido" : "O seu pedido de cotação")} <b>{reqRef}</b> foi atribuído(a) ao comprador <b>{System.Net.WebUtility.HtmlEncode(buyerName)}</b> por <b>{System.Net.WebUtility.HtmlEncode(evt.ActorName)}</b>. O processo continuará a partir deste ponto."
                        : $"O comprador <b>{System.Net.WebUtility.HtmlEncode(buyerName)}</b> assumiu a responsabilidade por {assignedScope} <b>{reqRef}</b>. O processo continuará a partir deste ponto.");
                break;
            }

            // --- Rejection/Adjustment: notify the requester ---
            case WorkflowEventCodes.AreaRejected:
            case WorkflowEventCodes.AreaAdjustment:
                // Requester becomes part of the fanning overrides, handled centrally
                await HandleAreaFanningOverridesAsync(recipients, evt, reqRef, commentHtml, isApproval: false);
                break;

            case WorkflowEventCodes.FinalRejected:
            case WorkflowEventCodes.FinalAdjustment:
                await AddUserRecipientAsync(recipients, evt.RequesterId);
                break;

            // --- Adjustment V2 (Phase 3): notify the responsible Buyer only (the sole actionable
            //     actor until Phase 4/5). No recipient resolvable (no assigned buyer) → the
            //     orchestrator safely logs "no recipients" and skips. ---
            case WorkflowEventCodes.BatchAreaAdjustment:
            case WorkflowEventCodes.BatchFinalAdjustment:
                await AddUserRecipientAsync(recipients, evt.BuyerId, bypassSelfNotifyRule: true);
                break;

            // --- Adjustment V2 (Phase 4): Buyer resolved + resubmitted → the lot is back at Area
            //     approval. Notify the Area manager(s) for the request's department + plant (the same
            //     canonical routing as pending-area-approval). No recipient resolvable → skip. ---
            case WorkflowEventCodes.BatchResubmitted:
                await AddAreaManagerRecipientsAsync(recipients, evt);
                break;

            // --- PO Registered: notify Finance users (plant-scoped; e-mail only if the company opted in) ---
            case WorkflowEventCodes.PoRegistered:
                await AddPlantScopedFinanceRecipientsAsync(recipients, evt.PlantId, evt.CompanyId);
                break;

            // --- Payment Progress: notify the requester ---
            case WorkflowEventCodes.PaymentScheduled:
            case WorkflowEventCodes.PaymentCompleted:
                await HandlePaymentFanningOverridesAsync(recipients, evt, reqRef);
                break;

            // --- Finance Return: notify the Buyer who owns the returned P.O. (docs §G.20 D1) ---
            case WorkflowEventCodes.FinanceReturned:
            {
                var owner = await ResolveFinanceReturnOwnerAsync(evt);
                if (owner.HasValue) await AddUserRecipientAsync(recipients, owner);
                // null → the caller's existing "No recipients resolved" warning; nothing is invented or fanned out.
                break;
            }

            // --- PO Correction Completed: notify Finance (same plant-scoped pattern as initial PO) ---
            case WorkflowEventCodes.PoCorrectionCompleted:
                await AddPlantScopedFinanceRecipientsAsync(recipients, evt.PlantId, evt.CompanyId);
                break;

            // --- Buy-to-Pay Advance Payments ---
            case WorkflowEventCodes.AdvancePaymentRequired:
                await AddPlantScopedFinanceRecipientsAsync(recipients, evt.PlantId, evt.CompanyId);
                break;

            case WorkflowEventCodes.AdvancePaymentScheduled:
            case WorkflowEventCodes.AdvancePaymentCompleted:
            case WorkflowEventCodes.ReconciliationCompleted:
                await HandlePaymentFanningOverridesAsync(recipients, evt, reqRef);
                break;

            // --- Lifecycle: notify key stakeholders ---
            case WorkflowEventCodes.RequestCancelled:
                await AddUserRecipientAsync(recipients, evt.RequesterId);
                await AddUserRecipientAsync(recipients, evt.BuyerId);
                await AddUserRecipientAsync(recipients, evt.AreaApproverId);
                break;

            // --- Phase 1 P.O. group cancellation / return to quotation ---
            case WorkflowEventCodes.PoGroupCancelled:
                await AddPoCancellationRecipientsAsync(recipients, evt, reqRef);
                break;
            case WorkflowEventCodes.PoCancellationConfirmed:
            case WorkflowEventCodes.PoCancellationDeclarationRejected:
                // D16: the responsible Buyer(s) only — never an e-mail to the requester for the ECF confirmation.
                await AddUserRecipientAsync(recipients, evt.BuyerId, bypassSelfNotifyRule: true);
                await AddUserRecipientAsync(recipients, evt.PreviousBuyerId, bypassSelfNotifyRule: true);
                break;

            case WorkflowEventCodes.RequestFinalized:
                await AddUserRecipientAsync(recipients, evt.RequesterId);
                break;
        }

        // Deduplicate by user (several sources may resolve the same person); then, by normalized e-mail, suppress the
        // DELIVERY for a second account sharing one mailbox while keeping that account's own in-app notice (the first
        // resolved account keeps its wording and the single e-mail).
        var byUser = recipients.GroupBy(r => r.UserId).Select(g => g.First()).ToList();
        var seenEmails = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<NotificationRecipient>(byUser.Count);
        foreach (var r in byUser)
        {
            var key = NormalizeEmail(r.Email);
            if (key != null && !seenEmails.Add(key))
            {
                _logger.LogDebug("Recipient {UserId}: e-mail {Email} already addressed for event {EventCode}; in-app kept, delivery suppressed", r.UserId, key, evt.EventCode);
                deduped.Add(r with { SuppressEmail = true });
                continue;
            }
            deduped.Add(r);
        }
        return deduped;
    }

    private static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>
    /// §8 BUYER_TRANSFERRED recipients — persisted relationships only, never role groups:
    ///  1. previous Buyer and 2. new Buyer (role-specific wording), 3. requester;
    ///  4. persisted pointers: Request.AreaApproverId / FinalApproverId, P.O. group owners (PoResponsibleBuyerId), receipt
    ///     completers and fiscal-receipt uploaders, payment schedulers/payers, winner selectors, rework requesters, reuse authorisers;
    ///  5. actors of explicit participant history actions (BuyerTakeOverPolicy.ParticipantHistoryActions);
    ///  6. PENDING area approvers only while an area decision is pending, through the established routing resolver
    ///     (plant-specific managers first, else global managers; active users only);
    ///  7. PENDING final approver only while a final decision is pending, through the established final-recipient resolver
    ///     (request nominee, else company nominee) — persisted nominations, never the "Final Approver" role group.
    /// Active users only (AddUserRecipientAsync); e-mail only when an address exists; dedup by user and e-mail at the end.
    /// </summary>
    private async Task AddBuyerTransferRecipientsAsync(List<NotificationRecipient> recipients, WorkflowEvent evt, string reqRef)
    {
        var req = await _context.Requests.AsNoTracking()
            .Include(r => r.Status)
            .Include(r => r.ApprovalBatches)
            .Include(r => r.PoGroups)
            .FirstOrDefaultAsync(r => r.Id == evt.RequestId);
        if (req == null) return;
        var names = await _context.Users.AsNoTracking()
            .Where(u => u.Id == evt.BuyerId || u.Id == evt.PreviousBuyerId)
            .Select(u => new { u.Id, u.FullName }).ToListAsync();
        string NameOf(Guid? id) => names.FirstOrDefault(n => n.Id == id)?.FullName ?? "—";
        var newName = System.Net.WebUtility.HtmlEncode(NameOf(evt.BuyerId));
        var prevName = System.Net.WebUtility.HtmlEncode(NameOf(evt.PreviousBuyerId));
        var actorName = System.Net.WebUtility.HtmlEncode(evt.ActorName);
        var when = AppTimeZone.FormatWithUtc(evt.OccurredAtUtc ?? DateTime.UtcNow);
        var just = System.Net.WebUtility.HtmlEncode(evt.Justification ?? "");
        var details = BuildBuyerTransferDetailsHtml(reqRef, evt.RequestTitle, prevName, newName, actorName, when, just);

        await AddUserRecipientAsync(recipients, evt.PreviousBuyerId,
            emailSubjectOverride: $"Deixou de ser o comprador responsável — {reqRef}",
            emailBodyOverride: $"O pedido <b>{reqRef}</b> deixou de estar sob a sua responsabilidade: o comprador <b>{newName}</b> assumiu-o.<br/><br/>{details}");
        await AddUserRecipientAsync(recipients, evt.BuyerId,
            emailSubjectOverride: $"Passou a ser o comprador responsável — {reqRef}",
            emailBodyOverride: $"Assumiu a responsabilidade pelo pedido <b>{reqRef}</b>, anteriormente de <b>{prevName}</b>.<br/><br/>{details}",
            bypassSelfNotifyRule: true);
        await AddUserRecipientAsync(recipients, evt.RequesterId);

        var ids = new HashSet<Guid>();
        void Add(Guid? id) { if (id.HasValue && id.Value != Guid.Empty) ids.Add(id.Value); }
        Add(req.AreaApproverId); Add(req.FinalApproverId);
        foreach (var g in req.PoGroups) { Add(g.PoResponsibleBuyerId); Add(g.OperationalReceiptCompletedByUserId); Add(g.FiscalReceiptUploadedByUserId); }
        foreach (var pmt in await _context.RequestPayments.AsNoTracking().Where(x => x.RequestId == req.Id).Select(x => new { x.ScheduledByUserId, x.PaidByUserId }).ToListAsync())
        { Add(pmt.ScheduledByUserId); Add(pmt.PaidByUserId); }
        var batchIds = req.ApprovalBatches.Select(b => b.Id).ToList();
        if (batchIds.Count > 0)
        {
            foreach (var id in await _context.ApprovalBatchItems.AsNoTracking().Where(bi => batchIds.Contains(bi.ApprovalBatchId) && bi.WinnerSelectedByUserId != null).Select(bi => bi.WinnerSelectedByUserId!.Value).Distinct().ToListAsync()) Add(id);
            foreach (var id in await _context.ApprovalBatchAdjustments.AsNoTracking().Where(a => batchIds.Contains(a.ApprovalBatchId)).Select(a => a.RequestedByUserId).Distinct().ToListAsync()) Add(id);
        }
        foreach (var id in await _context.QuotationReuseAuthorizations.AsNoTracking().Where(a => a.RequestId == req.Id).Select(a => a.AuthorizedByUserId).Distinct().ToListAsync()) Add(id);
        var actions = BuyerTakeOverPolicy.ParticipantHistoryActions.ToList();
        foreach (var id in await _context.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == req.Id && actions.Contains(h.ActionTaken)).Select(h => h.ActorUserId).Distinct().ToListAsync()) Add(id);
        foreach (var id in ids) await AddUserRecipientAsync(recipients, id);

        var statusCode = req.Status?.Code ?? string.Empty;
        var pendingArea = BuyerTakeOverPolicy.PendingAreaStatuses.Contains(statusCode) || req.ApprovalBatches.Any(b => BuyerTakeOverPolicy.PendingAreaStatuses.Contains(b.Status));
        if (pendingArea) await AddAreaManagerRecipientsAsync(recipients, evt);
        var pendingFinal = BuyerTakeOverPolicy.PendingFinalStatuses.Contains(statusCode) || req.ApprovalBatches.Any(b => BuyerTakeOverPolicy.PendingFinalStatuses.Contains(b.Status));
        if (pendingFinal)
        {
            var fin = await _approvalRouting.ResolveFinalNotificationRecipientsAsync(req.FinalApproverId, req.CompanyId);
            foreach (var f in fin.Recipients) recipients.Add(new NotificationRecipient(f.UserId, f.Email, f.FullName));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  Phase 1 P.O. group cancellation — recipients and wording (docs/PO_GROUP_CANCELLATION.md §I)
    //  Every text describes HUMAN statements: the Buyer DECLARES, Finance CONFIRMS. The Portal never claims to have
    //  annulled or verified an ECF in Primavera.
    // ═══════════════════════════════════════════════════════════════════════════════════════

    private async Task AddPoCancellationRecipientsAsync(List<NotificationRecipient> recipients, WorkflowEvent evt, string reqRef)
    {
        var texts = BuildPoCancellationTexts(evt, reqRef);

        // Responsible Buyer(s): request owner and the group's P.O. responsible (may differ), plus the actor (self-notify).
        var before = recipients.Count;
        await AddUserRecipientAsync(recipients, evt.BuyerId, texts.BuyerSubject, texts.BuyerBody, bypassSelfNotifyRule: true);
        await AddUserRecipientAsync(recipients, evt.PreviousBuyerId, texts.BuyerSubject, texts.BuyerBody, bypassSelfNotifyRule: true);
        var buyerResolved = recipients.Count > before;
        var buyerEmailQueued = recipients.Skip(before).Any(r => !string.IsNullOrWhiteSpace(r.Email) && !r.SuppressEmail);

        // Requester (D16: in-app + e-mail for the cancellation itself), area managers, final approver when persisted.
        await AddUserRecipientAsync(recipients, evt.RequesterId, texts.ParticipantSubject, texts.ParticipantBody);
        if (evt.DepartmentId.HasValue && evt.DepartmentId.Value != 0)
        {
            var routing = await _approvalRouting.ResolveAreaManagersAsync(evt.DepartmentId.Value, evt.PlantId);
            foreach (var m in routing?.Managers ?? new())
                await AddUserRecipientAsync(recipients, m.UserId, texts.ParticipantSubject, texts.ParticipantBody);
        }
        await AddUserRecipientAsync(recipients, evt.AreaApproverId, texts.ParticipantSubject, texts.ParticipantBody);
        await AddUserRecipientAsync(recipients, evt.FinalApproverId, texts.ParticipantSubject, texts.ParticipantBody);
        var finalApprovers = await _context.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == evt.RequestId && h.ActionTaken == "BATCH_FINAL_APPROVED")
            .Select(h => h.ActorUserId).Distinct().ToListAsync();
        foreach (var id in finalApprovers) await AddUserRecipientAsync(recipients, id, texts.ParticipantSubject, texts.ParticipantBody);

        // Finance — EXPLICIT POLICY (docs/PO_GROUP_CANCELLATION.md §8): active users with the Finance role scoped to the group's
        // plant get the in-app notice AND the e-mail, regardless of the company AP switch (NotifyFinanceUsersByEmail governs
        // other events only). Never a wider set: without a plant the fallback stays in-app only. The AP mailbox is additional
        // and keeps its own toggle (NotifyOnPoCancelled).
        var financeStart = recipients.Count;
        await AddPlantScopedFinanceRecipientsAsync(recipients, evt.PlantId, evt.CompanyId, emailRegardlessOfApSwitch: true);
        var financeEligible = 0; var financeWithEmail = 0; var financeQueued = 0;
        for (var i = financeStart; i < recipients.Count; i++)
        {
            recipients[i] = recipients[i] with { EmailSubjectOverride = texts.FinanceSubject, EmailBodyOverride = texts.FinanceBody };
            financeEligible++;
            var hasEmail = !string.IsNullOrWhiteSpace(recipients[i].Email);
            if (hasEmail) financeWithEmail++;
            if (hasEmail && !recipients[i].SuppressEmail) financeQueued++;
        }
        var policy = !evt.PlantId.HasValue ? PoCancellationRecipientOutcome.PolicyNoPlantInAppOnly
            : financeEligible == 0 ? PoCancellationRecipientOutcome.PolicyNoEligibleUsers
            : financeWithEmail == 0 ? PoCancellationRecipientOutcome.PolicyUsersWithoutEmail
            : PoCancellationRecipientOutcome.PolicyQueued;
        if (policy != PoCancellationRecipientOutcome.PolicyQueued)
            _logger.LogWarning("PO_GROUP_CANCELLED on Request {RequestId}: Finance e-mail policy outcome {Policy} (plant {PlantId}, eligible {Eligible}, with e-mail {WithEmail}); the Finance confirmation queue remains the durable path.", evt.RequestId, policy, evt.PlantId, financeEligible, financeWithEmail);

        evt.OnRecipientsResolved?.Invoke(new PoCancellationRecipientOutcome(financeEligible, financeWithEmail, financeQueued, policy, buyerResolved, buyerEmailQueued));
    }

    private sealed record PoCancellationTexts(string InAppTitle, string InAppMessage, string BuyerSubject, string BuyerBody,
        string ParticipantSubject, string ParticipantBody, string FinanceSubject, string FinanceBody, string FinanceAsk);

    private static PoCancellationTexts BuildPoCancellationTexts(WorkflowEvent evt, string reqRef)
    {
        Func<string?, string> enc = x => System.Net.WebUtility.HtmlEncode(x ?? "");
        var restart = evt.PoCancellationKind == PoGroupCancellationConstants.Kinds.RestartQuotation;
        var outcome = restart
            ? "Os itens afetados voltam à cotação e seguirão um NOVO ciclo de aprovação (área e final)."
            : "Os itens afetados foram encerrados; nada será comprado para eles neste grupo.";
        var when = AppTimeZone.FormatWithUtc(evt.OccurredAtUtc ?? DateTime.UtcNow);
        var reason = enc(evt.Comment);
        var poRef = string.IsNullOrWhiteSpace(evt.PreviousPoNumber) ? null : enc(evt.PreviousPoNumber);
        var ecfRef = string.IsNullOrWhiteSpace(evt.EcfReference) ? null : enc(evt.EcfReference);
        var isResend = evt.ActionTaken == "NOTIFICATION_RESENT";
        var isUpdate = evt.ActionTaken == "ECF_DECLARATION_UPDATED";
        var prefix = isResend ? "[Reenvio] " : isUpdate ? "[Declaração atualizada] " : "";

        var details = $"<b>Pedido:</b> {enc(reqRef)}{(string.IsNullOrWhiteSpace(evt.RequestTitle) ? "" : " — " + enc(evt.RequestTitle))}<br/>" +
                      $"<b>Ação:</b> {(restart ? "P.O cancelada — itens voltam a cotação" : "P.O cancelada — itens encerrados")}<br/>" +
                      $"<b>Ação de:</b> {enc(evt.ActorName)} ({when})<br/>" +
                      $"<b>Motivo:</b> {reason}<br/>" +
                      $"<b>P.O registada anteriormente:</b> {poRef ?? "nenhuma"}<br/>" +
                      (evt.BatchNumber.HasValue ? $"<b>Lote de aprovação:</b> #{evt.BatchNumber}<br/>" : "");

        // Buyer DECLARATION (never a Portal verification)
        string declaration, financeAsk;
        switch (evt.EcfStatus)
        {
            case PoGroupCancellationConstants.EcfStatuses.NotApplicable:
                declaration = "Não existe ECF/P.O emitida para este grupo; não há documento externo a anular.";
                financeAsk = "Nenhuma ação é necessária por parte de Finanças. Este aviso é apenas informativo.";
                break;
            case PoGroupCancellationConstants.EcfStatuses.DeclaredCancelled:
                declaration = $"O comprador <b>declara</b> que a ECF {ecfRef ?? ""} já foi anulada fora do Portal. Esta é uma declaração do comprador; o Portal não verifica o Primavera.";
                financeAsk = "Pedimos que verifiquem a anulação no Primavera e, em Obrigações &amp; Pagamentos → Cancelamentos a confirmar, <b>confirmem a anulação</b> ou <b>rejeitem a declaração</b> indicando o motivo. " +
                             "Enquanto a confirmação estiver pendente, o registo de uma nova P.O para os itens afetados fica bloqueado; a nova cotação e a aprovação podem prosseguir.";
                break;
            case PoGroupCancellationConstants.EcfStatuses.PendingExternal:
                declaration = $"O comprador <b>declara</b> que a ECF {ecfRef ?? "associada a esta P.O"} <b>ainda não foi anulada</b>. O Portal não anula nem verifica documentos no Primavera.";
                financeAsk = "Pedimos que a ECF seja anulada no Primavera (ou que a anulação seja coordenada com quem a executa) e que, depois, a anulação seja <b>confirmada no Portal</b> em Obrigações &amp; Pagamentos → Cancelamentos a confirmar. " +
                             "Enquanto a confirmação estiver pendente, o registo de uma nova P.O para os itens afetados fica bloqueado; a nova cotação e a aprovação podem prosseguir.";
                break;
            default:
                declaration = "O comprador <b>declara desconhecer</b> a situação da ECF associada a esta P.O. O Portal não verifica o Primavera.";
                financeAsk = "Pedimos que verifiquem a situação da ECF no Primavera, a anulem se ainda estiver ativa e, em seguida, <b>confirmem a anulação</b> no Portal (Obrigações &amp; Pagamentos → Cancelamentos a confirmar), ou rejeitem a declaração com o motivo. " +
                             "Enquanto a confirmação estiver pendente, o registo de uma nova P.O para os itens afetados fica bloqueado.";
                break;
        }

        var financeSubject = evt.EcfStatus switch
        {
            PoGroupCancellationConstants.EcfStatuses.NotApplicable => $"{prefix}Grupo P.O cancelado antes do registo da P.O — Pedido {reqRef}",
            PoGroupCancellationConstants.EcfStatuses.DeclaredCancelled => $"{prefix}Cancelamento de P.O — confirmar anulação da ECF {evt.EcfReference ?? ""} — Pedido {reqRef}".Replace("  ", " "),
            _ => $"{prefix}Cancelamento de P.O — anulação de ECF necessária — Pedido {reqRef}",
        };
        var financeBody = $"O comprador <b>{enc(evt.ActorName)}</b> cancelou no Portal a P.O do grupo do pedido <b>{enc(reqRef)}</b>.<br/><br/>{details}<br/>" +
                          $"<b>Situação da ECF (declaração do comprador):</b> {declaration}<br/><br/>" +
                          $"<p style='padding:12px; background:#fff8e1; border-left:4px solid #f39c12;'>{financeAsk}</p>" +
                          $"<p style='font-size:12px; color:#666;'>{outcome}</p>";

        var buyerSubject = $"{prefix}P.O cancelada — {(restart ? "itens voltam a cotação" : "itens encerrados")} — {reqRef}";
        var buyerBody = $"A P.O do grupo do pedido <b>{enc(reqRef)}</b> foi cancelada no Portal.<br/><br/>{details}<br/>{outcome}" +
                        (evt.FinanceConfirmationRequired
                            ? $"<br/><br/><b>Situação da ECF (sua declaração):</b> {declaration}<br/>" +
                              "<b>Importante:</b> a nova P.O para os itens afetados só poderá ser registada após Finanças confirmar a anulação da ECF anterior. A nova cotação e as aprovações podem prosseguir entretanto."
                            : "");

        var participantSubject = $"{prefix}P.O cancelada no pedido {reqRef}";
        var participantBody = $"A P.O do grupo do pedido <b>{enc(reqRef)}</b> foi cancelada por <b>{enc(evt.ActorName)}</b>.<br/><br/>{details}<br/>{outcome}" +
                              (evt.FinanceConfirmationRequired ? "<br/><br/>A nova P.O para os itens afetados só poderá ser registada após Finanças confirmar a anulação da ECF anterior." : "");

        var inAppTitle = restart ? "P.O cancelada — itens voltam a cotação" : "P.O cancelada — itens encerrados";
        var inAppMessage = $"{prefix}{enc(evt.ActorName)} cancelou a P.O do grupo do pedido {reqRef}{(poRef != null ? $" (P.O {poRef})" : "")}. Motivo: {evt.Comment}. " +
                           (evt.FinanceConfirmationRequired ? $"ECF: {(evt.EcfStatus == PoGroupCancellationConstants.EcfStatuses.DeclaredCancelled ? "comprador declara anulada — confirmação de Finanças pendente" : evt.EcfStatus == PoGroupCancellationConstants.EcfStatuses.PendingExternal ? "anulação pendente (declaração do comprador)" : "situação desconhecida — Finanças deve verificar")}." : "Sem ECF emitida.");

        return new PoCancellationTexts(inAppTitle, inAppMessage, buyerSubject, buyerBody, participantSubject, participantBody, financeSubject, financeBody, financeAsk);
    }

    /// <summary>
    /// Phase 1 P.O. cancellation — Accounts Payable notice through the OUTBOX (durable, retried by EmailOutboxProcessor, redirected
    /// in non-PROD by EmailService at send time, deduplicated per correlation + AP address). Written on the caller's context, so it
    /// commits or rolls back with the business change. Requires an active company configuration with NotifyOnPoCancelled.
    /// </summary>
    private async Task QueueAccountsPayableOutboxAsync(WorkflowEvent evt)
    {
        if (!evt.CompanyId.HasValue) return;
        var config = await _context.AccountsPayableNotificationConfigs.AsNoTracking().Include(c => c.Company)
            .FirstOrDefaultAsync(c => c.CompanyId == evt.CompanyId.Value && c.IsActive);
        if (config == null || !config.NotifyOnPoCancelled || string.IsNullOrWhiteSpace(config.Email)) return;

        var alreadyQueued = await _context.EmailOutbox.AnyAsync(e => e.CorrelationId == evt.CorrelationId && e.RecipientEmail == config.Email && e.Status != "DEAD_LETTER");
        if (alreadyQueued)
        {
            _logger.LogDebug("AP outbox dedup: notice already queued for {ApEmail} (CorrelationId {CorrelationId})", config.Email, evt.CorrelationId);
            return;
        }

        var req = await _context.Requests.AsNoTracking().Where(r => r.Id == evt.RequestId).Select(r => new { r.Title }).FirstOrDefaultAsync();
        var group = evt.PoGroupId.HasValue
            ? await _context.RequestPoGroups.AsNoTracking()
                .Where(g => g.Id == evt.PoGroupId.Value && g.RequestId == evt.RequestId)
                .Select(g => new { SupplierName = g.Supplier != null ? g.Supplier.Name : g.SupplierNameSnapshot, g.TotalAmount, g.CurrencyCode })
                .FirstOrDefaultAsync()
            : null;
        var companyName = config.Company?.Name ?? "Empresa";
        var supplierName = string.IsNullOrWhiteSpace(group?.SupplierName) ? "\u2014" : group!.SupplierName!;
        var amount = group?.TotalAmount ?? 0m;
        var currency = string.IsNullOrWhiteSpace(group?.CurrencyCode) ? "AOA" : group!.CurrencyCode!;
        var reqRef = evt.RequestNumber;
        var subject = $"[Portal Gerencial] P.O. cancelada \u2014 {(evt.PoCancellationKind == PoGroupCancellationConstants.Kinds.RestartQuotation ? "itens voltam a cota\u00e7\u00e3o" : "itens encerrados")} \u2014 Pedido {reqRef}";
        var headline = $"P.O. cancelada \u2014 {companyName}";
        var bodyHtml = BuildApPoCancelledBody(evt, reqRef, req?.Title ?? "", supplierName, amount, currency, companyName);
        var frontendBaseUrl = _config["AppConfig:FrontendBaseUrl"]?.TrimEnd('/') ?? "";

        _context.EmailOutbox.Add(new EmailOutboxEntry
        {
            RecipientEmail = config.Email,
            RecipientName = string.IsNullOrWhiteSpace(config.Label) ? "Contas a Pagar" : config.Label,
            Subject = subject,
            Headline = headline,
            BodyHtml = bodyHtml,
            ActionUrl = string.IsNullOrWhiteSpace(frontendBaseUrl) ? null : $"{frontendBaseUrl}/finance?requestId={evt.RequestId}",
            ActionLabel = "Ver Pedido",
            CcEmails = config.CcEmails,
            RequestId = evt.RequestId,
            RequestNumber = evt.RequestNumber,
            EventCode = evt.EventCode,
            CorrelationId = evt.CorrelationId,
            Status = "PENDING",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        await _adminLog.WriteAsync("Info", "WorkflowNotificationOrchestrator", "EMAIL_OUTBOX_QUEUED",
            $"Aviso de Contas a Pagar (P.O. cancelada) enfileirado para {config.Email}. Pedido: {evt.RequestNumber}.",
            payload: $"RequestId: {evt.RequestId}. CorrelationId: {evt.CorrelationId}. Subject: {subject}.");
    }

    private static string BuildApPoCancelledBody(WorkflowEvent evt, string reqRef, string requestTitle, string supplierName, decimal amount, string currency, string companyName)
    {
        Func<string?, string> enc = x => System.Net.WebUtility.HtmlEncode(x ?? "");
        var when = AppTimeZone.FormatWithUtc(evt.OccurredAtUtc ?? DateTime.UtcNow);
        var ecf = evt.EcfStatus switch
        {
            PoGroupCancellationConstants.EcfStatuses.NotApplicable => "não aplicável — nenhuma P.O/ECF emitida",
            PoGroupCancellationConstants.EcfStatuses.DeclaredCancelled => $"o comprador declara que a ECF {enc(evt.EcfReference)} já foi anulada fora do Portal (confirmação de Finanças pendente)",
            PoGroupCancellationConstants.EcfStatuses.PendingExternal => "o comprador declara que a ECF ainda não foi anulada (anulação e confirmação de Finanças pendentes)",
            _ => "situação desconhecida segundo o comprador (verificação de Finanças pendente)",
        };
        return $@"
<p>Uma Purchase Order foi <b>cancelada</b> no Portal Gerencial. Este aviso descreve uma ação no Portal; o Portal não anula nem verifica documentos no Primavera.</p>
<div style='background-color:#f8f9fa; border:1px solid #dee2e6; padding:15px; border-radius:6px; margin:20px 0;'>
    <table style='width:100%; font-size:14px; color:#333; border-collapse:collapse;'>
        <tr><td style='padding:6px 0;'><b>Empresa:</b></td><td style='padding:6px 0;'>{enc(companyName)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Pedido:</b></td><td style='padding:6px 0;'>{enc(reqRef)}</td></tr>
        <tr><td style='padding:6px 0;'><b>T&#237;tulo:</b></td><td style='padding:6px 0;'>{enc(requestTitle)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Fornecedor:</b></td><td style='padding:6px 0;'>{enc(supplierName)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Total do grupo P.O. cancelado:</b></td><td style='padding:6px 0;'>{amount:N2} {enc(currency)}</td></tr>
        <tr><td style='padding:6px 0;'><b>P.O. anterior:</b></td><td style='padding:6px 0;'>{enc(evt.PreviousPoNumber ?? "nenhuma registada")}</td></tr>
        <tr><td style='padding:6px 0;'><b>Resultado:</b></td><td style='padding:6px 0;'>{(evt.PoCancellationKind == PoGroupCancellationConstants.Kinds.RestartQuotation ? "itens voltam a cota&#231;&#227;o (novo ciclo de aprova&#231;&#227;o)" : "itens encerrados")}</td></tr>
        <tr><td style='padding:6px 0;'><b>ECF:</b></td><td style='padding:6px 0;'>{ecf}</td></tr>
        <tr><td style='padding:6px 0;'><b>Motivo:</b></td><td style='padding:6px 0;'>{enc(evt.Comment)}</td></tr>
        <tr><td style='padding:6px 0;'><b>A&#231;&#227;o realizada por:</b></td><td style='padding:6px 0;'>{enc(evt.ActorName)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Data/hora:</b></td><td style='padding:6px 0;'>{when}</td></tr>
    </table>
</div>
<p style='font-size:13px; color:#333;'><b>Nenhum pagamento deve ser efetuado para esta P.O.</b>{(evt.FinanceConfirmationRequired ? " A anula&#231;&#227;o da ECF deve ser confirmada no Portal por um utilizador Finan&#231;as da planta." : "")}</p>
<p style='font-size:12px; color:#999;'>Esta &#233; uma mensagem autom&#225;tica do Portal Gerencial.</p>";
    }

    private static string BuildBuyerTransferDetailsHtml(string reqRef, string? title, string prevName, string newName, string actorName, string when, string justificationHtml)
        => $"<b>Pedido:</b> {reqRef}{(string.IsNullOrWhiteSpace(title) ? "" : " — " + System.Net.WebUtility.HtmlEncode(title))}<br/>" +
           $"<b>Comprador anterior:</b> {prevName}<br/>" +
           $"<b>Novo comprador:</b> {newName}<br/>" +
           $"<b>Ação de:</b> {actorName}<br/>" +
           $"<b>Data/hora da transferência:</b> {when}<br/>" +
           $"<b>Justificação:</b> {justificationHtml}";


    // Helper method to keep ResolveRecipientsAsync clean
    private async Task HandlePendingAreaApprovalFanningAsync(List<NotificationRecipient> recipients, WorkflowEvent evt, string reqRef, bool isQuotation)
    {
        // 1. If Quotation Workflow, we must notify the Requester that the quotation finished. 
        // (For Direct Requests, they already received SubmissionConfirmed event, no need to duplicate)
        if (isQuotation)
        {
            await AddUserRecipientAsync(recipients, evt.RequesterId,
                emailSubjectOverride: $"Cotação Concluída — Pedido {reqRef}",
                emailBodyOverride: $"A cotação para o pedido <b>{reqRef}</b> foi submetida pelo departamento de compras e enviada para a Aprovação de Área.");
        }

        // 2. Notify the Area Manager(s) with the Contexto Financeiro Departamental.
        // Phase B: recipients come EXCLUSIVELY from the DepartmentManager routing
        // (strict cascade — plant-specific, else global, else none). evt.AreaApproverId
        // and Department.ResponsibleUserId are no longer used to pick recipients.
        var req = await _context.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == evt.RequestId);
        if (req == null) return;

        var deptId = evt.DepartmentId ?? req.DepartmentId;
        var plantId = evt.PlantId ?? req.PlantId;
        var thisAmount = req.EstimatedTotalAmount;
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        
        var summedAmount = await _context.Requests
            .AsNoTracking()
            .Where(r => r.DepartmentId == deptId 
                     && (r.Status.Code == "SCHEDULED" || r.Status.Code == "PAID" || r.Status.Code == "PARTIAL_PAID")
                     && r.UpdatedAtUtc >= monthStart)
            .SumAsync(r => r.EstimatedTotalAmount);

        var percentage = summedAmount > 0 ? (thisAmount / summedAmount * 100) : 0;
        
        var currency = req.CurrencyId.HasValue 
            ? await _context.Currencies.AsNoTracking().Where(c => c.Id == req.CurrencyId).Select(c => c.Code).FirstOrDefaultAsync() ?? "AOA" 
            : "AOA";

        var htmlOverride = $@"
<p>O pedido <b>{reqRef}</b> acabou de dar entrada na sua fila de área e aguarda a sua validação ou rateio de centro de custo.</p>
<div style='background-color:#fff1f2; border:1px solid #fecdd3; padding:15px; border-radius:6px; margin:20px 0;'>
    <h3 style='color:#be123c; margin-top:0;'>Contexto Financeiro Departamental (Mês Corrente)</h3>
    <ul style='color:#881337; font-size:14px; margin-bottom:0;'>
        <li style='margin-bottom: 5px'><b>Valor da Proposta Atual:</b> {thisAmount:N2} {currency}</li>
        <li style='margin-bottom: 5px'><b>Consumo Departamental Atual (Agendado/Pago):</b> {summedAmount:N2} {currency}</li>
        <li><b>Impacto Estimado:</b> Aprovar este pedido preencherá um rácio financeiro de <b>{percentage:N1}%</b> face ao orçamento consumido do departamento neste mês.</li>
    </ul>
</div>";

        var subjectOverride = $"[AÇÃO NECESSÁRIA] Aprovação de Área pendente - Pedido {reqRef}";

        var routing = await _approvalRouting.ResolveAreaManagersAsync(deptId, plantId);
        if (!routing.HasManagers)
        {
            _logger.LogError(
                "APPROVAL_EMAIL_NO_RECIPIENT: no area manager resolvable for Request {RequestId} (Dept {DeptId}, Plant {PlantId}, Event {EventCode})",
                evt.RequestId, deptId, plantId, evt.EventCode);
            await _adminLog.WriteAsync("Error", "WorkflowNotificationOrchestrator", "APPROVAL_EMAIL_NO_RECIPIENT",
                $"Nenhum responsável de área resolvível para o pedido {reqRef}. Nenhum e-mail de aprovação foi enviado.",
                payload: $"RequestId: {evt.RequestId}. DepartmentId: {deptId}. PlantId: {plantId?.ToString() ?? "null"}. Evento: {evt.EventCode}. Motivo: sem DepartmentManager ativo (específico ou global).");
            return;
        }

        foreach (var manager in routing.Managers)
        {
            recipients.Add(new NotificationRecipient(manager.UserId, manager.Email, manager.FullName)
            {
                EmailSubjectOverride = subjectOverride,
                EmailBodyOverride = htmlOverride
            });
        }
    }

    private async Task HandleAreaFanningOverridesAsync(List<NotificationRecipient> recipients, WorkflowEvent evt, string reqRef, string commentHtml, bool isApproval)
    {
        var actionWord = isApproval ? "aprovou" : (evt.EventCode == WorkflowEventCodes.AreaAdjustment ? "pediu reajustes no" : "rejeitou o");
        var actionWordDesc = isApproval ? "aprovado" : (evt.EventCode == WorkflowEventCodes.AreaAdjustment ? "com reajuste solicitado" : "rejeitado");
        var subjectWord = isApproval ? "Aprovado" : (evt.EventCode == WorkflowEventCodes.AreaAdjustment ? "Reajuste Solicitado" : "Rejeitado");

        // 1. Requester
        await AddUserRecipientAsync(recipients, evt.RequesterId,
            emailSubjectOverride: $"O seu pedido {reqRef} foi {actionWordDesc} na área",
            emailBodyOverride: $"O aprovador de área (<b>{evt.ActorName}</b>) {actionWord} pedido <b>{reqRef}</b>.{commentHtml}");

        // 2. Approver (Actor)
        await AddUserRecipientAsync(recipients, evt.AreaApproverId, // Assuming actor is AreaApproverId
            emailSubjectOverride: $"Decisão de Área Registada: {reqRef} {subjectWord}",
            emailBodyOverride: $"Você {actionWord} pedido <b>{reqRef}</b> na etapa área.{commentHtml}",
            bypassSelfNotifyRule: true);

        // 3. Buyer (only if QUOTATION)
        var req = await _context.Requests.Include(r => r.RequestType).FirstOrDefaultAsync(r => r.Id == evt.RequestId);
        if (req?.RequestType?.Code == "QUOTATION")
        {
            await AddUserRecipientAsync(recipients, evt.BuyerId,
                emailSubjectOverride: $"Cotação — Pedido {reqRef} {actionWordDesc} na área",
                emailBodyOverride: $"O pedido de cotação <b>{reqRef}</b> foi {actionWordDesc} pelo aprovador de área (<b>{evt.ActorName}</b>).{commentHtml}");
        }
    }

    private async Task HandlePaymentFanningOverridesAsync(List<NotificationRecipient> recipients, WorkflowEvent evt, string reqRef)
    {
        // 1. Always notify the Requester (using the default EventConfig)
        await AddUserRecipientAsync(recipients, evt.RequesterId);

        // 2. Area Approvers Fan-Out Notification
        if (!evt.DepartmentId.HasValue) return;

        var req = await _context.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == evt.RequestId);
        if (req == null) return;

        var ctxMonth = await ComputeDepartmentalMonthContextAsync(req, evt.DepartmentId.Value);
        var isScheduledEvent = evt.EventCode == WorkflowEventCodes.PaymentScheduled;
        // The acted-on unit (same resolver as the Accounts Payable notice): planned amount when scheduling, actual paid
        // amount when completing, with the payment row's / group's currency. Informative only — never part of the ratio.
        var unit = await ResolveApPaymentUnitAsync(evt, req, req.Supplier?.Name ?? "\u2014", ctxMonth.RequestCurrency ?? "AOA", req.ActualPaidAmount ?? req.EstimatedTotalAmount);

        // Phase B: departmental payment info goes to the area managers resolved by
        // department + PLANT (strict cascade) — fixes the cross-plant leak where every
        // approver with a department scope received it regardless of plant.
        var paymentRouting = await _approvalRouting.ResolveAreaManagersAsync(evt.DepartmentId.Value, evt.PlantId ?? req.PlantId);
        var areaApprovers = paymentRouting.Managers;

        Func<string?, string> enc = System.Net.WebUtility.HtmlEncode;
        var paymentState = isScheduledEvent ? "Agendado" : "Realizado";
        var actionLabel = isScheduledEvent ? "Valor desta a\u00e7\u00e3o (agendado, grupo P.O.)" : "Valor desta a\u00e7\u00e3o (pago, grupo P.O.)";
        var actionHtml = unit.FromPaymentRow
            ? $"{unit.Amount:N2} {enc(unit.Currency)}"
            : "<b>n/d</b> (sem registo de pagamento associado a esta a\u00e7\u00e3o)";

        var cur = ctxMonth.Current;
        var thisHtml = cur.IsResolved
            ? $"{cur.Amount:N2} {enc(cur.Currency)} <span style='color:#555;'>(base: {SourceLabel(cur.Source)})</span>"
            : $"<b>n/d</b> \u2014 {cur.Reason}";
        var totalHtml = cur.IsResolved
            ? $"{ctxMonth.TotalAmount!.Value:N2} {enc(cur.Currency)} <span style='color:#555;'>(composi\u00e7\u00e3o: {ctxMonth.RecordedApprovedCount} aprovado(s) registado(s), {ctxMonth.DerivedApprovedCount} com moeda derivada, {ctxMonth.EstimateFallbackCount} estimativa(s) \u2014 fallback)</span>"
            : $"<b>n/d</b> \u2014 {cur.Reason}";
        var impactHtml = ctxMonth.Percentage.HasValue
            ? $"Este pedido representa <b>{ctxMonth.Percentage.Value:N1}%</b> do acumulado resolvido ({enc(cur.Currency)}) do seu departamento."
            : (cur.IsResolved
                ? "<b>n/d</b> \u2014 sem base de compara\u00e7\u00e3o (acumulado ou valor deste pedido igual a zero)."
                : $"<b>n/d</b> \u2014 {cur.Reason}");
        var exclusionHtml = ctxMonth.ExcludedOtherCurrencyCount + ctxMonth.ExcludedUnresolvedCount > 0
            ? $"\n        <li style='margin-bottom: 5px'><b>Exclu\u00eddos:</b> {ctxMonth.ExcludedOtherCurrencyCount + ctxMonth.ExcludedUnresolvedCount} pedido(s) agendado(s)/pago(s) do departamento neste per\u00edodo \u2014 {ctxMonth.ExcludedOtherCurrencyCount} noutra moeda, {ctxMonth.ExcludedUnresolvedCount} sem valor resolvido ({ctxMonth.ExcludedUnresolvedReasons}). O acumulado e a percentagem n\u00e3o representam toda a atividade do departamento.</li>"
            : "";
        var departmentalContextHtml = $@"
<div style='background-color:#f0f9ff; border:1px solid #bae6fd; padding:15px; border-radius:6px; margin:20px 0;'>
    <h3 style='color:#0369a1; margin-top:0;'>Contexto Financeiro Departamental (pedidos atualizados no mês corrente)</h3>
    <ul style='color:#0c4a6e; font-size:14px; margin-bottom:0;'>
        <li style='margin-bottom: 5px'><b>{actionLabel}:</b> {actionHtml}</li>
        <li style='margin-bottom: 5px'><b>Valor deste pedido:</b> {thisHtml}</li>
        <li style='margin-bottom: 5px'><b>Acumulado (pedidos agendados/pagos atualizados no mês, na moeda resolvida deste pedido, incl. este):</b> {totalHtml}</li>
        <li style='margin-bottom: 5px'><b>Impacto:</b> {impactHtml}</li>{exclusionHtml}
        <li style='font-size:12px;'><b>Período:</b> pedidos do departamento com última atualização desde {ctxMonth.MonthStartUtc:dd/MM/yyyy} (UTC); não comprova que o agendamento/pagamento ocorreu neste mês. Valores ao nível do pedido (aprovado registado, aprovado com moeda derivada de dados atuais consistentes, ou estimativa como fallback), não montantes agendados/pagos.</li>
    </ul>
</div>";
        var htmlOverride = $"\n<p>O processo financeiro para o pedido <b>{reqRef}</b> foi <b>{paymentState.ToLower()}</b> pelas Finanças.</p>" + departmentalContextHtml;

        var subjectOverride = $"[{paymentState.ToUpper()}] Informação de departamento - Pedido {reqRef}";

        foreach (var approver in areaApprovers)
        {
            if (approver.UserId == evt.RequesterId)
            {
                // Requester who is ALSO a resolved eligible manager (TEST REQ-05/10/2026-526): one person, one action, ONE
                // e-mail and ONE in-app notification. The requester entry queued above is replaced by a copy that carries the
                // requester wording PLUS the departmental context; a second recipient would be dropped by the outbox dedup
                // (CorrelationId + RecipientEmail) and the departmental summary would be lost, as it was before.
                var idx = recipients.FindIndex(r => r.UserId == evt.RequesterId);
                if (idx >= 0)
                {
                    var config = await ResolveEventConfigAsync(evt);
                    var requesterBody = config?.EmailBody ?? $"O pagamento para o pedido <b>{reqRef}</b> foi {paymentState.ToLower()} pela equipa financeira.";
                    var requesterSubject = config?.EmailSubject ?? $"Pagamento {paymentState} — {reqRef}";
                    recipients[idx] = recipients[idx] with
                    {
                        EmailSubjectOverride = $"{requesterSubject} (com informação de departamento)",
                        EmailBodyOverride = $"<p>{requesterBody}</p>" + departmentalContextHtml
                    };
                    continue;
                }
                // Requester entry absent (inactive requester cannot happen here: the cascade also requires an active user) —
                // fall through and treat the person as a plain manager so the summary is never silently lost.
            }

            recipients.Add(new NotificationRecipient(approver.UserId, approver.Email, approver.FullName)
            {
                EmailSubjectOverride = subjectOverride,
                EmailBodyOverride = htmlOverride
            });
        }
    }

    private static string SourceLabel(RequestAmountSource source) => source switch
    {
        RequestAmountSource.RecordedApproved => "valor aprovado, moeda registada na aprova\u00e7\u00e3o",
        RequestAmountSource.DerivedApproved => "valor aprovado, moeda derivada de dados atuais consistentes (n\u00e3o registada na aprova\u00e7\u00e3o)",
        RequestAmountSource.EstimateFallback => "estimativa do pedido (fallback: sem valor aprovado utiliz\u00e1vel)",
        _ => "n/d"
    };

    /// <summary>
    /// Departmental month context shown to the area managers on PAYMENT_SCHEDULED / PAYMENT_COMPLETED (request-level metric,
    /// docs/APPROVAL_NOTIFICATIONS_PHASES.md §G.18). ONE resolver — <see cref="DepartmentalAmountResolver"/> — governs the
    /// current request (numerator) and every denominator candidate: recorded approved total, else approved total with a
    /// currency derived from current consistent data (legacy batch approvals), else the estimate under the estimate
    /// comparability rules, else unresolved. Candidates: other requests of the same department whose request-level status
    /// is PAYMENT_SCHEDULED / PAID / PAYMENT_COMPLETED and whose UpdatedAtUtc is on/after the first day of the current UTC
    /// month; each counted once; included only when resolved in the current request's resolved currency (no conversion,
    /// no cross-currency summation). When the current request is unresolved, no aggregate or percentage is rendered.
    /// Payment action amounts (RequestPayments) are never part of this ratio.
    /// </summary>
    internal sealed record DepartmentalMonthContext(
        RequestAmountResolution Current, string? RequestCurrency, decimal? TotalAmount, decimal? Percentage, DateTime MonthStartUtc,
        int RecordedApprovedCount, int DerivedApprovedCount, int EstimateFallbackCount,
        int ExcludedOtherCurrencyCount, int ExcludedUnresolvedCount, string ExcludedUnresolvedReasons);

    private static readonly string[] DepartmentalScheduledOrPaidStatuses =
    {
        RequestConstants.Statuses.PaymentScheduled,
        RequestConstants.Statuses.Paid,
        RequestConstants.Statuses.PaymentCompleted
    };

    private async Task<DepartmentalMonthContext> ComputeDepartmentalMonthContextAsync(Request req, int departmentId)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var candidateIds = await _context.Requests
            .AsNoTracking()
            .Where(r => r.Id != req.Id
                     && r.DepartmentId == departmentId
                     && DepartmentalScheduledOrPaidStatuses.Contains(r.Status!.Code)
                     && r.UpdatedAtUtc >= monthStart)
            .Select(r => r.Id)
            .ToListAsync();

        var facts = await LoadRequestAmountFactsAsync(candidateIds.Append(req.Id).Distinct().ToList());
        var current = facts.TryGetValue(req.Id, out var cf) ? DepartmentalAmountResolver.Resolve(cf) : new RequestAmountResolution(RequestAmountSource.Unresolved, req.EstimatedTotalAmount, null, "pedido n\u00e3o encontrado");
        var requestCurrency = cf?.RequestCurrency;

        if (!current.IsResolved)
            return new DepartmentalMonthContext(current, requestCurrency, null, null, monthStart, 0, 0, 0, 0, 0, string.Empty);

        int recorded = 0, derived = 0, estimate = 0, otherCurrency = 0, unresolved = 0;
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        decimal others = 0m;
        foreach (var id in candidateIds.Distinct())
        {
            if (!facts.TryGetValue(id, out var f)) continue;
            var r = DepartmentalAmountResolver.Resolve(f);
            if (!r.IsResolved) { unresolved++; if (r.Reason != null) reasons.Add(r.Reason); continue; }
            if (!string.Equals(r.Currency, current.Currency, StringComparison.Ordinal)) { otherCurrency++; continue; }
            others += r.Amount;
            switch (r.Source)
            {
                case RequestAmountSource.RecordedApproved: recorded++; break;
                case RequestAmountSource.DerivedApproved: derived++; break;
                default: estimate++; break;
            }
        }
        switch (current.Source)
        {
            case RequestAmountSource.RecordedApproved: recorded++; break;
            case RequestAmountSource.DerivedApproved: derived++; break;
            default: estimate++; break;
        }

        var total = others + current.Amount;
        decimal? percentage = total > 0 && current.Amount > 0 ? Math.Round(current.Amount / total * 100m, 1) : null;
        return new DepartmentalMonthContext(current, requestCurrency, total, percentage, monthStart,
            recorded, derived, estimate, otherCurrency, unresolved, string.Join("; ", reasons));
    }

    /// <summary>Loads, in four set-based queries, the facts the resolver needs for a set of requests.</summary>
    private async Task<Dictionary<Guid, RequestAmountFacts>> LoadRequestAmountFactsAsync(List<Guid> ids)
    {
        var result = new Dictionary<Guid, RequestAmountFacts>();
        if (ids.Count == 0) return result;

        var heads = await _context.Requests.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.EstimatedTotalAmount, RequestCurrency = r.Currency != null ? r.Currency.Code : null, r.ApprovedTotalAmount, r.ApprovedCurrencyCode })
            .ToListAsync();
        var groups = await _context.RequestPoGroups.AsNoTracking()
            .Where(g => ids.Contains(g.RequestId))
            .Select(g => new { g.RequestId, g.Status, g.TotalAmount, Cur = g.CurrencyCode != null ? g.CurrencyCode : (g.Currency != null ? g.Currency.Code : null) })
            .ToListAsync();
        var batches = await _context.ApprovalBatches.AsNoTracking()
            .Where(b => ids.Contains(b.RequestId))
            .Select(b => new { b.RequestId, b.Status, b.ApprovedTotalAmount })
            .ToListAsync();
        // Phase 1 P.O. cancellation (D5): released items of APPROVED batches are no longer live value.
        var releasedByRequest = await _context.ApprovalBatchItems.AsNoTracking()
            .Where(bi => ids.Contains(bi.ApprovalBatch.RequestId) && bi.ReleasedAtUtc != null)
            .GroupBy(bi => bi.ApprovalBatch.RequestId)
            .Select(g => new { RequestId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RequestId, x => x.Count);
        var histories = await _context.RequestStatusHistories.AsNoTracking()
            .Where(h => ids.Contains(h.RequestId) && h.ActionTaken == "BATCH_FINAL_APPROVED")
            .GroupBy(h => h.RequestId)
            .Select(g => new { RequestId = g.Key, Count = g.Count() })
            .ToListAsync();

        var groupsBy = groups.ToLookup(g => g.RequestId);
        var batchesBy = batches.ToLookup(b => b.RequestId);
        var histBy = histories.ToDictionary(h => h.RequestId, h => h.Count);
        foreach (var h in heads)
        {
            var live = groupsBy[h.Id].Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled).ToList();
            var bs = batchesBy[h.Id].ToList();
            result[h.Id] = new RequestAmountFacts(
                h.Id, h.EstimatedTotalAmount, h.RequestCurrency, h.ApprovedTotalAmount, h.ApprovedCurrencyCode,
                live.Select(g => g.Cur).ToList(), live.Sum(g => g.TotalAmount),
                live.Count(g => g.Status == RequestConstants.PoGroupStatuses.Pending),
                groupsBy[h.Id].Count(g => g.Status == RequestConstants.PoGroupStatuses.Cancelled),
                bs.Count, bs.Count(b => b.Status == RequestConstants.ApprovalBatchStatuses.Approved),
                bs.Where(b => b.Status == RequestConstants.ApprovalBatchStatuses.Approved).Sum(b => b.ApprovedTotalAmount ?? 0m),
                histBy.TryGetValue(h.Id, out var c) ? c : 0,
                releasedByRequest.TryGetValue(h.Id, out var rel) ? rel : 0);
        }
        return result;
    }

    // =====================================================================
    //  Accounts Payable External Notification
    // =====================================================================

    /// <summary>
    /// Sends an Accounts Payable notification email to the configured external mailbox
    /// for the request's company. Non-blocking — failures are logged but never stall
    /// the payment workflow.
    /// </summary>
    private async Task SendAccountsPayableNotificationAsync(WorkflowEvent evt)
    {
        if (!evt.CompanyId.HasValue)
        {
            _logger.LogDebug("AP notification skipped: no CompanyId on WorkflowEvent for Request {RequestId}", evt.RequestId);
            return;
        }

        var config = await _context.AccountsPayableNotificationConfigs
            .AsNoTracking()
            .Include(c => c.Company)
            .FirstOrDefaultAsync(c => c.CompanyId == evt.CompanyId.Value && c.IsActive);

        if (config == null)
        {
            _logger.LogInformation("No active Accounts Payable notification configuration found for CompanyId {CompanyId}. Payment workflow continues normally.", evt.CompanyId.Value);
            return;
        }

        // Per-event toggles
        bool shouldNotify = evt.EventCode switch
        {
            WorkflowEventCodes.PaymentScheduled => config.NotifyOnScheduled,
            WorkflowEventCodes.PaymentCompleted => config.NotifyOnCompleted,
            WorkflowEventCodes.PoRegistered => config.NotifyOnPoRegistered,
            _ => false
        };

        if (!shouldNotify)
        {
            _logger.LogDebug("AP notification disabled for event {EventCode} on CompanyId {CompanyId}", evt.EventCode, evt.CompanyId.Value);
            return;
        }

        // Dedup granularity (v2.247.4):
        //  - P.O. registered: per ACTION — the REGISTER_PO/REREGISTER_PO history row Id (evt.CorrelationId).
        //  - Payment scheduled/completed WITH group context (FinanceController.SchedulePayment / MarkAsPaid set PoGroupId):
        //    per ACTION as well — the PAYMENT_SCHEDULED / PAYMENT_COMPLETED history row Id. A request may hold several P.O.
        //    groups and a group may be legitimately rescheduled after a cancellation; each such action persists its own
        //    history row before the event is emitted, so distinct actions get distinct notices while a re-emission of the
        //    SAME persisted action is still skipped. PaymentId is NOT the identity: the advance cancel path resets the same
        //    RequestPayment row to PLANNED and reschedules it, so one row can carry several actions.
        //  - Payment events WITHOUT group context (legacy request-level operational transitions): per
        //    (Request, Event, Recipient) with CorrelationId NULL — unchanged fallback.
        //  The filtered unique index IX_ApNotifLogs_Dedup (RequestId, EventCode, RecipientEmail, CorrelationId) enforces the
        //  same rule in SQL Server: distinct correlations are accepted, a duplicate success for one action is refused, and
        //  NULL correlations (legacy rows, which are never rewritten) stay unique per request/event/recipient.
        var isGroupedPaymentEvent = (evt.EventCode == WorkflowEventCodes.PaymentScheduled || evt.EventCode == WorkflowEventCodes.PaymentCompleted)
                                    && evt.PoGroupId.HasValue;
        var isPerAction = evt.EventCode == WorkflowEventCodes.PoRegistered || isGroupedPaymentEvent;
        Guid? dedupCorrelation = isPerAction ? evt.CorrelationId : null;
        var alreadySent = await _context.AccountsPayableNotificationLogs
            .AnyAsync(l => l.RequestId == evt.RequestId
                        && l.EventCode == evt.EventCode
                        && l.RecipientEmail == config.Email
                        && (!isPerAction || l.CorrelationId == dedupCorrelation)
                        && l.Success && !l.Skipped);

        if (alreadySent)
        {
            _context.AccountsPayableNotificationLogs.Add(new Domain.Entities.AccountsPayableNotificationLog
            {
                RequestId = evt.RequestId,
                CompanyId = evt.CompanyId.Value,
                EventCode = evt.EventCode,
                RecipientEmail = config.Email,
                CcEmails = config.CcEmails,
                Subject = "(duplicate skipped)",
                SentAtUtc = DateTime.UtcNow,
                Success = false,
                Skipped = true,
                CorrelationId = dedupCorrelation
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("AP notification skipped (duplicate) for {EventCode} on Request {RequestId} to {ApEmail}", evt.EventCode, evt.RequestId, config.Email);
            return;
        }

        // Resolve request details for the email body
        var req = await _context.Requests
            .AsNoTracking()
            .Include(r => r.Supplier)
            .FirstOrDefaultAsync(r => r.Id == evt.RequestId);

        if (req == null)
        {
            _logger.LogWarning("AP notification skipped: Request {RequestId} not found", evt.RequestId);
            return;
        }

        var companyName = config.Company?.Name ?? "Empresa";
        var supplierName = req.Supplier?.Name ?? "\u2014";
        var currency = req.CurrencyId.HasValue
            ? await _context.Currencies.AsNoTracking().Where(c => c.Id == req.CurrencyId).Select(c => c.Code).FirstOrDefaultAsync() ?? "AOA"
            : "AOA";
        var amount = req.ActualPaidAmount ?? req.EstimatedTotalAmount;
        var reqRef = evt.RequestNumber;

        string subject, headline, bodyHtml;
        if (evt.EventCode != WorkflowEventCodes.PoRegistered)
        {
            // PAYMENT_SCHEDULED / PAYMENT_COMPLETED (v2.247.2): both Finance actions operate on ONE P.O. group and ONE
            // RequestPayment row (FinanceController.SchedulePayment / MarkAsPaid). Supplier comes from that group, the
            // amount/currency/date from that payment row: planned amount + scheduled date when scheduling, actual paid
            // amount + paid date when completing. Never another group, never the request aggregate, never a mix of
            // currencies. Without a group/payment reference (legacy request-level transition in RequestsController
            // operational/schedule-payment|complete-payment) the request header is used and the row is labelled as such.
            var unit = await ResolveApPaymentUnitAsync(evt, req, supplierName, currency, amount);
            var isScheduled = evt.EventCode == WorkflowEventCodes.PaymentScheduled;
            subject = isScheduled
                ? $"[Portal Gerencial] Pagamento agendado \u2014 Pedido {reqRef}"
                : $"[Portal Gerencial] Pagamento realizado \u2014 Pedido {reqRef}";
            headline = isScheduled
                ? $"Pagamento Agendado \u2014 {companyName}"
                : $"Pagamento Realizado \u2014 {companyName}";
            bodyHtml = BuildApPaymentBody(reqRef, req.Title ?? "", unit, isScheduled, evt.ActorName, companyName);
        }
        else
        {
            // Content comes from the REGISTERED P.O. GROUP (evt.PoGroupId): a request can carry several groups with
            // different suppliers, totals and currencies, and QUOTATION requests have no header supplier and a zero
            // header estimate (TEST v2.247.0 showed "\u2014" / "0.00 AOA"). Never the header, never another group.
            if (evt.PoGroupId.HasValue)
            {
                var group = await _context.RequestPoGroups
                    .AsNoTracking()
                    .Include(g => g.Supplier)
                    .Where(g => g.Id == evt.PoGroupId.Value && g.RequestId == evt.RequestId)
                    .Select(g => new { SupplierName = g.Supplier != null ? g.Supplier.Name : g.SupplierNameSnapshot, g.TotalAmount, g.CurrencyCode })
                    .FirstOrDefaultAsync();
                if (group != null)
                {
                    supplierName = string.IsNullOrWhiteSpace(group.SupplierName) ? "\u2014" : group.SupplierName;
                    amount = group.TotalAmount;
                    currency = string.IsNullOrWhiteSpace(group.CurrencyCode) ? currency : group.CurrencyCode;
                }
                else
                {
                    _logger.LogWarning("AP notification for {EventCode} on Request {RequestId}: P.O. group {PoGroupId} not found; falling back to request header values.", evt.EventCode, evt.RequestId, evt.PoGroupId);
                }
            }
            else
            {
                _logger.LogWarning("AP notification for {EventCode} on Request {RequestId} carries no PoGroupId; falling back to request header values.", evt.EventCode, evt.RequestId);
            }

            // "P.O. registered; review required" \u2014 explicitly NOT "payment authorized / ready": a registered P.O. does not
            // establish that post-paid receipt/invoice requirements are satisfied.
            var isCorrection = string.Equals(evt.ActionTaken, "REREGISTER_PO", StringComparison.OrdinalIgnoreCase);
            var registrationLabel = isCorrection ? "P.O. corrigida e re-registada" : "P.O. registada";
            subject = $"[Portal Gerencial] {registrationLabel} \u2014 revis\u00e3o de Contas a Pagar \u2014 Pedido {reqRef}";
            headline = $"{registrationLabel} \u2014 Revis\u00e3o Necess\u00e1ria \u2014 {companyName}";
            bodyHtml = BuildApPoRegisteredBody(reqRef, req.Title ?? "", supplierName, amount, currency, registrationLabel, evt.ActorName, companyName);
        }

        var frontendBaseUrl = _config["AppConfig:FrontendBaseUrl"]?.TrimEnd('/') ?? "";
        var actionUrl = $"{frontendBaseUrl}/finance?requestId={evt.RequestId}";

        try
        {
            var sent = await _emailService.SendWorkflowNotificationAsync(
                config.Email, companyName, subject, headline, bodyHtml,
                actionUrl, "Ver Pedido", config.CcEmails);

            _context.AccountsPayableNotificationLogs.Add(new Domain.Entities.AccountsPayableNotificationLog
            {
                RequestId = evt.RequestId,
                CompanyId = evt.CompanyId.Value,
                EventCode = evt.EventCode,
                RecipientEmail = config.Email,
                CcEmails = config.CcEmails,
                Subject = subject,
                SentAtUtc = DateTime.UtcNow,
                Success = sent,
                ErrorMessage = sent ? null : "SendWorkflowNotificationAsync returned false",
                CorrelationId = dedupCorrelation
            });
            await _context.SaveChangesAsync();

            if (sent)
                _logger.LogInformation("AP notification sent for {EventCode} on Request {RequestId} to {ApEmail}", evt.EventCode, evt.RequestId, config.Email);
            else
                _logger.LogWarning("AP notification SMTP returned false for {EventCode} on Request {RequestId} to {ApEmail}", evt.EventCode, evt.RequestId, config.Email);
        }
        catch (Exception ex)
        {
            // AP email failure must NEVER block the payment workflow
            _context.AccountsPayableNotificationLogs.Add(new Domain.Entities.AccountsPayableNotificationLog
            {
                RequestId = evt.RequestId,
                CompanyId = evt.CompanyId.Value,
                EventCode = evt.EventCode,
                RecipientEmail = config.Email,
                CcEmails = config.CcEmails,
                Subject = subject,
                SentAtUtc = DateTime.UtcNow,
                Success = false,
                ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message,
                CorrelationId = dedupCorrelation
            });
            try { await _context.SaveChangesAsync(); }
            catch (Exception logEx) { _logger.LogWarning(logEx, "Failed to persist AP notification failure log for Request {RequestId}", evt.RequestId); }

            _logger.LogWarning(ex, "Non-critical: AP notification email failed for {EventCode} on Request {RequestId} to {ApEmail}", evt.EventCode, evt.RequestId, config.Email);
        }
    }

    /// <summary>The affected unit of a scheduling/completion action as rendered to Accounts Payable.</summary>
    internal sealed record ApPaymentUnit(string SupplierName, decimal Amount, string Currency, DateTime? ScheduledDateUtc, DateTime? PaidDateUtc, bool FromGroup, bool FromPaymentRow = false);

    /// <summary>
    /// Resolves supplier/amount/currency/dates for a PAYMENT_SCHEDULED or PAYMENT_COMPLETED notice from the event's group
    /// and payment row, scoped to the event's request. Supplier: group.Supplier.Name → group.SupplierNameSnapshot →
    /// selected quotation's supplier (QUOTATION) → request supplier (legacy PAYMENT groups never synced a snapshot — same
    /// fall-through as FinanceGroupDisplayResolver) → "—". Currency: payment row → group → selected quotation → request.
    /// Amount: payment row (planned when scheduling, actual paid when completing) → group total. A missing group reference
    /// falls back to the request header values the caller resolved and is logged; FromGroup=false labels that in the body.
    /// </summary>
    private async Task<ApPaymentUnit> ResolveApPaymentUnitAsync(WorkflowEvent evt, Request req, string headerSupplier, string headerCurrency, decimal headerAmount)
    {
        var isCompleted = evt.EventCode == WorkflowEventCodes.PaymentCompleted;
        if (!evt.PoGroupId.HasValue)
        {
            _logger.LogWarning("AP notification for {EventCode} on Request {RequestId} carries no PoGroupId; falling back to request header values.", evt.EventCode, evt.RequestId);
            return new ApPaymentUnit(headerSupplier, headerAmount, headerCurrency, null, isCompleted ? req.ActualPaidAtUtc : null, false);
        }

        var group = await _context.RequestPoGroups
            .AsNoTracking()
            .Where(g => g.Id == evt.PoGroupId.Value && g.RequestId == evt.RequestId)
            .Select(g => new { SupplierName = g.Supplier != null ? g.Supplier.Name : null, g.SupplierNameSnapshot, g.TotalAmount, g.CurrencyCode })
            .FirstOrDefaultAsync();
        if (group == null)
        {
            _logger.LogWarning("AP notification for {EventCode} on Request {RequestId}: P.O. group {PoGroupId} not found; falling back to request header values.", evt.EventCode, evt.RequestId, evt.PoGroupId);
            return new ApPaymentUnit(headerSupplier, headerAmount, headerCurrency, null, isCompleted ? req.ActualPaidAtUtc : null, false);
        }

        var selectedQuotation = req.SelectedQuotationId.HasValue
            ? await _context.Quotations.AsNoTracking().Where(q => q.Id == req.SelectedQuotationId.Value).Select(q => new { q.SupplierNameSnapshot, q.Currency }).FirstOrDefaultAsync()
            : null;
        var supplier = !string.IsNullOrWhiteSpace(group.SupplierName)
            ? group.SupplierName!
            : FinanceGroupDisplayResolver.ResolveSupplierName(group.SupplierNameSnapshot, selectedQuotation != null, selectedQuotation?.SupplierNameSnapshot, req.Supplier?.Name);
        if (supplier == "---") supplier = "\u2014";
        var currency = FinanceGroupDisplayResolver.ResolveCurrencyCode(group.CurrencyCode, selectedQuotation != null, selectedQuotation?.Currency, headerCurrency);
        var amount = group.TotalAmount;
        DateTime? scheduledAt = null, paidAt = null;
        var fromPaymentRow = false;

        if (evt.PaymentId.HasValue)
        {
            var payment = await _context.RequestPayments
                .AsNoTracking()
                .Where(p => p.Id == evt.PaymentId.Value && p.RequestId == evt.RequestId && p.RequestPoGroupId == evt.PoGroupId.Value)
                .Select(p => new { p.PlannedAmount, p.ActualPaidAmount, p.CurrencyCode, p.ScheduledDateUtc, p.PaidDateUtc })
                .FirstOrDefaultAsync();
            if (payment != null)
            {
                amount = isCompleted ? (payment.ActualPaidAmount ?? payment.PlannedAmount) : payment.PlannedAmount;
                if (!string.IsNullOrWhiteSpace(payment.CurrencyCode) && payment.CurrencyCode != "---") currency = payment.CurrencyCode;
                scheduledAt = payment.ScheduledDateUtc;
                paidAt = payment.PaidDateUtc;
                fromPaymentRow = true;
            }
            else
            {
                _logger.LogWarning("AP notification for {EventCode} on Request {RequestId}: payment row {PaymentId} not found for group {PoGroupId}; using the group total.", evt.EventCode, evt.RequestId, evt.PaymentId, evt.PoGroupId);
            }
        }
        else
        {
            _logger.LogWarning("AP notification for {EventCode} on Request {RequestId} carries no PaymentId; using the group total of {PoGroupId}.", evt.EventCode, evt.RequestId, evt.PoGroupId);
        }

        return new ApPaymentUnit(supplier, amount, currency, scheduledAt, paidAt, true, fromPaymentRow);
    }

    /// <summary>
    /// Accounts Payable body for a SCHEDULED or COMPLETED payment. Distinct wording per event: scheduling announces an
    /// entry in the Accounts Payable list with the planned amount and scheduled date; completion announces a settled
    /// payment with the actual paid amount and payment date. Amount and supplier are those of the acted-on P.O. group /
    /// payment row ("Montante agendado (grupo P.O.)" / "Montante pago (grupo P.O.)"); a header fallback is labelled "(pedido)".
    /// </summary>
    private static string BuildApPaymentBody(string reqRef, string requestTitle, ApPaymentUnit unit, bool isScheduled, string actorName, string companyName)
    {
        Func<string?, string> enc = System.Net.WebUtility.HtmlEncode;
        var dateTime = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm") + " (UTC)";
        var scope = unit.FromGroup ? "grupo P.O." : "pedido";
        var intro = isScheduled
            ? "Um pagamento foi <b>agendado</b> pelas Finan&#231;as e entrou na lista de Contas a Pagar."
            : "Um pagamento foi confirmado como <b>realizado</b> pelas Finan&#231;as.";
        var amountLabel = isScheduled ? $"Montante agendado ({scope})" : $"Montante pago ({scope})";
        var dateRow = isScheduled
            ? (unit.ScheduledDateUtc.HasValue ? $"<tr><td style='padding:6px 0;'><b>Data agendada:</b></td><td style='padding:6px 0;'>{unit.ScheduledDateUtc.Value:dd/MM/yyyy}</td></tr>" : "")
            : (unit.PaidDateUtc.HasValue ? $"<tr><td style='padding:6px 0;'><b>Data do pagamento:</b></td><td style='padding:6px 0;'>{unit.PaidDateUtc.Value:dd/MM/yyyy}</td></tr>" : "");
        var statusLabel = isScheduled ? "Pagamento Agendado" : "Pagamento Realizado";

        return $@"
<p>{intro}</p>
<div style='background-color:#f8f9fa; border:1px solid #dee2e6; padding:15px; border-radius:6px; margin:20px 0;'>
    <table style='width:100%; font-size:14px; color:#333; border-collapse:collapse;'>
        <tr><td style='padding:6px 0;'><b>Empresa:</b></td><td style='padding:6px 0;'>{enc(companyName)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Pedido:</b></td><td style='padding:6px 0;'>{enc(reqRef)}</td></tr>
        <tr><td style='padding:6px 0;'><b>T&#237;tulo:</b></td><td style='padding:6px 0;'>{enc(requestTitle)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Fornecedor:</b></td><td style='padding:6px 0;'>{enc(unit.SupplierName)}</td></tr>
        <tr><td style='padding:6px 0;'><b>{amountLabel}:</b></td><td style='padding:6px 0;'>{unit.Amount:N2} {enc(unit.Currency)}</td></tr>
        {dateRow}
        <tr><td style='padding:6px 0;'><b>Status atual:</b></td><td style='padding:6px 0;'>{statusLabel}</td></tr>
        <tr><td style='padding:6px 0;'><b>A&#231;&#227;o realizada por:</b></td><td style='padding:6px 0;'>{enc(actorName)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Data/hora:</b></td><td style='padding:6px 0;'>{dateTime}</td></tr>
    </table>
</div>
<p style='font-size:13px; color:#666;'>Acesse o Portal Gerencial para consultar os detalhes do pedido.</p>
<p style='font-size:12px; color:#999;'>Esta &#233; uma mensagem autom&#225;tica do Portal Gerencial.</p>";
    }

    /// <summary>
    /// Accounts Payable body for a registered / re-registered P.O.: a REVIEW notice. It states explicitly that the
    /// registration does not authorize or schedule a payment and that the post-paid receipt/invoice requirements
    /// still apply — deliberately different from the "pedido de pagamento" wording of scheduling/completion.
    /// </summary>
    private static string BuildApPoRegisteredBody(
        string reqRef, string requestTitle, string supplierName,
        decimal amount, string currency, string registrationLabel,
        string actorName, string companyName)
    {
        Func<string?, string> enc = System.Net.WebUtility.HtmlEncode;
        var dateTime = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm") + " (UTC)";

        return $@"
<p>Uma Purchase Order foi {(registrationLabel.StartsWith("P.O. corrigida", StringComparison.OrdinalIgnoreCase) ? "corrigida e re-registada" : "registada")} no Portal Gerencial e aguarda a revis&#227;o de Contas a Pagar.</p>
<div style='background-color:#f8f9fa; border:1px solid #dee2e6; padding:15px; border-radius:6px; margin:20px 0;'>
    <table style='width:100%; font-size:14px; color:#333; border-collapse:collapse;'>
        <tr><td style='padding:6px 0;'><b>Empresa:</b></td><td style='padding:6px 0;'>{enc(companyName)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Pedido:</b></td><td style='padding:6px 0;'>{enc(reqRef)}</td></tr>
        <tr><td style='padding:6px 0;'><b>T&#237;tulo:</b></td><td style='padding:6px 0;'>{enc(requestTitle)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Fornecedor:</b></td><td style='padding:6px 0;'>{enc(supplierName)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Total do grupo P.O.:</b></td><td style='padding:6px 0;'>{amount:N2} {enc(currency)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Situa&#231;&#227;o:</b></td><td style='padding:6px 0;'>{enc(registrationLabel)} &#8212; revis&#227;o necess&#225;ria</td></tr>
        <tr><td style='padding:6px 0;'><b>A&#231;&#227;o realizada por:</b></td><td style='padding:6px 0;'>{enc(actorName)}</td></tr>
        <tr><td style='padding:6px 0;'><b>Data/hora:</b></td><td style='padding:6px 0;'>{dateTime}</td></tr>
    </table>
</div>
<p style='font-size:13px; color:#333;'><b>Este aviso n&#227;o significa que o pagamento est&#225; autorizado ou pronto.</b> O registo da P.O. n&#227;o confirma a rece&#231;&#227;o dos bens/servi&#231;os nem a fatura; para pagamentos p&#243;s-pagos essas condi&#231;&#245;es continuam a aplicar-se antes do agendamento.</p>
<p style='font-size:13px; color:#666;'>Acesse o Portal Gerencial para consultar os detalhes do pedido.</p>
<p style='font-size:12px; color:#999;'>Esta &#233; uma mensagem autom&#225;tica do Portal Gerencial.</p>";
    }

    /// <summary>
    /// docs §G.20 D1 — the Buyer responsible for a P.O. returned by Finance, by the ownership rule shared with the
    /// Buyer queue, "Para Minha Ação" and the dashboard count (PersonalPoCorrectionPredicate):
    /// QUOTATION → <c>Request.BuyerId</c> (as carried on the event); PAYMENT → the acted-on group's
    /// <c>PoResponsibleBuyerId</c>. Precedence is by request type only: a PAYMENT never falls back to
    /// <c>Request.BuyerId</c> (a historical request-level assignment is not correction ownership) and a QUOTATION never
    /// reads the group owner. The group context must belong to the event's request — a group of another request, a
    /// missing context or an unassigned group resolve to nobody (logged), never to a sibling or to every Buyer.
    /// </summary>
    private async Task<Guid?> ResolveFinanceReturnOwnerAsync(WorkflowEvent evt)
    {
        var request = await _context.Requests.AsNoTracking()
            .Where(r => r.Id == evt.RequestId)
            .Select(r => new { TypeCode = r.RequestType != null ? r.RequestType.Code : null })
            .FirstOrDefaultAsync();
        if (request == null) return null;

        if (request.TypeCode != RequestConstants.Types.Payment)
            return evt.BuyerId;

        if (!evt.PoGroupId.HasValue)
        {
            _logger.LogWarning("FinanceReturned on PAYMENT Request {RequestId} carries no PoGroupId; no Buyer recipient resolved", evt.RequestId);
            return null;
        }
        var group = await _context.RequestPoGroups.AsNoTracking()
            .Where(g => g.Id == evt.PoGroupId.Value && g.RequestId == evt.RequestId)
            .Select(g => new { g.PoResponsibleBuyerId })
            .FirstOrDefaultAsync();
        if (group == null)
        {
            _logger.LogWarning("FinanceReturned: PoGroupId {PoGroupId} does not belong to Request {RequestId}; no Buyer recipient resolved", evt.PoGroupId, evt.RequestId);
            return null;
        }
        if (!group.PoResponsibleBuyerId.HasValue)
            _logger.LogWarning("FinanceReturned: returned group {PoGroupId} of Request {RequestId} has no responsible Buyer (unassigned correction); no Buyer recipient resolved", evt.PoGroupId, evt.RequestId);
        return group.PoResponsibleBuyerId;
    }

    private async Task AddUserRecipientAsync(List<NotificationRecipient> recipients, Guid? userId,
        string? emailSubjectOverride = null, string? emailBodyOverride = null, bool bypassSelfNotifyRule = false)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty) return;

        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId.Value && u.IsActive)
            .Select(u => new { u.Id, u.Email, u.FullName })
            .FirstOrDefaultAsync();

        if (user != null)
        {
            recipients.Add(new NotificationRecipient(user.Id, user.Email, user.FullName)
            {
                EmailSubjectOverride = emailSubjectOverride,
                EmailBodyOverride = emailBodyOverride,
                BypassSelfNotifyRule = bypassSelfNotifyRule
            });
        }
    }

    /// <summary>
    /// Adjustment V2 (Phase 4): resolves the Area manager(s) for the request's department + plant via
    /// the canonical DepartmentManager routing (same recipients as pending area approval). None
    /// resolvable → the caller's "no recipients" path logs and skips.
    /// </summary>
    private async Task AddAreaManagerRecipientsAsync(List<NotificationRecipient> recipients, WorkflowEvent evt)
    {
        var req = await _context.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == evt.RequestId);
        var deptId = evt.DepartmentId ?? req?.DepartmentId ?? 0;
        var plantId = evt.PlantId ?? req?.PlantId;
        if (deptId == 0) return;

        var routing = await _approvalRouting.ResolveAreaManagersAsync(deptId, plantId);
        if (!routing.HasManagers) return;

        foreach (var manager in routing.Managers)
            recipients.Add(new NotificationRecipient(manager.UserId, manager.Email, manager.FullName));
    }

    /// <summary>
    /// Resolves all active Finance users scoped to the request's plant.
    /// Falls back to all active Finance users if no plant is specified or no
    /// plant-scoped users exist (global in-app only — email suppressed for global fan-out).
    /// E-mail to individual Finance users additionally requires the request's company to have an ACTIVE
    /// Accounts Payable configuration with NotifyFinanceUsersByEmail = true; a missing company, a missing or
    /// inactive configuration, or the flag off keeps the in-app notification and suppresses the e-mail.
    /// </summary>
    /// <param name="emailRegardlessOfApSwitch">Phase 1 P.O. cancellation policy: plant-scoped Finance users ALWAYS get the e-mail
    /// (the company AP switch NotifyFinanceUsersByEmail governs other events only). Never widens the recipient set.</param>
    private async Task AddPlantScopedFinanceRecipientsAsync(List<NotificationRecipient> recipients, int? plantId, int? companyId, bool emailRegardlessOfApSwitch = false)
    {
        var financeEmailEnabled = companyId.HasValue && await _context.AccountsPayableNotificationConfigs
            .AsNoTracking()
            .AnyAsync(c => c.CompanyId == companyId.Value && c.IsActive && c.NotifyFinanceUsersByEmail);
        if (!financeEmailEnabled)
        {
            _logger.LogDebug("Finance individual e-mail suppressed (company {CompanyId}: no active AP configuration with NotifyFinanceUsersByEmail); in-app notifications continue.", companyId);
        }

        IQueryable<Guid> financeUserIds;

        if (plantId.HasValue)
        {
            // Plant-scoped: Finance users who have the specific plant in their scope
            financeUserIds = _context.UserRoleAssignments
                .Where(ura => ura.Role!.RoleName == RoleConstants.Finance)
                .Select(ura => ura.UserId)
                .Intersect(
                    _context.UserPlantScopes
                        .Where(ups => ups.PlantId == plantId.Value)
                        .Select(ups => ups.UserId)
                );
        }
        else
        {
            // No plant context — fallback to all Finance users
            financeUserIds = _context.UserRoleAssignments
                .Where(ura => ura.Role!.RoleName == RoleConstants.Finance)
                .Select(ura => ura.UserId);
        }

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => financeUserIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.Email, u.FullName })
            .ToListAsync();

        foreach (var user in users)
        {
            recipients.Add(new NotificationRecipient(user.Id, user.Email, user.FullName)
            {
                // Suppress e-mail when we fell back to global (no plant) OR the company did not opt in
                SuppressEmail = !plantId.HasValue || (!financeEmailEnabled && !emailRegardlessOfApSwitch)
            });
        }
    }

    /// <summary>
    /// Resolves all active Buyer users scoped to the request's plant.
    /// Falls back to all active Buyer users if no plant is specified
    /// (email suppressed for global fan-out).
    /// </summary>
    private async Task AddPlantScopedBuyerRecipientsAsync(
        List<NotificationRecipient> recipients, WorkflowEvent evt)
    {
        var buyerRole = await _context.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RoleName == RoleConstants.Buyer);
        if (buyerRole == null) return;

        IQueryable<Guid> buyerUserIds;

        if (evt.PlantId.HasValue)
        {
            // Plant-scoped: Buyer users who have the specific plant in their scope
            buyerUserIds = _context.UserRoleAssignments
                .Where(ura => ura.RoleId == buyerRole.Id)
                .Select(ura => ura.UserId)
                .Intersect(
                    _context.UserPlantScopes
                        .Where(ups => ups.PlantId == evt.PlantId.Value)
                        .Select(ups => ups.UserId)
                );
        }
        else
        {
            // No plant context — fallback to all Buyer users
            buyerUserIds = _context.UserRoleAssignments
                .Where(ura => ura.RoleId == buyerRole.Id)
                .Select(ura => ura.UserId);
        }

        // Load request context for rich email body
        var req = await _context.Requests.AsNoTracking()
            .Include(r => r.Requester)
            .Include(r => r.Plant)
            .Include(r => r.Department)
            .Include(r => r.Currency)
            .FirstOrDefaultAsync(r => r.Id == evt.RequestId);

        var reqRef = evt.RequestNumber;
        var requesterName = req?.Requester?.FullName ?? "Desconhecido";
        var plantName = req?.Plant?.Name ?? "---";
        var deptName = req?.Department?.Name ?? "---";
        var currencyCode = req?.Currency?.Code ?? "AOA";
        var amount = req?.EstimatedTotalAmount ?? 0m;
        var needByDate = req?.NeedByDateUtc?.ToString("dd/MM/yyyy") ?? "Não definida";

        var portalBaseUrl = _config["AppConfig:FrontendBaseUrl"] ?? "";
        var requestDetailUrl = !string.IsNullOrWhiteSpace(portalBaseUrl)
            ? $"{portalBaseUrl.TrimEnd('/')}/requests/{evt.RequestId}?mode=view"
            : "";
        var ctaButtonHtml = !string.IsNullOrWhiteSpace(requestDetailUrl)
            ? $@"<div style='text-align: left; margin: 24px 0;'>
                    <a href='{requestDetailUrl}' 
                       style='display:inline-block; background-color:#002D72; color:#ffffff; padding:12px 24px; text-decoration:none; border-radius:4px; font-weight:bold;'>
                       Abrir Pedido no Portal &rarr;
                    </a>
                 </div>"
            : "";

        var htmlBody = $@"
<p>Um novo pedido de cotação <b>{reqRef}</b> está aguardando processamento na sua fila.</p>
<div style='background-color:#f0f9ff; border:1px solid #bae6fd; padding:15px; border-radius:6px; margin:20px 0;'>
    <h3 style='color:#0369a1; margin-top:0;'>Dados do Pedido</h3>
    <ul style='color:#0c4a6e; font-size:14px; margin-bottom:0;'>
        <li style='margin-bottom: 5px'><b>Nº do Pedido:</b> {reqRef}</li>
        <li style='margin-bottom: 5px'><b>Solicitante:</b> {System.Net.WebUtility.HtmlEncode(requesterName)}</li>
        <li style='margin-bottom: 5px'><b>Planta:</b> {System.Net.WebUtility.HtmlEncode(plantName)}</li>
        <li style='margin-bottom: 5px'><b>Departamento:</b> {System.Net.WebUtility.HtmlEncode(deptName)}</li>
        <li style='margin-bottom: 5px'><b>Valor Estimado:</b> {amount:N2} {currencyCode}</li>
        <li><b>Data de Necessidade:</b> {needByDate}</li>
    </ul>
</div>
{ctaButtonHtml}
<p>Por favor, revise o pedido e assuma a responsabilidade para dar início ao processo de cotação.</p>";

        var subjectOverride = $"[AÇÃO NECESSÁRIA] Novo Pedido de Cotação aguardando — {reqRef}";

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => buyerUserIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.Email, u.FullName })
            .ToListAsync();

        foreach (var user in users)
        {
            recipients.Add(new NotificationRecipient(user.Id, user.Email, user.FullName)
            {
                EmailSubjectOverride = subjectOverride,
                EmailBodyOverride = htmlBody,
                SuppressEmail = !evt.PlantId.HasValue  // suppress email if global fallback
            });
        }
    }

    // =====================================================================
    // DISPATCH
    // =====================================================================

    private async Task DispatchToRecipientAsync(WorkflowEvent evt, EventConfig config, NotificationRecipient recipient)
    {
        var targetPath = $"/requests/{evt.RequestId}?mode=view";

        // --- In-App Notification ---
        try
        {
            var created = await _notificationService.CreateNotificationWithDedupAsync(
                recipient.UserId,
                config.InAppTitle,
                config.InAppMessage,
                config.NotificationType,
                targetPath,
                evt.CorrelationId,
                config.Category
            );

            if (!created)
            {
                _logger.LogDebug("Dedup: notification already exists for CorrelationId {CorrelationId} + User {UserId}", evt.CorrelationId, recipient.UserId);
            }
        }
        catch (Exception ex) when (!config.RequireInAppPersistence)
        {
            // Historical policy for every other event: an in-app failure must not block the business action.
            _logger.LogWarning(ex, "Failed to create in-app notification for User {UserId} (Event: {EventCode})", recipient.UserId, evt.EventCode);
        }
        catch (Exception ex)
        {
            // §8 transactional events (BUYER_TRANSFERRED): the in-app row is part of the notification intent that must
            // commit with ownership and audit — propagate so the caller's transaction rolls everything back.
            _logger.LogError(ex, "In-app notification persistence failed for User {UserId} (Event: {EventCode}); propagating (transactional event)", recipient.UserId, evt.EventCode);
            throw;
        }

        // --- Email Notification (Outbox Pattern — non-blocking) ---
        if (config.SendEmail && !recipient.SuppressEmail && !string.IsNullOrWhiteSpace(recipient.Email))
        {
            var portalBaseUrl = _config["AppConfig:FrontendBaseUrl"] ?? "";
            var actionUrl = !string.IsNullOrWhiteSpace(portalBaseUrl) ? $"{portalBaseUrl.TrimEnd('/')}{targetPath}" : null;

            // Dedup at insert time: prevent duplicate outbox entries for the same event + recipient.
            var alreadyQueued = await _context.EmailOutbox.AnyAsync(
                e => e.CorrelationId == evt.CorrelationId
                  && e.RecipientEmail == recipient.Email
                  && e.Status != "DEAD_LETTER");

            if (alreadyQueued)
            {
                _logger.LogDebug(
                    "Outbox dedup: email already queued for {RecipientEmail} (Event: {EventCode}, CorrelationId: {CorrelationId}). Skipping insert.",
                    recipient.Email, evt.EventCode, evt.CorrelationId);
                return;
            }

            // Queue the email for async delivery instead of sending synchronously.
            // The EmailOutboxProcessor background service will pick this up and send it.
            _context.EmailOutbox.Add(new EmailOutboxEntry
            {
                RecipientEmail = recipient.Email,
                RecipientName = recipient.FullName,
                Subject = recipient.EmailSubjectOverride ?? config.EmailSubject,
                Headline = config.EmailHeadline,
                BodyHtml = recipient.EmailBodyOverride ?? config.EmailBody,
                ActionUrl = actionUrl,
                ActionLabel = "Ver Pedido",
                RequestId = evt.RequestId,
                RequestNumber = evt.RequestNumber,
                EventCode = evt.EventCode,
                CorrelationId = evt.CorrelationId,
                Status = "PENDING",
                CreatedAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Email queued to outbox for {RecipientEmail} (Event: {EventCode}, Request: {RequestNumber}, CorrelationId: {CorrelationId})",
                recipient.Email, evt.EventCode, evt.RequestNumber, evt.CorrelationId);

            await _adminLog.WriteAsync("Info", "WorkflowNotificationOrchestrator", "EMAIL_OUTBOX_QUEUED",
                $"E-mail de notificação enfileirado para {recipient.Email} ({recipient.FullName}). Pedido: {evt.RequestNumber}. Evento: {evt.EventCode}.",
                payload: $"RequestId: {evt.RequestId}. CorrelationId: {evt.CorrelationId}. Subject: {(recipient.EmailSubjectOverride ?? config.EmailSubject)}.");
        }
    }

    // =====================================================================
    // EVENT → MESSAGE MAPPING
    // =====================================================================

    private async Task<EventConfig?> ResolveEventConfigAsync(WorkflowEvent evt)
    {
        var reqRef = FormatRequestRef(evt);
        var reqContext = !string.IsNullOrWhiteSpace(evt.RequestTitle) ? $" (\"{evt.RequestTitle}\")" : "";
        var actorLabel = evt.ActorName;

        switch (evt.EventCode)
        {
            case WorkflowEventCodes.SubmissionConfirmed:
            {
                var itemsTable = await FormatLineItemsTableAsync(evt.RequestId);
                var req = await _context.Requests.Include(r => r.Currency).FirstOrDefaultAsync(r => r.Id == evt.RequestId);
                var currencyCode = req?.Currency?.Code ?? "AOA";
                var totalAmt = req?.EstimatedTotalAmount ?? 0m;

                var titleDisplay = !string.IsNullOrWhiteSpace(req?.Title) ? System.Net.WebUtility.HtmlEncode(req.Title) : "Sem título";
                var descDisplay = !string.IsNullOrWhiteSpace(req?.Description) ? System.Net.WebUtility.HtmlEncode(req.Description) : "Não informado";

                var bodyHtml = $"Recebemos o seu pedido <b>{reqRef}</b>{reqContext} com sucesso. O pedido foi enviado para a fila de aprovação.<br/><br/>" +
                               $"<div style='background-color:#f0f9ff; border:1px solid #bae6fd; padding:15px; border-radius:6px; margin:16px 0;'>" +
                               $"<h3 style='color:#0369a1; margin-top:0;'>Dados do Pedido</h3>" +
                               $"<p style='margin:6px 0;'><b>Título:</b> {titleDisplay}</p>" +
                               $"<p style='margin:6px 0;'><b>Descrição:</b> {descDisplay}</p>" +
                               $"<p style='margin:6px 0;'><b>Valor Total Estimado:</b> {totalAmt:N2} {currencyCode}</p>" +
                               $"</div>" +
                               $"<b>Resumo dos Itens:</b><br/>" + itemsTable;

                return new EventConfig
                {
                    Category = NotificationCategories.Approval,
                    NotificationType = NotificationTypes.Success,
                    InAppTitle = "Submissão Confirmada",
                    InAppMessage = $"O seu pedido {reqRef} foi submetido com sucesso.",
                    SendEmail = true,
                    EmailSubject = $"Confirmação de Submissão — {reqRef}",
                    EmailHeadline = "Submissão Confirmada",
                    EmailBody = bodyHtml
                };
            }

            case WorkflowEventCodes.RequestSubmitted: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Warning,
                InAppTitle = "Novo Pedido para Aprovação",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi submetido por {actorLabel} e aguarda a sua aprovação de área.",
                SendEmail = true,
                EmailSubject = $"Aprovação Solicitada — {reqRef}",
                EmailHeadline = "Aprovação de Área Solicitada",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} foi submetido por <b>{actorLabel}</b> e aguarda a sua decisão de aprovação."
            };

            case WorkflowEventCodes.AreaApproved: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Info,
                InAppTitle = "Aprovação da Área Concedida",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi aprovado na etapa de área por {actorLabel} e aguarda aprovação final.",
                SendEmail = true,
                EmailSubject = $"Aprovação Final Solicitada — {reqRef}",
                EmailHeadline = "Pedido Aguardando Aprovação Final",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} passou pela aprovação de área (por <b>{actorLabel}</b>) e aguarda a sua decisão de aprovação final."
            };

            case WorkflowEventCodes.FinalApproved: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Success,
                InAppTitle = "Pedido Aprovado",
                InAppMessage = $"O pedido {reqRef}{reqContext} recebeu aprovação final de {actorLabel}.",
                SendEmail = true,
                EmailSubject = $"Pedido Aprovado — {reqRef}",
                EmailHeadline = "Pedido Aprovado com Sucesso",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} foi aprovado por <b>{actorLabel}</b> e prossegue para a etapa operacional."
            };

            case WorkflowEventCodes.AreaRejected: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Error,
                InAppTitle = "Pedido Rejeitado — Aprovação de Área",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi rejeitado por {actorLabel} na aprovação de área.",
                SendEmail = true,
                EmailSubject = $"Pedido Rejeitado — {reqRef}",
                EmailHeadline = "Pedido Rejeitado na Aprovação de Área",
                EmailBody = BuildRejectionBody(reqRef, reqContext, actorLabel, "aprovação de área", evt.Comment)
            };

            case WorkflowEventCodes.FinalRejected: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Error,
                InAppTitle = "Pedido Rejeitado — Aprovação Final",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi rejeitado por {actorLabel} na aprovação final.",
                SendEmail = true,
                EmailSubject = $"Pedido Rejeitado — {reqRef}",
                EmailHeadline = "Pedido Rejeitado na Aprovação Final",
                EmailBody = BuildRejectionBody(reqRef, reqContext, actorLabel, "aprovação final", evt.Comment)
            };

            case WorkflowEventCodes.AreaAdjustment: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Warning,
                InAppTitle = "Reajuste Solicitado — Área",
                InAppMessage = $"O pedido {reqRef}{reqContext} requer ajustes solicitados por {actorLabel} na aprovação de área.",
                SendEmail = true,
                EmailSubject = $"Reajuste Solicitado — {reqRef}",
                EmailHeadline = "Reajuste de Pedido Solicitado",
                EmailBody = BuildAdjustmentBody(reqRef, reqContext, actorLabel, "aprovação de área", evt.Comment)
            };

            case WorkflowEventCodes.FinalAdjustment: return new EventConfig
            {
                Category = NotificationCategories.Approval,
                NotificationType = NotificationTypes.Warning,
                InAppTitle = "Reajuste Solicitado — Aprovação Final",
                InAppMessage = $"O pedido {reqRef}{reqContext} requer ajustes solicitados por {actorLabel} na aprovação final.",
                SendEmail = true,
                EmailSubject = $"Reajuste Solicitado — {reqRef}",
                EmailHeadline = "Reajuste de Pedido Solicitado",
                EmailBody = BuildAdjustmentBody(reqRef, reqContext, actorLabel, "aprovação final", evt.Comment)
            };

            // --- Adjustment V2 (Phase 3): structured, Buyer-facing action-required notification.
            //     Copy vocabulary is drawn from the shared AdjustmentEventLabels catalog so it stays
            //     identical to the future timeline / approver summary. ---
            case WorkflowEventCodes.BatchAreaAdjustment:
                return BuildBatchAdjustmentConfig(evt, reqRef, actorLabel, isFinal: false);

            case WorkflowEventCodes.BatchFinalAdjustment:
                return BuildBatchAdjustmentConfig(evt, reqRef, actorLabel, isFinal: true);

            // --- Adjustment V2 (Phase 4): Buyer resolved + resubmitted → Area approver action ---
            case WorkflowEventCodes.BatchResubmitted:
                return BuildBatchResubmittedConfig(evt, reqRef, actorLabel);

            case WorkflowEventCodes.PoRegistered: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Info,
                InAppTitle = "Nova P.O Registrada",
                InAppMessage = $"A P.O para o pedido {reqRef}{reqContext} foi registrada por {actorLabel}. Aguardando processamento financeiro.",
                SendEmail = true,
                EmailSubject = $"Nova P.O para Processamento — {reqRef}",
                EmailHeadline = "P.O Registrada — Processamento Financeiro",
                EmailBody = $"A Purchase Order para o pedido <b>{reqRef}</b>{reqContext} foi registrada por <b>{actorLabel}</b> e está pronta para processamento financeiro."
            };

            case WorkflowEventCodes.PaymentScheduled: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Info,
                InAppTitle = "Pagamento Agendado",
                InAppMessage = $"O pagamento referente ao pedido {reqRef}{reqContext} foi agendado.",
                SendEmail = true,
                EmailSubject = $"Pagamento Agendado — {reqRef}",
                EmailHeadline = "Pagamento Agendado",
                EmailBody = $"O pagamento para o pedido <b>{reqRef}</b>{reqContext} foi agendado pela equipa financeira."
            };

            case WorkflowEventCodes.PaymentCompleted: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Success,
                InAppTitle = "Pagamento Realizado",
                InAppMessage = $"O pagamento referente ao pedido {reqRef}{reqContext} foi realizado com sucesso.",
                SendEmail = true,
                EmailSubject = $"Pagamento Realizado — {reqRef}",
                EmailHeadline = "Pagamento Realizado com Sucesso",
                EmailBody = $"O pagamento para o pedido <b>{reqRef}</b>{reqContext} foi concluído pela equipa financeira."
            };

            case WorkflowEventCodes.FinanceReturned: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Warning,
                InAppTitle = evt.FinanceReturnKind == PoGroupCancellationConstants.FinanceReturnKinds.OrderCancellationRequested ? "Pedido Devolvido por Finanças — cancelamento solicitado" : "Pedido Devolvido por Finanças",
                InAppMessage = evt.FinanceReturnKind == PoGroupCancellationConstants.FinanceReturnKinds.OrderCancellationRequested
                    ? $"O pedido {reqRef}{reqContext} foi devolvido por {actorLabel}: Finanças indica que a encomenda foi cancelada e NÃO deve ser corrigida. Motivo: {evt.Comment}. Utilize \"Cancelar P.O.\" no grupo devolvido."
                    : $"O pedido {reqRef}{reqContext} foi devolvido por {actorLabel} para correção. Motivo: {evt.Comment}",
                SendEmail = true,
                EmailSubject = evt.FinanceReturnKind == PoGroupCancellationConstants.FinanceReturnKinds.OrderCancellationRequested ? $"Pedido Devolvido — cancelamento solicitado — {reqRef}" : $"Pedido Devolvido para Correção — {reqRef}",
                EmailHeadline = "Pedido Devolvido por Finanças",
                EmailBody = BuildFinanceReturnBody(reqRef, reqContext, actorLabel, evt.Comment)
            };

            case WorkflowEventCodes.PoCorrectionCompleted: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Info,
                InAppTitle = "P.O Corrigida e Re-registrada",
                InAppMessage = $"A P.O do pedido {reqRef}{reqContext} foi corrigida e re-registrada por {actorLabel} após devolução.",
                SendEmail = true,
                EmailSubject = $"P.O Corrigida — {reqRef}",
                EmailHeadline = "P.O Corrigida e Re-registrada",
                EmailBody = $"A Purchase Order do pedido <b>{reqRef}</b>{reqContext} foi corrigida e re-registrada por <b>{actorLabel}</b> após devolução por Finanças. O pedido retorna ao status de P.O emitida para processamento financeiro."
            };

            // ── Phase 1 P.O. group cancellation / return to quotation ──
            case WorkflowEventCodes.PoGroupCancelled:
            {
                var t = BuildPoCancellationTexts(evt, reqRef);
                return new EventConfig
                {
                    Category = NotificationCategories.Payment,
                    NotificationType = NotificationTypes.Warning,
                    RequireInAppPersistence = true,
                    InAppTitle = t.InAppTitle,
                    InAppMessage = t.InAppMessage,
                    SendEmail = true,
                    EmailSubject = t.ParticipantSubject,
                    EmailHeadline = t.InAppTitle,
                    EmailBody = t.ParticipantBody,
                };
            }
            case WorkflowEventCodes.PoCancellationConfirmed:
            {
                var when = AppTimeZone.FormatWithUtc(evt.OccurredAtUtc ?? DateTime.UtcNow);
                var ecf = System.Net.WebUtility.HtmlEncode(evt.EcfReference ?? "(sem referência)");
                var note = string.IsNullOrWhiteSpace(evt.Comment) ? "" : $"<br/><b>Nota de Finanças:</b> {System.Net.WebUtility.HtmlEncode(evt.Comment)}";
                return new EventConfig
                {
                    Category = NotificationCategories.Payment,
                    NotificationType = NotificationTypes.Success,
                    RequireInAppPersistence = true,
                    InAppTitle = "Anulação da ECF confirmada por Finanças",
                    InAppMessage = $"Finanças ({actorLabel}) confirmou a anulação da ECF {evt.EcfReference ?? ""} da P.O {evt.PreviousPoNumber ?? "—"} do pedido {reqRef} ({when}). O registo da nova P.O para os itens afetados está desbloqueado.".Replace("  ", " "),
                    SendEmail = true,
                    EmailSubject = $"Anulação da ECF confirmada — {reqRef}",
                    EmailHeadline = "Anulação da ECF confirmada por Finanças",
                    EmailBody = $"Finanças (<b>{actorLabel}</b>) confirmou em {when} a anulação da ECF <b>{ecf}</b> da P.O <b>{System.Net.WebUtility.HtmlEncode(evt.PreviousPoNumber ?? "—")}</b> cancelada no pedido <b>{reqRef}</b>{reqContext}. " +
                                "Esta confirmação é uma declaração de Finanças registada no Portal.{note}<br/><br/>O registo da nova P.O para os itens afetados está <b>desbloqueado</b>.",
                };
            }
            case WorkflowEventCodes.PoCancellationDeclarationRejected:
            {
                var when = AppTimeZone.FormatWithUtc(evt.OccurredAtUtc ?? DateTime.UtcNow);
                var ecf = System.Net.WebUtility.HtmlEncode(evt.EcfReference ?? "(sem referência)");
                var note = System.Net.WebUtility.HtmlEncode(evt.Comment ?? "");
                return new EventConfig
                {
                    Category = NotificationCategories.Payment,
                    NotificationType = NotificationTypes.Error,
                    RequireInAppPersistence = true,
                    InAppTitle = "Finanças não confirmou a anulação da ECF",
                    InAppMessage = $"Finanças ({actorLabel}) não confirmou a anulação da ECF {evt.EcfReference ?? ""} da P.O {evt.PreviousPoNumber ?? "—"} do pedido {reqRef} ({when}): \"{evt.Comment}\". A P.O anterior continua considerada ativa fora do Portal; atualize a declaração quando a anulação estiver concluída.".Replace("  ", " "),
                    SendEmail = true,
                    EmailSubject = $"Anulação da ECF NÃO confirmada — {reqRef}",
                    EmailHeadline = "Finanças não confirmou a anulação da ECF",
                    EmailBody = $"Finanças (<b>{actorLabel}</b>) não confirmou em {when} a anulação da ECF <b>{ecf}</b> da P.O <b>{System.Net.WebUtility.HtmlEncode(evt.PreviousPoNumber ?? "—")}</b> cancelada no pedido <b>{reqRef}</b>{reqContext}." +
                                $"<p style='padding:12px; background:#fdecea; border-left:4px solid #dc3545;'><b>Motivo indicado por Finanças:</b> {note}</p>" +
                                "A P.O anterior continua considerada ativa fora do Portal e o registo de uma P.O de substituição permanece bloqueado. Quando a anulação estiver concluída, atualize a sua declaração no grupo cancelado para nova revisão de Finanças.",
                };
            }

            case WorkflowEventCodes.RequestCancelled: return new EventConfig
            {
                Category = NotificationCategories.Info,
                NotificationType = NotificationTypes.Error,
                InAppTitle = "Pedido Cancelado",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi cancelado por {actorLabel}.",
                SendEmail = true,
                EmailSubject = $"Pedido Cancelado — {reqRef}",
                EmailHeadline = "Pedido Cancelado",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} foi cancelado por <b>{actorLabel}</b>. Nenhuma ação adicional é necessária."
            };

            case WorkflowEventCodes.RequestFinalized: return new EventConfig
            {
                Category = NotificationCategories.Receipt,
                NotificationType = NotificationTypes.Success,
                InAppTitle = "Pedido Finalizado",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi finalizado com sucesso. O recebimento está concluído.",
                SendEmail = true,
                EmailSubject = $"Pedido Finalizado — {reqRef}",
                EmailHeadline = "Pedido Finalizado com Sucesso",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} teve o processo de recebimento finalizado. O ciclo do pedido está completo."
            };

            case WorkflowEventCodes.AdvancePaymentRequired: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Warning,
                InAppTitle = "Adiantamento Necessário",
                InAppMessage = $"O pedido {reqRef}{reqContext} exige um pagamento de adiantamento.",
                SendEmail = true,
                EmailSubject = $"Adiantamento Necessário — {reqRef}",
                EmailHeadline = "Adiantamento Necessário",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} tem uma condição de pagamento que exige um adiantamento para prosseguir."
            };

            case WorkflowEventCodes.AdvancePaymentScheduled: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Info,
                InAppTitle = "Adiantamento Agendado",
                InAppMessage = $"O adiantamento referente ao pedido {reqRef}{reqContext} foi agendado.",
                SendEmail = true,
                EmailSubject = $"Adiantamento Agendado — {reqRef}",
                EmailHeadline = "Adiantamento Agendado",
                EmailBody = $"O pagamento de adiantamento para o pedido <b>{reqRef}</b>{reqContext} foi agendado pela equipa financeira."
            };

            case WorkflowEventCodes.AdvancePaymentCompleted: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Success,
                InAppTitle = "Adiantamento Realizado",
                InAppMessage = $"O adiantamento referente ao pedido {reqRef}{reqContext} foi realizado com sucesso.",
                SendEmail = true,
                EmailSubject = $"Adiantamento Realizado — {reqRef}",
                EmailHeadline = "Adiantamento Realizado com Sucesso",
                EmailBody = $"O pagamento de adiantamento para o pedido <b>{reqRef}</b>{reqContext} foi concluído pela equipa financeira."
            };

            case WorkflowEventCodes.ReconciliationCompleted: return new EventConfig
            {
                Category = NotificationCategories.Payment,
                NotificationType = NotificationTypes.Success,
                InAppTitle = "Reconciliação Concluída",
                InAppMessage = $"A reconciliação referente ao pedido {reqRef}{reqContext} foi realizada com sucesso.",
                SendEmail = true,
                EmailSubject = $"Reconciliação Concluída — {reqRef}",
                EmailHeadline = "Reconciliação Concluída",
                EmailBody = $"A reconciliação de adiantamento e faturamento para o pedido <b>{reqRef}</b>{reqContext} foi concluída."
            };

            case WorkflowEventCodes.QuotationCompleted: return new EventConfig
            {
                Category = NotificationCategories.Quotation,
                NotificationType = NotificationTypes.Success,
                InAppTitle = "Cotação Concluída",
                InAppMessage = $"A cotação para o pedido {reqRef}{reqContext} foi concluída e enviada para etapa de aprovação.",
                SendEmail = true,
                EmailSubject = $"Cotação Concluída — {reqRef}",
                EmailHeadline = "Cotação Concluída",
                EmailBody = $"A cotação para o pedido <b>{reqRef}</b>{reqContext} foi concluída por <b>{actorLabel}</b> e avança para a etapa de aprovação de área."
            };

            case WorkflowEventCodes.QuotationAwaitingBuyer: return new EventConfig
            {
                Category = NotificationCategories.Quotation,
                NotificationType = NotificationTypes.Warning,
                InAppTitle = "Novo Pedido de Cotação",
                InAppMessage = $"O pedido {reqRef}{reqContext} foi submetido por {actorLabel} e aguarda cotação.",
                SendEmail = true,
                EmailSubject = $"[AÇÃO NECESSÁRIA] Novo Pedido de Cotação aguardando — {reqRef}",
                EmailHeadline = "Novo Pedido de Cotação Aguardando",
                EmailBody = $"O pedido <b>{reqRef}</b>{reqContext} foi submetido por <b>{actorLabel}</b> e aguarda a atribuição de um comprador para dar início ao processo de cotação."
            };

            case WorkflowEventCodes.BuyerTransferred:
            {
                var req = await _context.Requests.AsNoTracking()
                    .Include(r => r.Requester).Include(r => r.Plant).Include(r => r.Department)
                    .FirstOrDefaultAsync(r => r.Id == evt.RequestId);
                var names = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == evt.BuyerId || u.Id == evt.PreviousBuyerId)
                    .Select(u => new { u.Id, u.FullName }).ToListAsync();
                string NameOf(Guid? id) => names.FirstOrDefault(n => n.Id == id)?.FullName ?? "—";
                var prevName = NameOf(evt.PreviousBuyerId);
                var newName = NameOf(evt.BuyerId);
                var when = AppTimeZone.FormatWithUtc(evt.OccurredAtUtc ?? DateTime.UtcNow);
                var details = BuildBuyerTransferDetailsHtml(reqRef, evt.RequestTitle, System.Net.WebUtility.HtmlEncode(prevName), System.Net.WebUtility.HtmlEncode(newName),
                    System.Net.WebUtility.HtmlEncode(actorLabel), when, System.Net.WebUtility.HtmlEncode(evt.Justification ?? ""));
                return new EventConfig
                {
                    Category = NotificationCategories.Quotation,
                    NotificationType = NotificationTypes.Info,
                    RequireInAppPersistence = true,
                    InAppTitle = "Responsabilidade do pedido transferida",
                    InAppMessage = $"O pedido {reqRef} passou do comprador {prevName} para {newName} (ação de {actorLabel}, {when}). Justificação: {evt.Justification}",
                    SendEmail = true,
                    EmailSubject = $"Responsabilidade do pedido transferida — {reqRef}",
                    EmailHeadline = "Comprador Responsável Transferido",
                    EmailBody = $"A responsabilidade pelo pedido <b>{reqRef}</b>{reqContext} passou do comprador <b>{System.Net.WebUtility.HtmlEncode(prevName)}</b> para <b>{System.Net.WebUtility.HtmlEncode(newName)}</b>.<br/><br/>{details}<br/>" +
                        $"<b>Solicitante:</b> {System.Net.WebUtility.HtmlEncode(req?.Requester?.FullName ?? "—")}<br/>" +
                        $"<b>Planta:</b> {System.Net.WebUtility.HtmlEncode(req?.Plant?.Name ?? "---")}<br/>" +
                        $"<b>Departamento:</b> {System.Net.WebUtility.HtmlEncode(req?.Department?.Name ?? "---")}"
                };
            }
            case WorkflowEventCodes.BuyerAssigned:
            {
                var req = await _context.Requests.AsNoTracking()
                    .Include(r => r.Requester)
                    .Include(r => r.Plant)
                    .Include(r => r.Department)
                    .Include(r => r.Currency)
                    .FirstOrDefaultAsync(r => r.Id == evt.RequestId);

                var requesterName = req?.Requester?.FullName ?? "Desconhecido";
                var plantName = req?.Plant?.Name ?? "---";
                var deptName = req?.Department?.Name ?? "---";
                var currencyCode = req?.Currency?.Code ?? "AOA";
                var amount = req?.EstimatedTotalAmount ?? 0m;
                var assignedBuyerName = evt.BuyerId.HasValue
                    ? (await _context.Users.AsNoTracking().Where(u => u.Id == evt.BuyerId.Value).Select(u => u.FullName).FirstOrDefaultAsync()) ?? actorLabel
                    : actorLabel;
                var selfClaim = !evt.BuyerId.HasValue || evt.BuyerId.Value == evt.ActorUserId;
                var scopeLabel = evt.PoGroupId.HasValue ? "a correção da P.O do pedido" : "o pedido de cotação";
                var assignedSentence = selfClaim
                    ? $"O comprador {actorLabel} assumiu {scopeLabel} {reqRef}."
                    : $"{char.ToUpperInvariant(scopeLabel[0])}{scopeLabel[1..]} {reqRef} foi atribuído(a) ao comprador {assignedBuyerName} por {actorLabel}.";

                return new EventConfig
                {
                    Category = NotificationCategories.Quotation,
                    NotificationType = NotificationTypes.Info,
                    InAppTitle = "Comprador Atribuído",
                    InAppMessage = assignedSentence,
                    SendEmail = true,
                    EmailSubject = $"Comprador atribuído ao pedido — {reqRef}",
                    EmailHeadline = "Comprador Atribuído ao Pedido",
                    EmailBody = $"{System.Net.WebUtility.HtmlEncode(assignedSentence)}{reqContext}<br/><br/>" +
                        $"<b>Solicitante:</b> {System.Net.WebUtility.HtmlEncode(requesterName)}<br/>" +
                        $"<b>Planta:</b> {System.Net.WebUtility.HtmlEncode(plantName)}<br/>" +
                        $"<b>Departamento:</b> {System.Net.WebUtility.HtmlEncode(deptName)}<br/>" +
                        $"<b>Valor Estimado:</b> {amount:N2} {currencyCode}<br/><br/>" +
                        $"O processo de cotação continuará a partir deste ponto."
                };
            }

            default: return null;
        }
    }

    // =====================================================================
    // EMAIL BODY HELPERS
    // =====================================================================

    private static string BuildRejectionBody(string reqRef, string reqContext, string actor, string stage, string? comment)
    {
        var commentHtml = !string.IsNullOrWhiteSpace(comment)
            ? $"<p style='margin-top:12px; padding:12px; background:#fff3f3; border-left:4px solid #e74c3c;'><b>Motivo:</b> {System.Net.WebUtility.HtmlEncode(comment)}</p>"
            : "";
        return $"O pedido <b>{reqRef}</b>{reqContext} foi <b>rejeitado</b> por <b>{actor}</b> na etapa de {stage}.{commentHtml}";
    }

    private static string BuildAdjustmentBody(string reqRef, string reqContext, string actor, string stage, string? comment)
    {
        var commentHtml = !string.IsNullOrWhiteSpace(comment)
            ? $"<p style='margin-top:12px; padding:12px; background:#fff8e1; border-left:4px solid #f39c12;'><b>Observação:</b> {System.Net.WebUtility.HtmlEncode(comment)}</p>"
            : "";
        return $"O pedido <b>{reqRef}</b>{reqContext} requer <b>reajustes</b> solicitados por <b>{actor}</b> na etapa de {stage}. Revise e submeta novamente.{commentHtml}";
    }

    private static string BuildFinanceReturnBody(string reqRef, string reqContext, string actor, string? comment)
    {
        var commentHtml = !string.IsNullOrWhiteSpace(comment)
            ? $"<p style='margin-top:12px; padding:12px; background:#fff8e1; border-left:4px solid #f39c12;'><b>Motivo:</b> {System.Net.WebUtility.HtmlEncode(comment)}</p>"
            : "";
        return $"O pedido <b>{reqRef}</b>{reqContext} foi devolvido pela equipa de Finanças (<b>{actor}</b>) para correção da P.O ou documentação.{commentHtml}";
    }

    /// <summary>
    /// Adjustment V2 (Phase 3) — Buyer-facing "Ação necessária" config for a structured adjustment
    /// cycle. Copy uses the shared <see cref="AdjustmentEventLabels"/> catalog (stage label + reason
    /// chips) so it matches the future timeline/approver wording exactly. Renders only friendly
    /// labels — never raw reason codes or GUIDs.
    /// </summary>
    private static EventConfig BuildBatchAdjustmentConfig(WorkflowEvent evt, string reqRef, string actorLabel, bool isFinal)
    {
        Func<string?, string> enc = System.Net.WebUtility.HtmlEncode;
        var stageLabel = isFinal ? AdjustmentEventLabels.RequestedAtFinal : AdjustmentEventLabels.RequestedAtArea;
        var lote = evt.BatchNumber.HasValue ? $"Lote #{evt.BatchNumber.Value}" : "o lote";

        var reasonsHtml = !string.IsNullOrWhiteSpace(evt.AdjustmentReasonLabels)
            ? $"<p style='margin:6px 0;'><b>Motivos:</b> {enc(evt.AdjustmentReasonLabels)}</p>"
            : "";
        var affectedHtml = !string.IsNullOrWhiteSpace(evt.AdjustmentAffectedItems)
            ? $"<p style='margin:6px 0;'><b>Itens afetados:</b> {enc(evt.AdjustmentAffectedItems)}</p>"
            : "";
        var commentHtml = !string.IsNullOrWhiteSpace(evt.Comment)
            ? $"<p style='margin-top:12px; padding:12px; background:#fff8e1; border-left:4px solid #f39c12;'><b>Comentário do aprovador:</b> {enc(evt.Comment)}</p>"
            : "";
        var returnNoteHtml = isFinal
            ? $"<p style='margin:12px 0 0; font-size:13px; color:#555;'>{enc(AdjustmentEventLabels.FinalReturnsViaAreaNote)}</p>"
            : "";

        var body = $@"
<p><b>{enc(stageLabel)}</b> — {lote} do pedido <b>{reqRef}</b> por <b>{enc(actorLabel)}</b>.</p>
<div style='background-color:#f8f9fa; border:1px solid #dee2e6; padding:15px; border-radius:6px; margin:16px 0;'>
    {reasonsHtml}
    {affectedHtml}
    {commentHtml}
</div>
<p>Reveja o lote e reenvie-o para aprovação.</p>
{returnNoteHtml}";

        return new EventConfig
        {
            Category = NotificationCategories.Approval,
            NotificationType = NotificationTypes.Warning,
            InAppTitle = stageLabel,
            InAppMessage = $"{lote} do pedido {reqRef} requer a sua revisão — {stageLabel.ToLowerInvariant()}.",
            SendEmail = true,
            EmailSubject = $"Ação necessária — {stageLabel} — {reqRef}",
            EmailHeadline = stageLabel,
            EmailBody = body
        };
    }

    /// <summary>
    /// Adjustment V2 (Phase 4) — Area-approver "Lote reenviado" config after the Buyer resolves an
    /// adjustment. Carries the Buyer's response and the structured reasons (friendly labels only);
    /// a Final-sourced cycle explains the return-via-Area. Next action: approve/review the lot.
    /// </summary>
    private static EventConfig BuildBatchResubmittedConfig(WorkflowEvent evt, string reqRef, string actorLabel)
    {
        Func<string?, string> enc = System.Net.WebUtility.HtmlEncode;
        var lote = evt.BatchNumber.HasValue ? $"Lote #{evt.BatchNumber.Value}" : "o lote";
        var isFinal = evt.AdjustmentSourceStage == AdjustmentConstants.SourceStages.Final;

        var reasonsHtml = !string.IsNullOrWhiteSpace(evt.AdjustmentReasonLabels)
            ? $"<p style='margin:6px 0;'><b>Motivos do reajuste:</b> {enc(evt.AdjustmentReasonLabels)}</p>" : "";
        var responseHtml = !string.IsNullOrWhiteSpace(evt.Comment)
            ? $"<p style='margin-top:12px; padding:12px; background:#eef7f0; border-left:4px solid #2e7d32;'><b>Resposta do Comprador:</b> {enc(evt.Comment)}</p>" : "";
        var originHtml = isFinal
            ? $"<p style='margin:6px 0; font-size:13px; color:#555;'>Reajuste originado na Aprovação Final — o lote retorna primeiro à Aprovação da Área.</p>" : "";

        var body = $@"
<p><b>{enc(AdjustmentEventLabels.ResubmittedToArea)}</b> — {lote} do pedido <b>{reqRef}</b> foi reenviado por <b>{enc(actorLabel)}</b> após a correção do reajuste.</p>
<div style='background-color:#f8f9fa; border:1px solid #dee2e6; padding:15px; border-radius:6px; margin:16px 0;'>
    {reasonsHtml}
    {responseHtml}
</div>
{originHtml}
<p>Reveja e aprove o lote na Aprovação da Área.</p>";

        return new EventConfig
        {
            Category = NotificationCategories.Approval,
            NotificationType = NotificationTypes.Info,
            InAppTitle = AdjustmentEventLabels.ResubmittedToArea,
            InAppMessage = $"{lote} do pedido {reqRef} foi reenviado após reajuste e aguarda a sua aprovação de área.",
            SendEmail = true,
            EmailSubject = $"Ação necessária — Lote reenviado após reajuste — {reqRef}",
            EmailHeadline = AdjustmentEventLabels.ResubmittedToArea,
            EmailBody = body
        };
    }


    // =====================================================================
    // BUYER P.O. CREATION INSTRUCTION EMAIL
    // =====================================================================

    /// <summary>
    /// Sends a dedicated, operational email to the assigned Buyer when a request is finally approved.
    /// Contains all data needed to create the P.O. in PRIMAVERA without manual lookups.
    /// Idempotency: uses a deterministic CorrelationId derived from RequestId + BuyerId to guarantee
    /// the Buyer never receives duplicate P.O. creation instruction emails for the same approved request.
    /// </summary>
    private async Task SendBuyerPoCreationInstructionAsync(WorkflowEvent evt)
    {
        // ── Guard: no buyer assigned ──
        if (!evt.BuyerId.HasValue)
        {
            _logger.LogWarning("[BuyerPO] No buyer assigned for Request {RequestId}. Skipping P.O. creation instruction email.", evt.RequestId);
            return;
        }

        var buyerId = evt.BuyerId.Value;

        // ── Idempotency: deterministic CorrelationId from RequestId + BuyerId ──
        // This ensures stability across retries — the same (request, buyer) pair always produces the same GUID.
        var dedupCorrelationId = GenerateDeterministicGuid(evt.RequestId, buyerId, "BUYER_PO_CREATION_INSTRUCTION");

        var alreadySent = await _context.InformationalNotifications
            .AnyAsync(n => n.EventCorrelationId == dedupCorrelationId && n.UserId == buyerId);

        if (alreadySent)
        {
            _logger.LogInformation("[BuyerPO] P.O. creation instruction already sent for Request {RequestId}, Buyer {BuyerId} (CorrelationId: {CorrelationId}). Skipping.",
                evt.RequestId, buyerId, dedupCorrelationId);
            return;
        }

        // ── Load full request data with quotation includes ──
        var request = await _context.Requests
            .AsNoTracking()
            .Include(r => r.Requester)
            .Include(r => r.Department)
            .Include(r => r.Plant).ThenInclude(p => p!.Company)
            .Include(r => r.Currency)
            .Include(r => r.Buyer)
            .Include(r => r.Quotations.Where(q => q.IsSelected))
                .ThenInclude(q => q.Supplier)
            .Include(r => r.Quotations.Where(q => q.IsSelected))
                .ThenInclude(q => q.Items.OrderBy(i => i.LineNumber))
                    .ThenInclude(i => i.ItemCatalog)
            .Include(r => r.Quotations.Where(q => q.IsSelected))
                .ThenInclude(q => q.Items)
                    .ThenInclude(i => i.Unit)
            .Include(r => r.Quotations.Where(q => q.IsSelected))
                .ThenInclude(q => q.Items)
                    .ThenInclude(i => i.IvaRate)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == evt.RequestId);

        if (request == null)
        {
            _logger.LogWarning("[BuyerPO] Request {RequestId} not found. Skipping P.O. creation instruction email.", evt.RequestId);
            return;
        }

        if (request.Buyer == null)
        {
            _logger.LogWarning("[BuyerPO] Buyer user record not found for BuyerId {BuyerId}. Skipping.", buyerId);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Buyer.Email))
        {
            _logger.LogWarning("[BuyerPO] Buyer {BuyerId} has no email address. Skipping P.O. creation instruction.", buyerId);
            return;
        }

        var selectedQuotation = request.Quotations.FirstOrDefault(q => q.IsSelected);
        var supplier = selectedQuotation?.Supplier;
        var quotationItems = selectedQuotation?.Items?.OrderBy(i => i.LineNumber).ToList();
        var reqRef = request.RequestNumber ?? "S/N";
        var currencyCode = request.Currency?.Code ?? selectedQuotation?.Currency ?? "AOA";

        _logger.LogInformation("[BuyerPO] Building P.O. creation instruction email for Buyer {BuyerName} ({BuyerEmail}), Request {RequestNumber}.",
            request.Buyer.FullName, request.Buyer.Email, reqRef);

        // ── Build rich HTML email body ──
        var sb = new System.Text.StringBuilder();

        // Section 1: Request Information
        sb.Append("<h3 style='color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 8px;'>Dados do Pedido</h3>");
        sb.Append("<table style='width: 100%; border-collapse: collapse; margin-bottom: 16px; font-size: 14px;'>");
        AppendInfoRow(sb, "Número do Pedido", $"<b>{Encode(reqRef)}</b>");
        AppendInfoRow(sb, "Título", Encode(request.Title ?? "—"));
        AppendInfoRow(sb, "Requisitante", Encode(request.Requester?.FullName ?? "—"));
        AppendInfoRow(sb, "Departamento", Encode(request.Department?.Name ?? "—"));
        AppendInfoRow(sb, "Planta / Empresa", $"{Encode(request.Plant?.Name ?? "—")} / {Encode(request.Plant?.Company?.Name ?? "—")}");
        AppendInfoRow(sb, "Data de Aprovação", DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm") + " (UTC)");
        AppendInfoRow(sb, "Comprador Atribuído", Encode(request.Buyer.FullName));
        if (!string.IsNullOrWhiteSpace(request.Description))
            AppendInfoRow(sb, "Observações", Encode(request.Description));
        sb.Append("</table>");

        // Section 2: Supplier Information
        sb.Append("<h3 style='color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 8px;'>Dados do Fornecedor</h3>");
        sb.Append("<table style='width: 100%; border-collapse: collapse; margin-bottom: 16px; font-size: 14px;'>");
        AppendInfoRow(sb, "Fornecedor", Encode(supplier?.Name ?? selectedQuotation?.SupplierNameSnapshot ?? "—"));
        AppendInfoRow(sb, "NIF", Encode(supplier?.TaxId ?? "—"));
        AppendInfoRow(sb, "Código PRIMAVERA", Encode(supplier?.PrimaveraCode ?? "Sem código PRIMAVERA"));
        AppendInfoRow(sb, "E-mail", Encode(supplier?.ContactEmail1 ?? "—"));
        sb.Append("</table>");

        // Section 3: Financial Summary
        sb.Append("<h3 style='color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 8px;'>Resumo Financeiro</h3>");
        sb.Append("<table style='width: 100%; border-collapse: collapse; margin-bottom: 16px; font-size: 14px;'>");
        AppendInfoRow(sb, "Moeda", currencyCode);
        if (selectedQuotation != null)
        {
            AppendInfoRow(sb, "Subtotal (Antes IVA)", $"{currencyCode} {selectedQuotation.TotalTaxableBase:N2}");
            if (selectedQuotation.DiscountAmount > 0)
                AppendInfoRow(sb, "Desconto", $"{currencyCode} {selectedQuotation.DiscountAmount:N2}");
            AppendInfoRow(sb, "IVA Total", $"{currencyCode} {selectedQuotation.TotalIvaAmount:N2}");
            AppendInfoRow(sb, "<b>Total</b>", $"<b>{currencyCode} {selectedQuotation.TotalAmount:N2}</b>");
        }
        else
        {
            var totalAmt = request.EstimatedTotalAmount;
            AppendInfoRow(sb, "<b>Total Estimado</b>", $"<b>{currencyCode} {totalAmt:N2}</b>");
        }
        sb.Append("</table>");

        // Section 4: Payment Condition
        sb.Append("<h3 style='color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 8px;'>Condição de Pagamento</h3>");
        sb.Append("<table style='width: 100%; border-collapse: collapse; margin-bottom: 16px; font-size: 14px;'>");
        var paymentLabel = request.PaymentConditionCode switch
        {
            "POST_PAID" => "Pós-pago (após entrega)",
            "ADVANCE_FULL" => "100% Antecipado",
            "ADVANCE_PARTIAL" => $"Antecipado Parcial ({request.AdvancePaymentPercent ?? 0}%)",
            _ => "A definir no registo da P.O."
        };
        AppendInfoRow(sb, "Condição", paymentLabel);
        if (request.AdvancePaymentPercent.HasValue && request.PaymentConditionCode != "POST_PAID")
            AppendInfoRow(sb, "Percentual de Adiantamento", $"{request.AdvancePaymentPercent}%");
        sb.Append("</table>");

        // Section 5: Line Items Table with PRIMAVERA Codes
        if (quotationItems != null && quotationItems.Any())
        {
            sb.Append("<h3 style='color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 8px;'>Itens da Cotação</h3>");
            sb.Append("<table style='width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 13px;'>");
            sb.Append("<thead><tr style='background-color: #f8f9fa; border-bottom: 2px solid #dee2e6;'>");
            sb.Append("<th style='padding: 8px; text-align: left;'>#</th>");
            sb.Append("<th style='padding: 8px; text-align: left;'>Descrição</th>");
            sb.Append("<th style='padding: 8px; text-align: right;'>Qtd</th>");
            sb.Append("<th style='padding: 8px; text-align: left;'>Un.</th>");
            sb.Append("<th style='padding: 8px; text-align: right;'>Preço Unit.</th>");
            sb.Append("<th style='padding: 8px; text-align: right;'>IVA %</th>");
            sb.Append("<th style='padding: 8px; text-align: right;'>Total</th>");
            sb.Append("<th style='padding: 8px; text-align: left;'>Cód. PRIMAVERA</th>");
            sb.Append("</tr></thead><tbody>");

            foreach (var item in quotationItems)
            {
                var primaveraCode = item.ItemCatalogId != null
                    ? Encode(item.ItemCatalog?.PrimaveraCode ?? item.ItemCatalog?.Code ?? "—")
                    : "<em style='color: #e67e22;'>Manual item - no PRIMAVERA item ID</em>";

                sb.Append("<tr style='border-bottom: 1px solid #e9ecef;'>");
                sb.Append($"<td style='padding: 8px;'>{item.LineNumber}</td>");
                sb.Append($"<td style='padding: 8px;'>{Encode(item.Description)}</td>");
                sb.Append($"<td style='padding: 8px; text-align: right;'>{item.Quantity:N2}</td>");
                sb.Append($"<td style='padding: 8px;'>{item.Unit?.Code ?? "—"}</td>");
                sb.Append($"<td style='padding: 8px; text-align: right;'>{item.UnitPrice:N2}</td>");
                sb.Append($"<td style='padding: 8px; text-align: right;'>{item.IvaRatePercent:N1}%</td>");
                sb.Append($"<td style='padding: 8px; text-align: right;'>{item.LineTotal:N2}</td>");
                sb.Append($"<td style='padding: 8px;'>{primaveraCode}</td>");
                sb.Append("</tr>");
            }

            sb.Append("</tbody></table>");
        }

        // CTA instruction
        sb.Append("<p style='margin-top: 20px; padding: 12px; background: #eaf4fc; border-left: 4px solid #3498db; font-size: 14px;'>");
        sb.Append("<b>Próximo passo:</b> Crie a Purchase Order no PRIMAVERA com os dados acima e faça o upload do documento P.O. no portal.");
        sb.Append("</p>");

        var emailBody = sb.ToString();
        var subject = $"Pedido aprovado - aguardando criação da P.O. | Request #{reqRef}";
        var headline = "Pedido Aprovado — Aguardando P.O.";

        var portalBaseUrl = _config["App:PortalBaseUrl"] ?? "";
        var actionUrl = $"{portalBaseUrl}/requests/{evt.RequestId}";

        // ── Send email ──
        var sent = await _emailService.SendWorkflowNotificationAsync(
            request.Buyer.Email,
            request.Buyer.FullName,
            subject,
            headline,
            emailBody,
            actionUrl,
            "Abrir Pedido no Portal");

        if (sent)
        {
            _logger.LogInformation("[BuyerPO] P.O. creation instruction email sent to {BuyerEmail} for Request {RequestNumber} (CorrelationId: {CorrelationId}).",
                request.Buyer.Email, reqRef, dedupCorrelationId);

            // Persist dedup record
            await _notificationService.CreateNotificationWithDedupAsync(
                buyerId,
                "Instrução de Criação de P.O.",
                $"O pedido {reqRef} foi aprovado. Crie a P.O. no PRIMAVERA.",
                "BUYER_INSTRUCTION",
                $"/requests/{evt.RequestId}",
                dedupCorrelationId,
                "BUYER_PO_CREATION_INSTRUCTION");
        }
        else
        {
            _logger.LogWarning("[BuyerPO] Email dispatch returned failure for Buyer {BuyerEmail}, Request {RequestNumber}.", request.Buyer.Email, reqRef);
        }
    }

    /// <summary>
    /// Generates a deterministic GUID from a set of input values using a namespace-based approach.
    /// Guarantees that the same inputs always produce the same GUID, enabling stable idempotency keys.
    /// </summary>
    private static Guid GenerateDeterministicGuid(Guid requestId, Guid buyerId, string purpose)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var input = $"{requestId}:{buyerId}:{purpose}";
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }

    private static void AppendInfoRow(System.Text.StringBuilder sb, string label, string value)
    {
        sb.Append("<tr>");
        sb.Append($"<td style='padding: 6px 8px; color: #7f8c8d; width: 40%;'>{label}</td>");
        sb.Append($"<td style='padding: 6px 8px; color: #2c3e50;'>{value}</td>");
        sb.Append("</tr>");
    }

    private static string Encode(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "—");

    private async Task<string> FormatLineItemsTableAsync(Guid requestId)
    {
        var items = await _context.RequestLineItems
            .Include(li => li.Unit)
            .Where(li => li.RequestId == requestId && !li.IsDeleted)
            .OrderBy(li => li.LineNumber)
            .ToListAsync();

        if (!items.Any()) return "<p><i>Nenhum item encontrado.</i></p>";

        var sb = new System.Text.StringBuilder();
        sb.Append("<table style='width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 14px;'>");
        sb.Append("<thead><tr style='background-color: #f8f9fa; border-bottom: 2px solid #dee2e6;'>");
        sb.Append("<th style='padding: 8px; text-align: left;'>Descrição</th>");
        sb.Append("<th style='padding: 8px; text-align: right;'>Qtd</th>");
        sb.Append("<th style='padding: 8px; text-align: left;'>Un.</th>");
        sb.Append("<th style='padding: 8px; text-align: right;'>Preço Unit.</th>");
        sb.Append("<th style='padding: 8px; text-align: right;'>Total</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var item in items)
        {
            sb.Append("<tr style='border-bottom: 1px solid #e9ecef;'>");
            sb.Append($"<td style='padding: 8px;'>{System.Net.WebUtility.HtmlEncode(item.Description)}</td>");
            sb.Append($"<td style='padding: 8px; text-align: right;'>{item.Quantity:N2}</td>");
            sb.Append($"<td style='padding: 8px;'>{item.Unit?.Code ?? "-"}</td>");
            sb.Append($"<td style='padding: 8px; text-align: right;'>{item.UnitPrice:N2}</td>");
            sb.Append($"<td style='padding: 8px; text-align: right;'>{item.TotalAmount:N2}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    // =====================================================================
    // INTERNAL TYPES
    // =====================================================================

    /// <summary>
    /// Human-readable reference used in subjects/bodies. Batch-scoped events (the approval-batch
    /// workflow sets <see cref="WorkflowEvent.BatchNumber"/>) carry the batch so an approver with
    /// several lots on the same request can tell which one the message is about. Request-level
    /// events are unchanged.
    /// </summary>
    public static string FormatRequestRef(WorkflowEvent evt) =>
        evt.BatchNumber.HasValue ? $"{evt.RequestNumber} (Lote #{evt.BatchNumber.Value})" : $"{evt.RequestNumber}";

    private record NotificationRecipient(Guid UserId, string Email, string FullName)
    {
        /// <summary>
        /// If true, email is suppressed for this recipient (e.g., global Finance fan-out fallback).
        /// </summary>
        public bool SuppressEmail { get; init; }
        
        public string? EmailSubjectOverride { get; init; }
        public string? EmailBodyOverride { get; init; }
        public bool BypassSelfNotifyRule { get; init; }
    }

    private class EventConfig
    {
        public string Category { get; init; } = NotificationCategories.Info;
        public string NotificationType { get; init; } = NotificationTypes.Info;
        public string InAppTitle { get; init; } = string.Empty;
        public string InAppMessage { get; init; } = string.Empty;
        public bool SendEmail { get; init; }
        public string EmailSubject { get; init; } = string.Empty;
        public string EmailHeadline { get; init; } = string.Empty;
        public string EmailBody { get; init; } = string.Empty;
        /// <summary>When true, a failure to persist a recipient's in-app row propagates (the caller runs the emit inside its
        /// transaction and must roll back ownership, audit and intent together). Default false keeps the historical
        /// "non-critical" policy for every other event.</summary>
        public bool RequireInAppPersistence { get; init; }
    }
}
