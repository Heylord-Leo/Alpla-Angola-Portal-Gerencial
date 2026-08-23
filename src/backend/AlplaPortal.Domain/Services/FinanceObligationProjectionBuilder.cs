using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Pure, static projection of ONE RequestPoGroup into a Finance obligation — "what do I need to do
/// now, for which supplier, for how much, and by when". It reuses the group's own lifecycle status
/// and payment rows; it never re-implements eligibility (the per-group <c>financeActions</c> are
/// passed in, already computed by FinancePaymentEligibilityService) and never mutates anything.
///
/// Not a second workflow engine: the action CLASS is a deterministic display bucket over the
/// existing RequestPoGroup.Status. The Request is only the container; every obligation is judged in
/// isolation, so a sibling group's lifecycle can never mask this one.
/// </summary>
public static class FinanceObligationProjectionBuilder
{
    public sealed record PaymentInput(
        int? Id,
        string PaymentType,
        string PaymentStatus,
        decimal PlannedAmount,
        decimal? ActualPaidAmount,
        System.DateTime? ScheduledDateUtc,
        System.DateTime? PaidDateUtc,
        bool HasProof,
        string? CurrencyCode);

    public sealed record GroupInput(
        System.Guid GroupId,
        int? SupplierId,
        string? SupplierName,
        string? SupplierNif,
        string? SupplierTaxId,
        string GroupStatus,
        string? PurchaseOrderNumber,
        string? CurrencyCode,
        decimal TotalAmount,
        System.Collections.Generic.IReadOnlyList<string> FinanceActions,
        System.Collections.Generic.IReadOnlyList<PaymentInput> Payments);

    public sealed record RequestInput(
        System.Guid RequestId,
        string RequestNumber,
        string RequestTypeCode,
        string? Title,
        string? Department,
        string? Plant);

    public sealed record FinanceObligation(
        // Identity
        System.Guid RequestId,
        string RequestNumber,
        string RequestTypeCode,
        string? RequestTitle,
        string? Department,
        string? Plant,
        // Group
        System.Guid RequestPoGroupId,
        int? SupplierId,
        string? SupplierName,
        string? SupplierNif,
        string? SupplierTaxId,
        string GroupStatusCode,
        string GroupStatusLabel,
        string OperationalStateLabel,
        string? PurchaseOrderNumber,
        string? CurrencyCode,
        decimal GroupAmount,
        // Payment
        int? PaymentId,
        string? PaymentType,
        System.DateTime? ScheduledDateUtc,
        decimal? PlannedAmount,
        decimal? ActualPaidAmount,
        System.DateTime? PaidDateUtc,
        bool HasPaymentProof,
        // Action
        System.Collections.Generic.IReadOnlyList<string> FinanceActions,
        string ActionClass,
        string ActionClassLabel,
        string? NextActionLabel,
        string ResponsibleRole,
        // Timing
        System.DateTime? DueDate,
        bool IsOverdue,
        int OverdueDays,
        bool IsDueToday,
        // Display
        decimal ObligationAmount);

    // Group-status → action class (deterministic). Uses the Statuses.* values, which are string-
    // identical to the PoGroupStatuses.* values the groups actually carry.
    private static string MapActionClass(string groupStatus) => groupStatus switch
    {
        RequestConstants.Statuses.PoIssued
            or RequestConstants.Statuses.AdvancePaymentRequired
            or RequestConstants.Statuses.PaymentRequestSent => FinanceActionClasses.NeedsScheduling,

        RequestConstants.Statuses.PaymentScheduled
            or RequestConstants.Statuses.AdvancePaymentScheduled => FinanceActionClasses.NeedsPayment,

        RequestConstants.Statuses.PaymentCompleted
            or RequestConstants.Statuses.AdvancePaymentCompleted
            or RequestConstants.Statuses.WaitingSupplierDelivery => FinanceActionClasses.PaidWaitingReceiving,

        RequestConstants.Statuses.WaitingReceipt
            or RequestConstants.Statuses.WaitingReconciliation
            or RequestConstants.Statuses.InFollowup => FinanceActionClasses.InReceivingFollowup,

        RequestConstants.Statuses.WaitingFiscalReceipt => FinanceActionClasses.FiscalDocumentPending,

        RequestConstants.Statuses.Completed => FinanceActionClasses.Completed,

        // PENDING / WAITING_PO / WAITING_PO_CORRECTION and anything else pre-finance/buyer-owned.
        _ => FinanceActionClasses.NoFinanceAction
    };

