using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AlplaPortal.Domain.Services;

/// <summary>Verdicts of <see cref="PurchaseOrderDocumentIdentifier"/>. Consumers branch on these, never on the pt-PT text.</summary>
public static class PoDocumentVerdicts
{
    /// <summary>The document carries exactly one Primavera P.O. identity (ECF / ECF10 / ECF11 + year/sequence) and nothing contradicts it.</summary>
    public const string IdentifiedPo = "IDENTIFIED_PO";
    /// <summary>The document is positively another type (quotation / proforma / invoice …) and carries no P.O. identity. Never a P.O.</summary>
    public const string IdentifiedOtherType = "IDENTIFIED_OTHER_TYPE";
    /// <summary>Nothing reliable: no identifier, an unknown prefix (e.g. a synthetic "ECF99"), several identities, a conflict, or no extraction at all.</summary>
    public const string Uncertain = "UNCERTAIN";
}

/// <summary>Machine-readable reasons behind a verdict.</summary>
public static class PoDocumentIdentificationReasons
{
    public const string PoIdentified = "PO_IDENTIFIED";
    public const string MultiplePoIdentities = "MULTIPLE_PO_IDENTITIES";
    public const string PoIdentityConflictsWithClassification = "PO_IDENTITY_CONFLICTS_WITH_CLASSIFICATION";
    public const string OtherDocumentType = "OTHER_DOCUMENT_TYPE";
    public const string PoReferencedOnly = "PO_REFERENCED_ONLY";
    public const string FamilyWithoutReference = "FAMILY_WITHOUT_REFERENCE";
    public const string UnrecognisedIdentifier = "UNRECOGNISED_IDENTIFIER";
    public const string NoIdentifier = "NO_IDENTIFIER";
    public const string NoExtraction = "NO_EXTRACTION";
}

/// <summary>Where a candidate reference was read from. Identity sources outrank body references.</summary>
public static class PoDocumentCandidateSources
{
    public const string DocumentNumber = "DOCUMENT_NUMBER";
    public const string Title = "TITLE";
    public const string BodyReference = "BODY_REFERENCE";
}

/// <summary>What the extraction said about a document — the only inputs the identifier reads. All optional.</summary>
public sealed record PoDocumentIdentificationInput
{
    /// <summary>The document's own number as extracted (for a Primavera P.O. the full ECF reference).</summary>
    public string? DocumentNumber { get; init; }
    /// <summary>The main heading of the document as printed ("Encomenda a Fornecedor ECF 2026/107", "Cotação", …).</summary>
    public string? DocumentTitle { get; init; }
    /// <summary>OCR classification proposal: PROFORMA | ADVANCE_INVOICE | INVOICE | INVOICE_RECEIPT | OTHER | null.</summary>
    public string? ClassificationType { get; init; }
    public decimal? ClassificationConfidence { get; init; }
    /// <summary>The title the classifier read (same evidence as <see cref="DocumentTitle"/>, older providers only fill this one).</summary>
    public string? ClassificationTitleFound { get; init; }
    /// <summary>Other document numbers mentioned in the body: quotations, previous / cancelled P.O.s, invoices. Never the document's own number.</summary>
    public IReadOnlyList<string>? ReferencedDocumentNumbers { get; init; }
    /// <summary>False when no extraction ran (OCR failed / unavailable): the verdict is then UNCERTAIN with NO_EXTRACTION.</summary>
    public bool ExtractionPerformed { get; init; } = true;
}

/// <summary>One P.O. reference found in the document, with its provenance.</summary>
public sealed record PoDocumentCandidate(string Reference, string Canonical, string Family, string FamilyLabel, string Source);

