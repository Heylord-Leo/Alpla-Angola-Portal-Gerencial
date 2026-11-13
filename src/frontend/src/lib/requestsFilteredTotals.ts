/**
 * Explorer "Total Filtrado" — presentation of the server's per-currency totals (`summary.filteredTotalsByCurrency`,
 * computed over every request matching the active filters, all pages, with the row amount / currency rules).
 *
 * One token per currency, e.g. ["AOA 100.000,00", "RND 28.290,00"]; the page joins them with " · " and lets them wrap at
 * narrow widths. Currencies are never summed together. No totals → a single "0,00" token (no currency can be claimed).
 */
export interface CurrencyTotal { currency: string; amount: number }

export const EMPTY_TOTAL_TOKEN = '0,00';

export function formatFilteredTotalTokens(totals: CurrencyTotal[] | null | undefined): string[] {
    if (!totals || totals.length === 0) return [EMPTY_TOTAL_TOKEN];
    return [...totals]
        .sort((a, b) => a.currency.localeCompare(b.currency))
        .map(t => `${t.currency} ${(t.amount || 0).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`);
}

/** The joined one-line form ("AOA 100.000,00 · RND 28.290,00"). */
export function formatFilteredTotals(totals: CurrencyTotal[] | null | undefined): string {
    return formatFilteredTotalTokens(totals).join(' · ');
}
