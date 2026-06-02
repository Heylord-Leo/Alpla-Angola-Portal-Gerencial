namespace AlplaPortal.Domain.Enums;

/// <summary>
/// Enumeration of AlplaPROD production plant databases.
///
/// Each value maps to a specific SQL Server instance and database.
/// The plant determines both the connection target and the pipeline model
/// used for timeline rendering.
///
/// Discovery reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §1.2
/// </summary>
public enum AlplaProdPlant
{
    /// <summary>AOVIA1VMS006 / AlplaPROD_aovia1 — Standard Logistics Model.</summary>
    VIANA1,

    /// <summary>AOVIA2VMS006 / AlplaPROD_aovia2 — Standard Logistics Model.</summary>
    VIANA2,

    /// <summary>AOVIA1VMS006 / AlplaPROD_aovia3 — Inhouse Model.</summary>
    VIANA3
}
