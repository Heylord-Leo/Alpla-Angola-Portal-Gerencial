using System;
using System.Collections.Generic;
using System.Linq;

namespace AlplaPortal.Application.Versioning;

/// <summary>Staged rollout mode for client-version write enforcement (config: <c>ClientVersionEnforcement:Mode</c>).</summary>
public enum EnforcementMode
{
    /// <summary>No validation and no rejection.</summary>
    Disabled = 0,
    /// <summary>Classify and log only; never reject.</summary>
    Observe = 1,
    /// <summary>Reject a present-but-different/malformed build on unsafe methods; missing header allowed.</summary>
    EnforceMismatch = 2,
    /// <summary>Reject missing, malformed, or mismatched build on unsafe methods.</summary>
    EnforceAll = 3
}

/// <summary>How the client build header compared to the server build (for decision + safe logging).</summary>
public enum HeaderState
{
    NotApplicable = 0,
    Matching = 1,
    Missing = 2,
    Malformed = 3,
    Mismatched = 4
}

public readonly record struct EnforcementDecision(bool Reject, HeaderState HeaderState, string Reason);

/// <summary>
/// Pure, framework-free decision logic for client-version write enforcement, extracted so the
/// unsafe-method rule, exempt-path rule, header classification and per-mode outcome are directly
/// unit-testable without spinning up ASP.NET. The middleware is a thin adapter over this.
///
/// <para>Compatibility is EXACT <see cref="BuildInfo"/> <c>BuildId</c> equality — no Git-SHA ordering,
/// no minimum-version logic. The client build header is untrusted metadata and must never influence
/// authentication or authorization.</para>
/// </summary>
public static class ClientVersionEnforcement
{
    public static readonly string[] UnsafeMethods = { "POST", "PUT", "PATCH", "DELETE" };

    /// <summary>Route prefixes exempt from enforcement (auth, anonymous metadata, health, telemetry).
    /// Reads (GET/HEAD/OPTIONS) and file downloads are already exempt because only unsafe methods
    /// are enforced.</summary>
    public static readonly string[] DefaultExemptPrefixes =
    {
        "/api/auth",
        "/api/app/environment",
        "/api/app/version",
        "/health",
        "/api/admin/logs/ingest"
    };

    private const int MaxHeaderLength = 128;

    public static bool IsUnsafeMethod(string? method) =>
        method != null && UnsafeMethods.Any(m => string.Equals(m, method, StringComparison.OrdinalIgnoreCase));

    public static bool IsExemptPath(string? path, IEnumerable<string> exemptPrefixes)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return exemptPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A present header that is too long or contains characters outside the build-id charset.</summary>
    public static bool IsMalformed(string? build)
    {
        if (string.IsNullOrWhiteSpace(build)) return false; // "missing", not "malformed"
        if (build.Length > MaxHeaderLength) return true;
        foreach (var c in build)
        {
            bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                      || c == '.' || c == '+' || c == '-' || c == '_';
            if (!ok) return true;
        }
        return false;
    }

    public static HeaderState Classify(string? clientBuild, string serverBuild)
    {
        if (string.IsNullOrWhiteSpace(clientBuild)) return HeaderState.Missing;
        if (IsMalformed(clientBuild)) return HeaderState.Malformed;
        return string.Equals(clientBuild, serverBuild, StringComparison.Ordinal)
            ? HeaderState.Matching
            : HeaderState.Mismatched;
    }

    /// <param name="serverMetadataValid">True only when the server build metadata status is Valid.
    /// When false the decision is fail-open (never reject) to avoid self-lockout on a degraded build.</param>
    public static EnforcementDecision Decide(
        EnforcementMode mode,
        string method,
        string path,
        string? clientBuild,
        string serverBuild,
        bool serverMetadataValid,
        IEnumerable<string>? exemptPrefixes = null)
    {
        exemptPrefixes ??= DefaultExemptPrefixes;

        if (mode == EnforcementMode.Disabled)
            return new EnforcementDecision(false, HeaderState.NotApplicable, "disabled");
        if (!IsUnsafeMethod(method))
            return new EnforcementDecision(false, HeaderState.NotApplicable, "safe-method");
        if (IsExemptPath(path, exemptPrefixes))
            return new EnforcementDecision(false, HeaderState.NotApplicable, "exempt-path");
        if (!serverMetadataValid)
            return new EnforcementDecision(false, HeaderState.NotApplicable, "server-metadata-invalid-fail-open");

        var state = Classify(clientBuild, serverBuild);

        switch (mode)
        {
            case EnforcementMode.Observe:
                return new EnforcementDecision(false, state, "observe");

            case EnforcementMode.EnforceMismatch:
                // Missing header is tolerated; a present-but-malformed header is treated as a mismatch.
                bool rejectMismatch = state is HeaderState.Mismatched or HeaderState.Malformed;
                return new EnforcementDecision(rejectMismatch, state, rejectMismatch ? "reject-mismatch" : "allow");

            case EnforcementMode.EnforceAll:
                bool rejectAll = state is HeaderState.Mismatched or HeaderState.Malformed or HeaderState.Missing;
                return new EnforcementDecision(rejectAll, state, rejectAll ? "reject-all" : "allow");

            default:
                return new EnforcementDecision(false, state, "unknown-mode");
        }
    }
}
