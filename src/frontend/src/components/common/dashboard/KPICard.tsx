import React from 'react';
import { motion } from 'framer-motion';
import { LucideIcon } from 'lucide-react';

interface KPICardProps {
    label: string;
    count: number;
    icon: LucideIcon;
    isActive: boolean;
    onClick: () => void;
    color: string;
    delay?: number;
}

export function KPICard({ label, count, icon: Icon, isActive, onClick, color, delay = 0 }: KPICardProps) {
    // Industrial color mapping
    const colorStyles: Record<string, { bg: string, border: string, iconBg: string }> = {
        'bg-blue-500': { bg: '#eff6ff', border: '#3b82f6', iconBg: '#3b82f6' },
        'bg-amber-500': { bg: '#fffbeb', border: '#f59e0b', iconBg: '#f59e0b' },
        'bg-rose-500': { bg: '#fff1f2', border: '#f43f5e', iconBg: '#f43f5e' },
        'bg-emerald-500': { bg: '#ecfdf5', border: '#10b981', iconBg: '#10b981' }
    };

    const palette = colorStyles[color] || { bg: '#f8fafc', border: '#cbd5e1', iconBg: '#64748b' };

    const cardStyle: React.CSSProperties = {
        position: 'relative',
        display: 'flex',
        flexDirection: 'column',
        justifyContent: 'space-between',
        padding: '24px',
        cursor: 'pointer',
        border: `2px solid ${isActive ? 'var(--color-primary)' : palette.border}`,
        backgroundColor: isActive ? 'white' : palette.bg,
        boxShadow: isActive ? '8px 8px 0px 0px rgba(0,0,0,1)' : '4px 4px 0px 0px rgba(0,0,0,0.05)',
        transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
        overflow: 'hidden',
        minHeight: '140px',
        flex: 1,
        zIndex: isActive ? 10 : 1,
        transform: isActive ? 'scale(1.02)' : 'scale(1)'
    };

    return (
        <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay, type: "spring", stiffness: 100 }}
            whileHover={{ y: -4, borderColor: 'var(--color-primary)', backgroundColor: 'white' }}
            whileTap={{ scale: 0.98 }}
            onClick={onClick}
            style={cardStyle}
        >
            {/* Background Watermark Icon */}
            <div style={{ position: 'absolute', right: '-20px', bottom: '-20px', opacity: 0.05, pointerEvents: 'none' }}>
                <Icon size={100} strokeWidth={1} />
            </div>

            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'start', marginBottom: '16px' }}>
                <div style={{ 
                    padding: '8px', 
                    border: '2px solid black', 
                    backgroundColor: palette.iconBg, 
                    boxShadow: '2px 2px 0px 0px rgba(0,0,0,1)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center'
                }}>
                    <Icon size={20} color="white" />
                </div>
                {isActive && (
                    <motion.div 
                        initial={{ scale: 0 }}
                        animate={{ scale: 1 }}
                        style={{ width: '10px', height: '10px', borderRadius: '50%', backgroundColor: 'var(--color-primary)' }}
                    />
                )}
            </div>

            <div>
                <div style={{ fontSize: '2rem', fontWeight: 900, letterSpacing: '-0.05em', color: 'var(--color-primary)', marginBottom: '4px', lineHeight: 1 }}>
                    {count.toLocaleString()}
                </div>
                <div style={{ fontSize: '0.65rem', fontWeight: 800, letterSpacing: '0.15em', color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                    {label}
                </div>
            </div>

            {/* Selection indicator line */}
            {isActive && (
                <motion.div 
                    layoutId="kpi-indicator"
                    style={{ position: 'absolute', bottom: 0, left: 0, right: 0, height: '4px', backgroundColor: 'var(--color-primary)' }}
                />
            )}
        </motion.div>
    );
}
