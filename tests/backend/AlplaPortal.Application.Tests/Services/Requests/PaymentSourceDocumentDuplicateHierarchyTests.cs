using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// LEVELS 2–4 of the duplicate-document hierarchy (v2.229.10).
///
/// <para>The defect this replaces: supplier + document number + series was treated as a document
/// identity and hard-blocked, forcing users to falsify real supplier references. CONSULTIT's
/// <c>ONP_18910_v3</c> names four materially different proposals — all legitimate. The hierarchy
/// blocks outright only when reference AND commercial content are both identical; header equality
/// without content evidence is AMBIGUOUS (explicit audited override), never a silent wall.</para>
/// </summary>
public class PaymentSourceDocumentDuplicateHierarchyTests
{
    private const string ConsultitReference = "ONP_18910_v3";

    private static List<DuplicateFingerprintItem> Items(params (string Desc, decimal Qty, decimal Price)[] lines) =>
        lines.Select(l => new DuplicateFingerprintItem(l.Desc, l.Qty, l.Price, l.Qty * l.Price)).ToList();

    private static BusinessDuplicateCandidate Candidate(
        string number = ConsultitReference, string? series = null, int companyId = 1,
        string? currency = "AOA", decimal? gross = 1_000_000m, string? fingerprint = null,
        int? supplierId = 77, string? supplierName = null, string? supplierTaxId = null,
        DateTime? date = null) => new()
    {
        DocumentNumber = number,
        DocumentSeries = series,
        SupplierId = supplierId,
        SupplierName = supplierName,
        SupplierTaxId = supplierTaxId,
        DocumentDate = date,
        CompanyId = companyId,
        Currency = currency,
        GrossAmount = gross,
        ItemFingerprint = fingerprint
    };

    private static BusinessDuplicateComparand Existing(
        string number = ConsultitReference, string? series = null, int companyId = 1,
        string? currency = "AOA", decimal? gross = 1_000_000m, string? fingerprint = null,
        BusinessDuplicateScope scope = BusinessDuplicateScope.SameRequest,
        string? requestNumber = null,
        int? supplierId = 77, string? supplierName = null, string? supplierTaxId = null,
        DateTime? date = null) => new()
    {
        Id = Guid.NewGuid(),
        SequenceNumber = 1,
        DocumentNumber = number,
        DocumentSeries = series,
        SupplierId = supplierId,
        SupplierName = supplierName,
        SupplierTaxId = supplierTaxId,
        DocumentDate = date,
        CompanyId = companyId,
        Currency = currency,
        GrossAmount = gross,
        ItemFingerprint = fingerprint,
        Scope = scope,
        RequestNumber = requestNumber
    };

    // ── The content fingerprint itself ──────────────────────────────────────────────────────

