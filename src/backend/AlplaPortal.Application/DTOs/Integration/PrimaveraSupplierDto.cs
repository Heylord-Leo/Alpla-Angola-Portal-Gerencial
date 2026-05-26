namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// DTO for Primavera supplier master data (read-only).
///
/// Source: Primavera table [Fornecedores] (116 columns; ~14 exposed here).
///
/// This DTO intentionally excludes financial, banking, tax-reconciliation,
/// and transactional fields. It focuses on supplier identity and contact
/// information suitable for lookup, search, and future procurement workflows.
///
/// Phase 4B scope — first supplier master-data DTO.
///
/// Deferred fields:
/// - CondPag (payment terms)
/// - ModoPag (payment method)
/// - TotalDeb / LimiteCred (credit/balance)
/// - Notas (ntext remarks)
/// - Bank/IBAN/Swift details
/// - B2B integration fields
/// - Retention/withholding fields
/// - eGAR waste management fields
/// </summary>
public class PrimaveraSupplierDto
{
    /// <summary>Supplier code (PK). Source: Fornecedores.Fornecedor</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Full supplier name. Source: Fornecedores.Nome</summary>
    public string? Name { get; set; }

    /// <summary>Fiscal/formal name. Source: Fornecedores.NomeFiscal</summary>
    public string? FiscalName { get; set; }

    /// <summary>Tax identification number / NIF. Source: Fornecedores.NumContrib</summary>
    public string? TaxId { get; set; }

    /// <summary>Email. Source: Fornecedores.Email</summary>
    public string? Email { get; set; }

    /// <summary>Telephone. Source: Fornecedores.Tel</summary>
    public string? Phone { get; set; }

    /// <summary>Fax. Source: Fornecedores.Fax</summary>
    public string? Fax { get; set; }

    /// <summary>Primary address. Source: Fornecedores.Morada</summary>
    public string? Address { get; set; }

    /// <summary>Secondary address line. Source: Fornecedores.Morada1</summary>
    public string? Address2 { get; set; }

    /// <summary>City/locality. Source: Fornecedores.Local</summary>
    public string? City { get; set; }

    /// <summary>Postal code. Source: Fornecedores.Cp</summary>
    public string? PostalCode { get; set; }

    /// <summary>Country code (ISO 2-letter). Source: Fornecedores.Pais</summary>
    public string? Country { get; set; }

    /// <summary>Supplier type code. Source: Fornecedores.TipoFor</summary>
    public string? SupplierType { get; set; }

    /// <summary>Whether the supplier record is voided/cancelled. Source: Fornecedores.FornecedorAnulado</summary>
    public bool IsCancelled { get; set; }

    /// <summary>Currency code. Source: Fornecedores.Moeda</summary>
    public string? Currency { get; set; }

    /// <summary>Record creation date. Source: Fornecedores.DataCriacao</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>Which Primavera company this supplier belongs to.</summary>
    public string SourceCompany { get; set; } = string.Empty;

    /// <summary>Always "PRIMAVERA".</summary>
    public string Source { get; set; } = "PRIMAVERA";
}
