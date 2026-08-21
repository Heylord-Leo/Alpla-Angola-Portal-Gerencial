using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>One commercial line reduced to what the content fingerprint needs.</summary>
public sealed record DuplicateFingerprintItem(
    string? Description, decimal Quantity, decimal UnitPrice, decimal Total);

/// <summary>
/// Deterministic content identity for a source document's commercial lines.
///
/// <para>Exists because supplier + document number is NOT a document identity in practice:
/// suppliers legitimately reuse a proposal reference across materially different proposals
/// (CONSULTIT's <c>ONP_18910_v3</c> named four different projects). Only the content can tell a
/// re-keyed copy of the same debt from a genuinely different document.</para>
///
/// <para>Pure and deterministic by contract: no OCR call, no fuzzy matching, no embeddings.
/// Order-independent, because item ordering on a supplier document carries no meaning.</para>
/// </summary>
public static class PaymentSourceDocumentFingerprint
{
    /// <summary>
    /// Canonical form of a document number or series for comparison: uppercased with whitespace,
    /// hyphens, underscores and dots removed — "ONP_18910_v3", "ONP 18910 V3" and "onp-18910-v3"
    /// are the same reference. Deliberately nothing cleverer: no stemming, no prefix guessing.
    /// </summary>
    public static string NormalizeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_' || ch == '.') continue;
            builder.Append(char.ToUpperInvariant(ch));
        }
        return builder.ToString();
    }

    /// <summary>
    /// SHA-256 over the sorted, normalized lines, or null when there are no lines — absence of
    /// content evidence must stay distinguishable from "empty content", because the hierarchy
    /// treats a missing fingerprint as AMBIGUOUS rather than as proof of anything.
    /// </summary>
    public static string? Compute(IEnumerable<DuplicateFingerprintItem>? items)
    {
        if (items == null) return null;

        var lines = items
            .Select(i => string.Join("|",
                NormalizeDescription(i.Description),
                i.Quantity.ToString("G29", CultureInfo.InvariantCulture),
                i.UnitPrice.ToString("G29", CultureInfo.InvariantCulture),
                i.Total.ToString("G29", CultureInfo.InvariantCulture)))
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

        if (lines.Count == 0) return null;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        return Convert.ToHexString(bytes);
    }

    /// <summary>Uppercased, internal whitespace runs collapsed to one space.</summary>
    private static string NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        var builder = new StringBuilder(description.Length);
        var pendingSpace = false;
        foreach (var ch in description.Trim())
        {
            if (char.IsWhiteSpace(ch)) { pendingSpace = true; continue; }
            if (pendingSpace && builder.Length > 0) builder.Append(' ');
            pendingSpace = false;
            builder.Append(char.ToUpperInvariant(ch));
        }
        return builder.ToString();
    }
}

/// <summary>Where a same-reference document lives relative to the candidate.</summary>
public enum BusinessDuplicateScope
{
    SameRequest = 0,
    OtherRequest = 1
}

/// <summary>
/// The hierarchy's verdict on a candidate document. Levels follow the approved design:
/// 1 = file identity (decided by hash elsewhere), 2 = strong semantic duplicate (hard block),
/// 3 = materially different (allow), 4 = ambiguous (explicit audited override required).
/// </summary>
public enum BusinessDuplicateVerdict
{
    /// <summary>No same-reference document exists, or every one is provably different (LEVEL 3).</summary>
    Allow = 0,

    /// <summary>LEVEL 2 — same reference, same company, same currency, totals within tolerance
    /// AND identical content fingerprint. No override exists for paying the same debt twice.</summary>
    Block = 2,

    /// <summary>LEVEL 4 — same reference but the content evidence cannot prove duplicate OR
    /// distinct. Allowed only through an explicit, audited confirmation with a written reason.</summary>
    Ambiguous = 4
}

/// <summary>
/// The human-facing shape of a match — presentation metadata riding on the verdicts, so the UI
/// can explain WHY without a second rule engine. Severity-ordered.
/// </summary>
public enum BusinessDuplicateClassification
{
    None = 0,

