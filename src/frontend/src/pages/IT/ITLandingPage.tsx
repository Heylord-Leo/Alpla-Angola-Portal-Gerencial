import React from 'react';
import { Outlet, NavLink } from 'react-router-dom';
import { Monitor, ClipboardList, Settings, Wrench } from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';

export default function ITLandingPage() {
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
                title="T.I."
                subtitle="Gestão de equipamentos, termos e configurações de tecnologia da informação"
                icon={<Monitor size={24} />}
            />

            <div data-tour="it-module-tabs" style={{ display: 'flex', gap: '4px', borderBottom: '1px solid var(--color-border)', paddingBottom: '0' }}>
                <NavLink to="/it/equipment" style={navStyle}>
                    <Monitor size={18} /> Estoque de Equipamentos
                </NavLink>
                <NavLink to="/it/delivery-terms" style={navStyle}>
                    <ClipboardList size={18} /> Termos de Entrega
                </NavLink>
                <NavLink to="/it/catalogs" style={navStyle}>
                    <Settings size={18} /> Catálogos
                </NavLink>
                <NavLink to="/it/types" style={navStyle}>
                    <Wrench size={18} /> Tipos de Equipamento
                </NavLink>
            </div>

            <Outlet />
        </PageContainer>
    );
}
