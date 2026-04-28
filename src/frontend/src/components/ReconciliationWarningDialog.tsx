/**
 * ReconciliationWarningDialog
 *
 * Lightweight confirmation dialog for submission guardrail.
 * Shown when a user attempts to save/submit with unresolved catalog items.
 *
 * Actions:
 *   - "Revisar Itens" → opens the reconciliation modal
 *   - "Continuar Mesmo Assim" → submits with free-text items (override)
 *   - "Cancelar" → closes the dialog, returns to form
 *
 * All user-facing labels are in Portuguese.
 */
import { AlertTriangle, Search, X } from 'lucide-react';

interface ReconciliationWarningDialogProps {
    isOpen: boolean;
    unresolvedCount: number;
    onReviewItems: () => void;
    onCancel: () => void;
}

export function ReconciliationWarningDialog({
    isOpen,
    unresolvedCount,
    onReviewItems,
    onCancel,
}: ReconciliationWarningDialogProps) {
    if (!isOpen) return null;

    return (
        <div style={{
            position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
            backgroundColor: 'rgba(0,0,0,0.5)', zIndex: 99999,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
        }}>
            <div style={{
                backgroundColor: 'var(--color-bg-surface, #fff)',
                borderRadius: 'var(--radius-lg, 12px)',
                padding: '32px',
                maxWidth: '520px',
                width: '90%',
                boxShadow: '0 20px 60px rgba(0,0,0,0.3)',
            }}>
                {/* Header */}
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '20px' }}>
                    <div style={{
                        width: '48px', height: '48px', borderRadius: '50%',
                        backgroundColor: '#FEF3C7', display: 'flex',
                        alignItems: 'center', justifyContent: 'center', flexShrink: 0,
                    }}>
                        <AlertTriangle size={24} style={{ color: '#D97706' }} />
                    </div>
                    <div>
                        <h3 style={{ margin: 0, fontSize: '1.125rem', fontWeight: 800, color: 'var(--color-text-primary, #1e293b)' }}>
                            Itens sem correspondência no catálogo
                        </h3>
                        <p style={{ margin: '4px 0 0', fontSize: '0.875rem', color: 'var(--color-text-muted, #64748b)' }}>
                            {unresolvedCount} {unresolvedCount === 1 ? 'item não possui' : 'itens não possuem'} vínculo com o catálogo de materiais.
                        </p>
                    </div>
                </div>

                {/* Message */}
                <div style={{
                    backgroundColor: '#FFFBEB', border: '1px solid #FDE68A',
                    borderRadius: 'var(--radius-sm, 6px)', padding: '16px',
                    marginBottom: '24px', fontSize: '0.875rem',
                    color: '#92400E', lineHeight: 1.6,
                }}>
                    Existem itens sem correspondência com o catálogo. Para continuar, revise os itens e vincule-os a um item existente ou crie um novo item no catálogo.
                </div>

                {/* Actions */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                    <button
                        onClick={onReviewItems}
                        style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                            padding: '12px 20px', borderRadius: 'var(--radius-sm, 6px)',
                            backgroundColor: 'var(--color-primary, #4f46e5)', color: '#fff',
                            border: 'none', cursor: 'pointer', fontWeight: 700,
                            fontSize: '0.875rem', transition: 'opacity 0.15s',
                        }}
                    >
                        <Search size={16} />
                        Revisar Itens
                    </button>

                    <button
                        onClick={onCancel}
                        style={{
                            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px',
                            padding: '12px 20px', borderRadius: 'var(--radius-sm, 6px)',
                            backgroundColor: 'transparent', color: 'var(--color-text-muted, #64748b)',
                            border: '1px solid var(--color-border, #e2e8f0)', cursor: 'pointer',
                            fontWeight: 600, fontSize: '0.875rem', transition: 'opacity 0.15s',
                        }}
                    >
                        <X size={16} />
                        Cancelar
                    </button>
                </div>
            </div>
        </div>
    );
}
