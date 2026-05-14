import React, { useState, useRef, useEffect } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import {
    Activity, Calendar, CalendarDays, Clock, Users, CreditCard,
    CalendarRange, ShieldCheck, ChevronDown
} from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { useAuth } from '../../features/auth/AuthContext';
import './hr-landing.css';

/* ─── Tab Definitions ─── */

interface HRTab {
    id: string;
    label: string;
    path: string;
    icon: React.ComponentType<{ size?: string | number }>;
    /** If true, tab appears only in the "Mais" overflow dropdown */
    secondary?: boolean;
    /** If true, restricted to System Administrator / HR roles */
    diagnosticOnly?: boolean;
}

const ALL_HR_TABS: HRTab[] = [
    // Primary tabs — always visible in the main bar
    { id: 'overview',    label: 'Visão Geral',              path: '/hr/overview',    icon: Activity },
    { id: 'leave',       label: 'Férias e Ausências',       path: '/hr/leave',       icon: Calendar },
    { id: 'calendar',    label: 'Calendário da Equipa',     path: '/hr/calendar',    icon: CalendarDays },
    { id: 'attendance',  label: 'Presenças',                path: '/hr/attendance',  icon: Clock },

    // Secondary tabs — collapsed into "Mais" dropdown
    { id: 'schedules',   label: 'Escalas & Horários',       path: '/hr/schedules',   icon: CalendarRange,  secondary: true },
    { id: 'directory',   label: 'Directório & Mapeamento',  path: '/hr/directory',   icon: Users,          secondary: true },
    { id: 'badges',      label: 'Gestão de Crachás',        path: '/hr/badges',      icon: CreditCard,     secondary: true },

    // Diagnostic — secondary + restricted
    { id: 'attendance-review', label: 'Revisão de Presenças', path: '/hr/attendance-review', icon: ShieldCheck, secondary: true, diagnosticOnly: true },
];

/**
 * Tabs visible to Viewer / Management users.
 * These users access the HR module to see their own overview,
 * calendar, and leave information — other HR features are not
 * relevant to them.
 */
const VIEWER_ONLY_TAB_IDS = ['overview', 'calendar', 'leave'];

/**
 * HRLandingPage — Unified HR workspace with tabbed sub-navigation.
 * Mirrors the FinanceLandingPage pattern: PageHeader + NavLinks + <Outlet />.
 *
 * Navigation layout:
 *   - Primary tabs displayed directly in the tab bar (max 4).
 *   - Secondary tabs collapsed into a "Mais" dropdown.
 *   - The "Mais" button appears active when the current route matches a secondary tab.
 *
 * Tab visibility by role:
 *   - System Administrator / HR: all tabs including diagnostic.
 *   - Local Manager / Department Manager: all core tabs (no diagnostic).
 *   - Viewer / Management: overview, calendar, leave only (no "Mais").
 */
