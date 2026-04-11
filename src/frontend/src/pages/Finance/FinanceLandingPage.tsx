import { Outlet, NavLink } from 'react-router-dom';
import { CreditCard, Activity, DollarSign, Archive } from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';

export default function FinanceLandingPage() {
    return (
        <PageContainer>
            <PageHeader 
                title="Finanças"
                subtitle="Gestão de Tesouraria e Pagamentos"
            />

            <div style={{ display: 'flex', gap: '16px', borderBottom: '1px solid var(--color-border)', paddingBottom: '16px' }}>
                <NavLink
                    to="/finance/overview"
                    style={({ isActive }) => ({
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 24px', fontWeight: 700, fontSize: '0.9rem',
                        backgroundColor: isActive ? 'var(--color-bg-page)' : 'transparent',
                        color: isActive ? 'var(--color-primary)' : 'var(--color-text-muted)',
                        borderBottom: isActive ? '2px solid var(--color-primary)' : '2px solid transparent',
                        textDecoration: 'none', transition: 'all 0.2s',
                        borderRadius: 'var(--radius-lg) var(--radius-lg) 0 0'
                    })}
                >
                    <Activity size={18} /> Resumo Operacional
                </NavLink>
                <NavLink
                    to="/finance/payments"
                    style={({ isActive }) => ({
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 24px', fontWeight: 700, fontSize: '0.9rem',
                        backgroundColor: isActive ? 'var(--color-bg-page)' : 'transparent',
                        color: isActive ? 'var(--color-primary)' : 'var(--color-text-muted)',
                        borderBottom: isActive ? '2px solid var(--color-primary)' : '2px solid transparent',
                        textDecoration: 'none', transition: 'all 0.2s',
                        borderRadius: 'var(--radius-lg) var(--radius-lg) 0 0'
                    })}
                >
                    <DollarSign size={18} /> Pagamentos
                </NavLink>
                <NavLink
                    to="/finance/history"
                    style={({ isActive }) => ({
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 24px', fontWeight: 700, fontSize: '0.9rem',
                        backgroundColor: isActive ? 'var(--color-bg-page)' : 'transparent',
                        color: isActive ? 'var(--color-primary)' : 'var(--color-text-muted)',
                        borderBottom: isActive ? '2px solid var(--color-primary)' : '2px solid transparent',
                        textDecoration: 'none', transition: 'all 0.2s',
                        borderRadius: 'var(--radius-lg) var(--radius-lg) 0 0'
                    })}
                >
                    <Archive size={18} /> Histórico
                </NavLink>
            </div>
            
            <Outlet />
        </PageContainer>
    );
}
