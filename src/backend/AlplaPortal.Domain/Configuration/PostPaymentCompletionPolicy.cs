using System;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Configuration;

/// <summary>
/// Pure, side-effect-free decision helpers for the Post-Payment Completion Workflow.
///
/// The effective date is evaluated against <c>Request.CreatedAtUtc</c> and ONLY against
/// <c>Request.CreatedAtUtc</c> — never submission, approval or payment date.
///
/// Authoritative semantics (plan v6 §4.5):
/// <list type="bullet">
/// <item>COMPLETED requests stay completed under LEGACY_COMPLETED. Nothing is backfilled.</item>
/// <item>Open grouped request, created on/after the effective date → classification enforced at
/// creation; new completion path is the only completion path.</item>
/// <item>Open grouped request, created before the effective date → classification was not enforced
/// at creation, but is still REQUIRED before completion (Release 5 Finance classification).</item>
/// <item>Open request with no PO group → the legacy FinalizeRequest fallback, and the only state
/// in which it remains permitted while the feature is enabled.</item>
/// </list>
/// </summary>
public static class PostPaymentCompletionPolicy
{
    /// <summary>
    /// Feature completely disabled — legacy behavior unchanged, in every environment.
    /// This is the Release 1 state everywhere and the first check of every gated code path.
    /// </summary>
    public static bool IsFeatureDisabled(PostPaymentCompletionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return !options.Enabled;
    }

    /// <summary>
    /// New workflow mandatory for this request: the billing document type must be chosen at
    /// creation/submission, and completion happens exclusively through the completion service.
    /// </summary>
    public static bool IsNewWorkflowMandatory(PostPaymentCompletionOptions options, Request request)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);
        return options.Enabled && request.CreatedAtUtc >= options.EffectiveDateUtc;
    }

    /// <summary>
    /// Historical request with the feature enabled — compatibility rules apply.
    /// Compatibility means "classification was not enforced at creation", NEVER
    /// "classification may be skipped": see <see cref="RequiresClassification"/>.
    /// </summary>
    public static bool IsHistoricalCompatibility(PostPaymentCompletionOptions options, Request request)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);
        return options.Enabled && request.CreatedAtUtc < options.EffectiveDateUtc;
    }

    /// <summary>
    /// True when the request still owns at least one UNCLASSIFIED PO group and is not already
    /// completed. Applies to mandatory AND historical requests alike — <c>CreatedAtUtc</c> plays
    /// no part here. While true, neither the legacy FinalizeRequest nor the new completion path
    /// may proceed (rule R15).
    /// </summary>
    public static bool RequiresClassification(
        PostPaymentCompletionOptions options,
        bool requestIsCompleted,
        bool hasAnyGroup,
        bool anyGroupUnclassified)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Enabled && !requestIsCompleted && hasAnyGroup && anyGroupUnclassified;
    }
}
