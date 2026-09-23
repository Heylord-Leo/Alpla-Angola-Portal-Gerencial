using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Domain.Constants;

/// <summary>
/// Canonical Finance work classes for a single RequestPoGroup obligation. These are a DISPLAY /
/// triage taxonomy derived deterministically from the group's existing lifecycle status — never a
/// new workflow state machine. The authoritative status stays on RequestPoGroup.Status; this only
/// answers "what kind of Finance work, if any, does this obligation represent right now".
/// </summary>
public static class FinanceActionClasses
{
    public const string NeedsScheduling = "NEEDS_SCHEDULING";
    public const string NeedsPayment = "NEEDS_PAYMENT";
    public const string PaidWaitingReceiving = "PAID_WAITING_RECEIVING";
    public const string InReceivingFollowup = "IN_RECEIVING_FOLLOWUP";
    public const string FiscalDocumentPending = "FISCAL_DOCUMENT_PENDING";
    public const string Completed = "COMPLETED";
    public const string NoFinanceAction = "NO_FINANCE_ACTION";

    /// <summary>PT label shown on the work-queue cards / row grouping (corporate Finance terminology).</summary>
    public static string Label(string actionClass) => actionClass switch
    {
        NeedsScheduling => "Aguardando Agendamento",
        NeedsPayment => "Pagamento Pendente",
        PaidWaitingReceiving => "Pagos / Aguardando Recebimento",
        InReceivingFollowup => "Em Recebimento / Acompanhamento",
        FiscalDocumentPending => "Documento Fiscal Pendente",
        Completed => "Concluído",
        NoFinanceAction => "Sem Ação Financeira",
        _ => actionClass
    };

    /// <summary>True when Finance is the responsible actor for this class (drives the "actionable" filters/cards).</summary>
    public static bool IsFinanceActionable(string actionClass) =>
        actionClass is NeedsScheduling or NeedsPayment or FiscalDocumentPending;
}

/// <summary>
/// v2.245.11 — which execution flow settles a group's open payment obligation. ADVANCE obligations are
/// completed ONLY through the dedicated advance endpoints (b2p/schedule-advance, b2p/confirm-advance);
/// STANDARD obligations through FinanceController.SchedulePayment / MarkAsPaid. Server-authoritative:
/// exposed on the Finance obligation DTO so the client never infers the route from labels, and
/// enforced by MarkAsPaid so a direct API call cannot settle an advance through the normal endpoint.
/// </summary>
public static class FinancePaymentFlows
{
    public const string Advance = "ADVANCE";
    public const string Standard = "STANDARD";

    /// <summary>
    /// The single rule (used by the obligation projection AND the MarkAsPaid guard). A group's open
    /// obligation is an ADVANCE when the group sits in an advance-pending status, or when it carries a
    /// SCHEDULED advance payment row (an open, scheduled advance is unambiguous even if the group's
    /// status drifted). A PLANNED advance on a non-advance group status does not change the flow, and a
    /// COMPLETED / CANCELLED advance never does (the group has moved on to delivery / final balance).
    /// </summary>
    public static bool IsAdvance(string? groupStatus, IEnumerable<(string PaymentType, string PaymentStatus)> payments)
    {
        if (groupStatus is RequestConstants.Statuses.AdvancePaymentRequired
            or RequestConstants.Statuses.AdvancePaymentScheduled)
            return true;
        return payments.Any(p => p.PaymentType == "ADVANCE" && p.PaymentStatus == "SCHEDULED");
    }

    public static string Resolve(string? groupStatus, IEnumerable<(string PaymentType, string PaymentStatus)> payments) =>
        IsAdvance(groupStatus, payments) ? Advance : Standard;
}

/// <summary>Responsible-role labels used by the Finance obligation projection (PT, display only).</summary>
public static class FinanceResponsibleRoles
{
    public const string Finance = "Financeiro";
    public const string Receiving = "Recebimento";
    public const string Buyer = "Comprador";
    public const string None = "—";
}
