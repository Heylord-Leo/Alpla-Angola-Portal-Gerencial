using System;
using System.Linq;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// The deterministic Primavera purchase-order grammar (ECF / ECF10 / ECF11) and the
/// canonical-identity duplicate rule — the fix for NIF-as-PO-number extraction
/// (e.g. REQ-20/07/2026-101 storing Gasp's NIF 5001713205 as its P.O.).
/// </summary>
public class PrimaveraPoReferenceTests
{
    private static readonly string[] KnownNifs =
        { "5001713205", "5417549142", "5410002539", "5417567485", "5001760246" };

    // ── P1-P3: the three official layouts ───────────────────────────────────────────────────

    [Fact]
    public void P1_ecf11_services_title_parses_to_the_canonical_identity()
    {
        var parse = PrimaveraPoReference.TryParse("PO Serviços ECF11 2026/421");

        Assert.NotNull(parse);
        Assert.Equal("ECF11", parse!.Family);
        Assert.Equal("ECF11 2026/421", parse.Display);
        Assert.Equal("ECF11-2026-421", parse.Canonical);
    }

    [Fact]
    public void P2_ecf10_materials_title_parses()
    {
        var parse = PrimaveraPoReference.TryParse("Encomenda Mat Escritório/Diversos ECF10 2026/219");

        Assert.Equal("ECF10-2026-219", parse!.Canonical);
    }

    [Fact]
    public void P3_ecf_stock_title_parses()
    {
        var parse = PrimaveraPoReference.TryParse("Encomenda a Fornecedor ECF 2026/107");

        Assert.Equal("ECF-2026-107", parse!.Canonical);
    }

    // ── P4 / P12: fiscal numbers can never become PO numbers ────────────────────────────────

    [Theory]
    [InlineData("5001713205")]   // Gasp
    [InlineData("5417549142")]   // A.D CERTO
    [InlineData("5410002539")]   // SINTESE
    [InlineData("5417567485")]   // ALPLA PLASTICO
    [InlineData("5001760246")]   // ALPLA SOPRO
    public void P4_P12_nifs_are_forbidden_po_numbers_and_never_parse(string nif)
    {
        Assert.Null(PrimaveraPoReference.TryParse(nif));
        Assert.True(PrimaveraPoReference.IsForbiddenPoNumber(nif, KnownNifs));
        Assert.True(PrimaveraPoReference.LooksLikeNif(nif));
    }

    [Fact]
    public void A_positive_reference_is_never_forbidden()
    {
        // Positive identification always wins — a real reference never trips the backstop.
        Assert.False(PrimaveraPoReference.IsForbiddenPoNumber("ECF11 2026/421", KnownNifs));
    }

    [Fact]
    public void An_unknown_ten_digit_number_is_still_shape_rejected()
    {
        Assert.True(PrimaveraPoReference.IsForbiddenPoNumber("5099999999", KnownNifs));
    }

    [Fact]
    public void Letter_bearing_supplier_references_are_not_forbidden()
    {
        // FT/FP/FA/PP/FTC-style values are legitimate non-Primavera references.
        Assert.False(PrimaveraPoReference.IsForbiddenPoNumber("FT8326S25689N/102", KnownNifs));
        Assert.False(PrimaveraPoReference.IsForbiddenPoNumber("FP 2026/3578", KnownNifs));
    }

    // ── P5: different sequences stay distinct ───────────────────────────────────────────────

    [Fact]
    public void P5_different_sequences_are_distinct_identities()
    {
        Assert.NotEqual(
            PrimaveraPoReference.TryParse("ECF11 2026/420")!.Canonical,
            PrimaveraPoReference.TryParse("ECF11 2026/421")!.Canonical);
    }

    // ── P6 / P11: duplicate scope — canonical identity + legal entity ───────────────────────

    private static readonly Guid GroupA = Guid.NewGuid();

    [Fact]
    public void P6_same_canonical_same_company_blocks()
    {
        var match = PrimaveraPoReference.Evaluate(
            "ecf11 2026-421", candidateCompanyId: 1,
            new[] { (GroupA, (string?)"PO Serviços ECF11 2026/421", 1, (string?)"REQ-X") });

        Assert.Equal(PoDuplicateVerdict.Block, match.Verdict);
        Assert.Equal(GroupA, match.GroupId);
    }

    [Fact]
    public void P11_same_canonical_other_company_is_informational_not_blocking()
    {
        // ALPLA Plástico and ALPLA SOPRO are separate legal entities with independent Primavera
        // sequences — ECF10 2026/219 existing on both must never demand an override.
        var match = PrimaveraPoReference.Evaluate(
            "ECF10 2026/219", candidateCompanyId: 2,
            new[] { (GroupA, (string?)"ECF10 2026/219", 1, (string?)"REQ-PLASTICO") });

        Assert.Equal(PoDuplicateVerdict.CrossCompanyInfo, match.Verdict);
    }

    // ── P7 / P9: formatting variants normalize together ─────────────────────────────────────

