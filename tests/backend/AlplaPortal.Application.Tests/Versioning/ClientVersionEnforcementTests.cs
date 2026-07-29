using AlplaPortal.Application.Versioning;
using Xunit;

namespace AlplaPortal.Application.Tests.Versioning;

/// <summary>
/// Unit tests for the pure client-version enforcement decision logic (no ASP.NET / no DB).
/// Covers unsafe-method detection, exempt paths, header classification, malformed handling, the
/// per-mode outcome matrix, and the fail-open rule when server metadata is invalid.
/// </summary>
public class ClientVersionEnforcementTests
{
    private const string Server = "2.216.1+372ac47";

    [Theory]
    [InlineData("POST", true)]
    [InlineData("put", true)]
    [InlineData("Patch", true)]
    [InlineData("DELETE", true)]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    public void IsUnsafeMethod_classifies_write_methods(string method, bool expected)
        => Assert.Equal(expected, ClientVersionEnforcement.IsUnsafeMethod(method));

    [Theory]
    [InlineData("/api/auth/login", true)]
    [InlineData("/api/app/version", true)]
    [InlineData("/api/app/environment", true)]
    [InlineData("/health", true)]
    [InlineData("/api/admin/logs/ingest", true)]
    [InlineData("/api/requests", false)]
    [InlineData("/api/approvals/x", false)]
    public void IsExemptPath_matches_prefixes(string path, bool expected)
        => Assert.Equal(expected, ClientVersionEnforcement.IsExemptPath(path, ClientVersionEnforcement.DefaultExemptPrefixes));

    [Theory]
    [InlineData("2.216.1+372ac47", false)]
    [InlineData("DEV-LOCAL", false)]
    [InlineData("0.0.0-dev", false)]
    [InlineData("", false)]           // empty is "missing", not "malformed"
    [InlineData("has space", true)]
    [InlineData("has;semicolon", true)]
    [InlineData("<script>", true)]
    public void IsMalformed_flags_bad_values(string build, bool expected)
        => Assert.Equal(expected, ClientVersionEnforcement.IsMalformed(build));

    [Fact]
    public void IsMalformed_flags_overlong_value()
        => Assert.True(ClientVersionEnforcement.IsMalformed(new string('a', 200)));

    [Theory]
    [InlineData("2.216.1+372ac47", HeaderState.Matching)]
    [InlineData("2.216.0+aaaaaaa", HeaderState.Mismatched)]
    [InlineData(null, HeaderState.Missing)]
    [InlineData("", HeaderState.Missing)]
    [InlineData("bad value", HeaderState.Malformed)]
    public void Classify_returns_expected_state(string? client, HeaderState expected)
        => Assert.Equal(expected, ClientVersionEnforcement.Classify(client, Server));

    // ── Per-mode outcome matrix (unsafe method, non-exempt path, valid server metadata) ──

    [Fact]
    public void Disabled_never_rejects()
    {
        var d = ClientVersionEnforcement.Decide(EnforcementMode.Disabled, "POST", "/api/requests", "old", Server, true);
        Assert.False(d.Reject);
    }

    [Fact]
    public void SafeMethod_never_rejects()
    {
        var d = ClientVersionEnforcement.Decide(EnforcementMode.EnforceAll, "GET", "/api/requests", null, Server, true);
        Assert.False(d.Reject);
    }

    [Fact]
    public void ExemptPath_never_rejects_even_in_EnforceAll()
    {
        var d = ClientVersionEnforcement.Decide(EnforcementMode.EnforceAll, "POST", "/api/auth/login", null, Server, true);
        Assert.False(d.Reject);
    }

    [Fact]
    public void InvalidServerMetadata_fails_open()
    {
        var d = ClientVersionEnforcement.Decide(EnforcementMode.EnforceAll, "POST", "/api/requests", "old", Server, serverMetadataValid: false);
        Assert.False(d.Reject);
    }

    [Theory]
    [InlineData("2.216.0+aaaaaaa")] // mismatch
    [InlineData(null)]              // missing
    [InlineData("bad value")]       // malformed
    public void Observe_never_rejects_but_classifies(string? client)
    {
        var d = ClientVersionEnforcement.Decide(EnforcementMode.Observe, "POST", "/api/requests", client, Server, true);
        Assert.False(d.Reject);
        Assert.NotEqual(HeaderState.NotApplicable, d.HeaderState);
    }

    [Theory]
    [InlineData("2.216.1+372ac47", false)] // matching → allow
    [InlineData("2.216.0+aaaaaaa", true)]  // mismatch → reject
    [InlineData("bad value", true)]        // malformed → treated as mismatch → reject
    [InlineData(null, false)]              // missing → tolerated
    public void EnforceMismatch_matrix(string? client, bool expectReject)
    {
        var d = ClientVersionEnforcement.Decide(EnforcementMode.EnforceMismatch, "POST", "/api/requests", client, Server, true);
        Assert.Equal(expectReject, d.Reject);
    }

    [Theory]
    [InlineData("2.216.1+372ac47", false)] // matching → allow
    [InlineData("2.216.0+aaaaaaa", true)]  // mismatch → reject
    [InlineData("bad value", true)]        // malformed → reject
    [InlineData(null, true)]               // missing → reject
    public void EnforceAll_matrix(string? client, bool expectReject)
    {
        var d = ClientVersionEnforcement.Decide(EnforcementMode.EnforceAll, "POST", "/api/requests", client, Server, true);
        Assert.Equal(expectReject, d.Reject);
    }
}
