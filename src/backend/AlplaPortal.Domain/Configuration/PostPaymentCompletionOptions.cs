using System;

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
    /// Master switch. When false, all new-workflow behavior is disabled and the system behaves
    /// exactly as it did before this feature: no dimension is written, no completion is evaluated,
    /// no new endpoint is reachable, and FinalizeRequest keeps its original code path.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Requests whose <c>Request.CreatedAtUtc</c> is at or after this UTC instant use the new
    /// mandatory workflow — classification is enforced at creation/submission.
    /// Requests created before it follow historical compatibility rules, which still require
    /// classification before completion when they own PO groups (see
    /// <see cref="PostPaymentCompletionPolicy.RequiresClassification"/>). The date decides
    /// WHEN classification is enforced, never whether it may be skipped.
    /// </summary>
    public DateTime EffectiveDateUtc { get; set; } = DateTime.MaxValue;
}
