import { ReactNode, useState, useEffect } from 'react';
import { Outlet } from 'react-router-dom';
import { Sidebar } from '../components/layout/Sidebar';
import { Topbar } from '../components/layout/Topbar';
import { ErrorBoundary } from '../components/ErrorBoundary';
import { motion } from 'framer-motion';
import { GuidedTourProvider } from '../features/guided-tour/GuidedTourProvider';
import { PendingApprovalsSticker } from '../components/ui/PendingApprovalsSticker';
import { PendingReceivingSticker } from '../components/ui/PendingReceivingSticker';
import { EnvironmentBanner } from '../components/ui/EnvironmentBanner';
import { useEnvironment } from '../contexts/EnvironmentContext';

/** Breakpoint at which the sidebar auto-collapses for small laptops */
const COMPACT_BREAKPOINT = 1366;

interface AppShellProps {
    children?: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
    const [isSidebarExpanded, setIsSidebarExpanded] = useState(() => {
        // On small viewports, default to collapsed regardless of saved preference
        if (typeof window !== 'undefined' && window.innerWidth <= COMPACT_BREAKPOINT) {
            return false;
        }
        const saved = localStorage.getItem('sidebarExpanded');
        return saved !== null ? JSON.parse(saved) : true;
    });

    const toggleSidebar = () => {
        setIsSidebarExpanded((prev: boolean) => {
            const newValue = !prev;
            localStorage.setItem('sidebarExpanded', JSON.stringify(newValue));
            return newValue;
        });
    };

    // Auto-collapse sidebar on small viewports
    useEffect(() => {
        const mql = window.matchMedia(`(max-width: ${COMPACT_BREAKPOINT}px)`);
        const handler = (e: MediaQueryListEvent) => {
            if (e.matches && isSidebarExpanded) {
                setIsSidebarExpanded(false);
            }
        };
        mql.addEventListener('change', handler);
        return () => mql.removeEventListener('change', handler);
    }, [isSidebarExpanded]);

    const { showBanner } = useEnvironment();
    const bannerOffset = showBanner ? 'var(--env-banner-height)' : '0px';

    return (
        <GuidedTourProvider>
            <div className={`app-shell${showBanner ? ' has-env-banner' : ''}`} style={{ display: 'flex', flexDirection: 'column', minHeight: '100vh', backgroundColor: 'var(--color-bg-page)', paddingTop: showBanner ? 'var(--env-banner-height)' : undefined, overflowX: 'clip' }}>
                {/* Environment indicator — rendered once for authenticated pages */}
                <EnvironmentBanner />

                {/* Topbar acts as the solid, heavy corporate anchor */}
                <Topbar />

                {/* The main workspace is an asymmetric overlapping grid */}
                <div className="app-shell-grid" style={{
                    display: 'grid',
                    gridTemplateColumns: isSidebarExpanded ? '260px minmax(0, 1fr)' : '80px minmax(0, 1fr)',
                    gap: '2rem',
                    padding: '2rem 3rem',
                    maxWidth: '1800px',
                    margin: '0 auto',
                    width: '100%',
                    flex: 1,
                    alignItems: 'stretch',
                    transition: 'grid-template-columns 0.3s ease-in-out'
                }}>
                    {/* Full-height sidebar container with independent scroll support */}
                    <div className="app-shell-sidebar" style={{ 
                        position: 'sticky', 
                        top: `calc(64px + 1rem + ${bannerOffset})`, 
                        height: `calc(100vh - 64px - 2rem - ${bannerOffset})`,
                        display: 'flex',
                        flexDirection: 'column',
                        transition: 'width 0.3s ease-in-out'
                    }}>
                        <Sidebar 
                            isExpanded={isSidebarExpanded} 
                            onToggle={toggleSidebar} 
                        />
                    </div>

                    {/* Main Content Area has a heavy dramatic entry */}
                    <motion.main
                        className="app-shell-main"
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        transition={{ duration: 0.5, ease: 'easeOut' }}
                        style={{
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            boxShadow: 'var(--shadow-soft)',
                            borderRadius: 'var(--radius-lg)',
                            minHeight: '70vh',
                            display: 'flex',
                            flexDirection: 'column',
                            minWidth: 0,
                            maxWidth: '100%',
                            overflowX: 'clip',
                            padding: '1.5rem',
                            position: 'relative'
                        }}
                    >
                        {children || <ErrorBoundary fallbackName="AppShell.Outlet"><Outlet /></ErrorBoundary>}
                    </motion.main>
                </div>

                {/* Global Right-Side Stickers */}
                <PendingApprovalsSticker />
                <PendingReceivingSticker />
            </div>
        </GuidedTourProvider>
    );
}
