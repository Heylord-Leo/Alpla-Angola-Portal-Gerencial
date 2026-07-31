using System;
using System.IO;
using System.Runtime.CompilerServices;
using AlplaPortal.Api.Helpers;
using AlplaPortal.Domain.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Regression tests for the configuration binding of <see cref="PostPaymentCompletionOptions"/>.
///
/// <para><b>The defect these lock down.</b> The options were originally registered with
/// <c>Configure&lt;T&gt;(GetSection(...))</c>. The default <c>ConfigurationBinder</c> converts a
/// date via <c>DateTime.Parse</c>, which reads the trailing <c>Z</c> of the "never" sentinel
/// <c>9999-12-31T23:59:59Z</c> as UTC and then shifts it into the machine's LOCAL time. On any
/// host east of UTC — the deployment target runs at UTC+1 — that lands past
/// <see cref="DateTime.MaxValue"/> and the binder throws
/// <c>InvalidOperationException: Failed to convert configuration value at
/// 'PostPaymentCompletion:EffectiveDateUtc'</c>.</para>
///
/// <para>Options bind lazily, so the throw did not appear at startup: it surfaced when the first
/// controller injecting <c>IOptions&lt;PostPaymentCompletionOptions&gt;</c> was constructed,
/// turning every RequestsController endpoint into a 500 while the feature was switched off.
/// The earlier unit tests missed it because they built the options object in code and never went
/// through configuration binding at all.</para>
///
/// <para>These tests therefore bind the REAL <c>appsettings.json</c> shipped in the repository,
/// not a synthetic fixture.</para>
/// </summary>
public class PostPaymentCompletionOptionsBindingTests
{
    /// <summary>
    /// Walks up to the repository root from this file's own compile-time path. Anchoring on
    /// <see cref="CallerFilePathAttribute"/> rather than <c>AppContext.BaseDirectory</c> keeps the
    /// test correct when the build output is redirected outside the repository.
    /// </summary>
    private static string RepoRoot([CallerFilePath] string callerFilePath = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "backend", "AlplaPortal.Api", "appsettings.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repository root containing appsettings.json was not found.");
    }

    private static IConfigurationRoot RealApiConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(Path.Combine(RepoRoot(), "src", "backend", "AlplaPortal.Api"))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

    // ── The regression itself, through the real DI registration ──

    [Fact]
    public void Resolving_the_options_through_the_real_registration_does_not_throw()
    {
        // This is the exact path that produced the outage: Program.cs registers the options, and
        // IOptions<T>.Value is first evaluated when a controller is constructed. Resolving it here
        // reproduces that moment. Before the fix this threw InvalidOperationException on a UTC+1
        // host and every RequestsController endpoint returned 500.
        var services = new ServiceCollection();
        services.AddPostPaymentCompletionOptions(RealApiConfiguration());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PostPaymentCompletionOptions>>().Value;

        Assert.NotNull(options);
        Assert.False(options.Enabled);
        Assert.True(PostPaymentCompletionPolicy.IsFeatureDisabled(options));
    }

    [Fact]
    public void Registration_is_resilient_to_a_hostile_effective_date_value()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["PostPaymentCompletion:Enabled"] = "false",
                ["PostPaymentCompletion:EffectiveDateUtc"] = "9999-12-31T23:59:59.9999999Z"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPostPaymentCompletionOptions(config);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PostPaymentCompletionOptions>>().Value;

        Assert.False(options.Enabled);
        Assert.True(options.EffectiveDateUtc > new DateTime(9000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    // ── Binding helpers ──

    [Fact]
    public void Real_appsettings_binds_without_throwing()
    {
        var section = RealApiConfiguration().GetSection(PostPaymentCompletionOptions.SectionName);

        // Before the fix this threw InvalidOperationException on any host east of UTC.
        var options = PostPaymentCompletionOptions.FromConfigurationValues(
            section["Enabled"], section["EffectiveDateUtc"]);

        Assert.NotNull(options);
    }

    [Fact]
    public void Real_appsettings_keeps_the_feature_disabled_with_an_unreachable_effective_date()
    {
        var section = RealApiConfiguration().GetSection(PostPaymentCompletionOptions.SectionName);

        var options = PostPaymentCompletionOptions.FromConfigurationValues(
            section["Enabled"], section["EffectiveDateUtc"]);

        Assert.False(options.Enabled);
        Assert.True(PostPaymentCompletionPolicy.IsFeatureDisabled(options));
        Assert.True(options.EffectiveDateUtc > new DateTime(9000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Section_is_actually_present_in_the_real_appsettings()
    {
        var section = RealApiConfiguration().GetSection(PostPaymentCompletionOptions.SectionName);

        Assert.True(section.Exists());
        Assert.Equal("false", section["Enabled"], ignoreCase: true);
    }

    // ── The parser ──

    [Theory]
    [InlineData("9999-12-31T23:59:59Z")]   // the "never" sentinel, UTC-suffixed — the crashing value
    [InlineData("9999-12-31T23:59:59")]    // same instant without a suffix
    [InlineData("9999-12-31")]             // date only
    public void Sentinel_dates_parse_without_overflow(string raw)
    {
        var parsed = PostPaymentCompletionOptions.ParseEffectiveDateUtc(raw);

        Assert.True(parsed > new DateTime(9000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Utc_suffix_is_not_shifted_into_local_time()
    {
        // The property is documented as UTC. A Z-suffixed value must land on the same instant
        // regardless of the host timezone — the old binder shifted it by the local offset.
        var withZ = PostPaymentCompletionOptions.ParseEffectiveDateUtc("2026-08-15T00:00:00Z");
        var withoutZ = PostPaymentCompletionOptions.ParseEffectiveDateUtc("2026-08-15T00:00:00");

        Assert.Equal(new DateTime(2026, 8, 15, 0, 0, 0), withZ);
        Assert.Equal(withZ, withoutZ);
        Assert.Equal(DateTimeKind.Utc, withZ.Kind);
    }

    [Fact]
    public void Explicit_offset_is_normalised_to_utc()
    {
        // 02:00 at +02:00 is midnight UTC.
        var parsed = PostPaymentCompletionOptions.ParseEffectiveDateUtc("2026-08-15T02:00:00+02:00");

        Assert.Equal(new DateTime(2026, 8, 15, 0, 0, 0), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    [InlineData("31/12/9999")]
    public void Missing_or_unparseable_values_fail_closed_to_never(string? raw)
    {
        // "Never effective" is the safe direction. An unreadable value must never be read as
        // "effective immediately".
        Assert.Equal(DateTime.MaxValue, PostPaymentCompletionOptions.ParseEffectiveDateUtc(raw));
    }

    // ── The flag ──

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("1", false)]        // not a bool literal → stays off
    [InlineData("yes", false)]      // not a bool literal → stays off
    [InlineData("enabled", false)]
    public void Enabled_flag_fails_closed_unless_explicitly_true(string? raw, bool expected)
    {
        var options = PostPaymentCompletionOptions.FromConfigurationValues(raw, null);

        Assert.Equal(expected, options.Enabled);
    }

    [Fact]
    public void An_absent_section_yields_a_disabled_never_effective_configuration()
    {
        // TEST and PROD run from server-side appsettings files that carry no PostPaymentCompletion
        // section at all — that must resolve to "off", not to a crash or an accidental activation.
        var empty = new ConfigurationBuilder().Build()
            .GetSection(PostPaymentCompletionOptions.SectionName);

        var options = PostPaymentCompletionOptions.FromConfigurationValues(
            empty["Enabled"], empty["EffectiveDateUtc"]);

        Assert.False(options.Enabled);
        Assert.Equal(DateTime.MaxValue, options.EffectiveDateUtc);
    }

    [Fact]
    public void A_local_override_can_still_enable_the_feature_for_development()
    {
        // Releases 2-4 are exercised locally through an uncommitted environment override.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["PostPaymentCompletion:Enabled"] = "true",
                ["PostPaymentCompletion:EffectiveDateUtc"] = "2020-01-01T00:00:00Z"
            })
            .Build();

        var section = config.GetSection(PostPaymentCompletionOptions.SectionName);
        var options = PostPaymentCompletionOptions.FromConfigurationValues(
            section["Enabled"], section["EffectiveDateUtc"]);

        Assert.True(options.Enabled);
        Assert.False(PostPaymentCompletionPolicy.IsFeatureDisabled(options));
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0), options.EffectiveDateUtc);
    }
}
