import { motion, AnimatePresence } from 'framer-motion';
import { X } from 'lucide-react';
import { RequestEdit } from '../../RequestEdit';

interface RequestDrawerProps {
    requestId: string | null;
    isOpen: boolean;
    onClose: () => void;
}

export function RequestDrawerPresentation({ requestId, isOpen, onClose }: RequestDrawerProps) {
    return (
        <AnimatePresence>
            {isOpen && requestId && (
                <>
                    {/* Overlay */}
                    <motion.div
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        onClick={onClose}
                        style={{
                            position: 'fixed',
                            inset: 0,
                            backgroundColor: 'rgba(15, 23, 42, 0.4)',
                            backdropFilter: 'blur(4px)',
                            WebkitBackdropFilter: 'blur(4px)',
                            zIndex: 'var(--z-drawer)' as any,
                        }}
                    />

                    {/* Drawer */}
                    <motion.div
                        initial={{ x: '100%', opacity: 0 }}
                        animate={{ x: 0, opacity: 1 }}
                        exit={{ x: '100%', opacity: 0 }}
                        transition={{ type: 'spring', damping: 25, stiffness: 200 }}
                        style={{
                            position: 'fixed',
                            right: 0,
                            top: 0,
                            bottom: 0,
                            width: '800px',
                            maxWidth: '100vw',
                            backgroundColor: 'var(--color-bg-surface)',
                            boxShadow: 'var(--shadow-premium)',
                            display: 'flex',
                            flexDirection: 'column',
                            zIndex: 'calc(var(--z-drawer) + 1)' as any,
                            overflow: 'hidden',
                        }}
                    >
                        {/* Header */}
                        <div style={{
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            padding: '16px 20px',
                            borderBottom: '1px solid var(--color-border)',
                            backgroundColor: 'var(--color-bg-surface)',
                            position: 'sticky',
                            top: 0,
                            zIndex: 10,
                            flexShrink: 0,
                        }}>
                            <div>
                                <h2 style={{ 
                                    fontSize: '1.1rem', 
                                    fontWeight: 800, 
                                    color: 'var(--color-primary)',
                                    margin: 0,
                                    textTransform: 'uppercase',
                                    letterSpacing: '0.02em',
                                }}>Detalhes do Pedido</h2>
                                <p style={{ 
                                    fontSize: '0.8rem', 
                                    color: 'var(--color-text-muted)',
                                    margin: '2px 0 0',
                                    fontWeight: 500,
                                    textTransform: 'none',
                                }}>Visualização Rápida</p>
                            </div>
                            <button
                                onClick={onClose}
                                style={{
                                    padding: '8px',
                                    borderRadius: 'var(--radius-md)',
                                    border: '1px solid var(--color-border)',
                                    backgroundColor: 'transparent',
                                    cursor: 'pointer',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    transition: 'background-color 0.15s ease',
                                    color: 'var(--color-text-muted)',
                                }}
                                onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'; }}
                                onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = 'transparent'; }}
                            >
                                <X size={20} />
                            </button>
                        </div>

                        {/* Content Scrollable Area */}
                        <div style={{ flex: 1, overflowY: 'auto', width: '100%', position: 'relative', padding: '24px' }}>
                            <RequestEdit requestId={requestId} onClose={onClose} />
                        </div>
                    </motion.div>
                </>
            )}
        </AnimatePresence>
    );
}
