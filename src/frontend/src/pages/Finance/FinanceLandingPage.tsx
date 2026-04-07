import { Outlet, NavLink } from 'react-router-dom';
import { CreditCard, Activity, DollarSign, Archive } from 'lucide-react';

export default function FinanceLandingPage() {
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px', width: '100%', minWidth: 0 }}>
            <div style={{ 
                display: 'flex', 
                justifyContent: 'space-between', 
                alignItems: 'center', 
                borderBottom: '4px solid var(--color-primary)', 
                paddingBottom: '24px', 
                width: '100%' 
            }}>
                <div>
                    <h1 style={{ margin: 0, fontSize: '2.8rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                        Finanças
                    </h1>
                    <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 700, letterSpacing: '0.05em', textTransform: 'uppercase', fontSize: '0.9rem' }}>
                        Gestão de Tesouraria e Pagamentos
                    </p>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', opacity: 0.6 }}>
                    <CreditCard size={32} strokeWidth={2.5} />
                </div>
            </div>

            <div style={{ display: 'flex', gap: '16px', borderBottom: '2px solid var(--color-border)', paddingBottom: '16px' }}>
                <NavLink
                    to="/finance/overview"
                    style={({ isActive }) => ({
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 24px', fontWeight: 800, textTransform: 'uppercase', fontSize: '0.9rem',
                        backgroundColor: isActive ? 'var(--color-primary)' : 'var(--color-bg-surface)',
                        color: isActive ? '#fff' : 'var(--color-text)',
                        border: '2px solid var(--color-border)',
                        boxShadow: isActive ? '4px 4px 0 var(--color-text)' : '4px 4px 0 transparent',
                        textDecoration: 'none', transition: 'all 0.2s'
                    })}
                >
                    <Activity size={18} /> Resumo Operacional
                </NavLink>
                <NavLink
                    to="/finance/payments"
                    style={({ isActive }) => ({
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 24px', fontWeight: 800, textTransform: 'uppercase', fontSize: '0.9rem',
                        backgroundColor: isActive ? 'var(--color-primary)' : 'var(--color-bg-surface)',
                        color: isActive ? '#fff' : 'var(--color-text)',
                        border: '2px solid var(--color-border)',
                        boxShadow: isActive ? '4px 4px 0 var(--color-text)' : '4px 4px 0 transparent',
                        textDecoration: 'none', transition: 'all 0.2s'
                    })}
                >
                    <DollarSign size={18} /> Pagamentos
                </NavLink>
                <NavLink
                    to="/finance/history"
                    style={({ isActive }) => ({
                        display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 24px', fontWeight: 800, textTransform: 'uppercase', fontSize: '0.9rem',
                        backgroundColor: isActive ? 'var(--color-primary)' : 'var(--color-bg-surface)',
                        color: isActive ? '#fff' : 'var(--color-text)',
                        border: '2px solid var(--color-border)',
                        boxShadow: isActive ? '4px 4px 0 var(--color-text)' : '4px 4px 0 transparent',
                        textDecoration: 'none', transition: 'all 0.2s'
                    })}
                >
                    <Archive size={18} /> Histórico
                </NavLink>
            </div>
            
            <Outlet />
        </div>
    );
}
