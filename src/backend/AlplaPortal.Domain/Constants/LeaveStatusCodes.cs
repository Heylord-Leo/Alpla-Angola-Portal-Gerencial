namespace AlplaPortal.Domain.Constants;

/// <summary>
/// Stable internal status codes for leave records.
/// Display labels (Portuguese) are resolved in the frontend.
///
/// State machine:
///   DRAFT → SUBMITTED → APPROVED | REJECTED
///   DRAFT → CANCELLED
///   SUBMITTED → CANCELLED
///   APPROVED → CANCELLED
/// </summary>
public static class LeaveStatusCodes
{
    public const string Draft = "DRAFT";
    public const string Submitted = "SUBMITTED";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";

    public static readonly string[] All = { Draft, Submitted, Approved, Rejected, Cancelled };

    /// <summary>Returns true if the transition from → to is valid.</summary>
    public static bool IsValidTransition(string from, string to)
    {
        return (from, to) switch
        {
            (Draft, Submitted) => true,
            (Draft, Cancelled) => true,
            (Submitted, Approved) => true,
            (Submitted, Rejected) => true,
            (Submitted, Cancelled) => true,
            (Approved, Cancelled) => true,
            _ => false
        };
    }
}
