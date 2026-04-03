import React, { useState, useEffect, useRef } from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { FileText, Home, LogOut, Settings, List, ShoppingCart, ChevronDown, ChevronRight, Package, Activity, Network, ChevronLeft, CheckCircle } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { Z_INDEX } from '../../constants/ui';
import { DropdownPortal } from '../ui/DropdownPortal';

type NavItemType = 'link' | 'group' | 'action';

interface NavItem {
    id: string;
    type: NavItemType;
    label: string;
    icon: React.ReactNode;
    to?: string;
    onClick?: () => void;
    children?: NavItem[];
}

import { useAuth } from '../../features/auth/AuthContext';
import { Shield } from 'lucide-react';

interface SidebarProps {
    isExpanded: boolean;
    onToggle: () => void;
}

export function Sidebar({ isExpanded, onToggle }: SidebarProps) {
    const location = useLocation() as { pathname: string; state: { fromList?: string } | null };
    const [expandedGroupId, setExpandedGroupId] = useState<string | null>(null);
    const { isAdmin, isLocalManager, logout } = useAuth();
    
    // Hover Flyout State
    const [hoveredItem, setHoveredItem] = useState<NavItem | null>(null);
    const [hoverRect, setHoverRect] = useState<DOMRect | null>(null);
    const [sidebarRect, setSidebarRect] = useState<DOMRect | null>(null);
    const hoverTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const sidebarRef = useRef<HTMLElement>(null);

    const MENU_ITEMS: NavItem[] = [
        {
            id: 'dashboard',
            type: 'link',
            label: 'Dashboard',
            icon: <Home size={18} strokeWidth={2.5} />,
            to: '/dashboard'
        },
        {
            id: 'approvals',
            type: 'link',
            label: 'Centro de Aprovações',
            icon: <CheckCircle size={18} strokeWidth={2.5} />,
            to: '/approvals'
        },
        ...((isAdmin || isLocalManager) ? [{
            id: 'administracao',
            type: 'group',
            label: 'Administração',
            icon: <Shield size={18} strokeWidth={2.5} />,
            children: [
                {
                    id: 'admin-workspace',
                    type: 'link',
                    label: 'Workspace',
                    icon: <Shield size={18} strokeWidth={2.5} />,
                    to: '/admin/workspace'
                },
                ...(isAdmin ? [
                    {
                        id: 'admin-logs',
                        type: 'link',
                        label: 'Logs do Sistema',
                        icon: <FileText size={18} strokeWidth={2.5} />,
                        to: '/admin/logs'
                    },
                    {
                        id: 'admin-diagnosis',
                        type: 'link',
                        label: 'Diagnóstico de Serviços',
                        icon: <Activity size={18} strokeWidth={2.5} />,
                        to: '/admin/diagnosis'
                    },
                    {
                        id: 'admin-health',
                        type: 'link',
                        label: 'Saúde das Integrações',
                        icon: <Network size={18} strokeWidth={2.5} />,
                        to: '/admin/health'
                    }
                ] : [])
            ]
        } as NavItem] : []),
        {
            id: 'compras-logistica',
            type: 'group',
            label: 'Compras & Logística',
            to: '/purchasing',
            icon: <ShoppingCart size={18} strokeWidth={2.5} />,
            children: [
                {
                    id: 'pedidos',
                    type: 'link',
                    label: 'Pedidos',
                    icon: <FileText size={18} strokeWidth={2.5} />,
                    to: `/requests${location.state?.fromList || ''}`
                },
                {
                    id: 'itens-pedido',
                    type: 'link',
                    label: 'Gestão de Cotações',
                    icon: <List size={18} strokeWidth={2.5} />,
                    to: '/buyer/items'
                },
                {
                    id: 'recebimento',
                    type: 'link',
                    label: 'Recebimento',
                    icon: <Package size={18} strokeWidth={2.5} />,
                    to: '/receiving/workspace'
                }
            ]
        },
        ...(isAdmin ? [{
            id: 'configuracoes',
            type: 'group',
            label: 'Configurações',
            icon: <Settings size={18} strokeWidth={2.5} />,
            children: [
                {
                    id: 'dados-mestres',
                    type: 'link',
                    label: 'Dados Mestres',
                    icon: <List size={18} strokeWidth={2.5} />,
                    to: '/settings/master-data'
                },
                {
                    id: 'extracao-documentos',
                    type: 'link',
                    label: 'Extração Docs',
                    icon: <FileText size={18} strokeWidth={2.5} />,
                    to: '/settings/document-extraction'
                }
            ]
        } as NavItem] : [])
    ];

    // Auto-expand groups if child is active - but only if sidebar is expanded
    useEffect(() => {
        if (!isExpanded) {
            setExpandedGroupId(null);
            return;
        }

        // Find the group that should be expanded based on active children
        const activeGroup = MENU_ITEMS.find(item => {
            if (item.type === 'group' && item.children) {
                const isChildActive = item.children.some(child => child.to && location.pathname.startsWith(child.to.split('?')[0]));
                const isParentActive = item.to && (location.pathname === item.to || location.pathname.startsWith(item.to + '/'));
                return isChildActive || isParentActive;
            }
            return false;
        });

        if (activeGroup && expandedGroupId !== activeGroup.id) {
            setExpandedGroupId(activeGroup.id);
        }
    }, [location.pathname, isExpanded]);

    const toggleGroup = (id: string) => {
        if (!isExpanded) {
            onToggle();
            setExpandedGroupId(id);
            return;
        }
        setExpandedGroupId(prev => (prev === id ? null : id));
    };

    const handleLogout = () => {
        logout();
    };

    const handleItemMouseEnter = (item: NavItem, event: React.MouseEvent) => {
        if (isExpanded) return;
        if (hoverTimeoutRef.current) clearTimeout(hoverTimeoutRef.current);
        setHoveredItem(item);
        setHoverRect(event.currentTarget.getBoundingClientRect());
        setSidebarRect(sidebarRef.current?.getBoundingClientRect() || null);
    };

    const handleItemMouseLeave = () => {
        if (isExpanded) return;
        hoverTimeoutRef.current = setTimeout(() => {
            setHoveredItem(null);
            setHoverRect(null);
        }, 150);
    };

    const handleFlyoutMouseEnter = () => {
        if (hoverTimeoutRef.current) clearTimeout(hoverTimeoutRef.current);
    };

    return (
        <div style={{ position: 'relative', height: '100%' }}>
            <aside 
                ref={sidebarRef}
                style={{
                backgroundColor: 'var(--color-bg-surface)',
                borderRight: '1px solid var(--color-border)',
                boxShadow: 'var(--shadow-sm)',
                display: 'flex',
                flexDirection: 'column',
                width: isExpanded ? '260px' : '80px',
                height: '100%',
                overflow: 'hidden',
                transition: 'width 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
                position: 'relative'
            }}>
                {/* Header Zone with Logo */}
                <div style={{
                    height: '90px',
                    borderBottom: '1px solid var(--color-border)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: isExpanded ? 'space-between' : 'center',
                    padding: isExpanded ? '0 1.5rem' : '0',
                    backgroundColor: 'rgba(var(--color-primary-rgb), 0.02)',
                    position: 'relative'
                }}>
                    <AnimatePresence mode="wait">
                        {isExpanded ? (
                            <motion.img
                                key="full-logo"
                                src="/logo-v2.png"
                                alt="Alpla Logo"
                                initial={{ opacity: 0, x: -10 }}
                                animate={{ opacity: 1, x: 0 }}
                                exit={{ opacity: 0, x: -10 }}
                                transition={{ duration: 0.2 }}
                                style={{ height: '50px', width: 'auto', objectFit: 'contain' }}
                            />
                        ) : (
                            <motion.img
                                key="mini-logo"
                                src="/alplaMINILogo.png"
                                alt="Alpla Mini"
                                initial={{ opacity: 0, scale: 0.8 }}
                                animate={{ opacity: 1, scale: 1 }}
                                exit={{ opacity: 0, scale: 0.8 }}
                                transition={{ duration: 0.2 }}
                                style={{ height: '40px', width: '40px', objectFit: 'contain' }}
                            />
                        )}
                    </AnimatePresence>
                </div>

                {/* Top Zone: Main Navigation (Scrollable) */}
                <nav style={{ 
                    padding: '1rem', 
                    display: 'flex', 
                    flexDirection: 'column', 
                    gap: '0.25rem', 
                    flex: 1, 
                    overflowY: 'auto',
                    scrollbarWidth: 'thin',
                    scrollbarColor: 'var(--color-primary) transparent',
                    overflowX: 'hidden'
                }}>
                    {isExpanded && (
                        <div style={{
                            fontSize: '0.65rem',
                            fontWeight: 700,
                            letterSpacing: '0.1em',
                            color: 'var(--color-text-muted)',
                            textTransform: 'uppercase',
                            marginBottom: '1rem',
                            paddingLeft: '0.5rem'
                        }}>
                            Navegação Modular
                        </div>
                    )}

                    {MENU_ITEMS.map(item => (
                        <div 
                            key={item.id}
                            onMouseEnter={(e) => handleItemMouseEnter(item, e)}
                            onMouseLeave={handleItemMouseLeave}
                        >
                            {item.type === 'group' ? (
                                <SidebarGroup
                                    item={item}
                                    isExpanded={expandedGroupId === item.id}
                                    onToggle={() => toggleGroup(item.id)}
                                    currentPath={location.pathname}
                                    isSidebarExpanded={isExpanded}
                                />
                            ) : (
                                <SidebarLink
                                    to={item.to!}
                                    icon={item.icon}
                                    label={item.label}
                                    isSidebarExpanded={isExpanded}
                                />
                            )}
                        </div>
                    ))}
                </nav>

                {/* Bottom Zone: System Actions (Fixed) */}
                <div style={{ borderTop: '1px solid var(--color-border)', backgroundColor: 'rgba(var(--color-primary-rgb), 0.02)' }}>
                    <button
                        className="logout-btn"
                        onClick={handleLogout}
                        title={isExpanded ? "" : "Sair"}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: isExpanded ? 'flex-start' : 'center',
                            gap: '0.75rem',
                            width: '100%',
                            padding: isExpanded ? '1rem 1.5rem' : '1.25rem 0',
                            backgroundColor: 'transparent',
                            border: 'none',
                            color: 'var(--color-primary)',
                            cursor: 'pointer',
                            fontWeight: 800,
                            textAlign: 'left',
                            fontFamily: 'var(--font-family-display)',
                            textTransform: 'uppercase',
                            fontSize: '0.85rem',
                            letterSpacing: '0.05em',
                            transition: 'all 0.2s'
                        }}>
                        <LogOut size={18} strokeWidth={2.5} />
                        {isExpanded && "Sair"}
                    </button>
                </div>
            </aside>

            {/* Hoisted Toggle Button - Outside overflow:hidden aside */}
            <button
                onClick={onToggle}
                style={{
                    position: 'absolute',
                    right: '-14px',
                    top: '45px', // Vertically centered in the 90px header
                    transform: 'translateY(-50%)',
                    width: '28px',
                    height: '28px',
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    cursor: 'pointer',
                    zIndex: Z_INDEX.SIDEBAR as any,
                    padding: 0,
                    boxShadow: 'var(--shadow-sm)',
                    borderRadius: 'var(--radius-full)',
                    transition: 'all 0.2s ease'
                }}
                title={isExpanded ? "Recolher Menu" : "Expandir Menu"}
            >
                {isExpanded ? <ChevronLeft size={16} strokeWidth={2.5} /> : <ChevronRight size={16} strokeWidth={2.5} />}
            </button>

            {/* Hover Flyout */}
            <SidebarFlyout 
                item={hoveredItem}
                rect={hoverRect}
                sidebarRect={sidebarRect}
                onMouseEnter={handleFlyoutMouseEnter}
                onMouseLeave={handleItemMouseLeave}
            />
        </div>
    );
}

