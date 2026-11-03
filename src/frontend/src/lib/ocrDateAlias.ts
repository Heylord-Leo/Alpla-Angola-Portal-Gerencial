/**
 * v2.249.8 — the extraction's date strings → the wizard's date-only value (YYYY-MM-DD). Pure and deterministic; extracted from
 * hooks/useOcrProcessor so the rule is unit-tested (finding 3 of the v2.249.7 TEST validation: the quotation validity date shown
 * by the wizard differed between imports of the same PDF — 30/11/2026 printed, 01/12/2026 once shown).
 *
 * What this function does NOT do, by design: no default, no "today", no timezone conversion (`new Date()` is never involved), no
 * arithmetic on durations. The extraction provider is asked for the SEMANTIC date already encoded as YYYY-MM-DD; the aliases below
 * only normalise the printed forms a provider may still return. An ISO date-time ("2026-11-30T00:00:00", "…Z") keeps its calendar
 * day as written (slicing, never a Date round-trip, which could move the day on a UTC+1 browser). Anything unrecognised is
 * returned trimmed, so an unusable value stays visible instead of silently becoming another date.
 */
export function resolveOcrDateAlias(val: string | null | undefined): string {
    if (!val) return '';
    const trimmed = val.trim();

    // Already YYYY-MM-DD
    if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) return trimmed;

    // ISO date-time: the calendar day as written (no timezone conversion)
    const isoDateTime = trimmed.match(/^(\d{4}-\d{2}-\d{2})[T ]\d{2}:\d{2}/);
    if (isoDateTime) return isoDateTime[1];

    // Common format DD/MM/YYYY or DD-MM-YYYY or DD.MM.YYYY (these documents are day-first)
    const dmyMatch = trimmed.match(/^(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{4})$/);
    if (dmyMatch) {
        const day = dmyMatch[1].padStart(2, '0');
        const month = dmyMatch[2].padStart(2, '0');
        const year = dmyMatch[3];
        return `${year}-${month}-${day}`;
    }

    // YYYY/MM/DD (or with - or .)
    const ymdMatch = trimmed.match(/^(\d{4})[\/\-.](\d{1,2})[\/\-.](\d{1,2})$/);
    if (ymdMatch) {
        const year = ymdMatch[1];
        const month = ymdMatch[2].padStart(2, '0');
        const day = ymdMatch[3].padStart(2, '0');
        return `${year}-${month}-${day}`;
    }

    return trimmed;
}

export type QuotationDateSource = 'EXTRACTED' | 'MANUAL';

/** pt-PT provenance line for the validity date shown in the wizard (document step and final review). */
export function describeQuotationDueDateSource(source: QuotationDateSource | undefined): string {
    switch (source) {
        case 'EXTRACTED': return 'lida do documento pela extração';
        case 'MANUAL': return 'introduzida manualmente';
        default: return 'origem não registada';
    }
}
