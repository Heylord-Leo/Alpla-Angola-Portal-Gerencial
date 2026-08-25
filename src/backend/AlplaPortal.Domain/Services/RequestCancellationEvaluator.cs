using System.Linq;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Pure, centralized cancellation eligibility — the single source of truth shared by
/// RequestsController.CancelRequest and BuyerQueueProjectionBuilder (canCancel/cancelBlockReason).
/// Mirrors the exact production rules confirmed in Phase 0; this refactor MUST NOT relax them.
///
/// QUOTATION: cancellable only from DRAFT/WAITING_QUOTATION; a WAITING_QUOTATION request is blocked
/// once Buyer processing has started (request supplier, a PROFORMA/QUOTATION attachment, or any line
/// item that already has a supplier or a LineItemStatus beyond WAITING_QUOTATION/PENDING). In BUYER
/// mode the Buyer may cancel only WAITING_QUOTATION (never DRAFT). PAYMENT: Buyers may never cancel;
/// others may cancel in the pre-operational statuses while no PO/schedule/proof attachment exists.
/// </summary>
public static class RequestCancellationEvaluator
{
    public static readonly string[] LineItemQuotationOpenStatuses = { "WAITING_QUOTATION", "PENDING" };
    private static readonly string[] PaymentCancellableStatuses =
        { "DRAFT", "WAITING_AREA_APPROVAL", "AREA_ADJUSTMENT", "WAITING_FINAL_APPROVAL", "FINAL_ADJUSTMENT", "WAITING_COST_CENTER", "APPROVED" };

    public sealed record Input(
        string RequestTypeCode,
        string StatusCode,
        bool IsCancelled,
        bool ActorIsBuyerMode,
        // QUOTATION signals
        bool RequestHasSupplier,
        bool HasProformaOrQuotationAttachment,
        bool AnyLineItemProcessed,
        // PAYMENT signals
        bool HasPaymentOperationalAttachment);

    public sealed record Result(bool CanCancel, string? BlockReason, string? BlockCode);

    public static Result Evaluate(Input i)
    {
        // Terminal states dominate.
        if (i.IsCancelled || i.StatusCode is "CANCELLED" or "COMPLETED" or "REJECTED")
            return Block("O pedido já está num estado final e não pode ser cancelado.", "TERMINAL");

        var isQuotation = i.RequestTypeCode == "QUOTATION";
        var isPayment = i.RequestTypeCode == "PAYMENT";

        if (isQuotation)
        {
            if (i.StatusCode != "DRAFT" && i.StatusCode != "WAITING_QUOTATION")
                return Block("Apenas pedidos em rascunho ou aguardando cotação podem ser cancelados.", "STATUS_NOT_CANCELLABLE");

            if (i.StatusCode == "WAITING_QUOTATION"
                && (i.RequestHasSupplier || i.HasProformaOrQuotationAttachment || i.AnyLineItemProcessed))
                return Block("O pedido já foi processado pelo comprador (fornecedor definido, proforma anexada ou itens atualizados) e não pode ser cancelado.", "BUYER_PROCESSING_STARTED");

            if (i.ActorIsBuyerMode && i.StatusCode != "WAITING_QUOTATION")
                return Block("O comprador só pode cancelar pedidos neste momento que estejam aguardando cotação.", "BUYER_DRAFT_NOT_ALLOWED");

            return Ok();
        }

        if (isPayment)
        {
            if (i.ActorIsBuyerMode)
                return Block("O comprador não tem permissão para cancelar pedidos de pagamento.", "BUYER_PAYMENT_FORBIDDEN");
            if (!PaymentCancellableStatuses.Contains(i.StatusCode))
                return Block("O pedido de pagamento já avançou para processamento operacional e não pode ser cancelado.", "PAYMENT_OPERATIONAL");
            if (i.HasPaymentOperationalAttachment)
                return Block("O pedido possui evidências de processamento operacional (documentos anexados) e não pode ser cancelado.", "PAYMENT_EVIDENCE");
            return Ok();
        }

        return Ok();
    }

    private static Result Ok() => new(true, null, null);
    private static Result Block(string reason, string code) => new(false, reason, code);
}
