import React from 'react';
import { Outlet, NavLink } from 'react-router-dom';
import { Activity, DollarSign, Archive, BarChart2 } from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';

export default function FinanceLandingPage() {
    const navStyle = ({ isActive }: { isActive: boolean }): React.CSSProperties => ({
        display: 'flex', alignItems: 'center', gap: '8px',
        padding: '12px 24px', fontWeight: 700, fontSize: '0.9rem',
        backgroundColor: isActive ? 'var(--color-bg-page)' : 'transparent',
        color: isActive ? 'var(--color-primary)' : 'var(--color-text-muted)',
        borderBottom: isActive ? '2px solid var(--color-primary)' : '2px solid transparent',
        textDecoration: 'none', transition: 'all 0.2s',
        borderRadius: 'var(--radius-lg) var(--radius-lg) 0 0',
    });

    return (
        <PageContainer>
            <PageHeader
                title="Finanças"
                subtitle="Gestão de Tesouraria e Pagamentos"
            />

            <div style={{ display: 'flex', gap: '4px', borderBottom: '1px solid var(--color-border)', paddingBottom: '0' }}>
                <NavLink to="/finance/overview" style={navStyle}>
                    <Activity size={18} /> Resumo Operacional
                </NavLink>
                <NavLink to="/finance/payments" style={navStyle}>
                    <DollarSign size={18} /> Pagamentos
                </NavLink>
                <NavLink to="/finance/history" style={navStyle}>
                    <Archive size={18} /> Histórico
                </NavLink>
                <NavLink to="/finance/budget" style={navStyle}>
                    <BarChart2 size={18} /> Orçamento
                </NavLink>
            </div>

            <Outlet />
        </PageContainer>
    );
}
