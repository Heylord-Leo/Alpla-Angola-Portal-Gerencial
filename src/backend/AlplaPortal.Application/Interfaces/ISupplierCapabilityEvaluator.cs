namespace AlplaPortal.Application.Interfaces;

/// <summary>
/// The set of Supplier Sheet ("ficha") operations a specific user may perform on a specific supplier.
/// This is the SINGLE server-side authority for Supplier Sheet authorization (Phase 3D). It is resolved
/// by <see cref="ISupplierCapabilityEvaluator"/> from the caller's roles AND — for the Buyer role — from
/// whether the supplier is involved in a request assigned to that buyer (request-scoped access).
///
/// The same object is returned to the frontend so the drawer/page can HIDE or present-as-read-only the
/// controls the user cannot use. The frontend flags are presentation only; every write endpoint MUST
/// re-check the matching flag server-side (disabled buttons are not authorization).
/// </summary>
public sealed class SupplierSheetCapabilities
{
    /// <summary>May open/read the ficha, completeness and history.</summary>
    public bool CanView { get; init; }

    // ─── Operational fields (Buyer-eligible) ───
    /// <summary>May edit the two contact blocks.</summary>
    public bool CanEditContacts { get; init; }
    /// <summary>May edit the postal address.</summary>
    public bool CanEditAddress { get; init; }
    /// <summary>May edit the free-text internal observations.</summary>
    public bool CanEditObservations { get; init; }
    /// <summary>May upload / replace the required compliance documents.</summary>
    public bool CanUploadDocuments { get; init; }
    /// <summary>May delete an existing compliance document (a more sensitive operation than upload).</summary>
    public bool CanDeleteDocuments { get; init; }

    // ─── Master / finance-sensitive fields (NOT Buyer-eligible) ───
    /// <summary>May edit legal name and Primavera code (identity master data).</summary>
    public bool CanEditGeneralIdentity { get; init; }
    /// <summary>May edit the NIF / tax id.</summary>
    public bool CanEditTaxLegal { get; init; }
    /// <summary>May edit bank account / IBAN / SWIFT.</summary>
    public bool CanEditBanking { get; init; }
    /// <summary>May edit payment terms / method.</summary>
    public bool CanEditCommercialTerms { get; init; }

    // ─── Governance lifecycle ───
    /// <summary>May change the registration status (suspend / block / reactivate).</summary>
    public bool CanChangeStatus { get; init; }
    /// <summary>May submit a completed ficha for approval.</summary>
    public bool CanSubmitForApproval { get; init; }
    /// <summary>May approve a supplier registration (DAF/DG).</summary>
    public bool CanApprove { get; init; }
    /// <summary>May return / reject a supplier registration.</summary>
    public bool CanReject { get; init; }

    /// <summary>True when the user may write at least one ficha field group.</summary>
    public bool CanEditAnyField =>
        CanEditContacts || CanEditAddress || CanEditObservations ||
        CanEditGeneralIdentity || CanEditTaxLegal || CanEditBanking || CanEditCommercialTerms;

    /// <summary>No access at all (used for a not-involved Buyer). All flags false.</summary>
    public static readonly SupplierSheetCapabilities None = new();
}

/// <summary>
/// Resolves <see cref="SupplierSheetCapabilities"/> for a user against one supplier. Centralizes the
/// Supplier Sheet authorization matrix (Phase 3D) so role logic is not scattered across endpoints.
/// </summary>
public interface ISupplierCapabilityEvaluator
{
    /// <param name="supplierId">The supplier being acted upon.</param>
    /// <param name="userId">The acting user's id (claims NameIdentifier).</param>
    /// <param name="roles">The acting user's role names (claims Role).</param>
    Task<SupplierSheetCapabilities> EvaluateAsync(
        int supplierId, Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct = default);
}