    /// <summary>Same/related reference, provably different commercial act (LEVEL 3). Informational
    /// only — the approved CONSULTIT rule: never friction, never an override.</summary>
    RelatedDocument = 1,

    /// <summary>A strong candidate exists but relevant fields conflict, or the evidence cannot
    /// decide. Justified override required (LEVEL 4).</summary>
    AmbiguousMatch = 2,

    /// <summary>Same probable supplier, same reference, same date, same currency, totals within
    /// tolerance — the same commercial identity in a different file. Justified override required;
    /// different bytes are never evidence of a new commercial document.</summary>
    StrongBusinessDuplicate = 3,

    /// <summary>Identical content fingerprints under the LEVEL 2 rules. Hard block.</summary>
    SemanticDuplicate = 4
}

/// <summary>Stable field codes for matching/conflicting evidence, shared with the UI.</summary>
public static class BusinessDuplicateFields
{
    public const string DocumentNumber = "DOCUMENT_NUMBER";
    public const string SupplierName = "SUPPLIER_NAME";
    public const string SupplierNif = "SUPPLIER_NIF";
    public const string Supplier = "SUPPLIER";
    public const string DocumentDate = "DOCUMENT_DATE";
    public const string Currency = "CURRENCY";
    public const string GrossAmount = "GROSS_AMOUNT";
    public const string Company = "COMPANY";
    public const string Content = "CONTENT";
}

/// <summary>An existing active document reduced for comparison. Supplier identity is a SIGNAL
/// here, never a prerequisite — comparands are gathered by reference, across suppliers.</summary>
public sealed record BusinessDuplicateComparand
{
    public Guid Id { get; init; }
    public int SequenceNumber { get; init; }
    public string? DocumentNumber { get; init; }
    public string? DocumentSeries { get; init; }
    public int? SupplierId { get; init; }
    public string? SupplierName { get; init; }
    public string? SupplierTaxId { get; init; }
    public DateTime? DocumentDate { get; init; }
    public int CompanyId { get; init; }
    public string? Currency { get; init; }
    public decimal? GrossAmount { get; init; }
    /// <summary>Null when the document has no active items — no content evidence, not "empty".</summary>
    public string? ItemFingerprint { get; init; }
    public BusinessDuplicateScope Scope { get; init; }
    /// <summary>Populated for OtherRequest comparands so a block can name where the twin lives.</summary>
    public string? RequestNumber { get; init; }
    public Guid? RequestId { get; init; }
}

/// <summary>The candidate document being created or edited, reduced for comparison.</summary>
public sealed record BusinessDuplicateCandidate
{
    public string? DocumentNumber { get; init; }
    public string? DocumentSeries { get; init; }
    public int? SupplierId { get; init; }
    public string? SupplierName { get; init; }
    public string? SupplierTaxId { get; init; }
    public DateTime? DocumentDate { get; init; }
    public int? CompanyId { get; init; }
    public string? Currency { get; init; }
    public decimal? GrossAmount { get; init; }
    public string? ItemFingerprint { get; init; }
}

public sealed record BusinessDuplicateDecision
{
    public BusinessDuplicateVerdict Verdict { get; init; } = BusinessDuplicateVerdict.Allow;
    public BusinessDuplicateClassification Classification { get; init; } = BusinessDuplicateClassification.None;
    /// <summary>The comparand that produced the verdict, when one did.</summary>
    public BusinessDuplicateComparand? Match { get; init; }
    /// <summary>Short pt-PT phrase describing why, for messages and the audit comment.</summary>
    public string? Reason { get; init; }
    /// <summary>Field codes that MATCHED between candidate and comparand.</summary>
    public IReadOnlyList<string> MatchingFields { get; init; } = Array.Empty<string>();
    /// <summary>Field codes that CONFLICTED (both sides known, values differ).</summary>
    public IReadOnlyList<string> ConflictingFields { get; init; } = Array.Empty<string>();

    public static readonly BusinessDuplicateDecision Clear = new();
}

