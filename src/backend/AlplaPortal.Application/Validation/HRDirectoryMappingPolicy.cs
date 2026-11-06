namespace AlplaPortal.Application.Validation;

/// <summary>
/// Pure rules for the HR directory mapping workflow (/hr/directory). No I/O, fully unit-testable.
///
/// 1. Company map — the three vocabularies that already exist in the system, confirmed against PROD
///    (P3.5 / P3.6, 2026-11-05):
///      PrimaveraCompany.ALPLAPLASTICO → Companies.Code "APA" → DepartmentMasters.CompanyCode "AlplaPLASTICOS" (PRI2975140)
///      PrimaveraCompany.ALPLASOPRO    → Companies.Code "APS" → DepartmentMasters.CompanyCode "AlplaSOPRO"     (PRI297514003)
///    The department-master label keeps its exact spelling (final "S" in AlplaPLASTICOS).
/// 2. Mapping state — computed from PlantId, DepartmentMasterId and ManagerUserId only. The stored
///    HREmployee.IsMapped flag keeps its legacy meaning (any of the four fields) for existing callers.
/// 3. Suggestion state — a plant suggestion is ACTIONABLE only when: no plant assigned, the lookup that
///    produced it was complete for the current company scope, the employee was found in exactly one
///    company, and that company has exactly one active plant. The same rule feeds the KPI and the button.
/// 4. Company consistency — a plant and a department master must belong to the same company.
/// </summary>
public static class HRDirectoryMappingPolicy
{
    public sealed record CompanyMapEntry(
        string PrimaveraCompany,          // enum name used by the integration layer
        string SuggestedPlantSource,      // value stored in HREmployees.SuggestedPlantSource
        string CompanyCode,               // Companies.Code
        string DepartmentMasterCompanyCode); // DepartmentMasters.CompanyCode label (exact spelling)

    public static readonly IReadOnlyList<CompanyMapEntry> CompanyMap = new[]
    {
        new CompanyMapEntry("ALPLAPLASTICO", "PRIMAVERA:ALPLAPLASTICO", "APA", "AlplaPLASTICOS"),
        new CompanyMapEntry("ALPLASOPRO",    "PRIMAVERA:ALPLASOPRO",    "APS", "AlplaSOPRO"),
    };

    public static CompanyMapEntry? ByPrimaveraCompany(string? primaveraCompany)
        => primaveraCompany is null ? null
         : CompanyMap.FirstOrDefault(c => string.Equals(c.PrimaveraCompany, primaveraCompany, StringComparison.Ordinal));

    public static CompanyMapEntry? BySuggestedPlantSource(string? source)
        => source is null ? null
         : CompanyMap.FirstOrDefault(c => string.Equals(c.SuggestedPlantSource, source, StringComparison.Ordinal));

    public static CompanyMapEntry? ByCompanyCode(string? companyCode)
        => companyCode is null ? null
         : CompanyMap.FirstOrDefault(c => string.Equals(c.CompanyCode, companyCode, StringComparison.Ordinal));

    public static CompanyMapEntry? ByDepartmentMasterCompanyCode(string? label)
        => label is null ? null
         : CompanyMap.FirstOrDefault(c => string.Equals(c.DepartmentMasterCompanyCode, label, StringComparison.Ordinal));

    // ───────────────────────── mapping state ─────────────────────────

    public const string StateComplete = "COMPLETE";
    public const string StatePartial = "PARTIAL";
    public const string StateUnmapped = "UNMAPPED";

    /// <summary>Complete = all three; Unmapped = none; Partial = otherwise. PortalDepartmentId is ignored.</summary>
    public static string ComputeMappingState(int? plantId, int? departmentMasterId, Guid? managerUserId)
    {
        var set = (plantId.HasValue ? 1 : 0) + (departmentMasterId.HasValue ? 1 : 0) + (managerUserId.HasValue ? 1 : 0);
        return set switch { 3 => StateComplete, 0 => StateUnmapped, _ => StatePartial };
    }

    /// <summary>Legacy flag semantics, unchanged: true when any of the four Portal fields is set.</summary>
    public static bool ComputeLegacyIsMapped(int? plantId, int? portalDepartmentId, int? departmentMasterId, Guid? managerUserId)
        => plantId.HasValue || portalDepartmentId.HasValue || departmentMasterId.HasValue || managerUserId.HasValue;

    // ───────────────────────── suggestion state ─────────────────────────

    public const string SuggestionActionable = "ACTIONABLE";   // one-click prefill allowed
    public const string SuggestionNeedsChoice = "NEEDS_CHOICE"; // single company, several plants
    public const string SuggestionManual = "MANUAL";            // found in both companies
    public const string SuggestionUnverified = "UNVERIFIED";    // produced by a partial/legacy lookup
    public const string SuggestionNone = "NONE";

    public const string SourceMulti = "PRIMAVERA:MULTI";
    public const string SourceNotFound = "PRIMAVERA:NOT_FOUND";
    public const string SourceNoCode = "PRIMAVERA:NO_CODE";

    /// <summary>
    /// Canonical scope string for a set of company codes, e.g. "APA|APS". Order-independent.
    /// Stored in HREmployees.SuggestedPlantLookupScope and compared against the current configuration.
    /// </summary>
    public static string BuildLookupScope(IEnumerable<string> companyCodes)
        => string.Join("|", companyCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal));

    public static string ComputeSuggestionState(
        int? plantId,
        string? suggestedPlantSource,
        bool? lookupComplete,
        string? lookupScope,
        string currentScope,
        int activePlantsInSuggestedCompany)
    {
        if (plantId.HasValue) return SuggestionNone;
        if (string.IsNullOrWhiteSpace(suggestedPlantSource)) return SuggestionNone;
        if (suggestedPlantSource == SourceNotFound || suggestedPlantSource == SourceNoCode) return SuggestionNone;

        // Completeness gate: legacy rows (null) and partial lookups are never actionable.
        if (lookupComplete != true) return SuggestionUnverified;
        if (!string.Equals(lookupScope, currentScope, StringComparison.Ordinal)) return SuggestionUnverified;

        if (suggestedPlantSource == SourceMulti) return SuggestionManual;

        if (BySuggestedPlantSource(suggestedPlantSource) is null) return SuggestionNone; // unknown company label

        return activePlantsInSuggestedCompany switch
        {
            1 => SuggestionActionable,
            > 1 => SuggestionNeedsChoice,
            _ => SuggestionNone,
        };
    }

    // ───────────────────────── company consistency ─────────────────────────

    /// <summary>
    /// True when the plant's company and the department master's company agree, or when either side is absent.
    /// A master label that is not in the map is treated as inconsistent with any plant (fail closed).
    /// </summary>
    public static bool IsCompanyConsistent(string? plantCompanyCode, string? departmentMasterCompanyCode)
    {
        if (string.IsNullOrWhiteSpace(plantCompanyCode) || string.IsNullOrWhiteSpace(departmentMasterCompanyCode)) return true;
        var byMaster = ByDepartmentMasterCompanyCode(departmentMasterCompanyCode);
        if (byMaster is null) return false;
        return string.Equals(byMaster.CompanyCode, plantCompanyCode, StringComparison.Ordinal);
    }
}
