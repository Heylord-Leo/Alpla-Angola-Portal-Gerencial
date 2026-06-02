namespace AlplaPortal.Domain.Enums;

/// <summary>
/// Pipeline model for an AlplaPROD production plant.
///
/// Determines which timeline events and SQL queries are applicable:
/// - STANDARD: Full logistics pipeline (PO → EAI → Abruf → Loading → GR)
/// - INHOUSE: Shorter pipeline (PO → EAI → InhouseLieferungen → GR)
/// - PARTIAL: Only PO/EAI/GR data available (incomplete pipeline)
///
/// Discovery reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §1.3
/// </summary>
public enum AlplaProdPipelineModel
{
    /// <summary>
    /// Viana 1 / Viana 2: PO → EAI Journal → Abruf → LadePlanungen → LadeAuftraege → Wareneingang.
    /// Full external logistics pipeline with carrier management and loading orders.
    /// </summary>
    STANDARD,

    /// <summary>
    /// Viana 3: PO → EAI Journal → InhouseLieferungen → Wareneingang.
    /// Shorter in-house delivery pipeline without carrier or loading orders.
    /// </summary>
    INHOUSE,

    /// <summary>
    /// Fallback: Only PO/EAI/GR data available. Used when neither Abruf nor
    /// InhouseLieferungen records exist for a given transfer.
    /// </summary>
    PARTIAL
}
