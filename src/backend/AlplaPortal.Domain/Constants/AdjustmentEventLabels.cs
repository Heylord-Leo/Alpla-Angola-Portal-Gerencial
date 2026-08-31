using System.Collections.Generic;

namespace AlplaPortal.Domain.Constants;

/// <summary>
/// Adjustment V2 — the single shared catalog of user-facing Portuguese business labels for the
/// structured adjustment cycle. Introduced in Phase 3 (moved forward from Phase 7 per the approved
/// design §16) so the notification code and the future timeline / approver-summary read models
/// consume ONE source of truth instead of duplicating strings.
///
/// <para>These are friendly business labels — never the raw <see cref="AdjustmentConstants"/> codes,
/// which must not leak to users.</para>
/// </summary>
public static class AdjustmentEventLabels
{
    // ── Cycle event labels (design §16 catalog) ──
    public const string RequestedAtArea = "Reajuste solicitado na Aprovação de Área";
    public const string RequestedAtFinal = "Reajuste solicitado na Aprovação Final";
    public const string ActionRequiredRequester = "Ação necessária do Solicitante";
    public const string ActionRequiredBuyer = "Ação necessária do Comprador";
    public const string RequesterCorrectionCompleted = "Correção concluída pelo Solicitante";
    public const string BuyerReviewCompleted = "Revisão comercial concluída pelo Comprador";
    public const string ResubmittedToArea = "Lote reenviado para Aprovação de Área";
    public const string ReturnedToFinal = "Lote retornou à Aprovação Final";
    public const string Cancelled = "Reajuste cancelado";

    // ── Return-through-Area note for Final-sourced cycles (design §3/§10) ──
    public const string FinalReturnsViaAreaNote = "Após correção, o lote retorna primeiro à Aprovação da Área.";

    /// <summary>Friendly Portuguese label for one <see cref="AdjustmentConstants.ReasonCodes"/> value.
    /// Canonical wording from the approved design §2 "Label (PT)" column.</summary>
    private static readonly IReadOnlyDictionary<string, string> ReasonLabelMap = new Dictionary<string, string>
    {
        // Buyer-owned
        [AdjustmentConstants.ReasonCodes.PriceNegotiation] = "Preço / negociação",
        [AdjustmentConstants.ReasonCodes.NewQuotation] = "Solicitar nova cotação",
        [AdjustmentConstants.ReasonCodes.Supplier] = "Fornecedor",
        [AdjustmentConstants.ReasonCodes.SupplierDeliveryTime] = "Prazo de entrega do fornecedor",
        [AdjustmentConstants.ReasonCodes.PaymentTerms] = "Condição de pagamento",
        [AdjustmentConstants.ReasonCodes.Documentation] = "Documentação / Proforma",
        [AdjustmentConstants.ReasonCodes.BatchComposition] = "Composição do lote",
        [AdjustmentConstants.ReasonCodes.ExtraQuotationItem] = "Item adicional da cotação",
        [AdjustmentConstants.ReasonCodes.Other] = "Outro",
        // Requester-first
        [AdjustmentConstants.ReasonCodes.RequestedQuantity] = "Quantidade solicitada",
        [AdjustmentConstants.ReasonCodes.Specification] = "Descrição / especificação",
        [AdjustmentConstants.ReasonCodes.RequestedUnit] = "Unidade de medida",
        [AdjustmentConstants.ReasonCodes.NeededByDate] = "Data necessária",
        [AdjustmentConstants.ReasonCodes.MissingItem] = "Item faltante no pedido",
        [AdjustmentConstants.ReasonCodes.RemoveRequestItem] = "Remover item do pedido",
    };

    /// <summary>Friendly label for a reason code, or the code itself if somehow unmapped
    /// (defensive — every catalog code has a label).</summary>
    public static string ReasonLabel(string reasonCode) =>
        ReasonLabelMap.TryGetValue(reasonCode, out var label) ? label : reasonCode;

    /// <summary>The cycle "requested" event label for a source stage
    /// (<see cref="AdjustmentConstants.SourceStages"/>).</summary>
    public static string RequestedAt(string sourceStage) =>
        sourceStage == AdjustmentConstants.SourceStages.Final ? RequestedAtFinal : RequestedAtArea;
}
