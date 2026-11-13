import { describe, it, expect } from 'vitest';
import { formatFilteredTotalTokens, formatFilteredTotals, EMPTY_TOTAL_TOKEN } from './requestsFilteredTotals';

// Group separators differ between Node's ICU and browsers; the figures are asserted with them stripped.
const norm = (s: string) => s.replace(/(\d)[\s.  ](?=\d{3})/g, '$1');

describe('explorer Total Filtrado tokens', () => {
    it('one currency → one token', () => {
        const tokens = formatFilteredTotalTokens([{ currency: 'AOA', amount: 100000 }]);
        expect(tokens).toHaveLength(1);
        expect(norm(tokens[0])).toBe('AOA 100000,00');
    });

    it('multiple currencies → one token each, sorted, never summed', () => {
        const text = formatFilteredTotals([{ currency: 'RND', amount: 28290 }, { currency: 'AOA', amount: 100000 }]);
        expect(norm(text)).toBe('AOA 100000,00 · RND 28290,00');
    });

    it('a request with several currencies arrives as separate buckets and stays separate', () => {
        // Server shape for a MULTIPLE approval (AOA 100 + USD 200) plus a plain AOA 50 request.
        const tokens = formatFilteredTotalTokens([{ currency: 'AOA', amount: 150 }, { currency: 'USD', amount: 200 }]);
        expect(tokens.map(norm)).toEqual(['AOA 150,00', 'USD 200,00']);
    });

    it('empty results → a single "0,00" token without a currency', () => {
        expect(formatFilteredTotalTokens([])).toEqual([EMPTY_TOTAL_TOKEN]);
        expect(formatFilteredTotalTokens(null)).toEqual([EMPTY_TOTAL_TOKEN]);
        expect(formatFilteredTotals(undefined)).toBe('0,00');
    });

    it('formats cents and treats a missing amount as zero', () => {
        expect(norm(formatFilteredTotals([{ currency: 'RND', amount: 11274.23 }]))).toBe('RND 11274,23');
        expect(norm(formatFilteredTotals([{ currency: 'AOA', amount: NaN }]))).toBe('AOA 0,00');
    });
});
