import { useState, useEffect } from 'react';

import { useNavigate, useLocation } from 'react-router-dom';
import { X, FileWarning, ArrowRight } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { Z_INDEX } from '../../constants/ui';
import { usePendingPoCorrectionsCount } from '../../hooks/usePendingPoCorrectionsCount';

/**
 * v2.242.0 — footer sticker for PO groups returned by Finance for Buyer correction. Same visual
 * architecture as the approvals/receiving stickers; amber accent to distinguish it. Cross-type,
 * personal count (QUOTATION + PAYMENT corrections the Buyer owns); CTA lands on the personal work
 * surface "Para Minha Ação" (/requests?isAttention=true), which now surfaces both types.
 */
export function PendingPoCorrectionsSticker() {
    const { count, loading } = usePendingPoCorrectionsCount();
    const navigate = useNavigate();
    const location = useLocation();
    const [dismissed, setDismissed] = useState(() => {
        return sessionStorage.getItem('pendingPoCorrectionsDismissed') === 'true';
    });

    const isVisible = !loading && count > 0 && !dismissed;

    useEffect(() => {
        if (count === 0 && dismissed) {
            sessionStorage.removeItem('pendingPoCorrectionsDismissed');
            setDismissed(false);
        }
    }, [count, dismissed]);

    const handleDismiss = () => {
        setDismissed(true);
        sessionStorage.setItem('pendingPoCorrectionsDismissed', 'true');
    };

    const handleNavigate = () => {
        handleDismiss();
        // Cross-type personal correction work lives in "Para Minha Ação" (QUOTATION + PAYMENT). The
        // Buyer queue is QUOTATION-only, so the CTA lands on the requests personal-action surface.
        navigate('/requests?isAttention=true');
    };

    const onQueuePage = location.pathname === '/requests';

    const title = count === 1 ? 'Correção de P.O. pendente' : 'Correções de P.O. pendentes';
    const body = count === 1
        ? 'Você possui 1 pedido devolvido por Finanças aguardando correção.'
        : `Você possui ${count} pedidos devolvidos por Finanças aguardando correção.`;

    return (
        <AnimatePresence>
            {isVisible && !onQueuePage && (
                <motion.div
                    key="pending-po-corrections-sticker"
                    initial={{ opacity: 0, x: 60, scale: 0.95 }}
                    animate={{ opacity: 1, x: 0, scale: 1 }}
                    exit={{ opacity: 0, x: 60, scale: 0.95 }}
                    transition={{ duration: 0.35, ease: [0.4, 0, 0.2, 1] }}
                    role="status"
                    aria-live="polite"
                    style={{
                        width: '340px',
                        maxWidth: 'calc(100vw - 48px)',
                        backgroundColor: '#FEF3C7', // amber light
                        borderRadius: 'var(--radius-md)',
                        borderLeft: '4px solid #D97706', // amber main
                        boxShadow: '0 8px 24px rgba(0, 0, 0, 0.12), 0 2px 8px rgba(0, 0, 0, 0.08)',
                        zIndex: Z_INDEX.TOAST as any,
                        overflow: 'hidden',
                        pointerEvents: 'auto'
                    }}
                >
                    <div style={{
                        display: 'flex',
                        alignItems: 'flex-start',
                        gap: '10px',
                        padding: '14px 14px 0 14px'
                    }}>
                        <FileWarning
                            size={20}
                            strokeWidth={2.2}
                            style={{ flexShrink: 0, marginTop: '1px', color: '#D97706' }}
                        />
                        <div style={{ flex: 1, minWidth: 0 }}>
                            <div style={{
                                fontFamily: 'var(--font-family-display)',
                                fontWeight: 700,
                                fontSize: '0.82rem',
                                color: '#92400E',
                                textTransform: 'uppercase',
                                letterSpacing: '0.03em',
                                lineHeight: 1.3
                            }}>
                                {title}
                            </div>
                            <div style={{
                                fontFamily: 'var(--font-family-body)',
                                fontWeight: 500,
                                fontSize: '0.83rem',
                                color: '#B45309',
                                lineHeight: 1.5,
                                marginTop: '4px'
                            }}>
                                {body}
                            </div>
                        </div>
                        <button
                            onClick={handleDismiss}
                            style={{
                                background: 'transparent',
                                border: 'none',
                                padding: '4px',
                                cursor: 'pointer',
                                color: '#D97706',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                borderRadius: '4px',
                                marginLeft: '-4px',
                                marginTop: '-4px',
                                transition: 'all 0.2s'
                            }}
                            onMouseOver={(e) => {
                                e.currentTarget.style.backgroundColor = 'rgba(217, 119, 6, 0.1)';
                                e.currentTarget.style.color = '#92400E';
                            }}
                            onMouseOut={(e) => {
                                e.currentTarget.style.backgroundColor = 'transparent';
                                e.currentTarget.style.color = '#D97706';
                            }}
                            aria-label="Dispensar aviso"
                        >
                            <X size={16} strokeWidth={2.5} />
                        </button>
                    </div>

                    {/* Action row */}
                    <div style={{ padding: '10px 14px 14px 14px' }}>
                        <button
                            onClick={handleNavigate}
                            style={{
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: '6px',
                                background: 'none',
                                border: 'none',
                                cursor: 'pointer',
                                fontFamily: 'var(--font-family-display)',
                                fontWeight: 700,
                                fontSize: '0.78rem',
                                color: '#D97706',
                                textTransform: 'uppercase',
                                letterSpacing: '0.04em',
                                padding: '6px 10px',
                                borderRadius: 'var(--radius-sm)',
                                transition: 'background-color 0.15s ease',
                                marginLeft: '30px'
                            }}
                            onMouseOver={(e) => (e.currentTarget.style.backgroundColor = 'rgba(217, 119, 6, 0.1)')}
                            onMouseOut={(e) => (e.currentTarget.style.backgroundColor = 'transparent')}
                        >
                            Ver correções
                            <ArrowRight size={14} strokeWidth={2.5} />
                        </button>
                    </div>
                </motion.div>
            )}
        </AnimatePresence>
    );
}
