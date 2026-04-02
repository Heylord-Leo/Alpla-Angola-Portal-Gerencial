import React from 'react';
import { Link } from 'react-router-dom';
import { Feedback, FeedbackType } from '../../../components/ui/Feedback';
import { Z_INDEX } from '../../../constants/ui';

export interface BreadcrumbItem {
    label: string;
    to?: string;
}

export interface OperationalGuidance {
    responsible: string;
    nextAction: string;
}

interface RequestActionHeaderProps {
    breadcrumbs: BreadcrumbItem[];
    title: string;
    requestNumber?: string | null;
    statusBadge?: React.ReactNode;
    contextBadges?: React.ReactNode;
    primaryActions?: React.ReactNode;
    secondaryActions?: React.ReactNode;
    operationalGuidance?: OperationalGuidance | null;
    feedback: { type: FeedbackType; message: string | null };
    onCloseFeedback: () => void;
    children?: React.ReactNode;
}

export const RequestActionHeader: React.FC<RequestActionHeaderProps> = ({
    breadcrumbs,
    title,
    requestNumber,
    statusBadge,
    contextBadges,
    primaryActions,
    secondaryActions,
    operationalGuidance,
    feedback,
    onCloseFeedback,
    children
}) => {
    return (
        <div style={{
            position: 'sticky',
            top: 'calc(var(--header-height) - 1rem)',
            zIndex: Z_INDEX.STICKY as any,
            backgroundColor: 'var(--color-bg-page)',
            margin: '-1rem -24px 0 -24px',
            padding: '8px 24px 0 24px',
            borderBottom: '2px solid var(--color-border-heavy)',
            display: 'flex',
            flexDirection: 'column',
            transition: 'all 0.2s ease',
            boxShadow: '0 4px 6px -1px rgba(0, 0, 0, 0.05)'
        }}>
            {/* 0. Global Feedback */}
            <Feedback
                type={feedback.type}
                message={feedback.message}
                onClose={onCloseFeedback}
                isFixed={true}
            />

            {/* Row 1: Context & Status Context (Secondary) */}
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap', minHeight: '28px', padding: '4px 0' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--color-text-muted)' }}>
                    {breadcrumbs.map((item, idx) => (
                        <React.Fragment key={idx}>
                            {item.to ? (
                                <Link to={item.to} style={{ color: 'inherit', textDecoration: 'none', opacity: 0.7 }} className="hover-primary">{item.label}</Link>
                            ) : (
                                <span style={{ color: 'var(--color-text-main)' }}>{item.label}</span>
                            )}
                            {idx < breadcrumbs.length - 1 && <span style={{ opacity: 0.3 }}>/</span>}
                        </React.Fragment>
                    ))}

                    {/* Status Badge */}
                    {statusBadge}
                </div>

                <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                    {contextBadges}
                </div>
            </div>

            {/* Row 2: Identity & Primary Actions */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '16px', minHeight: '48px', padding: '4px 0' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <h1 style={{ 
                        margin: 0, 
                        fontSize: '1.35rem', 
                        fontWeight: 900, 
                        color: 'var(--color-text-main)', 
                        textTransform: 'uppercase', 
                        letterSpacing: '-0.02em',
                        whiteSpace: 'nowrap'
                    }}>
                        {title}
                    </h1>
                    {requestNumber && (
                        <div style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '12px',
                            borderLeft: '2px solid var(--color-border)',
                            paddingLeft: '12px'
                        }}>
                            <span style={{
                                fontSize: '1.35rem',
                                fontWeight: 900,
                                color: 'var(--color-primary)',
                                fontFamily: 'var(--font-family-display)'
                            }}>
                                {requestNumber}
                            </span>
                        </div>
                    )}
                </div>

                <div style={{ display: 'flex', gap: '12px', alignItems: 'center' }}>
                    {secondaryActions}
                    {primaryActions}
                </div>
            </div>

            {/* Row 3: Operational Context (Compact Strip) */}
            {operationalGuidance && (
                <div style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '24px',
                    padding: '8px 16px',
                    backgroundColor: 'var(--color-bg-surface)',
                    borderRadius: '8px',
                    borderLeft: '4px solid var(--color-primary)',
                    fontSize: '0.75rem',
                    marginBottom: '12px',
                    boxShadow: 'inset 0 1px 2px rgba(0,0,0,0.05)'
                }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <span style={{ fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', fontSize: '0.65rem' }}>Responsável:</span>
                        <span style={{ fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase' }}>{operationalGuidance.responsible}</span>
                    </div>
                    <div style={{ width: '1px', height: '12px', backgroundColor: 'var(--color-border)' }}></div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <span style={{ fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', fontSize: '0.65rem' }}>Próxima Ação:</span>
                        <span style={{ fontWeight: 700, fontStyle: 'italic', color: 'var(--color-text-main)' }}>"{operationalGuidance.nextAction}"</span>
                    </div>
                </div>
            )}

            {/* Slots for Action Bars */}
            {children && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', paddingBottom: '12px' }}>
                    {children}
                </div>
            )}
        </div>
    );
};
