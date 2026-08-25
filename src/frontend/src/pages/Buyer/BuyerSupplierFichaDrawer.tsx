import React, { forwardRef, useCallback, useImperativeHandle, useState } from 'react';
import { X } from 'lucide-react';
import SupplierFichaDetailContent from '../Contracts/SupplierFichaDetailContent';
import { ConfirmationDialog } from '../../components/common/ConfirmationDialog';
import { DrawerGuard, decideOpen, decideClose, resolveDiscard } from './supplierDrawerGuard';

// Phase 3D / Layer D — Buyer Workspace right-side Supplier Sheet drawer. It owns ONLY host concerns:
// open/close state, the selected supplier id, drawer chrome, and the dirty/supplier-switch guards. The
// entire ficha UI (fields, save, documents, capabilities) is the SHARED SupplierFichaDetailContent mounted
// with hostMode="drawer" — there is no duplicated Supplier form and no role logic here (authorization is
// the backend `capabilities` the content already consumes). The parent opens it imperatively so the
// supplier carousel's own index/scroll state is never remounted.

export interface BuyerSupplierFichaDrawerHandle {
  /** Open the drawer for a supplier (or switch to another one, guarded when the form is dirty). */
  open: (supplierId: number) => void;
}

interface BuyerSupplierFichaDrawerProps {
  /** Host refresh after a successful save (e.g. silently reload the Workspace to update the carousel card). */
  onSaved?: () => void;
}

export const BuyerSupplierFichaDrawer = forwardRef<BuyerSupplierFichaDrawerHandle, BuyerSupplierFichaDrawerProps>(
  ({ onSaved }, ref) => {
    const [shownId, setShownId] = useState<number | null>(null);
    const [isDirty, setIsDirty] = useState(false);
    const [guard, setGuard] = useState<DrawerGuard | null>(null);

    const open = useCallback((supplierId: number) => {
      const d = decideOpen(shownId, isDirty, supplierId);
      setShownId(d.nextShownId);
      if (d.guard) setGuard(d.guard);
    }, [shownId, isDirty]);

    useImperativeHandle(ref, () => ({ open }), [open]);

    const requestClose = () => {
      const d = decideClose(isDirty);
      if (d.close) { setShownId(null); setIsDirty(false); }
      if (d.guard) setGuard(d.guard);
    };

    // Discard: honor the pending intent (switch supplier or close) and drop unsaved edits by remounting/
    // unmounting the content (id change remounts; close unmounts). Never auto-saves.
    const discardChanges = () => {
      if (!guard) return;
      const { nextShownId } = resolveDiscard(guard);
      setShownId(nextShownId);
      setIsDirty(false);
      setGuard(null);
    };
    const keepEditing = () => setGuard(null);

    if (shownId === null) return null;

    return (
      <>
        <div
          onClick={requestClose}
          style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.45)', zIndex: 1200 }}
          aria-hidden="true"
        />
        <aside
          role="dialog"
          aria-modal="true"
          aria-label="Ficha do fornecedor"
          style={{
            position: 'fixed', top: 0, right: 0, height: '100vh',
            width: 'clamp(520px, 42vw, 620px)', maxWidth: '100vw',
            background: 'var(--color-bg-base, var(--color-bg-surface))',
            borderLeft: '1px solid var(--color-border)',
            boxShadow: '-8px 0 28px rgba(0,0,0,0.28)',
            zIndex: 1201, overflowY: 'auto', overflowX: 'hidden',
          }}
        >
          <button
            onClick={requestClose}
            aria-label="Fechar"
            style={{
              position: 'absolute', top: 12, right: 12, zIndex: 2,
              display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
              width: 32, height: 32, borderRadius: 8, cursor: 'pointer',
              border: '1px solid var(--color-border)', background: 'var(--color-bg-surface)',
              color: 'var(--color-text-main)',
            }}
          >
            <X size={16} />
          </button>

          <SupplierFichaDetailContent
            key={shownId}
            supplierId={shownId}
            hostMode="drawer"
            onClose={requestClose}
            onSaved={() => onSaved?.()}
            onDirtyChange={setIsDirty}
          />
        </aside>

        {guard && (
          <ConfirmationDialog
            title="Alterações não guardadas"
            variant="warning"
            confirmText="Descartar alterações"
            cancelText="Continuar editando"
            onConfirm={discardChanges}
            onCancel={keepEditing}
            message={
              guard.reason === 'switch'
                ? 'Tem alterações por guardar nesta ficha. Se mudar de fornecedor, essas alterações serão descartadas.'
                : 'Tem alterações por guardar nesta ficha. Se fechar, essas alterações serão descartadas.'
            }
          />
        )}
      </>
    );
  }
);

BuyerSupplierFichaDrawer.displayName = 'BuyerSupplierFichaDrawer';
