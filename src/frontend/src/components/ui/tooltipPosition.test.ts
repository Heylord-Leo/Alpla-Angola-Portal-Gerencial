import { describe, it, expect } from 'vitest';
import { computeTooltipPosition, type TooltipRect } from './tooltipPosition';

const VP = { width: 1000, height: 800 };
const MARGIN = 12;

const rect = (over: Partial<TooltipRect>): TooltipRect => ({
  top: 0, left: 0, right: 0, bottom: 0, width: 0, height: 0, ...over,
});

// A trigger of size 16x16 at (x,y).
const trigger = (x: number, y: number) => rect({ left: x, top: y, right: x + 16, bottom: y + 16, width: 16, height: 16 });

describe('computeTooltipPosition', () => {
  it('flips to below when there is no room above (side=top near the top edge)', () => {
    // Trigger at y=10; a 200-tall popover cannot fit above → must open below.
    const p = computeTooltipPosition(trigger(500, 10), { width: 300, height: 200 }, 'top', 'start', VP, 320, MARGIN);
    expect(p.placedSide).toBe('bottom');
    expect(p.top).toBeGreaterThanOrEqual(trigger(500, 10).bottom); // below the trigger
    expect(p.transformOrigin).toBe('top');
  });

  it('flips to above when there is no room below (side=bottom near the bottom edge)', () => {
    const p = computeTooltipPosition(trigger(500, 770), { width: 300, height: 200 }, 'bottom', 'start', VP, 320, MARGIN);
    expect(p.placedSide).toBe('top');
    expect(p.top + 200).toBeLessThanOrEqual(trigger(500, 770).top); // wholly above the trigger
  });

  it('keeps the requested side when it fits', () => {
    const p = computeTooltipPosition(trigger(500, 400), { width: 300, height: 120 }, 'top', 'start', VP, 320, MARGIN);
    expect(p.placedSide).toBe('top');
  });

  it('clamps horizontally at the right edge (align=start, wide popover)', () => {
    // Trigger near the right; a 300-wide popover left-anchored would overflow → clamp.
    const p = computeTooltipPosition(trigger(950, 400), { width: 300, height: 120 }, 'top', 'start', VP, 320, MARGIN);
    expect(p.left + 300).toBeLessThanOrEqual(VP.width - MARGIN);
    expect(p.left).toBeGreaterThanOrEqual(MARGIN);
  });

  it('clamps horizontally at the left edge (align=end)', () => {
    const p = computeTooltipPosition(trigger(4, 400), { width: 300, height: 120 }, 'top', 'end', VP, 320, MARGIN);
    expect(p.left).toBeGreaterThanOrEqual(MARGIN);
  });

  it('constrains width to the viewport on narrow screens', () => {
    const narrow = { width: 280, height: 800 };
    const p = computeTooltipPosition(trigger(140, 400), { width: 340, height: 120 }, 'top', 'start', narrow, 340, MARGIN);
    expect(p.maxWidth).toBe(narrow.width - 2 * MARGIN); // 256
  });

  it('caps height to the viewport for tall content (caller adds overflowY:auto)', () => {
    const p = computeTooltipPosition(trigger(500, 400), { width: 300, height: 5000 }, 'top', 'start', VP, 320, MARGIN);
    expect(p.maxHeight).toBe(VP.height - 2 * MARGIN);
  });

  it('never crosses any viewport edge, across a grid of trigger positions', () => {
    const tip = { width: 300, height: 220 };
    for (const x of [0, 250, 500, 750, 990]) {
      for (const y of [0, 200, 400, 600, 790]) {
        const p = computeTooltipPosition(trigger(x, y), tip, 'top', 'start', VP, 320, MARGIN);
        const w = Math.min(tip.width, p.maxWidth);
        const h = Math.min(tip.height, p.maxHeight);
        expect(p.left).toBeGreaterThanOrEqual(MARGIN);
        expect(p.top).toBeGreaterThanOrEqual(MARGIN);
        expect(p.left + w).toBeLessThanOrEqual(VP.width - MARGIN + 0.001);
        expect(p.top + h).toBeLessThanOrEqual(VP.height - MARGIN + 0.001);
      }
    }
  });

  it('flips left/right sides horizontally when out of room', () => {
    const p = computeTooltipPosition(trigger(10, 400), { width: 300, height: 120 }, 'left', 'center', VP, 320, MARGIN);
    expect(p.placedSide).toBe('right'); // no room on the left → open to the right
  });
});
