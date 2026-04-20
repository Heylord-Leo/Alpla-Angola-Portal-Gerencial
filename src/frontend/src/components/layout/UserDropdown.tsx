import { useState, useRef, useEffect } from 'react';
import { User, LogOut, Key, ChevronDown, Moon, Sun, Monitor } from 'lucide-react';
import { useTheme } from '../../hooks/useTheme';
import { useAuth } from '../../features/auth/AuthContext';
import { motion, AnimatePresence } from 'framer-motion';
import { UserProfileDrawer } from './UserProfileDrawer';

export function UserDropdown() {
    const { user, logout } = useAuth();
    const { theme, setTheme } = useTheme();
    const [isOpen, setIsOpen] = useState(false);
    const [isProfileOpen, setIsProfileOpen] = useState(false);
    const dropdownRef = useRef<HTMLDivElement>(null);

    // Close on outside click
    useEffect(() => {
        function handleClickOutside(event: MouseEvent) {
            if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        }
        document.addEventListener("mousedown", handleClickOutside);
        return () => document.removeEventListener("mousedown", handleClickOutside);
    }, []);

    const menuItems = [
        { label: 'Meu Perfil', icon: <User size={16} />, onClick: () => setIsProfileOpen(true) },
        { label: 'Alterar Palavra-passe', icon: <Key size={16} />, onClick: () => window.location.href = '/change-password' },
        { label: 'Terminar Sessão', icon: <LogOut size={16} />, onClick: logout, danger: true },
    ];

    return (
        <>
            <div style={{ position: 'relative' }} ref={dropdownRef}>
                <button 
                    onClick={() => setIsOpen(!isOpen)}
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '1rem',
                        padding: '0.5rem 1rem',
                        background: 'none',
                        border: '2px solid transparent',
                        cursor: 'pointer',
                        color: 'var(--color-bg-surface)',
                        transition: 'all 0.2s',
                        borderRadius: '4px'
                    }}
                    onMouseOver={(e) => (e.currentTarget.style.backgroundColor = 'rgba(255,255,255,0.15)')}
                    onMouseOut={(e) => (e.currentTarget.style.backgroundColor = 'transparent')}
                >
                    <div style={{
                        width: '36px',
                        height: '36px',
                        backgroundColor: 'var(--color-bg-surface)',
                        color: 'var(--color-primary)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        border: '2px solid var(--color-accent)',
                        boxShadow: '2px 2px 0 var(--color-accent)'
                    }}>
                        <User size={18} strokeWidth={2.5} />
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', textAlign: 'left' }}>
                        <span style={{ fontSize: '0.8rem', fontWeight: 800, textTransform: 'uppercase' }}>
                            {user?.fullName.split(' ')[0]} {user?.fullName.split(' ').slice(-1)}
                        </span>
                        <span style={{ fontSize: '0.65rem', fontWeight: 600, color: 'rgba(255,255,255,0.7)', textTransform: 'uppercase' }}>
                            {user?.roles[0] || 'Usuário'}
                        </span>
                    </div>
                    <ChevronDown size={14} style={{ transform: isOpen ? 'rotate(180deg)' : 'none', transition: 'transform 0.2s' }} />
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
                                width: '240px',
                                backgroundColor: 'white',
                                border: '2px solid var(--color-primary)',
                                boxShadow: 'var(--shadow-md)',
                                zIndex: 100,
                                padding: '8px 0'
                            }}
                        >
                            <div style={{ padding: '12px 16px', borderBottom: '1px solid var(--color-border)', marginBottom: 8 }}>
                                <div style={{ fontWeight: 800, fontSize: '0.85rem', color: 'var(--color-primary)' }}>{user?.fullName}</div>
                                <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>{user?.email}</div>
                            </div>
                            
                            <div style={{ padding: '8px 16px', borderBottom: '1px solid var(--color-border)', marginBottom: 8 }}>
                                <div style={{ fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', marginBottom: '8px' }}>Tema Visual</div>
                                <div style={{ display: 'flex', gap: '4px', backgroundColor: 'var(--color-bg-page)', padding: '4px', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)' }}>
                                    <button 
                                        onClick={() => setTheme('light')}
                                        title="Claro"
                                        style={{ flex: 1, padding: '4px', border: 'none', background: theme === 'light' ? 'var(--color-bg-surface)' : 'transparent', color: theme === 'light' ? 'var(--color-primary)' : 'var(--color-text-muted)', borderRadius: 'var(--radius-sm)', cursor: 'pointer', boxShadow: theme === 'light' ? 'var(--shadow-sm)' : 'none', transition: 'all 0.2s', display: 'flex', justifyContent: 'center' }}>
                                        <Sun size={14} strokeWidth={theme === 'light' ? 3 : 2} />
                                    </button>
                                    <button 
                                        onClick={() => setTheme('dark')}
                                        title="Escuro"
                                        style={{ flex: 1, padding: '4px', border: 'none', background: theme === 'dark' ? 'var(--color-bg-surface)' : 'transparent', color: theme === 'dark' ? 'var(--color-primary)' : 'var(--color-text-muted)', borderRadius: 'var(--radius-sm)', cursor: 'pointer', boxShadow: theme === 'dark' ? 'var(--shadow-sm)' : 'none', transition: 'all 0.2s', display: 'flex', justifyContent: 'center' }}>
                                        <Moon size={14} strokeWidth={theme === 'dark' ? 3 : 2} />
                                    </button>
                                    <button 
                                        onClick={() => setTheme('system')}
                                        title="Sistema"
                                        style={{ flex: 1, padding: '4px', border: 'none', background: theme === 'system' ? 'var(--color-bg-surface)' : 'transparent', color: theme === 'system' ? 'var(--color-primary)' : 'var(--color-text-muted)', borderRadius: 'var(--radius-sm)', cursor: 'pointer', boxShadow: theme === 'system' ? 'var(--shadow-sm)' : 'none', transition: 'all 0.2s', display: 'flex', justifyContent: 'center' }}>
                                        <Monitor size={14} strokeWidth={theme === 'system' ? 3 : 2} />
                                    </button>
                                </div>
                            </div>

                            {menuItems.map((item, idx) => (
                                <button
                                    key={idx}
                                    onClick={() => { item.onClick(); setIsOpen(false); }}
                                    style={{
                                        display: 'flex',
                                        alignItems: 'center',
                                        gap: 12,
                                        width: '100%',
                                        padding: '10px 16px',
                                        border: 'none',
                                        background: 'none',
                                        cursor: 'pointer',
                                        textAlign: 'left',
                                        fontSize: '0.85rem',
                                        fontWeight: 700,
                                        color: item.danger ? 'var(--color-status-red)' : 'var(--color-text-main)',
                                        transition: 'all 0.1s'
                                    }}
                                    onMouseOver={(e) => (e.currentTarget.style.backgroundColor = '#f8fafc')}
                                    onMouseOut={(e) => (e.currentTarget.style.backgroundColor = 'transparent')}
                                >
                                    {item.icon}
                                    <span>{item.label}</span>
                                </button>
                            ))}
                        </motion.div>
                    )}
                </AnimatePresence>
            </div>

            <UserProfileDrawer 
                isOpen={isProfileOpen} 
                onClose={() => setIsProfileOpen(false)} 
            />
        </>
    );
}
