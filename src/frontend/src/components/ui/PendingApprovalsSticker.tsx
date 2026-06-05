import { useState, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate, useLocation } from 'react-router-dom';
import { X, ClipboardCheck, ArrowRight } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { Z_INDEX } from '../../constants/ui';
import { usePendingApprovalsCount } from '../../hooks/usePendingApprovalsCount';

/**
 * Right-side sticker notification for pending approvals.
 *
 * Behavior:
 *  - Shows when the logged-in user has pending approvals (count > 0).
 *  - Dismissible via "X" — stays hidden for the current browser session.
 *  - Auto-hides when count drops to 0.
 *  - Reappears after page reload / new session if count is still > 0.
 *  - "Ver aprovações" button navigates to /approvals.
 *
 * Visual style follows the existing Feedback.tsx pattern (border-left, bg tint, portal).
 */
export function PendingApprovalsSticker() {
    const { count, loading } = usePendingApprovalsCount();
    const navigate = useNavigate();
    const location = useLocation();
    const [dismissed, setDismissed] = useState(() => {
        return sessionStorage.getItem('pendingApprovalsDismissed') === 'true';
    });

    // Auto-show again if count changes from 0 → N (e.g. new approval arrives during session)
    // but respect the dismissed flag set by the user
    const isVisible = !loading && count > 0 && !dismissed;

    // If count drops to 0, clear the dismissed flag so next time it can show again
    useEffect(() => {
        if (count === 0 && dismissed) {
            sessionStorage.removeItem('pendingApprovalsDismissed');
            setDismissed(false);
        }
    }, [count, dismissed]);

    const handleDismiss = () => {
        setDismissed(true);
        sessionStorage.setItem('pendingApprovalsDismissed', 'true');
    };

    const handleNavigate = () => {
        handleDismiss();
        navigate('/approvals');
    };

    // Don't show if already on the approvals page
    const onApprovalsPage = location.pathname === '/approvals';

    const title = count === 1 ? 'Aprovação pendente' : 'Aprovações pendentes';
    const body = count === 1
        ? 'Você possui 1 item aguardando aprovação.'
        : `Você possui ${count} itens aguardando aprovação.`;

    return createPortal(
        <AnimatePresence>
            {isVisible && !onApprovalsPage && (
                <motion.div
                    key="pending-approvals-sticker"
                    initial={{ opacity: 0, x: 60, scale: 0.95 }}
                    animate={{ opacity: 1, x: 0, scale: 1 }}
                    exit={{ opacity: 0, x: 60, scale: 0.95 }}
                    transition={{ duration: 0.35, ease: [0.4, 0, 0.2, 1] }}
                    role="status"
                    aria-live="polite"
                    style={{
                        position: 'fixed',
                        bottom: '24px',
                        right: '24px',
                        width: '340px',
                        maxWidth: 'calc(100vw - 48px)',
                        backgroundColor: '#EFF6FF',
                        borderRadius: 'var(--radius-md)',
                        borderLeft: '4px solid var(--color-primary)',
                        boxShadow: '0 8px 24px rgba(0, 0, 0, 0.12), 0 2px 8px rgba(0, 0, 0, 0.08)',
                        zIndex: Z_INDEX.TOAST as any,
                        overflow: 'hidden',
                        pointerEvents: 'auto'
                    }}
                >
                    {/* Header row */}
                    <div style={{
                        display: 'flex',
                        alignItems: 'flex-start',
                        gap: '10px',
                        padding: '14px 14px 0 14px'
                    }}>
                        <ClipboardCheck
                            size={20}
                            strokeWidth={2.2}
                            style={{
                                flexShrink: 0,
                                marginTop: '1px',
                                color: 'var(--color-primary)'
                            }}
                        />
                        <div style={{ flex: 1, minWidth: 0 }}>
                            <div style={{
                                fontFamily: 'var(--font-family-display)',
                                fontWeight: 700,
                                fontSize: '0.82rem',
                                color: '#1E40AF',
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
                                color: '#1D4ED8',
                                lineHeight: 1.5,
                                marginTop: '4px'
                            }}>
                                {body}
                            </div>
                        </div>
                        <button
                            onClick={handleDismiss}
                            aria-label="Fechar notificação"
                            style={{
                                background: 'none',
                                border: 'none',
                                cursor: 'pointer',
                                color: '#1D4ED8',
                                padding: '2px',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                opacity: 0.6,
                                transition: 'opacity 0.2s',
                                flexShrink: 0
                            }}
                            onMouseOver={(e) => (e.currentTarget.style.opacity = '1')}
                            onMouseOut={(e) => (e.currentTarget.style.opacity = '0.6')}
                        >
                            <X size={16} />
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
                                color: 'var(--color-primary)',
                                textTransform: 'uppercase',
                                letterSpacing: '0.04em',
                                padding: '6px 10px',
                                borderRadius: 'var(--radius-sm)',
                                transition: 'background-color 0.15s ease',
                                marginLeft: '30px' // Align with text (icon width + gap)
                            }}
                            onMouseOver={(e) => (e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.10)')}
                            onMouseOut={(e) => (e.currentTarget.style.backgroundColor = 'transparent')}
                        >
                            Ver aprovações
                            <ArrowRight size={14} strokeWidth={2.5} />
                        </button>
                    </div>
                </motion.div>
            )}
        </AnimatePresence>,
        document.body
    );
}
