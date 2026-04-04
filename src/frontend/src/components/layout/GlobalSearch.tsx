import { useState, useEffect, useRef, useMemo } from 'react';
import { Search, Command, X } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { motion, AnimatePresence } from 'framer-motion';
import { useAuth } from '../../features/auth/AuthContext';
import { getNavigationConfig, NavItem } from '../../constants/navigation';

export function GlobalSearch() {
    const [query, setQuery] = useState('');
    const [isFocused, setIsFocused] = useState(false);
    const navigate = useNavigate();
    const searchRef = useRef<HTMLDivElement>(null);
    const { user } = useAuth();

    // Obter configuração de navegação autorizada
    const authorizedNav = useMemo(() => getNavigationConfig(user?.roles || []), [user?.roles]);

    // Achatar a árvore de navegação para apenas itens navegáveis (folhas)
    const searchableModules = useMemo(() => {
        const flattened: (NavItem & { groupLabel?: string })[] = [];
        const process = (items: NavItem[], groupLabel?: string) => {
            items.forEach(item => {
                if (item.type === 'link' && item.to) {
                    flattened.push({ ...item, groupLabel });
                }
                if (item.children) {
                    process(item.children, item.label);
                }
            });
        };
        process(authorizedNav);
        return flattened;
    }, [authorizedNav]);

    const filteredModules = query.length > 1 
        ? searchableModules.filter(m => 
            m.label.toLowerCase().includes(query.toLowerCase()) || 
            (m.groupLabel && m.groupLabel.toLowerCase().includes(query.toLowerCase())) ||
            m.keywords?.some(k => k.toLowerCase().includes(query.toLowerCase())))
        : [];

    useEffect(() => {
        function handleKeyDown(e: KeyboardEvent) {
            if (e.ctrlKey && e.key === 'k') {
                e.preventDefault();
                setIsFocused(true);
            }
        }
        document.addEventListener('keydown', handleKeyDown);
        return () => document.removeEventListener('keydown', handleKeyDown);
    }, []);

    // Fechar ao clicar fora
    useEffect(() => {
        function handleClickOutside(event: MouseEvent) {
            if (searchRef.current && !searchRef.current.contains(event.target as Node)) {
                setIsFocused(false);
            }
        }
        document.addEventListener("mousedown", handleClickOutside);
        return () => document.removeEventListener("mousedown", handleClickOutside);
    }, []);

    return (
        <div 
            ref={searchRef}
            style={{ 
                position: 'relative', 
                width: '100%', 
                maxWidth: '600px',
                zIndex: 101 
            }}
        >
            <div style={{ 
                display: 'flex',
                alignItems: 'center',
                gap: '12px',
                padding: '10px 16px',
                backgroundColor: isFocused ? 'white' : 'rgba(255, 255, 255, 0.2)',
                border: isFocused ? '2px solid var(--color-accent)' : '1px solid rgba(255, 255, 255, 0.25)',
                boxShadow: isFocused ? 'var(--shadow-md)' : 'none',
                transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
                borderRadius: 'var(--radius-md)',
                cursor: 'text'
            }} onClick={() => setIsFocused(true)}>
                <Search size={18} style={{ color: isFocused ? 'var(--color-primary)' : 'rgba(255, 255, 255, 0.95)' }} />
                <input 
                    type="text"
                    placeholder="Pesquisar módulos... (Ctrl + K)"
                    className={!isFocused ? 'placeholder-light' : ''}
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    onFocus={() => setIsFocused(true)}
                    style={{
                        background: 'none',
                        border: 'none',
                        outline: 'none',
                        width: '100%',
                        fontSize: '0.95rem',
                        fontWeight: 600,
                        fontFamily: 'var(--font-family-body)',
                        color: isFocused ? 'var(--color-primary)' : 'white'
                    }}
                />
                {!isFocused ? (
                    <div style={{
                        fontSize: '0.7rem',
                        fontWeight: 800,
                        backgroundColor: 'rgba(255, 255, 255, 0.1)',
                        padding: '2px 6px',
                        borderRadius: '4px',
                        color: 'rgba(255, 255, 255, 0.8)',
                        border: '1px solid rgba(255, 255, 255, 0.2)',
                        letterSpacing: '0.05em'
                    }}>
                        CTRL K
                    </div>
                ) : (
                    <button 
                        onClick={(e) => { e.stopPropagation(); setQuery(''); setIsFocused(false); }} 
                        style={{ background: 'none', border: 'none', cursor: 'pointer', display: 'flex', color: 'var(--color-text-muted)' }}
                    >
                        <X size={16} />
                    </button>
                )}
            </div>

            <AnimatePresence>
                {isFocused && query.length > 1 && (
                    <motion.div
                        initial={{ opacity: 0, y: 10, scale: 0.98 }}
                        animate={{ opacity: 1, y: 0, scale: 1 }}
                        exit={{ opacity: 0, y: 10, scale: 0.98 }}
                        transition={{ duration: 0.2, ease: 'easeOut' }}
                        style={{
                            position: 'absolute',
                            top: 'calc(100% + 12px)',
                            left: 0,
                            right: 0,
                            backgroundColor: 'white',
                            border: '1px solid var(--color-border)',
                            boxShadow: 'var(--shadow-premium)',
                            padding: '12px 0',
                            borderRadius: 'var(--radius-lg)',
                            overflow: 'hidden'
                        }}
                    >
                        <div style={{ 
                            padding: '0 20px 8px', 
                            fontSize: '0.7rem', 
                            fontWeight: 800, 
                            letterSpacing: '0.05em',
                            color: 'var(--color-text-muted)', 
                            borderBottom: '1px solid var(--color-border)', 
                            marginBottom: 8,
                            fontFamily: 'var(--font-family-display)'
                        }}>
                            MÓDULOS ENCONTRADOS
                        </div>
                        {filteredModules.length > 0 ? (
                            <div style={{ display: 'flex', flexDirection: 'column' }}>
                                {filteredModules.map((m, idx) => (
                                    <button
                                        key={m.id || idx}
                                        onClick={() => {
                                            navigate(m.to!);
                                            setIsFocused(false);
                                            setQuery('');
                                        }}
                                        style={{
                                            display: 'flex',
                                            alignItems: 'center',
                                            justifyContent: 'space-between',
                                            width: '100%',
                                            padding: '12px 20px',
                                            border: 'none',
                                            background: 'none',
                                            cursor: 'pointer',
                                            textAlign: 'left',
                                            transition: 'all 0.2s'
                                        }}
                                        className="search-result-item"
                                    >
                                        <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
                                            <div style={{
                                                width: 32,
                                                height: 32,
                                                borderRadius: 'var(--radius-md)',
                                                backgroundColor: 'rgba(0, 77, 144, 0.05)',
                                                display: 'flex',
                                                alignItems: 'center',
                                                justifyContent: 'center',
                                                color: 'var(--color-primary)'
                                            }}>
                                                {m.icon}
                                            </div>
                                            <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                <span style={{ 
                                                    fontSize: '0.9rem', 
                                                    fontWeight: 700, 
                                                    fontFamily: 'var(--font-family-display)',
                                                    color: 'var(--color-primary)'
                                                }}>
                                                    {m.label}
                                                </span>
                                                {m.groupLabel && (
                                                    <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 500 }}>
                                                        {m.groupLabel}
                                                    </span>
                                                )}
                                            </div>
                                        </div>
                                        <Command size={12} style={{ color: 'var(--color-border-heavy)', opacity: 0.3 }} />
                                    </button>
                                ))}
                            </div>
                        ) : (
                            <div style={{ padding: '24px', fontSize: '0.9rem', color: 'var(--color-text-muted)', textAlign: 'center', fontFamily: 'var(--font-family-body)' }}>
                                Nenhum módulo encontrado para "<span style={{ fontWeight: 700 }}>{query}</span>"
                            </div>
                        )}
                    </motion.div>
                )}
            </AnimatePresence>

            <style>{`
                .search-result-item:hover {
                    background-color: rgba(var(--color-primary-rgb), 0.04);
                }
                .search-result-item:hover span {
                    color: var(--color-secondary) !important;
                }
            `}</style>
        </div>
    );
}

