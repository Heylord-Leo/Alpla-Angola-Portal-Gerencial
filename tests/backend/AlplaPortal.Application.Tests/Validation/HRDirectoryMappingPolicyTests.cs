using System;
using AlplaPortal.Application.Validation;
using Xunit;

namespace AlplaPortal.Application.Tests.Validation;

/// <summary>
/// HR directory Phase 1 rules: company map (confirmed against PROD P3.5/P3.6 on 2026-11-05),
/// mapping state, legacy IsMapped compatibility, suggestion actionability and company consistency.
/// </summary>
public class HRDirectoryMappingPolicyTests
{
    private static readonly Guid Mgr = Guid.NewGuid();

    // ───────── company map ─────────

    [Theory]
    [InlineData("ALPLAPLASTICO", "PRIMAVERA:ALPLAPLASTICO", "APA", "AlplaPLASTICOS")]
    [InlineData("ALPLASOPRO", "PRIMAVERA:ALPLASOPRO", "APS", "AlplaSOPRO")]
    public void Company_map_matches_PROD_vocabularies_exactly(string primavera, string source, string companyCode, string masterLabel)
    {
        var byPrimavera = HRDirectoryMappingPolicy.ByPrimaveraCompany(primavera);
        Assert.NotNull(byPrimavera);
        Assert.Equal(source, byPrimavera!.SuggestedPlantSource);
        Assert.Equal(companyCode, byPrimavera.CompanyCode);
        Assert.Equal(masterLabel, byPrimavera.DepartmentMasterCompanyCode);

        Assert.Same(byPrimavera, HRDirectoryMappingPolicy.BySuggestedPlantSource(source));
        Assert.Same(byPrimavera, HRDirectoryMappingPolicy.ByCompanyCode(companyCode));
        Assert.Same(byPrimavera, HRDirectoryMappingPolicy.ByDepartmentMasterCompanyCode(masterLabel));
    }

    [Theory]
    [InlineData("AlplaPLASTICO")]   // missing final S — must NOT match
    [InlineData("alplaplasticos")]  // case differs — exact match only
    [InlineData("APA ")]
    [InlineData(null)]
    public void Company_map_is_exact_and_case_sensitive(string? label)
    {
        Assert.Null(HRDirectoryMappingPolicy.ByDepartmentMasterCompanyCode(label));
        Assert.Null(HRDirectoryMappingPolicy.ByCompanyCode(label));
    }

    // ───────── mapping state ─────────

    [Fact]
    public void Mapping_state_uses_only_plant_department_master_and_manager()
    {
        Assert.Equal("COMPLETE", HRDirectoryMappingPolicy.ComputeMappingState(1, 2, Mgr));
        Assert.Equal("UNMAPPED", HRDirectoryMappingPolicy.ComputeMappingState(null, null, null));
        Assert.Equal("PARTIAL", HRDirectoryMappingPolicy.ComputeMappingState(1, null, null));
        Assert.Equal("PARTIAL", HRDirectoryMappingPolicy.ComputeMappingState(null, 2, null));
        Assert.Equal("PARTIAL", HRDirectoryMappingPolicy.ComputeMappingState(null, null, Mgr));
        Assert.Equal("PARTIAL", HRDirectoryMappingPolicy.ComputeMappingState(1, 2, null));
    }

    [Fact]
    public void Legacy_IsMapped_still_counts_the_portal_department()
    {
        // A row with only the legacy PortalDepartmentId: IsMapped stays true (compatibility) but the state is UNMAPPED.
        Assert.True(HRDirectoryMappingPolicy.ComputeLegacyIsMapped(null, 7, null, null));
        Assert.Equal("UNMAPPED", HRDirectoryMappingPolicy.ComputeMappingState(null, null, null));
        Assert.False(HRDirectoryMappingPolicy.ComputeLegacyIsMapped(null, null, null, null));
    }

    // ───────── lookup scope ─────────

