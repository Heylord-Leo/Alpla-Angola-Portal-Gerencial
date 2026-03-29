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
            border: '2px solid var(--color-primary)', 
            boxShadow: 'var(--shadow-brutal)',
            marginBottom: '16px',
            overflow: 'hidden'
        }}>
            <button
                onClick={onToggle}
                style={{
                    width: '100%',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    padding: '16px 24px',
                    backgroundColor: 'var(--color-bg-page)',
                    border: 'none',
                    borderBottom: isOpen ? '2px solid var(--color-primary)' : 'none',
                    cursor: 'pointer',
                    textAlign: 'left',
                    transition: 'background-color 0.2s'
                }}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    {isOpen ? <ChevronDown size={20} color="var(--color-primary)" strokeWidth={3} /> : <ChevronRight size={20} color="var(--color-primary)" strokeWidth={3} />}
                    <span style={{ 
                        fontSize: '1rem', 
                        fontWeight: 900, 
                        color: 'var(--color-primary)', 
                        textTransform: 'uppercase',
                        letterSpacing: '0.05em'
                    }}>
                        {title}
                    </span>
                    <span style={{ 
                        backgroundColor: 'var(--color-primary)', 
                        color: 'white', 
                        padding: '2px 8px', 
                        fontSize: '0.75rem', 
                        fontWeight: 800,
                        borderRadius: '0'
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
