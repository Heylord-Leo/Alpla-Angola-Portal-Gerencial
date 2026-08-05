using System;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// What the user decided when they contradicted the document, expressed as data the persistence
/// layer can store without making any further judgement.
/// </summary>
public sealed record DocumentClassificationOverrideRequest
{
    public string Context { get; init; } = string.Empty;
    public Guid ScopeId { get; init; }
    public Guid? AttachmentId { get; init; }

    public string? SuggestedType { get; init; }
    public decimal? Confidence { get; init; }
    public string? TitleFound { get; init; }
    public string? EvidenceJson { get; init; }
    public string? ConflictingEvidenceJson { get; init; }
    public string? SuggestionSource { get; init; }

    public string? SelectedType { get; init; }
    public bool Acknowledged { get; init; }
    public string? Justification { get; init; }
}

/// <summary>What kind of decision the user made, which is not always an override.</summary>
public enum DocumentClassificationDecisionKind
{
    /// <summary>Nothing to record: the user agreed with the reading, or chose nothing.</summary>
    None = 0,

    /// <summary>
    /// The user contradicted a specific suggestion. Requires acknowledgement, and a written reason
    /// when the contradiction is high-risk.
    /// </summary>
    Override = 1,

    /// <summary>
    /// The user named a type the extraction could not determine — it read OTHER, UNCLASSIFIED, or
    /// nothing. Recorded for provenance, but <b>no acknowledgement and no justification are owed</b>:
    /// there was no opinion to contradict, only a gap to fill.
    /// </summary>
    UserClassifiedIndeterminate = 2
}

/// <summary>The recorder's verdict: either a complete, storable decision or the reason it is not.</summary>
public sealed record DocumentClassificationOverrideResult
{
    /// <summary>False when there is nothing to record — not an error, simply no override happened.</summary>
    public bool ShouldRecord { get; init; }

    /// <summary>Which kind of decision this is. Callers must not infer it from the other fields.</summary>
    public DocumentClassificationDecisionKind Kind { get; init; } = DocumentClassificationDecisionKind.None;

    /// <summary>Set when the caller asked to record an override that is not admissible.</summary>
    public string? RejectionReason { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>One sentence for the request timeline, in the language the user reads it in.</summary>
    public string HistoryComment { get; init; } = string.Empty;

    public string NormalizedContext { get; init; } = string.Empty;
    public string NormalizedSelectedType { get; init; } = string.Empty;
    public string? NormalizedSuggestedType { get; init; }
    public string? NormalizedSuggestionSource { get; init; }
    public string? TrimmedJustification { get; init; }
}

/// <summary>
/// Decides whether a classification decision is an auditable override, and shapes the record.
///
/// <para>Pure by design. Everything here — is this actually a contradiction, is the justification
/// adequate, what should the timeline say, what identity does the event have — is decided without
/// touching the database, so the rules can be tested directly rather than inferred from what got
/// written. The caller does exactly two things with the result: store it, or reject the request.</para>
///
/// <para>The one rule worth stating plainly: <b>an override is only recorded when the user actually
/// contradicted a suggestion.</b> Agreeing with the evidence, or classifying a document nothing was
/// suggested for, is ordinary data entry and produces no audit row. Recording those too would bury
/// the handful of decisions that matter under thousands that do not.</para>
/// </summary>
public static class DocumentClassificationOverrideRecorder
{
    /// <summary>
    /// Minimum characters of written reason for a high-risk override. Mirrors the frontend's
    /// CONFLICT_JUSTIFICATION_MIN_LENGTH; the backend re-checks because the frontend cannot be
    /// trusted to have asked.
    /// </summary>
    public const int MinimumJustificationLength = 20;

    /// <summary>
    /// Confidence at or above which a plain disagreement is already serious enough to demand a
    /// written reason. Below it, only a fiscal-vs-non-fiscal contradiction does.
    /// </summary>
    public const decimal HighConfidenceThreshold = 0.70m;

