namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// Absence code from Innux dbo.CodigosAusencia.
/// Reference data — rarely changes, suitable for long-TTL caching.
/// </summary>
public class InnuxAbsenceCodeDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// Portal classification derived from Innux code semantics.
    /// Values: "Unjustified", "Justified", "Vacation", "SickLeave", "Other".
    /// </summary>
    public string Classification { get; set; } = "";

    /// <summary>Innux display color hint (hex), null if not set.</summary>
    public string? Color { get; set; }

    /// <summary>Whether the code is inactive in Innux.</summary>
    public bool IsInactive { get; set; }
}

/// <summary>
/// Work code from Innux dbo.CodigosTrabalho.
/// Reference data — rarely changes, suitable for long-TTL caching.
/// </summary>
public class InnuxWorkCodeDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Whether this code represents overtime work.</summary>
    public bool IsOvertime { get; set; }
}
