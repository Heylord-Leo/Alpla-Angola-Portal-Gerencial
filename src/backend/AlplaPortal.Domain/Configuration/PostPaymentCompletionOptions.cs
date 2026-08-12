using System;
using System.Globalization;

namespace AlplaPortal.Domain.Configuration;

/// <summary>
/// Feature-flag and effective-date configuration for the Post-Payment Completion Workflow.
///
/// Bound from the "PostPaymentCompletion" configuration section. Both defaults are deliberately
/// the safest possible values: the feature is OFF and the effective date is unreachable, so an
/// environment whose configuration file omits the section behaves exactly as it did before this
/// feature existed. Release 1 ships with the flag false in every environment.
/// </summary>
public class PostPaymentCompletionOptions
{
    public const string SectionName = "PostPaymentCompletion";

    /// <summary>
    /// Master switch of the Release 4 INTAKE/COVERAGE capability: document classification and
    /// multi-source-document enforcement, PO-group obligation stamping (including
    /// ExpectedOperationInvoiceTotal capture), the operation-invoice obligations read model and
    /// the R15 unclassified-data quality guard. When false, all new-workflow behavior is disabled
    /// and the system behaves exactly as it did before this feature: no dimension is written, no
    /// completion is evaluated, no new endpoint is reachable, and FinalizeRequest keeps its
    /// original code path.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The Phase 4 COMPLETION lifecycle switch, split from <see cref="Enabled"/> at the Phase 3A
    /// checkpoint: the redirect from legacy FinalizeRequest into the new post-payment completion
    /// path, and the RequestCompletionService evaluation. Meaningless on its own — completion
    /// presupposes intake, so the effective test is always
    /// <see cref="PostPaymentCompletionPolicy.IsCompletionDisabled"/>
    /// (<c>Enabled &amp;&amp; CompletionEnabled</c>). The Phase 3B TEST configuration is
    /// Enabled=true with CompletionEnabled=false: coverage works, legacy finalization keeps
    /// working, and Phase 4 transitions do not run.
    /// </summary>
    public bool CompletionEnabled { get; set; } = false;

    /// <summary>
    /// Requests whose <c>Request.CreatedAtUtc</c> is at or after this UTC instant use the new
    /// mandatory workflow — classification is enforced at creation/submission.
    /// Requests created before it follow historical compatibility rules, which still require
    /// classification before completion when they own PO groups (see
    /// <see cref="PostPaymentCompletionPolicy.RequiresClassification"/>). The date decides
    /// WHEN classification is enforced, never whether it may be skipped.
    /// </summary>
    public DateTime EffectiveDateUtc { get; set; } = DateTime.MaxValue;

    /// <summary>
    /// Builds the options from raw configuration strings.
    ///
    /// <para><b>Why this exists instead of plain <c>Configure&lt;T&gt;(GetSection(...))</c>.</b>
    /// The default <c>ConfigurationBinder</c> converts a date with <c>DateTime.Parse</c>, which
    /// interprets a trailing <c>Z</c> as UTC and then converts the result to the machine's LOCAL
    /// time. For the "never" sentinel <c>9999-12-31T23:59:59Z</c> that conversion pushes the value
    /// past <see cref="DateTime.MaxValue"/> in any timezone east of UTC, and the binder throws.
    /// Because options are resolved lazily, the throw surfaces when the first controller that
    /// injects them is constructed — turning a disabled feature into a 500 on every endpoint of
    /// that controller. Parsing here is timezone-independent and never throws.</para>
    /// </summary>
    public static PostPaymentCompletionOptions FromConfigurationValues(
        string? enabledRaw, string? effectiveDateUtcRaw, string? completionEnabledRaw = null)
    {
        return new PostPaymentCompletionOptions
        {
            // Fail closed: anything that is not an explicit "true" leaves the feature off.
            Enabled = bool.TryParse(enabledRaw, out var enabled) && enabled,
            CompletionEnabled = bool.TryParse(completionEnabledRaw, out var completion) && completion,
            EffectiveDateUtc = ParseEffectiveDateUtc(effectiveDateUtcRaw)
        };
    }

    /// <summary>
    /// Parses the effective date as a true UTC instant, independent of the machine's timezone.
    ///
    /// <para><c>AssumeUniversal</c> treats a value without an offset as already-UTC;
    /// <c>AdjustToUniversal</c> stops the parser from shifting an offset-bearing value into local
    /// time. Together they mean <c>9999-12-31T23:59:59Z</c> and <c>9999-12-31T23:59:59</c> both
    /// yield the same UTC instant, on every machine, without overflowing.</para>
    ///
    /// <para>Fails closed: a missing, blank or unparseable value returns
    /// <see cref="DateTime.MaxValue"/>, i.e. "never effective" — never "effective immediately".</para>
    /// </summary>
    public static DateTime ParseEffectiveDateUtc(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DateTime.MaxValue;

        return DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : DateTime.MaxValue;
    }
}
