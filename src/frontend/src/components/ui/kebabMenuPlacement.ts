// Viewport-aware placement for the shared KebabMenu popover (v2.249.1). The menu is rendered through
// DropdownPortal (document.body) with position:fixed, so no ancestor overflow/transform can clip it; this
// helper decides WHERE: below the trigger and right-aligned to it by default, flipped above when the space
// below is insufficient, and clamped inside the viewport (narrow screens, browser zoom, last table rows).
// Delegates to the tested tooltip engine so both popovers share one collision rule. Pure + node-vitest.
import { computeTooltipPosition, type TooltipRect, type TooltipSize, type TooltipViewport } from './tooltipPosition';

export interface KebabMenuPlacement {
  top: number;
  left: number;
  /** Height cap so a long menu scrolls internally instead of leaving the viewport. */
  maxHeight: number;
  placedSide: 'top' | 'bottom';
}

export const KEBAB_MENU_MIN_WIDTH = 160;

export function computeKebabMenuPlacement(
  trigger: TooltipRect,
  menu: TooltipSize,
  viewport: TooltipViewport,
  margin = 8,
): KebabMenuPlacement {
  const width = Math.max(menu.width, KEBAB_MENU_MIN_WIDTH);
  const p = computeTooltipPosition(trigger, { width, height: menu.height }, 'bottom', 'end', viewport, width, margin);
  return {
    top: p.top,
    left: p.left,
    maxHeight: p.maxHeight,
    placedSide: p.placedSide === 'top' ? 'top' : 'bottom',
  };
}
