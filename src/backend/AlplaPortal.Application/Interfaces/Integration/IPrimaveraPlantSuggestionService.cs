namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Read-only Primavera plant suggestion resolver.
///
/// Looks up employee codes across configured Primavera company databases
/// to infer which company (and therefore which plant) an employee belongs to.
///
/// This is advisory only — it never writes to Primavera and never modifies
/// confirmed Portal mapping fields (PlantId, DepartmentMasterId, ManagerUserId).
///
/// Inference rules:
/// - ALPLASOPRO match → High confidence → Viana 3
/// - ALPLAPLASTICO match → Ambiguous → Viana 1 or Viana 2 (user must choose)
/// - Multiple matches → Ambiguous
/// - No match → NotFound
/// </summary>
public interface IPrimaveraPlantSuggestionService
{
    /// <summary>
    /// Resolves plant suggestions for all unmapped employees (no PlantId)
    /// that have not been resolved yet or were resolved before the given cutoff.
    /// </summary>
    /// <returns>Number of employees enriched with suggestions.</returns>
    Task<PlantSuggestionResult> ResolveSuggestionsAsync(CancellationToken cancellationToken = default);
}

public class PlantSuggestionResult
{
    public int TotalProcessed { get; set; }
    public int HighConfidence { get; set; }
    public int Ambiguous { get; set; }
    public int NotFound { get; set; }
    public int AlreadyMapped { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = new();

    /// <summary>Canonical company scope of this run (e.g. "APA|APS").</summary>
    public string LookupScope { get; set; } = string.Empty;

    /// <summary>
    /// True when every configured company answered. When false, NOTHING was persisted:
    /// a partial lookup must never produce a suggestion that looks complete.
    /// </summary>
    public bool LookupComplete { get; set; }

    /// <summary>Employees that were due for resolution but left untouched because the lookup was incomplete.</summary>
    public int SkippedIncompleteLookup { get; set; }
}