    [Fact]
    public void The_fingerprint_is_deterministic_and_order_independent()
    {
        var forward = PaymentSourceDocumentFingerprint.Compute(Items(
            ("Câmara IP 4MP", 12m, 85_000m), ("Switch PoE 16 portas", 2m, 240_000m)));
        var reversed = PaymentSourceDocumentFingerprint.Compute(Items(
            ("Switch PoE 16 portas", 2m, 240_000m), ("Câmara IP 4MP", 12m, 85_000m)));

        Assert.NotNull(forward);
        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void The_fingerprint_normalizes_case_and_whitespace_but_not_content()
    {
        var a = PaymentSourceDocumentFingerprint.Compute(Items(("Câmara  IP   4MP", 12m, 85_000m)));
        var b = PaymentSourceDocumentFingerprint.Compute(Items(("CÂMARA IP 4MP", 12m, 85_000m)));
        var c = PaymentSourceDocumentFingerprint.Compute(Items(("Câmara IP 8MP", 12m, 85_000m)));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void No_items_means_no_fingerprint_never_an_empty_one()
    {
        Assert.Null(PaymentSourceDocumentFingerprint.Compute(new List<DuplicateFingerprintItem>()));
        Assert.Null(PaymentSourceDocumentFingerprint.Compute(null));
    }

    [Theory]
    [InlineData("ONP_18910_v3", "ONP 18910 V3")]
    [InlineData("ONP_18910_v3", "onp-18910-v3")]
    [InlineData("ONP_18910_v3", "ONP.18910.V3")]
    public void Reference_normalization_sees_through_separator_styles(string a, string b)
    {
        Assert.Equal(
            PaymentSourceDocumentFingerprint.NormalizeReference(a),
            PaymentSourceDocumentFingerprint.NormalizeReference(b));
    }

    [Fact]
    public void Reference_normalization_never_merges_different_references()
    {
        Assert.NotEqual(
            PaymentSourceDocumentFingerprint.NormalizeReference("ONP_18910_v3"),
            PaymentSourceDocumentFingerprint.NormalizeReference("ONP_18910_v4"));
    }

    // ── LEVEL 3: materially different documents are allowed ─────────────────────────────────

    [Fact]
    public void The_consultit_case_same_reference_different_totals_is_allowed()
    {
        // The four real proposals: Decoder 1.301.655,95 · CCTV Viana02 3.433.527,55 ·
        // CCTV Viana01 2.856.658,96 · Reestruturação Bastidor 1.492.231,88 — all ONP_18910_v3.
        var candidate = Candidate(gross: 3_433_527.55m);
        var existing = new[]
        {
            Existing(gross: 2_856_658.96m),
            Existing(gross: 1_492_231.88m)
        };

        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(candidate, existing);

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
    }

    [Fact]
    public void A_different_legal_company_proves_documentary_distinctness()
    {
        // Decoder (AlplaSOPRO) vs the Plásticos proposals: same supplier, same reference, another
        // legal company → distinct documents. WHERE each may live is still decided by the separate
        // one-request-one-company guard, which this hierarchy neither implements nor relaxes.
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(companyId: 2, gross: 1_301_655.95m),
            new[] { Existing(companyId: 1, gross: 1_301_655.95m, scope: BusinessDuplicateScope.OtherRequest) });

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
    }

    [Fact]
    public void A_different_currency_proves_distinctness()
    {
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(currency: "USD"),
            new[] { Existing(currency: "AOA") });

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
    }