/// <summary>
/// LEVELS 2–4 of the duplicate-document hierarchy (LEVEL 1, exact file identity, is decided by
/// the attachment hash in <see cref="PaymentSourceDocumentDuplicatePolicy"/> and the controller).
///
/// <para>The rule this replaces treated supplier + number + series as a document identity and
/// hard-blocked on it, which forced users to falsify real supplier references to register
/// legitimate documents. The hierarchy blocks outright only when the evidence is strong in BOTH
/// directions of the claim: identical reference AND identical commercial content. Header equality
/// alone — with no item fingerprint on either side — is never a hard block; it is AMBIGUOUS and
/// requires an explicit, audited confirmation instead of a silent wall.</para>
///
/// <para>Pure and deliberately free of I/O so the same rule runs identically in tests, in the
/// create path and in the update path.</para>
/// </summary>
public static class PaymentSourceDocumentDuplicateHierarchy
{
    /// <summary>ProblemDetails extension code for a LEVEL 2 hard block.</summary>
    public const string SemanticDuplicateCode = "DUPLICATE_SEMANTIC";

    /// <summary>ProblemDetails extension code for a LEVEL 4 refusal awaiting explicit override.</summary>
    public const string AmbiguousDuplicateCode = "DUPLICATE_AMBIGUOUS";

    /// <summary>ProblemDetails extension code for a LEVEL 1 cross-request file-identity block.</summary>
    public const string CrossRequestFileCode = "DUPLICATE_FILE_CROSS_REQUEST";

    /// <summary>Minimum written reason for a LEVEL 4 override. Mirrors the classification rule.</summary>
    public const int MinimumOverrideReasonLength = 20;

    /// <summary>
    /// Evaluates every same-reference comparand and returns each decision — severity-ordered,
    /// strongest first. The preflight endpoint returns all of them so the UI can explain the
    /// evidence; the persistence guard acts on the first.
    /// </summary>
    public static IReadOnlyList<BusinessDuplicateDecision> EvaluateAll(
        BusinessDuplicateCandidate candidate,
        IEnumerable<BusinessDuplicateComparand> comparands)
    {
        var number = PaymentSourceDocumentFingerprint.NormalizeReference(candidate.DocumentNumber);
        if (number.Length == 0) return Array.Empty<BusinessDuplicateDecision>();

        var series = PaymentSourceDocumentFingerprint.NormalizeReference(candidate.DocumentSeries);

        return comparands
            .Where(other =>
                string.Equals(PaymentSourceDocumentFingerprint.NormalizeReference(other.DocumentNumber),
                    number, StringComparison.Ordinal) &&
                string.Equals(PaymentSourceDocumentFingerprint.NormalizeReference(other.DocumentSeries),
                    series, StringComparison.Ordinal))
            .Select(other => JudgePair(candidate, other))
            .Where(d => d.Classification != BusinessDuplicateClassification.None)
            .OrderByDescending(d => (int)d.Classification)
            .ToList();
    }

    /// <summary>The strongest decision, for the persistence guard. Allow when nothing qualifies.</summary>
    public static BusinessDuplicateDecision Judge(
        BusinessDuplicateCandidate candidate,
        IEnumerable<BusinessDuplicateComparand> comparands)
    {
        var all = EvaluateAll(candidate, comparands);
        // RelatedDocument is informational: the strongest ACTIONABLE decision wins; when only
        // related documents exist the verdict is Allow (with the evidence still attached).
        return all.FirstOrDefault(d => d.Verdict != BusinessDuplicateVerdict.Allow)
               ?? all.FirstOrDefault()
               ?? BusinessDuplicateDecision.Clear;
    }

