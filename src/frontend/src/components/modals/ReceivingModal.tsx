import React, { useState, useEffect } from 'react';
import { Z_INDEX } from '../../constants/ui';
import { motion, AnimatePresence } from 'framer-motion';
import { X, AlertTriangle } from 'lucide-react';
import { DropdownPortal } from '../ui/DropdownPortal';

interface ReceivingModalProps {
  open: boolean;
  onClose: () => void;
  onConfirm: (receivedQty: number, notes: string) => void;
  itemDescription: string;
  authorizedQty: number;
  currentReceivedQty: number;
  unit: string;
  readOnly?: boolean;
}

const ReceivingModal: React.FC<ReceivingModalProps> = ({
  open,
  onClose,
  onConfirm,
  itemDescription,
  authorizedQty,
  currentReceivedQty,
  unit,
  readOnly = false
}) => {
  const [receivedQty, setReceivedQty] = useState<number>(currentReceivedQty || 0);
  const [notes, setNotes] = useState<string>('');

  useEffect(() => {
    if (open) {
      setReceivedQty(currentReceivedQty || 0);
      setNotes('');
    }
  }, [open, currentReceivedQty]);

  if (!open) return null;

  const handleConfirm = () => {
    onConfirm(receivedQty, notes);
    onClose();
  };

  const isOverReceiving = receivedQty > authorizedQty;

  const inputStyle = {
    width: '100%',
    padding: '12px 14px',
    backgroundColor: 'var(--color-bg-page)',
    border: '2px solid var(--color-border)',
    borderRadius: 'var(--radius-sm)',
    fontSize: '0.875rem',
    fontWeight: 600,
    color: 'var(--color-text-main)',
    transition: 'all 0.2s ease',
    fontFamily: 'inherit',
    outline: 'none'
  };

  return (
    <DropdownPortal>
      <AnimatePresence>
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          style={{
            position: 'fixed' as const,
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: 'rgba(0,0,0,0.8)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: Z_INDEX.MODAL as any,
            padding: '20px'
          }}
        >
          <motion.div
            initial={{ scale: 0.9, y: 20 }}
            animate={{ scale: 1, y: 0 }}
            style={{
              backgroundColor: 'var(--color-bg-surface)',
              padding: '40px',
              borderRadius: 'var(--radius-md)',
              maxWidth: '600px',
              width: '100%',
              border: '1px solid var(--color-border)',
              boxShadow: 'var(--shadow-md)',
              position: 'relative' as const
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '24px' }}>
              <h2 style={{ fontSize: '1.5rem', fontWeight: 900, color: 'var(--color-text-main)', textTransform: 'uppercase', margin: 0 }}>
                Registrar Recebimento
              </h2>
              <button 
                onClick={onClose} 
                style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '4px', color: 'var(--color-text-muted)' }}
              >
                <X size={24} />
              </button>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '8px' }}>
                  Item
                </label>
                <p style={{ margin: 0, fontSize: '1rem', fontWeight: 700, color: 'var(--color-text-main)', textTransform: 'uppercase' }}>
                  {itemDescription}
                </p>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
                <div style={{ padding: '16px', backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-border)' }}>
                  <span style={{ display: 'block', fontSize: '0.65rem', fontWeight: 900, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Qtd. Autorizada</span>
                  <span style={{ fontSize: '1.25rem', fontWeight: 900 }}>{authorizedQty} {unit}</span>
                </div>
                <div style={{ padding: '16px', backgroundColor: 'rgba(var(--color-primary-rgb), 0.05)', border: '2px solid var(--color-primary)' }}>
                  <span style={{ display: 'block', fontSize: '0.65rem', fontWeight: 900, textTransform: 'uppercase', color: 'var(--color-primary)', marginBottom: '4px' }}>Recebido Anterior</span>
                  <span style={{ fontSize: '1.25rem', fontWeight: 900, color: 'var(--color-primary)' }}>{currentReceivedQty} {unit}</span>
                </div>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '8px' }}>
                  Quantidade Total Recebida (Acumulada)
                </label>
                <input
                  type="number"
                  value={receivedQty}
                  onChange={(e) => setReceivedQty(Number(e.target.value))}
                  style={{ ...inputStyle, fontSize: '1.1rem', padding: '16px', opacity: readOnly ? 0.7 : 1, cursor: readOnly ? 'not-allowed' : 'text' }}
                  placeholder="0.00"
                  disabled={readOnly}
                />
                <p style={{ marginTop: '8px', fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)', margin: 0 }}>
                  Informe o saldo total físico conferido deste item até o momento.
                </p>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '8px' }}>
                  Notas de Divergência / Observações
                </label>
                <textarea
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  rows={3}
                  style={{ ...inputStyle, height: 'auto', padding: '16px', opacity: readOnly ? 0.7 : 1, cursor: readOnly ? 'not-allowed' : 'text' }}
                  placeholder={readOnly ? "Sem observações registradas." : "Ex: Item com avaria, quantidade menor que a nota, etc."}
                  disabled={readOnly}
                />
              </div>

              {isOverReceiving && (
                <div style={{ backgroundColor: '#fff7ed', borderLeft: '4px solid #f97316', padding: '16px', display: 'flex', gap: '12px', alignItems: 'center' }}>
                  <AlertTriangle style={{ color: '#f97316', flexShrink: 0 }} size={20} />
                  <p style={{ margin: 0, fontSize: '0.85rem', fontWeight: 700, color: '#9a3412' }}>
                    Atenção: A quantidade informada ({receivedQty}) é maior que a autorizada ({authorizedQty}).
                  </p>
                </div>
              )}
            </div>

            <div style={{ display: 'flex', gap: '16px', marginTop: '40px' }}>
              <button
                onClick={onClose}
                style={{
                  flex: 1,
                  height: '48px',
                  padding: '0 24px',
                  background: 'none',
                  border: '1px solid var(--color-border)',
                  cursor: 'pointer',
                  fontWeight: 800,
                  borderRadius: 'var(--radius-sm)',
                  fontFamily: 'var(--font-family-display)',
                  fontSize: '0.875rem'
                }}
              >
                CANCELAR
              </button>
              <button
                onClick={handleConfirm}
                disabled={receivedQty < 0 || readOnly}
                style={{
                  flex: 1,
                  height: '48px',
                  padding: '0 24px',
                  backgroundColor: readOnly ? 'var(--color-bg-page)' : 'var(--color-status-green)',
                  color: readOnly ? 'var(--color-text-muted)' : '#fff',
                  border: readOnly ? '2px solid var(--color-border)' : 'none',
                  cursor: readOnly ? 'not-allowed' : 'pointer',
                  fontWeight: 800,
                  borderRadius: 'var(--radius-sm)',
                  boxShadow: readOnly ? 'none' : 'var(--shadow-md)',
                  fontFamily: 'var(--font-family-display)',
                  fontSize: '0.875rem',
                  opacity: (receivedQty < 0 || readOnly) ? 0.5 : 1
                }}
              >
                {readOnly ? 'MODO VISUALIZAÇÃO' : 'CONFIRMAR RECEBIMENTO'}
              </button>
            </div>
          </motion.div>
        </motion.div>
      </AnimatePresence>
    </DropdownPortal>
  );
};

export default ReceivingModal;
