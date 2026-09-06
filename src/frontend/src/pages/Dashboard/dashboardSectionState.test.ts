import { describe, it, expect } from 'vitest';
import hookSrc from './useSectionData.ts?raw';
import skeletonSrc from './components/DashboardSectionSkeleton.tsx?raw';
import errorSrc from './components/DashboardSectionError.tsx?raw';
import personalSrc from './components/DashboardV2PersonalSection.tsx?raw';
import buyerSrc from './components/DashboardV2BuyerSection.tsx?raw';
import financeSrc from './components/DashboardV2FinanceSection.tsx?raw';
import receivingSrc from './components/DashboardV2ReceivingSection.tsx?raw';
import pipelineSrc from './components/DashboardV2PipelineSection.tsx?raw';
import financialSrc from './components/DashboardV2FinancialSection.tsx?raw';
import dashboardSrc from './Dashboard.tsx?raw';
import apiSrc from '../../lib/api.ts?raw';

// Cross-cutting section states: loading / error / (unauthorized|empty) / ready. One reusable hook +
// skeleton + error, wired into every independently-fetched V2 section.
describe('Dashboard section state primitives', () => {
  it('useSectionData distinguishes loading / error / ready and exposes retry (per-section refetch)', () => {
    expect(hookSrc).toMatch(/'loading' \| 'error' \| 'ready'/);
    expect(hookSrc).toMatch(/setStatus\('loading'\)/);
    expect(hookSrc).toMatch(/setStatus\('ready'\)/);
    expect(hookSrc).toMatch(/setStatus\('error'\)/);
    expect(hookSrc).toMatch(/const retry = useCallback/);
    // Refetch is driven by an explicit nonce, not the changing fetcher identity.
    expect(hookSrc).toMatch(/\[nonce\]/);
  });

  it('useSectionData aborts the in-flight request on cleanup (cancels the StrictMode replay)', () => {
    expect(hookSrc).toMatch(/new AbortController\(\)/);
    expect(hookSrc).toMatch(/fetcherRef\.current\(controller\.signal\)/);
    expect(hookSrc).toMatch(/return \(\) => controller\.abort\(\)/);
    // The fetcher now takes an AbortSignal.
    expect(hookSrc).toMatch(/fetcher: \(signal: AbortSignal\) => Promise<T>/);
  });

  it('an aborted request is cancellation, not an error; a real failure still becomes error', () => {
    // Abort → return early (no setStatus('error')); otherwise set error.
    expect(hookSrc).toMatch(/if \(controller\.signal\.aborted \|\| isAbort\(e\)\) return/);
    expect(hookSrc).toMatch(/setData\(null\); setStatus\('error'\)/);
    // A stale aborted response can never overwrite current data (guarded by the aborted check on success).
    expect(hookSrc).toMatch(/if \(!controller\.signal\.aborted\) \{ setData\(d\); setStatus\('ready'\)/);
  });

  it('apiFetch re-throws AbortError instead of wrapping it as a user-facing failure', () => {
    expect(apiSrc).toMatch(/error\?\.name === 'AbortError' \|\| options\.signal\?\.aborted/);
  });

  it('every V2 section passes the AbortSignal into its API call', () => {
    expect(personalSrc).toMatch(/getPersonal\(signal\)/);
    expect(buyerSrc).toMatch(/getBuyer\(undefined, signal\)/);
    expect(financeSrc).toMatch(/getFinance\(signal\)/);
    expect(receivingSrc).toMatch(/getReceiving\(signal\)/);
    expect(pipelineSrc).toMatch(/getPipeline\(signal\)/);
    expect(financialSrc).toMatch(/getFinancial\(signal\)/);
  });

  it('skeleton is aria-busy, decorative content aria-hidden, reduced-motion respected', () => {
    expect(skeletonSrc).toMatch(/aria-busy="true"/);
    expect(skeletonSrc).toMatch(/aria-hidden="true"/);
    expect(skeletonSrc).toMatch(/prefers-reduced-motion: reduce/);
    expect(skeletonSrc).not.toMatch(/var\(--color-text\)/);
    expect(skeletonSrc).toMatch(/--color-bg-surface|--color-border/);
  });

  it('error is a role=alert with a keyboard-accessible retry button, neutral wording', () => {
    expect(errorSrc).toMatch(/role="alert"/);
    expect(errorSrc).toMatch(/<button/);
    expect(errorSrc).toMatch(/onClick=\{onRetry\}/);
    expect(errorSrc).toMatch(/Tentar novamente/);
    expect(errorSrc).toMatch(/Não foi possível carregar esta seção/);
    // Must NOT imply a permission failure.
    expect(errorSrc).not.toMatch(/permiss|acesso negado|não tem/i);
    expect(errorSrc).not.toMatch(/var\(--color-text\)/);
  });

  it('every V2 section wires the loading skeleton and error+retry', () => {
    for (const src of [personalSrc, buyerSrc, financeSrc, receivingSrc, pipelineSrc, financialSrc]) {
      expect(src).toMatch(/useSectionData/);
      expect(src).toMatch(/status === 'loading'/);
      expect(src).toMatch(/<DashboardSectionSkeleton/);
      expect(src).toMatch(/<DashboardSectionError onRetry=\{retry\}/);
      // No section still returns null for loading (the reported defect).
      expect(src).not.toMatch(/if \(loading \|\| !data\) return null/);
    }
  });

  it('financial keeps unauthorized (null) DISTINCT from loading/error (only null hides the section)', () => {
    // Loading shows a shell; only a RESOLVED null currentExposure hides the section.
    expect(financialSrc).toMatch(/status === 'loading'/);
    expect(financialSrc).toMatch(/data\.currentExposure === null\) return null/);
  });

  it('empty state stays distinct from unauthorized (personal/financial show honest empty text)', () => {
    expect(personalSrc).toMatch(/Nenhuma ação atribuída pessoalmente no momento/);
    expect(financialSrc).toMatch(/Não há exposição financeira no escopo atual/);
  });

  it('no full-page blocking loader in the Dashboard shell', () => {
    // The legacy full-page skeleton guard was for the cockpit fetch; sections load independently now.
    expect(dashboardSrc).not.toMatch(/full-screen|fullscreen|blocking loader/i);
  });
});
