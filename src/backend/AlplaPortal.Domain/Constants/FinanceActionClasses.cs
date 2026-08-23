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

/// <summary>Responsible-role labels used by the Finance obligation projection (PT, display only).</summary>
public static class FinanceResponsibleRoles
{
    public const string Finance = "Financeiro";
    public const string Receiving = "Recebimento";
    public const string Buyer = "Comprador";
    public const string None = "—";
}
