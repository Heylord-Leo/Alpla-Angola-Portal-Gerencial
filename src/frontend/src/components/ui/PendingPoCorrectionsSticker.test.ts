import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level guards for the PO-corrections footer sticker (no jsdom/RTL).
import sticker from './PendingPoCorrectionsSticker.tsx?raw';
import hook from '../../hooks/usePendingPoCorrectionsCount.ts?raw';
import appShell from '../../layouts/AppShell.tsx?raw';

describe('PendingPoCorrectionsSticker — v2.242.0', () => {
  it('reads the count from the dedicated Buyer-corrections hook', () => {
    expect(sticker).toMatch(/usePendingPoCorrectionsCount/);
  });

  it('hides when count is 0 or dismissed, and re-arms when count returns to 0', () => {
    expect(sticker).toMatch(/count > 0 && !dismissed/);
    expect(sticker).toMatch(/pendingPoCorrectionsDismissed/);
    expect(sticker).toMatch(/removeItem\('pendingPoCorrectionsDismissed'\)/);
  });

  it('is mounted in AppShell (visibility is not gated by AppShell either)', () => {
    expect(appShell).toMatch(/import \{ PendingPoCorrectionsSticker \}/);
    expect(appShell).toMatch(/<PendingPoCorrectionsSticker \/>/);
  });

  it('v2.243.0 Phase 3 fix — NO route-based suppression (visible on /requests too)', () => {
    // Regression guard: the sticker must not hide itself based on the current route (it used to hide
    // on /requests, its own CTA destination, which is now the operational home).
    expect(sticker).not.toMatch(/onQueuePage/);
    expect(sticker).not.toMatch(/location\.pathname/);
    expect(sticker).not.toMatch(/useLocation/);
    // Render gate is purely the personal count + dismiss state.
    expect(sticker).toMatch(/\{isVisible && \(/);
    expect(sticker).not.toMatch(/isVisible && !onQueuePage/);
  });

  it('uses singular vs plural copy and the CORREÇÕES title/CTA', () => {
    expect(sticker).toMatch(/Você possui 1 pedido devolvido por Finanças aguardando correção\./);
    expect(sticker).toMatch(/pedidos devolvidos por Finanças aguardando correção\./);
    expect(sticker).toMatch(/Correções de P\.O\. pendentes/);
    expect(sticker).toMatch(/Ver correções/);
  });

  it('CTA lands on the PO-corrections category of Para Minha Ação (v2.243.0 Phase 3, category-only)', () => {
    expect(sticker).toMatch(/\/requests\?action=PO_CORRECTION/);
    // Category-only: no fabricated requestId/poGroupId, and not the legacy attention/buyer-queue links.
    expect(sticker).not.toMatch(/requestId=/);
    expect(sticker).not.toMatch(/\/requests\?isAttention=true/);
    expect(sticker).not.toMatch(/\/buyer\/items\?card=attention/);
  });
});

describe('usePendingPoCorrectionsCount — v2.242.0', () => {
  it('is Buyer-role gated and reads the cross-type personal correction count endpoint', () => {
    expect(hook).toMatch(/roles\?\.includes\('Buyer'\)/);
    // Dedicated cross-type personal count (QUOTATION + PAYMENT), one aggregate — not a list scan.
    expect(hook).toMatch(/api\.requests\.personalPoCorrectionsCount\(\)/);
    expect(hook).not.toMatch(/api\.requests\.list/);
  });

  it('no longer uses the QUOTATION-only Buyer-queue summary source', () => {
    // The prior source missed PAYMENT corrections (e.g. REQ-254); it must be gone.
    expect(hook).not.toMatch(/buyerQueue\.getSummary/);
    expect(hook).not.toMatch(/getSummary\(\{\s*ownership:\s*'me'\s*\}\)/);
  });

  it('polls in the background (2 minutes), mirroring the sibling stickers', () => {
    expect(hook).toMatch(/setInterval/);
    expect(hook).toMatch(/2 \* 60 \* 1000/);
  });
});
