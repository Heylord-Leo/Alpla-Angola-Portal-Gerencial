using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Suppliers;

/// <summary>
/// Centralized Supplier Sheet authorization (Phase 3D). Resolves the per-user, per-supplier capability
/// set from role membership, with request-scoped access for the Buyer role.
///
/// Matrix (locked 2026-08-25):
///  • System Administrator — full.
///  • Contracts / Finance — full ficha edit + status + submit + documents (governance owners). Global.
///  • Local Manager — full ficha edit + documents + submit, but NOT status (security fix). Global.
///  • Buyer — ONLY operational edits (contacts, address, observations, documents) + submit, and ONLY for
///    a supplier involved in a request assigned to that buyer (request-scoped). Never identity/tax/
///    banking/commercial/status/approve. If not involved: no access at all.
///  • Approve/Reject are never granted here — those are the DAF/DG approval endpoints (Area/Final
///    Approver), which keep their own attribute gates and are not part of the Supplier Sheet surface.
///
/// Multiple roles combine as the UNION of their granted capabilities (a Buyer who is also Contracts gets
/// the broader, global Contracts set).
/// </summary>
public sealed class SupplierCapabilityEvaluator : ISupplierCapabilityEvaluator
{
    private readonly ApplicationDbContext _context;

    public SupplierCapabilityEvaluator(ApplicationDbContext context) => _context = context;

    public async Task<SupplierSheetCapabilities> EvaluateAsync(
        int supplierId, Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        roles ??= System.Array.Empty<string>();
        bool Has(string role) => roles.Contains(role);

        // System Administrator — full authority, no scoping.
        if (Has(RoleConstants.SystemAdministrator))
            return Full();

        // Contracts / Finance — governance owners: full ficha edit + status + submit + documents. Global.
        bool govOwner = Has(RoleConstants.Contracts) || Has(RoleConstants.Finance);

        // Local Manager — same as governance owners MINUS status change (Phase 3D security fix). Global.
        bool localManager = Has(RoleConstants.LocalManager);

        // Buyer — operational-only, request-scoped. Only resolve involvement if the caller is a Buyer AND
        // does not already have a broader global grant (avoids an unnecessary query).
        bool buyerOperational = false;
        if (Has(RoleConstants.Buyer) && !govOwner && !localManager)
            buyerOperational = await IsSupplierInvolvedWithBuyerAsync(supplierId, userId, roles, ct);
        else if (Has(RoleConstants.Buyer))
            buyerOperational = true; // already global via another role; scope check is moot

        if (govOwner)
            return new SupplierSheetCapabilities
            {
                CanView = true,
                CanEditContacts = true, CanEditAddress = true, CanEditObservations = true,
                CanUploadDocuments = true, CanDeleteDocuments = true,
                CanEditGeneralIdentity = true, CanEditTaxLegal = true, CanEditBanking = true, CanEditCommercialTerms = true,
                CanChangeStatus = true, CanSubmitForApproval = true,
            };

        if (localManager)
            return new SupplierSheetCapabilities
            {
                CanView = true,
                CanEditContacts = true, CanEditAddress = true, CanEditObservations = true,
                CanUploadDocuments = true, CanDeleteDocuments = true,
                CanEditGeneralIdentity = true, CanEditTaxLegal = true, CanEditBanking = true, CanEditCommercialTerms = true,
                CanChangeStatus = false, CanSubmitForApproval = true,
            };

        if (buyerOperational)
            return new SupplierSheetCapabilities
            {
                CanView = true,
                CanEditContacts = true, CanEditAddress = true, CanEditObservations = true,
                CanUploadDocuments = true,   // Buyer may add documents…
                CanDeleteDocuments = false,  // …but NOT delete them (approved decision).
                CanSubmitForApproval = false, // Buyer may NOT submit for approval (approved decision).
                // identity / tax / banking / commercial / status / approve / reject all remain FALSE.
            };

        // Any other authenticated role (or a Buyer not involved with this supplier) gets no access.
        return SupplierSheetCapabilities.None;
    }

    /// <summary>
    /// Request-scoped access rule (Layer B.1 correction): a supplier is accessible to a Buyer when it is
    /// involved in ANY request that Buyer is authorized to access under the CANONICAL request scope
    /// (<see cref="RequestAccessScope"/> — plant/department, the same policy behind /buyer/requests/{id}),
    /// NOT merely requests the Buyer owns (BuyerId). Involvement sources are request-specific: the request
    /// supplier, a quotation supplier, a line-item supplier, or a PO-group supplier. This never grants
    /// global supplier access from the Buyer role alone — a supplier outside every scoped request is denied.
    /// </summary>
    private async Task<bool> IsSupplierInvolvedWithBuyerAsync(
        int supplierId, Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        var scopedRequests = await RequestAccessScope.ScopedRequestsAsync(_context, userId, roles, ct);
        return await scopedRequests.AnyAsync(r =>
            r.SupplierId == supplierId ||
            r.Quotations.Any(q => q.SupplierId == supplierId) ||
            r.LineItems.Any(li => li.SupplierId == supplierId) ||
            r.PoGroups.Any(pg => pg.SupplierId == supplierId),
            ct);
    }

    private static SupplierSheetCapabilities Full() => new()
    {
        CanView = true,
        CanEditContacts = true, CanEditAddress = true, CanEditObservations = true,
        CanEditGeneralIdentity = true, CanEditTaxLegal = true, CanEditBanking = true, CanEditCommercialTerms = true,
        CanUploadDocuments = true, CanDeleteDocuments = true,
        CanChangeStatus = true, CanSubmitForApproval = true, CanApprove = true, CanReject = true,
    };
}
