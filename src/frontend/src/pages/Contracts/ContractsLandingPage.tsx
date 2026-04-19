import { Outlet, NavLink } from 'react-router-dom';
import { List, Bell } from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';

export default function ContractsLandingPage() {
    return (
        <PageContainer>
            <PageHeader 
                title="Contratos"
                subtitle="Gestão de Contratos e Obrigações de Pagamento"
            />

            <div style={{ display: 'flex', gap: '16px', borderBottom: '1px solid var(--color-border)', paddingBottom: '16px' }}>
                <NavLink
                    to="/contracts/list"
                    style={({ isActive }) => ({
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 24px', fontWeight: 700, fontSize: '0.9rem',
                        backgroundColor: isActive ? 'var(--color-bg-page)' : 'transparent',
                        color: isActive ? 'var(--color-primary)' : 'var(--color-text-muted)',
                        borderBottom: isActive ? '2px solid var(--color-primary)' : '2px solid transparent',
                        textDecoration: 'none', transition: 'all 0.2s',
                        borderRadius: 'var(--radius-lg) var(--radius-lg) 0 0'
                    })}
                >
                    <List size={18} /> Contratos
                </NavLink>
                <NavLink
                    to="/contracts/alerts"
                    style={({ isActive }) => ({
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 24px', fontWeight: 700, fontSize: '0.9rem',
                        backgroundColor: isActive ? 'var(--color-bg-page)' : 'transparent',
                        color: isActive ? 'var(--color-primary)' : 'var(--color-text-muted)',
                        borderBottom: isActive ? '2px solid var(--color-primary)' : '2px solid transparent',
                        textDecoration: 'none', transition: 'all 0.2s',
                        borderRadius: 'var(--radius-lg) var(--radius-lg) 0 0'
                    })}
                >
                    <Bell size={18} /> Alertas
                </NavLink>
            </div>
            
            <Outlet />
        </PageContainer>
    );
}
