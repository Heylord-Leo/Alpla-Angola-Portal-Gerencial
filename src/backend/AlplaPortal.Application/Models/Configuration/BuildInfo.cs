namespace AlplaPortal.Application.Models.Configuration;

/// <summary>
/// Status of the deployed build metadata (mirrors the frontend <c>BuildMetadataStatus</c>).
/// <para><b>Valid</b> — a GitHub Actions release manifest was loaded.
/// <b>DevelopmentFallback</b> — running locally without a manifest (clearly not a release).
/// <b>Missing</b> — no manifest found in a non-Development environment (deployment must fail-closed).
/// <b>Malformed</b> — a manifest exists but could not be parsed / lacks required fields.
/// <b>Unknown</b> — not yet resolved.</para>
/// </summary>
public enum BuildMetadataStatus
{
    Unknown = 0,
    Valid = 1,
    DevelopmentFallback = 2,
    Missing = 3,
    Malformed = 4
}

/// <summary>
/// Canonical build/deployment identity for the running backend. Populated once at startup from the
/// GitHub-Actions-generated <c>build-manifest.json</c> placed beside the API. The single runtime
/// source of version truth — never derived from stale hard-coded assembly literals.
///
/// <para>Compatibility with the frontend is decided ONLY by <see cref="BuildId"/> equality
/// (<c>version + '+' + shortSha</c>, environment-independent). Deployment-only fields
/// (<see cref="DeploymentId"/> etc.) are audit metadata and are never part of the comparison.</para>
/// </summary>
public class BuildInfo
{
    // ── Build-time canonical (identical in FE & BE artifacts; drives compatibility) ──
    public string Version { get; set; } = "0.0.0-dev";
    /// <summary>The only compatibility key. Compared by EQUALITY only — never ordered.</summary>
    public string BuildId { get; set; } = "DEV-LOCAL";
    public string ShortSha { get; set; } = "dev";
    public string GitSha { get; set; } = string.Empty;
    public string BuiltAtUtc { get; set; } = string.Empty;

    // ── Deployment-time (audit only; NOT part of compatibility; not in the public FE identity) ──
    public string? DeploymentId { get; set; }
    public string? GithubRunId { get; set; }
    public string? GithubRunNumber { get; set; }
    public string? GithubRunAttempt { get; set; }
    public string? DeploymentStartedAtUtc { get; set; }

    public BuildMetadataStatus Status { get; set; } = BuildMetadataStatus.Unknown;

    /// <summary>True only for a genuine GitHub Actions release build.</summary>
    public bool IsValid => Status == BuildMetadataStatus.Valid;
}