/// <summary>The explained verdict. Pure data — the UI renders it, RegisterPo enforces it.</summary>
public sealed record PoDocumentIdentification
{
    public string Verdict { get; init; } = PoDocumentVerdicts.Uncertain;
    public string ReasonCode { get; init; } = PoDocumentIdentificationReasons.NoExtraction;
    /// <summary>pt-PT explanation for the operator.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Set only for IDENTIFIED_PO (and for an UNCERTAIN verdict with exactly one identity candidate, as the suggestion).</summary>
    public string? Family { get; init; }
    public string? FamilyLabel { get; init; }
    public string? Reference { get; init; }
    public string? Canonical { get; init; }

    /// <summary>Every distinct P.O. reference read from the document's IDENTITY fields (number / title).</summary>
    public IReadOnlyList<PoDocumentCandidate> IdentityCandidates { get; init; } = Array.Empty<PoDocumentCandidate>();
    /// <summary>P.O. references found only in the body (references to previous / cancelled orders). Never an identity.</summary>
    public IReadOnlyList<PoDocumentCandidate> BodyReferences { get; init; } = Array.Empty<PoDocumentCandidate>();

    /// <summary>For IDENTIFIED_OTHER_TYPE: the classification code (PROFORMA, INVOICE, …) and its label.</summary>
    public string? OtherTypeCode { get; init; }
    public string? OtherTypeLabel { get; init; }

    /// <summary>True for UNCERTAIN: the Buyer must explicitly confirm the document is a P.O. and justify (existing override policy).</summary>
    public bool RequiresExplicitConfirmation => Verdict == PoDocumentVerdicts.Uncertain;
    /// <summary>True for IDENTIFIED_OTHER_TYPE: refused server-side, no override.</summary>
    public bool IsBlocked => Verdict == PoDocumentVerdicts.IdentifiedOtherType;
}

/// <summary>
/// Decides whether an extracted document is a purchase order, and which one — shared by the P.O. registration and the
/// P.O. correction (both call <c>direct-ocr</c> and <c>register-po</c>). Pure and deterministic: no OCR call, no I/O.
///
/// <para>Rules, in order:</para>
/// <list type="number">
/// <item>The document's IDENTITY is its own number and its heading. Only the three Primavera families are recognised
/// (<see cref="PrimaveraPoReference"/>: ECF11 = Serviços, ECF10 = Material diverso / escritório, ECF = Material de stock),
/// longest family first and with word boundaries, so "ECF99" or "ECF1" is never read as "ECF" (substring matches are
/// impossible by construction).</item>
/// <item>Exactly one identity reference and no strong contradicting classification → IDENTIFIED_PO.</item>
/// <item>Several distinct identity references → UNCERTAIN (the Buyer picks); one identity reference but the classifier is
/// confident (≥ 0.70) the document is a quotation / invoice → UNCERTAIN (conflict, explicit review).</item>
/// <item>No identity reference and the document is positively a quotation / proforma / invoice (classifier ≥ 0.50, or an
/// unambiguous title) → IDENTIFIED_OTHER_TYPE: never accepted as a P.O., whatever the supplier and amount say.</item>
/// <item>Everything else → UNCERTAIN with the precise reason: P.O. references only in the body (cancellation notes,
/// "em substituição da ECF …"), a family word without a complete reference, an unrecognised identifier (synthetic
/// "ECF99 2026/9003"), or no identifier at all. The Buyer may still register after an explicit confirmation + justification.</item>
/// </list>
/// </summary>
public static class PurchaseOrderDocumentIdentifier
{
    private static readonly Regex OtherTypeTitle = new(
        @"\b(COTA[ÇC][ÃA]O|OR[ÇC]AMENTO|PROPOSTA(\s+COMERCIAL)?|QUOTATION|QUOTE|ESTIMATE|PRO-?\s?FORMA|FACTURA|FATURA|INVOICE|RECIBO)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PoTitle = new(
        @"\b(ENCOMENDA|PURCHASE\s+ORDER|ORDEM\s+DE\s+COMPRA)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Family-looking tokens that are NOT a recognised family (ECF99, ECF1, ECF12 …): reported, never classified.</summary>
    private static readonly Regex UnrecognisedFamily = new(
        @"\bECF\d{1,3}\b[\s.:]*20\d{2}\s*[/\-]\s*\d{1,6}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> OtherTypeLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PROFORMA"] = "Cotação / Factura pró-forma",
            ["ESTIMATE"] = "Cotação / Orçamento",
            ["INVOICE"] = "Factura",
            ["INVOICE_RECEIPT"] = "Factura-recibo",
            ["ADVANCE_INVOICE"] = "Factura de adiantamento",
        };

