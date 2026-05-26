import { useState, useRef, useEffect } from 'react';
import { HelpCircle, Compass, Map, MonitorSmartphone } from 'lucide-react';
import { useGuidedTourContext } from './GuidedTourProvider';
import { getToursForRoute } from './guidedTourRegistry';
import { useLocation } from 'react-router-dom';
import { Z_INDEX } from '../../constants/ui';

/**
 * GuidedTourButton
 * 
 * Permanent help button rendered in the Topbar.
 * Opens a dropdown menu with up to 3 contextual tour options:
 * 1. "Tour inicial do Portal" — always visible
 * 2. "Tour deste módulo" — visible if a module tour exists for the current route
 * 3. "Tour desta tela" — visible if a page tour exists for the current route
 * 
 * Styled to match the existing Topbar action buttons (NotificationBell pattern).
 */
export function GuidedTourButton() {
    const { startTour, startCurrentModuleTour, startCurrentPageTour } = useGuidedTourContext();
    const location = useLocation();
    const [isOpen, setIsOpen] = useState(false);
    const containerRef = useRef<HTMLDivElement>(null);

    // Resolve available tours for the current route
    const { module, page } = getToursForRoute(location.pathname);

    // Close on outside click
    useEffect(() => {
        if (!isOpen) return;
        const handleClickOutside = (e: MouseEvent) => {
            if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
                setIsOpen(false);
            }
        };
        document.addEventListener('mousedown', handleClickOutside);
        return () => document.removeEventListener('mousedown', handleClickOutside);
    }, [isOpen]);

    // Close on Escape
    useEffect(() => {
        if (!isOpen) return;
        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') setIsOpen(false);
        };
        document.addEventListener('keydown', handleKeyDown);
        return () => document.removeEventListener('keydown', handleKeyDown);
    }, [isOpen]);

    const handleSelect = (action: () => void) => {
        setIsOpen(false);
        action();
    };

    return (
        <div ref={containerRef} style={{ position: 'relative' }}>
            <button
                data-tour="guided-help-button"
                onClick={() => setIsOpen(!isOpen)}
                title="Tour guiado"
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    gap: '4px',
                    width: '40px',
                    height: '40px',
                    background: isOpen ? 'var(--color-bg-surface)' : 'rgba(255, 255, 255, 0.1)',
                    border: '2px solid transparent',
                    cursor: 'pointer',
                    color: isOpen ? 'var(--color-primary)' : 'white',
                    transition: 'all 0.2s',
                    borderRadius: '4px',
                    position: 'relative',
                }}
                onMouseOver={(e) => {
                    if (!isOpen) {
                        e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)';
                        e.currentTarget.style.color = 'var(--color-primary)';
                    }
                }}
                onMouseOut={(e) => {
                    if (!isOpen) {
                        e.currentTarget.style.backgroundColor = 'rgba(255,255,255,0.1)';
                        e.currentTarget.style.color = 'white';
                    }
                }}
            >
                <HelpCircle size={20} strokeWidth={2.5} />
            </button>

            {/* Dropdown Menu */}
            {isOpen && (
                <div style={{
                    position: 'absolute',
                    top: 'calc(100% + 8px)',
                    right: 0,
                    minWidth: '240px',
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)',
                    borderRadius: '10px',
                    boxShadow: '0 12px 40px rgba(0,0,0,0.15), 0 4px 12px rgba(0,0,0,0.08)',
                    zIndex: Z_INDEX.POPOVER as any,
                    overflow: 'hidden',
                    fontFamily: 'var(--font-family-body)',
                }}>
                    {/* Header */}
                    <div style={{
                        padding: '12px 16px 8px',
                        fontSize: '0.65rem',
                        fontWeight: 800,
                        color: 'var(--color-text-muted)',
                        textTransform: 'uppercase',
                        letterSpacing: '0.1em',
                    }}>
                        Tours Disponíveis
                    </div>

                    {/* Portal Tour — always visible */}
                    <DropdownItem
                        icon={<Compass size={16} strokeWidth={2} />}
                        label="Tour inicial do Portal"
                        description="Conhecer a estrutura geral do sistema"
                        onClick={() => handleSelect(() => startTour('portal-main'))}
                    />

                    {/* Module Tour — visible if a module tour exists */}
                    {module && (
                        <DropdownItem
                            icon={<Map size={16} strokeWidth={2} />}
                            label={module.label}
                            description="Explorar as áreas deste módulo"
                            onClick={() => handleSelect(startCurrentModuleTour)}
                        />
                    )}

                    {/* Page Tour — visible if a page tour exists */}
                    {page && (
                        <DropdownItem
                            icon={<MonitorSmartphone size={16} strokeWidth={2} />}
                            label={page.label}
                            description="Aprender a usar esta tela"
                            onClick={() => handleSelect(startCurrentPageTour)}
                        />
                    )}

                    {/* Bottom padding */}
                    <div style={{ height: '4px' }} />
                </div>
            )}
        </div>
    );
}

/** Reusable dropdown menu item */
function DropdownItem({ icon, label, description, onClick }: {
    icon: React.ReactNode;
    label: string;
    description: string;
    onClick: () => void;
}) {
    return (
        <button
            onClick={onClick}
            style={{
                display: 'flex',
                alignItems: 'center',
                gap: '12px',
                width: '100%',
                padding: '10px 16px',
                border: 'none',
                background: 'none',
                cursor: 'pointer',
                textAlign: 'left',
                transition: 'background-color 0.15s',
                color: 'var(--color-text-main)',
            }}
            onMouseEnter={(e) => {
                e.currentTarget.style.backgroundColor = 'rgba(var(--color-primary-rgb), 0.06)';
            }}
            onMouseLeave={(e) => {
                e.currentTarget.style.backgroundColor = 'transparent';
            }}
        >
            <div style={{
                width: '32px',
                height: '32px',
                borderRadius: '8px',
                backgroundColor: 'rgba(var(--color-primary-rgb), 0.08)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
                color: 'var(--color-primary)',
            }}>
                {icon}
            </div>
            <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{
                    fontSize: '0.85rem',
                    fontWeight: 700,
                    color: 'var(--color-text-main)',
                    fontFamily: 'var(--font-family-display)',
                    lineHeight: 1.3,
                }}>
                    {label}
                </div>
                <div style={{
                    fontSize: '0.7rem',
                    fontWeight: 500,
                    color: 'var(--color-text-muted)',
                    lineHeight: 1.3,
                    marginTop: '2px',
                }}>
                    {description}
                </div>
            </div>
        </button>
    );
}
