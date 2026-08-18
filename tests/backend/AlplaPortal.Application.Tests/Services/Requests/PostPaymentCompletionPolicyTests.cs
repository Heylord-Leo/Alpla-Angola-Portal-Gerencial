using System;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Entities;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Release 1 foundation tests for the Post-Payment Completion feature gate and its
/// effective-date semantics (plan v6 §4.4/§4.5).
///
/// The load-bearing assertion of this file is the one most likely to be got wrong later:
/// "created before the effective date" means classification was not enforced AT CREATION —
/// it never means an open grouped request may skip classification.
/// </summary>
public class PostPaymentCompletionPolicyTests
{
    private static readonly DateTime EffectiveDate = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    private static PostPaymentCompletionOptions Enabled() => new()
    {
        Enabled = true,
        EffectiveDateUtc = EffectiveDate
    };

    private static Request RequestCreatedAt(DateTime createdAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAtUtc = createdAtUtc
    };

    // ── Defaults: the safest possible configuration ──

    [Fact]
    public void Options_default_to_disabled_and_unreachable_effective_date()
    {
        var options = new PostPaymentCompletionOptions();

        Assert.False(options.Enabled);
        Assert.Equal(DateTime.MaxValue, options.EffectiveDateUtc);
        Assert.True(PostPaymentCompletionPolicy.IsFeatureDisabled(options));
    }

    [Fact]
    public void Section_name_matches_the_configuration_key()
    {
        Assert.Equal("PostPaymentCompletion", PostPaymentCompletionOptions.SectionName);
    }

    // ── Feature gate ──

