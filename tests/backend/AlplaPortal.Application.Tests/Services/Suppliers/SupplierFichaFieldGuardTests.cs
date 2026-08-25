using AlplaPortal.Api.Authorization;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Entities;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Suppliers;

/// <summary>
/// Phase 3D / Layer B.1 — direct-API forbidden-field proof for PUT /suppliers/{id}/ficha. Even if a Buyer
/// crafts a payload by hand, any attempt to CHANGE a field group they lack capability for is detected and
/// rejected (the controller turns a non-null return into 403); echoing existing values or omitting a group
/// is allowed. This asserts the actual server-side guard, not a frontend flag.
/// </summary>
public class SupplierFichaFieldGuardTests
{
    // Capabilities matching an in-scope Buyer (operational-only).
    private static readonly SupplierSheetCapabilities BuyerCaps = new()
    {
        CanView = true,
        CanEditContacts = true, CanEditAddress = true, CanEditObservations = true,
        CanUploadDocuments = true,
        // banking / tax / identity / commercial / delete / submit / status all FALSE
    };

    private static Supplier Stored() => new()
    {
        Id = 7, Name = "ACME LDA", PortalCode = "SUP-000007", PrimaveraCode = "ACM",
        TaxId = "5000000000", Address = "Rua A", ContactName1 = "João",
        BankIban = "AO06000000000000000000000", BankAccountNumber = "111", BankSwift = "BIC",
        PaymentTerms = "30 dias", PaymentMethod = "TRANSF", Notes = "nota"
    };

    [Fact]
    public void Buyer_ChangingBanking_IsRejected()
    {
        var dto = new UpdateSupplierFichaDto { BankIban = "AO9999999999999999999999" };
        Assert.Equal("dados bancários", SupplierFichaFieldGuard.FindForbiddenMutation(Stored(), dto, BuyerCaps));
    }

    [Fact]
    public void Buyer_ChangingTaxId_IsRejected()
    {
        var dto = new UpdateSupplierFichaDto { TaxId = "5099999999" };
        Assert.Equal("NIF / dados fiscais", SupplierFichaFieldGuard.FindForbiddenMutation(Stored(), dto, BuyerCaps));
    }

    [Fact]
    public void Buyer_ChangingIdentity_IsRejected()
    {
        var dto = new UpdateSupplierFichaDto { Name = "OUTRO NOME" };
        Assert.Equal("denominação / código Primavera", SupplierFichaFieldGuard.FindForbiddenMutation(Stored(), dto, BuyerCaps));
    }

    [Fact]
    public void Buyer_ChangingPrimaveraCode_IsRejected_CaseInsensitiveToPersistPath()
    {
        // Stored is "ACM"; sending "acm" upper-cases to "ACM" → NOT a change (persistence path uppercases).
        Assert.Null(SupplierFichaFieldGuard.FindForbiddenMutation(Stored(), new UpdateSupplierFichaDto { PrimaveraCode = "acm" }, BuyerCaps));
        // Sending a genuinely different code IS a change → rejected.
        Assert.Equal("denominação / código Primavera",
            SupplierFichaFieldGuard.FindForbiddenMutation(Stored(), new UpdateSupplierFichaDto { PrimaveraCode = "XYZ" }, BuyerCaps));
    }

    [Fact]
    public void Buyer_ChangingCommercialTerms_IsRejected()
    {
        var dto = new UpdateSupplierFichaDto { PaymentTerms = "90 dias" };
        Assert.Equal("condições comerciais", SupplierFichaFieldGuard.FindForbiddenMutation(Stored(), dto, BuyerCaps));
    }

    [Fact]
    public void Buyer_EchoingForbiddenValues_IsAllowed()
    {
        // Full payload that repeats the stored banking/tax/identity/commercial values + changes contacts.
        var s = Stored();
        var dto = new UpdateSupplierFichaDto
        {
            Name = s.Name, PrimaveraCode = s.PrimaveraCode, TaxId = s.TaxId,
            BankIban = s.BankIban, BankAccountNumber = s.BankAccountNumber, BankSwift = s.BankSwift,
            PaymentTerms = s.PaymentTerms, PaymentMethod = s.PaymentMethod,
            ContactName1 = "João Silva", // the only real change (permitted)
        };
        Assert.Null(SupplierFichaFieldGuard.FindForbiddenMutation(s, dto, BuyerCaps));
    }

    [Fact]
    public void Buyer_OmittingForbiddenGroups_IsAllowed()
    {
        var dto = new UpdateSupplierFichaDto { ContactName1 = "Novo", Address = "Rua B", Notes = "x" };
        Assert.Null(SupplierFichaFieldGuard.FindForbiddenMutation(Stored(), dto, BuyerCaps));
    }

    [Fact]
    public void Buyer_ChangingPermittedFields_IsAllowed()
    {
        var dto = new UpdateSupplierFichaDto { ContactEmail1 = "novo@x.com", Address = "Rua Nova", Notes = "obs" };
        Assert.Null(SupplierFichaFieldGuard.FindForbiddenMutation(Stored(), dto, BuyerCaps));
    }

    [Fact]
    public void GovernanceOwner_ChangingBanking_IsAllowed()
    {
        var full = new SupplierSheetCapabilities
        {
            CanView = true, CanEditContacts = true, CanEditAddress = true, CanEditObservations = true,
            CanEditGeneralIdentity = true, CanEditTaxLegal = true, CanEditBanking = true, CanEditCommercialTerms = true,
            CanUploadDocuments = true, CanDeleteDocuments = true,
        };
        var dto = new UpdateSupplierFichaDto { BankIban = "AO9999999999999999999999", TaxId = "5099999999" };
        Assert.Null(SupplierFichaFieldGuard.FindForbiddenMutation(Stored(), dto, full));
    }
}
