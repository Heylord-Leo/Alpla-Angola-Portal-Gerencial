import { ReactNode, useState } from 'react';
import { Outlet } from 'react-router-dom';
import { Sidebar } from '../components/layout/Sidebar';
import { Topbar } from '../components/layout/Topbar';
import { ErrorBoundary } from '../components/ErrorBoundary';
import { motion } from 'framer-motion';

interface AppShellProps {
    children?: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
    const [isSidebarExpanded, setIsSidebarExpanded] = useState(() => {
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

    return (
        <div style={{ display: 'flex', flexDirection: 'column', minHeight: '100vh', backgroundColor: 'var(--color-bg-page)' }}>
            {/* Topbar acts as the solid, heavy corporate anchor */}
            <Topbar />

            {/* The main workspace is an asymmetric overlapping grid */}
            <div style={{
                display: 'grid',
                gridTemplateColumns: isSidebarExpanded ? '260px minmax(0, 1fr)' : '80px minmax(0, 1fr)',
                gap: '2rem',
                padding: '2rem 3rem',
                maxWidth: '1800px',
                margin: '0 auto',
                width: '100%',
                flex: 1,
                alignItems: 'start',
                transition: 'grid-template-columns 0.3s ease-in-out'
            }}>
                {/* Full-height sidebar container with independent scroll support */}
                <div style={{ 
                    position: 'sticky', 
                    top: 'calc(64px + 2rem)', 
                    height: 'calc(100vh - 64px - 4rem)',
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
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    transition={{ duration: 0.5, ease: 'easeOut' }}
                    style={{
                        backgroundColor: 'var(--color-bg-surface)',
                        border: '1px solid var(--color-border)',
                        boxShadow: 'var(--shadow-soft)',
                        borderRadius: 'var(--radius-lg)',
                        minHeight: '70vh',
                        padding: '1.5rem',
                        position: 'relative',
                        minWidth: 0 // Crucial for grid/flex child consistency
                    }}
                >
                    {children || <ErrorBoundary fallbackName="AppShell.Outlet"><Outlet /></ErrorBoundary>}
                </motion.main>
            </div>
        </div>
    );
}
