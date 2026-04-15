import React from 'react';
import { X, BookOpen } from 'lucide-react';

export interface GuideModalProps {
    isOpen: boolean;
    onClose: () => void;
    title: string;
    subtitle?: string;
    children: React.ReactNode;
}

export function GuideModal({ isOpen, onClose, title, subtitle, children }: GuideModalProps) {
    if (!isOpen) return null;

    return (
        <div style={{ 
            position: 'fixed', top: 0, left: 0, width: '100vw', height: '100vh', 
            backgroundColor: 'rgba(15, 23, 42, 0.7)', zIndex: 9999, 
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            backdropFilter: 'blur(4px)'
        }}>
            <div style={{
                backgroundColor: 'var(--color-bg-surface)', border: '4px solid #0f172a',
                width: '90%', maxWidth: '750px', maxHeight: '90vh', overflowY: 'auto',
                boxShadow: '16px 16px 0 #0f172a', padding: '32px', position: 'relative'
            }}>
                <button 
                    onClick={onClose}
                    style={{
                        position: 'absolute', top: '16px', right: '16px', backgroundColor: 'transparent',
                        border: 'none', cursor: 'pointer', color: '#64748b'
                    }}
                    onMouseOver={(e) => e.currentTarget.style.color = '#0f172a'}
                    onMouseOut={(e) => e.currentTarget.style.color = '#64748b'}
                >
                    <X size={32} />
                </button>
                
                <h2 style={{ fontSize: '2rem', fontWeight: 900, textTransform: 'uppercase', marginBottom: '8px', borderBottom: '4px solid #e2e8f0', paddingBottom: '16px' }}>
                    {title}
                </h2>
                {subtitle && (
                    <p style={{ fontWeight: 500, fontSize: '15px', color: '#475569', marginBottom: '32px' }}>
                        {subtitle}
                    </p>
                )}

                <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                    {children}
                </div>
            </div>
        </div>
    );
}

export interface GuideModalSectionProps {
    icon: React.ReactNode;
    iconBgColor: string;
    iconColor: string;
    title: string;
    children: React.ReactNode;
}

export function GuideModalSection({ icon, iconBgColor, iconColor, title, children }: GuideModalSectionProps) {
    return (
        <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
            <div style={{ backgroundColor: iconBgColor, padding: '12px', color: iconColor, border: `2px solid ${iconColor}` }}>
                {icon}
            </div>
            <div style={{ flex: 1 }}>
                <h4 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 900 }}>{title}</h4>
                {children}
            </div>
        </div>
    );
}
