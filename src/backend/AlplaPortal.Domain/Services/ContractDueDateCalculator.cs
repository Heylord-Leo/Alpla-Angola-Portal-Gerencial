using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Pure static service that calculates payment due dates, grace dates, and penalty start dates
/// from a contract's structured payment rule.
///
/// FIXED_DAY_OF_MONTH interpretation (DEC-117):
///   If the reference date is on or before the fixed day in the same month → due date = fixed day in that same month.
///   If the reference date is after the fixed day in that month → due date = fixed day in the following month.
///   Example: fixed day = 10, reference = April 3  → due date = April 10.
///            fixed day = 10, reference = April 12 → due date = May 10.
///   Capped at day 28 to avoid month-end issues (Feb, etc.).
///
/// ADVANCE_PAYMENT: No auto-calculation. Treated identically to MANUAL — returns null.
///   The UI must clearly communicate that due date must be set manually.
///
/// Monetary penalty/interest calculation is intentionally out of scope for this phase.
/// These methods only derive dates for tracking purposes.
/// </summary>
public static class ContractDueDateCalculator
{
    // ─── Public API ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to calculate the due date for an obligation given the contract's payment rule
    /// and a resolved reference date.
    /// Returns null when the term type is MANUAL, ADVANCE_PAYMENT, CUSTOM_TEXT,
    /// or when required configuration is missing.
    /// </summary>
    public static DateTime? CalculateDueDate(Contract contract, DateTime referenceDate)
    {
        if (string.IsNullOrEmpty(contract.PaymentTermTypeCode))
            return null;

        var refDate = referenceDate.Date; // normalize to date only

        return contract.PaymentTermTypeCode switch
        {
            ContractConstants.PaymentTermTypes.FixedDaysAfterReference => CalculateFixedDaysAfterReference(contract, refDate),
            ContractConstants.PaymentTermTypes.FixedDayOfMonth        => CalculateFixedDayOfMonth(contract, refDate),
            ContractConstants.PaymentTermTypes.NextMonthFixedDay       => CalculateNextMonthFixedDay(contract, refDate),
            ContractConstants.PaymentTermTypes.OnReceipt               => refDate,
            // ADVANCE_PAYMENT: user must set manually — no auto-calc
            ContractConstants.PaymentTermTypes.AdvancePayment          => null,
            // MANUAL: user provides per-obligation — no auto-calc
            ContractConstants.PaymentTermTypes.Manual                  => null,
            // CUSTOM_TEXT: non-modelable rule — no auto-calc
            ContractConstants.PaymentTermTypes.CustomText              => null,
            _                                                          => null
        };
    }

    /// <summary>
    /// Calculates the grace date from the effective due date and the contract's grace period.
    /// GraceDate = DueDate + GracePeriodDays (or DueDate when no grace period configured).
    /// </summary>
    public static DateTime CalculateGraceDate(DateTime dueDate, int? gracePeriodDays)
    {
        var grace = gracePeriodDays ?? 0;
        return grace > 0 ? dueDate.Date.AddDays(grace) : dueDate.Date;
    }

    /// <summary>
    /// Calculates the first day on which late penalty/interest begins accruing.
    /// PenaltyStartDate = GraceDate + 1 day.
    /// </summary>
    public static DateTime CalculatePenaltyStartDate(DateTime graceDate)
        => graceDate.Date.AddDays(1);

    /// <summary>
    /// Determines the reference date to use for obligation creation based on the contract's
    /// ReferenceEventTypeCode and data available from the DTO.
    /// Returns null (with an error reason) if the reference event requires data that was not provided.
    /// </summary>
    public static (DateTime? ReferenceDate, string? ErrorReason) ResolveReferenceDate(
        Contract contract,
        DateTime creationDateUtc,
        DateTime? manualReferenceDateUtc,
        DateTime? invoiceReceivedDateUtc)
    {
        // For term types that do not require a reference event, no resolution needed
        var noRefRequired = contract.PaymentTermTypeCode is
            ContractConstants.PaymentTermTypes.Manual or
            ContractConstants.PaymentTermTypes.AdvancePayment or
            ContractConstants.PaymentTermTypes.CustomText or
            null;

        if (noRefRequired)
            return (creationDateUtc, null);

        return contract.ReferenceEventTypeCode switch
        {
            ContractConstants.ReferenceEventTypes.ObligationCreationDate =>
                (creationDateUtc, null),

            ContractConstants.ReferenceEventTypes.ContractSignDate =>
                contract.SignedAtUtc.HasValue
                    ? (contract.SignedAtUtc.Value, null)
                    : (null, "O contrato não possui data de assinatura registada. Por favor, actualize o contrato com a data de assinatura antes de criar esta obrigação."),

            ContractConstants.ReferenceEventTypes.InvoiceReceivedDate =>
                invoiceReceivedDateUtc.HasValue
                    ? (invoiceReceivedDateUtc.Value, null)
                    : (null, "A regra de pagamento deste contrato exige a data de recebimento da fatura. Por favor, informe a data da fatura para calcular o vencimento."),

            ContractConstants.ReferenceEventTypes.ManualReferenceDate =>
                manualReferenceDateUtc.HasValue
                    ? (manualReferenceDateUtc.Value, null)
                    : (null, "A regra de pagamento deste contrato exige uma data de referência manual. Por favor, informe a data de referência para calcular o vencimento."),

            // Future events (not yet surfaced in UI) — fall back to creation date with no error
            ContractConstants.ReferenceEventTypes.ServiceAcceptanceDate =>
                (creationDateUtc, null),

            ContractConstants.ReferenceEventTypes.DeliveryDate =>
                (creationDateUtc, null),

            // No reference event type configured — no auto-calc possible
            _ => (null, null)
        };
    }

