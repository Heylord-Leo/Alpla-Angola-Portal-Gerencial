import React, { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { X, AlertTriangle, History } from 'lucide-react';
import { Z_INDEX } from '../../constants/ui';
import { DropdownPortal } from '../ui/DropdownPortal';

interface ReopenReceivingModalProps {
  open: boolean;
  groupName?: string;
  processing?: boolean;
  error?: string | null;
  onClose: () => void;
  /** Called only with a non-empty, trimmed reason. */
  onConfirm: (reason: string) => void;
}

/**
 * v2.245.5 — REABRIR RECEBIMENTO. A confirmed group (WAITING_RECEIPT) is returned to Receiving for
 * correction. The reason is mandatory and is persisted in the RECEIVING_REOPENED audit event. This modal
 * never touches quantities itself: reopening only unlocks correction; a NEW confirmation is required
 * before the group can return to WAITING_RECEIPT.
 */
const ReopenReceivingModal: React.FC<ReopenReceivingModalProps> = ({
  open, groupName, processing = false, error = null, onClose, onConfirm,
}) => {
  const [reason, setReason] = useState('');

  useEffect(() => { if (open) setReason(''); }, [open]);

  if (!open) return null;

  const trimmed = reason.trim();
  const canSubmit = trimmed.length > 0 && !processing;

  const inputStyle = {
    width: '100%', padding: '12px 14px', backgroundColor: 'var(--color-bg-page)',
    border: '2px solid var(--color-border)', borderRadius: 'var(--radius-sm)',
    fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)', fontFamily: 'inherit', outline: 'none',
  };

  return (
    <DropdownPortal>
      <AnimatePresence>
        <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          style={{ position: 'fixed' as const, inset: 0, backgroundColor: 'rgba(0,0,0,0.8)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: Z_INDEX.MODAL as any, padding: '20px' }}>
          <motion.div initial={{ scale: 0.9, y: 20 }} animate={{ scale: 1, y: 0 }}
            style={{ backgroundColor: 'var(--color-bg-surface)', padding: '40px', borderRadius: 'var(--radius-md)', maxWidth: '600px', width: '100%', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-md)' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '24px' }}>
              <h2 style={{ fontSize: '1.5rem', fontWeight: 900, color: 'var(--color-text-main)', textTransform: 'uppercase', margin: 0, display: 'flex', alignItems: 'center', gap: '10px' }}>
                <History size={22} /> Reabrir Recebimento
              </h2>
              <button onClick={onClose} disabled={processing} style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '4px', color: 'var(--color-text-muted)' }} aria-label="Fechar">
                <X size={24} />
              </button>
            </div>

            {groupName && (
              <div style={{ marginBottom: '16px', padding: '8px', backgroundColor: '#EFF6FF', borderRadius: '4px', border: '1px solid #BFDBFE' }}>
                <span style={{ fontSize: '0.75rem', fontWeight: 800, color: '#1E40AF', textTransform: 'uppercase' }}>Grupo:</span>
                <div style={{ fontSize: '0.875rem', fontWeight: 700, color: '#1E3A8A' }}>{groupName}</div>
              </div>
            )}

            <div style={{ marginBottom: '16px', padding: '12px 16px', backgroundColor: '#FFFBEB', border: '1px solid #FCD34D', borderRadius: 'var(--radius-sm)', display: 'flex', gap: '10px', alignItems: 'flex-start' }}>
              <AlertTriangle size={16} style={{ color: '#B45309', flexShrink: 0, marginTop: '2px' }} />
              <div style={{ fontSize: '0.8rem', fontWeight: 600, color: '#92400E' }}>
                A reabertura devolve este grupo ao Recebimento para correção dos itens ou quantidades.
                As quantidades já registadas são preservadas e o histórico não é apagado.
                <span style={{ display: 'block', marginTop: '4px', fontWeight: 800 }}>
                  Será obrigatória uma nova confirmação do recebimento.
                </span>
              </div>
            </div>

            <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '8px' }}>
              Motivo da reabertura <span style={{ color: '#dc2626' }}>*</span>
            </label>
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              rows={3}
              disabled={processing}
              placeholder="Ex: Item ou quantidade recebida incorretamente; confirmação efetuada antes da conferência."
              style={{ ...inputStyle, height: 'auto', padding: '16px' }}
            />
            {!canSubmit && trimmed.length === 0 && (
              <p style={{ margin: '6px 0 0', fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>
                Informe o motivo para prosseguir.
              </p>
            )}

            {error && (
              <div role="alert" style={{ marginTop: '12px', padding: '12px', backgroundColor: '#fee2e2', color: '#b91c1c', borderRadius: '4px', fontSize: '0.85rem', fontWeight: 700, border: '1px solid #f87171' }}>
                {error}
              </div>
            )}

            <div style={{ display: 'flex', gap: '16px', marginTop: '32px' }}>
              <button onClick={onClose} disabled={processing}
                style={{ flex: 1, height: '48px', padding: '0 24px', background: 'none', border: '1px solid var(--color-border)', cursor: 'pointer', fontWeight: 800, borderRadius: 'var(--radius-sm)', fontFamily: 'var(--font-family-display)', fontSize: '0.875rem' }}>
                CANCELAR
              </button>
              <button onClick={() => { if (canSubmit) onConfirm(trimmed); }} disabled={!canSubmit}
                style={{ flex: 1, height: '48px', padding: '0 24px', backgroundColor: canSubmit ? '#b45309' : 'var(--color-bg-page)', color: canSubmit ? '#fff' : 'var(--color-text-muted)', border: canSubmit ? 'none' : '2px solid var(--color-border)', cursor: canSubmit ? 'pointer' : 'not-allowed', fontWeight: 800, borderRadius: 'var(--radius-sm)', fontFamily: 'var(--font-family-display)', fontSize: '0.875rem', opacity: canSubmit ? 1 : 0.6 }}>
                {processing ? 'A REABRIR…' : 'REABRIR RECEBIMENTO'}
              </button>
            </div>
          </motion.div>
        </motion.div>
      </AnimatePresence>
    </DropdownPortal>
  );
};

export default ReopenReceivingModal;
