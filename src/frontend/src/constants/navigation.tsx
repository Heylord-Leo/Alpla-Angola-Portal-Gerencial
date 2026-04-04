import React from 'react';
import { 
    FileText, Home, Settings, List, ShoppingCart, 
    Package, Activity, Network, Shield, CheckCircle 
} from 'lucide-react';
import { ROLES } from './roles';

export type NavItemType = 'link' | 'group' | 'action';

export interface NavItem {
    id: string;
    type: NavItemType;
    label: string;
    icon: React.ReactNode;
    to?: string;
    onClick?: () => void;
    children?: NavItem[];
    keywords?: string[];
    roles?: string[];      // New: explicit allowed roles
    isAdminOnly?: boolean;  // Legacy
    isManagerOnly?: boolean; // Legacy
}

/**
 * Single source of truth for all system modules and navigation.
 * All new modules must be registered here to automatically appear
 * in both the Sidebar and the Global Search.
 */
export const getNavigationConfig = (userRoles: string[]): NavItem[] => {
    const isAdmin = userRoles.includes(ROLES.SYSTEM_ADMINISTRATOR);
    const isLocalManager = userRoles.includes(ROLES.LOCAL_MANAGER);

    const config: NavItem[] = [
        {
            id: 'dashboard',
            type: 'link',
            label: 'Dashboard',
            icon: <Home size={18} strokeWidth={2.5} />,
            to: '/dashboard',
            keywords: ['resumo', 'gráficos', 'geral', 'estatísticas', 'home', 'início']
        },
        {
            id: 'approvals',
            type: 'link',
            label: 'Centro de Aprovações',
            icon: <CheckCircle size={18} strokeWidth={2.5} />,
            to: '/approvals',
            roles: [ROLES.AREA_APPROVER, ROLES.FINAL_APPROVER, ROLES.SYSTEM_ADMINISTRATOR],
            keywords: ['decisão', 'aprovar', 'rejeitar', 'pendente', 'aprovações', 'workflow', 'centro']
        },
        {
            id: 'administracao',
            type: 'group',
            label: 'Administração',
            icon: <Shield size={18} strokeWidth={2.5} />,
            roles: [ROLES.SYSTEM_ADMINISTRATOR, ROLES.LOCAL_MANAGER],
            children: [
                {
                    id: 'admin-workspace',
                    type: 'link',
                    label: 'Workspace do Administrador',
                    icon: <Shield size={18} strokeWidth={2.5} />,
                    to: '/admin/workspace',
                    roles: [ROLES.SYSTEM_ADMINISTRATOR],
                    keywords: ['admin', 'técnico', 'gerenciamento', 'workspace', 'administrador']
                },
                {
                    id: 'admin-users',
                    type: 'link',
                    label: 'Gestão de Usuários',
                    icon: <Shield size={18} strokeWidth={2.5} />,
                    to: '/admin/users',
                    roles: [ROLES.SYSTEM_ADMINISTRATOR, ROLES.LOCAL_MANAGER],
                    keywords: ['usuarios', 'perfil', 'acesso', 'permissoes', 'admin', 'senhas']
                },
                {
                    id: 'admin-logs',
                    type: 'link',
                    label: 'Logs do Sistema',
                    icon: <FileText size={18} strokeWidth={2.5} />,
                    to: '/admin/logs',
                    roles: [ROLES.SYSTEM_ADMINISTRATOR],
                    keywords: ['admin', 'técnico', 'eventos', 'histórico', 'erros', 'rastreamento']
                },
                {
                    id: 'admin-diagnosis',
                    type: 'link',
                    label: 'Diagnóstico de Serviços',
                    icon: <Activity size={18} strokeWidth={2.5} />,
                    to: '/admin/diagnosis',
                    roles: [ROLES.SYSTEM_ADMINISTRATOR],
                    keywords: ['admin', 'técnico', 'sistema', 'status', 'saúde', 'serviços', 'infra']
                },
                {
                    id: 'admin-health',
                    type: 'link',
                    label: 'Saúde das Integrações',
                    icon: <Network size={18} strokeWidth={2.5} />,
                    to: '/admin/health',
                    roles: [ROLES.SYSTEM_ADMINISTRATOR],
                    keywords: ['admin', 'técnico', 'dados', 'sincronizacao', 'primavera', 'erp', 'integridade']
                }
            ]
        },
        {
            id: 'compras-logistica',
            type: 'group',
            label: 'Compras & Logística',
            to: '/purchasing',
            icon: <ShoppingCart size={18} strokeWidth={2.5} />,
            keywords: ['compras', 'logística', 'suprimentos', 'alpla', 'módulos'],
            children: [
                {
                    id: 'pedidos',
                    type: 'link',
                    label: 'Pedidos',
                    icon: <FileText size={18} strokeWidth={2.5} />,
                    to: '/requests',
                    keywords: ['compras', 'lista', 'solicitações', 'pedidos', 'histórico', 'novo pedido']
                },
                {
                    id: 'itens-pedido',
                    type: 'link',
                    label: 'Gestão de Cotações',
                    icon: <List size={18} strokeWidth={2.5} />,
                    to: '/buyer/items',
                    roles: [ROLES.BUYER, ROLES.SYSTEM_ADMINISTRATOR],
                    keywords: ['compras', 'cotações', 'itens', 'comprador', 'vendedor', 'proposta', 'comparativo']
                },
                {
                    id: 'recebimento',
                    type: 'link',
                    label: 'Recebimento',
                    icon: <Package size={18} strokeWidth={2.5} />,
                    to: '/receiving/workspace',
                    roles: [ROLES.RECEIVING, ROLES.LOCAL_MANAGER, ROLES.SYSTEM_ADMINISTRATOR],
                    keywords: ['entrega', 'materiais', 'armazém', 'logística', 'conferência', 'entrada', 'estoque']
                }
            ]
        },
        {
            id: 'configuracoes',
            type: 'group',
            label: 'Configurações',
            icon: <Settings size={18} strokeWidth={2.5} />,
            roles: [ROLES.SYSTEM_ADMINISTRATOR],
            children: [
                {
                    id: 'dados-mestres',
                    type: 'link',
                    label: 'Dados Mestres',
                    icon: <List size={18} strokeWidth={2.5} />,
                    to: '/settings/master-data',
                    keywords: ['configuração', 'fornecedores', 'materiais', 'mestre', 'tabelas', 'unidades']
                },
                {
                    id: 'extracao-documentos',
                    type: 'link',
                    label: 'Extração de Documentos',
                    icon: <FileText size={18} strokeWidth={2.5} />,
                    to: '/settings/document-extraction',
                    keywords: ['ocr', 'documentos', 'configuracao', 'extração', 'ia', 'inteligência', 'pdf', 'leitura']
                }
            ]
        }
    ];

    // Unified filtering logic for Sidebar and Search
    return config
        .filter(item => {
            // Priority 1: Explicit Role Check
            if (item.roles && !item.roles.some(r => userRoles.includes(r))) {
                return false;
            }

            // Priority 2: Legacy Booleans (Fallback)
            if (item.isAdminOnly && !isAdmin) return false;
            if (item.isManagerOnly && !isLocalManager) return false;

            return true;
        })
        .map(item => {
            if (item.children) {
                return {
                    ...item,
                    children: item.children.filter(child => {
                        if (child.roles && !child.roles.some(r => userRoles.includes(r))) {
                            return false;
                        }
                        if (child.isAdminOnly && !isAdmin) return false;
                        if (child.isManagerOnly && !isLocalManager) return false;
                        return true;
                    })
                };
            }
            return item;
        })
        // Remove empty groups
        .filter(item => item.type !== 'group' || (item.children && item.children.length > 0));
};
