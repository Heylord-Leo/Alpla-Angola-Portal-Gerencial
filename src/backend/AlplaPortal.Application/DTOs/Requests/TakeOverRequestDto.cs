using System;
using System.Text.Json;

namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// Body of <c>POST /api/v1/requests/{id}/take-over</c> — a Buyer taking over a QUOTATION request assigned to another Buyer.
/// </summary>
public class TakeOverRequestDto
{
    /// <summary>Mandatory, trimmed, 1..<see cref="AlplaPortal.Domain.Constants.BuyerTakeOverPolicy.JustificationMaxLength"/> characters.</summary>
    public string? Justification { get; set; }

    /// <summary>
    /// The Buyer the caller saw as current owner when confirming. Kept as a raw JSON element so the server can tell
    /// an OMITTED field (ValueKind Undefined → 400 TAKEOVER_EXPECTED_OWNER_MISSING) from an EXPLICIT null (ValueKind
    /// Null → 400 TAKEOVER_EXPECTED_OWNER_NULL: "unassigned" is a claim, not a takeover) from a GUID that no longer
    /// matches the owner (409 TAKEOVER_STALE). A stale confirmation can never overwrite a newer owner.
    /// </summary>
    public JsonElement ExpectedBuyerId { get; set; }

    public TakeOverExpectedOwner ReadExpectedOwner()
    {
        switch (ExpectedBuyerId.ValueKind)
        {
            case JsonValueKind.Undefined: return TakeOverExpectedOwner.Missing();
            case JsonValueKind.Null: return TakeOverExpectedOwner.ExplicitNull();
            case JsonValueKind.String:
                return Guid.TryParse(ExpectedBuyerId.GetString(), out var g) && g != Guid.Empty ? TakeOverExpectedOwner.Of(g) : TakeOverExpectedOwner.Invalid();
            default: return TakeOverExpectedOwner.Invalid();
        }
    }
}

public readonly record struct TakeOverExpectedOwner(TakeOverExpectedOwnerKind Kind, Guid? BuyerId)
{
    public static TakeOverExpectedOwner Missing() => new(TakeOverExpectedOwnerKind.Missing, null);
    public static TakeOverExpectedOwner ExplicitNull() => new(TakeOverExpectedOwnerKind.ExplicitNull, null);
    public static TakeOverExpectedOwner Invalid() => new(TakeOverExpectedOwnerKind.Invalid, null);
    public static TakeOverExpectedOwner Of(Guid id) => new(TakeOverExpectedOwnerKind.Provided, id);
}

public enum TakeOverExpectedOwnerKind { Missing, ExplicitNull, Invalid, Provided }

public class TakeOverRequestResultDto
{
    public string Message { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public Guid? PreviousBuyerId { get; set; }
    public string? PreviousBuyerName { get; set; }
    public Guid NewBuyerId { get; set; }
    public string? NewBuyerName { get; set; }
    public Guid? HistoryId { get; set; }
    public DateTime? TransferredAtUtc { get; set; }
}
