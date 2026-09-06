import { describe, it, expect } from 'vitest';
// Node-only vitest — source-level guards that the viewport-aware wiring is in place and the existing
// interactions are preserved. The positioning math itself is covered by tooltipPosition.test.ts.
import src from './ModernTooltip.tsx?raw';

describe('ModernTooltip — viewport-aware placement wiring', () => {
  it('uses the pure computeTooltipPosition helper (no inline ad-hoc placement)', () => {
    expect(src).toMatch(/import \{ computeTooltipPosition \} from '\.\/tooltipPosition'/);
    expect(src).toMatch(/computeTooltipPosition\(/);
  });

  it('measures the popover before placing it (ref + useLayoutEffect + measuring style)', () => {
    expect(src).toMatch(/useLayoutEffect/);
    expect(src).toMatch(/tooltipRef/);
    expect(src).toMatch(/offsetWidth/);
    expect(src).toMatch(/offsetHeight/);
    expect(src).toMatch(/positioned \? tooltipStyles : measuringStyles/);
  });

  it('reads the live viewport dimensions', () => {
    expect(src).toMatch(/window\.innerWidth/);
    expect(src).toMatch(/window\.innerHeight/);
  });

  it('applies a max-height + internal scroll for tall content', () => {
    expect(src).toMatch(/maxHeight/);
    expect(src).toMatch(/overflowY: 'auto'/);
  });

  it('renders through the body portal (no ancestor clipping)', () => {
    expect(src).toMatch(/<DropdownPortal>/);
    expect(src).toMatch(/position: 'fixed'/);
  });

  it('preserves hover, click/tap, keyboard, ESC, outside-blur and aria', () => {
    expect(src).toMatch(/onMouseEnter/);
    expect(src).toMatch(/onMouseLeave/);
    expect(src).toMatch(/onFocus/);
    expect(src).toMatch(/onBlur/);
    expect(src).toMatch(/e\.key === 'Enter' \|\| e\.key === ' '/);
    expect(src).toMatch(/e\.key === 'Escape'/);
    expect(src).toMatch(/aria-label=\{ariaLabel\}/);
    expect(src).toMatch(/aria-expanded/);
  });

  it('dark-mode safe: defined tokens only', () => {
    expect(src).not.toMatch(/var\(--color-text\)[^-]/);
    expect(src).toMatch(/--color-bg-surface|--color-text-main|--color-border/);
  });
});