    /// <summary>
    /// Builds a human-readable Portuguese summary of the contract's payment rule.
    /// For structured rule types: auto-generated.
    /// For CUSTOM_TEXT: returns null (caller/user must provide text in PaymentRuleSummary).
    /// </summary>
    public static string? BuildPaymentRuleSummary(Contract contract)
    {
        if (string.IsNullOrEmpty(contract.PaymentTermTypeCode))
            return null;

        var refEventLabel = GetReferenceEventLabel(contract.ReferenceEventTypeCode);
        var graceText = contract.GracePeriodDays > 0
            ? $" Tolerância de {contract.GracePeriodDays} dia(s)."
            : string.Empty;
        var penaltyText = contract.HasLatePenalty && contract.LatePenaltyValue.HasValue
            ? $" Multa de {contract.LatePenaltyValue:0.##}{(contract.LatePenaltyTypeCode == ContractConstants.LatePenaltyTypes.Percentage ? "%" : " (valor fixo)")} após o prazo."
            : string.Empty;

        return contract.PaymentTermTypeCode switch
        {
            ContractConstants.PaymentTermTypes.FixedDaysAfterReference =>
                $"Pagamento em {contract.PaymentTermDays ?? 0} dia(s) após {refEventLabel}.{graceText}{penaltyText}",

            ContractConstants.PaymentTermTypes.FixedDayOfMonth =>
                $"Pagamento no dia {contract.PaymentFixedDay ?? 0} do mês (referência: {refEventLabel}).{graceText}{penaltyText}",

            ContractConstants.PaymentTermTypes.NextMonthFixedDay =>
                $"Pagamento no dia {contract.PaymentFixedDay ?? 0} do mês seguinte à {refEventLabel}.{graceText}{penaltyText}",

            ContractConstants.PaymentTermTypes.OnReceipt =>
                $"Pagamento imediato na {refEventLabel}.{graceText}{penaltyText}",

            ContractConstants.PaymentTermTypes.AdvancePayment =>
                $"Pagamento antecipado. Vencimento definido manualmente por parcela.{graceText}{penaltyText}",

            ContractConstants.PaymentTermTypes.Manual =>
                $"Vencimento definido manualmente por parcela.{penaltyText}",

            ContractConstants.PaymentTermTypes.CustomText =>
                // Caller must use the user-authored PaymentRuleSummary field directly
                null,

            _ => null
        };
    }

    // ─── Private Calculation Helpers ─────────────────────────────────────────────────

    private static DateTime? CalculateFixedDaysAfterReference(Contract contract, DateTime refDate)
    {
        if (!contract.PaymentTermDays.HasValue || contract.PaymentTermDays <= 0)
            return null;
        return refDate.AddDays(contract.PaymentTermDays.Value);
    }

    private static DateTime? CalculateFixedDayOfMonth(Contract contract, DateTime refDate)
    {
        // Cap to day 28 to safely handle February and short months
        var day = Math.Min(contract.PaymentFixedDay ?? 0, 28);
        if (day <= 0) return null;

        // If reference date is on or before the fixed day this month → use this month
        if (refDate.Day <= day)
            return new DateTime(refDate.Year, refDate.Month, day, 0, 0, 0, DateTimeKind.Utc);

        // Otherwise → use next month
        var next = refDate.AddMonths(1);
        return new DateTime(next.Year, next.Month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    private static DateTime? CalculateNextMonthFixedDay(Contract contract, DateTime refDate)
    {
        // Cap to day 28. Due date = fixed day of the month following the reference month.
        var day = Math.Min(contract.PaymentFixedDay ?? 0, 28);
        if (day <= 0) return null;

        var next = refDate.AddMonths(1);
        return new DateTime(next.Year, next.Month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    private static string GetReferenceEventLabel(string? eventTypeCode) =>
        eventTypeCode switch
        {
            ContractConstants.ReferenceEventTypes.ObligationCreationDate => "criação da obrigação",
            ContractConstants.ReferenceEventTypes.ContractSignDate        => "assinatura do contrato",
            ContractConstants.ReferenceEventTypes.InvoiceReceivedDate     => "recebimento da fatura",
            ContractConstants.ReferenceEventTypes.ManualReferenceDate     => "data de referência informada",
            ContractConstants.ReferenceEventTypes.ServiceAcceptanceDate   => "aceite do serviço",
            ContractConstants.ReferenceEventTypes.DeliveryDate            => "entrega",
            _                                                             => "evento de referência"
        };
}
