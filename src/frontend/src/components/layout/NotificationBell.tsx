import { useState, useRef, useEffect } from 'react';
import { Bell, Info, AlertCircle, Clock, Shield, ShoppingCart, Package, CreditCard, Check, Trash2 } from 'lucide-react';
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

    const getIcon = (category: string, type: string) => {
        const size = 18;
        const strokeWidth = 2.5;

        switch (category) {
            case NotificationCategories.APPROVAL: return <Shield size={size} strokeWidth={strokeWidth} color="var(--color-status-orange)" />;
            case NotificationCategories.QUOTATION: return <ShoppingCart size={size} strokeWidth={strokeWidth} color="var(--color-primary)" />;
            case NotificationCategories.RECEIPT: return <Package size={size} strokeWidth={strokeWidth} color="var(--color-status-green)" />;
            case NotificationCategories.PAYMENT: return <CreditCard size={size} strokeWidth={strokeWidth} color="var(--color-status-blue)" />;
            default:
                if (type === 'ERROR') return <AlertCircle size={size} strokeWidth={strokeWidth} color="var(--color-status-red)" />;
                return <Info size={size} strokeWidth={strokeWidth} color="var(--color-primary)" />;
        }
    };

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
                            width: '360px',
                            backgroundColor: 'white',
                            border: '2px solid var(--color-primary)',
                            boxShadow: 'var(--shadow-brutal)',
                            zIndex: 1002,
                            padding: '0',
                            textAlign: 'left',
                            display: 'flex',
                            flexDirection: 'column'
                        }}
                    >
                        {/* Header */}
                        <div style={{ 
                            padding: '16px', 
                            display: 'flex', 
                            justifyContent: 'space-between', 
                            alignItems: 'center', 
                            borderBottom: '2px solid var(--color-primary)',
                            backgroundColor: 'white'
                        }}>
                            <div style={{ fontWeight: 900, fontSize: '0.9rem', color: 'var(--color-primary)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                Notificações
                            </div>
                            <div style={{ display: 'flex', gap: '8px' }}>
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
                                        padding: '4px'
                                    }}
                                    className="hover:text-primary"
                                >
                                    <Check size={16} strokeWidth={3} />
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
                                        padding: '4px'
                                    }}
                                    className="hover:text-status-red"
                                >
                                    <Trash2 size={16} strokeWidth={2.5} />
                                </button>
                            </div>
                        </div>

                        {unreadCount > 0 && (
                            <div style={{ backgroundColor: 'var(--color-bg-page)', padding: '8px 16px', fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-status-red)', borderBottom: '1px solid var(--color-border)', display: 'flex', gap: '8px', alignItems: 'center' }}>
                                <AlertCircle size={12} />
                                {unreadCount.toString().toUpperCase()} PENDENTES DE REVISÃO
                            </div>
                        )}

                        {/* List */}
                        <div style={{ maxHeight: '400px', overflowY: 'auto' }}>
                            {loading && notifications.length === 0 ? (
                                <div style={{ padding: '32px', textAlign: 'center' }}>
                                    <div className="animate-spin" style={{ margin: '0 auto 12px', width: '24px', height: '24px', border: '3px solid var(--color-border)', borderTopColor: 'var(--color-primary)', borderRadius: '50%' }} />
                                    <div style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>Sincronizando...</div>
                                </div>
                            ) : notifications.length > 0 ? (
                                notifications.map((n) => (
                                    <button
                                        key={n.id}
                                        onClick={() => handleMarkAsRead(n.id, n.targetPath)}
                                        style={{
                                            display: 'flex',
                                            gap: '16px',
                                            width: '100%',
                                            padding: '16px',
                                            border: 'none',
                                            background: n.isRead ? 'white' : 'var(--color-bg-page)',
                                            cursor: 'pointer',
                                            textAlign: 'left',
                                            transition: 'all 0.1s',
                                            borderBottom: '1px solid var(--color-border)',
                                            alignItems: 'flex-start',
                                            position: 'relative'
                                        }}
                                        className="hover:bg-slate-50"
                                    >
                                        {!n.isRead && (
                                            <div style={{
                                                position: 'absolute',
                                                top: '16px',
                                                left: '8px',
                                                width: '6px',
                                                height: '6px',
                                                borderRadius: '50%',
                                                backgroundColor: 'var(--color-primary)'
                                            }} />
                                        )}
                                        <div style={{ 
                                            padding: '8px', 
                                            backgroundColor: n.isRead ? 'var(--color-bg-page)' : 'white', 
                                            border: n.isRead ? '2px solid var(--color-border)' : '2px solid var(--color-primary)',
                                            flexShrink: 0
                                        }}>
                                            {getIcon(n.category, n.type)}
                                        </div>
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', flex: 1 }}>
                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                <span style={{ 
                                                    fontSize: '0.85rem', 
                                                    fontWeight: n.isRead ? 700 : 900, 
                                                    color: 'var(--color-primary)', 
                                                    textTransform: 'uppercase',
                                                    opacity: n.isRead ? 0.7 : 1
                                                }}>
                                                    {n.title}
                                                </span>
                                                {n.count > 0 && (
                                                    <span style={{ fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-status-red)' }}>
                                                        ({n.count})
                                                    </span>
                                                )}
                                            </div>
                                            <p style={{ 
                                                fontSize: '0.8rem', 
                                                fontWeight: n.isRead ? 500 : 600, 
                                                color: 'var(--color-text-main)', 
                                                margin: 0, 
                                                lineHeight: 1.4,
                                                opacity: n.isRead ? 0.6 : 1
                                            }}>
                                                {n.description}
                                            </p>
                                            <div style={{ 
                                                marginTop: '4px', 
                                                fontSize: '0.7rem', 
                                                fontWeight: 800, 
                                                color: 'var(--color-primary)', 
                                                display: 'flex', 
                                                alignItems: 'center', 
                                                gap: '4px',
                                                opacity: n.isRead ? 0.5 : 1
                                            }}>
                                                {n.isOperational ? 'Sincronizado com Workflow' : 'Informação'} &rarr;
                                            </div>
                                        </div>
                                    </button>
                                ))
                            ) : (
                                <div style={{ padding: '48px 32px', textAlign: 'center' }}>
                                    <div style={{ 
                                        width: '64px', 
                                        height: '64px', 
                                        backgroundColor: 'var(--color-bg-page)', 
                                        color: 'var(--color-border)', 
                                        display: 'flex', 
                                        alignItems: 'center', 
                                        justifyContent: 'center', 
                                        margin: '0 auto 16px',
                                        border: '2px dashed var(--color-border)'
                                    }}>
                                        <Clock size={32} strokeWidth={1.5} />
                                    </div>
                                    <div style={{ fontSize: '0.9rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                                        Sem Alertas Relevantes
                                    </div>
                                    <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: '8px', fontWeight: 600 }}>
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
                                padding: '14px',
                                background: 'var(--color-bg-page)',
                                border: 'none',
                                borderTop: '2px solid var(--color-primary)',
                                color: 'var(--color-primary)',
                                fontWeight: 900,
                                fontSize: '0.75rem',
                                cursor: 'pointer',
                                textTransform: 'uppercase',
                                letterSpacing: '0.05em'
                            }}
                            className="hover:bg-primary-hover hover:text-white"
                        >
                            Ver Todo o Processamento Operativo
                        </button>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}