export default function HRLandingPage() {
    const location = useLocation();
    const { hasHRAdminAccess } = useAuth();
    const [moreOpen, setMoreOpen] = useState(false);
    const moreRef = useRef<HTMLDivElement>(null);

    // ─── Role checks ───
    // hasHRAdminAccess: HR or System Administrator → sees full module
    // Otherwise: team-level user → sees only overview, calendar, leave
    const isTeamOnlyUser = !hasHRAdminAccess;

    // ─── Build visible tabs ───
    const visibleTabs = ALL_HR_TABS.filter(tab => {
        // Diagnostic tab: admin/HR only
        if (tab.diagnosticOnly && !hasHRAdminAccess) return false;
        // Team-only users: restricted to overview, calendar, leave
        if (isTeamOnlyUser && !VIEWER_ONLY_TAB_IDS.includes(tab.id)) return false;
        return true;
    });

    const primaryTabs = visibleTabs.filter(t => !t.secondary);
    const secondaryTabs = visibleTabs.filter(t => t.secondary);
    const hasSecondaryTabs = secondaryTabs.length > 0;

    // ─── "Mais" active detection ───
    const isSecondaryActive = secondaryTabs.some(t => location.pathname.startsWith(t.path));

    // ─── Close dropdown on outside click / Escape ───
    useEffect(() => {
        if (!moreOpen) return;

        const handleClickOutside = (e: MouseEvent) => {
            if (moreRef.current && !moreRef.current.contains(e.target as Node)) {
                setMoreOpen(false);
            }
        };
        const handleEscape = (e: KeyboardEvent) => {
            if (e.key === 'Escape') setMoreOpen(false);
        };

        document.addEventListener('mousedown', handleClickOutside);
        document.addEventListener('keydown', handleEscape);
        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
            document.removeEventListener('keydown', handleEscape);
        };
    }, [moreOpen]);

    // ─── Tab style — matches FinanceLandingPage exactly ───
    const navStyle = ({ isActive }: { isActive: boolean }): React.CSSProperties => ({
        display: 'flex', alignItems: 'center', gap: '8px',
        padding: '12px 24px', fontWeight: 700, fontSize: '0.9rem',
        backgroundColor: isActive ? 'var(--color-bg-page)' : 'transparent',
        color: isActive ? 'var(--color-primary)' : 'var(--color-text-muted)',
        borderBottom: isActive ? '2px solid var(--color-primary)' : '2px solid transparent',
        textDecoration: 'none', transition: 'all 0.2s',
        borderRadius: 'var(--radius-lg) var(--radius-lg) 0 0',
        whiteSpace: 'nowrap',
    });

    // ─── "Mais" button style ───
    const moreButtonStyle: React.CSSProperties = {
        display: 'flex', alignItems: 'center', gap: '8px',
        padding: '12px 24px', fontWeight: 700, fontSize: '0.9rem',
        backgroundColor: isSecondaryActive ? 'var(--color-bg-page)' : 'transparent',
        color: isSecondaryActive ? 'var(--color-primary)' : 'var(--color-text-muted)',
        borderBottom: isSecondaryActive ? '2px solid var(--color-primary)' : '2px solid transparent',
        textDecoration: 'none', transition: 'all 0.2s',
        borderRadius: 'var(--radius-lg) var(--radius-lg) 0 0',
        cursor: 'pointer', border: 'none',
        whiteSpace: 'nowrap',
    };

    // ─── Role-aware titles ───
    const pageTitle = hasHRAdminAccess ? 'Recursos Humanos' : 'Gestão da Equipa';
    const pageSubtitle = hasHRAdminAccess
        ? 'Gestão de funcionários, férias, ausências e calendário da equipa'
        : 'Calendário da equipa, férias e ausências';
    const pageIcon = hasHRAdminAccess
        ? <Users size={24} strokeWidth={2.5} />
        : <CalendarDays size={24} strokeWidth={2.5} />;

    return (
        <PageContainer>
            <PageHeader
                title={pageTitle}
                subtitle={pageSubtitle}
                icon={pageIcon}
            />

            {/* Tab navigation — Finance-style bottom-border pattern */}
            <div style={{ display: 'flex', gap: '4px', borderBottom: '1px solid var(--color-border)', paddingBottom: '0' }}>
                {primaryTabs.map(tab => {
                    const Icon = tab.icon;
                    return (
                        <NavLink key={tab.id} to={tab.path} style={navStyle}>
                            <Icon size={18} /> {tab.label}
                        </NavLink>
                    );
                })}

                {/* "Mais" dropdown trigger */}
                {hasSecondaryTabs && (
                    <div className="hr-more-container" ref={moreRef}>
                        <button
                            type="button"
                            style={moreButtonStyle}
                            onClick={() => setMoreOpen(prev => !prev)}
                            aria-expanded={moreOpen}
                            aria-haspopup="true"
                        >
                            Mais <ChevronDown size={16} style={{
                                transition: 'transform 0.2s',
                                transform: moreOpen ? 'rotate(180deg)' : 'rotate(0deg)'
                            }} />
                        </button>

                        {moreOpen && (
                            <div className="hr-more-dropdown">
                                {secondaryTabs.map(tab => {
                                    const Icon = tab.icon;
                                    const isActive = location.pathname.startsWith(tab.path);
                                    return (
                                        <NavLink
                                            key={tab.id}
                                            to={tab.path}
                                            className={`hr-more-item ${isActive ? 'active' : ''}`}
                                            onClick={() => setMoreOpen(false)}
                                        >
                                            <Icon size={16} />
                                            <span>{tab.label}</span>
                                        </NavLink>
                                    );
                                })}
                            </div>
                        )}
                    </div>
                )}
            </div>

            {/* Routed sub-page content */}
            <Outlet />
        </PageContainer>
    );
}
