import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { motion } from 'framer-motion';
import {
    Activity, Calendar, CalendarDays, Clock, Users, CreditCard, CalendarRange
} from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import './hr-landing.css';

const HR_TABS = [
    { id: 'overview', label: 'Visão Geral', path: '/hr/overview', icon: Activity },
    { id: 'leave', label: 'Férias e Ausências', path: '/hr/leave', icon: Calendar },
    { id: 'calendar', label: 'Calendário da Equipa', path: '/hr/calendar', icon: CalendarDays },
    { id: 'attendance', label: 'Presenças', path: '/hr/attendance', icon: Clock },
    { id: 'schedules', label: 'Escalas & Horários', path: '/hr/schedules', icon: CalendarRange },
    { id: 'directory', label: 'Directório & Mapeamento', path: '/hr/directory', icon: Users },
    { id: 'badges', label: 'Gestão de Crachás', path: '/hr/badges', icon: CreditCard },
];

/**
 * HRLandingPage — Unified HR workspace with tabbed sub-navigation.
 * Mirrors the FinanceLandingPage pattern: PageHeader + NavLinks + <Outlet />.
 */
export default function HRLandingPage() {
    const location = useLocation();

    return (
        <PageContainer>
            <PageHeader
                title="Recursos Humanos"
                subtitle="Gestão de funcionários, férias, ausências e calendário da equipa"
                icon={<Users size={24} strokeWidth={2.5} />}
            />

            {/* Sub-navigation tabs */}
            <nav className="hr-tab-nav">
                {HR_TABS.map(tab => {
                    const Icon = tab.icon;
                    const isActive = location.pathname.startsWith(tab.path);
                    return (
                        <NavLink
                            key={tab.id}
                            to={tab.path}
                            className={`hr-tab-link ${isActive ? 'active' : ''}`}
                        >
                            <Icon size={16} />
                            <span>{tab.label}</span>
                            {isActive && (
                                <motion.div
                                    className="hr-tab-indicator"
                                    layoutId="hr-tab-indicator"
                                    transition={{ type: 'spring', stiffness: 380, damping: 30 }}
                                />
                            )}
                        </NavLink>
                    );
                })}
            </nav>

            {/* Routed sub-page content */}
            <div className="hr-tab-content">
                <Outlet />
            </div>
        </PageContainer>
    );
}
