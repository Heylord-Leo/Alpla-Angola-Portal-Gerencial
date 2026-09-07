import { api } from '../../../lib/api';
import { formatCurrencyAO } from '../../../lib/utils';
import { SectionInfo } from '../../../components/ui/SectionInfo';
import { DASHBOARD_SECTION_HELP } from '../dashboardSectionHelp';
import { entityUnit } from '../pipelineView';
import { useSectionData } from '../useSectionData';
import { DashboardSectionSkeleton } from './DashboardSectionSkeleton';
import { DashboardSectionError } from './DashboardSectionError';
import { UNKNOWN_CURRENCY } from '../../../types/dashboardV2';
import type { FinancialCategoryDto, CurrencyAmountDto, PaidHistoryDto } from '../../../types/dashboardV2';

// Dashboard V2 B7 — canonical currency-safe Financial Summary (GERENCIAL, read-only). Renders monetary
// exposure per category, one row PER CURRENCY (never combined), straight from GET /api/dashboard/v2/financial.
// The section is entitlement-gated on the server: currentExposure === null → not entitled → render nothing.
// The frontend only formats values (no currency conversion, no summing across currencies/categories, no
// population/paid/status logic).

const GERENCIAL = { label: 'Gerencial', color: '#3b5069' };

function currencyLabel(code: string): string {
  return code === UNKNOWN_CURRENCY ? 'Moeda não identificada' : code;
}

export function DashboardV2FinancialSection() {
  const { status, data, retry } = useSectionData((signal) => api.dashboardV2.getFinancial(signal));

  const header = (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
      <h2 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 }}>Resumo Financeiro</h2>
      <span style={{
        fontSize: '0.68rem', fontWeight: 700, letterSpacing: '0.04em', textTransform: 'uppercase',
        color: GERENCIAL.color, backgroundColor: `${GERENCIAL.color}1A`, borderRadius: 999, padding: '2px 9px',
      }}>{GERENCIAL.label}</span>
      <SectionInfo {...DASHBOARD_SECTION_HELP.financialSummary} />
    </div>
  );

  // A pending request is NOT "unauthorized": show the header + a loading shell until entitlement is known.
  if (status === 'loading') {
    return <section data-testid="dashboard-v2-financial" aria-busy="true">{header}<DashboardSectionSkeleton label="Carregando resumo financeiro..." cards={4} /></section>;
  }
  if (status === 'error' || !data) {
    return <section data-testid="dashboard-v2-financial">{header}<DashboardSectionError onRetry={retry} /></section>;
  }
  // Resolved & not entitled (server authority) → hide the section entirely; no placeholder, no denial message.
  if (data.currentExposure === null) return null;

  return (
    <section data-testid="dashboard-v2-financial">
      {header}

      {data.currentExposure.length === 0 ? (
        <div style={{
          backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
          borderRadius: 12, padding: '20px', color: 'var(--color-text-muted)', fontSize: '0.9rem',
        }}>Não há exposição financeira no escopo atual.</div>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 12 }}>
          {data.currentExposure.map((c) => <FinancialCategoryCard key={c.code} category={c} />)}
        </div>
      )}

      {/* Secondary, visually subordinate: paid-history evidence for the period (B7.3). */}
      {data.paidHistory && <PaidHistoryBlock history={data.paidHistory} />}
    </section>
  );
}

function PaidHistoryBlock({ history }: { history: PaidHistoryDto }) {
  const isEmpty = history.paymentCount === 0;
  return (
    <div style={{ marginTop: 24 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
        <h3 style={{ fontSize: '0.9rem', fontWeight: 700, color: 'var(--color-text-main)', margin: 0 }}>Histórico de Pagamentos</h3>
        <span style={{
          fontSize: '0.66rem', fontWeight: 600, color: 'var(--color-text-muted)',
          backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)', borderRadius: 999, padding: '1px 8px',
        }}>{history.periodLabel}</span>
      </div>
      <p style={{ fontSize: '0.76rem', color: 'var(--color-text-muted)', margin: '0 0 10px 0' }}>
        Pagamentos confirmados no período, separados por moeda.
      </p>

      {isEmpty ? (
        <div style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>
          Nenhum pagamento confirmado nos últimos 30 dias.
        </div>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 10 }}>
          {history.currencies.map((cur) => (
            <div key={cur.currencyCode} style={{
              backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
              borderRadius: 12, padding: '12px 14px',
            }}>
              <div style={{ fontSize: '0.72rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>{currencyLabel(cur.currencyCode)}</div>
              <div style={{ marginTop: 3, fontSize: '1.05rem', fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: 'var(--color-text-main)' }}>
                {formatCurrencyAO(cur.amount)}
              </div>
              <div style={{ marginTop: 2, fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>
                {cur.entityCount} pagamento{cur.entityCount === 1 ? '' : 's'} · {cur.requestCount} pedido{cur.requestCount === 1 ? '' : 's'}
              </div>
            </div>
          ))}
        </div>
      )}
      {!history.isAuthoritative && !isEmpty && (
        <div style={{ marginTop: 8, fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>
          Parte dos pagamentos não possui valor confirmado.
        </div>
      )}
    </div>
  );
}

function categoryCountText(c: FinancialCategoryDto): string {
  const unit = entityUnit(c.entityType, c.entityCount);
  const primary = unit ? `${c.entityCount} ${unit}` : `${c.entityCount}`;
  // Don't repeat the count when the grain already IS the request.
  if (c.entityType === 'REQUEST' && c.entityCount === c.requestCount) return primary;
  return `${primary} · ${c.requestCount} pedido${c.requestCount === 1 ? '' : 's'}`;
}

function FinancialCategoryCard({ category }: { category: FinancialCategoryDto }) {
  const isEmpty = category.entityCount === 0;
  return (
    <div style={{
      backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
      borderRadius: 12, padding: '14px 16px', display: 'flex', flexDirection: 'column', gap: 8,
    }}>
      <div>
        <div style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-main)' }}>{category.label}</div>
        <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', marginTop: 2 }}>{categoryCountText(category)}</div>
      </div>

      {isEmpty ? (
        <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>Sem valores</div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          {category.currencies.map((cur) => <CurrencyRow key={cur.currencyCode} row={cur} />)}
          {/* Some entities carry no authoritative amount — counted, never given a fabricated value. */}
          {!category.isAuthoritative && (
            <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', marginTop: 2 }}>
              {category.currencies.length === 0
                ? 'Valor não disponível'
                : 'Parte dos itens não possui valor financeiro autoritativo.'}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function CurrencyRow({ row }: { row: CurrencyAmountDto }) {
  return (
    <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 10 }}>
      <span style={{ fontSize: '0.72rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>{currencyLabel(row.currencyCode)}</span>
      <span style={{ fontSize: '0.95rem', fontWeight: 700, fontVariantNumeric: 'tabular-nums', color: 'var(--color-text-main)' }}>
        {formatCurrencyAO(row.amount)}
      </span>
    </div>
  );
}
