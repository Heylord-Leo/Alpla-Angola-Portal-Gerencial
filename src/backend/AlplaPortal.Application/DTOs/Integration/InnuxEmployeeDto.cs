namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// Innux employee operational identity DTO — read-only.
///
/// Maps fields from dbo.Funcionarios with department enrichment from dbo.Departamentos.
/// Focused on operational identity for lookup/search. No attendance/event fields.
///
/// Field naming notes:
/// - IsActiveOperational: explicitly Innux operational active state,
///   NOT the unified or authoritative cross-system employee status.
/// - HasPhoto: boolean presence indicator only — never exposes the binary payload.
/// - Source: always "INNUX" for traceability in future unified profile scenarios.
/// </summary>
public class InnuxEmployeeDto
{
    /// <summary>Innux employee number — primary match key with Primavera Codigo.</summary>
    public required string EmployeeNumber { get; set; }

    /// <summary>Internal Innux technical PK (IDFuncionario).</summary>
    public int InnuxEmployeeId { get; set; }

    /// <summary>Full employee name.</summary>
    public required string Name { get; set; }

    /// <summary>Abbreviated display name.</summary>
    public string? ShortName { get; set; }

    /// <summary>Contact email.</summary>
    public string? Email { get; set; }

    /// <summary>Contact phone.</summary>
    public string? Phone { get; set; }

    /// <summary>Mobile phone.</summary>
    public string? Mobile { get; set; }

    /// <summary>FK to dbo.Departamentos.</summary>
    public int? DepartmentId { get; set; }

    /// <summary>Department description — enriched via LEFT JOIN on Departamentos.</summary>
    public string? DepartmentName { get; set; }

    /// <summary>Badge / biometric card number.</summary>
    public string? CardNumber { get; set; }

    /// <summary>
    /// Innux operational active flag.
    /// This is the Innux-specific operational state, NOT the unified or
    /// authoritative cross-system employee status (which belongs to Primavera).
    /// </summary>
    public bool? IsActiveOperational { get; set; }

    /// <summary>Active Directory login reference.</summary>
    public string? LoginAd { get; set; }

    /// <summary>Admission date.</summary>
    public DateTime? HireDate { get; set; }

    /// <summary>Termination date.</summary>
    public DateTime? TerminationDate { get; set; }

    /// <summary>Employee category.</summary>
    public string? Category { get; set; }

    /// <summary>Cost center code.</summary>
    public string? CostCenter { get; set; }

    /// <summary>
    /// Boolean presence indicator — true if the Funcionarios.Fotografia column
    /// is not null. Never returns the actual image/blob payload.
    /// </summary>
    public bool HasPhoto { get; set; }

    /// <summary>Source system — always "INNUX" for traceability.</summary>
    public string Source { get; set; } = "INNUX";
}
