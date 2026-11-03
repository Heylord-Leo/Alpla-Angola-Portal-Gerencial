import { describe, it, expect } from 'vitest';
import { computeKebabMenuPlacement, KEBAB_MENU_MIN_WIDTH } from './kebabMenuPlacement';

const viewport = { width: 1280, height: 800 };
const trigger = (top: number, left: number, size = 28) => ({ top, left, right: left + size, bottom: top + size, width: size, height: size });

describe('computeKebabMenuPlacement — portal popover never clipped or off-screen', () => {
  it('opens below the trigger, right-aligned to it, when there is room', () => {
    const p = computeKebabMenuPlacement(trigger(100, 1000), { width: 190, height: 44 }, viewport);
    expect(p.placedSide).toBe('bottom');
    expect(p.top).toBeGreaterThan(128);
    // right edge of the menu == right edge of the trigger (align "end")
    expect(p.left + 190).toBe(1028);
  });

  it('flips above the trigger near the bottom of the viewport (last table row)', () => {
    const p = computeKebabMenuPlacement(trigger(780, 1000), { width: 190, height: 44 }, viewport);
    expect(p.placedSide).toBe('top');
    expect(p.top + 44).toBeLessThanOrEqual(780);
    expect(p.top).toBeGreaterThanOrEqual(8);
  });

  it('clamps inside a narrow viewport instead of overflowing the left edge', () => {
    const p = computeKebabMenuPlacement(trigger(100, 10), { width: 190, height: 44 }, { width: 360, height: 640 });
    expect(p.left).toBeGreaterThanOrEqual(8);
    expect(p.left + 190).toBeLessThanOrEqual(360 - 8);
  });

  it('caps the height to the viewport so a long menu scrolls internally', () => {
    const p = computeKebabMenuPlacement(trigger(300, 600), { width: 190, height: 2000 }, viewport);
    expect(p.maxHeight).toBe(800 - 16);
    expect(p.top).toBeGreaterThanOrEqual(8);
  });

  it('never measures narrower than the minimum menu width', () => {
    const p = computeKebabMenuPlacement(trigger(100, 1000), { width: 40, height: 44 }, viewport);
    expect(p.left + KEBAB_MENU_MIN_WIDTH).toBe(1028);
  });
});
