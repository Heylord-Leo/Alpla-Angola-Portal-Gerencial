import { ROLES } from '../constants/roles';
import { SupplierAdditionalInfo } from '../components/suppliers/SupplierAdditionalInfoPanel';
import { SupplierExtractionSnapshot } from './paymentRequestCreation';

/**
 * Maps what the extraction read about a supplier onto the shared registration form.
 *
 * <p>Only fields the document actually carried are returned; an absent one is simply omitted so the
 * form shows it empty. Nothing is inferred, and the user reviews every value before saving.</p>
 */
export function supplierExtractionToInfo(
    snapshot: SupplierExtractionSnapshot | null | undefined
): Partial<SupplierAdditionalInfo> {
    if (!snapshot) return {};

    const out: Partial<SupplierAdditionalInfo> = {};
    if (snapshot.address) out.Address = snapshot.address;
    if (snapshot.contactName) out.ContactName1 = snapshot.contactName;
    if (snapshot.email) out.ContactEmail1 = snapshot.email;
    if (snapshot.phone) out.ContactPhone1 = snapshot.phone;
    if (snapshot.bankIban) out.BankIban = snapshot.bankIban;
    if (snapshot.bankAccountNumber) out.BankAccountNumber = snapshot.bankAccountNumber;
    if (snapshot.bankSwift) out.BankSwift = snapshot.bankSwift;
    if (snapshot.paymentTerms) out.PaymentTerms = snapshot.paymentTerms;
    return out;
}

/**
 * Who may create a supplier from inside a Payment flow.
 *
 * <p>Mirrors <c>LookupsController.CanCreateSupplierContextuallyAsync</c>, which backs
 * <c>POST /api/v1/lookups/suppliers/from-payment-ocr</c>: the administrative roles, <b>or</b> any
 * user holding both a plant scope and a department scope — that is, anyone who can raise a payment
 * request at all. The endpoint creates a DRAFT supplier (<c>Origin = PAYMENT_OCR</c>) and never
 * touches Primavera codes or administrative fields, which is what makes the wider audience safe.</p>
 *
 * <p>Stated here so the button is never offered to someone the server will refuse. The server stays
 * the authority; this only decides whether to show the door.</p>
 */
export function canCreateSupplierContextually(
    roles: string[] | undefined,
    scope: { hasPlantScope: boolean; hasDepartmentScope: boolean }
): boolean {
    const privileged = [
        ROLES.SYSTEM_ADMINISTRATOR,
        ROLES.BUYER,
        ROLES.FINANCE,
        ROLES.CONTRACTS,
        ROLES.LOCAL_MANAGER
    ];

    if (roles?.some(r => privileged.includes(r))) return true;

    return scope.hasPlantScope && scope.hasDepartmentScope;
}

/** Shown in place of the create action when the user may not create suppliers. */
export const SUPPLIER_CREATE_NOT_ALLOWED =
    'Não tem autorização para registar fornecedores. Selecione um fornecedor existente ou peça a ' +
    'um utilizador autorizado que registe este.';
