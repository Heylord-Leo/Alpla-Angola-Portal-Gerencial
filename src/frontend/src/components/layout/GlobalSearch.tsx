import { useState, useEffect, useRef } from 'react';
import { Search, Command, X } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { motion, AnimatePresence } from 'framer-motion';

export function GlobalSearch() {
    const [query, setQuery] = useState('');
    const [isFocused, setIsFocused] = useState(false);
    const navigate = useNavigate();
    const searchRef = useRef<HTMLDivElement>(null);

    // Mock search results for navigation (Phase 3)
    const modules = [
        { label: 'Dashboard', path: '/dashboard', keywords: ['resumo', 'gráficos', 'geral'] },
        { label: 'Pedidos', path: '/requests', keywords: ['compras', 'lista', 'solicitações'] },
        { label: 'Gestão de Cotações', path: '/buyer/items', keywords: ['compras', 'cotações', 'itens', 'comprador'] },
        { label: 'Recebimento', path: '/receiving/workspace', keywords: ['entrega', 'materiais', 'armazém', 'logística'] },
        { label: 'Dados Mestres', path: '/settings/master-data', keywords: ['configuração', 'fornecedores', 'materiais'] },
        { label: 'Logs do Sistema', path: '/admin/logs', keywords: ['admin', 'técnico', 'eventos'] },
    ];

    const filteredModules = query.length > 1 
        ? modules.filter(m => 
            m.label.toLowerCase().includes(query.toLowerCase()) || 
            m.keywords.some(k => k.toLowerCase().includes(query.toLowerCase())))
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

    // Close on outside click
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
                maxWidth: '400px', 
                zIndex: 101 
            }}
        >
            <div style={{
                display: 'flex',
                alignItems: 'center',
                gap: '12px',
                padding: '10px 16px',
                backgroundColor: isFocused ? 'var(--color-bg-surface)' : 'rgba(255, 255, 255, 0.1)',
                border: isFocused ? '2px solid var(--color-accent)' : '2px solid transparent',
                boxShadow: isFocused ? '4px 4px 0 var(--color-accent)' : 'none',
                transition: 'all 0.2s',
                borderRadius: '4px'
            }}>
                <Search size={18} style={{ color: isFocused ? 'var(--color-primary)' : 'rgba(255, 255, 255, 0.7)' }} />
                <input 
                    type="text"
                    placeholder="Search modules... (Ctrl + K)"
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    onFocus={() => setIsFocused(true)}
                    style={{
                        background: 'none',
                        border: 'none',
                        outline: 'none',
                        width: '100%',
                        fontSize: '0.9rem',
                        fontWeight: 600,
                        color: isFocused ? 'var(--color-primary)' : 'white'
                    }}
                />
                {isFocused && (
                    <button onClick={() => { setQuery(''); setIsFocused(false); }} style={{ background: 'none', border: 'none', cursor: 'pointer', display: 'flex' }}>
                        <X size={16} />
                    </button>
                )}
            </div>

            <AnimatePresence>
                {isFocused && query.length > 1 && (
                    <motion.div
                        initial={{ opacity: 0, y: 10, scale: 0.95 }}
                        animate={{ opacity: 1, y: 0, scale: 1 }}
                        exit={{ opacity: 0, y: 10, scale: 0.95 }}
                        transition={{ duration: 0.15, ease: 'easeOut' }}
                        style={{
                            position: 'absolute',
                            top: 'calc(100% + 8px)',
                            left: 0,
                            right: 0,
                            backgroundColor: 'white',
                            border: '2px solid var(--color-primary)',
                            boxShadow: 'var(--shadow-brutal)',
                            padding: '8px 0',
                            overflow: 'hidden'
                        }}
                    >
                        <div style={{ padding: '8px 16px', fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', borderBottom: '1px solid var(--color-border)', marginBottom: 4 }}>
                            MÓDULOS ENCONTRADOS
                        </div>
                        {filteredModules.length > 0 ? (
                            filteredModules.map((m, idx) => (
                                <button
                                    key={idx}
                                    onClick={() => {
                                        navigate(m.path);
                                        setIsFocused(false);
                                        setQuery('');
                                    }}
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
                                        color: 'var(--color-primary)',
                                        transition: 'all 0.1s'
                                    }}
                                    className="hover:bg-slate-50"
                                >
                                    <Command size={14} />
                                    <span>{m.label}</span>
                                </button>
                            ))
                        ) : (
                            <div style={{ padding: '16px', fontSize: '0.85rem', color: 'var(--color-text-muted)', textAlign: 'center' }}>
                                Nenhum módulo encontrado.
                            </div>
                        )}
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}
