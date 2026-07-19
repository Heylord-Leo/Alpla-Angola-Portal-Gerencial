namespace AlplaPortal.Application.Interfaces.Purchasing;

/// <summary>Eligibility reason codes for a quotation item as a batch/approval candidate.</summary>
public static class QuotationItemEligibilityReasons
{
    public const string EligibleNormal = "ELIGIBLE_NORMAL";
    public const string ReuseAuthorizationRequired = "REUSE_AUTHORIZATION_REQUIRED";
    public const string ReuseAuthorized = "REUSE_AUTHORIZED";
    public const string QuotationDeleted = "QUOTATION_DELETED";
    public const string ItemNotReconciled = "ITEM_NOT_RECONCILED";
    public const string AuthorizationConsumed = "AUTHORIZATION_CONSUMED";
    public const string AuthorizationRevoked = "AUTHORIZATION_REVOKED";
}

/// <summary>Eligibility + provenance of one quotation item for selection in a NEW approval batch.</summary>
public sealed class QuotationItemEligibility
{
    public Guid QuotationItemId { get; init; }
    public Guid QuotationId { get; init; }

    /// <summary>True when the item may be selected in a new batch right now.</summary>
    public bool IsEligible { get; init; }

    /// <summary>True when the item was used in a CANCELLED batch and has no valid active authorization.</summary>
    public bool IsReuseBlocked { get; init; }

    /// <summary>True when an active, unconsumed authorization makes this item eligible again.</summary>
    public bool IsReuseAuthorized { get; init; }

    public Guid? SourceCancelledBatchId { get; init; }
    public int? SourceCancelledBatchNumber { get; init; }
    public Guid? ReuseAuthorizationId { get; init; }
    public Guid? ReuseConsumedByBatchId { get; init; }

    /// <summary>One of <see cref="QuotationItemEligibilityReasons"/>.</summary>
    public string ReasonCode { get; init; } = QuotationItemEligibilityReasons.EligibleNormal;
}

/// <summary>
/// Single backend source of truth for the cancelled-batch reuse rule (Option C):
/// a quotation item selected in a CANCELLED approval batch is NOT automatically eligible for a
/// new batch — it needs an explicit, active, unconsumed Buyer authorization for that exact
/// (item, source cancelled batch) pair. Controllers must call this service instead of
/// re-implementing the rule.
/// </summary>
public interface IQuotationItemEligibilityService
{
    /// <summary>Resolves eligibility for every quotation item of the request (one pass, for DTO annotation).</summary>
    Task<IReadOnlyDictionary<Guid, QuotationItemEligibility>> GetEligibilityMapAsync(Guid requestId);

    /// <summary>
    /// Validates a set of quotation item ids about to be selected/persisted for a new batch or
    /// individual approval. Returns the blocked entries (empty = all allowed) plus the entries
    /// whose ACTIVE authorization must be consumed by the caller in the same transaction.
    /// <paramref name="consumingBatchId"/>: when re-validating an EXISTING batch (update/resubmit),
    /// items whose authorization was already consumed BY THAT batch are legitimate, not blocked.
    /// </summary>
    Task<(IReadOnlyList<QuotationItemEligibility> Blocked, IReadOnlyList<QuotationItemEligibility> AuthorizedToConsume)>
        ValidateSelectionAsync(Guid requestId, IEnumerable<Guid> quotationItemIds, Guid? consumingBatchId = null);
}
