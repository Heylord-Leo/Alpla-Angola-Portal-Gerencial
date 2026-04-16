import React from 'react';

interface KPICardProps {
    title: string;
    icon?: React.ReactNode;
    value: string | number;
    subtitle?: React.ReactNode;
    onClick?: () => void;
    color?: string; // Hex color for icon/text (default: primary or inherit depending on the component)
    borderColor?: string; // Custom border instead of default
    bgColor?: string;
    style?: React.CSSProperties;
    trend?: {
        value: number;
        isPositive: boolean;
        label?: string;
    };
}

export function KPICard({
    title,
    icon,
    value,
    subtitle,
    onClick,
    color,
    borderColor,
    bgColor = 'var(--color-bg-surface)',
    style = {},
    trend
}: KPICardProps) {
    const isInteractive = !!onClick;
    
    return (
        <div 
            onClick={onClick}
            style={{ 
                backgroundColor: bgColor,
                padding: '24px', 
                borderRadius: '12px', 
                border: `1px solid ${borderColor || 'var(--color-border)'}`, 
                boxShadow: '0 4px 6px rgba(0,0,0,0.05)', 
                display: 'flex', 
                flexDirection: 'column', 
                gap: '8px',
                position: 'relative',
                overflow: 'hidden',
                cursor: isInteractive ? 'pointer' : 'default',
                transition: 'all 0.2s',
                ...style 
            }}
            onMouseOver={(e) => {
                if (isInteractive) {
                    e.currentTarget.style.transform = 'translateY(-2px)';
                    e.currentTarget.style.boxShadow = '0 10px 25px rgba(56, 189, 248, 0.25), 0 0 0 1px rgba(56, 189, 248, 0.2)';
                    e.currentTarget.style.borderColor = 'rgba(56, 189, 248, 0.4)';
                }
            }}
            onMouseOut={(e) => {
                if (isInteractive) {
                    e.currentTarget.style.transform = 'none';
                    e.currentTarget.style.boxShadow = '0 4px 6px rgba(0,0,0,0.05)';
                    e.currentTarget.style.borderColor = borderColor || 'var(--color-border)';
                }
            }}
        >
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '8px', position: 'relative', zIndex: 1 }}>
                <span style={{ 
                    fontWeight: 600, 
                    fontSize: '13px', 
                    textTransform: 'uppercase', 
                    color: color || 'var(--color-text-muted)' 
                }}>
                    {title}
                </span>
                {icon && (
                    <div style={{ 
                        width: 40,
                        height: 40,
                        backgroundColor: color ? `${color}1A` : 'rgba(0, 0, 0, 0.05)',
                        color: color || 'var(--color-text-muted)',
                        borderRadius: '8px',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                    }}>
                        {icon}
                    </div>
                )}
            </div>
            
            <div style={{ 
                fontSize: '2.5rem', 
                fontWeight: 700, 
                lineHeight: 1, 
                color: color || 'var(--color-text)',
                display: 'flex',
                alignItems: 'center',
                gap: '12px',
                position: 'relative',
                zIndex: 1
            }}>
                {value}
                
                {trend && (
                    <div style={{ 
                        display: 'flex', 
                        alignItems: 'center', 
                        gap: '4px', 
                        fontSize: '0.85rem',
                        fontWeight: 600,
                        backgroundColor: trend.isPositive ? '#ecfdf5' : '#fef2f2',
                        color: trend.isPositive ? '#10b981' : '#ef4444',
                        padding: '2px 8px',
                        borderRadius: '12px'
                    }}>
                        {trend.isPositive ? '+' : '-'}{Math.abs(trend.value)}%
                    </div>
                )}
            </div>
            
            {subtitle && (
                <div style={{ 
                    fontSize: '1.1rem', 
                    fontWeight: 600, 
                    color: color || 'var(--color-text-muted)',
                    marginTop: '4px',
                    position: 'relative',
                    zIndex: 1
                }}>
                    {subtitle}
                </div>
            )}

            {icon && React.isValidElement(icon) && (
                <div style={{
                    position: 'absolute',
                    right: -8,
                    bottom: -8,
                    opacity: 0.04,
                    pointerEvents: 'none',
                    zIndex: 0
                }}>
                    {React.cloneElement(icon as React.ReactElement<any>, { size: 80 })}
                </div>
            )}
        </div>
    );
}
