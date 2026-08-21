using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

// ─────────────────────────────────────────────────────────────────────────────
// v2.230.0 — Multi-Group Request Workflow projection.
//
// "Request is the container. Operational units are batches/groups."
//
// Pure, static, side-effect-free builder (RequestStatusCalculator pattern):
// operates only on the entities the caller loaded, never persists anything.
// Serialized directly by the API (camelCase) and mirrored by the frontend
// types in src/frontend/src/types (RequestWorkflowProjection).
// ─────────────────────────────────────────────────────────────────────────────

public sealed record WorkflowNextAction(
    string UnitType,          // "BATCH" | "GROUP"
    Guid UnitId,
    string UnitLabel,
    string ActionType,        // e.g. REGISTER_PO, SCHEDULE_PAYMENT, AREA_APPROVE…
    string Label,             // PT copy shown to the user
    string ResponsibleRole,   // PT role label, same vocabulary as the legacy header
    int Priority);            // lower = shown first (furthest-behind unblocks the request)

public sealed record WorkflowResponsibility(string Role, int UnitCount);

public sealed record WorkflowUnit(
    string UnitType,          // "BATCH" (pre-PO approval wave) | "GROUP" (operational unit)
    Guid UnitId,
    string Label,
    int? SupplierId,
    string? SupplierName,
    decimal TotalAmount,
    string? CurrencyCode,
    int ItemCount,
    IReadOnlyList<int> ItemLineNumbers,
    int? BatchNumber,
    string StatusCode,        // group status, or batch status for BATCH units
    string StatusLabel,       // PT label for StatusCode
    string ApprovalState,     // COMPLETE | IN_PROGRESS | ADJUSTMENT
    string? PurchaseOrderNumber,
    string PoState,           // PENDING | CORRECTION | ISSUED | NOT_APPLICABLE
    string PaymentState,      // PENDING | ADVANCE_IN_PROGRESS | SCHEDULED | COMPLETE | NOT_STARTED
    string ReceivingState,    // PENDING | IN_PROGRESS | COMPLETE | NOT_STARTED
    string CompletionState,   // COMPLETE | WAITING_FISCAL_RECEIPT | NOT_STARTED
    string ResponsibleRole,
    WorkflowNextAction? NextAction);

public sealed record WorkflowAggregateDisplay(string StatusCode, string Label);

public sealed record RequestWorkflowProjection(
    WorkflowAggregateDisplay AggregateDisplay,
    IReadOnlyList<WorkflowUnit> Units,
    IReadOnlyList<WorkflowResponsibility> Responsibilities,
    IReadOnlyList<WorkflowNextAction> NextActions,
    IReadOnlyList<string> Warnings);