    [Fact]
    public void A_substantially_different_item_set_proves_distinctness()
    {
        var cameras = PaymentSourceDocumentFingerprint.Compute(Items(("Câmara IP 4MP", 12m, 85_000m)));
        var racks = PaymentSourceDocumentFingerprint.Compute(Items(("Reestruturação do bastidor", 1m, 1_020_000m)));

        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(fingerprint: cameras),
            new[] { Existing(fingerprint: racks) });

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
    }

    [Fact]
    public void Cross_request_same_reference_with_different_content_is_allowed()
    {
        // Instruction M: the reference alone must never manufacture double-payment protection
        // around legitimately different proposals living in different requests.
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: 1_301_655.95m),
            new[] { Existing(gross: 2_856_658.96m,
                             scope: BusinessDuplicateScope.OtherRequest, requestNumber: "REQ-001") });

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
    }

    [Fact]
    public void A_different_reference_is_not_even_a_candidate()
    {
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(number: "ONP_18910_v4"),
            new[] { Existing(number: "ONP_18910_v3") });

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
        Assert.Null(decision.Match);
    }

    // ── LEVEL 2: proven semantic duplicates are blocked ─────────────────────────────────────

    [Fact]
    public void Identical_reference_company_currency_totals_and_content_is_blocked()
    {
        var fingerprint = PaymentSourceDocumentFingerprint.Compute(Items(("Câmara IP 4MP", 12m, 85_000m)));

        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: 1_020_000m, fingerprint: fingerprint),
            new[] { Existing(gross: 1_020_000m, fingerprint: fingerprint) });

        Assert.Equal(BusinessDuplicateVerdict.Block, decision.Verdict);
        Assert.NotNull(decision.Match);
    }

    [Fact]
    public void Totals_within_the_financial_integrity_tolerance_still_count_as_identical()
    {
        var fingerprint = PaymentSourceDocumentFingerprint.Compute(Items(("Serviço", 1m, 1_000_000m)));

        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: 1_000_000.50m, fingerprint: fingerprint),
            new[] { Existing(gross: 1_000_000m, fingerprint: fingerprint) });

        Assert.Equal(BusinessDuplicateVerdict.Block, decision.Verdict);
    }

    [Fact]
    public void A_reference_style_variant_of_the_same_content_is_still_blocked()
    {
        var fingerprint = PaymentSourceDocumentFingerprint.Compute(Items(("Serviço", 1m, 500_000m)));

        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(number: "ONP 18910 V3", gross: 500_000m, fingerprint: fingerprint),
            new[] { Existing(number: "onp-18910-v3", gross: 500_000m, fingerprint: fingerprint) });

        Assert.Equal(BusinessDuplicateVerdict.Block, decision.Verdict);
    }

    // ── LEVEL 4: header equality without content evidence is AMBIGUOUS, never a hard block ──

    [Fact]
    public void Header_equality_without_item_evidence_is_ambiguous_not_blocked()
    {
        // The mandatory refinement: identical headers with no fingerprint on either side must not
        // hard-block — content equivalence is unproven, so the user decides, audited.
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: 1_000_000m, fingerprint: null),
            new[] { Existing(gross: 1_000_000m, fingerprint: null) });

        Assert.Equal(BusinessDuplicateVerdict.Ambiguous, decision.Verdict);
        Assert.NotNull(decision.Match);
    }

    [Fact]
    public void One_sided_item_evidence_is_still_ambiguous()
    {
        var fingerprint = PaymentSourceDocumentFingerprint.Compute(Items(("Serviço", 1m, 1_000_000m)));

        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: 1_000_000m, fingerprint: null),
            new[] { Existing(gross: 1_000_000m, fingerprint: fingerprint) });

        Assert.Equal(BusinessDuplicateVerdict.Ambiguous, decision.Verdict);
    }

    [Fact]
    public void Missing_totals_leave_identical_content_ambiguous_rather_than_blocked()
    {
        // Equal fingerprints without provable totals is not "strong" evidence — LEVEL 2 requires
        // every condition at once.
        var fingerprint = PaymentSourceDocumentFingerprint.Compute(Items(("Serviço", 1m, 1_000_000m)));

        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: null, fingerprint: fingerprint),
            new[] { Existing(gross: null, fingerprint: fingerprint) });

        Assert.Equal(BusinessDuplicateVerdict.Ambiguous, decision.Verdict);
    }

    [Fact]
    public void A_proven_block_outranks_an_ambiguous_neighbour()
    {
        var fingerprint = PaymentSourceDocumentFingerprint.Compute(Items(("Serviço", 1m, 1_000_000m)));

        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: 1_000_000m, fingerprint: fingerprint),
            new[]
            {
                Existing(gross: 1_000_000m, fingerprint: null),          // ambiguous pair
                Existing(gross: 1_000_000m, fingerprint: fingerprint)    // proven duplicate
            });

        Assert.Equal(BusinessDuplicateVerdict.Block, decision.Verdict);
    }

    [Fact]
    public void A_missing_document_number_matches_nothing()
    {
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(number: ""),
            new[] { Existing() });

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
    }

    [Fact]
    public void The_series_distinguishes_two_suppliers_numbering_schemes()
    {
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(series: "A"),
            new[] { Existing(series: "B") });

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
    }

    // ── L4 candidate matching: supplier identity is a SIGNAL, never a gate ──────────────────

    [Fact]
    public void A_supplier_nif_mismatch_produces_an_ambiguous_match_not_a_new_document()
    {
        // L4-1, the CONSULTIT evidence case: same name, same reference, same date, same currency,
        // same total — only the NIF differs (misread or changed). The old logic never even
        // searched (supplier unresolved → new document). Now the agreement of the other signals
        // survives and the mismatch is reported as the reason.
        var date = new DateTime(2026, 7, 23);
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(supplierId: null, supplierName: "CONSULTIT, LDA", supplierTaxId: "5000000000",
                      gross: 1_492_231.88m, date: date),
            new[] { Existing(supplierId: 77, supplierName: "CONSULTIT, LDA", supplierTaxId: "5417049840",
                             gross: 1_492_231.88m, date: date) });

        Assert.Equal(BusinessDuplicateVerdict.Ambiguous, decision.Verdict);
        Assert.Equal(BusinessDuplicateClassification.AmbiguousMatch, decision.Classification);
        Assert.Contains(BusinessDuplicateFields.SupplierNif, decision.ConflictingFields);
        Assert.Contains(BusinessDuplicateFields.SupplierName, decision.MatchingFields);
        Assert.Contains(BusinessDuplicateFields.DocumentDate, decision.MatchingFields);
        Assert.Contains(BusinessDuplicateFields.GrossAmount, decision.MatchingFields);
    }

    [Fact]
    public void The_same_commercial_identity_in_a_different_file_is_a_strong_business_duplicate()
    {
        // L4-6: same strong supplier (id/NIF), same reference, same date, same currency, totals
        // within tolerance. Different bytes are never evidence of a new commercial document.
        var date = new DateTime(2026, 7, 23);
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: 1_492_231.88m, date: date),
            new[] { Existing(gross: 1_492_231.88m, date: date) });

        Assert.Equal(BusinessDuplicateVerdict.Ambiguous, decision.Verdict);   // justified override, not a wall
        Assert.Equal(BusinessDuplicateClassification.StrongBusinessDuplicate, decision.Classification);
    }

    [Fact]
    public void A_date_conflict_downgrades_the_strong_duplicate_to_ambiguous()
    {
        // L4-3: everything else agrees but the date differs — a strong candidate with a conflict.
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: 1_492_231.88m, date: new DateTime(2026, 7, 24)),
            new[] { Existing(gross: 1_492_231.88m, date: new DateTime(2026, 7, 23)) });

        Assert.Equal(BusinessDuplicateVerdict.Ambiguous, decision.Verdict);
        Assert.Equal(BusinessDuplicateClassification.AmbiguousMatch, decision.Classification);
        Assert.Contains(BusinessDuplicateFields.DocumentDate, decision.ConflictingFields);
    }

    [Fact]
    public void Content_inequality_cannot_outrank_the_complete_commercial_identity()
    {
        // Strong supplier + same reference + same date + same currency + same gross, but the two
        // content fingerprints differ (OCR variation, regenerated PDF, line wrapping…). The
        // representation-sensitive evidence must NOT silently downgrade the full commercial
        // identity to a frictionless related document — the pair requires justified review.
        var date = new DateTime(2026, 7, 23);
        var a = PaymentSourceDocumentFingerprint.Compute(Items(("Câmara IP 4MP", 12m, 85_000m)));
        var b = PaymentSourceDocumentFingerprint.Compute(Items(("Camara IP 4MP (rev)", 12m, 85_000m)));

        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: 1_492_231.88m, date: date, fingerprint: a),
            new[] { Existing(gross: 1_492_231.88m, date: date, fingerprint: b) });

        Assert.Equal(BusinessDuplicateVerdict.Ambiguous, decision.Verdict);          // review required
        Assert.Equal(BusinessDuplicateClassification.AmbiguousMatch, decision.Classification);
        Assert.Contains(BusinessDuplicateFields.Content, decision.ConflictingFields);
        Assert.Contains(BusinessDuplicateFields.DocumentDate, decision.MatchingFields);
        Assert.Contains(BusinessDuplicateFields.GrossAmount, decision.MatchingFields);
    }

    [Fact]
    public void Content_inequality_without_the_full_identity_keeps_the_approved_related_rule()
    {
        // No date agreement → the identity is not complete → differing content keeps meaning
        // "distinct commercial acts" (the approved LEVEL 3 behavior), informational only.
        var a = PaymentSourceDocumentFingerprint.Compute(Items(("Câmara IP 4MP", 12m, 85_000m)));
        var b = PaymentSourceDocumentFingerprint.Compute(Items(("Reestruturação do bastidor", 1m, 1_020_000m)));

        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(fingerprint: a),
            new[] { Existing(fingerprint: b) });

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
        Assert.Equal(BusinessDuplicateClassification.RelatedDocument, decision.Classification);
    }

    [Fact]
    public void A_number_only_match_with_no_corroboration_is_not_a_candidate()
    {
        // The false-positive floor: different suppliers legitimately share numbering schemes, so
        // a bare reference coincidence — no supplier evidence, no date, no total — is nothing.
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(supplierId: null, gross: null, currency: null),
            new[] { Existing(supplierId: 77, supplierName: "OUTRA EMPRESA", supplierTaxId: "999",
                             gross: 500m, currency: "AOA") });

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
        Assert.Equal(BusinessDuplicateClassification.None, decision.Classification);
    }

    [Fact]
    public void A_totals_difference_is_reported_as_a_related_document_not_a_candidate()
    {
        // L4-5 / CONSULTIT: informational only — persistence keeps allowing without friction.
        var decision = PaymentSourceDocumentDuplicateHierarchy.Judge(
            Candidate(gross: 3_433_527.55m),
            new[] { Existing(gross: 2_856_658.96m) });

        Assert.Equal(BusinessDuplicateVerdict.Allow, decision.Verdict);
        Assert.Equal(BusinessDuplicateClassification.RelatedDocument, decision.Classification);
        Assert.Contains(BusinessDuplicateFields.GrossAmount, decision.ConflictingFields);
    }

    [Fact]
    public void Evaluate_all_orders_candidates_by_severity()
    {
        var date = new DateTime(2026, 7, 23);
        var all = PaymentSourceDocumentDuplicateHierarchy.EvaluateAll(
            Candidate(gross: 1_492_231.88m, date: date),
            new[]
            {
                Existing(gross: 2_856_658.96m),                          // related (totals differ)
                Existing(gross: 1_492_231.88m, date: date)               // strong business duplicate
            });

        Assert.Equal(2, all.Count);
        Assert.Equal(BusinessDuplicateClassification.StrongBusinessDuplicate, all[0].Classification);
        Assert.Equal(BusinessDuplicateClassification.RelatedDocument, all[1].Classification);
    }

    // ── v2.229.10 monetary reconciliation: fingerprint stability under residual allocation ──

    [Fact]
    public void The_fingerprint_is_stable_across_repeated_residual_reconciliation()
    {
        // Two independent reads of the same document produce the same canonical line totals and
        // the same declared gross → the deterministic residual rule adjusts the same line by the
        // same amount → identical fingerprints. Duplicate detection must not weaken because a
        // cent was attributed.
        var canonical = new[] { 1_000_000.00m, 1_433_527.54m, 1_000_000.00m };

        var firstRead = PaymentRoundingResidual.Allocate(canonical, 3_433_527.55m);
        var secondRead = PaymentRoundingResidual.Allocate(canonical, 3_433_527.55m);

        string? Fingerprint(System.Collections.Generic.IReadOnlyList<decimal> totals) =>
            PaymentSourceDocumentFingerprint.Compute(
                totals.Select((t, i) => new DuplicateFingerprintItem($"Linha {i + 1}", 1m, t, t)));

        Assert.True(firstRead.Applied);
        Assert.Equal(Fingerprint(firstRead.Totals), Fingerprint(secondRead.Totals));

        // And the adjusted set is genuinely a different content identity from the unreconciled
        // one — the cent lives in the hash, deterministically, on both sides of any comparison.
        Assert.NotEqual(Fingerprint(canonical), Fingerprint(firstRead.Totals));
    }
}
