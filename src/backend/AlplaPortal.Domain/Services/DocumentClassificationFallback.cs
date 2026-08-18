using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>Outcome of the fallback classifier. Always a suggestion, never a verdict.</summary>
public sealed record FallbackClassification
{
    public string? SuggestedType { get; init; }
    public decimal Confidence { get; init; }
    public string? TitleFound { get; init; }
    public List<string> SupportingEvidence { get; init; } = new();
    public List<string> NonFiscalMarkers { get; init; } = new();
    public bool IndicatesFiscalDocument { get; init; }

    public static FallbackClassification None => new();
}

/// <summary>
/// Derives a LOW-CONFIDENCE document-identity suggestion from data the Portal already has, for use
/// when the extraction provider returns no structured classification block — because it is an older
/// provider, the response was truncated, the document is a scan with little text, or extraction is
/// unavailable.
///
/// <para><b>Why a fallback exists at all.</b> With no suggestion the UI has nothing to reconcile the
/// user's choice against, which is precisely how an <c>FT</c> invoice was accepted as a
/// "Fatura Proforma" unchallenged. A weak, clearly-labelled suggestion still surfaces that conflict;
/// silence does not.</para>
///
/// <para><b>Deliberate ceilings.</b> A document-number prefix caps confidence at 0.50 and a filename
/// at 0.35 — neither can ever reach the 0.70 "high confidence" bar on its own. Only explicit title
/// wording found in the document text is treated as strong. The point is to raise a question, never
/// to answer it.</para>
///
/// <para>Pure and side-effect-free.</para>
/// </summary>
public static class DocumentClassificationFallback
{
    /// <summary>Ceiling for a suggestion resting only on a document-number prefix.</summary>
    public const decimal PrefixOnlyMaxConfidence = 0.50m;

    /// <summary>Ceiling for a suggestion resting only on a filename. Weaker still — filenames lie.</summary>
    public const decimal FileNameOnlyMaxConfidence = 0.35m;

    /// <summary>Confidence for explicit title wording read from the document text.</summary>
    private const decimal ExplicitTitleConfidence = 0.85m;

    /// <summary>The threshold the conflict rules treat as "high confidence".</summary>
    public const decimal HighConfidenceThreshold = 0.70m;

    /// <summary>
    /// Title wording, most specific first. Order matters: "FACTURA-RECIBO" and
    /// "FACTURA DE ADIANTAMENTO" both contain "FACTURA", so a plain-Factura match must be tested last
    /// or every specific document would be flattened into INVOICE.
    /// </summary>
    private static readonly (string Phrase, string Type)[] TitlePhrases =
    {
        ("FACTURA-RECIBO", RequestConstants.SourceDocumentTypes.InvoiceReceipt),
        ("FATURA-RECIBO", RequestConstants.SourceDocumentTypes.InvoiceReceipt),
        ("FACTURA RECIBO", RequestConstants.SourceDocumentTypes.InvoiceReceipt),
        ("FATURA RECIBO", RequestConstants.SourceDocumentTypes.InvoiceReceipt),

        ("FACTURA DE ADIANTAMENTO", RequestConstants.SourceDocumentTypes.AdvanceInvoice),
        ("FATURA DE ADIANTAMENTO", RequestConstants.SourceDocumentTypes.AdvanceInvoice),
        ("FACTURA ADIANTAMENTO", RequestConstants.SourceDocumentTypes.AdvanceInvoice),
        ("FATURA ADIANTAMENTO", RequestConstants.SourceDocumentTypes.AdvanceInvoice),

        ("FACTURA PRÓ-FORMA", RequestConstants.SourceDocumentTypes.Proforma),
        ("FACTURA PRO-FORMA", RequestConstants.SourceDocumentTypes.Proforma),
        ("FATURA PRÓ-FORMA", RequestConstants.SourceDocumentTypes.Proforma),
        ("FATURA PRO-FORMA", RequestConstants.SourceDocumentTypes.Proforma),
        ("PRÓ-FORMA", RequestConstants.SourceDocumentTypes.Proforma),
        ("PRO-FORMA", RequestConstants.SourceDocumentTypes.Proforma),
        ("PROFORMA", RequestConstants.SourceDocumentTypes.Proforma),

        // Commercial-offer wording. Canonically PROFORMA since v2.229.10: an orçamento, cotação or
        // proposta is the same payable origin as a Factura Pró-forma, and suggesting a separate
        // code manufactured conflicts with the only type the payment screen offers.
        ("ORÇAMENTO", RequestConstants.SourceDocumentTypes.Proforma),
        ("ORCAMENTO", RequestConstants.SourceDocumentTypes.Proforma),
        ("COTAÇÃO", RequestConstants.SourceDocumentTypes.Proforma),
        ("COTACAO", RequestConstants.SourceDocumentTypes.Proforma),
        ("PROPOSTA COMERCIAL", RequestConstants.SourceDocumentTypes.Proforma),
        ("COMMERCIAL PROPOSAL", RequestConstants.SourceDocumentTypes.Proforma),
        ("QUOTATION", RequestConstants.SourceDocumentTypes.Proforma),

        ("FACTURA", RequestConstants.SourceDocumentTypes.Invoice),
        ("FATURA", RequestConstants.SourceDocumentTypes.Invoice)
    };

