import { describe, it, expect } from 'vitest';
import {
  readCapabilities, fieldEditability, isFichaDirty, NO_SUPPLIER_CAPABILITIES, SupplierFichaCapabilities,
} from './supplierFichaContent';

// Phase 3D / Layer C — the content component consumes backend capabilities; it NEVER maps roles. These
// pin the pure helpers that drive field/action availability and the dirty signal.

const full: SupplierFichaCapabilities = {
  canView: true, canEditContacts: true, canEditAddress: true, canEditObservations: true,
  canUploadDocuments: true, canDeleteDocuments: true, canEditGeneralIdentity: true, canEditTaxLegal: true,
  canEditBanking: true, canEditCommercialTerms: true, canChangeStatus: true, canSubmitForApproval: true,
  canApprove: true, canReject: true, canEditAnyField: true,
};

// Operational-only (in-scope Buyer): contacts/address/observations/upload, nothing sensitive.
const buyer: SupplierFichaCapabilities = {
  ...NO_SUPPLIER_CAPABILITIES,
  canView: true, canEditContacts: true, canEditAddress: true, canEditObservations: true,
  canUploadDocuments: true, canEditAnyField: true,
};

describe('readCapabilities', () => {
  it('reads capabilities off the ficha payload', () => {
    expect(readCapabilities({ capabilities: full }).canEditBanking).toBe(true);
  });
  it('falls back to all-false when capabilities are absent (backend still enforces)', () => {
    expect(readCapabilities({}).canView).toBe(false);
    expect(readCapabilities(null).canEditAnyField).toBe(false);
    expect(readCapabilities(undefined)).toBe(NO_SUPPLIER_CAPABILITIES);
  });
  it('merges a partial payload onto the safe default', () => {
    const caps = readCapabilities({ capabilities: { canView: true, canEditContacts: true } });
    expect(caps.canView).toBe(true);
    expect(caps.canEditContacts).toBe(true);
    expect(caps.canEditBanking).toBe(false); // unspecified → default false
  });
});

describe('fieldEditability', () => {
  it('is entirely false when not in edit mode, regardless of capabilities', () => {
    const fe = fieldEditability(full, false);
    expect(Object.values(fe).every(v => v === false)).toBe(true);
  });

  it('full-capability user (page host) can edit every group in edit mode', () => {
    const fe = fieldEditability(full, true);
    expect(fe).toEqual({
      identity: true, taxLegal: true, address: true, contacts: true,
      banking: true, commercial: true, observations: true,
    });
  });

  it('operational-only Buyer can edit only contacts/address/observations — not identity/tax/banking/commercial', () => {
    const fe = fieldEditability(buyer, true);
    expect(fe.contacts).toBe(true);
    expect(fe.address).toBe(true);
    expect(fe.observations).toBe(true);
    expect(fe.identity).toBe(false);
    expect(fe.taxLegal).toBe(false);
    expect(fe.banking).toBe(false);
    expect(fe.commercial).toBe(false);
  });
});

describe('isFichaDirty', () => {
  const base = { name: 'ACME', contactName1: 'João', bankIban: 'AO1', notes: '' };
  it('is false when the form equals the loaded snapshot', () => {
    expect(isFichaDirty({ ...base }, { ...base })).toBe(false);
  });
  it('is true when any editable field differs', () => {
    expect(isFichaDirty({ ...base, contactName1: 'João Silva' }, base)).toBe(true);
  });
  it('treats null/undefined and empty string as equal (no false positives)', () => {
    expect(isFichaDirty({ notes: '' }, { notes: null })).toBe(false);
    expect(isFichaDirty({ notes: undefined }, {})).toBe(false);
  });
});
