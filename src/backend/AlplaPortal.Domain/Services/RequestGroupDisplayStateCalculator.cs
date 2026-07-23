namespace AlplaPortal.Domain.Services;

/// <summary>
/// Result of resolving a request's group-aware display state. Null fields mean "no override" —
/// the caller should keep showing the persisted Request.Status.Code/Name unchanged.
/// </summary>
public sealed record RequestGroupDisplayState(string? DisplayStatusCode, string? DisplayStatusName);

/// <summary>
/// Pure, static, side-effect-free calculator for a display-only "group-aware" status label,
/// separate from persisted Request.Status aggregation (RequestStatusCalculator/
/// StatusAggregationService). Never used for permissions/eligibility, never persisted, never
/// alters Request.Status. Exists because the persisted aggregate always picks one "furthest
/// behind" group (see RequestStatusCalculator's GroupStatusPriority), which reads as misleading
/// once active groups sit on structurally different tracks (e.g. one PAYMENT_SCHEDULED, another
/// ADVANCE_PAYMENT_COMPLETED) — this calculator instead classifies groups into coarse "display
/// buckets" and only overrides the label when there is genuinely no single accurate persisted name
/// to show.
///
/// Mirrors the frontend's requestGroupDisplayState.ts bucket table exactly (status lists AND
/// label strings) — the two are independently maintained (C#/TypeScript, no shared codegen), kept
/// in sync only by both being tested against this same literal spec. Update both sides together.
/// </summary>
public static class RequestGroupDisplayStateCalculator
{
    private const string CancelledGroupStatus = "CANCELLED";
    private const string MixedDisplayCode = "PAYMENTS_IN_PROGRESS";
    private const string MixedDisplayName = "Pagamentos em andamento";

    private enum Bucket
    {
        WaitingAction,
        Scheduled,
        AdvancePaid,
        PaidOrPostPayment,
        Completed
    }

    private static readonly Dictionary<string, Bucket> BucketByGroupStatus = new()
    {
        ["PO_ISSUED"] = Bucket.WaitingAction,
        ["PAYMENT_REQUEST_SENT"] = Bucket.WaitingAction,
        ["ADVANCE_PAYMENT_REQUIRED"] = Bucket.WaitingAction,

        ["PAYMENT_SCHEDULED"] = Bucket.Scheduled,
        ["ADVANCE_PAYMENT_SCHEDULED"] = Bucket.Scheduled,

        ["ADVANCE_PAYMENT_COMPLETED"] = Bucket.AdvancePaid,
        ["WAITING_SUPPLIER_DELIVERY"] = Bucket.AdvancePaid,

        ["PAYMENT_COMPLETED"] = Bucket.PaidOrPostPayment,
        ["WAITING_RECEIPT"] = Bucket.PaidOrPostPayment,
        ["WAITING_RECONCILIATION"] = Bucket.PaidOrPostPayment,
        ["IN_FOLLOWUP"] = Bucket.PaidOrPostPayment,

        ["COMPLETED"] = Bucket.Completed,
    };

    // Generic label used only when active groups differ in literal status code but share one
    // bucket (e.g. one PO_ISSUED + one PAYMENT_REQUEST_SENT, both WAITING_ACTION) — the persisted
    // Request.Status name reflects only one of them in that case, so it cannot be reused as-is.
    private static readonly Dictionary<Bucket, (string Code, string Name)> BucketDisplayLabel = new()
    {
        [Bucket.WaitingAction] = ("WAITING_ACTION", "Aguardando processamento financeiro"),
        [Bucket.Scheduled] = ("SCHEDULED", "Pagamentos agendados"),
        [Bucket.AdvancePaid] = ("ADVANCE_PAID", "Adiantamentos realizados"),
        [Bucket.PaidOrPostPayment] = ("PAID_OR_POST_PAYMENT", "Pagamentos concluídos"),
        [Bucket.Completed] = ("COMPLETED_ALL", "Concluído"),
    };

    /// <summary>
    /// Resolves the display override for a request given its active (non-CANCELLED) PO group
    /// statuses. Returns (null, null) whenever the persisted status should be shown unchanged:
    /// no groups, an unrecognized group status, or every group sharing the exact same status code.
    /// </summary>
    public static RequestGroupDisplayState Resolve(IEnumerable<string?> groupStatuses)
    {
        var operational = (groupStatuses ?? Enumerable.Empty<string?>())
            .Where(s => !string.IsNullOrEmpty(s) && s != CancelledGroupStatus)
            .Select(s => s!)
            .ToList();

        if (operational.Count == 0)
            return new RequestGroupDisplayState(null, null);

        var buckets = new List<Bucket>(operational.Count);
        foreach (var status in operational)
        {
            if (!BucketByGroupStatus.TryGetValue(status, out var bucket))
                return new RequestGroupDisplayState(null, null); // unrecognized status — never guess

            buckets.Add(bucket);
        }

        if (buckets.Distinct().Count() > 1)
            return new RequestGroupDisplayState(MixedDisplayCode, MixedDisplayName);

        if (operational.Distinct().Count() == 1)
            return new RequestGroupDisplayState(null, null); // one shared code — persisted name is already accurate

        var label = BucketDisplayLabel[buckets[0]];
        return new RequestGroupDisplayState(label.Code, label.Name);
    }
}