public static class RequestWorkflowProjectionBuilder
{
    // PT labels for persisted status codes (mirrors the RequestStatuses seed) plus the
    // display-only aggregate codes ComputeDisplayWorkflowState can emit. Single source for
    // the projection so the frontend never needs its own duplicate of this table.
    private static readonly Dictionary<string, string> StatusLabels = new(StringComparer.Ordinal)
    {
        ["DRAFT"] = "Rascunho",
        ["WAITING_QUOTATION"] = "Aguardando Cotação",
        ["WAITING_AREA_APPROVAL"] = "Aguardando Aprovação da Área",
        ["AREA_ADJUSTMENT"] = "Reajuste A.A",
        ["WAITING_FINAL_APPROVAL"] = "Aguardando Aprovação Final",
        ["FINAL_ADJUSTMENT"] = "Reajuste A.F",
        ["REJECTED"] = "Rejeitado",
        ["APPROVED"] = "Aprovado",
        ["QUOTATION_COMPLETED"] = "Cotação Concluída",
        ["PO_REQUESTED"] = "Aguardando P.O.",
        ["PENDING"] = "Aguardando Ativação",
        ["WAITING_PO"] = "Aguardando P.O.",
        ["WAITING_PO_CORRECTION"] = "Devolvido para Compras",
        ["PO_PARTIALLY_UPLOADED"] = "P.O Parcialmente Registrada",
        ["PO_ISSUED"] = "P.O Emitida",
        ["ADVANCE_PAYMENT_REQUIRED"] = "Adiantamento Necessário",
        ["ADVANCE_PAYMENT_SCHEDULED"] = "Adiantamento Agendado",
        ["ADVANCE_PAYMENT_COMPLETED"] = "Adiantamento Realizado",
        ["WAITING_SUPPLIER_DELIVERY"] = "Ag. Entrega/Serviço",
        ["PAYMENT_REQUEST_SENT"] = "Solicitação Pagamento Enviada",
        ["PAYMENT_SCHEDULED"] = "Pagamento Agendado",
        ["PAYMENT_COMPLETED"] = "Pagamento Realizado",
        ["WAITING_RECEIPT"] = "Aguardando Recibo",
        ["WAITING_RECONCILIATION"] = "Ag. Reconciliação",
        ["WAITING_FISCAL_RECEIPT"] = "Ag. Recibo Fiscal",
        ["IN_FOLLOWUP"] = "Em Acompanhamento",
        ["COMPLETED"] = "Finalizado",
        ["CANCELLED"] = "Cancelado",
        // Display-only aggregate codes (never persisted)
        ["MIXED_PROCESSING"] = "Processamento Parcial",
        ["PARTIALLY_PO_ISSUED"] = "P.O Parcialmente Registrada",
        ["PARTIALLY_APPROVED"] = "Parcialmente Aprovado",
        ["PARTIALLY_IN_APPROVAL"] = "Parcialmente em Aprovação",
        ["QUOTATION_IN_APPROVAL"] = "Cotação em Aprovação",
        ["QUOTATION_IN_PROGRESS"] = "Cotação em Andamento",
        ["FULLY_APPROVED"] = "Aprovado",
        ["APPROVED_WITH_CLOSURES"] = "Aprovado (com encerramentos)",
        ["FULLY_COMPLETED"] = "Finalizado",
        ["COMPLETED_WITH_CLOSURES"] = "Finalizado (com encerramentos)",
    };

    public static string LabelFor(string statusCode) =>
        StatusLabels.TryGetValue(statusCode, out var label) ? label : statusCode;

    /// <summary>
    /// v2.230.0 historical compatibility — Requests-list badge override for a SINGLE-unit
    /// request whose persisted scalar lags the operational unit (REQ-140 class: scalar APPROVED
    /// while the group is PO_ISSUED). DISPLAY ONLY: never used for permissions, workflow
    /// authorization or server-side filtering. Guardrails: terminal scalars (CANCELLED/
    /// REJECTED/COMPLETED) stay authoritative; multi-unit rows keep the existing group-aware/
    /// display-state path; no override when the unit agrees with the scalar. Returns null when
    /// no reliable override exists — callers fall back to the persisted status name.
    /// </summary>
    public static (string Code, string Label)? ResolveSingleUnitBadgeOverride(
        RequestWorkflowProjection projection, string scalarStatusCode)
    {
        if (scalarStatusCode is "CANCELLED" or "REJECTED" or "COMPLETED") return null;
        if (projection.Units.Count != 1) return null;

        var unit = projection.Units[0];
        if (unit.StatusCode == scalarStatusCode) return null;

        return (unit.StatusCode, unit.StatusLabel);
    }