    [Fact]
    public void Feature_is_disabled_when_flag_is_false_regardless_of_effective_date()
    {
        var options = new PostPaymentCompletionOptions
        {
            Enabled = false,
            EffectiveDateUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var request = RequestCreatedAt(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(PostPaymentCompletionPolicy.IsFeatureDisabled(options));
        Assert.False(PostPaymentCompletionPolicy.IsNewWorkflowMandatory(options, request));
        Assert.False(PostPaymentCompletionPolicy.IsHistoricalCompatibility(options, request));
        Assert.False(PostPaymentCompletionPolicy.RequiresClassification(
            options, requestIsCompleted: false, hasAnyGroup: true, anyGroupUnclassified: true));
    }

    // ── Completion split (Phase 3A checkpoint): Enabled ≠ CompletionEnabled ──

    [Fact]
    public void Completion_defaults_to_disabled()
    {
        var options = new PostPaymentCompletionOptions();

        Assert.False(options.CompletionEnabled);
        Assert.True(PostPaymentCompletionPolicy.IsCompletionDisabled(options));
    }

    [Theory]
    [InlineData(false, false, true)]   // committed default everywhere
    [InlineData(true, false, true)]    // the Phase 3B TEST state: intake on, completion off
    [InlineData(false, true, true)]    // mistyped config fails closed — completion presupposes intake
    [InlineData(true, true, false)]    // Phase 4 activation
    public void Completion_is_enabled_only_when_both_switches_are_on(
        bool enabled, bool completionEnabled, bool expectedDisabled)
    {
        var options = new PostPaymentCompletionOptions
        {
            Enabled = enabled,
            CompletionEnabled = completionEnabled,
            EffectiveDateUtc = EffectiveDate
        };

        Assert.Equal(expectedDisabled, PostPaymentCompletionPolicy.IsCompletionDisabled(options));
    }

    [Fact]
    public void The_phase_3b_state_keeps_intake_on_while_completion_stays_off()
    {
        var options = new PostPaymentCompletionOptions
        {
            Enabled = true,
            CompletionEnabled = false,
            EffectiveDateUtc = EffectiveDate
        };

        Assert.False(PostPaymentCompletionPolicy.IsFeatureDisabled(options));      // coverage capability on
        Assert.True(PostPaymentCompletionPolicy.IsCompletionDisabled(options));    // Phase 4 lifecycle off
    }

    // ── Effective date: evaluated against Request.CreatedAtUtc only ──

    [Fact]
    public void Request_created_on_the_effective_date_is_mandatory_boundary_is_inclusive()
    {
        var request = RequestCreatedAt(EffectiveDate);

        Assert.True(PostPaymentCompletionPolicy.IsNewWorkflowMandatory(Enabled(), request));
        Assert.False(PostPaymentCompletionPolicy.IsHistoricalCompatibility(Enabled(), request));
    }

    [Fact]
    public void Request_created_one_tick_before_the_effective_date_is_historical()
    {
        var request = RequestCreatedAt(EffectiveDate.AddTicks(-1));

        Assert.False(PostPaymentCompletionPolicy.IsNewWorkflowMandatory(Enabled(), request));
        Assert.True(PostPaymentCompletionPolicy.IsHistoricalCompatibility(Enabled(), request));
    }

    [Fact]
    public void Mandatory_and_historical_are_mutually_exclusive_and_exhaustive_while_enabled()
    {
        foreach (var createdAt in new[]
                 {
                     EffectiveDate.AddYears(-3),
                     EffectiveDate.AddDays(-1),
                     EffectiveDate,
                     EffectiveDate.AddDays(1)
                 })
        {
            var request = RequestCreatedAt(createdAt);
            var mandatory = PostPaymentCompletionPolicy.IsNewWorkflowMandatory(Enabled(), request);
            var historical = PostPaymentCompletionPolicy.IsHistoricalCompatibility(Enabled(), request);

            Assert.NotEqual(mandatory, historical);
        }
    }

    // ── The anti-bypass rule ──

    [Fact]
    public void Historical_request_with_an_unclassified_group_still_requires_classification()
    {
        var options = Enabled();
        var historical = RequestCreatedAt(EffectiveDate.AddYears(-2));

        // It IS historical...
        Assert.True(PostPaymentCompletionPolicy.IsHistoricalCompatibility(options, historical));

        // ...and it STILL requires classification. Being old is never a bypass.
        Assert.True(PostPaymentCompletionPolicy.RequiresClassification(
            options, requestIsCompleted: false, hasAnyGroup: true, anyGroupUnclassified: true));
    }

    [Fact]
    public void Completed_request_never_requires_classification_legacy_completed_stays_completed()
    {
        Assert.False(PostPaymentCompletionPolicy.RequiresClassification(
            Enabled(), requestIsCompleted: true, hasAnyGroup: true, anyGroupUnclassified: true));
    }

    [Fact]
    public void Groupless_request_never_requires_classification_there_is_nothing_to_classify()
    {
        Assert.False(PostPaymentCompletionPolicy.RequiresClassification(
            Enabled(), requestIsCompleted: false, hasAnyGroup: false, anyGroupUnclassified: false));
    }

    [Fact]
    public void Fully_classified_open_request_does_not_require_classification()
    {
        Assert.False(PostPaymentCompletionPolicy.RequiresClassification(
            Enabled(), requestIsCompleted: false, hasAnyGroup: true, anyGroupUnclassified: false));
    }

    // ── Argument hygiene ──

    [Fact]
    public void Policy_rejects_null_arguments()
    {
        var request = RequestCreatedAt(EffectiveDate);

        Assert.Throws<ArgumentNullException>(() => PostPaymentCompletionPolicy.IsFeatureDisabled(null!));
        Assert.Throws<ArgumentNullException>(() => PostPaymentCompletionPolicy.IsNewWorkflowMandatory(null!, request));
        Assert.Throws<ArgumentNullException>(() => PostPaymentCompletionPolicy.IsNewWorkflowMandatory(Enabled(), null!));
        Assert.Throws<ArgumentNullException>(() => PostPaymentCompletionPolicy.IsHistoricalCompatibility(null!, request));
        Assert.Throws<ArgumentNullException>(() => PostPaymentCompletionPolicy.IsHistoricalCompatibility(Enabled(), null!));
    }
}
