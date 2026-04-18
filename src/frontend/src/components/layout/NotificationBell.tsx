import { useState, useRef, useEffect } from 'react';
import { Z_INDEX } from '../../constants/ui';
import { Bell, Info, AlertCircle, Shield, ShoppingCart, Package, CreditCard, Check, Trash2 } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { api } from '../../lib/api';
import { useNavigate } from 'react-router-dom';
import { NotificationCategories } from '../../constants/notificationConstants';

interface Notification {
    id: string;
    title: string;
    description: string;
    targetPath: string;
    type: string;
    category: string;
    count: number;
    isRead: boolean;
    isOperational: boolean;
    createdAtUtc: string;
}

export function NotificationBell() {
    const [isOpen, setIsOpen] = useState(false);
    const [notifications, setNotifications] = useState<Notification[]>([]);
    const [loading, setLoading] = useState(false);
    const bellRef = useRef<HTMLDivElement>(null);
    const navigate = useNavigate();

    const fetchNotifications = async () => {
        try {
            setLoading(true);
            const data = await api.notifications.list();
            setNotifications(data);
        } catch (err) {
            console.error('Failed to fetch notifications:', err);
        } finally {
            setLoading(false);
        }
    };

    const handleMarkAsRead = async (id: string, targetPath: string) => {
        try {
            await api.notifications.markAsRead(id);
            navigate(targetPath);
            setIsOpen(false);
            fetchNotifications();
        } catch (err) {
            console.error('Failed to mark as read:', err);
            navigate(targetPath);
            setIsOpen(false);
        }
    };

    const handleMarkAllAsRead = async () => {
        try {
            await api.notifications.markAllAsRead();
            fetchNotifications();
        } catch (err) {
            console.error('Failed to mark all as read:', err);
        }
    };

    const handleClearRead = async () => {
        try {
            await api.notifications.clearRead();
            fetchNotifications();
        } catch (err) {
            console.error('Failed to clear read:', err);
        }
    };

    useEffect(() => {
        fetchNotifications();
        // Refresh every 5 minutes
        const interval = setInterval(fetchNotifications, 5 * 60 * 1000);
        return () => clearInterval(interval);
    }, []);

    // Also refresh when opening
    useEffect(() => {
        if (isOpen) {
            fetchNotifications();
        }
    }, [isOpen]);

    // Close on outside click
    useEffect(() => {
        function handleClickOutside(event: MouseEvent) {
            if (bellRef.current && !bellRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        }
        document.addEventListener("mousedown", handleClickOutside);
        return () => document.removeEventListener("mousedown", handleClickOutside);
    }, []);



    const unreadCount = notifications.filter(n => !n.isRead).length;

    return (
        <div style={{ position: 'relative' }} ref={bellRef}>
            <button 
                onClick={() => setIsOpen(!isOpen)}
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    width: '40px',
                    height: '40px',
                    background: isOpen ? 'var(--color-bg-surface)' : 'rgba(255, 255, 255, 0.1)',
                    border: '2px solid transparent',
                    cursor: 'pointer',
                    color: isOpen ? 'var(--color-primary)' : 'white',
                    transition: 'all 0.2s',
                    borderRadius: '4px',
                    position: 'relative'
                }}
                className="hover:bg-primary-hover"
            >
                <Bell size={20} strokeWidth={2.5} />
                {unreadCount > 0 && (
                    <div style={{
                        position: 'absolute',
                        top: '-4px',
                        right: '-4px',
                        minWidth: '20px',
                        height: '20px',
                        backgroundColor: 'var(--color-status-red)',
                        color: 'white',
                        borderRadius: '10px',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        fontSize: '10px',
                        fontWeight: 900,
                        border: '2px solid var(--color-primary)',
                        padding: '0 4px',
                        boxShadow: '2px 2px 0 rgba(0,0,0,0.2)'
                    }}>
                        {unreadCount > 99 ? '99+' : unreadCount}
                    </div>
                )}
            </button>

            <AnimatePresence>
                {isOpen && (
                    <motion.div
                        initial={{ opacity: 0, y: 10, scale: 0.95 }}
                        animate={{ opacity: 1, y: 0, scale: 1 }}
                        exit={{ opacity: 0, y: 10, scale: 0.95 }}
                        transition={{ duration: 0.15, ease: 'easeOut' }}
                        style={{
                            position: 'absolute',
                            top: 'calc(100% + 8px)',
                            right: 0,
                            width: '380px',
                            backgroundColor: 'var(--color-bg-surface)',
                            border: '1px solid var(--color-border)',
                            borderRadius: 'var(--radius-lg)',
                            boxShadow: 'var(--shadow-lg)',
                            zIndex: Z_INDEX.POPOVER as any,
                            padding: '0',
                            textAlign: 'left',
                            display: 'flex',
                            flexDirection: 'column',
                            overflow: 'hidden'
                        }}
                    >
                        {/* Header */}
                        <div style={{ 
                            padding: '16px', 
                            display: 'flex', 
                            justifyContent: 'space-between', 
                            alignItems: 'center', 
                            borderBottom: '1px solid var(--color-border)',
                            backgroundColor: 'transparent'
                        }}>
                            <div style={{ fontWeight: 600, fontSize: '1rem', color: 'var(--color-text)' }}>
                                Notificações
                            </div>
                            <div style={{ display: 'flex', gap: '4px' }}>
                                <button 
                                    onClick={(e) => { e.stopPropagation(); handleMarkAllAsRead(); }}
                                    title="Marcar todas como lidas"
                                    style={{
                                        background: 'none',
                                        border: 'none',
                                        cursor: 'pointer',
                                        color: 'var(--color-text-muted)',
                                        display: 'flex',
                                        alignItems: 'center',
                                        padding: '4px',
                                        transition: 'color 0.2s',
                                        borderRadius: 'var(--radius-md)'
                                    }}
                                    onMouseEnter={(e) => { e.currentTarget.style.color = 'var(--color-primary)'; e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'; }}
                                    onMouseLeave={(e) => { e.currentTarget.style.color = 'var(--color-text-muted)'; e.currentTarget.style.backgroundColor = 'transparent'; }}
                                >
                                    <Check size={18} strokeWidth={2} />
                                </button>
                                <button 
                                    onClick={(e) => { e.stopPropagation(); handleClearRead(); }}
                                    title="Limpar lidas"
                                    style={{
                                        background: 'none',
                                        border: 'none',
                                        cursor: 'pointer',
                                        color: 'var(--color-text-muted)',
                                        display: 'flex',
                                        alignItems: 'center',
                                        padding: '4px',
                                        transition: 'color 0.2s',
                                        borderRadius: 'var(--radius-md)'
                                    }}
                                    onMouseEnter={(e) => { e.currentTarget.style.color = 'var(--color-status-red)'; e.currentTarget.style.backgroundColor = '#fee2e2'; }}
                                    onMouseLeave={(e) => { e.currentTarget.style.color = 'var(--color-text-muted)'; e.currentTarget.style.backgroundColor = 'transparent'; }}
                                >
                                    <Trash2 size={18} strokeWidth={2} />
                                </button>
                            </div>
                        </div>

                        {unreadCount > 0 && (
                            <div style={{ 
                                backgroundColor: '#fee2e2', 
                                padding: '10px 16px', 
                                fontSize: '0.75rem', 
                                fontWeight: 600, 
                                color: 'var(--color-status-red)', 
                                borderBottom: '1px solid var(--color-border)', 
                                display: 'flex', 
                                gap: '8px', 
                                alignItems: 'center' 
                            }}>
                                <AlertCircle size={14} />
                                {unreadCount} PENDENTE{unreadCount > 1 ? 'S' : ''} DE REVISÃO
                            </div>
                        )}

                        {/* List */}
                        <div style={{ maxHeight: '400px', overflowY: 'auto' }}>
                            {loading && notifications.length === 0 ? (
                                <div style={{ padding: '32px', textAlign: 'center' }}>
                                    <div className="animate-spin" style={{ margin: '0 auto 12px', width: '24px', height: '24px', border: '3px solid var(--color-border)', borderTopColor: 'var(--color-primary)', borderRadius: '50%' }} />
                                    <div style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>Sincronizando...</div>
                                </div>
                            ) : notifications.length > 0 ? (
                                notifications.map((n) => {
                                    // Local mapping to respect modern rounded icon design
                                    let iconElem, iconColor;
                                    const size = 20, strW = 2;
                                    switch (n.category) {
                                        case NotificationCategories.APPROVAL: iconElem = <Shield size={size} strokeWidth={strW} />; iconColor = '#f97316'; break;
                                        case NotificationCategories.QUOTATION: iconElem = <ShoppingCart size={size} strokeWidth={strW} />; iconColor = '#3b82f6'; break;
                                        case NotificationCategories.RECEIPT: iconElem = <Package size={size} strokeWidth={strW} />; iconColor = '#10b981'; break;
                                        case NotificationCategories.PAYMENT: iconElem = <CreditCard size={size} strokeWidth={strW} />; iconColor = '#0284c7'; break;
                                        default:
                                            if (n.type === 'ERROR') { iconElem = <AlertCircle size={size} strokeWidth={strW} />; iconColor = '#ef4444'; }
                                            else { iconElem = <Info size={size} strokeWidth={strW} />; iconColor = '#3b82f6'; }
                                    }

                                    return (
                                        <button
                                            key={n.id}
                                            onClick={() => handleMarkAsRead(n.id, n.targetPath)}
                                            style={{
                                                display: 'flex',
                                                gap: '12px',
                                                width: '100%',
                                                padding: '16px',
                                                border: 'none',
                                                background: n.isRead ? 'transparent' : 'rgba(59, 130, 246, 0.03)',
                                                cursor: 'pointer',
                                                textAlign: 'left',
                                                transition: 'all 0.2s',
                                                borderBottom: '1px solid var(--color-border)',
                                                alignItems: 'flex-start',
                                                position: 'relative'
                                            }}
                                            onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'; }}
                                            onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = n.isRead ? 'transparent' : 'rgba(59, 130, 246, 0.03)'; }}
                                        >
                                            {!n.isRead && (
                                                <div style={{
                                                    position: 'absolute',
                                                    top: '50%',
                                                    transform: 'translateY(-50%)',
                                                    left: '6px',
                                                    width: '6px',
                                                    height: '6px',
                                                    borderRadius: '50%',
                                                    backgroundColor: 'var(--color-primary)'
                                                }} />
                                            )}
                                            <div style={{ 
                                                width: 40,
                                                height: 40,
                                                backgroundColor: n.isRead ? 'var(--color-bg-page)' : `${iconColor}1A`, 
                                                color: n.isRead ? 'var(--color-text-muted)' : iconColor,
                                                borderRadius: 'var(--radius-md)',
                                                display: 'flex',
                                                alignItems: 'center',
                                                justifyContent: 'center',
                                                flexShrink: 0,
                                                marginLeft: '8px'
                                            }}>
                                                {iconElem}
                                            </div>
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', flex: 1 }}>
                                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                                    <span style={{ 
                                                        fontSize: '0.85rem', 
                                                        fontWeight: 600, 
                                                        color: n.isRead ? 'var(--color-text-muted)' : 'var(--color-text)', 
                                                    }}>
                                                        {n.title}
                                                    </span>
                                                    {n.count > 0 && (
                                                        <span style={{ fontSize: '0.7rem', fontWeight: 600, color: 'var(--color-status-red)', backgroundColor: '#fee2e2', padding: '2px 6px', borderRadius: '12px' }}>
                                                            {n.count} {n.count > 1 ? 'itens' : 'item'}
                                                        </span>
                                                    )}
                                                </div>
                                                <p style={{ 
                                                    fontSize: '0.8rem', 
                                                    fontWeight: 400, 
                                                    color: 'var(--color-text-main)', 
                                                    margin: 0, 
                                                    lineHeight: 1.5,
                                                    opacity: n.isRead ? 0.7 : 1
                                                }}>
                                                    {n.description}
                                                </p>
                                                <div style={{ 
                                                    marginTop: '4px', 
                                                    fontSize: '0.75rem', 
                                                    fontWeight: 500, 
                                                    color: 'var(--color-primary)', 
                                                    display: 'flex', 
                                                    alignItems: 'center', 
                                                    gap: '4px',
                                                    opacity: n.isRead ? 0.7 : 1
                                                }}>
                                                    {n.isOperational ? 'Acesso ao Workflow' : 'Ver Detalhes'} &rarr;
                                                </div>
                                            </div>
                                        </button>
                                    );
                                })
                            ) : (
                                <div style={{ padding: '48px 32px', textAlign: 'center' }}>
                                    <div style={{ 
                                        width: '64px', 
                                        height: '64px', 
                                        backgroundColor: 'var(--color-bg-page)', 
                                        color: 'var(--color-text-muted)', 
                                        display: 'flex', 
                                        alignItems: 'center', 
                                        justifyContent: 'center', 
                                        margin: '0 auto 16px',
                                        border: '1px solid var(--color-border)',
                                        borderRadius: '50%'
                                    }}>
                                        <Bell size={28} strokeWidth={1.5} />
                                    </div>
                                    <div style={{ fontSize: '0.9rem', fontWeight: 600, color: 'var(--color-text)' }}>
                                        Sem Alertas Relevantes
                                    </div>
                                    <p style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: '8px', fontWeight: 400 }}>
                                        O seu ecossistema operativo está totalmente regularizado.
                                    </p>
                                </div>
                            )}
                        </div>

                        {/* Footer */}
                        <button 
                            onClick={() => {
                                navigate('/requests');
                                setIsOpen(false);
                            }}
                            style={{
                                width: '100%',
                                padding: '16px',
                                background: 'transparent',
                                border: 'none',
                                borderTop: '1px solid var(--color-border)',
                                color: 'var(--color-text)',
                                fontWeight: 500,
                                fontSize: '0.85rem',
                                cursor: 'pointer',
                                transition: 'all 0.2s',
                                backgroundColor: 'var(--color-bg-surface)'
                            }}
                            onMouseEnter={(e) => { e.currentTarget.style.color = 'var(--color-primary)'; e.currentTarget.style.backgroundColor = 'var(--color-bg-page)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.color = 'var(--color-text)'; e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'; }}
                        >
                            Ver Todos os Processos
                        </button>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}

