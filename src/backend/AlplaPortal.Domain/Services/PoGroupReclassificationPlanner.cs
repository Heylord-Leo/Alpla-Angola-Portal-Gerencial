using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// One PO group as the grouping-key integrity decision needs to see it: its current key, the
/// resulting key of every active document feeding it (with the proposed edit already applied),
/// and what financial evidence already hangs off it.
///
/// <para>Keys are <see cref="PaymentGroupingKey"/> — the same canonical normalization the group
/// builder uses. No second comparison algorithm exists.</para>
/// </summary>
public sealed record PoGroupReclassificationInput
{
    public Guid GroupId { get; init; }

    /// <summary>The identity the group carries today, built from its own columns.</summary>
    public PaymentGroupingKey CurrentGroupKey { get; init; }

    /// <summary>
    /// The resulting key of every active document contributing lines to this group, with the
    /// proposed edit applied to the document being changed. The planner judges the group by what
    /// its documents say NOW — never by the request header or the old stamp.
    /// </summary>
    public IReadOnlyList<PaymentGroupingKey> ContributingKeys { get; init; } =
        Array.Empty<PaymentGroupingKey>();

    /// <summary>
    /// The operation-invoice lifecycle has started: an allocation, an active short-close, a
    /// reconciliation snapshot, a fiscal receipt or a confirmed operational receipt exists.
    /// Blocks ANY identity change, including a type-only correction (Phase 1c rule).
    /// </summary>
    public bool HasOperationInvoiceActivity { get; init; }

    /// <summary>
    /// Commercial evidence exists downstream of the group's identity: a registered P.O. number,
    /// P.O. attachments, payments or reconciliations. Blocks changes to the COMMERCIAL dimensions
    /// (supplier, currency, payment condition, plant) — a type-only correction is not blocked by
    /// this, because the obligation it re-derives is not what the P.O. documents.
    /// </summary>
    public bool HasCommercialEvidence { get; init; }

    /// <summary>
    /// ExpectedOperationInvoiceTotal was captured. A currency change would falsify that snapshot
    /// (the amount is denominated in the captured currency), so it blocks the change.
    /// </summary>
    public bool HasCapturedExpectedTotal { get; init; }
}

public enum PoGroupReclassificationAction
{
    /// <summary>The documents still resolve to the group's key — nothing to do.</summary>
    NoChange,

    /// <summary>Identity and derived obligations must be re-stamped, same transaction.</summary>
    Restamp,

    /// <summary>The change must be refused. See the reason code.</summary>
    Blocked
}

/// <summary>Typed refusal codes, so callers and the UI branch on codes and never on message text.</summary>
public static class PoGroupReclassificationBlockReasons
{
    /// <summary>
    /// The group's documents would no longer share one grouping key
    /// (Supplier+Currency+PaymentCondition+Plant+SourceDocumentType). Financial groups are never
    /// silently split or rebuilt once the PO workflow may have started. Business restriction
    /// pending a Finance-approved regrouping policy.
    /// </summary>
    public const string GroupingKeyInvalidated = "GROUPING_KEY_INVALIDATED";

    /// <summary>
    /// Operation invoices, a short-close, a reconciliation, a fiscal receipt or an operational
    /// receipt already answer the current obligation. Rewriting the identity underneath that
    /// evidence would discard what Finance already judged.
    /// </summary>
    public const string PostPaymentActivityStarted = "OPERATION_INVOICE_ACTIVITY_STARTED";

    /// <summary>
    /// A registered P.O., P.O. attachments, payments, reconciliations — or a captured expected
    /// total a currency change would falsify — already document the group's commercial identity.
    /// </summary>
    public const string FinancialEvidenceExists = "GROUP_FINANCIAL_EVIDENCE_EXISTS";

    /// <summary>A source document whose lines already sit in a PO group may not be voided/removed.</summary>
    public const string DocumentContributesToGroups = "SOURCE_DOCUMENT_IN_PO_GROUP";
}

/// <summary>The decision for one group. NewKey/Obligations are present only when Action is Restamp.</summary>
public sealed record PoGroupReclassificationDecision
{
    public Guid GroupId { get; init; }
    public PoGroupReclassificationAction Action { get; init; }

