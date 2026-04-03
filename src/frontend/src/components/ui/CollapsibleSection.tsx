import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { ChevronDown, ChevronRight } from 'lucide-react';

interface CollapsibleSectionProps {
    title: string;
    count: number;
    isOpen: boolean;
    onToggle: () => void;
    children: React.ReactNode;
}

export function CollapsibleSection({ title, count, isOpen, onToggle, children }: CollapsibleSectionProps) {
    return (
        <div style={{ 
            backgroundColor: 'var(--color-bg-surface)', 
            border: '1px solid var(--color-border)', 
            boxShadow: 'var(--shadow-soft)',
            borderRadius: 'var(--radius-md)',
            marginBottom: '16px',
            overflow: 'hidden'
        }}>
            <button
                type="button"
                onClick={onToggle}
                style={{
                    width: '100%',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    padding: '12px 20px',
                    backgroundColor: isOpen ? 'rgba(var(--color-primary-rgb), 0.04)' : 'transparent',
                    border: 'none',
                    borderBottom: isOpen ? '1px solid var(--color-border)' : 'none',
                    cursor: 'pointer',
                    textAlign: 'left',
                    transition: 'all 0.2s'
                }}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    {isOpen ? <ChevronDown size={18} color="var(--color-primary)" strokeWidth={2.5} /> : <ChevronRight size={18} color="var(--color-primary)" strokeWidth={2.5} />}
                    <span style={{ 
                        fontSize: '0.8rem', 
                        fontWeight: 900, 
                        color: 'var(--color-primary)', 
                        textTransform: 'uppercase',
                        letterSpacing: '0.08em'
                    }}>
                        {title}
                    </span>
                    <span style={{ 
                        backgroundColor: 'var(--color-primary)', 
                        color: 'white', 
                        padding: '2px 10px', 
                        fontSize: '0.65rem', 
                        fontWeight: 900,
                        borderRadius: '100px' /* Pill shape for count */
                    }}>
                        {count}
                    </span>
                </div>
            </button>

            <AnimatePresence initial={false}>
                {isOpen && (
                    <motion.div
                        initial={{ height: 0, opacity: 0 }}
                        animate={{ height: 'auto', opacity: 1 }}
                        exit={{ height: 0, opacity: 0 }}
                        transition={{ duration: 0.2, ease: 'easeInOut' }}
                        style={{ overflowX: 'auto' }}
                    >
                        <div style={{ padding: '0px' }}>
                            {children}
                        </div>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}
