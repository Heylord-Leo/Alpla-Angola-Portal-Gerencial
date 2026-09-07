// Reusable per-section inline error. A failed section fetch shows this (never a silent disappearance,
// never a browser alert/toast-only) with a keyboard-accessible "Tentar novamente" that refetches ONLY
// this section. Neutral wording — it must read as a transient load failure, not an entitlement problem.
// Dark-mode via defined tokens.

interface DashboardSectionErrorProps {
  message?: string;
  onRetry: () => void;
}

export function DashboardSectionError({ message = 'Não foi possível carregar esta seção.', onRetry }: DashboardSectionErrorProps) {
  return (
    <div
      role="alert"
      data-testid="dashboard-section-error"
      style={{
        display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap',
        backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
        borderRadius: 12, padding: '14px 16px',
      }}
    >
      <span style={{ fontSize: '0.85rem', color: 'var(--color-text-main)' }}>{message}</span>
      <button
        type="button"
        onClick={onRetry}
        style={{
          font: 'inherit', fontSize: '0.8rem', fontWeight: 600, cursor: 'pointer',
          color: 'var(--color-text-main)', backgroundColor: 'var(--color-bg-page)',
          border: '1px solid var(--color-border)', borderRadius: 8, padding: '6px 12px',
        }}
      >
        Tentar novamente
      </button>
    </div>
  );
}
