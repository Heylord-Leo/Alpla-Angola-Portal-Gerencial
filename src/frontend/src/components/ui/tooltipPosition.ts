// Pure viewport-aware positioning for ModernTooltip. Given the trigger rect, the measured tooltip size,
// the requested side/align, the viewport and a safe margin, it returns an explicit fixed-position
// top/left (no CSS transform) plus width/height constraints and the resolved side. Explicit coordinates
// make the clamp exact and the whole thing unit-testable without a real layout engine.
//
// Rules: flip vertically/horizontally to the side with room; clamp within the viewport so the popover
// never leaves the top/left/right/bottom edges; constrain width to the viewport on narrow screens; cap
// height to the viewport (the caller adds overflowY:auto for internal scrolling).

export type TooltipSide = 'top' | 'bottom' | 'left' | 'right';
export type TooltipAlign = 'start' | 'center' | 'end';

export interface TooltipRect { top: number; left: number; right: number; bottom: number; width: number; height: number; }
export interface TooltipSize { width: number; height: number; }
export interface TooltipViewport { width: number; height: number; }

export interface TooltipPlacement {
  top: number;
  left: number;
  maxWidth: number;
  maxHeight: number;
  placedSide: TooltipSide;
  transformOrigin: string;
}

const GAP = 8; // gap between trigger and popover

function clamp(value: number, min: number, max: number): number {
  if (max < min) return min; // popover larger than the available band → pin to the safe start
  return Math.min(Math.max(value, min), max);
}

export function computeTooltipPosition(
  trigger: TooltipRect,
  tip: TooltipSize,
  side: TooltipSide,
  align: TooltipAlign,
  viewport: TooltipViewport,
  configuredMaxWidth: number,
  margin = 12,
): TooltipPlacement {
  const maxWidth = Math.min(configuredMaxWidth, viewport.width - 2 * margin);
  const maxHeight = viewport.height - 2 * margin;
  const w = Math.min(tip.width, maxWidth);
  const h = Math.min(tip.height, maxHeight);

  const spaceAbove = trigger.top - margin;
  const spaceBelow = viewport.height - trigger.bottom - margin;
  const spaceLeft = trigger.left - margin;
  const spaceRight = viewport.width - trigger.right - margin;

  let placedSide: TooltipSide = side;
  let top = 0;
  let left = 0;

  if (side === 'top' || side === 'bottom') {
    // Vertical flip: keep the requested side if it fits, else use whichever has more room.
    if (side === 'top') placedSide = h + GAP <= spaceAbove ? 'top' : (h + GAP <= spaceBelow ? 'bottom' : (spaceAbove >= spaceBelow ? 'top' : 'bottom'));
    else placedSide = h + GAP <= spaceBelow ? 'bottom' : (h + GAP <= spaceAbove ? 'top' : (spaceBelow >= spaceAbove ? 'bottom' : 'top'));

    top = placedSide === 'top' ? trigger.top - GAP - h : trigger.bottom + GAP;

    // Horizontal alignment relative to the trigger, then clamp into the viewport.
    if (align === 'start') left = trigger.left;
    else if (align === 'end') left = trigger.right - w;
    else left = trigger.left + trigger.width / 2 - w / 2;
    left = clamp(left, margin, viewport.width - w - margin);
    top = clamp(top, margin, viewport.height - h - margin);
  } else {
    // Horizontal flip for left/right.
    if (side === 'left') placedSide = w + GAP <= spaceLeft ? 'left' : (w + GAP <= spaceRight ? 'right' : (spaceLeft >= spaceRight ? 'left' : 'right'));
    else placedSide = w + GAP <= spaceRight ? 'right' : (w + GAP <= spaceLeft ? 'left' : (spaceRight >= spaceLeft ? 'right' : 'left'));

    left = placedSide === 'left' ? trigger.left - GAP - w : trigger.right + GAP;

    if (align === 'start') top = trigger.top;
    else if (align === 'end') top = trigger.bottom - h;
    else top = trigger.top + trigger.height / 2 - h / 2;
    top = clamp(top, margin, viewport.height - h - margin);
    left = clamp(left, margin, viewport.width - w - margin);
  }

  const transformOrigin =
    placedSide === 'top' ? 'bottom' :
    placedSide === 'bottom' ? 'top' :
    placedSide === 'left' ? 'right' : 'left';

  return { top, left, maxWidth, maxHeight, placedSide, transformOrigin };
}