    private const decimal OtherTypeThreshold = 0.50m;
    private const decimal ConflictThreshold = 0.70m;

    public static PoDocumentIdentification Identify(PoDocumentIdentificationInput? input)
    {
        if (input == null || !input.ExtractionPerformed)
        {
            return new PoDocumentIdentification
            {
                Verdict = PoDocumentVerdicts.Uncertain,
                ReasonCode = PoDocumentIdentificationReasons.NoExtraction,
                Reason = "A extração do documento não está disponível: o tipo e o número da P.O. não foram verificados. Confirme manualmente que o documento é uma Ordem de Compra e introduza o número completo.",
            };
        }

        var identityCandidates = Candidates(input.DocumentNumber, PoDocumentCandidateSources.DocumentNumber)
            .Concat(Candidates(input.DocumentTitle, PoDocumentCandidateSources.Title))
            .Concat(Candidates(input.ClassificationTitleFound, PoDocumentCandidateSources.Title))
            .GroupBy(c => c.Canonical, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var bodyReferences = (input.ReferencedDocumentNumbers ?? Array.Empty<string>())
            .SelectMany(r => Candidates(r, PoDocumentCandidateSources.BodyReference))
            .GroupBy(c => c.Canonical, StringComparer.Ordinal)
            .Select(g => g.First())
            .Where(c => identityCandidates.All(i => !string.Equals(i.Canonical, c.Canonical, StringComparison.Ordinal)))
            .ToList();

        var classification = input.ClassificationType?.Trim().ToUpperInvariant();
        var classifiedAsOther = classification != null && OtherTypeLabels.ContainsKey(classification);
        var confidence = input.ClassificationConfidence ?? 0m;
        var titleText = $"{input.DocumentTitle} {input.ClassificationTitleFound}".Trim();
        var titleSaysOther = OtherTypeTitle.IsMatch(titleText) && !PoTitle.IsMatch(titleText) && !identityCandidates.Any();

        if (identityCandidates.Count == 1)
        {
            var only = identityCandidates[0];
            if (classifiedAsOther && confidence >= ConflictThreshold)
            {
                return new PoDocumentIdentification
                {
                    Verdict = PoDocumentVerdicts.Uncertain,
                    ReasonCode = PoDocumentIdentificationReasons.PoIdentityConflictsWithClassification,
                    Reason = $"O documento identifica-se como P.O. {only.Reference} ({only.FamilyLabel}), mas a leitura classificou-o como {OtherTypeLabels[classification!]}. Reveja o documento antes de confirmar.",
                    Family = only.Family, FamilyLabel = only.FamilyLabel, Reference = only.Reference, Canonical = only.Canonical,
                    IdentityCandidates = identityCandidates, BodyReferences = bodyReferences,
                    OtherTypeCode = classification, OtherTypeLabel = OtherTypeLabels[classification!],
                };
            }

            return new PoDocumentIdentification
            {
                Verdict = PoDocumentVerdicts.IdentifiedPo,
                ReasonCode = PoDocumentIdentificationReasons.PoIdentified,
                Reason = $"Ordem de Compra identificada: {only.Reference} ({only.Family} — {only.FamilyLabel}).",
                Family = only.Family, FamilyLabel = only.FamilyLabel, Reference = only.Reference, Canonical = only.Canonical,
                IdentityCandidates = identityCandidates, BodyReferences = bodyReferences,
            };
        }

        if (identityCandidates.Count > 1)
        {
            return new PoDocumentIdentification
            {
                Verdict = PoDocumentVerdicts.Uncertain,
                ReasonCode = PoDocumentIdentificationReasons.MultiplePoIdentities,
                Reason = "O documento apresenta mais do que uma referência de P.O. na sua identificação (" +
                         string.Join(", ", identityCandidates.Select(c => c.Reference)) + "). Selecione ou introduza a referência correta.",
                IdentityCandidates = identityCandidates, BodyReferences = bodyReferences,
            };
        }

        // No identity reference at all from here on.
        if ((classifiedAsOther && confidence >= OtherTypeThreshold) || titleSaysOther)
        {
            var code = classifiedAsOther ? classification! : "PROFORMA";
            var label = OtherTypeLabels[code];
            return new PoDocumentIdentification
            {
                Verdict = PoDocumentVerdicts.IdentifiedOtherType,
                ReasonCode = PoDocumentIdentificationReasons.OtherDocumentType,
                Reason = $"O documento foi identificado como {label}" + (string.IsNullOrWhiteSpace(titleText) ? "" : $" (\"{titleText.Trim()}\")") +
                         " e não contém nenhuma referência de Ordem de Compra (ECF, ECF10 ou ECF11). Não pode ser registado como P.O., mesmo que o fornecedor e o valor coincidam.",
                BodyReferences = bodyReferences,
                OtherTypeCode = code, OtherTypeLabel = label,
            };
        }

        if (bodyReferences.Count > 0)
        {
            return new PoDocumentIdentification
            {
                Verdict = PoDocumentVerdicts.Uncertain,
                ReasonCode = PoDocumentIdentificationReasons.PoReferencedOnly,
                Reason = "O documento refere P.O.(s) no corpo (" + string.Join(", ", bodyReferences.Select(c => c.Reference)) +
                         ") mas não se identifica como uma delas — pode ser uma cotação, uma nota de cancelamento ou outro documento que cita a encomenda. Confirme o tipo e o número antes de registar.",
                BodyReferences = bodyReferences,
            };
        }

        var identityText = $"{input.DocumentNumber} {titleText}".Trim();
        if (UnrecognisedFamily.IsMatch(identityText))
        {
            return new PoDocumentIdentification
            {
                Verdict = PoDocumentVerdicts.Uncertain,
                ReasonCode = PoDocumentIdentificationReasons.UnrecognisedIdentifier,
                Reason = $"O identificador \"{UnrecognisedFamily.Match(identityText).Value.Trim()}\" não corresponde a nenhuma série de P.O. reconhecida (ECF11 = Serviços, ECF10 = Materiais diversos, ECF = Material de stock). Confirme o documento e introduza o número manualmente.",
            };
        }

        if (PrimaveraPoReference.MentionsFamilyWithoutReference(identityText))
        {
            return new PoDocumentIdentification
            {
                Verdict = PoDocumentVerdicts.Uncertain,
                ReasonCode = PoDocumentIdentificationReasons.FamilyWithoutReference,
                Reason = "O documento parece ser uma P.O. Primavera, mas a referência completa (série + ano/sequência) não foi lida. Introduza o número manualmente e confirme.",
            };
        }

        return new PoDocumentIdentification
        {
            Verdict = PoDocumentVerdicts.Uncertain,
            ReasonCode = PoDocumentIdentificationReasons.NoIdentifier,
            Reason = "Não foi possível identificar o tipo do documento nem uma referência de Ordem de Compra. Confirme manualmente que o documento é uma P.O. e introduza o número completo.",
        };
    }

    private static IEnumerable<PoDocumentCandidate> Candidates(string? text, string source) =>
        PrimaveraPoReference.ParseAll(text).Select(p => new PoDocumentCandidate(
            p.Display, p.Canonical, p.Family,
            PrimaveraPoReference.FamilyLabels.TryGetValue(p.Family, out var label) ? label : p.Family,
            source));
}
