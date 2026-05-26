/**
 * LoadingSkeleton
 *
 * A layout-aware loading fallback for lazy-loaded route components.
 * Renders a minimal page skeleton that matches the general layout structure
 * (header bar + content blocks) to prevent layout jank during chunk loading.
 *
 * Phase 5A: Used as the Suspense fallback for route-level code splitting.
 */
import React from 'react';

const shimmer = `
@keyframes re-shimmer {
    0% { background-position: -400px 0; }
    100% { background-position: 400px 0; }
}
`;

const barStyle = (width: string, height: string = '16px'): React.CSSProperties => ({
    width,
    height,
    borderRadius: 'var(--radius-sm, 4px)',
    background: 'linear-gradient(90deg, var(--color-bg-page, #f3f4f6) 25%, var(--color-border, #e5e7eb) 50%, var(--color-bg-page, #f3f4f6) 75%)',
    backgroundSize: '800px 100%',
    animation: 're-shimmer 1.8s ease-in-out infinite',
});

export function LoadingSkeleton() {
    return (
        <>
            <style>{shimmer}</style>
            <div
                role="status"
                aria-label="Carregando conteúdo..."
                style={{
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '24px',
                    maxWidth: '1400px',
                    margin: '0 auto',
                    padding: '0 24px',
                }}
            >
                {/* Header skeleton: breadcrumb + title bar */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                    <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                        <div style={barStyle('80px', '12px')} />
                        <div style={barStyle('6px', '12px')} />
                        <div style={barStyle('100px', '12px')} />
                    </div>
                    <div style={barStyle('320px', '28px')} />
                </div>

                {/* Content card skeleton */}
                <div
                    style={{
                        backgroundColor: 'var(--color-bg-surface, #fff)',
                        border: '1px solid var(--color-border, #e5e7eb)',
                        borderRadius: 'var(--radius-md, 8px)',
                        padding: '32px',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '24px',
                    }}
                >
                    {/* Section title */}
                    <div style={barStyle('200px', '14px')} />

                    {/* Field rows */}
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '24px' }}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                            <div style={barStyle('90px', '10px')} />
                            <div style={barStyle('100%', '40px')} />
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                            <div style={barStyle('120px', '10px')} />
                            <div style={barStyle('100%', '40px')} />
                        </div>
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '24px' }}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                            <div style={barStyle('70px', '10px')} />
                            <div style={barStyle('100%', '40px')} />
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                            <div style={barStyle('110px', '10px')} />
                            <div style={barStyle('100%', '40px')} />
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                            <div style={barStyle('85px', '10px')} />
                            <div style={barStyle('100%', '40px')} />
                        </div>
                    </div>
                </div>

                {/* Second card skeleton (table-like) */}
                <div
                    style={{
                        backgroundColor: 'var(--color-bg-surface, #fff)',
                        border: '1px solid var(--color-border, #e5e7eb)',
                        borderRadius: 'var(--radius-md, 8px)',
                        padding: '32px',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '16px',
                    }}
                >
                    <div style={barStyle('160px', '14px')} />
                    {[1, 2, 3].map(i => (
                        <div key={i} style={{ display: 'flex', gap: '16px', alignItems: 'center' }}>
                            <div style={barStyle('40px', '14px')} />
                            <div style={barStyle('60%', '14px')} />
                            <div style={barStyle('80px', '14px')} />
                            <div style={barStyle('80px', '14px')} />
                        </div>
                    ))}
                </div>
            </div>
        </>
    );
}
