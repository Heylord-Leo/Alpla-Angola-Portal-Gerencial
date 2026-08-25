// Phase 3D / Layer C — pure helpers for the extracted Supplier Ficha content. Authorization is decided by
// the BACKEND and returned on GET /ficha as `capabilities`; the UI only CONSUMES those flags. There is no
// role-name mapping here (or anywhere in the content component) — capability is never re-derived from role.

export interface SupplierFichaCapabilities {
  canView: boolean;
  canEditContacts: boolean;
  canEditAddress: boolean;
  canEditObservations: boolean;
  canUploadDocuments: boolean;
  canDeleteDocuments: boolean;
  canEditGeneralIdentity: boolean;
  canEditTaxLegal: boolean;
  canEditBanking: boolean;
  canEditCommercialTerms: boolean;
  canChangeStatus: boolean;
  canSubmitForApproval: boolean;
  canApprove: boolean;
  canReject: boolean;
  canEditAnyField: boolean;
}

/**
 * Safe default when the API response omits capabilities: everything hidden. The backend still enforces
 * authorization on every endpoint, so a missing capability payload degrades to a read-only presentation
 * rather than silently exposing an action.
 */
export const NO_SUPPLIER_CAPABILITIES: SupplierFichaCapabilities = {
  canView: false,
  canEditContacts: false,
  canEditAddress: false,
  canEditObservations: false,
  canUploadDocuments: false,
  canDeleteDocuments: false,
  canEditGeneralIdentity: false,
  canEditTaxLegal: false,
  canEditBanking: false,
  canEditCommercialTerms: false,
  canChangeStatus: false,
  canSubmitForApproval: false,
  canApprove: false,
  canReject: false,
  canEditAnyField: false,
};

/** Reads the capabilities off a raw ficha payload, falling back to the safe (all-false) default. */
export function readCapabilities(ficha: any): SupplierFichaCapabilities {
  const c = ficha?.capabilities;
  return c ? { ...NO_SUPPLIER_CAPABILITIES, ...c } : NO_SUPPLIER_CAPABILITIES;
}

/** Per-field-group editability = the form is in edit mode AND the caller holds that group's capability. */
export interface SupplierFichaFieldEditability {
  identity: boolean;      // legal name + Primavera code
  taxLegal: boolean;      // NIF
  address: boolean;
  contacts: boolean;
  banking: boolean;
  commercial: boolean;
  observations: boolean;
}

export function fieldEditability(caps: SupplierFichaCapabilities, editMode: boolean): SupplierFichaFieldEditability {
  return {
    identity: editMode && caps.canEditGeneralIdentity,
    taxLegal: editMode && caps.canEditTaxLegal,
    address: editMode && caps.canEditAddress,
    contacts: editMode && caps.canEditContacts,
    banking: editMode && caps.canEditBanking,
    commercial: editMode && caps.canEditCommercialTerms,
    observations: editMode && caps.canEditObservations,
  };
}

/** The editable ficha form fields (the keys mirrored into local form state). */
export const FICHA_FORM_FIELDS = [
  'name', 'taxId', 'primaveraCode', 'address',
  'contactName1', 'contactRole1', 'contactPhone1', 'contactEmail1',
  'contactName2', 'contactRole2', 'contactPhone2', 'contactEmail2',
  'bankAccountNumber', 'bankIban', 'bankSwift',
  'paymentTerms', 'paymentMethod', 'notes',
] as const;

/** A reliable dirty signal already exists: the working form vs the loaded snapshot. */
export function isFichaDirty(formData: Record<string, any>, snapshot: Record<string, any>): boolean {
  return FICHA_FORM_FIELDS.some(f => (formData?.[f] ?? '') !== (snapshot?.[f] ?? ''));
}