    // Per-unit guidance. Labels reuse the SAME strings the legacy single-status header
    // (frontend lib/utils.ts getRequestGuidance) shows today so single-unit requests remain
    // string-identical — the compatibility rule of this release.
    private static (string Role, string ActionType, string Label, int Priority) GroupGuidance(string status) => status switch
    {
        RequestConstants.PoGroupStatuses.Pending
            => ("Aprovador Final", "FINAL_APPROVE", "Aguardar decisão da aprovação final", 20),
        RequestConstants.PoGroupStatuses.WaitingPo
            => ("Comprador", "REGISTER_PO", "Prosseguir com a emissão ou inserção da P.O", 30),
        RequestConstants.PoGroupStatuses.WaitingPoCorrection
            => ("Comprador", "CORRECT_PO", "Corrigir P.O devolvida por Finanças e re-registrar", 25),
        RequestConstants.PoGroupStatuses.AdvancePaymentRequired
            => ("Financeiro", "SCHEDULE_ADVANCE", "Agendar o adiantamento", 40),
        RequestConstants.PoGroupStatuses.AdvancePaymentScheduled
            => ("Financeiro", "CONFIRM_ADVANCE", "Confirmar o pagamento do adiantamento", 41),
        RequestConstants.PoGroupStatuses.AdvancePaymentCompleted
            => ("Comprador", "CONFIRM_DELIVERY", "Acompanhar a entrega do fornecedor", 42),
        RequestConstants.PoGroupStatuses.WaitingSupplierDelivery
            => ("Comprador", "CONFIRM_DELIVERY", "Acompanhar a entrega do fornecedor", 43),
        RequestConstants.PoGroupStatuses.PoIssued
            => ("Financeiro", "SCHEDULE_PAYMENT", "Pagar ou agendar o pagamento", 50),
        RequestConstants.PoGroupStatuses.PaymentRequestSent
            => ("Financeiro", "SCHEDULE_PAYMENT", "Pagar ou agendar o pagamento", 51),
        RequestConstants.PoGroupStatuses.PaymentScheduled
            => ("Financeiro", "COMPLETE_PAYMENT", "Realizar o pagamento", 55),
        RequestConstants.PoGroupStatuses.PaymentCompleted
            => ("Recebimento", "RECEIVE", "Mover para fase de recebimento e conferir itens", 60),
        RequestConstants.PoGroupStatuses.WaitingReceipt
            => ("Financeiro", "ATTACH_RECEIPT", "Anexar recibo do fornecedor e finalizar pedido", 65),
        RequestConstants.PoGroupStatuses.WaitingReconciliation
            => ("Recebimento", "RECONCILE", "Concluir a reconciliação do recebimento", 66),
        RequestConstants.PoGroupStatuses.WaitingFiscalReceipt
            => ("Financeiro", "ATTACH_FISCAL_RECEIPT", "Registrar o Recibo Fiscal para concluir o grupo", 70),
        RequestConstants.PoGroupStatuses.InFollowup
            => ("Recebimento", "RESOLVE_FOLLOWUP", "Resolver itens pendentes e confirmar recebimento", 67),
        _ => ("Sem ação", "NONE", "Sem ação pendente", 999),
    };

    private static (string Role, string ActionType, string Label, int Priority) BatchGuidance(string status) => status switch
    {
        RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval
            => ("Aprovador da Área", "AREA_APPROVE", "Selecionar vencedor e aprovar", 10),
        RequestConstants.ApprovalBatchStatuses.AreaAdjustment
            => ("Comprador", "RESOLVE_ADJUSTMENT", "Revisar o lote e reenviar para a próxima etapa", 12),
        RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval
            => ("Aprovador Final", "FINAL_APPROVE", "Aguardar decisão da aprovação final", 15),
        RequestConstants.ApprovalBatchStatuses.FinalAdjustment
            => ("Comprador", "RESOLVE_ADJUSTMENT", "Revisar o lote e reenviar para a próxima etapa", 16),
        _ => ("Sem ação", "NONE", "Sem ação pendente", 999),
    };