    private static string ResponsibleFor(string actionClass) => actionClass switch
    {
        FinanceActionClasses.NeedsScheduling or FinanceActionClasses.NeedsPayment
            or FinanceActionClasses.FiscalDocumentPending => FinanceResponsibleRoles.Finance,
        FinanceActionClasses.PaidWaitingReceiving or FinanceActionClasses.InReceivingFollowup => FinanceResponsibleRoles.Receiving,
        FinanceActionClasses.Completed => FinanceResponsibleRoles.None,
        _ => FinanceResponsibleRoles.Buyer // NO_FINANCE_ACTION: PENDING/WAITING_PO(_CORRECTION) are Buyer's
    };

    /// <summary>The active (non-cancelled) payment row relevant to this group's current state:
    /// the scheduled one if scheduled, else the completed one, else the latest planned.</summary>
    private static PaymentInput? RelevantPayment(GroupInput group)
    {
        var live = group.Payments.Where(p => p.PaymentStatus != "CANCELLED").ToList();
        if (live.Count == 0) return null;
        return live.FirstOrDefault(p => p.PaymentStatus == "SCHEDULED")
            ?? live.FirstOrDefault(p => p.PaymentStatus == "COMPLETED")
            ?? live[^1];
    }

    /// <summary><paramref name="today"/> is the current business date (date-only, UTC midnight).</summary>
    public static FinanceObligation Build(RequestInput request, GroupInput group, System.DateTime today)
    {
        var actionClass = MapActionClass(group.GroupStatus);
        var payment = RelevantPayment(group);

        // Due date: ONLY from an active scheduled payment — never fabricated from the request deadline.
        System.DateTime? dueDate = group.GroupStatus is RequestConstants.Statuses.PaymentScheduled
                or RequestConstants.Statuses.AdvancePaymentScheduled
            ? payment?.ScheduledDateUtc
            : null;

        var isOverdue = false;
        var overdueDays = 0;
        var isDueToday = false;
        if (dueDate.HasValue && actionClass == FinanceActionClasses.NeedsPayment)
        {
            var due = dueDate.Value.Date;
            if (due < today) { isOverdue = true; overdueDays = (int)(today - due).TotalDays; }
            else if (due == today) { isDueToday = true; }
        }

        // Amount: unscheduled → group total; scheduled → planned payment amount; paid → actual paid.
        decimal obligationAmount = actionClass switch
        {
            FinanceActionClasses.NeedsPayment => payment?.PlannedAmount ?? group.TotalAmount,
            FinanceActionClasses.PaidWaitingReceiving => payment?.ActualPaidAmount ?? payment?.PlannedAmount ?? group.TotalAmount,
            _ => group.TotalAmount
        };

        var advance = group.GroupStatus is RequestConstants.Statuses.AdvancePaymentRequired
            or RequestConstants.Statuses.AdvancePaymentScheduled;
        var nextAction = NextActionLabel(actionClass, group.GroupStatus, advance);
        var operationalState = OperationalStateLabel(actionClass, group.GroupStatus, advance, isOverdue);

        return new FinanceObligation(
            RequestId: request.RequestId,
            RequestNumber: request.RequestNumber,
            RequestTypeCode: request.RequestTypeCode,
            RequestTitle: request.Title,
            Department: request.Department,
            Plant: request.Plant,
            RequestPoGroupId: group.GroupId,
            SupplierId: group.SupplierId,
            SupplierName: group.SupplierName,
            SupplierNif: group.SupplierNif,
            SupplierTaxId: group.SupplierTaxId,
            GroupStatusCode: group.GroupStatus,
            GroupStatusLabel: GroupStatusLabel(group.GroupStatus),
            OperationalStateLabel: operationalState,
            PurchaseOrderNumber: group.PurchaseOrderNumber,
            CurrencyCode: group.CurrencyCode,
            GroupAmount: group.TotalAmount,
            PaymentId: payment?.Id,
            PaymentType: payment?.PaymentType,
            ScheduledDateUtc: payment?.ScheduledDateUtc,
            PlannedAmount: payment?.PlannedAmount,
            ActualPaidAmount: payment?.ActualPaidAmount,
            PaidDateUtc: payment?.PaidDateUtc,
            HasPaymentProof: payment?.HasProof ?? false,
            FinanceActions: group.FinanceActions,
            ActionClass: actionClass,
            ActionClassLabel: FinanceActionClasses.Label(actionClass),
            NextActionLabel: nextAction,
            ResponsibleRole: ResponsibleFor(actionClass),
            DueDate: dueDate,
            IsOverdue: isOverdue,
            OverdueDays: overdueDays,
            IsDueToday: isDueToday,
            ObligationAmount: obligationAmount);
    }

