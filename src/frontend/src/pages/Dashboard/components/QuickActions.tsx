import { motion } from 'framer-motion';
import { useNavigate } from 'react-router-dom';
import { PlusCircle, LayoutList, Package } from 'lucide-react';
import { useAuth } from '../../../features/auth/AuthContext';
import { ROLES } from '../../../constants/roles';

interface QuickActionItemProps {
    label: string;
    icon: React.ReactNode;
    description: string;
    onClick: () => void;
    color?: string;
}

function QuickActionItem({ label, icon, description, onClick, color = 'var(--color-primary)' }: QuickActionItemProps) {
    return (
        <motion.button
            whileHover={{ y: -2, boxShadow: 'var(--shadow-md)', transition: { duration: 0.1 } }}
            whileTap={{ scale: 0.98 }}
            onClick={onClick}
            style={{
                backgroundColor: 'var(--color-bg-surface)',
                border: '1px solid var(--color-border)',
                boxShadow: 'var(--shadow-sm)',
                borderRadius: 'var(--radius-lg)',
                padding: '1.25rem',
                display: 'flex',
                alignItems: 'center',
                gap: '1.25rem',
                textAlign: 'left',
                cursor: 'pointer',
                width: '100%',
                position: 'relative',
                overflow: 'hidden'
            }}
        >
            <div style={{ 
                backgroundColor: `${color}15`, 
                padding: '0.75rem', 
                color: color,
                display: 'flex',
                borderRadius: '4px'
            }}>
                {icon}
            </div>
            <div style={{ flex: 1 }}>
                <div style={{ 
                    fontSize: '1rem', 
                    fontWeight: 900, 
                    textTransform: 'uppercase', 
                    color: 'var(--color-text-main)',
                    letterSpacing: '-0.01em',
                    lineHeight: 1.2
                }}>
                    {label}
                </div>
                <div style={{ 
                    fontSize: '0.75rem', 
                    color: 'var(--color-text-muted)',
                    fontWeight: 500,
                    marginTop: '2px'
                }}>
                    {description}
                </div>
            </div>
            
            {/* Removed corner clip-path for premium clean look */}
        </motion.button>
    );
}

export function QuickActions() {
    const navigate = useNavigate();
    const { user } = useAuth();

    const actions = [
        {
            label: 'Novo Pedido',
            description: 'Criar uma nova solicitação',
            icon: <PlusCircle size={24} />,
            color: 'var(--color-primary)',
            onClick: () => navigate('/requests/create')
        },
        {
            label: 'Ver Pedidos',
            description: 'Acompanhar lista de pedidos',
            icon: <LayoutList size={24} />,
            color: 'var(--color-status-blue)',
            onClick: () => navigate('/requests')
        },
        {
            label: 'Gestão de Cotações',
            description: 'Gerencie itens solicitados e processe cotações de fornecedores',
            icon: <Package size={24} />,
            color: 'var(--color-status-indigo)',
            onClick: () => navigate('/buyer/items')
        }
    ].filter(action => {
        if (action.label === 'Gestão de Cotações') {
            return user?.roles?.includes(ROLES.BUYER) || user?.roles?.includes(ROLES.SYSTEM_ADMINISTRATOR);
        }
        return true;
    });

    return (
        <section style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <header>
                <h2 style={{ 
                    margin: 0, 
                    fontSize: '1.25rem', 
                    fontWeight: 900, 
                    textTransform: 'uppercase',
                    borderLeft: '4px solid var(--color-primary)',
                    paddingLeft: '1rem',
                    letterSpacing: '-0.01em'
                }}>
                    Ações Rápidas
                </h2>
                <p style={{ margin: '0.25rem 0 0 1.25rem', color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
                    Atalhos para as operações mais comuns do dia a dia
                </p>
            </header>

            <div style={{ 
                display: 'grid', 
                gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', 
                gap: '1rem',
                marginTop: '0.5rem'
            }}>
                {actions.map((action, index) => (
                    <QuickActionItem key={index} {...action} />
                ))}
            </div>
        </section>
    );
}
