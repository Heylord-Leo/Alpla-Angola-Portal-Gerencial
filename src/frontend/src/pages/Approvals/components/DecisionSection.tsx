import { useState } from 'react';
import { ChevronDown } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';

interface DecisionSectionProps {
    title: string;
    icon?: React.ReactNode;
    count?: number;
    isCollapsible?: boolean;
    defaultOpen?: boolean;
    children: React.ReactNode;
    noPadding?: boolean;
    headerRight?: React.ReactNode;
}

export function DecisionSection({
    title,
    icon,
    count,
    isCollapsible = true,
    defaultOpen = true,
    children,
    noPadding = false,
    headerRight
}: DecisionSectionProps) {
    const [isOpen, setIsOpen] = useState(defaultOpen);

    const toggle = () => {
        if (!isCollapsible) return;
        setIsOpen(!isOpen);
    };

    return (
        <div style={{
            marginBottom: '24px', backgroundColor: 'var(--color-bg-surface)', 
            borderRadius: 'var(--radius-lg)', boxShadow: 'var(--shadow-sm)', 
            border: '1px solid var(--color-border)', overflow: 'hidden'
        }}>
            {/* --- Section Header --- */}
            <div 
                onClick={toggle}
                style={{
                    display: 'flex', alignItems: 'center', justifyContent: 'space-between', 
                    padding: '16px 24px', backgroundColor: 'var(--color-bg-surface)', userSelect: 'none',
                    cursor: isCollapsible ? 'pointer' : 'default',
                    borderBottom: ((isCollapsible && isOpen) || !isCollapsible) ? '1px solid var(--color-border)' : 'none',
                    transition: 'background-color 0.2s'
                }}
                onMouseOver={(e) => { if (isCollapsible) e.currentTarget.style.backgroundColor = 'var(--color-bg-page)' }}
                onMouseOut={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-surface)'}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    {icon && <span style={{ color: 'var(--color-text-muted)', display: 'flex' }}>{icon}</span>}
                    <span style={{ fontWeight: 700, color: 'var(--color-text-main)', letterSpacing: '-0.01em' }}>
                        {title}
                    </span>
                    {typeof count === 'number' && (
                        <span style={{
                            marginLeft: '4px', backgroundColor: 'var(--color-bg-page)', color: 'var(--color-text-muted)',
                            padding: '2px 8px', borderRadius: '9999px', fontSize: '0.75rem', fontWeight: 700
                        }}>
                            {count}
                        </span>
                    )}
                </div>
                
                <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                    {headerRight}
                    {isCollapsible && (
                        <motion.div
                            animate={{ rotate: isOpen ? 180 : 0 }}
                            transition={{ duration: 0.2 }}
                            style={{ color: 'var(--color-text-muted)', display: 'flex' }}
                        >
                            <ChevronDown size={20} strokeWidth={2} />
                        </motion.div>
                    )}
                </div>
            </div>

            {/* --- Section Content --- */}
            <AnimatePresence initial={false}>
                {(!isCollapsible || isOpen) && (
                    <motion.div
                        initial={isCollapsible ? { height: 0, opacity: 0 } : false}
                        animate={{ height: 'auto', opacity: 1 }}
                        exit={{ height: 0, opacity: 0 }}
                        transition={{ duration: 0.2, ease: 'easeInOut' }}
                    >
                        <div style={{ padding: noPadding ? '0' : '24px' }}>
                            {children}
                        </div>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
}
