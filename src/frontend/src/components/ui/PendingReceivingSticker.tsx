import { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate, useLocation } from 'react-router-dom';
import { X, PackageCheck, ArrowRight } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { Z_INDEX } from '../../constants/ui';
import { usePendingReceivingCount } from '../../hooks/usePendingReceivingCount';

export function PendingReceivingSticker() {
    const { count, loading } = usePendingReceivingCount();
    const navigate = useNavigate();
    const location = useLocation();
    const [dismissed, setDismissed] = useState(() => {
        return sessionStorage.getItem('pendingReceivingDismissed') === 'true';
    });

    const isVisible = !loading && count > 0 && !dismissed;

    useEffect(() => {
        if (count === 0 && dismissed) {
            sessionStorage.removeItem('pendingReceivingDismissed');
            setDismissed(false);
        }
    }, [count, dismissed]);

    const handleDismiss = () => {
        setDismissed(true);
        sessionStorage.setItem('pendingReceivingDismissed', 'true');
    };

    const handleNavigate = () => {
        handleDismiss();
        navigate('/receiving');
    };

    const onReceivingPage = location.pathname === '/receiving';

    const title = count === 1 ? 'Entrega pendente' : 'Entregas pendentes';
    const body = count === 1
        ? 'Você possui 1 pedido aguardando confirmação de entrega.'
        : `Você possui ${count} pedidos aguardando confirmação de entrega.`;

    return createPortal(
        <AnimatePresence>
            {isVisible && !onReceivingPage && (
                <motion.div
                    key="pending-receiving-sticker"
                    initial={{ opacity: 0, x: 60, scale: 0.95 }}
                    animate={{ opacity: 1, x: 0, scale: 1 }}
                    exit={{ opacity: 0, x: 60, scale: 0.95 }}
                    transition={{ duration: 0.35, ease: [0.4, 0, 0.2, 1] }}
                    role="status"
                    aria-live="polite"
                    style={{
                        position: 'fixed',
                        bottom: '120px', // slightly above approvals sticker to avoid overlap
                        right: '24px',
                        width: '340px',
                        maxWidth: 'calc(100vw - 48px)',
                        backgroundColor: '#F3E8FF', // purple light
                        borderRadius: 'var(--radius-md)',
                        borderLeft: '4px solid #9333EA', // purple main
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
                        <PackageCheck
                            size={20}
                            strokeWidth={2.2}
                            style={{
                                flexShrink: 0,
                                marginTop: '1px',
                                color: '#9333EA'
                            }}
                        />
                        <div style={{ flex: 1, minWidth: 0 }}>
                            <h4 style={{
                                margin: '0 0 4px 0',
                                fontSize: '0.9rem',
                                fontWeight: 800,
                                color: '#6B21A8',
                                letterSpacing: '-0.01em',
                                lineHeight: 1.2
                            }}>
                                {title}
                            </h4>
                            <p style={{
                                margin: 0,
                                fontSize: '0.8125rem',
                                color: '#7E22CE',
                                fontWeight: 500,
                                lineHeight: 1.4
                            }}>
                                {body}
                            </p>
                        </div>
                        <button
                            onClick={handleDismiss}
                            style={{
                                background: 'transparent',
                                border: 'none',
                                padding: '4px',
                                cursor: 'pointer',
                                color: '#A855F7',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                borderRadius: '4px',
                                marginLeft: '-4px',
                                marginTop: '-4px',
                                transition: 'all 0.2s'
                            }}
                            onMouseOver={(e) => {
                                e.currentTarget.style.backgroundColor = 'rgba(147, 51, 234, 0.1)';
                                e.currentTarget.style.color = '#7E22CE';
                            }}
                            onMouseOut={(e) => {
                                e.currentTarget.style.backgroundColor = 'transparent';
                                e.currentTarget.style.color = '#A855F7';
                            }}
                            aria-label="Dispensar aviso"
                        >
                            <X size={16} strokeWidth={2.5} />
                        </button>
                    </div>

                    <div style={{
                        padding: '12px 14px 14px 44px',
                        display: 'flex',
                        justifyContent: 'flex-start'
                    }}>
                        <button
                            onClick={handleNavigate}
                            style={{
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: '6px',
                                background: 'transparent',
                                border: 'none',
                                padding: 0,
                                color: '#9333EA',
                                fontSize: '0.8125rem',
                                fontWeight: 800,
                                cursor: 'pointer',
                                textTransform: 'uppercase',
                                letterSpacing: '0.02em',
                                transition: 'color 0.2s'
                            }}
                            onMouseOver={(e) => e.currentTarget.style.color = '#7E22CE'}
                            onMouseOut={(e) => e.currentTarget.style.color = '#9333EA'}
                        >
                            Ir para Recebimento
                            <ArrowRight size={14} strokeWidth={2.5} />
                        </button>
                    </div>
                </motion.div>
            )}
        </AnimatePresence>,
        document.body
    );
}