    public static RequestWorkflowProjection Build(
        Request request,
        string displayWorkflowStateCode)
    {
        var lineItems = request.LineItems.Where(li => !li.IsDeleted).ToList();
        var allGroups = request.PoGroups.ToList();
        var allBatches = request.ApprovalBatches.ToList();
        var requestStatusCode = request.Status?.Code ?? "";

        var warnings = new List<string>();
        var units = new List<WorkflowUnit>();

        // ── Terminal request states dominate: no active units, no actions ──
        var isTerminal = requestStatusCode is "CANCELLED" or "REJECTED";
        if (!isTerminal)
        {
            // Superseded batches: excluded from active units/responsibilities/actions,
            // surfaced as diagnostics warnings instead (never hidden from history).
            var activeBatchIds = new HashSet<Guid>();
            foreach (var batch in allBatches.Where(SupersededBatchPolicy.IsInApproval))
            {
                if (SupersededBatchPolicy.IsSuperseded(batch, lineItems, allGroups))
                {
                    warnings.Add(
                        $"Lote #{batch.BatchNumber} obsoleto — os itens deste lote já foram processados por outro fluxo.");
                    continue;
                }
                activeBatchIds.Add(batch.Id);
                units.Add(BuildBatchUnit(batch, lineItems));
            }

            foreach (var group in allGroups.Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled))
            {
                // One wave = one active unit: a PENDING group is only the pre-activation
                // representation of its batch — while that batch is itself an active BATCH
                // unit, emitting the group too would duplicate the wave (units,
                // responsibilities and next actions all doubled). Identity-based: the
                // group's ApprovalBatchId must reference the emitted batch. Once the batch
                // settles (APPROVED), the group leaves PENDING and becomes the single
                // active operational unit — never suppressed then.
                if (group.Status == RequestConstants.PoGroupStatuses.Pending &&
                    group.ApprovalBatchId.HasValue &&
                    activeBatchIds.Contains(group.ApprovalBatchId.Value))
                {
                    continue;
                }
                units.Add(BuildGroupUnit(group, lineItems));
            }
        }

        var responsibilities = units
            .Where(u => u.NextAction != null)
            .GroupBy(u => u.ResponsibleRole)
            .Select(g => new WorkflowResponsibility(g.Key, g.Count()))
            .OrderBy(r => units.Where(u => u.ResponsibleRole == r.Role && u.NextAction != null)
                               .Min(u => u.NextAction!.Priority))
            .ToList();

        var nextActions = units
            .Where(u => u.NextAction != null)
            .Select(u => u.NextAction!)
            .OrderBy(a => a.Priority)
            .ToList();

        var aggregateCode = isTerminal ? requestStatusCode
            : string.IsNullOrWhiteSpace(displayWorkflowStateCode) ? requestStatusCode
            : displayWorkflowStateCode;

