/**
 * Locale-independent monetary parsing and presentation (v2.229.8).
 *
 * The Portal owns monetary semantics: what "." and "," mean NEVER depends on the Windows
 * language, the browser locale or the keyboard layout. Confirmed live on REQ-18/08/2026-233:
 * `<input type="number">` on an English-locale Windows refused "," and silently mangled
 * values, so every editable currency field goes through these helpers instead.
 *
 * ── Normalization policy (documented, deliberately simple — no clever guessing) ──
 *  1. Currency letters/symbols and regular/NBSP/narrow spaces are stripped; spaces are
 *     ALWAYS thousands grouping, never a decimal separator.
 *  2. Both "." and "," present → the LAST separator that occurs is the decimal separator,
 *     every other one is grouping:  "120,000.00" → 120000.00 · "120.000,00" → 120000.00
 *  3. One separator type occurring MULTIPLE times → all grouping: "1.234.567" → 1234567.00
 *  4. One single separator:
 *     · followed by 1–2 digits → decimal:            "120000.5" → 120000.50
 *     · followed by exactly 3 digits AND preceded by 1–3 digits not starting with "0" →
 *       grouping (the shape of a grouped thousand — a grouped number never starts "0."):
 *       "120.000" → 120000.00 · "1,234" → 1234.00 · but "0.999" → 1.00 (decimal, rounded)
 *     · anything else → decimal, rounded to 2 places (half-up on the digit string, never
 *       float math): "1234.567" → 1234.57
 *  5. Any remaining non-numeric character → invalid (null). No browser alert, ever.
 *
 * Canonical value: a plain decimal string with "." and exactly 2 decimals ("123456.78") —
 * `Number(canonical)` is safe wherever the existing API contracts send numbers.
 * Display value: the Portal's existing pt-AO convention ("123 456,78"), identical to
 * `formatCurrencyAO` in `lib/utils.ts`.
 */

/** Live keystroke filter: digits plus at most ONE decimal separator ("." or ",") with at
 *  most 2 digits after it. Grouping characters are never typed — they come from formatting
 *  or paste (paste goes through {@link parseMoneyInput} instead). */
export function sanitizeMoneyTyping(text: string, allowNegative = false): string {
    let out = '';
    let sepSeen = false;
    let decimals = 0;
    for (let i = 0; i < text.length; i++) {
        const ch = text[i];
        if (ch >= '0' && ch <= '9') {
            if (sepSeen) {
                if (decimals >= 2) continue; // currency = 2 decimal places, extra digits ignored
                decimals++;
            }
            out += ch;
        } else if ((ch === '.' || ch === ',') && !sepSeen) {
            sepSeen = true;
            out += ch;
        } else if (allowNegative && ch === '-' && out === '') {
            out += ch;
        }
        // every other character is ignored cleanly (§8: no alert, no jump)
    }
    return out;
}

/** True when the text needs full paste-style normalization rather than the typing filter
 *  (spaces, repeated separators, or both separator kinds at once). */
export function needsPasteNormalization(text: string): boolean {
    const seps = (text.match(/[.,]/g) ?? []).length;
    return /[\s  ]/.test(text) || seps > 1;
}

/**
 * Parses ANY user-provided monetary text (typed or pasted) into the canonical decimal
 * string ("123456.78", always 2 decimals) following the normalization policy above.
 * Returns null for blank input and for text that is not a number.
 */
export function parseMoneyInput(raw: string | number | null | undefined): string | null {
    if (raw === null || raw === undefined) return null;
    if (typeof raw === 'number') {
        if (!isFinite(raw)) return null;
        return raw.toFixed(2);
    }

    // Rule 1: strip grouping spaces and anything that is not digit/separator/sign.
    let text = raw.replace(/[\s  ]/g, '');
    const negative = text.trim().startsWith('-');
    text = text.replace(/[^0-9.,]/g, '');
    if (text === '') return null;

    const dots = (text.match(/\./g) ?? []).length;
    const commas = (text.match(/,/g) ?? []).length;

    let decimalSep: string | null = null;
    if (dots > 0 && commas > 0) {
        // Rule 2: the LAST occurring separator is the decimal one.
        decimalSep = text.lastIndexOf('.') > text.lastIndexOf(',') ? '.' : ',';
    } else if (dots + commas === 1) {
        const sep = dots === 1 ? '.' : ',';
        const idx = text.indexOf(sep);
        const before = idx;
        const after = text.length - idx - 1;
        // Rule 4: single separator — grouped-thousand shape reads as grouping, but a
        // grouped number never starts with "0" ("0.999" is a decimal, not 999).
        decimalSep = (after === 3 && before >= 1 && before <= 3 && text[0] !== '0')
            ? null
            : sep;
    }
    // Rule 3 (dots>1 XOR commas>1 of a single kind): decimalSep stays null → all grouping.

    let intPart: string;
    let fracPart: string;
    if (decimalSep) {
        const idx = text.lastIndexOf(decimalSep);
        intPart = text.slice(0, idx).replace(/[.,]/g, '');
        fracPart = text.slice(idx + 1).replace(/[.,]/g, '');
    } else {
        intPart = text.replace(/[.,]/g, '');
        fracPart = '';
    }

    if (!/^\d*$/.test(intPart) || !/^\d*$/.test(fracPart)) return null;
    if (intPart === '' && fracPart === '') return null;

    intPart = intPart.replace(/^0+(?=\d)/, '');
    if (intPart === '') intPart = '0';

    // 2 decimal places by string arithmetic — never parseFloat on the raw user string.
    if (fracPart.length < 2) {
        fracPart = fracPart.padEnd(2, '0');
    } else if (fracPart.length > 2) {
        // Rule 4 tail: round half-up on the digit string.
        const keep = fracPart.slice(0, 2);
        const next = fracPart.charCodeAt(2) - 48;
        if (next >= 5) {
            let carried = (BigInt(intPart + keep) + 1n).toString().padStart(3, '0');
            intPart = carried.slice(0, -2).replace(/^0+(?=\d)/, '') || '0';
            fracPart = carried.slice(-2);
        } else {
            fracPart = keep;
        }
    }

    const canonical = `${intPart}.${fracPart}`;
    if (canonical === '0.00') return negative ? '0.00' : canonical;
    return negative ? `-${canonical}` : canonical;
}

/** Portal display convention — identical to `formatCurrencyAO`: pt-AO grouping (space) and
 *  comma decimals, always 2 places: "123 456,78". Blank/invalid → "". */
export function formatMoneyInput(value: string | number | null | undefined): string {
    const canonical = parseMoneyInput(value ?? null);
    if (canonical === null) return '';
    return new Intl.NumberFormat('pt-AO', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    }).format(Number(canonical));
}

/** Plain editable form shown while the field is focused: no grouping, comma decimal
 *  ("123456,78"), trailing ",00" dropped for whole numbers so typing continues naturally. */
export function moneyEditingValue(value: string | number | null | undefined): string {
    const canonical = parseMoneyInput(value ?? null);
    if (canonical === null) return '';
    const [i, f] = canonical.replace('-', '').split('.');
    const sign = canonical.startsWith('-') ? '-' : '';
    return f === '00' ? `${sign}${i}` : `${sign}${i},${f}`;
}