    /// <summary>
    /// One candidate-vs-existing comparison over independent signals. Supplier identity is
    /// EVIDENCE, never a gate: a NIF mismatch weakens the match to AMBIGUOUS, it does not erase
    /// the agreement of number, date, currency and total. Difference-proof still wins first —
    /// a different legal company, a different currency or totals outside the financial-integrity
    /// tolerance prove distinct commercial acts (the approved CONSULTIT rule), whatever the
    /// shared reference.
    /// </summary>
    private static BusinessDuplicateDecision JudgePair(
        BusinessDuplicateCandidate candidate, BusinessDuplicateComparand other)
    {
        var matching = new List<string> { BusinessDuplicateFields.DocumentNumber };
        var conflicting = new List<string>();

        // ── Supplier identity signals ──
        var idsKnown = candidate.SupplierId.HasValue && other.SupplierId.HasValue;
        var idEqual = idsKnown && candidate.SupplierId == other.SupplierId;

        var candidateNif = PaymentSourceDocumentFingerprint.NormalizeReference(candidate.SupplierTaxId);
        var otherNif = PaymentSourceDocumentFingerprint.NormalizeReference(other.SupplierTaxId);
        var nifsKnown = candidateNif.Length > 0 && otherNif.Length > 0;
        var nifEqual = nifsKnown && string.Equals(candidateNif, otherNif, StringComparison.Ordinal);

        var candidateName = NormalizeText(candidate.SupplierName);
        var otherName = NormalizeText(other.SupplierName);
        var namesKnown = candidateName.Length > 0 && otherName.Length > 0;
        var nameEqual = namesKnown && string.Equals(candidateName, otherName, StringComparison.Ordinal);

        var supplierStrong = idEqual || nifEqual;
        var supplierProbable = supplierStrong || nameEqual;

        if (supplierStrong) matching.Add(BusinessDuplicateFields.Supplier);
        else if (nameEqual) matching.Add(BusinessDuplicateFields.SupplierName);
        if (nifsKnown && !nifEqual) conflicting.Add(BusinessDuplicateFields.SupplierNif);
        if (namesKnown && !nameEqual) conflicting.Add(BusinessDuplicateFields.SupplierName);

        // ── Document signals ──
        var datesKnown = candidate.DocumentDate.HasValue && other.DocumentDate.HasValue;
        var dateEqual = datesKnown && candidate.DocumentDate!.Value.Date == other.DocumentDate!.Value.Date;
        if (dateEqual) matching.Add(BusinessDuplicateFields.DocumentDate);
        else if (datesKnown) conflicting.Add(BusinessDuplicateFields.DocumentDate);

        var currenciesKnown =
            !string.IsNullOrWhiteSpace(candidate.Currency) && !string.IsNullOrWhiteSpace(other.Currency);
        var currencyEqual = currenciesKnown && string.Equals(
            candidate.Currency!.Trim(), other.Currency!.Trim(), StringComparison.OrdinalIgnoreCase);
        if (currencyEqual) matching.Add(BusinessDuplicateFields.Currency);
        else if (currenciesKnown) conflicting.Add(BusinessDuplicateFields.Currency);

        var grossKnown = candidate.GrossAmount.HasValue && other.GrossAmount.HasValue;
        var grossWithin = false;
        if (grossKnown)
        {
            var tolerance = RequestConstants.FinancialIntegrity.CalculateTolerance(other.GrossAmount!.Value);
            grossWithin = Math.Abs(candidate.GrossAmount!.Value - other.GrossAmount.Value) <= tolerance;
            if (grossWithin) matching.Add(BusinessDuplicateFields.GrossAmount);
            else conflicting.Add(BusinessDuplicateFields.GrossAmount);
        }

        var companiesKnown = candidate.CompanyId.HasValue;
        var companyEqual = companiesKnown && candidate.CompanyId == other.CompanyId;
        if (companyEqual) matching.Add(BusinessDuplicateFields.Company);
        else if (companiesKnown) conflicting.Add(BusinessDuplicateFields.Company);

        BusinessDuplicateDecision Build(
            BusinessDuplicateVerdict verdict, BusinessDuplicateClassification classification, string reason) =>
            new()
            {
                Verdict = verdict,
                Classification = classification,
                Match = other,
                Reason = reason,
                MatchingFields = matching,
                ConflictingFields = conflicting
            };

        // ── Difference-proof first (LEVEL 3, informational): distinct commercial acts ──
        if (companiesKnown && !companyEqual)
            return Build(BusinessDuplicateVerdict.Allow, BusinessDuplicateClassification.RelatedDocument,
                "mesma referência, outra empresa — documentos distintos");
        if (currenciesKnown && !currencyEqual)
            return Build(BusinessDuplicateVerdict.Allow, BusinessDuplicateClassification.RelatedDocument,
                "mesma referência, moeda diferente — documentos distintos");
        if (grossKnown && !grossWithin)
            return Build(BusinessDuplicateVerdict.Allow, BusinessDuplicateClassification.RelatedDocument,
                "mesma referência, total materialmente diferente — documentos distintos");

        // The complete commercial identity, provable without content: strong supplier (id/NIF),
        // same reference, same date, same currency, totals within tolerance.
        var fullCommercialIdentity = supplierStrong && dateEqual && currencyEqual && grossWithin;

        // ── Content fingerprint, when both sides have one ──
        var fingerprintsKnown = candidate.ItemFingerprint != null && other.ItemFingerprint != null;
        if (fingerprintsKnown)
        {
            if (!string.Equals(candidate.ItemFingerprint, other.ItemFingerprint, StringComparison.Ordinal))
            {
                conflicting.Add(BusinessDuplicateFields.Content);

                // A fingerprint difference is representation-sensitive evidence (OCR variation,
                // regenerated PDF, line wrapping, textual correction). It must never OUTRANK the
                // complete commercial identity: with supplier, reference, date, currency and total
                // all agreeing, the pair requires justified review — content inequality alone
                // cannot make it frictionless. Without that full identity, differing content keeps
                // the approved LEVEL 3 meaning: distinct commercial acts, informational only.
                if (fullCommercialIdentity)
                {
                    return Build(BusinessDuplicateVerdict.Ambiguous,
                        BusinessDuplicateClassification.AmbiguousMatch,
                        "mesma identidade comercial mas conteúdo lido diferente — requer revisão");
                }

                return Build(BusinessDuplicateVerdict.Allow, BusinessDuplicateClassification.RelatedDocument,
                    "mesma referência, conteúdo comercial diferente — documentos distintos");
            }

            matching.Add(BusinessDuplicateFields.Content);

            // LEVEL 2 requires every strong condition at once: strong supplier identity, same
            // currency, totals within tolerance AND identical content. Anything unproven demotes
            // to the override path rather than a wall.
            if (supplierStrong && grossKnown && currenciesKnown)
            {
                return Build(BusinessDuplicateVerdict.Block, BusinessDuplicateClassification.SemanticDuplicate,
                    "mesmo fornecedor, mesma referência e conteúdo comercial idêntico");
            }

            return Build(BusinessDuplicateVerdict.Ambiguous, BusinessDuplicateClassification.AmbiguousMatch,
                "conteúdo idêntico mas identidade não totalmente comprovável");
        }

        // ── No content proof: a candidate needs a conservative evidence floor ──
        // Number alone, or number + one weak agreement, is not a candidate — different suppliers
        // legitimately share numbering schemes.
        var corroborating = (dateEqual ? 1 : 0) + (currencyEqual ? 1 : 0) + (grossWithin ? 1 : 0);
        if (!supplierProbable && corroborating < 2)
            return Build(BusinessDuplicateVerdict.Allow, BusinessDuplicateClassification.None,
                "referência coincidente sem evidência corroborante");

        // The strongest business-level condition without content proof: same probable supplier
        // (by id or NIF), same reference, same date, same currency, totals within tolerance.
        // Different bytes are NEVER evidence of a new commercial document.
        if (fullCommercialIdentity)
        {
            return Build(BusinessDuplicateVerdict.Ambiguous,
                BusinessDuplicateClassification.StrongBusinessDuplicate,
                "mesma identidade comercial (fornecedor, referência, data, moeda e total)");
        }

        return Build(BusinessDuplicateVerdict.Ambiguous, BusinessDuplicateClassification.AmbiguousMatch,
            conflicting.Count > 0
                ? "candidato forte com campos em conflito"
                : "mesma referência sem evidência de conteúdo suficiente para comparar");
    }

    /// <summary>Uppercased, whitespace-collapsed, punctuation-light text identity for names.</summary>
    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new System.Text.StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch) || ch == ',' || ch == '.' || ch == ';') { pendingSpace = true; continue; }
            if (pendingSpace && builder.Length > 0) builder.Append(' ');
            pendingSpace = false;
            builder.Append(char.ToUpperInvariant(ch));
        }
        return builder.ToString();
    }
}