    /// <summary>PT display label for a group status code (secondary chip on the obligation row).</summary>
    private static string GroupStatusLabel(string status) => status switch
    {
        RequestConstants.Statuses.PoIssued => "P.O Emitida",
        RequestConstants.Statuses.PaymentRequestSent => "Pagamento Solicitado",
        RequestConstants.Statuses.PaymentScheduled => "Pagamento Agendado",
        RequestConstants.Statuses.PaymentCompleted => "Pago",
        RequestConstants.Statuses.AdvancePaymentRequired => "Adiantamento Requerido",
        RequestConstants.Statuses.AdvancePaymentScheduled => "Adiantamento Agendado",
        RequestConstants.Statuses.AdvancePaymentCompleted => "Adiantamento Pago",
        RequestConstants.Statuses.WaitingSupplierDelivery => "Aguardando Entrega",
        RequestConstants.Statuses.WaitingReceipt => "Aguardando Recibo",
        RequestConstants.Statuses.WaitingReconciliation => "Aguardando Reconciliação",
        RequestConstants.Statuses.InFollowup => "Em Acompanhamento",
        RequestConstants.Statuses.WaitingFiscalReceipt => "Aguardando Recibo Fiscal",
        RequestConstants.Statuses.Completed => "Concluído",
        RequestConstants.PoGroupStatuses.WaitingPo => "Aguardando P.O.",
        RequestConstants.Statuses.WaitingPoCorrection => "Devolvido para Compras",
        RequestConstants.PoGroupStatuses.Pending => "Aguardando Ativação",
        _ => status
    };

    // Next action — corporate Finance verbs. Overdue is signalled in the due-date column, so the
    // next-action stays the plain imperative ("Efetuar pagamento").
    private static string? NextActionLabel(string actionClass, string groupStatus, bool advance)
    {
        return actionClass switch
        {
            FinanceActionClasses.NeedsScheduling => advance ? "Agendar adiantamento" : "Agendar pagamento",
            FinanceActionClasses.NeedsPayment => advance ? "Efetuar adiantamento" : "Efetuar pagamento",
            FinanceActionClasses.FiscalDocumentPending => "Anexar recibo fiscal",
            FinanceActionClasses.PaidWaitingReceiving => "Aguardando Recebimento",
            FinanceActionClasses.InReceivingFollowup => "Em recebimento / acompanhamento",
            FinanceActionClasses.Completed => null,
            FinanceActionClasses.NoFinanceAction => groupStatus switch
            {
                RequestConstants.PoGroupStatuses.WaitingPo => "Aguardando emissão da P.O. pelo Comprador",
                RequestConstants.Statuses.WaitingPoCorrection => "Devolvido para correção da P.O.",
                _ => "Sem ação financeira"
            },
            _ => null
        };
    }

    // Operational state — the corporate label for the obligation's current situation (secondary to
    // the next action). Overdue upgrades the payment states to "Pagamento Vencido".
    private static string OperationalStateLabel(string actionClass, string groupStatus, bool advance, bool isOverdue)
    {
        if (isOverdue) return "Pagamento Vencido";
        return actionClass switch
        {
            FinanceActionClasses.NeedsScheduling => advance ? "Adiantamento Pendente" : "Aguardando Agendamento",
            FinanceActionClasses.NeedsPayment => advance ? "Adiantamento Pendente" : "Pagamento Pendente",
            FinanceActionClasses.PaidWaitingReceiving => "Pago / Aguardando Recebimento",
            FinanceActionClasses.FiscalDocumentPending => "Documento Fiscal Pendente",
            FinanceActionClasses.InReceivingFollowup => "Em Recebimento / Acompanhamento",
            FinanceActionClasses.Completed => "Concluído",
            // NO_FINANCE_ACTION: keep the group's own descriptive label (Aguardando P.O. / Devolvido…).
            _ => GroupStatusLabel(groupStatus)
        };
    }
}
