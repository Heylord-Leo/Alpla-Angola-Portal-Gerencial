// Reusable per-section loading placeholder. Shows an optional label ("Carregando…") plus a row of
// pulsing card placeholders that approximate the loaded section's height (reduces layout shift). One
// implementation for every Dashboard V2 section — no duplicated skeleton CSS. The pulse respects
// prefers-reduced-motion. Decorative content is aria-hidden; the wrapper is aria-busy for assistive tech.

interface DashboardSectionSkeletonProps {
  label?: string;
  cards?: number;
  cardHeight?: number;
}

export function DashboardSectionSkeleton({ label, cards = 4, cardHeight = 72 }: DashboardSectionSkeletonProps) {
  return (
    <div aria-busy="true" data-testid="dashboard-section-skeleton">
      <style>{`
        @keyframes dashSkeletonPulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.45; } }
        .dash-skel-box { animation: dashSkeletonPulse 1.5s ease-in-out infinite; }
        @media (prefers-reduced-motion: reduce) { .dash-skel-box { animation: none; } }
      `}</style>
      {label && (
        <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginBottom: 10 }}>{label}</div>
      )}
      <div aria-hidden="true" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 12 }}>
        {Array.from({ length: cards }).map((_, i) => (
          <div
            key={i}
            className="dash-skel-box"
            style={{
              height: cardHeight,
              backgroundColor: 'var(--color-bg-surface)',
              border: '1px solid var(--color-border)',
              borderRadius: 12,
            }}
          />
        ))}
      </div>
    </div>
  );
}