    public static DocumentClassificationOverrideResult Evaluate(DocumentClassificationOverrideRequest request)
    {
        var selected = RequestConstants.SourceDocumentTypes.Normalize(request.SelectedType);
        var suggested = RequestConstants.SourceDocumentTypes.Normalize(request.SuggestedType);

        // Agreed with the reading, chose nothing, or classified a document nothing was suggested
        // for. The last is ordinary data entry: recording it would bury the handful of decisions
        // that matter under thousands that do not.
        if (selected == null || suggested == null || selected == suggested)
            return new DocumentClassificationOverrideResult { ShouldRecord = false };

        if (!RequestConstants.DocumentClassificationContexts.IsValid(request.Context))
        {
            return new DocumentClassificationOverrideResult
            {
                ShouldRecord = false,
                RejectionReason = "Contexto de classificação inválido."
            };
        }

        if (request.ScopeId == Guid.Empty)
        {
            return new DocumentClassificationOverrideResult
            {
                ShouldRecord = false,
                RejectionReason = "A decisão de classificação não tem um âmbito válido."
            };
        }

        var context = request.Context.Trim().ToUpperInvariant();
        var justification = request.Justification?.Trim();

        // ── The extraction looked, and could not decide ──
        // OTHER and UNCLASSIFIED mean "could not place this document in a category". Naming a type
        // then SUPPLIES the classification rather than overriding one, so demanding an
        // acknowledgement and a 20-character written reason would be asking the user to justify
        // answering a question the system itself asked. Still recorded — unlike the no-suggestion
        // case above — because a reading did happen and it must remain visible that the type
        // attached to this document was not the one it produced.
        if (!IsAuthoritative(suggested))
        {
            return new DocumentClassificationOverrideResult
            {
                ShouldRecord = true,
                Kind = DocumentClassificationDecisionKind.UserClassifiedIndeterminate,
                IdempotencyKey = PostPaymentIdempotencyKeys.DocumentClassificationSet(
                    context, request.ScopeId, request.AttachmentId, selected),
                HistoryComment = BuildIndeterminateComment(selected),
                NormalizedContext = context,
                NormalizedSelectedType = selected,
                NormalizedSuggestedType = suggested,
                NormalizedSuggestionSource =
                    RequestConstants.DocumentClassificationSources.Normalize(request.SuggestionSource),
                TrimmedJustification = null
            };
        }

        if (!request.Acknowledged)
        {
            return new DocumentClassificationOverrideResult
            {
                ShouldRecord = false,
                RejectionReason =
                    $"A classificação selecionada ({RequestConstants.SourceDocumentTypes.DisplayName(selected)}) " +
                    $"contradiz a leitura do documento ({RequestConstants.SourceDocumentTypes.DisplayName(suggested)}). " +
                    "É necessário confirmar explicitamente a divergência."
            };
        }

        if (RequiresJustification(suggested, selected, request.Confidence) &&
            (justification == null || justification.Length < MinimumJustificationLength))
        {
            return new DocumentClassificationOverrideResult
            {
                ShouldRecord = false,
                RejectionReason =
                    $"É obrigatório justificar por escrito (mínimo {MinimumJustificationLength} caracteres) " +
                    "a classificação que contradiz o documento."
            };
        }

        return new DocumentClassificationOverrideResult
        {
            ShouldRecord = true,
            Kind = DocumentClassificationDecisionKind.Override,
            IdempotencyKey = PostPaymentIdempotencyKeys.DocumentClassificationOverride(
                context, request.ScopeId, request.AttachmentId, selected),
            HistoryComment = BuildComment(suggested, selected, justification),
            NormalizedContext = context,
            NormalizedSelectedType = selected,
            NormalizedSuggestedType = suggested,
            NormalizedSuggestionSource = RequestConstants.DocumentClassificationSources.Normalize(request.SuggestionSource),
            TrimmedJustification = justification
        };
    }

    /// <summary>
    /// Whether a written reason is owed, not merely a click.
    ///
    /// <para>Choosing a NON-FISCAL type for a document the evidence reads as FISCAL always demands
    /// one, whatever the confidence: that is the direction that understates fiscal reality, and it
    /// is exactly the mistake this whole mechanism exists to catch. Other disagreements demand one
    /// only when the evidence was strong.</para>
    /// </summary>
    public static bool RequiresJustification(string? suggestedType, string? selectedType, decimal? confidence)
    {
        var suggested = RequestConstants.SourceDocumentTypes.Normalize(suggestedType);
        var selected = RequestConstants.SourceDocumentTypes.Normalize(selectedType);

        if (suggested == null || selected == null || suggested == selected) return false;

        // Filling a gap the extraction left is never a contradiction, so it never owes a reason.
        if (!IsAuthoritative(suggested)) return false;

        var understatesFiscalReality =
            RequestConstants.SourceDocumentTypes.IsFiscal(suggested) &&
            !RequestConstants.SourceDocumentTypes.IsFiscal(selected);

        return understatesFiscalReality || (confidence ?? 0m) >= HighConfidenceThreshold;
    }

    /// <summary>
    /// Whether the extraction actually committed to a type.
    ///
    /// <para><c>OTHER</c> and <c>UNCLASSIFIED</c> are the extraction declining to classify, and a
    /// null suggestion is the same statement made by omission. None of them is an opinion that can
    /// be contradicted.</para>
    /// </summary>
    public static bool IsAuthoritative(string? suggestedType)
    {
        var suggested = RequestConstants.SourceDocumentTypes.Normalize(suggestedType);

        return suggested != null
            && suggested != RequestConstants.SourceDocumentTypes.Other
            && suggested != RequestConstants.SourceDocumentTypes.Unclassified;
    }

    private static string BuildIndeterminateComment(string selected)
        => $"Classificação do documento definida como " +
           $"\"{RequestConstants.SourceDocumentTypes.DisplayName(selected)}\" pelo utilizador. " +
           "A leitura automática não identificou um tipo específico.";

    private static string BuildComment(string suggested, string selected, string? justification)
    {
        var text =
            $"Classificação do documento alterada de \"{RequestConstants.SourceDocumentTypes.DisplayName(suggested)}\" " +
            $"para \"{RequestConstants.SourceDocumentTypes.DisplayName(selected)}\".";

        return string.IsNullOrWhiteSpace(justification)
            ? text
            : $"{text} Justificativa: {justification}";
    }
}
