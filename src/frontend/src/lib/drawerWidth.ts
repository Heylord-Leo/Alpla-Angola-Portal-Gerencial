/**
 * How wide the request drawer is, and how that survives.
 *
 * <p>Pure so the clamping can be reasoned about without a browser: the drawer is a real workspace —
 * draft editing, document review, submission, later P.O. actions — and a width that silently
 * exceeds the viewport, or collapses below what a table needs, is worse than no memory at all.</p>
 */

/** Enough to inspect a request without the header actions wrapping into uselessness. */
export const DRAWER_MIN_WIDTH = 460;

/** The width before this feature existed, so nothing moves for anyone who never drags. */
export const DRAWER_DEFAULT_WIDTH = 800;

/** Leave a strip of the list visible; a drawer at 100% is a page, and should be opened as one. */
export const DRAWER_MAX_VIEWPORT_RATIO = 0.95;

export const DRAWER_WIDTH_STORAGE_KEY = 'alpla.requestDrawer.width';

/** Keyboard resize steps. */
export const DRAWER_KEYBOARD_STEP = 24;
export const DRAWER_KEYBOARD_STEP_LARGE = 120;

/**
 * Confines a width to what the current viewport can actually show.
 *
 * <p>The maximum is computed from the viewport every time rather than stored, so a width chosen on
 * a wide monitor does not push the drawer off a laptop screen later.</p>
 */
export function clampDrawerWidth(width: number, viewportWidth: number): number {
    const max = Math.max(DRAWER_MIN_WIDTH, Math.floor(viewportWidth * DRAWER_MAX_VIEWPORT_RATIO));

    // A viewport narrower than the minimum gets the whole viewport: on a small screen the drawer is
    // the screen, and refusing to shrink would push its own actions out of reach.
    if (viewportWidth <= DRAWER_MIN_WIDTH) return viewportWidth;

    return Math.min(max, Math.max(DRAWER_MIN_WIDTH, Math.round(width)));
}

/** The width for a drag, derived from the pointer: the right edge stays pinned to the viewport. */
export function widthFromPointer(clientX: number, viewportWidth: number): number {
    return clampDrawerWidth(viewportWidth - clientX, viewportWidth);
}

export function readStoredDrawerWidth(viewportWidth: number): number {
    try {
        const raw = window.localStorage.getItem(DRAWER_WIDTH_STORAGE_KEY);
        const parsed = raw ? Number(raw) : NaN;
        if (!Number.isFinite(parsed) || parsed <= 0) return clampDrawerWidth(DRAWER_DEFAULT_WIDTH, viewportWidth);
        return clampDrawerWidth(parsed, viewportWidth);
    } catch {
        // Private browsing, disabled storage: a remembered width is a convenience, never a
        // requirement, so failing to read one must not stop the drawer opening.
        return clampDrawerWidth(DRAWER_DEFAULT_WIDTH, viewportWidth);
    }
}

export function storeDrawerWidth(width: number): void {
    try {
        window.localStorage.setItem(DRAWER_WIDTH_STORAGE_KEY, String(Math.round(width)));
    } catch {
        /* not worth reporting: the drawer works, it just will not remember */
    }
}
