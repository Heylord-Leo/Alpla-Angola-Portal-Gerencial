import { motion } from 'framer-motion';
import { useNavigate } from 'react-router-dom';
import { PlusCircle, LayoutList, Package, CheckSquare, CreditCard, Truck } from 'lucide-react';
import { useAuth } from '../../../features/auth/AuthContext';
import { ROLES } from '../../../constants/roles';
import { SectionInfo } from '../../../components/ui/SectionInfo';
import { DASHBOARD_SECTION_HELP } from '../dashboardSectionHelp';

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
                padding: '1rem',
                display: 'flex',
                alignItems: 'center',
                gap: '1rem',
                textAlign: 'left',
                cursor: 'pointer',
                width: '100%',
                position: 'relative',
                overflow: 'hidden'
            }}
        >
            <div style={{ 
                backgroundColor: `${color}15`, 
                padding: '0.6rem', 
                color: color,
                display: 'flex',
                borderRadius: '4px'
            }}>
                {icon}
            </div>
            <div style={{ flex: 1 }}>
                <div style={{ 
                    fontSize: '0.85rem', 
                    fontWeight: 700, 
                    color: 'var(--color-text-main)',
                    letterSpacing: '-0.01em',
                    lineHeight: 1.2
                }}>
                    {label}
                </div>
                <div style={{ 
                    fontSize: '0.7rem', 
                    color: 'var(--color-text-muted)',
                    fontWeight: 500,
                    marginTop: '2px'
                }}>
                    {description}
                </div>
            </div>
        </motion.button>
    );
}

export function QuickActions() {
    const navigate = useNavigate();
    const { user } = useAuth();

    const hasRole = (role: string) => user?.roles?.includes(role) || user?.roles?.includes(ROLES.SYSTEM_ADMINISTRATOR);

    const allActions = [
        {
            label: 'Novo Pedido',
            description: 'Criar uma nova solicitação',
            icon: <PlusCircle size={20} />,
            color: 'var(--color-primary)',
            onClick: () => navigate('/requests/new'),
            visible: true
        },
        {
            label: 'Ver Pedidos',
            description: 'Lista de pedidos',
            icon: <LayoutList size={20} />,
            color: 'var(--color-status-blue)',
            onClick: () => navigate('/requests'),
            visible: true
        },
        {
            label: 'Gestão de Cotações',
            description: 'Cotações e fornecedores',
            icon: <Package size={20} />,
            color: 'var(--color-status-indigo)',
            onClick: () => navigate('/buyer/items'),
            visible: hasRole(ROLES.BUYER)
        },
        {
            label: 'Centro de Aprovações',
            description: 'Aprovar pedidos pendentes',
            icon: <CheckSquare size={20} />,
            color: '#10b981',
            onClick: () => navigate('/approvals'),
            visible: hasRole(ROLES.AREA_APPROVER) || hasRole(ROLES.FINAL_APPROVER)
        },
        {
            label: 'Pagamentos',
            description: 'Gestão financeira',
            icon: <CreditCard size={20} />,
            color: '#f97316',
            onClick: () => navigate('/finance'),
            visible: hasRole(ROLES.FINANCE)
        },
        {
            label: 'Recebimentos',
            description: 'Conferir entregas',
            icon: <Truck size={20} />,
            color: '#8b5cf6',
            onClick: () => navigate('/receiving/workspace'),
            visible: hasRole(ROLES.RECEIVING)
        }
    ];

    const actions = allActions.filter(a => a.visible);

    return (
        <section>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, margin: '0 0 12px 0' }}>
                <h2 style={{
                    fontSize: '1.1rem',
                    fontWeight: 700,
                    color: 'var(--color-text-main)',
                    margin: 0
                }}>
                    Ações Rápidas
                </h2>
                <SectionInfo {...DASHBOARD_SECTION_HELP.quickActions} />
            </div>
            <div style={{ 
                display: 'grid', 
                gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', 
                gap: '10px'
            }}>
                {actions.map((action, index) => (
                    <QuickActionItem key={index} {...action} />
                ))}
            </div>
        </section>
    );
}
