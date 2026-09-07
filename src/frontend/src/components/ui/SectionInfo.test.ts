import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level structural guards (no jsdom/RTL).
import src from './SectionInfo.tsx?raw';

// Reusable analytical section-help affordance. Built on the shared ModernTooltip (one popover
// implementation), accessible, dark-mode safe, structured fields, absent fields omitted.
describe('SectionInfo — structure', () => {
  it('renders an Info icon trigger', () => {
    expect(src).toMatch(/import \{ Info \} from 'lucide-react'/);
    expect(src).toMatch(/<Info /);
  });

  it('reuses the shared ModernTooltip (no duplicated popover implementation)', () => {
    expect(src).toMatch(/import \{ ModernTooltip \}/);
    expect(src).toMatch(/<ModernTooltip/);
    expect(src).toMatch(/openOnClick/);
    expect(src).toMatch(/ariaLabel=\{`Ajuda: \$\{title\}`\}/);
  });

  it('renders the analytical field labels', () => {
    for (const label of ['O que mede', 'Como interpretar', 'O que observar', 'Para que serve', 'Exemplo', 'Observação']) {
      expect(src).toMatch(new RegExp(label));
    }
  });

  it('omits absent fields cleanly (no empty headings)', () => {
    // Each block returns null when its text is falsy.
    expect(src).toMatch(/if \(!text\) return null/);
  });

  it('supports a temporary marker for legacy sections', () => {
    expect(src).toMatch(/temporary &&/);
    expect(src).toMatch(/temporária/);
  });

  it('is not a browser title-only tooltip', () => {
    expect(src).not.toMatch(/title=\{/);
  });

  it('dark-mode safe: only defined tokens', () => {
    expect(src).not.toMatch(/var\(--color-text\)/);
    expect(src).not.toMatch(/--color-bg-elevated/);
    expect(src).toMatch(/--color-text-main|--color-text-muted/);
  });
});
