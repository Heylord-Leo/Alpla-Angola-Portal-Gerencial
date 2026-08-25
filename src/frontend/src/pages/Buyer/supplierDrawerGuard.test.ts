import { describe, it, expect } from 'vitest';
import { decideOpen, decideClose, resolveDiscard } from './supplierDrawerGuard';

// Phase 3D / Layer D — the Supplier Sheet drawer's open/close/switch guard decisions. These pin that
// unsaved edits are never discarded silently: a dirty close or a dirty supplier-switch always raises a
// confirmation, while clean actions proceed immediately.

describe('decideOpen', () => {
  it('opens when the drawer is closed', () => {
    expect(decideOpen(null, false, 7)).toEqual({ nextShownId: 7, guard: null });
  });
  it('is a no-op guard when re-opening the same supplier', () => {
    expect(decideOpen(7, true, 7)).toEqual({ nextShownId: 7, guard: null });
  });
  it('switches immediately to another supplier when NOT dirty', () => {
    expect(decideOpen(7, false, 9)).toEqual({ nextShownId: 9, guard: null });
  });
  it('guards a supplier switch when dirty (keeps the current supplier)', () => {
    expect(decideOpen(7, true, 9)).toEqual({ nextShownId: 7, guard: { reason: 'switch', target: 9 } });
  });
});

describe('decideClose', () => {
  it('closes immediately when clean', () => {
    expect(decideClose(false)).toEqual({ close: true, guard: null });
  });
  it('guards close when dirty (does not close)', () => {
    expect(decideClose(true)).toEqual({ close: false, guard: { reason: 'close' } });
  });
});

describe('resolveDiscard', () => {
  it('a discarded switch proceeds to the target supplier', () => {
    expect(resolveDiscard({ reason: 'switch', target: 9 })).toEqual({ nextShownId: 9 });
  });
  it('a discarded close unmounts the drawer', () => {
    expect(resolveDiscard({ reason: 'close' })).toEqual({ nextShownId: null });
  });
});