    /// <summary>
    /// Document-number prefixes, longest first so "FR" is not shadowed by "F".
    /// FA is deliberately absent: ALPLA uses it for both "Factura" and "Factura de Adiantamento",
    /// so it carries no usable signal until Finance confirms otherwise.
    /// </summary>
    private static readonly (string Prefix, string Type)[] NumberPrefixes =
    {
        ("ORC", RequestConstants.SourceDocumentTypes.Proforma),
        ("PRO", RequestConstants.SourceDocumentTypes.Proforma),
        ("PF", RequestConstants.SourceDocumentTypes.Proforma),
        ("FR", RequestConstants.SourceDocumentTypes.InvoiceReceipt),
        ("FT", RequestConstants.SourceDocumentTypes.Invoice)
    };

    /// <summary>Wording by which a document declares it has no fiscal force.</summary>
    private static readonly string[] NonFiscalPhrases =
    {
        "SEM VALOR FISCAL",
        "NÃO SERVE DE FACTURA",
        "NAO SERVE DE FACTURA",
        "NÃO SERVE DE FATURA",
        "DOCUMENTO NÃO FISCAL",
        "DOCUMENTO NAO FISCAL",
        "NÃO SERVE DE RECIBO",
        "NAO SERVE DE RECIBO"
    };

    /// <summary>
    /// Builds a suggestion from whatever is available. Returns <see cref="FallbackClassification.None"/>
    /// when nothing supports one — silence is better than a fabricated guess.
    /// </summary>
    /// <param name="documentNumber">Extracted document number, e.g. "FT5926S42989N/52".</param>
    /// <param name="fileName">Uploaded file name. Weakest evidence.</param>
    /// <param name="rawText">Document text when available. The only source of strong evidence.</param>
    public static FallbackClassification Derive(string? documentNumber, string? fileName, string? rawText = null)
    {
        var evidence = new List<string>();
        var nonFiscalMarkers = new List<string>();

        var text = (rawText ?? string.Empty).ToUpperInvariant();

        // A document declaring itself non-fiscal overrides any upward reading. It is a statement by
        // the issuer, not an inference, so it is applied before anything else.
        foreach (var phrase in NonFiscalPhrases)
        {
            if (text.Contains(phrase, StringComparison.Ordinal))
                nonFiscalMarkers.Add(phrase);
        }

        // ── Strongest: explicit title wording in the document text ──
        if (text.Length > 0)
        {
            foreach (var (phrase, type) in TitlePhrases)
            {
                if (!text.Contains(phrase, StringComparison.Ordinal)) continue;

                var resolved = type;
                if (nonFiscalMarkers.Count > 0 && RequestConstants.SourceDocumentTypes.IsFiscal(resolved))
                    resolved = RequestConstants.SourceDocumentTypes.Proforma;

                evidence.Add($"Texto do documento: \"{phrase}\"");
                return new FallbackClassification
                {
                    SuggestedType = resolved,
                    Confidence = ExplicitTitleConfidence,
                    TitleFound = phrase,
                    SupportingEvidence = evidence,
                    NonFiscalMarkers = nonFiscalMarkers,
                    IndicatesFiscalDocument = nonFiscalMarkers.Count == 0
                                              && RequestConstants.SourceDocumentTypes.IsFiscal(resolved)
                };
            }
        }

        // ── Weak: document-number prefix ──
        var number = (documentNumber ?? string.Empty).Trim().ToUpperInvariant();
        foreach (var (prefix, type) in NumberPrefixes)
        {
            if (!number.StartsWith(prefix, StringComparison.Ordinal)) continue;

            evidence.Add($"Prefixo do número do documento: \"{prefix}\"");
            return new FallbackClassification
            {
                SuggestedType = type,
                Confidence = PrefixOnlyMaxConfidence,
                SupportingEvidence = evidence,
                NonFiscalMarkers = nonFiscalMarkers,
                IndicatesFiscalDocument = nonFiscalMarkers.Count == 0
                                          && RequestConstants.SourceDocumentTypes.IsFiscal(type)
            };
        }

        // ── Weakest: filename ──
        var baseName = SafeFileNameStem(fileName);
        foreach (var (prefix, type) in NumberPrefixes)
        {
            if (!baseName.StartsWith(prefix, StringComparison.Ordinal)) continue;

            evidence.Add($"Nome do ficheiro começa por \"{prefix}\"");
            return new FallbackClassification
            {
                SuggestedType = type,
                Confidence = FileNameOnlyMaxConfidence,
                SupportingEvidence = evidence,
                NonFiscalMarkers = nonFiscalMarkers,
                IndicatesFiscalDocument = nonFiscalMarkers.Count == 0
                                          && RequestConstants.SourceDocumentTypes.IsFiscal(type)
            };
        }

        return FallbackClassification.None;
    }

    /// <summary>Uppercased file stem with separators stripped. Never throws on an odd path.</summary>
    private static string SafeFileNameStem(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;

        var name = fileName.Trim();
        var slash = name.LastIndexOfAny(new[] { '/', '\\' });
        if (slash >= 0 && slash < name.Length - 1) name = name[(slash + 1)..];

        var dot = name.LastIndexOf('.');
        if (dot > 0) name = name[..dot];

        return name.ToUpperInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
    }
}