    /// <summary>The agreed key every contributor resolves to, when a re-stamp is allowed.</summary>
    public PaymentGroupingKey? NewKey { get; init; }
    public DocumentObligations? Obligations { get; init; }

    /// <summary>Portuguese display names of the key dimensions that changed. For audit/messages.</summary>
    public IReadOnlyList<string> ChangedDimensions { get; init; } = Array.Empty<string>();

    public string? BlockReasonCode { get; init; }
    /// <summary>User-facing refusal, pt-PT.</summary>
    public string? BlockReason { get; init; }
}

/// <summary>
/// Release 4 Phase 1d: decides what a source-document edit that touches any grouping-key
/// dimension means for the PO groups already built over its lines. Generalizes the Phase 1c
/// type-only planner to the full key.
///
/// <para>Pure and side-effect-free. The write path (same transaction as the document change)
/// applies the decisions; this class only judges. Obligations always come from
/// <see cref="DocumentObligationResolver"/>.</para>
///
/// <para>The conservative rules, in order: contributors that would DISAGREE on the key block the
/// change (no silent regrouping); contributors that still agree with the group's key need
/// nothing; a change to a commercial dimension (supplier, currency, condition, plant) is blocked
/// by any financial evidence — a registered P.O., P.O. attachments, payments, reconciliations,
/// operation-invoice activity, or a captured expected total when the currency would change; a
/// type-only change is blocked by operation-invoice activity alone (the Phase 1c rule — a P.O.
/// documents the commercial identity, not the obligation). Only then is a re-stamp allowed.</para>
/// </summary>
public static class PoGroupReclassificationPlanner
{
    private const string MixedKeysReason =
        "A alteração deixaria o grupo de pagamento com documentos de características diferentes " +
        "({0}), invalidando o agrupamento aprovado (fornecedor + moeda + condição de pagamento + " +
        "planta + tipo de documento). Grupos financeiros não são reorganizados automaticamente " +
        "após a aprovação — esta correção requer reconciliação pelo Financeiro.";

    private const string ActivityStartedReason =
        "Este grupo já iniciou o ciclo pós-pagamento (fatura final, recibo fiscal ou recebimento " +
        "operacional registados). A classificação que originou a obrigação não pode ser alterada " +
        "sem decisão do Financeiro.";

    private const string FinancialEvidenceReason =
        "Este grupo já tem evidência financeira registada (P.O., pagamentos, reconciliação ou " +
        "total esperado capturado). A identidade comercial do grupo ({0}) não pode ser reescrita " +
        "sob essa evidência — esta correção requer reconciliação pelo Financeiro.";

    public static IReadOnlyList<PoGroupReclassificationDecision> Plan(
        IEnumerable<PoGroupReclassificationInput> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return groups.Select(PlanGroup).ToList();
    }

