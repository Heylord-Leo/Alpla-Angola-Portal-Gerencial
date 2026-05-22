import { createContext, useContext, ReactNode } from 'react';
import { Joyride } from 'react-joyride';
import { motion, AnimatePresence } from 'framer-motion';
import { Compass, Info } from 'lucide-react';
import { useGuidedTour } from './useGuidedTour';
import type { GuidedTourContextValue } from './guidedTourTypes';

/**
 * GuidedTourProvider
 * 
 * Context provider that renders the Joyride overlay, the Welcome Modal,
 * and a transient "no steps" toast.
 * Must be placed inside AuthProvider (for useAuth) and inside AppShell
 * (for access to the data-tour DOM elements).
 */

const GuidedTourContext = createContext<GuidedTourContextValue>({
    startTour: () => {},
    startCurrentModuleTour: () => {},
    startCurrentPageTour: () => {},
    getAvailableTours: () => [],
    activeTourId: null,
});

export function useGuidedTourContext() {
    return useContext(GuidedTourContext);
}

interface GuidedTourProviderProps {
    children: ReactNode;
}

export function GuidedTourProvider({ children }: GuidedTourProviderProps) {
    const {
        run,
        steps,
        showWelcome,
        handleEvent,
        startTour,
        startCurrentModuleTour,
        startCurrentPageTour,
        getAvailableTours,
        activeTourId,
        activeTourDef,
        noStepsMessage,
        dismissWelcome,
    } = useGuidedTour();

    // Drawer tours need different Joyride settings:
    // - disable native scroll (we handle it via scrollTargetIntoView)
    // - prevent overlay click close to avoid accidentally dismissing the drawer
    const isDrawerTour = activeTourDef?.level === 'drawer';

    const contextValue: GuidedTourContextValue = {
        startTour,
        startCurrentModuleTour,
        startCurrentPageTour,
        getAvailableTours,
        activeTourId,
    };

    return (
        <GuidedTourContext.Provider value={contextValue}>
            {children}

            {/* Joyride Tour Overlay */}
            <Joyride
                steps={steps}
                run={run}
                continuous
                scrollToFirstStep={!isDrawerTour}
                onEvent={handleEvent}
                locale={{
                    back: 'Voltar',
                    close: 'Concluir',
                    last: 'Concluir',
                    next: 'Próximo',
                    skip: 'Sair do Tour',
                }}
                options={{
                    showProgress: true,
                    spotlightPadding: 8,
                    spotlightRadius: 12,
                    scrollOffset: 80,
                    scrollDuration: 350,
                    overlayClickAction: isDrawerTour ? false : 'close',
                    buttons: ['skip', 'back', 'close', 'primary'],
                    overlayColor: 'rgba(0, 0, 0, 0.5)',
                    primaryColor: 'var(--color-primary)',
                    textColor: 'var(--color-text-main)',
                    backgroundColor: 'var(--color-bg-surface)',
                    arrowColor: 'var(--color-bg-surface)',
                    zIndex: 10000,
                    skipBeacon: true,
                }}
                styles={{
                    tooltip: {
                        borderRadius: '12px',
                        padding: '20px 24px',
                        fontFamily: 'var(--font-family-body)',
                        boxShadow: '0 20px 60px rgba(0,0,0,0.15), 0 4px 12px rgba(0,0,0,0.1)',
                        border: '1px solid var(--color-border)',
                    },
                    tooltipTitle: {
                        fontSize: '1rem',
                        fontWeight: 800,
                        fontFamily: 'var(--font-family-display)',
                        color: 'var(--color-primary)',
                        marginBottom: '8px',
                    },
                    tooltipContent: {
                        fontSize: '0.875rem',
                        fontWeight: 500,
                        lineHeight: 1.6,
                        color: 'var(--color-text-main)',
                        padding: '8px 0 0',
                    },
                    buttonPrimary: {
                        backgroundColor: 'var(--color-primary)',
                        color: 'white',
                        borderRadius: '8px',
                        fontWeight: 700,
                        fontSize: '0.8rem',
                        padding: '8px 20px',
                        fontFamily: 'var(--font-family-display)',
                        letterSpacing: '0.03em',
                        textTransform: 'uppercase' as const,
                        border: 'none',
                        outline: 'none',
                    },
                    buttonBack: {
                        color: 'var(--color-text-muted)',
                        fontWeight: 600,
                        fontSize: '0.8rem',
                        fontFamily: 'var(--font-family-display)',
                        marginRight: '8px',
                    },
                    buttonSkip: {
                        color: 'var(--color-text-muted)',
                        fontWeight: 600,
                        fontSize: '0.75rem',
                        fontFamily: 'var(--font-family-body)',
                    },
                    buttonClose: {
                        color: 'var(--color-text-muted)',
                    },
                    tooltipFooter: {
                        marginTop: '12px',
                    },
                }}
            />

            {/* Welcome Modal (portal-main only) */}
            <AnimatePresence>
                {showWelcome && (
                    <motion.div
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        transition={{ duration: 0.3 }}
                        style={{
                            position: 'fixed',
                            inset: 0,
                            zIndex: 'var(--z-modal)' as any,
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            backgroundColor: 'rgba(0, 0, 0, 0.45)',
                            backdropFilter: 'blur(4px)',
                        }}
                        onClick={dismissWelcome}
                    >
                        <motion.div
                            initial={{ opacity: 0, y: 30, scale: 0.95 }}
                            animate={{ opacity: 1, y: 0, scale: 1 }}
                            exit={{ opacity: 0, y: 20, scale: 0.95 }}
                            transition={{ duration: 0.35, ease: [0.4, 0, 0.2, 1] }}
                            onClick={(e) => e.stopPropagation()}
                            style={{
                                backgroundColor: 'var(--color-bg-surface)',
                                border: '1px solid var(--color-border)',
                                borderRadius: '16px',
                                padding: '40px 48px',
                                maxWidth: '440px',
                                width: '90vw',
                                textAlign: 'center',
                                boxShadow: '0 24px 80px rgba(0,0,0,0.2), 0 8px 24px rgba(0,0,0,0.1)',
                                position: 'relative',
                            }}
                        >
                            {/* Icon */}
                            <div style={{
                                width: '64px',
                                height: '64px',
                                borderRadius: '16px',
                                background: 'linear-gradient(135deg, rgba(var(--color-primary-rgb), 0.12), rgba(var(--color-primary-rgb), 0.04))',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                margin: '0 auto 24px',
                                border: '1px solid rgba(var(--color-primary-rgb), 0.15)',
                            }}>
                                <Compass size={32} strokeWidth={2} style={{ color: 'var(--color-primary)' }} />
                            </div>

                            {/* Title */}
                            <h2 style={{
                                fontSize: '1.35rem',
                                fontWeight: 800,
                                fontFamily: 'var(--font-family-display)',
                                color: 'var(--color-primary)',
                                margin: '0 0 8px',
                                letterSpacing: '-0.01em',
                            }}>
                                Bem-vindo ao Portal Gerencial!
                            </h2>

                            {/* Subtitle */}
                            <p style={{
                                fontSize: '0.9rem',
                                fontWeight: 500,
                                color: 'var(--color-text-muted)',
                                lineHeight: 1.6,
                                margin: '0 0 32px',
                                fontFamily: 'var(--font-family-body)',
                            }}>
                                Quer fazer um tour rápido para conhecer as principais áreas do sistema?
                            </p>

                            {/* Actions */}
                            <div style={{ display: 'flex', gap: '12px', justifyContent: 'center' }}>
                                <button
                                    onClick={dismissWelcome}
                                    style={{
                                        padding: '10px 24px',
                                        backgroundColor: 'transparent',
                                        color: 'var(--color-text-muted)',
                                        border: '1px solid var(--color-border)',
                                        borderRadius: '8px',
                                        fontWeight: 700,
                                        fontSize: '0.85rem',
                                        fontFamily: 'var(--font-family-display)',
                                        cursor: 'pointer',
                                        transition: 'all 0.2s',
                                        letterSpacing: '0.02em',
                                    }}
                                    onMouseEnter={(e) => {
                                        e.currentTarget.style.backgroundColor = 'var(--color-bg-page)';
                                        e.currentTarget.style.borderColor = 'var(--color-border-heavy)';
                                    }}
                                    onMouseLeave={(e) => {
                                        e.currentTarget.style.backgroundColor = 'transparent';
                                        e.currentTarget.style.borderColor = 'var(--color-border)';
                                    }}
                                >
                                    Agora Não
                                </button>
                                <button
                                    onClick={() => startTour('portal-main')}
                                    style={{
                                        padding: '10px 28px',
                                        backgroundColor: 'var(--color-primary)',
                                        color: 'white',
                                        border: 'none',
                                        borderRadius: '8px',
                                        fontWeight: 800,
                                        fontSize: '0.85rem',
                                        fontFamily: 'var(--font-family-display)',
                                        cursor: 'pointer',
                                        transition: 'all 0.2s',
                                        letterSpacing: '0.03em',
                                        textTransform: 'uppercase',
                                        boxShadow: '0 4px 12px rgba(var(--color-primary-rgb), 0.3)',
                                    }}
                                    onMouseEnter={(e) => {
                                        e.currentTarget.style.transform = 'translateY(-1px)';
                                        e.currentTarget.style.boxShadow = '0 6px 20px rgba(var(--color-primary-rgb), 0.4)';
                                    }}
                                    onMouseLeave={(e) => {
                                        e.currentTarget.style.transform = 'translateY(0)';
                                        e.currentTarget.style.boxShadow = '0 4px 12px rgba(var(--color-primary-rgb), 0.3)';
                                    }}
                                >
                                    Iniciar Tour
                                </button>
                            </div>
                        </motion.div>
                    </motion.div>
                )}
            </AnimatePresence>

            {/* No Steps Toast — auto-dismisses after 3s */}
            <AnimatePresence>
                {noStepsMessage && (
                    <motion.div
                        initial={{ opacity: 0, y: -20 }}
                        animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0, y: -20 }}
                        transition={{ duration: 0.25 }}
                        style={{
                            position: 'fixed',
                            top: '80px',
                            left: '50%',
                            transform: 'translateX(-50%)',
                            zIndex: 'var(--z-toast)' as any,
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            borderRadius: '10px',
                            padding: '12px 20px',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '10px',
                            boxShadow: '0 8px 30px rgba(0,0,0,0.12)',
                            fontFamily: 'var(--font-family-body)',
                            fontSize: '0.85rem',
                            fontWeight: 600,
                            color: 'var(--color-text-main)',
                            whiteSpace: 'nowrap',
                        }}
                    >
                        <Info size={16} style={{ color: 'var(--color-text-muted)', flexShrink: 0 }} />
                        {noStepsMessage}
                    </motion.div>
                )}
            </AnimatePresence>
        </GuidedTourContext.Provider>
    );
}
