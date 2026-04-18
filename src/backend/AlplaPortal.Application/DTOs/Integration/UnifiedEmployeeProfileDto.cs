namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// Unified employee profile — read-only composition of Primavera (master)
/// and Innux (operational complement).
///
/// Top level contains only cross-profile composition metadata.
/// Detailed source-specific fields live inside the separated sections.
///
/// Design rules:
/// - Primavera is the authoritative master source
/// - Innux is an optional operational complement
/// - Sections are never flattened — provenance must remain explicit
/// - Innux failure does NOT fail the whole profile; Primavera is still returned
/// </summary>
public class UnifiedEmployeeProfileDto
{
    // ─── Cross-profile identity ───

    /// <summary>Primavera master employee code.</summary>
    public required string EmployeeCode { get; set; }

    /// <summary>Primavera company target (ALPLAPLASTICO / ALPLASOPRO).</summary>
    public required string Company { get; set; }

    // ─── Source sections ───

    /// <summary>Master employee data from Primavera.</summary>
    public required PrimaveraProfileSection Primavera { get; set; }

    /// <summary>Operational complement from Innux. Null if no match or lookup error.</summary>
    public InnuxProfileSection? Innux { get; set; }

    // ─── Match diagnostics ───

    /// <summary>Whether an Innux record was found for this employee.</summary>
    public bool HasInnuxMatch { get; set; }

    /// <summary>
    /// Match strategy used. Always "EMPLOYEE_CODE_TO_NUMBER" in this phase.
    /// Documents that Primavera.Codigo was used to look up Innux.Numero.
    /// </summary>
    public string InnuxMatchStrategy { get; set; } = "EMPLOYEE_CODE_TO_NUMBER";

    /// <summary>
    /// Innux lookup outcome: "MATCHED", "NOT_FOUND", or "ERROR".
    /// </summary>
    public required string InnuxLookupStatus { get; set; }

    /// <summary>
    /// Safe diagnostic message. Null on success.
    /// On error: short, user-facing text (no raw exception details).
    /// </summary>
    public string? InnuxLookupMessage { get; set; }
}

/// <summary>
/// Primavera source section — master employee identity fields.
/// Mapped from PrimaveraEmployeeDto without adding new SQL queries.
/// </summary>
public class PrimaveraProfileSection
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? ShortName { get; set; }
    public string? FirstName { get; set; }
    public string? FirstLastName { get; set; }
    public string? SecondLastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? DepartmentCode { get; set; }
    public string? DepartmentName { get; set; }
    public DateTime? HireDate { get; set; }
    public DateTime? TerminationDate { get; set; }
    public bool? IsTemporarilyInactive { get; set; }
    public string? SourceCompany { get; set; }

    // ─── Identity Document Fields ───

    /// <summary>Bilhete de Identidade number (Funcionarios.NumBI).</summary>
    public string? IdentityDocNumber { get; set; }

    /// <summary>Passport number (Funcionarios.NumPassaporte).</summary>
    public string? PassportNumber { get; set; }

    /// <summary>Nationality (Funcionarios.Nacionalidade).</summary>
    public string? Nationality { get; set; }
}

/// <summary>
/// Innux source section — operational complement fields.
/// Mapped from InnuxEmployeeDto without adding new SQL queries.
///
/// IsActiveOperational is explicitly the Innux operational state,
/// not the unified or authoritative cross-system employee status.
/// HasPhoto is a boolean presence indicator only — no blob data.
/// </summary>
public class InnuxProfileSection
{
    public int InnuxEmployeeId { get; set; }
    public required string EmployeeNumber { get; set; }
    public string? CardNumber { get; set; }
    public bool? IsActiveOperational { get; set; }
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? LoginAd { get; set; }
    public string? Category { get; set; }
    public string? CostCenter { get; set; }
    public bool HasPhoto { get; set; }
    public string Source { get; set; } = "INNUX";
}
