using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Pure, side-effect-free eligibility rules for cancelling a P.O. group after final approval (phase 1). The single
/// source of truth shared by the endpoint (write) and the Workspace projection (read: "can the Buyer cancel this
/// group, and if not, why"). Mirrors the approved matrix (docs/PO_GROUP_CANCELLATION.md §K.1) exactly:
///
///  • cancellable FROM: WAITING_PO, WAITING_PO_CORRECTION, PO_ISSUED, ADVANCE_PAYMENT_REQUIRED;
///  • PAYMENT_SCHEDULED / ADVANCE_PAYMENT_SCHEDULED → refused: Finance must cancel the schedule or return the group (D2);
///  • any state after a completed payment, any receiving/invoice state, PENDING, CANCELLED → refused;
///  • any affected line RECEIVED / PARTIALLY_RECEIVED or with a received quantity → refused;
///  • any affected line ORDERED → refused, "requires investigation" (D11: never reset automatically);
///  • a COMPLETED payment row, an active operation-invoice allocation or a fiscal receipt on the group → refused;
///  • batch items pointing at the group while their line is not linked to it (broken linkage) → refused.
/// </summary>
public static class PoGroupCancellationEvaluator
{
    public static readonly string[] CancellableGroupStatuses =
    {
        RequestConstants.PoGroupStatuses.WaitingPo,
        RequestConstants.PoGroupStatuses.WaitingPoCorrection,
        RequestConstants.PoGroupStatuses.PoIssued,
        RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
    };

    public static readonly string[] ScheduledPaymentGroupStatuses =
    {
        RequestConstants.PoGroupStatuses.PaymentScheduled,
        RequestConstants.PoGroupStatuses.AdvancePaymentScheduled,
    };

    /// <summary>Line statuses that may be restored to WAITING_QUOTATION on a restart (§B of the plan).</summary>
    public static readonly string[] RestorableLineStatuses = { "WAITING_QUOTATION", "PENDING", "UNDER_REVIEW", "WAITING_ORDER" };

    public static readonly string[] ReceivedLineStatuses = { "RECEIVED", "PARTIALLY_RECEIVED" };

    public sealed record LineFacts(Guid LineItemId, int LineNumber, string? LineItemStatusCode, decimal ReceivedQuantity, bool IsDeleted);
    public sealed record PaymentFacts(string PaymentType, string PaymentStatus);

    public sealed record Input(
        string RequestTypeCode,
        string RequestStatusCode,
        bool RequestIsCancelled,
        string GroupStatus,
        bool GroupAlreadyHasCancellation,
        IReadOnlyList<LineFacts> GroupLines,
        IReadOnlyList<PaymentFacts> GroupPayments,
        bool HasActiveOperationInvoiceAllocation,
        bool HasFiscalReceipt,
        bool HasOperationalReceiptStamp,
        /// <summary>Batch items of the APPROVED batch whose line is NOT linked to this group although the group was
        /// built for it (PROD linkage-repair backlog): refuse rather than guess.</summary>
        bool LinkageInconsistent);

    public sealed record Result(bool CanCancel, string? Code, string? Message)
    {
        public static Result Ok() => new(true, null, null);
        public static Result Refuse(string code, string message) => new(false, code, message);
    }

    public static Result Evaluate(Input i)
    {
        if (i.RequestTypeCode != RequestConstants.Types.Quotation)
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.RequestTypeNotSupported,
                "O cancelamento de grupo P.O aplica-se apenas a pedidos de cotação nesta fase.");

        if (i.RequestIsCancelled || i.RequestStatusCode is "CANCELLED" or "REJECTED" or "COMPLETED")
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.RequestTerminal,
                "O pedido está num estado final; nenhum grupo P.O pode ser alterado.");

        if (i.GroupAlreadyHasCancellation || i.GroupStatus == RequestConstants.PoGroupStatuses.Cancelled)
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.GroupAlreadyCancelled,
                "Este grupo P.O já foi cancelado.");

        if (ScheduledPaymentGroupStatuses.Contains(i.GroupStatus))
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.PaymentScheduledActive,
                "Existe um pagamento agendado para este grupo. Finanças deve cancelar o agendamento ou devolver o grupo antes do cancelamento.");

        if (i.GroupStatus == RequestConstants.PoGroupStatuses.Pending)
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.GroupStatusNotCancellable,
                "O grupo ainda não foi aprovado na Aprovação Final. Utilize a rejeição ou o reajuste do lote.");

        if (!CancellableGroupStatuses.Contains(i.GroupStatus))
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.PaymentOrReceiptEvidence,
                $"O grupo está em {i.GroupStatus}: já existe pagamento executado ou receção em curso. O cancelamento após pagamento está fora do âmbito desta fase.");

        if (i.GroupPayments.Any(p => p.PaymentStatus == "COMPLETED"))
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.PaymentOrReceiptEvidence,
                "Existe um pagamento concluído para este grupo; não pode ser cancelado no Portal.");

        if (i.GroupPayments.Any(p => p.PaymentStatus == "SCHEDULED"))
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.PaymentScheduledActive,
                "Existe um pagamento agendado para este grupo. Finanças deve cancelar o agendamento antes do cancelamento.");

        if (i.HasActiveOperationInvoiceAllocation || i.HasFiscalReceipt || i.HasOperationalReceiptStamp)
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.PaymentOrReceiptEvidence,
                "O grupo já tem fatura de operação, recibo fiscal ou receção registada; utilize os fluxos de receção/faturação.");

        if (i.LinkageInconsistent)
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.GroupLinkageInconsistent,
                "A ligação entre os itens e este grupo P.O está inconsistente (reparação de dados pendente). O cancelamento foi recusado para não alterar itens errados.");

        var live = i.GroupLines.Where(l => !l.IsDeleted).ToList();
        var received = live.Where(l => ReceivedLineStatuses.Contains(l.LineItemStatusCode ?? string.Empty) || l.ReceivedQuantity > 0m).ToList();
        if (received.Count > 0)
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.PaymentOrReceiptEvidence,
                $"Os itens {Lines(received)} já têm receção registada; o grupo não pode ser cancelado.");

        var ordered = live.Where(l => string.Equals(l.LineItemStatusCode, "ORDERED", StringComparison.Ordinal)).ToList();
        if (ordered.Count > 0)
            return Result.Refuse(PoGroupCancellationConstants.ErrorCodes.OrderedLineRequiresInvestigation,
                $"Os itens {Lines(ordered)} estão marcados como \"Encomendado\" (ORDERED). Este caso requer investigação: confirme com o Recebimento se a mercadoria foi ou não recebida antes de qualquer cancelamento. O estado dos itens não é alterado automaticamente.");

        return Result.Ok();
    }

    /// <summary>Whether a line status may be restored to WAITING_QUOTATION on restart (never ORDERED/RECEIVED — those refuse above).</summary>
    public static bool IsRestorableLineStatus(string? code) =>
        string.IsNullOrEmpty(code) || RestorableLineStatuses.Contains(code);

    /// <summary>D4 §D.1 — Finance confirmation is required when the group EVER had a registered P.O.</summary>
    public static bool RequiresFinanceConfirmation(string? purchaseOrderNumber, bool hasAnyPoAttachment, bool hasRegisterPoHistory) =>
        !string.IsNullOrWhiteSpace(purchaseOrderNumber) || hasAnyPoAttachment || hasRegisterPoHistory;

    private static string Lines(IEnumerable<LineFacts> lines) =>
        string.Join(", ", lines.OrderBy(l => l.LineNumber).Select(l => $"#{l.LineNumber}"));
}
