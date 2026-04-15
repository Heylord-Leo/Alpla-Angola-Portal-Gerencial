import React from 'react';
import { useNavigate } from 'react-router-dom';
import { Plus, ClipboardList, Search, Truck } from 'lucide-react';
import { useAuth } from '../../../features/auth/AuthContext';
import { ROLES } from '../../../constants/roles';

export function QuickActions() {
    const navigate = useNavigate();
    const { user } = useAuth();

    const actions = [
        {
            label: 'Novo Pedido',
            icon: <Plus />,
            path: '/requests/new',
            description: 'Criar pedido de compra ou pagamento'
        },
        {
            label: 'Meus Pedidos',
            icon: <ClipboardList />,
            path: '/requests',
            description: 'Acompanhar seus pedidos ativos'
        },
        {
            label: 'Gestão de Cotações',
            icon: <Search />,
            path: '/buyer/items',
            description: 'Registrar e comparar proformas'
        },
        {
            label: 'Recebimento',
            icon: <Truck />,
            path: '/receiving/workspace',
            description: 'Entrada de mercadorias em armazém'
        }
    ].filter(action => {
        if (action.label === 'Gestão de Cotações') {
            return user?.roles?.includes(ROLES.BUYER) || user?.roles?.includes(ROLES.SYSTEM_ADMINISTRATOR);
        }
        if (action.label === 'Recebimento') {
            return user?.roles?.includes(ROLES.RECEIVING) || user?.roles?.includes(ROLES.SYSTEM_ADMINISTRATOR);
        }
        return true;
    });

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
            <h2 style={{ 
                margin: 0, 
                fontSize: '1.2rem', 
                fontWeight: 800, 
                textTransform: 'uppercase', 
                letterSpacing: '0.05em',
                color: 'var(--color-primary)',
                borderLeft: '4px solid var(--color-primary)',
                paddingLeft: '12px'
            }}>
                Ações Rápidas
            </h2>
            <div style={{ 
                display: 'grid', 
                gridTemplateColumns: '1fr', 
                gap: '12px'
            }}>
                {actions.map(action => (
                    <button
                        key={action.path}
                        onClick={() => navigate(action.path)}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '16px',
                            padding: '16px',
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '2px solid var(--color-border)',
                            cursor: 'pointer',
                            textAlign: 'left',
                            transition: 'all 0.1s ease',
                            boxShadow: '4px 4px 0px var(--color-border)',
                        }}
                        onMouseOver={(e) => {
                            e.currentTarget.style.borderColor = 'var(--color-primary)';
                            e.currentTarget.style.transform = 'translate(-2px, -2px)';
                            e.currentTarget.style.boxShadow = '6px 6px 0px var(--color-primary)';
                        }}
                        onMouseOut={(e) => {
                            e.currentTarget.style.borderColor = 'var(--color-border)';
                            e.currentTarget.style.transform = 'translate(0, 0)';
                            e.currentTarget.style.boxShadow = '4px 4px 0px var(--color-border)';
                        }}
                    >
                        <div style={{ 
                            backgroundColor: 'rgba(var(--color-primary-rgb), 0.1)', 
                            padding: '10px', 
                            display: 'flex',
                            color: 'var(--color-primary)'
                        }}>
                            {React.cloneElement(action.icon as React.ReactElement, { size: 24, strokeWidth: 2.5 })}
                        </div>
                        <div>
                            <div style={{ fontWeight: 800, fontSize: '0.9rem', textTransform: 'uppercase', color: 'var(--color-primary)' }}>
                                {action.label}
                            </div>
                            <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: '2px' }}>
                                {action.description}
                            </div>
                        </div>
                    </button>
                ))}
            </div>
        </div>
    );
}
