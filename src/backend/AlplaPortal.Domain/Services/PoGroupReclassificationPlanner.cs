using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// One PO group as the reclassification decision needs to see it: its current identity, the
/// current document types of everything that feeds it (with the proposed change already applied),
/// and whether its post-payment lifecycle has started.
/// </summary>
public sealed record PoGroupReclassificationInput
{
    public Guid GroupId { get; init; }

    /// <summary>The identity stamped on the group today. Null on never-classified groups.</summary>
    public string? CurrentSourceDocumentType { get; init; }

    /// <summary>
    /// The CURRENT source-document types of every active document contributing lines to this
    /// group, with the proposed change applied to the document being edited. The planner judges
    /// the group by what its documents say NOW — never by the request header or the old stamp.
    /// </summary>
    public IReadOnlyList<string?> ContributingDocumentTypes { get; init; } = Array.Empty<string?>();

    /// <summary>An allocation, fiscal receipt or operational receipt already exists.</summary>
    public bool HasPostPaymentActivity { get; init; }
}

public enum PoGroupReclassificationAction
{
    /// <summary>The documents still agree with the stamped identity — nothing to do.</summary>
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
    /// The group's documents would no longer share one type: the grouping key
    /// (Supplier+Currency+PaymentCondition+Plant+SourceDocumentType) is invalidated, and financial
    /// groups are never silently rebuilt once the PO workflow may have started. Business
    /// restriction pending a Finance-approved regrouping policy.
    /// </summary>
    public const string MixedDocumentTypes = "GROUPING_KEY_INVALIDATED";

    /// <summary>
    /// Operation invoices, a fiscal receipt or an operational receipt already answer the current
    /// obligation. Rewriting the identity underneath that evidence would discard what Finance
    /// already judged — same rule as GroupBuilderService's winner-replacement guard.
    /// </summary>
    public const string PostPaymentActivityStarted = "OPERATION_INVOICE_ACTIVITY_STARTED";

    /// <summary>A source document whose lines already sit in a PO group may not be voided/removed.</summary>
    public const string DocumentContributesToGroups = "SOURCE_DOCUMENT_IN_PO_GROUP";
}

/// <summary>The decision for one group. Obligations are present only when Action is Restamp.</summary>
public sealed record PoGroupReclassificationDecision
{
    public Guid GroupId { get; init; }
    public PoGroupReclassificationAction Action { get; init; }

    public string? NewSourceDocumentType { get; init; }
    public DocumentObligations? Obligations { get; init; }

    public string? BlockReasonCode { get; init; }
    /// <summary>User-facing refusal, pt-PT.</summary>
    public string? BlockReason { get; init; }
}

/// <summary>
/// Release 4 Phase 1c: decides what a source-document reclassification means for the PO groups
/// that already exist over its lines.
///
/// <para>Pure and side-effect-free. The write path (same transaction as the classification
/// change) applies the decisions; this class only judges. Obligations always come from
/// <see cref="DocumentObligationResolver"/> — never assembled here.</para>
///
/// <para>The conservative rules, in order: a group whose documents would DISAGREE on type is a
/// broken grouping key and blocks the change (no silent regrouping of financial groups); a group
/// whose documents still agree with its stamp needs nothing; a group whose post-payment lifecycle
/// has started blocks the change (evidence already answers the old obligation); otherwise the
/// group is re-stamped from the agreed identity.</para>
/// </summary>
public static class PoGroupReclassificationPlanner
{
    private const string MixedTypesReason =
        "A alteração do tipo de documento deixaria o grupo de pagamento com documentos de tipos " +
        "diferentes, invalidando o agrupamento aprovado. Grupos financeiros não são reorganizados " +
        "automaticamente após a aprovação — esta correção requer reconciliação pelo Financeiro.";

    private const string ActivityStartedReason =
        "Este grupo já iniciou o ciclo pós-pagamento (fatura final, recibo fiscal ou recebimento " +
        "operacional registados). A classificação que originou a obrigação não pode ser alterada " +
        "sem decisão do Financeiro.";

    public static IReadOnlyList<PoGroupReclassificationDecision> Plan(
        IEnumerable<PoGroupReclassificationInput> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return groups.Select(PlanGroup).ToList();
    }

    private static PoGroupReclassificationDecision PlanGroup(PoGroupReclassificationInput input)
    {
        var distinctTypes = input.ContributingDocumentTypes
            .Select(RequestConstants.SourceDocumentTypes.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Zero active contributing documents is as undecidable as disagreement: there is nothing
        // left to derive an identity from, so nothing may be silently rewritten.
        if (distinctTypes.Count != 1)
        {
            return new PoGroupReclassificationDecision
            {
                GroupId = input.GroupId,
                Action = PoGroupReclassificationAction.Blocked,
                BlockReasonCode = PoGroupReclassificationBlockReasons.MixedDocumentTypes,
                BlockReason = MixedTypesReason
            };
        }

        var agreedType = distinctTypes[0];
        var currentType = RequestConstants.SourceDocumentTypes.Normalize(input.CurrentSourceDocumentType);

        if (string.Equals(agreedType, currentType, StringComparison.OrdinalIgnoreCase))
        {
            return new PoGroupReclassificationDecision
            {
                GroupId = input.GroupId,
                Action = PoGroupReclassificationAction.NoChange
            };
        }

        if (input.HasPostPaymentActivity)
        {
            return new PoGroupReclassificationDecision
            {
                GroupId = input.GroupId,
                Action = PoGroupReclassificationAction.Blocked,
                BlockReasonCode = PoGroupReclassificationBlockReasons.PostPaymentActivityStarted,
                BlockReason = ActivityStartedReason
            };
        }

        return new PoGroupReclassificationDecision
        {
            GroupId = input.GroupId,
            Action = PoGroupReclassificationAction.Restamp,
            NewSourceDocumentType = agreedType,
            Obligations = DocumentObligationResolver.Resolve(
                agreedType, DocumentUsageContext.PaymentRequest)
        };
    }
}
