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
        string? currency = "AOA", decimal? gross = 1_000_000m, string? fingerprint = null) => new()
    {
        DocumentNumber = number,
        DocumentSeries = series,
        CompanyId = companyId,
        Currency = currency,
        GrossAmount = gross,
        ItemFingerprint = fingerprint
    };

    private static BusinessDuplicateComparand Existing(
        string number = ConsultitReference, string? series = null, int companyId = 1,
        string? currency = "AOA", decimal? gross = 1_000_000m, string? fingerprint = null,
        BusinessDuplicateScope scope = BusinessDuplicateScope.SameRequest,
        string? requestNumber = null) => new()
    {
        Id = Guid.NewGuid(),
        SequenceNumber = 1,
        DocumentNumber = number,
        DocumentSeries = series,
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
}
