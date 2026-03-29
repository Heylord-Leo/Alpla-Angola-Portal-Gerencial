using System;

namespace AlplaPortal.Domain.Entities;

public class SystemCounter
{
    public string Id { get; set; } = string.Empty; // E.g. REQ_NO_QUOTATION_20260227
    public int CurrentValue { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}
