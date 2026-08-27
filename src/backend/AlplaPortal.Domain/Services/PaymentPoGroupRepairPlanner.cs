namespace AlplaPortal.Domain.Services;

/// <summary>
/// Pure, static, side-effect-free classifier for the historical PAYMENT PO-group repair
/// (Phase 4B.2). Decides whether an APPROVED payment request that is MISSING its
/// <c>RequestPoGroup</c>(s) may be safely repaired by re-running the canonical PAYMENT group
/// builder, or must be left for a human.
///
/// <para>Never persists, never mutates, never used for permissions. It only classifies a set of
/// already-gathered facts, so the same verdict can be asserted in tests without a database and shown
/// verbatim in a dry-run. The actual grouping is still produced exclusively by
/// <c>BuildPaymentPoGroupsAsync</c> — this planner decides IF, never HOW.</para>
///
/// <para>Scalar policy is fixed by product decision OPTION B: a repaired PAYMENT keeps
/// <c>Request.Status = APPROVED</c>; the operational truth (<c>WAITING_PO</c>) lives on the group
/// and surfaces through the display projection. This planner therefore never proposes a status
/// change.</para>
/// </summary>
public static class PaymentPoGroupRepairPlanner
{
    public enum Classification { SafeToRepair, ManualReview, Skip }

    /// <summary>How the missing groups would be rebuilt, mirroring BuildPaymentPoGroupsAsync's fork.</summary>
    public enum Model { MultiDocument, LegacyHeader, Ambiguous }

    public sealed record Input(
        string RequestTypeCode,
        string RequestStatusCode,
        bool FinalApprovalCompleted,
        int ExistingGroupCount,
        int SourceDocumentCount,
        int ActiveLineItemCount,
        int LineItemsLinkedToDocumentsCount,
        bool HasSupplierSource,
        bool HasDownstreamEvidence);

    public sealed record Assessment(
        Classification Verdict,
        Model Model,
        string Reason);

    // Kept in one place so the endpoint and the tests read the same vocabulary.
    private const string Payment = "PAYMENT";
    private const string Approved = "APPROVED";

    public static Assessment Assess(Input i)
    {
        // H — only PAYMENT is ever touched. QUOTATION uses the award/GroupBuilder path.
        if (!string.Equals(i.RequestTypeCode, Payment, System.StringComparison.Ordinal))
            return new Assessment(Classification.Skip, Model.Ambiguous,
                "Não é um pedido de pagamento — fora do âmbito desta reparação.");

        // G — only a live APPROVED request qualifies. APPROVED already excludes CANCELLED / REJECTED
        // / COMPLETED, so a scalar check is sufficient and keeps terminal rows untouched.
        if (!string.Equals(i.RequestStatusCode, Approved, System.StringComparison.Ordinal))
            return new Assessment(Classification.Skip, Model.Ambiguous,
                $"Estado '{i.RequestStatusCode}' não é elegível — apenas pagamentos APPROVED sem grupos.");

        if (!i.FinalApprovalCompleted)
            return new Assessment(Classification.Skip, Model.Ambiguous,
                "Aprovação final não registada — nada a reparar antes da aprovação.");

        // D — already has groups: the normal path already produced them. No-op, never a duplicate.
        if (i.ExistingGroupCount > 0)
            return new Assessment(Classification.Skip, Model.Ambiguous,
                "Já possui grupo(s) de P.O. — nenhuma reparação necessária.");

        // §10 / E — downstream artefacts without any group is an anomaly a blind rebuild could
        // corrupt (a P.O., a payment, a receipt that no group explains). Never auto-repaired.
        if (i.HasDownstreamEvidence)
            return new Assessment(Classification.ManualReview, Model.Ambiguous,
                "Existe evidência downstream (P.O./pagamento/recibo) sem grupos — revisão manual obrigatória.");

        // A / F — multi-document model: the plan is document-driven and needs items linked to the
        // documents. Documents present but no linked items is an inconsistent topology, not a repair.
        if (i.SourceDocumentCount > 0)
        {
            if (i.LineItemsLinkedToDocumentsCount == 0)
                return new Assessment(Classification.ManualReview, Model.MultiDocument,
                    "Documentos de origem sem itens ligados — topologia inconsistente, revisão manual.");

            return new Assessment(Classification.SafeToRepair, Model.MultiDocument,
                "Pagamento multi-documento com itens ligados — reparável pelo construtor canónico.");
        }

        // B — legacy/header model: one group from the request header. It must have a supplier, or the
        // rebuilt group could never receive a P.O. (RegisterPo blocks a supplier-less group).
        if (!i.HasSupplierSource)
            return new Assessment(Classification.ManualReview, Model.LegacyHeader,
                "Pagamento legado sem fornecedor no cabeçalho — revisão manual (grupo não emitiria P.O.).");

        return new Assessment(Classification.SafeToRepair, Model.LegacyHeader,
            "Pagamento legado (cabeçalho) com fornecedor — reparável pela via legada canónica.");
    }
}
