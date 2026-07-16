/**
 * Minimum lead time between the selected need level (Grau de Necessidade) and the
 * "Necessário até" date (NeedByDateUtc) on a Quotation request.
 *
 * Mirrors the server-side rule in RequestConstants.NeedLevels (backend is authoritative).
 * Keyed by NeedLevel.Code, since need levels are an editable Master Data lookup:
 * a code that is not listed here carries no minimum.
 */
export const NEED_LEVEL_MIN_LEAD_DAYS: Record<string, number> = {
    CRITICO: 0,  // Imediato — pode ser hoje
    URGENTE: 1,  // 24 horas
    NORMAL: 7,   // 7 dias
    BAIXO: 15,   // 15 dias
};

const pad = (value: number) => String(value).padStart(2, '0');

/** Formats a Date as YYYY-MM-DD in local time (the wire format used by DateInput). */
export function toIsoDate(date: Date): string {
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Formats a YYYY-MM-DD string as DD/MM/YYYY for display. */
export function toDisplayDate(isoDate: string): string {
    const [y, m, d] = isoDate.split('-');
    return y && m && d ? `${d}/${m}/${y}` : isoDate;
}

export function getMinLeadDays(needLevelCode: string | null | undefined): number | null {
    if (!needLevelCode) return null;
    const days = NEED_LEVEL_MIN_LEAD_DAYS[needLevelCode.toUpperCase()];
    return days === undefined ? null : days;
}

/**
 * Earliest date (YYYY-MM-DD) accepted for "Necessário até" under the given need level.
 * Returns null when the level imposes no minimum (unknown/custom code, or no level selected).
 */
export function getMinNeedByDate(needLevelCode: string | null | undefined, today: Date = new Date()): string | null {
    const days = getMinLeadDays(needLevelCode);
    if (days === null) return null;

    const min = new Date(today.getFullYear(), today.getMonth(), today.getDate());
    min.setDate(min.getDate() + days);
    return toIsoDate(min);
}

/** True when `needByDate` (YYYY-MM-DD) falls before the minimum required by the need level. */
export function isBeforeMinNeedByDate(needByDate: string, minNeedByDate: string | null): boolean {
    if (!needByDate || !minNeedByDate) return false;
    return needByDate < minNeedByDate; // ISO dates compare correctly as strings
}

/** Discreet helper shown under the field, e.g. "Prazo mínimo para Normal: 7 dias (21/07/2026)." */
export function getMinNeedByHint(needLevelName: string, minNeedByDate: string, minLeadDays: number): string {
    const prazo = minLeadDays === 0
        ? 'imediato'
        : minLeadDays === 1
            ? '24 horas'
            : `${minLeadDays} dias`;
    return `Prazo mínimo para ${needLevelName}: ${prazo} (a partir de ${toDisplayDate(minNeedByDate)}).`;
}

/** Discreet notice shown when the date was pushed forward after a need-level change. */
export function getNeedByAdjustmentNotice(needLevelName: string, minNeedByDate: string): string {
    return `Data ajustada para ${toDisplayDate(minNeedByDate)} para respeitar o prazo mínimo do grau ${needLevelName}.`;
}

/** Blocking message shown on submit / when the chosen date is too early. */
export function getMinNeedByError(needLevelName: string, minNeedByDate: string): string {
    return `A data “Necessário até” não pode ser anterior ao prazo mínimo do grau ${needLevelName}. Data mínima: ${toDisplayDate(minNeedByDate)}.`;
}