        return new RequestWorkflowProjection(
            new WorkflowAggregateDisplay(aggregateCode, LabelFor(aggregateCode)),
            units,
            responsibilities,
            nextActions,
            warnings);
    }

    private static WorkflowUnit BuildBatchUnit(ApprovalBatch batch, IReadOnlyList<RequestLineItem> lineItems)
    {
        var itemIds = batch.Items.Select(bi => bi.RequestLineItemId).ToHashSet();
        var coveredItems = lineItems.Where(li => itemIds.Contains(li.Id)).ToList();
        var guidance = BatchGuidance(batch.Status);
        var label = $"Lote #{batch.BatchNumber}";

        var approvalState = batch.Status is "AREA_ADJUSTMENT" or "FINAL_ADJUSTMENT" ? "ADJUSTMENT" : "IN_PROGRESS";

        return new WorkflowUnit(
            UnitType: "BATCH",
            UnitId: batch.Id,
            Label: label,
            SupplierId: null,
            SupplierName: null,
            TotalAmount: batch.ApprovedTotalAmount ?? 0m,
            CurrencyCode: null,
            ItemCount: coveredItems.Count,
            ItemLineNumbers: coveredItems.Select(li => li.LineNumber).OrderBy(n => n).ToList(),
            BatchNumber: batch.BatchNumber,
            StatusCode: batch.Status,
            StatusLabel: LabelFor(batch.Status),
            ApprovalState: approvalState,
            PurchaseOrderNumber: null,
            PoState: "NOT_APPLICABLE",
            PaymentState: "NOT_STARTED",
            ReceivingState: "NOT_STARTED",
            CompletionState: "NOT_STARTED",
            ResponsibleRole: guidance.Role,
            NextAction: new WorkflowNextAction("BATCH", batch.Id, label, guidance.ActionType, guidance.Label, guidance.Role, guidance.Priority));
    }

    private static WorkflowUnit BuildGroupUnit(RequestPoGroup group, IReadOnlyList<RequestLineItem> lineItems)
    {
        var coveredItems = lineItems.Where(li => li.RequestPoGroupId == group.Id).ToList();
        var guidance = GroupGuidance(group.Status);
        var label = string.IsNullOrWhiteSpace(group.SupplierNameSnapshot)
            ? "Grupo sem fornecedor definido"
            : $"Grupo {group.SupplierNameSnapshot}";

        var s = group.Status;
        var poState = s switch
        {
            RequestConstants.PoGroupStatuses.Pending or RequestConstants.PoGroupStatuses.WaitingPo => "PENDING",
            RequestConstants.PoGroupStatuses.WaitingPoCorrection => "CORRECTION",
            RequestConstants.PoGroupStatuses.AdvancePaymentRequired or
            RequestConstants.PoGroupStatuses.AdvancePaymentScheduled or
            RequestConstants.PoGroupStatuses.AdvancePaymentCompleted or
            RequestConstants.PoGroupStatuses.WaitingSupplierDelivery
                => string.IsNullOrWhiteSpace(group.PurchaseOrderNumber) ? "PENDING" : "ISSUED",
            _ => "ISSUED",
        };
        var paymentState = s switch
        {
            RequestConstants.PoGroupStatuses.AdvancePaymentRequired or
            RequestConstants.PoGroupStatuses.AdvancePaymentScheduled => "ADVANCE_IN_PROGRESS",
            RequestConstants.PoGroupStatuses.AdvancePaymentCompleted or
            RequestConstants.PoGroupStatuses.WaitingSupplierDelivery => "ADVANCE_IN_PROGRESS",
            RequestConstants.PoGroupStatuses.PoIssued or
            RequestConstants.PoGroupStatuses.PaymentRequestSent => "PENDING",
            RequestConstants.PoGroupStatuses.PaymentScheduled => "SCHEDULED",
            RequestConstants.PoGroupStatuses.PaymentCompleted or
            RequestConstants.PoGroupStatuses.WaitingReceipt or
            RequestConstants.PoGroupStatuses.WaitingReconciliation or
            RequestConstants.PoGroupStatuses.WaitingFiscalReceipt or
            RequestConstants.PoGroupStatuses.InFollowup or
            RequestConstants.PoGroupStatuses.Completed => "COMPLETE",
            _ => "NOT_STARTED",
        };
        // Domain semantics (v2.229.9, confirmed 2026-08-21): WAITING_RECEIPT means the
        // receiving/execution work is DONE and the group waits for the supplier's receipt —
        // a fiscal/finance document — to finalize. It is therefore receiving-COMPLETE and
        // fiscal-documentation-current, never an in-progress receiving state. True
        // receiving/execution states are RECONCILIATION/FOLLOWUP (and the delivery wait).
        var receivingState = s switch
        {
            RequestConstants.PoGroupStatuses.WaitingReconciliation or
            RequestConstants.PoGroupStatuses.InFollowup => "IN_PROGRESS",
            RequestConstants.PoGroupStatuses.WaitingReceipt or
            RequestConstants.PoGroupStatuses.WaitingFiscalReceipt or
            RequestConstants.PoGroupStatuses.Completed => "COMPLETE",
            RequestConstants.PoGroupStatuses.PaymentCompleted => "PENDING",
            _ => "NOT_STARTED",
        };
        var completionState = s switch
        {
            RequestConstants.PoGroupStatuses.Completed => "COMPLETE",
            RequestConstants.PoGroupStatuses.WaitingFiscalReceipt => "WAITING_FISCAL_RECEIPT",
            RequestConstants.PoGroupStatuses.WaitingReceipt => "WAITING_SUPPLIER_RECEIPT",
            _ => "NOT_STARTED",
        };

        var hasAction = guidance.ActionType != "NONE";
        return new WorkflowUnit(
            UnitType: "GROUP",
            UnitId: group.Id,
            Label: label,
            SupplierId: group.SupplierId,
            SupplierName: group.SupplierNameSnapshot,
            TotalAmount: group.TotalAmount,
            CurrencyCode: group.CurrencyCode,
            ItemCount: coveredItems.Count,
            ItemLineNumbers: coveredItems.Select(li => li.LineNumber).OrderBy(n => n).ToList(),
            BatchNumber: null,
            StatusCode: s,
            StatusLabel: LabelFor(s),
            ApprovalState: "COMPLETE",
            PurchaseOrderNumber: group.PurchaseOrderNumber,
            PoState: poState,
            PaymentState: paymentState,
            ReceivingState: receivingState,
            CompletionState: completionState,
            ResponsibleRole: guidance.Role,
            NextAction: hasAction
                ? new WorkflowNextAction("GROUP", group.Id, label, guidance.ActionType, guidance.Label, guidance.Role, guidance.Priority)
                : null);
    }

    /// <summary>
    /// Compact list-row summary, e.g. "3 grupos · 1 P.O. emitida · 1 em aprovação · 1 pagamento
    /// agendado". Returns null for requests with at most one active unit — single-unit rows keep
    /// their current appearance (compatibility rule).
    /// </summary>
    public static string? BuildUnitSummary(RequestWorkflowProjection projection)
    {
        if (projection.Units.Count <= 1) return null;

        var parts = projection.Units
            .GroupBy(u => SummaryBucket(u))
            .OrderBy(g => g.Min(u => u.NextAction?.Priority ?? 998))
            .Select(g => g.Count() == 1 ? $"1 {g.Key.Singular}" : $"{g.Count()} {g.Key.Plural}")
            .ToList();

        var unitWord = projection.Units.Count == 1 ? "grupo" : "grupos";
        return $"{projection.Units.Count} {unitWord} · " + string.Join(" · ", parts);
    }

    private readonly record struct SummaryBucketLabel(string Singular, string Plural);

    private static SummaryBucketLabel SummaryBucket(WorkflowUnit u)
    {
        if (u.UnitType == "BATCH") return new("em aprovação", "em aprovação");
        return u.StatusCode switch
        {
            RequestConstants.PoGroupStatuses.Pending => new("em aprovação", "em aprovação"),
            RequestConstants.PoGroupStatuses.WaitingPo or
            RequestConstants.PoGroupStatuses.WaitingPoCorrection => new("aguardando P.O.", "aguardando P.O."),
            RequestConstants.PoGroupStatuses.PoIssued or
            RequestConstants.PoGroupStatuses.PaymentRequestSent => new("P.O. emitida", "P.O. emitidas"),
            RequestConstants.PoGroupStatuses.AdvancePaymentRequired or
            RequestConstants.PoGroupStatuses.AdvancePaymentScheduled or
            RequestConstants.PoGroupStatuses.AdvancePaymentCompleted or
            RequestConstants.PoGroupStatuses.WaitingSupplierDelivery => new("adiantamento", "adiantamentos"),
            RequestConstants.PoGroupStatuses.PaymentScheduled => new("pagamento agendado", "pagamentos agendados"),
            RequestConstants.PoGroupStatuses.PaymentCompleted => new("pagamento realizado", "pagamentos realizados"),
            RequestConstants.PoGroupStatuses.WaitingReceipt or
            RequestConstants.PoGroupStatuses.WaitingReconciliation or
            RequestConstants.PoGroupStatuses.InFollowup => new("em recebimento", "em recebimento"),
            RequestConstants.PoGroupStatuses.WaitingFiscalReceipt => new("ag. recibo fiscal", "ag. recibo fiscal"),
            RequestConstants.PoGroupStatuses.Completed => new("concluído", "concluídos"),
            _ => new("em processamento", "em processamento"),
        };
    }
}
