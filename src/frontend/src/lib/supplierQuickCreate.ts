import { ROLES } from '../constants/roles';

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
