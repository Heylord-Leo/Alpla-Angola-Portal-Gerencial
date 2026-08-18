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

/// <summary>An existing active document sharing the candidate's supplier, reduced for comparison.</summary>
public sealed record BusinessDuplicateComparand
{
    public Guid Id { get; init; }
    public int SequenceNumber { get; init; }
    public string? DocumentNumber { get; init; }
    public string? DocumentSeries { get; init; }
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
    public int CompanyId { get; init; }
    public string? Currency { get; init; }
    public decimal? GrossAmount { get; init; }
    public string? ItemFingerprint { get; init; }
}

public sealed record BusinessDuplicateDecision
{
    public BusinessDuplicateVerdict Verdict { get; init; } = BusinessDuplicateVerdict.Allow;
    /// <summary>The comparand that produced the verdict, when it is not Allow.</summary>
    public BusinessDuplicateComparand? Match { get; init; }
    /// <summary>Short pt-PT phrase describing why, for messages and the audit comment.</summary>
    public string? Reason { get; init; }

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

    public static BusinessDuplicateDecision Judge(
        BusinessDuplicateCandidate candidate,
        IEnumerable<BusinessDuplicateComparand> comparands)
    {
        var number = PaymentSourceDocumentFingerprint.NormalizeReference(candidate.DocumentNumber);
        if (number.Length == 0) return BusinessDuplicateDecision.Clear;

        var series = PaymentSourceDocumentFingerprint.NormalizeReference(candidate.DocumentSeries);

        BusinessDuplicateDecision? ambiguous = null;

        foreach (var other in comparands)
        {
            if (!string.Equals(
                    PaymentSourceDocumentFingerprint.NormalizeReference(other.DocumentNumber),
                    number, StringComparison.Ordinal))
                continue;
            if (!string.Equals(
                    PaymentSourceDocumentFingerprint.NormalizeReference(other.DocumentSeries),
                    series, StringComparison.Ordinal))
                continue;

            var pair = JudgePair(candidate, other);
            if (pair == null) continue;                                   // LEVEL 3 — provably different

            if (pair.Verdict == BusinessDuplicateVerdict.Block) return pair;  // strongest wins outright
            ambiguous ??= pair;
        }

        return ambiguous ?? BusinessDuplicateDecision.Clear;
    }

    /// <summary>
    /// One candidate-vs-existing comparison. Null means LEVEL 3: the pair is materially different
    /// and no message is owed. Difference evidence is checked first — a different legal company, a
    /// different currency or a gross total outside the financial-integrity tolerance each prove the
    /// documents are distinct commercial acts, whatever the shared reference.
    /// </summary>
    private static BusinessDuplicateDecision? JudgePair(
        BusinessDuplicateCandidate candidate, BusinessDuplicateComparand other)
    {
        // Different legal company → documentary identity is distinct (the one-request-one-company
        // guard still decides, separately, where each document may live).
        if (candidate.CompanyId != other.CompanyId) return null;

        var currenciesKnown =
            !string.IsNullOrWhiteSpace(candidate.Currency) && !string.IsNullOrWhiteSpace(other.Currency);
        if (currenciesKnown && !string.Equals(
                candidate.Currency!.Trim(), other.Currency!.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;

        var grossKnown = candidate.GrossAmount.HasValue && other.GrossAmount.HasValue;
        if (grossKnown)
        {
            var tolerance = RequestConstants.FinancialIntegrity.CalculateTolerance(other.GrossAmount!.Value);
            if (Math.Abs(candidate.GrossAmount!.Value - other.GrossAmount.Value) > tolerance)
                return null;                                              // materially different totals
        }

        var fingerprintsKnown = candidate.ItemFingerprint != null && other.ItemFingerprint != null;
        if (fingerprintsKnown)
        {
            if (!string.Equals(candidate.ItemFingerprint, other.ItemFingerprint, StringComparison.Ordinal))
                return null;                                              // different commercial content

            // LEVEL 2 requires every strong condition at once: same reference, same company, same
            // currency, totals within tolerance AND identical content. Missing amounts on either
            // side leave the totals unproven, and equality without proven totals is not "strong".
            if (grossKnown && currenciesKnown)
            {
                return new BusinessDuplicateDecision
                {
                    Verdict = BusinessDuplicateVerdict.Block,
                    Match = other,
                    Reason = "mesmo fornecedor, mesma referência e conteúdo comercial idêntico"
                };
            }
        }

        // Same reference and nothing left that can prove duplicate or distinct — AMBIGUOUS by
        // mandate, never a silent hard block on header equality alone.
        return new BusinessDuplicateDecision
        {
            Verdict = BusinessDuplicateVerdict.Ambiguous,
            Match = other,
            Reason = fingerprintsKnown
                ? "mesma referência com totais não comprováveis"
                : "mesma referência sem evidência de conteúdo suficiente para comparar"
        };
    }
}
