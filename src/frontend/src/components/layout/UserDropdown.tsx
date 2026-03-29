import { useState, useRef, useEffect } from 'react';
import { User, LogOut, Key, ChevronDown } from 'lucide-react';
import { useAuth } from '../../features/auth/AuthContext';
import { motion, AnimatePresence } from 'framer-motion';
import { UserProfileDrawer } from './UserProfileDrawer';

export function UserDropdown() {
    const { user, logout } = useAuth();
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
                    className="hover:bg-primary-hover"
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
                                boxShadow: 'var(--shadow-brutal)',
                                zIndex: 100,
                                padding: '8px 0'
                            }}
                        >
                            <div style={{ padding: '12px 16px', borderBottom: '1px solid var(--color-border)', marginBottom: 8 }}>
                                <div style={{ fontWeight: 800, fontSize: '0.85rem', color: 'var(--color-primary)' }}>{user?.fullName}</div>
                                <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>{user?.email}</div>
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
                                    className="hover:bg-slate-50"
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