function SidebarFlyout({ item, rect, sidebarRect, onMouseEnter, onMouseLeave }: {
    item: NavItem | null;
    rect: DOMRect | null;
    sidebarRect: DOMRect | null;
    onMouseEnter: () => void;
    onMouseLeave: () => void;
}) {
    if (!item || !rect || !sidebarRect) return null;

    // Viewport-aware positioning
    const PADDING = 16;
    const HORIZONTAL_GAP = 12;
    const estimatedHeight = (item.children?.length || 1) * 45 + 50; // Dynamic height estimate
    let top = rect.top;
    
    // Check if it goes beyond the bottom of the viewport
    if (top + estimatedHeight > window.innerHeight - PADDING) {
        top = Math.max(PADDING, window.innerHeight - estimatedHeight - PADDING);
    }

    return (
        <DropdownPortal>
            <motion.div
                onMouseEnter={onMouseEnter}
                onMouseLeave={onMouseLeave}
                initial={{ opacity: 0, x: 5 }}
                animate={{ opacity: 1, x: 0 }}
                exit={{ opacity: 0, x: 5 }}
                transition={{ duration: 0.15 }}
                style={{
                    position: 'fixed',
                    top: `${top}px`,
                    left: `${sidebarRect.right + HORIZONTAL_GAP}px`,
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)',
                    boxShadow: 'var(--shadow-lg)',
                    borderRadius: 'var(--radius-md)',
                    zIndex: Z_INDEX.SIDEBAR as any, // Same as sidebar but portal handles layering
                    display: 'flex',
                    flexDirection: 'column',
                    minWidth: '220px',
                    pointerEvents: 'auto',
                    overflow: 'hidden'
                }}
            >
                {/* Flyout Header */}
                <div style={{
                    padding: '0.75rem 1rem',
                    borderBottom: '1px solid var(--color-border)',
                    backgroundColor: 'rgba(var(--color-primary-rgb), 0.05)',
                    fontWeight: 700,
                    fontSize: '0.75rem',
                    textTransform: 'uppercase',
                    color: 'var(--color-primary)',
                    letterSpacing: '0.05em'
                }}>
                    {item.label}
                </div>

                {/* Flyout Links */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', padding: '0.5rem' }}>
                    {item.children && item.children.length > 0 ? (
                        item.children.map(child => (
                            <SidebarLink
                                key={child.id}
                                to={child.to!}
                                icon={child.icon}
                                label={child.label}
                                isSidebarExpanded={true} // Forces labels in flyout links
                                isNested
                            />
                        ))
                    ) : (
                        item.to && (
                            <SidebarLink
                                to={item.to}
                                icon={item.icon}
                                label="Visualizar"
                                isSidebarExpanded={true}
                                isNested
                            />
                        )
                    )}
                </div>
            </motion.div>
        </DropdownPortal>
    );
}