    [Theory]
    [InlineData("ECF11 2026/421")]
    [InlineData("ECF11 2026-421")]
    [InlineData("ECF11  2026 / 421")]
    [InlineData("ecf11 2026/421")]
    [InlineData("PO Serviços ECF11 2026/421")]      // P9: title-prefixed stored value
    public void P7_P9_formatting_variants_share_one_canonical_identity(string variant)
    {
        Assert.Equal("ECF11-2026-421", PrimaveraPoReference.TryParse(variant)!.Canonical);
    }

    // ── P8: the three families never collapse ───────────────────────────────────────────────

    [Fact]
    public void P8_families_remain_distinct_for_the_same_sequence()
    {
        var canonicals = new[] { "ECF 2026/219", "ECF10 2026/219", "ECF11 2026/219" }
            .Select(v => PrimaveraPoReference.TryParse(v)!.Canonical)
            .ToList();

        Assert.Equal(3, canonicals.Distinct().Count());
        Assert.Contains("ECF-2026-219", canonicals);
        Assert.Contains("ECF10-2026-219", canonicals);
        Assert.Contains("ECF11-2026-219", canonicals);
    }

    // ── P10: non-Primavera references remain supported (conservative normalization) ─────────

    [Fact]
    public void P10_non_primavera_references_compare_by_conservative_normalization()
    {
        var match = PrimaveraPoReference.Evaluate(
            "  fp 2026/3578 ", candidateCompanyId: 2,
            new[] { (GroupA, (string?)"FP 2026/3578", 1, (string?)"REQ-Y") });

        // Pre-existing global scope preserved for non-Primavera values.
        Assert.Equal(PoDuplicateVerdict.Block, match.Verdict);
    }

    [Fact]
    public void P10b_a_family_is_never_invented_for_a_familyless_value()
    {
        // "2026/107" (REQ-13/07/2026-038) must NOT match "ECF 2026/107" (REQ-11/08/2026-228):
        // the family cannot be guessed — the historical row stays flagged for repair instead.
        var match = PrimaveraPoReference.Evaluate(
            "ECF 2026/107", candidateCompanyId: 1,
            new[] { (GroupA, (string?)"2026/107", 1, (string?)"REQ-038") });

        Assert.Equal(PoDuplicateVerdict.None, match.Verdict);
    }

    [Fact]
    public void Family_words_without_a_full_reference_warn_rather_than_guess()
    {
        Assert.True(PrimaveraPoReference.MentionsFamilyWithoutReference("PO Serviços ECF11"));
        Assert.False(PrimaveraPoReference.MentionsFamilyWithoutReference("PO Serviços ECF11 2026/421"));
        Assert.False(PrimaveraPoReference.MentionsFamilyWithoutReference("FT 91819"));
    }

    // ── S-rules: supplier backfill eligibility (Population A/B discipline) ──────────────────

    [Fact]
    public void S1_deterministic_header_supplier_is_copyable()
    {
        var decision = PoSupplierBackfillRule.Evaluate(
            groupSupplierId: null, groupStatus: "WAITING_PO",
            requestSupplierId: 127, supplierExists: true, supplierIsActive: true);

        Assert.Equal(PoSupplierBackfillAction.CopyHeaderSupplier, decision.Action);
    }

    [Fact]
    public void S2_a_request_with_no_structured_supplier_is_never_guessed()
    {
        // The REQ-31/07/2026-193 shape.
        var decision = PoSupplierBackfillRule.Evaluate(
            groupSupplierId: null, groupStatus: "WAITING_PO",
            requestSupplierId: null, supplierExists: false, supplierIsActive: false);

        Assert.Equal(PoSupplierBackfillAction.RequiresManualConfirmation, decision.Action);
    }

    [Fact]
    public void S4_the_rule_is_deterministic()
    {
        var first = PoSupplierBackfillRule.Evaluate(null, "PENDING", 123, true, true);
        var second = PoSupplierBackfillRule.Evaluate(null, "PENDING", 123, true, true);

        Assert.Equal(first, second);
        Assert.Equal(PoSupplierBackfillAction.CopyHeaderSupplier, first.Action);
    }

    [Fact]
    public void S5_groups_whose_po_already_advanced_are_never_repaired()
    {
        // The REQ-192 shape: an active Release-4 lifecycle (WAITING_SUPPLIER_DELIVERY etc.) —
        // the backfill may never touch it, and the rule never proposes status changes at all.
        foreach (var status in new[] { "PO_ISSUED", "WAITING_SUPPLIER_DELIVERY", "PAYMENT_COMPLETED", "IN_FOLLOWUP", "COMPLETED" })
        {
            var decision = PoSupplierBackfillRule.Evaluate(null, status, 181, true, true);
            Assert.Equal(PoSupplierBackfillAction.Skip, decision.Action);
        }
    }

    [Fact]
    public void An_inactive_header_supplier_demands_confirmation_not_a_silent_copy()
    {
        var decision = PoSupplierBackfillRule.Evaluate(null, "WAITING_PO", 44, true, false);

        Assert.Equal(PoSupplierBackfillAction.RequiresManualConfirmation, decision.Action);
    }
}
