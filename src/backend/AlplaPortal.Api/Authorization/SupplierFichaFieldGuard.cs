using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Api.Authorization;

/// <summary>
/// Field-level write guard for the Supplier Sheet (Phase 3D / Layer B.1). Given the caller's resolved
/// capabilities, detects whether a <see cref="UpdateSupplierFichaDto"/> payload ATTEMPTS to change a field
/// group the caller may not edit. The controller uses this to reject such a request with 403 rather than
/// silently applying a partial update — so no forbidden value is ever persisted and no misleading success
/// is returned. Extracted as a pure function so the rule is unit-tested directly.
/// </summary>
public static class SupplierFichaFieldGuard
{
    /// <summary>
    /// Returns a human label for the FIRST forbidden field group the payload tries to mutate, or null when
    /// nothing forbidden is changed. "Tries to mutate" = a non-null supplied value that differs (trimmed;
    /// Primavera code upper-cased to match the persistence path) from the stored value. A null/omitted or
    /// unchanged value is not an attempt, so echoing existing values or omitting a group is allowed.
    /// </summary>
    public static string? FindForbiddenMutation(Supplier e, UpdateSupplierFichaDto dto, SupplierSheetCapabilities caps)
    {
        static bool Changes(string? incoming, string? current) =>
            incoming != null && incoming.Trim() != (current ?? string.Empty);

        if (!caps.CanEditGeneralIdentity &&
            (Changes(dto.Name, e.Name) ||
             (dto.PrimaveraCode != null && dto.PrimaveraCode.Trim().ToUpper() != (e.PrimaveraCode ?? string.Empty))))
            return "denominação / código Primavera";
        if (!caps.CanEditTaxLegal && Changes(dto.TaxId, e.TaxId)) return "NIF / dados fiscais";
        if (!caps.CanEditAddress && Changes(dto.Address, e.Address)) return "morada";
        if (!caps.CanEditContacts &&
            (Changes(dto.ContactName1, e.ContactName1) || Changes(dto.ContactRole1, e.ContactRole1) ||
             Changes(dto.ContactPhone1, e.ContactPhone1) || Changes(dto.ContactEmail1, e.ContactEmail1) ||
             Changes(dto.ContactName2, e.ContactName2) || Changes(dto.ContactRole2, e.ContactRole2) ||
             Changes(dto.ContactPhone2, e.ContactPhone2) || Changes(dto.ContactEmail2, e.ContactEmail2)))
            return "contactos";
        if (!caps.CanEditBanking &&
            (Changes(dto.BankAccountNumber, e.BankAccountNumber) || Changes(dto.BankIban, e.BankIban) ||
             Changes(dto.BankSwift, e.BankSwift)))
            return "dados bancários";
        if (!caps.CanEditCommercialTerms &&
            (Changes(dto.PaymentTerms, e.PaymentTerms) || Changes(dto.PaymentMethod, e.PaymentMethod)))
            return "condições comerciais";
        if (!caps.CanEditObservations && Changes(dto.Notes, e.Notes)) return "observações";
        return null;
    }
}
