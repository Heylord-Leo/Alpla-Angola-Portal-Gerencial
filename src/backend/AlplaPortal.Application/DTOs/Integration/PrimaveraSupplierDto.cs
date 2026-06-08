namespace AlplaPortal.Application.DTOs.Integration;

/// <summary>
/// DTO for Primavera supplier master data (read-only).
///
/// Source: Primavera table [Fornecedores] (116 columns; ~19 exposed here).
///
/// This DTO covers supplier identity, contact information, address,
/// banking details, and payment terms for Supplier Ficha enrichment.
///
/// Phase 4B+: extended to include banking and payment fields for
/// Supplier Ficha auto-population during Primavera sync/import.
///
/// Deferred fields:
/// - TotalDeb / LimiteCred (credit/balance)
/// - Notas (ntext remarks)
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

    // ─── Contact Person (Supplier Ficha enrichment) ───

    /// <summary>Primary contact person name. Source: Fornecedores.Contacto</summary>
    public string? ContactPerson { get; set; }

    /// <summary>Contact person mobile phone. Source: Fornecedores.Telemovel</summary>
    public string? MobilePhone { get; set; }

    /// <summary>Contact person role/title. Source: Fornecedores.Cargo</summary>
    public string? ContactRole { get; set; }

    // ─── Banking (Supplier Ficha enrichment) ───

    /// <summary>IBAN. Source: Fornecedores.IBAN</summary>
    public string? IBAN { get; set; }

    /// <summary>SWIFT/BIC code. Source: Fornecedores.Swift</summary>
    public string? Swift { get; set; }

    /// <summary>Bank account number. Source: Fornecedores.NumCB</summary>
    public string? BankAccountNumber { get; set; }

    // ─── Payment Terms (Supplier Ficha enrichment) ───

    /// <summary>Payment terms code. Source: Fornecedores.CondPag</summary>
    public string? PaymentTerms { get; set; }

    /// <summary>Payment method code. Source: Fornecedores.ModoPag</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>Which Primavera company this supplier belongs to.</summary>
    public string SourceCompany { get; set; } = string.Empty;

    /// <summary>Always "PRIMAVERA".</summary>
    public string Source { get; set; } = "PRIMAVERA";
}
