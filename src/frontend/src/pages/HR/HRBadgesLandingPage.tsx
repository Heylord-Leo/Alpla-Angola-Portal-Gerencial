import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { motion } from 'framer-motion';
import { UserCheck, Layers, History } from 'lucide-react';
import './hr-badges-landing.css';

const BADGE_TABS = [
    { id: 'employees', label: 'Funcionários', path: '/hr/badges/employees', icon: UserCheck },
    { id: 'layouts', label: 'Layouts', path: '/hr/badges/layouts', icon: Layers },
    { id: 'history', label: 'Histórico de Impressão', path: '/hr/badges/history', icon: History },
];

/**
 * HRBadgesLandingPage — Sub-navigation shell for the Badge Management area.
 *
 * Renders 3 tabs (Funcionários, Layouts, Histórico) with animated indicator
 * and an <Outlet /> for the active sub-page.
 *
 * Architecture note: This follows the same tabbed navigation pattern as
 * FinanceLandingPage for visual consistency across the Portal.
 */
export default function HRBadgesLandingPage() {
    const location = useLocation();

    return (
        <div className="hr-badges-shell">
            {/* Sub-section tabs */}
            <nav className="hr-badges-tab-nav">
                {BADGE_TABS.map(tab => {
                    const Icon = tab.icon;
                    const isActive = location.pathname.startsWith(tab.path);
                    return (
                        <NavLink
                            key={tab.id}
                            to={tab.path}
                            className={`hr-badges-tab-link ${isActive ? 'active' : ''}`}
                        >
                            <Icon size={15} />
                            <span>{tab.label}</span>
                            {isActive && (
                                <motion.div
                                    className="hr-badges-tab-indicator"
                                    layoutId="hr-badges-tab-indicator"
                                    transition={{ type: 'spring', stiffness: 380, damping: 30 }}
                                />
                            )}
                        </NavLink>
                    );
                })}
            </nav>

            {/* Routed sub-page content */}
            <div className="hr-badges-content">
                <Outlet />
            </div>
        </div>
    );
}