    private static PoGroupReclassificationDecision PlanGroup(PoGroupReclassificationInput input)
    {
        // PaymentGroupingKey is a record struct over normalized components, so Distinct() IS the
        // canonical comparison — the same equality the group builder deduplicates by.
        var distinctKeys = input.ContributingKeys.Distinct().ToList();

        // Zero active contributing documents is as undecidable as disagreement: there is nothing
        // left to derive an identity from, so nothing may be silently rewritten.
        if (distinctKeys.Count != 1)
        {
            return new PoGroupReclassificationDecision
            {
                GroupId = input.GroupId,
                Action = PoGroupReclassificationAction.Blocked,
                ChangedDimensions = distinctKeys.Count == 0
                    ? Array.Empty<string>()
                    : DescribeDivergence(distinctKeys),
                BlockReasonCode = PoGroupReclassificationBlockReasons.GroupingKeyInvalidated,
                BlockReason = string.Format(MixedKeysReason, distinctKeys.Count == 0
                    ? "sem documentos ativos"
                    : string.Join(", ", DescribeDivergence(distinctKeys)))
            };
        }

        var agreedKey = distinctKeys[0];
        var changed = ChangedDimensions(input.CurrentGroupKey, agreedKey);

        if (changed.Count == 0)
        {
            return new PoGroupReclassificationDecision
            {
                GroupId = input.GroupId,
                Action = PoGroupReclassificationAction.NoChange
            };
        }

        var commercialChanged = changed.Any(d => d != DimensionNames.SourceDocumentType);
        var currencyChanged = changed.Contains(DimensionNames.Currency);

        if (commercialChanged &&
            (input.HasCommercialEvidence ||
             input.HasOperationInvoiceActivity ||
             (currencyChanged && input.HasCapturedExpectedTotal)))
        {
            return new PoGroupReclassificationDecision
            {
                GroupId = input.GroupId,
                Action = PoGroupReclassificationAction.Blocked,
                ChangedDimensions = changed,
                BlockReasonCode = PoGroupReclassificationBlockReasons.FinancialEvidenceExists,
                BlockReason = string.Format(FinancialEvidenceReason, string.Join(", ", changed))
            };
        }

        if (input.HasOperationInvoiceActivity)
        {
            return new PoGroupReclassificationDecision
            {
                GroupId = input.GroupId,
                Action = PoGroupReclassificationAction.Blocked,
                ChangedDimensions = changed,
                BlockReasonCode = PoGroupReclassificationBlockReasons.PostPaymentActivityStarted,
                BlockReason = ActivityStartedReason
            };
        }

        return new PoGroupReclassificationDecision
        {
            GroupId = input.GroupId,
            Action = PoGroupReclassificationAction.Restamp,
            NewKey = agreedKey,
            ChangedDimensions = changed,
            Obligations = DocumentObligationResolver.Resolve(
                agreedKey.SourceDocumentType, DocumentUsageContext.PaymentRequest)
        };
    }

    /// <summary>Portuguese display names, stable for audit comments and refusal messages.</summary>
    public static class DimensionNames
    {
        public const string Supplier = "Fornecedor";
        public const string Currency = "Moeda";
        public const string PaymentCondition = "Condição de Pagamento";
        public const string Plant = "Planta";
        public const string SourceDocumentType = "Tipo de Documento";
    }

    private static IReadOnlyList<string> ChangedDimensions(PaymentGroupingKey current, PaymentGroupingKey proposed)
    {
        var changed = new List<string>(5);
        if (current.SupplierId != proposed.SupplierId) changed.Add(DimensionNames.Supplier);
        if (!string.Equals(current.CurrencyCode, proposed.CurrencyCode, StringComparison.Ordinal))
            changed.Add(DimensionNames.Currency);
        if (!string.Equals(current.PaymentConditionCode, proposed.PaymentConditionCode, StringComparison.Ordinal))
            changed.Add(DimensionNames.PaymentCondition);
        if (current.PlantId != proposed.PlantId) changed.Add(DimensionNames.Plant);
        if (!string.Equals(current.SourceDocumentType, proposed.SourceDocumentType, StringComparison.Ordinal))
            changed.Add(DimensionNames.SourceDocumentType);
        return changed;
    }

    /// <summary>The dimensions on which the contributing keys disagree among THEMSELVES.</summary>
    private static IReadOnlyList<string> DescribeDivergence(IReadOnlyList<PaymentGroupingKey> keys)
    {
        var changed = new List<string>(5);
        if (keys.Select(k => k.SupplierId).Distinct().Count() > 1) changed.Add(DimensionNames.Supplier);
        if (keys.Select(k => k.CurrencyCode).Distinct(StringComparer.Ordinal).Count() > 1)
            changed.Add(DimensionNames.Currency);
        if (keys.Select(k => k.PaymentConditionCode).Distinct(StringComparer.Ordinal).Count() > 1)
            changed.Add(DimensionNames.PaymentCondition);
        if (keys.Select(k => k.PlantId).Distinct().Count() > 1) changed.Add(DimensionNames.Plant);
        if (keys.Select(k => k.SourceDocumentType).Distinct(StringComparer.Ordinal).Count() > 1)
            changed.Add(DimensionNames.SourceDocumentType);
        return changed;
    }
}