    [Fact]
    public void Lookup_scope_is_canonical_and_order_independent()
    {
        Assert.Equal("APA|APS", HRDirectoryMappingPolicy.BuildLookupScope(new[] { "APS", "APA" }));
        Assert.Equal("APA|APS", HRDirectoryMappingPolicy.BuildLookupScope(new[] { "APA", " APS ", "APA" }));
        Assert.Equal("APS", HRDirectoryMappingPolicy.BuildLookupScope(new[] { "APS" }));
        Assert.Equal("", HRDirectoryMappingPolicy.BuildLookupScope(Array.Empty<string>()));
    }

    // ───────── suggestion state ─────────

    private const string Scope = "APA|APS";

    [Fact]
    public void APS_only_with_single_active_plant_and_complete_lookup_is_actionable()
    {
        var state = HRDirectoryMappingPolicy.ComputeSuggestionState(null, "PRIMAVERA:ALPLASOPRO", true, Scope, Scope, activePlantsInSuggestedCompany: 1);
        Assert.Equal("ACTIONABLE", state);
    }

    [Fact]
    public void APA_only_with_two_active_plants_needs_a_choice()
    {
        var state = HRDirectoryMappingPolicy.ComputeSuggestionState(null, "PRIMAVERA:ALPLAPLASTICO", true, Scope, Scope, 2);
        Assert.Equal("NEEDS_CHOICE", state);
    }

    [Fact]
    public void Found_in_both_companies_stays_manual()
    {
        Assert.Equal("MANUAL", HRDirectoryMappingPolicy.ComputeSuggestionState(null, "PRIMAVERA:MULTI", true, Scope, Scope, 1));
    }

    [Theory]
    [InlineData(null, Scope)]      // legacy row: completeness unknown
    [InlineData(false, Scope)]     // partial lookup
    [InlineData(true, "APA")]      // resolved against a narrower scope than today's configuration
    [InlineData(true, null)]
    public void Incomplete_or_foreign_scope_lookups_are_unverified_even_when_otherwise_actionable(bool? complete, string? scope)
    {
        var state = HRDirectoryMappingPolicy.ComputeSuggestionState(null, "PRIMAVERA:ALPLASOPRO", complete, scope, Scope, 1);
        Assert.Equal("UNVERIFIED", state);
    }

    [Theory]
    [InlineData("PRIMAVERA:NOT_FOUND")]
    [InlineData("PRIMAVERA:NO_CODE")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PRIMAVERA:SOMETHING_ELSE")]
    public void No_usable_source_means_no_suggestion(string? source)
    {
        Assert.Equal("NONE", HRDirectoryMappingPolicy.ComputeSuggestionState(null, source, true, Scope, Scope, 1));
    }

    [Fact]
    public void Assigned_plant_or_zero_candidate_plants_means_no_suggestion()
    {
        Assert.Equal("NONE", HRDirectoryMappingPolicy.ComputeSuggestionState(3, "PRIMAVERA:ALPLASOPRO", true, Scope, Scope, 1));
        Assert.Equal("NONE", HRDirectoryMappingPolicy.ComputeSuggestionState(null, "PRIMAVERA:ALPLASOPRO", true, Scope, Scope, 0));
    }

    // ───────── company consistency ─────────

    [Theory]
    [InlineData("APA", "AlplaPLASTICOS", true)]
    [InlineData("APS", "AlplaSOPRO", true)]
    [InlineData("APA", "AlplaSOPRO", false)]
    [InlineData("APS", "AlplaPLASTICOS", false)]
    [InlineData("APA", "AlplaPLASTICO", false)]   // unknown label fails closed
    [InlineData(null, "AlplaSOPRO", true)]        // no plant: nothing to check
    [InlineData("APA", null, true)]               // no department: nothing to check
    [InlineData("", "", true)]
    public void Plant_and_department_master_must_share_a_company(string? plantCompany, string? masterLabel, bool expected)
    {
        Assert.Equal(expected, HRDirectoryMappingPolicy.IsCompanyConsistent(plantCompany, masterLabel));
    }
}
