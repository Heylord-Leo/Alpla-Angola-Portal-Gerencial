// v2.245.0 Request Print View — a deliberately tiny wrapper around the browser print dialog so callers
// can be unit-tested by spying on printService.print(). No popup, no document dot write, no external
// dependency, no business logic.
//
// Optional documentTitle temporarily overrides document.title so Chromium/Edge suggests a meaningful
// Save-as-PDF filename, then restores the original title after the dialog closes (afterprint) — with a
// defensive restore if window.print() throws, so a failed print never leaves the app title changed.

export interface PrintOptions {
  documentTitle?: string;
}

export const printService = {
  print(options?: PrintOptions): void {
    if (typeof window === 'undefined' || typeof window.print !== 'function') return;

    const title = options?.documentTitle;
    if (!title) {
      window.print();
      return;
    }

    const originalTitle = document.title;
    let restored = false;
    const restore = () => {
      if (restored) return;
      restored = true;
      document.title = originalTitle;
      window.removeEventListener('afterprint', restore);
    };

    document.title = title;
    // Normal path: restore once the print/preview dialog is dismissed.
    window.addEventListener('afterprint', restore);

    try {
      window.print();
    } catch (e) {
      // Defensive: a throwing print() must not leave the tab title modified.
      restore();
      throw e;
    }
  },
};