function SidebarGroup({ item, isExpanded, onToggle, currentPath, isSidebarExpanded }: {
    item: NavItem;
    isExpanded: boolean;
    onToggle: () => void;
    currentPath: string;
    isSidebarExpanded: boolean;
}) {
    const navigate = useNavigate();
    const isChildActive = item.children?.some(child => child.to && currentPath.startsWith(child.to.split('?')[0]));
    const isParentActive = item.to && (currentPath === item.to || currentPath.startsWith(item.to + '/'));
    const isActive = isChildActive || isParentActive;

    const handleClick = () => {
        if (!isSidebarExpanded) {
            // Click Fallback in Collapsed Mode: Navigate to first child or its own link if available
            if (item.to) {
                navigate(item.to);
            } else if (item.children && item.children[0]?.to) {
                navigate(item.children[0].to);
            }
            return;
        }

        if (item.to) {
            navigate(item.to);
            if (!isExpanded) onToggle();
        } else {
            onToggle();
        }
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem' }}>
            <button
                onClick={handleClick}
                aria-expanded={isExpanded}
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: isSidebarExpanded ? 'space-between' : 'center',
                    width: '100%',
                    padding: '0.75rem 1rem',
                    backgroundColor: isActive ? 'rgba(var(--color-primary-rgb), 0.1)' : 'transparent',
                    border: '2px solid transparent',
                    color: isActive ? 'var(--color-primary)' : 'var(--color-text-main)',
                    cursor: 'pointer',
                    fontWeight: isActive ? 700 : 600,
                    fontFamily: 'var(--font-family-body)',
                    fontSize: '0.9rem',
                    borderRadius: '0',
                    textAlign: 'left',
                    transition: 'all 0.2s ease'
                }}
                title={isSidebarExpanded ? "" : item.label}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                    <motion.div style={{ display: 'flex' }}>
                        {item.icon}
                    </motion.div>
                    {isSidebarExpanded && item.label}
                </div>
                {isSidebarExpanded && (isExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />)}
            </button>

            <AnimatePresence>
                {isExpanded && isSidebarExpanded && (
                    <motion.div
                        initial={{ height: 0, opacity: 0 }}
                        animate={{ height: 'auto', opacity: 1 }}
                        exit={{ height: 0, opacity: 0 }}
                        transition={{ duration: 0.2, ease: 'easeInOut' }}
                        style={{ overflow: 'hidden', paddingLeft: '1rem', display: 'flex', flexDirection: 'column', gap: '0.25rem' }}
                    >
                        {item.children?.map(child => (
                            <SidebarLink
                                key={child.id}
                                to={child.to!}
                                icon={child.icon}
                                label={child.label}
                                isSidebarExpanded={isSidebarExpanded}
                                isNested
                            />
                        ))}
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}

function SidebarLink({ to, icon, label, isSidebarExpanded, isNested }: { 
    to: string; 
    icon: React.ReactNode; 
    label: string; 
    isSidebarExpanded: boolean;
    isNested?: boolean 
}) {
    return (
        <NavLink
            to={to}
            className={({ isActive }) => `sidebar-link ${isActive ? 'active' : ''}`}
            style={({ isActive }) => ({
                display: 'flex',
                alignItems: 'center',
                justifyContent: isSidebarExpanded ? 'flex-start' : 'center',
                gap: '0.75rem',
                padding: isNested ? (isSidebarExpanded ? '0.6rem 1rem' : '0.6rem 0') : (isSidebarExpanded ? '0.75rem 1rem' : '0.75rem 0'),
                color: isActive ? 'white' : 'var(--color-text-main)',
                backgroundColor: isActive ? 'var(--color-primary)' : 'transparent',
                fontWeight: isActive ? 700 : 500,
                textDecoration: 'none',
                fontFamily: 'var(--font-family-body)',
                fontSize: isNested ? '0.85rem' : '0.9rem',
                border: 'none',
                borderRadius: 'var(--radius-md)',
                boxShadow: isActive ? 'var(--shadow-sm)' : 'none',
                transition: 'all 0.15s ease-out',
                marginBottom: isNested ? '0' : (isSidebarExpanded ? '0.25rem' : '0'),
                width: isSidebarExpanded ? 'calc(100% - 1rem)' : '100%',
                margin: isSidebarExpanded ? '0 auto' : '0'
            })}
            title={isSidebarExpanded ? "" : label}
        >
            <motion.div
                whileHover={{ scale: 1.1 }}
                whileTap={{ scale: 0.95 }}
                style={{ display: 'flex' }}
            >
                {icon}
            </motion.div>
            {isSidebarExpanded && label}
        </NavLink>
    );
}
