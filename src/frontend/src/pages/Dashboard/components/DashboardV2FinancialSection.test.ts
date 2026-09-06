import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './DashboardV2FinancialSection.tsx?raw';

// Dashboard V2 B7.2 — canonical currency-safe Financial Summary. Server-sourced, per-currency, never
// combined, entitlement-gated on the server, no client financial logic.
describe('DashboardV2FinancialSection — structure', () => {
  it('fetches the financial summary from the server (no client recompute)', () => {
    expect(src).toMatch(/api\.dashboardV2\.getFinancial\(/);
  });

  it('renders under [Gerencial] with a SectionInfo affordance', () => {
    expect(src).toMatch(/Resumo Financeiro/);
    expect(src).toMatch(/label: 'Gerencial'/);
    expect(src).toMatch(/<SectionInfo \{\.\.\.DASHBOARD_SECTION_HELP\.financialSummary\}/);
  });

  it('hides the whole section when the server says not entitled (currentExposure === null)', () => {
    expect(src).toMatch(/data\.currentExposure === null\) return null/);
    // No "no permission" placeholder.
    expect(src).not.toMatch(/Sem permissão|sem permiss/i);
  });

  it('renders one card per server category and one row per currency', () => {
    expect(src).toMatch(/data\.currentExposure\.map/);
    expect(src).toMatch(/category\.currencies\.map/);
    // Uses server code/label/counts, not hardcoded categories.
    expect(src).toMatch(/category\.label/);
    expect(src).not.toMatch(/EM_APROVACAO|AGUARDANDO_PO|PAGO_AGUARDANDO/); // no hardcoded category codes in JSX
  });

  it('never combines currencies and always shows the currency code', () => {
    expect(src).toMatch(/currencyLabel\(row\.currencyCode\)/);
    // Formats one currency's amount at a time — no cross-currency arithmetic.
    expect(src).toMatch(/formatCurrencyAO\(row\.amount\)/);
    expect(src).not.toMatch(/Multi-moeda/);
    expect(src).not.toMatch(/reduce\(|\.amount \+/);
  });

  it('labels the UNKNOWN currency bucket "Moeda não identificada"', () => {
    expect(src).toMatch(/UNKNOWN_CURRENCY/);
    expect(src).toMatch(/Moeda não identificada/);
  });

  it('shows category counts via the centralized entity-unit helper', () => {
    expect(src).toMatch(/entityUnit\(/);
    // Avoid "N pedidos · N pedidos" for REQUEST grain.
    expect(src).toMatch(/entityType === 'REQUEST' && c\.entityCount === c\.requestCount/);
  });

  it('handles non-authoritative categories without fabricating a zero amount', () => {
    expect(src).toMatch(/!category\.isAuthoritative/);
    expect(src).toMatch(/Valor não disponível/);
    expect(src).toMatch(/não possui valor financeiro autoritativo/);
    // Empty category shows counts, not a fake currency line.
    expect(src).toMatch(/category\.entityCount === 0/);
  });

  it('performs no client-side financial/urgency/FX logic', () => {
    expect(src).not.toMatch(/PaymentStatus|PaymentType|Vencido|overdue|ScheduledDate|Exchange|Convert|toFixed\(/i);
    expect(src).not.toMatch(/actionClass|NEEDS_|PAID_WAITING/);
  });

  it('renders Paid History below Current Exposure, per currency, secondary (B7.3)', () => {
    expect(src).toMatch(/data\.paidHistory && <PaidHistoryBlock/);
    expect(src).toMatch(/Histórico de Pagamentos/);
    expect(src).toMatch(/history\.periodLabel/);      // static "Últimos 30 dias" chip from server
    expect(src).toMatch(/Pagamentos confirmados no período/);
    expect(src).toMatch(/history\.currencies\.map/);  // one row per currency
    expect(src).toMatch(/formatCurrencyAO\(cur\.amount\)/);
    expect(src).toMatch(/currencyLabel\(cur\.currencyCode\)/); // UNKNOWN → "Moeda não identificada"
    expect(src).toMatch(/pagamento.*pedido/);          // payment + request counts
  });

  it('Paid History has an empty state and never combines currencies or nets refunds', () => {
    expect(src).toMatch(/history\.paymentCount === 0/);
    expect(src).toMatch(/Nenhum pagamento confirmado nos últimos 30 dias/);
    expect(src).not.toMatch(/Multi-moeda|Total geral|líquid|net paid/i);
    expect(src).not.toMatch(/reduce\(/); // no client aggregation across currencies/categories
  });

  it('dark-mode safe: only defined tokens', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
    expect(src).not.toMatch(/--color-bg-elevated/);
    expect(src).toMatch(/--color-text-main|--color-text-muted|--color-bg-surface/);
  });

  it('uses the shared section-state hook + skeleton + error primitives (no per-section fetch logic)', () => {
    expect(src).toMatch(/useSectionData/);
    expect(src).toMatch(/status === 'loading'/);
    expect(src).toMatch(/<DashboardSectionSkeleton/);
    expect(src).toMatch(/<DashboardSectionError onRetry=\{retry\}/);
    // Error is a distinct state now, never a silent null.
    expect(src).not.toMatch(/if \(loading \|\| !data\) return null/);
  });
});
